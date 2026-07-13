using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class DataSourceAccessSourceRowLoaderTests
{
    [Test]
    public async Task SelectReadReader_EarlyDisposalOwnsCommandAndReaderLifetime()
    {
        var table = CreateTable(new RecordingIdConverter());
        var reader = new TrackingReader(
        [
            [42, "Ada"],
            [43, "Grace"]
        ]);
        var command = new TrackingCommand();
        var databaseAccess = new TrackingDatabaseAccess(reader);
        var provider = new TrackingProvider(
            table.Database,
            databaseAccess,
            new RecordingWriter(),
            command);
        var source = new TrackingDataSourceAccess(provider, databaseAccess);
        var rows = new SqlQuery(table, source).SelectQuery().ReadReader().GetEnumerator();

        try
        {
            await Assert.That(rows.MoveNext()).IsTrue();
            await Assert.That(command.Disposed).IsFalse();
            await Assert.That(reader.Disposed).IsFalse();
        }
        finally
        {
            rows.Dispose();
        }

        await Assert.That(databaseAccess.LastCommand).IsSameReferenceAs(command);
        await Assert.That(command.Disposed).IsTrue();
        await Assert.That(reader.Disposed).IsTrue();
    }

    [Test]
    public async Task Load_BuffersCanonicalRowsAndOwnsCommandReaderLifetime()
    {
        var converter = new RecordingIdConverter();
        var table = CreateTable(converter);
        var reader = new TrackingReader(
        [
            [42, "Ada"],
            [43, "Grace"]
        ]);
        var command = new TrackingCommand();
        var databaseAccess = new TrackingDatabaseAccess(reader);
        var writer = new RecordingWriter();
        var provider = new TrackingProvider(table.Database, databaseAccess, writer, command);
        var source = new TrackingDataSourceAccess(provider, databaseAccess);
        var services = (IDataLinqSourceRowServices)source;
        var request = new SourcePrimaryKeyRowRequest(
            table,
            [DataLinqKey.FromValue(42), DataLinqKey.FromValue(43)]);

        var result = services.RowLoader.Load(request);

        await Assert.That(services.RowLoader).IsSameReferenceAs(services.RowLoader);
        await Assert.That(result.Request).IsSameReferenceAs(request);
        await Assert.That(result.Rows.Length).IsEqualTo(2);
        await Assert.That(result.Rows[0][table.GetColumnByDbName("id")]).IsEqualTo(42);
        await Assert.That(result.Rows[0][table.GetColumnByDbName("id")]).IsTypeOf<int>();
        await Assert.That(result.Rows[1][table.GetColumnByDbName("name")]).IsEqualTo("Grace");
        await Assert.That(writer.Values.Select(static value => value.Value).ToArray())
            .IsEquivalentTo(new object?[] { 42, 43 });
        await Assert.That(converter.ToProviderCalls).IsEqualTo(0);
        await Assert.That(converter.FromProviderCalls).IsEqualTo(0);
        await Assert.That(provider.CommandCreationCalls).IsEqualTo(1);
        await Assert.That(databaseAccess.LastCommand).IsSameReferenceAs(command);
        await Assert.That(command.Disposed).IsTrue();
        await Assert.That(reader.Disposed).IsTrue();
    }

    [Test]
    public async Task Load_CancellationDuringReadDisposesCommandAndReader()
    {
        var table = CreateTable(new RecordingIdConverter());
        using var cancellation = new CancellationTokenSource();
        var reader = new TrackingReader([[42, "Ada"]])
        {
            BeforeReturningRow = cancellation.Cancel
        };
        var command = new TrackingCommand();
        var databaseAccess = new TrackingDatabaseAccess(reader);
        var provider = new TrackingProvider(
            table.Database,
            databaseAccess,
            new RecordingWriter(),
            command);
        var source = new TrackingDataSourceAccess(provider, databaseAccess);
        var request = new SourcePrimaryKeyRowRequest(
            table,
            [DataLinqKey.FromValue(42)],
            cancellation.Token);

        var exception = Capture<OperationCanceledException>(() =>
            ((IDataLinqSourceRowServices)source).RowLoader.Load(request));

        await Assert.That(exception.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(command.Disposed).IsTrue();
        await Assert.That(reader.Disposed).IsTrue();
    }

    [Test]
    public async Task Load_SingleScalarPrimaryKeyUsesEqualityPredicate()
    {
        var table = CreateTable(new RecordingIdConverter());
        var reader = new TrackingReader([[42, "Ada"]]);
        var command = new TrackingCommand();
        var databaseAccess = new TrackingDatabaseAccess(reader);
        var provider = new TrackingProvider(
            table.Database,
            databaseAccess,
            new RecordingWriter(),
            command);
        var source = new TrackingDataSourceAccess(provider, databaseAccess);
        var request = new SourcePrimaryKeyRowRequest(
            table,
            [DataLinqKey.FromValue(42)]);

        _ = ((IDataLinqSourceRowServices)source).RowLoader.Load(request);

        var select = (Select<object>)provider.LastQuery!;
        var primaryKey = select.Query.TryGetSimplePrimaryKey();
        await Assert.That(primaryKey).IsNotNull();
        await Assert.That(primaryKey!.Value.GetValue(0)).IsEqualTo(42);
        await Assert.That(command.Disposed).IsTrue();
        await Assert.That(reader.Disposed).IsTrue();
    }

    private static TableDefinition CreateTable(RecordingIdConverter converter)
    {
        var scalarConverter = new MetadataScalarConverterDraft(
            new CsTypeDeclaration(typeof(ModelId)),
            new CsTypeDeclaration(typeof(int)),
            new CsTypeDeclaration(typeof(RecordingIdConverter)),
            () => converter)
        {
            Origin = ScalarConverterOrigin.Property
        };
        var draft = new MetadataDatabaseDraft(
            "SourceRowLoaderDb",
            new CsTypeDeclaration(typeof(DataSourceAccessSourceRowLoaderTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(SourceRowModel)))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(ModelId)),
                                new MetadataColumnDraft("id") { PrimaryKey = true })
                            {
                                ScalarConverter = scalarConverter
                            },
                            new MetadataValuePropertyDraft(
                                "Name",
                                new CsTypeDeclaration(typeof(string)),
                                new MetadataColumnDraft("name"))
                        ]
                    },
                    new MetadataTableDraft("source_rows"))
            ]
        };

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels[0]
            .Table;
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }

    private sealed record ModelId(int Value);
    private sealed class SourceRowModel;

    private sealed class RecordingIdConverter : DataLinqScalarConverter<ModelId, int>
    {
        public int ToProviderCalls { get; private set; }
        public int FromProviderCalls { get; private set; }

        public override int ToProvider(ModelId modelValue, in ScalarConversionContext context)
        {
            ToProviderCalls++;
            return modelValue.Value;
        }

        public override ModelId FromProvider(int providerValue, in ScalarConversionContext context)
        {
            FromProviderCalls++;
            return new ModelId(providerValue);
        }
    }

    private sealed class TrackingDataSourceAccess(
        IDatabaseProvider provider,
        TrackingDatabaseAccess databaseAccess) : DataSourceAccess(provider)
    {
        public override IDatabaseAccess DatabaseAccess => databaseAccess;
        public override IEnumerable<T> GetFromQuery<T>(string query) => throw new NotSupportedException();
        public override IEnumerable<T> GetFromCommand<T>(IDbCommand dbCommand) => throw new NotSupportedException();
    }

    private sealed class TrackingProvider(
        DatabaseDefinition metadata,
        TrackingDatabaseAccess databaseAccess,
        RecordingWriter writer,
        TrackingCommand command) : IDatabaseProvider
    {
        public int CommandCreationCalls { get; private set; }
        public IQuery? LastQuery { get; private set; }
        public string TelemetryInstanceId { get; } = Guid.NewGuid().ToString("N");
        public string DatabaseName => metadata.DbName;
        public string ConnectionString => "tracking";
        public DatabaseDefinition Metadata => metadata;
        public DatabaseAccess DatabaseAccess => databaseAccess;
        public State State => throw new NotSupportedException();
        public IDatabaseProviderConstants Constants { get; } = new TrackingConstants();
        public ReadOnlyAccess ReadOnlyAccess => throw new NotSupportedException();
        public DatabaseType DatabaseType => DatabaseType.SQLite;

        public IDbCommand ToDbCommand(IQuery query)
        {
            CommandCreationCalls++;
            LastQuery = query;
            return command;
        }

        public IDataLinqDataWriter GetWriter() => writer;
        public Transaction StartTransaction(TransactionType transactionType = TransactionType.ReadAndWrite) => throw new NotSupportedException();
        public DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) => throw new NotSupportedException();
        public DatabaseTransaction AttachDatabaseTransaction(IDbTransaction dbTransaction, TransactionType type) => throw new NotSupportedException();
        public string GetLastIdQuery() => throw new NotSupportedException();
        public string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) => throw new NotSupportedException();
        public TableCache GetTableCache(TableDefinition table) => throw new NotSupportedException();
        public string GetOperatorSql(Operator @operator) => throw new NotSupportedException();
        public Sql GetParameter(Sql sql, string key, object? value) => throw new NotSupportedException();
        public Sql GetParameterValue(Sql sql, string key) => throw new NotSupportedException();
        public string GetParameterName(Operator relation, string[] key) => throw new NotSupportedException();
        public Sql GetParameterComparison(Sql sql, string field, Operator @operator, string[] prefix) => throw new NotSupportedException();
        public Sql GetLimitOffset(Sql sql, int? limit, int? offset) => throw new NotSupportedException();
        public bool DatabaseExists(string? databaseName = null) => throw new NotSupportedException();
        public bool FileOrServerExists() => throw new NotSupportedException();
        public Sql GetTableName(Sql sql, string tableName, string? alias = null) => throw new NotSupportedException();
        public M Commit<M>(Func<Transaction, M> func) => throw new NotSupportedException();
        public void Commit(Action<Transaction> action) => throw new NotSupportedException();
        public bool TableExists(string tableName, string? databaseName = null) => throw new NotSupportedException();
        public IDbConnection GetDbConnection() => throw new NotSupportedException();
        public Sql GetCreateSql() => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class TrackingConstants : IDatabaseProviderConstants
    {
        public string ParameterSign => "@";
        public string LastInsertCommand => string.Empty;
        public string EscapeCharacter => "\"";
        public bool SupportsMultipleDatabases => false;
    }

    private sealed class RecordingWriter : IDataLinqDataWriter
    {
        public List<(ColumnDefinition Column, object? Value)> Values { get; } = [];

        public object? ConvertValue(ColumnDefinition column, object? value)
        {
            Values.Add((column, value));
            return value;
        }
    }

    private sealed class TrackingDatabaseAccess(TrackingReader reader) : DatabaseAccess
    {
        public IDbCommand? LastCommand { get; private set; }

        public override IDataLinqDataReader ExecuteReader(IDbCommand command)
        {
            LastCommand = command;
            return reader;
        }

        public override IDataLinqDataReader ExecuteReader(string query) => throw new NotSupportedException();
        public override object? ExecuteScalar(IDbCommand command) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();
        public override object? ExecuteScalar(string query) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();
        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();
        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();
    }

    private sealed class TrackingReader(IReadOnlyList<object?[]> rows) : IDataLinqDataReader
    {
        private int rowIndex = -1;

        public Action? BeforeReturningRow { get; init; }
        public bool Disposed { get; private set; }
        private object?[] Current => rows[rowIndex];

        public bool ReadNextRow()
        {
            if (rowIndex + 1 >= rows.Count)
                return false;

            rowIndex++;
            BeforeReturningRow?.Invoke();
            return true;
        }

        public object GetValue(int ordinal) => Current[ordinal]!;
        public int GetOrdinal(string name) => throw new NotSupportedException();
        public string GetString(int ordinal) => (string)Current[ordinal]!;
        public bool GetBoolean(int ordinal) => (bool)Current[ordinal]!;
        public int GetInt32(int ordinal) => Convert.ToInt32(Current[ordinal]);
        public DateOnly GetDateOnly(int ordinal) => (DateOnly)Current[ordinal]!;
        public Guid GetGuid(int ordinal) => (Guid)Current[ordinal]!;
        public byte[]? GetBytes(int ordinal) => (byte[]?)Current[ordinal];
        public long GetBytes(int ordinal, Span<byte> buffer) => throw new NotSupportedException();
        public T? GetValue<T>(ColumnDefinition column) => (T?)Current[column.Index];
        public T? GetValue<T>(ColumnDefinition column, int ordinal) => (T?)Current[ordinal];
        public bool IsDbNull(int ordinal) => Current[ordinal] is null or DBNull;
        public void Dispose() => Disposed = true;
    }

    private sealed class TrackingCommand : IDbCommand
    {
        public bool Disposed { get; private set; }
        [AllowNull]
        public string CommandText { get; set; } = string.Empty;
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
        public void Dispose() => Disposed = true;
    }
}
