using System;
using System.Data;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Metadata;

namespace DataLinq.Tests.Unit.Core;

public class DatabaseAccessReaderLifetimeTests
{
    [Test]
    [Arguments("complete")]
    [Arguments("early")]
    [Arguments("read-failure")]
    [Arguments("creation-failure")]
    public async Task OwnedReaderDisposesItsCommandAcrossEnumerationOutcomes(string outcome)
    {
        var order = new List<string>();
        var reader = new TrackingDataReader(2, outcome == "read-failure" ? 2 : null)
        {
            OnDispose = () => order.Add("reader")
        };
        var command = new TrackingCommand { OnDispose = () => order.Add("command") };
        var access = new TrackingDatabaseAccess(reader, command) { FailCreation = outcome == "creation-failure" };
        Exception? failure = null;
        try
        {
            _ = access.ReadReader("SELECT 1").Take(outcome == "early" ? 1 : 10).ToArray();
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }
        await Assert.That(failure is not null).IsEqualTo(outcome.EndsWith("failure", StringComparison.Ordinal));
        await Assert.That(command.DisposeCalls).IsEqualTo(1);
        await Assert.That(order.SequenceEqual(outcome == "creation-failure" ? ["command"] : new[] { "reader", "command" })).IsTrue();
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task OwnedReaderDisposalAttemptsBothResourcesExactlyOnce(bool failReader, bool failCommand)
    {
        var readerFailure = new InvalidOperationException("reader disposal");
        var commandFailure = new InvalidOperationException("command disposal");
        var reader = new TrackingDataReader(1) { DisposeFailure = failReader ? readerFailure : null };
        var command = new TrackingCommand { DisposeFailure = failCommand ? commandFailure : null };
        var access = new TrackingDatabaseAccess(reader, command);
        var result = access.ExecuteReader("SELECT 1");
        await Assert.That(command.DisposeCalls).IsEqualTo(0);
        Exception? observed = null;
        try { result.Dispose(); }
        catch (Exception exception) { observed = exception; }
        result.Dispose();
        await Assert.That(command.DisposeCalls).IsEqualTo(1);
        await Assert.That(reader.DisposeCalls).IsEqualTo(1);
        if (failReader && failCommand)
            await Assert.That(((AggregateException)observed!).InnerExceptions.SequenceEqual(new[] { readerFailure, commandFailure })).IsTrue();
        else
            await Assert.That(ReferenceEquals(observed, failReader ? readerFailure : failCommand ? commandFailure : null)).IsTrue();
    }

    [Test]
    public async Task CreationAndCleanupFailuresAreBothRetained()
    {
        var commandFailure = new InvalidOperationException("command disposal");
        var command = new TrackingCommand { DisposeFailure = commandFailure };
        var access = new TrackingDatabaseAccess(new TrackingDataReader(0), command) { FailCreation = true };
        AggregateException? observed = null;
        try { _ = access.ExecuteReader("SELECT 1"); }
        catch (AggregateException exception) { observed = exception; }
        await Assert.That(observed!.InnerExceptions[0].Message).IsEqualTo("reader creation");
        await Assert.That(observed.InnerExceptions[1]).IsSameReferenceAs(commandFailure);
        await Assert.That(command.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task CommandOverloadKeepsCallerOwnershipAndOptionalBinarySpiIsPreserved()
    {
        var callerCommand = new TrackingCommand();
        var borrowedReader = new TrackingDataReader(1);
        var access = new TrackingDatabaseAccess(borrowedReader);
        access.ExecuteReader(callerCommand).Dispose();
        await Assert.That(callerCommand.DisposeCalls).IsEqualTo(0);
        var binaryReader = new OwnedBinaryReader();
        var owned = new TrackingDatabaseAccess(binaryReader, new TrackingCommand()).ExecuteReader("SELECT blob");
        using (owned)
        {
            await Assert.That(owned is IDataLinqOwnedBinaryBufferReader).IsTrue();
            await Assert.That(((IDataLinqOwnedBinaryBufferReader)owned).TakeOwnedBytes(0)).IsSameReferenceAs(binaryReader.Buffer);
        }
        using var ordinary = new TrackingDatabaseAccess(new TrackingDataReader(1), new TrackingCommand()).ExecuteReader("SELECT 1");
        await Assert.That(ordinary is IDataLinqOwnedBinaryBufferReader).IsFalse();
    }

    [Test]
    public async Task ReadReader_DisposesReaderAfterCompleteEnumeration()
    {
        var reader = new TrackingDataReader(rowCount: 2);
        var access = new TrackingDatabaseAccess(reader);

        var rows = access.ReadReader("SELECT 1").ToArray();

        await Assert.That(rows.Length).IsEqualTo(2);
        await Assert.That(reader.IsDisposed).IsTrue();
    }

    [Test]
    public async Task ReadReader_DisposesReaderWhenEnumerationStopsEarly()
    {
        var reader = new TrackingDataReader(rowCount: 2);
        var access = new TrackingDatabaseAccess(reader);
        var enumerator = access.ReadReader("SELECT 1").GetEnumerator();

        try
        {
            await Assert.That(enumerator.MoveNext()).IsTrue();
            await Assert.That(reader.IsDisposed).IsFalse();
        }
        finally
        {
            enumerator.Dispose();
        }

        await Assert.That(reader.IsDisposed).IsTrue();
    }

    [Test]
    public async Task ReadReader_DisposesReaderWhenReadingThrows()
    {
        var reader = new TrackingDataReader(rowCount: 2, throwOnReadCall: 2);
        var access = new TrackingDatabaseAccess(reader);
        InvalidOperationException? exception = null;

        try
        {
            _ = access.ReadReader("SELECT 1").ToArray();
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).IsEqualTo("Synthetic reader failure.");
        await Assert.That(reader.IsDisposed).IsTrue();
    }

    private sealed class TrackingDatabaseAccess(TrackingDataReader reader, TrackingCommand? ownedCommand = null) : DatabaseAccess
    {
        internal bool FailCreation { get; init; }
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) =>
            FailCreation ? throw new InvalidOperationException("reader creation") : reader;

        public override IDataLinqDataReader ExecuteReader(string query) => ownedCommand is null ? reader : ExecuteOwnedReader(ownedCommand);

        public override object? ExecuteScalar(IDbCommand command) => throw new NotSupportedException();

        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();

        public override object? ExecuteScalar(string query) => throw new NotSupportedException();

        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();

        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();

        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();
    }

    private class TrackingDataReader(int rowCount, int? throwOnReadCall = null) : IDataLinqDataReader
    {
        private int remainingRows = rowCount;
        private int readCalls;

        public bool IsDisposed { get; private set; }
        internal int DisposeCalls { get; private set; }
        internal Action? OnDispose { get; init; }
        internal Exception? DisposeFailure { get; init; }

        public void Dispose()
        {
            DisposeCalls++;
            IsDisposed = true;
            OnDispose?.Invoke();
            if (DisposeFailure is not null)
                throw DisposeFailure;
        }

        public bool ReadNextRow()
        {
            readCalls++;
            if (readCalls == throwOnReadCall)
                throw new InvalidOperationException("Synthetic reader failure.");

            if (remainingRows == 0)
                return false;

            remainingRows--;
            return true;
        }

        public object GetValue(int ordinal) => throw new NotSupportedException();

        public int GetOrdinal(string name) => throw new NotSupportedException();

        public string GetString(int ordinal) => throw new NotSupportedException();

        public bool GetBoolean(int ordinal) => throw new NotSupportedException();

        public int GetInt32(int ordinal) => throw new NotSupportedException();

        public DateOnly GetDateOnly(int ordinal) => throw new NotSupportedException();

        public Guid GetGuid(int ordinal) => throw new NotSupportedException();

        public byte[]? GetBytes(int ordinal) => throw new NotSupportedException();

        public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();

        public T? GetValue<T>(ColumnDefinition column) => throw new NotSupportedException();

        public T? GetValue<T>(ColumnDefinition column, int ordinal) => throw new NotSupportedException();

        public bool IsDbNull(int ordinal) => throw new NotSupportedException();
    }

    private sealed class OwnedBinaryReader() : TrackingDataReader(1), IDataLinqOwnedBinaryBufferReader
    {
        internal readonly byte[] Buffer = [1, 2, 3];
        public byte[]? TakeOwnedBytes(int ordinal) => Buffer;
    }

    private sealed class TrackingCommand : IDbCommand
    {
        internal int DisposeCalls { get; private set; }
        internal Action? OnDispose { get; init; }
        internal Exception? DisposeFailure { get; init; }
        [AllowNull] public string CommandText { get; set; } = string.Empty;
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; }
        public IDbConnection? Connection { get; set; }
        public IDataParameterCollection Parameters => throw new NotSupportedException();
        public IDbTransaction? Transaction { get; set; }
        public UpdateRowSource UpdatedRowSource { get; set; }
        public void Cancel() => throw new NotSupportedException();
        public IDbDataParameter CreateParameter() => throw new NotSupportedException();
        public int ExecuteNonQuery() => throw new NotSupportedException();
        public IDataReader ExecuteReader() => throw new NotSupportedException();
        public IDataReader ExecuteReader(CommandBehavior behavior) => throw new NotSupportedException();
        public object? ExecuteScalar() => throw new NotSupportedException();
        public void Prepare() => throw new NotSupportedException();
        public void Dispose()
        {
            DisposeCalls++;
            OnDispose?.Invoke();
            if (DisposeFailure is not null)
                throw DisposeFailure;
        }
    }
}
