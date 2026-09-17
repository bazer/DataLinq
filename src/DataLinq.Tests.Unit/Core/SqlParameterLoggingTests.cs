using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Logging;
using DataLinq.Mutation;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DataLinq.Tests.Unit.Core;

public sealed class SqlParameterLoggingTests
{
    [Test]
    public async Task DefaultFormattingHidesAllNonNullValuesRegardlessOfNameOrType()
    {
        using var command = new SqliteCommand("SELECT @value, @data, @number, @empty");
        command.Parameters.AddWithValue("@value", "synthetic-password-123");
        command.Parameters.AddWithValue("@data", new byte[] { 0xAA, 0xBB, 0xCC });
        command.Parameters.AddWithValue("@number", 912345678);
        command.Parameters.AddWithValue("@empty", DBNull.Value);

        var formatted = command.FormatCommand();
        await Assert.That(formatted.Contains("synthetic-password-123", StringComparison.Ordinal)).IsFalse();
        await Assert.That(formatted.Contains("AABBCC", StringComparison.Ordinal)).IsFalse();
        await Assert.That(formatted.Contains("912345678", StringComparison.Ordinal)).IsFalse();
        await Assert.That(formatted.Contains("@value = <redacted>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(formatted.Contains("Length: 3", StringComparison.Ordinal)).IsTrue();
        await Assert.That(formatted.Contains("@empty = NULL", StringComparison.Ordinal)).IsTrue();
        await Assert.That(formatted.Contains(command.CommandText, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task OptInStillBoundsStringsAndBinaryAndHonorsRedaction()
    {
        using var command = new SqliteCommand("SELECT @value");
        command.Parameters.AddWithValue("@value", "abcdefghijklmnop");
        command.Parameters.AddWithValue("@binary", new byte[] { 0xAB, 0xCD, 0xEF, 0x12, 0x34 });
        command.Parameters.AddWithValue("@keepHidden", "never-print-this");
        var options = new SqlParameterLoggingOptions
        {
            IncludeSensitiveValues = true,
            MaximumValueLength = 6,
            RedactParameter = parameter => parameter.ParameterName == "@keepHidden"
        };

        var formatted = command.FormatCommand(options);
        await Assert.That(formatted.Contains("\"abcdef…\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(formatted.Contains("0xABCDEF…", StringComparison.Ordinal)).IsTrue();
        await Assert.That(formatted.Contains("ghijkl", StringComparison.Ordinal)).IsFalse();
        await Assert.That(formatted.Contains("never-print-this", StringComparison.Ordinal)).IsFalse();
        await Assert.That(formatted.Contains("@keepHidden = <redacted>", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task OptInEscapesLineBreaksAndDoesNotInvokeArbitraryToString()
    {
        using var command = new SqliteCommand();
        command.Parameters.AddWithValue("@text", "a\r\nb\t\0\u2028\\\"");
        command.Parameters.Add(new SqliteParameter("@custom", DbType.Object) { Value = new UnformattableValue() });
        var formatted = command.FormatCommand(new SqlParameterLoggingOptions { IncludeSensitiveValues = true });
        await Assert.That(formatted.Contains("a\\r\\nb\\t\\u0000\\u2028\\\\\\\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(formatted.Contains("<unsupported>", StringComparison.Ordinal)).IsTrue();

        var zeroLength = command.FormatCommand(new SqlParameterLoggingOptions { IncludeSensitiveValues = true, MaximumValueLength = 0 });
        await Assert.That(zeroLength.Contains("@text = \"…\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(() => new SqlParameterLoggingOptions { MaximumValueLength = -1 }).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task LargeParameterFormattingAllocatesOnlyBoundedOutput()
    {
        using var command = new SqliteCommand("SELECT @bytes, @text");
        command.Parameters.AddWithValue("@bytes", new byte[10 * 1024 * 1024]);
        command.Parameters.AddWithValue("@text", new string('s', 10 * 1024 * 1024));
        foreach (var includeValues in new[] { false, true })
        {
            var options = new SqlParameterLoggingOptions { IncludeSensitiveValues = includeValues, MaximumValueLength = 64 };
            _ = command.FormatCommand(options); // Exclude first-use setup from the allocation check.
            var before = GC.GetAllocatedBytesForCurrentThread();
            var formatted = command.FormatCommand(options);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            await Assert.That(formatted.Length < 1024).IsTrue();
            await Assert.That(allocated < 16_384).IsTrue();
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DatabaseAndTransactionExecutionUseTheConfiguredPolicy(bool includeValues)
    {
        const string secret = "synthetic-sensitive-value";
        using var loggerFactory = new RecordingLoggerFactory();
        var configuration = new DataLinqLoggingConfiguration(loggerFactory)
        {
            SqlParameters = new SqlParameterLoggingOptions { IncludeSensitiveValues = includeValues }
        };
        var access = new SQLiteDbAccess("Data Source=:memory:", configuration);
        using var command = new SqliteCommand("SELECT @value");
        command.Parameters.AddWithValue("@value", secret);
        await Assert.That(access.ExecuteScalar(command)).IsEqualTo(secret);
        access.ExecuteNonQuery(command);
        using (var reader = access.ExecuteReader(command))
            await Assert.That(reader.ReadNextRow()).IsTrue();

        using var transaction = new SQLiteDatabaseTransaction("Data Source=:memory:", TransactionType.ReadAndWrite, configuration);
        await Assert.That(transaction.ExecuteScalar(command)).IsEqualTo(secret);
        transaction.ExecuteNonQuery(command);
        using (var reader = transaction.ExecuteReader(command))
            await Assert.That(reader.ReadNextRow()).IsTrue();

        var commands = loggerFactory.Messages.Where(message => message.Contains("Parameters:", StringComparison.Ordinal)).ToArray();
        await Assert.That(commands.Length).IsEqualTo(6);
        await Assert.That(commands.All(message => message.Contains(secret, StringComparison.Ordinal) == includeValues)).IsTrue();

        loggerFactory.Messages.Clear();
        Log.SqlCommand(configuration.SqlCommandLogger, command);
        await Assert.That(loggerFactory.Messages.Single().Contains(secret, StringComparison.Ordinal)).IsFalse();
        loggerFactory.Enabled = false;
        Log.SqlCommand(configuration, command);
        await Assert.That(loggerFactory.Messages.Count).IsEqualTo(1);
    }

    private sealed class UnformattableValue
    {
        public override string ToString() => throw new InvalidOperationException("Custom values must not be formatted.");
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory, ILogger
    {
        public List<string> Messages { get; } = [];
        public bool Enabled { get; set; } = true;
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => Enabled;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
