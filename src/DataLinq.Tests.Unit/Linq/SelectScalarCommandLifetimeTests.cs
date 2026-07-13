using System;
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
using DataLinq.Tests.Models.Employees;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Linq;

public sealed class SelectScalarCommandLifetimeTests
{
    [Test]
    public async Task ExecuteScalar_ObjectResultDisposesCommandOnSuccess()
    {
        var fixture = CreateFixture(7L);

        var result = fixture.Select.ExecuteScalar();

        await Assert.That(result).IsEqualTo(7L);
        await Assert.That(ReferenceEquals(fixture.DatabaseAccess.LastCommand, fixture.Command)).IsTrue();
        await Assert.That(fixture.Command.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteScalar_ObjectResultDisposesCommandOnFailure()
    {
        var failure = new InvalidOperationException("Synthetic scalar failure.");
        var fixture = CreateFixture(null, failure);

        var exception = Capture<InvalidOperationException>(() => fixture.Select.ExecuteScalar());

        await Assert.That(ReferenceEquals(exception, failure)).IsTrue();
        await Assert.That(ReferenceEquals(fixture.DatabaseAccess.LastCommand, fixture.Command)).IsTrue();
        await Assert.That(fixture.Command.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteScalar_TypedResultDisposesCommandOnSuccess()
    {
        var fixture = CreateFixture(7);

        var result = fixture.Select.ExecuteScalar<int>();

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(ReferenceEquals(fixture.DatabaseAccess.LastCommand, fixture.Command)).IsTrue();
        await Assert.That(fixture.Command.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteScalar_TypedResultDisposesCommandOnFailure()
    {
        var failure = new InvalidOperationException("Synthetic typed scalar failure.");
        var fixture = CreateFixture(null, failure);

        var exception = Capture<InvalidOperationException>(() => fixture.Select.ExecuteScalar<int>());

        await Assert.That(ReferenceEquals(exception, failure)).IsTrue();
        await Assert.That(ReferenceEquals(fixture.DatabaseAccess.LastCommand, fixture.Command)).IsTrue();
        await Assert.That(fixture.Command.DisposeCalls).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteScalar_CancellationDuringCommandCreationDisposesWithoutProviderExecution()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = CreateFixture(7L, onCommandCreated: cancellation.Cancel);

        _ = Capture<OperationCanceledException>(() =>
            fixture.Select.ExecuteScalar(cancellation.Token));

        await Assert.That(fixture.DatabaseAccess.LastCommand).IsNull();
        await Assert.That(fixture.Command.DisposeCalls).IsEqualTo(1);
    }

    private static ScalarFixture CreateFixture(
        object? result,
        Exception? failure = null,
        Action? onCommandCreated = null)
    {
        var metadata = MetadataFromTypeFactory
            .ParseDatabaseFromDatabaseModel(typeof(EmployeesDb))
            .ValueOrException();
        var table = metadata.TableModels
            .Single(model => model.Model.CsType.Type == typeof(Employee))
            .Table;
        var command = new TrackingCommand();
        var databaseAccess = new TrackingDatabaseAccess(result, failure);
        var provider = new TrackingProvider(
            metadata,
            databaseAccess,
            command,
            onCommandCreated);
        var dataSource = new TrackingDataSourceAccess(provider, databaseAccess);
        var select = new SqlQuery<object>(table, dataSource, "t0")
            .SelectQuery()
            .What("COUNT(*)");

        return new ScalarFixture(select, databaseAccess, command);
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

        throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
    }

    private sealed record ScalarFixture(
        Select<object> Select,
        TrackingDatabaseAccess DatabaseAccess,
        TrackingCommand Command);

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
        TrackingCommand command,
        Action? onCommandCreated) : IDatabaseProvider
    {
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
            onCommandCreated?.Invoke();
            return command;
        }
        public IDataLinqDataWriter GetWriter() => throw new NotSupportedException();
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

    private sealed class TrackingDatabaseAccess(
        object? scalarResult,
        Exception? failure) : DatabaseAccess
    {
        public IDbCommand? LastCommand { get; private set; }

        public override object? ExecuteScalar(IDbCommand command)
        {
            LastCommand = command;
            if (failure is not null)
                throw failure;

            return scalarResult;
        }

        public override T ExecuteScalar<T>(IDbCommand command)
        {
            var result = ExecuteScalar(command);
            if (result is T typed)
                return typed;

            return (T)Convert.ChangeType(result!, typeof(T));
        }

        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new NotSupportedException();
        public override IDataLinqDataReader ExecuteReader(string query) => throw new NotSupportedException();
        public override object? ExecuteScalar(string query) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();
        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();
        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();
    }

    private sealed class TrackingCommand : IDbCommand
    {
        public int DisposeCalls { get; private set; }
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
        public void Dispose() => DisposeCalls++;
    }
}
