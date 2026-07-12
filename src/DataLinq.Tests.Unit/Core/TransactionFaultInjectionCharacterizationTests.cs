using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.SQLite;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Unit.Core;

/// <summary>
/// Freezes the provider-failure partitions that exist before the v0.9 TX-1 through TX-4 work.
/// Tests named CurrentBehavior document a known gap; they are not the target failure contract.
/// </summary>
public class TransactionFaultInjectionCharacterizationTests
{
    [Test]
    public async Task ImmutableReadSource_TerminalTransactionsFallBackToProviderReadOnlyAccess()
    {
        using var committedFixture = CreateFixture();
        var committedRow = new TransactionBoundImmutable(
            new SourceOnlyRowData(committedFixture.Cache.Table),
            committedFixture.Transaction);

        await Assert.That(committedRow.GetReadSource()).IsSameReferenceAs(committedFixture.Transaction);
        await Assert.That(committedRow.GetDataSource()).IsSameReferenceAs(committedFixture.Transaction);

        committedFixture.Transaction.Commit();

        await Assert.That(committedRow.GetReadSource()).IsSameReferenceAs(committedFixture.Transaction.Provider.ReadOnlyAccess);
        await Assert.That(committedRow.GetDataSource()).IsSameReferenceAs(committedFixture.Transaction.Provider.ReadOnlyAccess);

        using var rolledBackFixture = CreateFixture();
        var rolledBackRow = new TransactionBoundImmutable(
            new SourceOnlyRowData(rolledBackFixture.Cache.Table),
            rolledBackFixture.Transaction);

        rolledBackFixture.Transaction.Rollback();

        await Assert.That(rolledBackRow.GetReadSource()).IsSameReferenceAs(rolledBackFixture.Transaction.Provider.ReadOnlyAccess);
        await Assert.That(rolledBackRow.GetDataSource()).IsSameReferenceAs(rolledBackFixture.Transaction.Provider.ReadOnlyAccess);
    }

    [Test]
    public async Task Commit_ProviderCompletesBeforeTransactionCacheCleanup()
    {
        using var fixture = CreateFixture();

        fixture.Transaction.Commit();

        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await Assert.That(fixture.Transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.Committed);
        await Assert.That(fixture.OwnershipLifecycle.Snapshot.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(fixture.OwnershipLifecycle.Snapshot.TransactionOwner).IsNull();
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.commit(cache=present) -> provider.dispose(cache=present) -> provider.resources.dispose -> status.Committed");
    }

    [Test]
    public async Task CommitProviderFailure_CurrentBehavior_PropagatesAndRetainsOpenTransactionCache()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("commit");
        fixture.Scenario.CommitFailure = expected;

        var observed = CaptureException(fixture.Transaction.Commit);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsTrue();
        await AssertUnresolvedOwnership(fixture);
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo("provider.commit(cache=present)");
    }

    [Test]
    public async Task CommitDisposalFailure_CurrentBehavior_PropagatesAndRetainsCommittedTransactionCache()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("commit disposal");
        fixture.Scenario.ResourceDisposalFailure = expected;

        var observed = CaptureException(fixture.Transaction.Commit);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsTrue();
        await AssertUnresolvedOwnership(fixture);
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.commit(cache=present) -> provider.dispose(cache=present) -> provider.resources.dispose");
    }

    [Test]
    public async Task Rollback_ProviderCompletesBeforeTransactionCacheCleanup()
    {
        using var fixture = CreateFixture();

        fixture.Transaction.Rollback();

        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.rollback(cache=present) -> status.RolledBack -> provider.dispose(cache=present) -> provider.resources.dispose");
    }

    [Test]
    public async Task RollbackProviderFailure_CurrentBehavior_PropagatesAndRetainsOpenTransactionCache()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("rollback");
        fixture.Scenario.RollbackFailure = expected;

        var observed = CaptureException(fixture.Transaction.Rollback);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsTrue();
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo("provider.rollback(cache=present)");
    }

    [Test]
    public async Task RollbackDisposalFailure_CurrentBehavior_PropagatesAndRetainsRolledBackTransactionCache()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("rollback disposal");
        fixture.Scenario.ResourceDisposalFailure = expected;

        var observed = CaptureException(fixture.Transaction.Rollback);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsTrue();
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.rollback(cache=present) -> status.RolledBack -> provider.dispose(cache=present) -> provider.resources.dispose");
    }

    [Test]
    public async Task Dispose_CleansTransactionCacheBeforeProviderRollbackAndDisposal()
    {
        using var fixture = CreateFixture();

        fixture.Transaction.Dispose();

        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.dispose(cache=absent) -> provider.rollback-during-dispose -> status.RolledBack -> provider.resources.dispose");
    }

    [Test]
    public async Task DisposeRollbackFailure_PropagatesButTransactionCacheIsAlreadyClean()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("dispose rollback");
        fixture.Scenario.DisposeRollbackFailure = expected;

        var observed = CaptureException(fixture.Transaction.Dispose);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.dispose(cache=absent) -> provider.rollback-during-dispose");
    }

    [Test]
    public async Task DisposeResourceFailure_PropagatesAfterRollbackAndTransactionCacheCleanup()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("resource disposal");
        fixture.Scenario.ResourceDisposalFailure = expected;

        var observed = CaptureException(fixture.Transaction.Dispose);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.dispose(cache=absent) -> provider.rollback-during-dispose -> status.RolledBack -> provider.resources.dispose");
    }

    private static Fixture CreateFixture()
    {
        var scenario = new FaultScenario();
        var provider = new FaultInjectingProvider(scenario);
        var transaction = new Transaction(provider, TransactionType.ReadAndWrite);
        var employeeTable = provider.Metadata.TableModels
            .Single(tableModel => tableModel.Model.CsType.Type == typeof(Employee))
            .Table;
        var cache = provider.GetTableCache(employeeTable);

        scenario.IsTransactionCached = () => cache.IsTransactionInCache(transaction);
        transaction.OnStatusChanged += (_, args) => scenario.Calls.Add($"status.{args.Status}");

        // A public lookup is enough to create the transaction cache scope. The fake reader
        // returns no rows because these tests only need the lifecycle entry, not row data.
        _ = cache.GetRow(int.MaxValue, transaction);

        if (transaction.Status != DatabaseTransactionStatus.Open || !cache.IsTransactionInCache(transaction))
        {
            provider.Dispose();
            throw new InvalidOperationException("The fault-injection fixture did not establish an open cached transaction.");
        }

        scenario.Calls.Clear();
        var ownershipLifecycle = MutableLifecycle.New();
        ownershipLifecycle.ValidateHydratedAdvance();
        ownershipLifecycle.AdvanceHydrated(transaction.MutableOwnership);
        return new Fixture(provider, transaction, cache, scenario, ownershipLifecycle);
    }

    private static async Task AssertUnresolvedOwnership(Fixture fixture)
    {
        await Assert.That(fixture.Transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.Unresolved);
        await Assert.That(fixture.OwnershipLifecycle.Snapshot.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(fixture.OwnershipLifecycle.Snapshot.TransactionOwner)
            .IsSameReferenceAs(fixture.Transaction.MutableOwnership);
    }

    private static string Describe(IEnumerable<string> calls) => string.Join(" -> ", calls);

    private static Exception CaptureException(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected the injected provider exception to propagate.");
    }

    private sealed class Fixture(
        FaultInjectingProvider provider,
        Transaction transaction,
        TableCache cache,
        FaultScenario scenario,
        MutableLifecycle ownershipLifecycle) : IDisposable
    {
        public Transaction Transaction { get; } = transaction;
        public TableCache Cache { get; } = cache;
        public FaultScenario Scenario { get; } = scenario;
        public MutableLifecycle OwnershipLifecycle { get; } = ownershipLifecycle;

        public void Dispose() => provider.Dispose();
    }

    private sealed class TransactionBoundImmutable(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<Employee, EmployeesDb>(rowData, dataSource);

    private sealed class SourceOnlyRowData(TableDefinition table) : IRowData
    {
        public TableDefinition Table { get; } = table;
        public object? this[ColumnDefinition column] => throw new NotSupportedException();
        public object? this[int columnIndex] => throw new NotSupportedException();
        public object? GetValue(ColumnDefinition column) => throw new NotSupportedException();
        public object? GetValue(int columnIndex) => throw new NotSupportedException();
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) => [];
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() => [];
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(IEnumerable<ColumnDefinition> columns) => [];
    }

    private sealed class FaultScenario
    {
        public List<string> Calls { get; } = [];
        public Func<bool> IsTransactionCached { get; set; } = static () => false;
        public Exception? CommitFailure { get; set; }
        public Exception? RollbackFailure { get; set; }
        public Exception? DisposeRollbackFailure { get; set; }
        public Exception? ResourceDisposalFailure { get; set; }

        public string CacheState => IsTransactionCached() ? "present" : "absent";
    }

    private sealed class FaultInjectingProvider : SQLiteProvider<EmployeesDb>
    {
        private readonly FaultInjectingDatabaseTransaction databaseTransaction;

        public FaultInjectingProvider(FaultScenario scenario)
            : base("Data Source=:memory:")
        {
            databaseTransaction = new FaultInjectingDatabaseTransaction(scenario);
        }

        public override DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) => databaseTransaction;
    }

    private sealed class FaultInjectingDatabaseTransaction(FaultScenario scenario)
        : DatabaseTransaction(TransactionType.ReadAndWrite)
    {
        public override void Commit()
        {
            scenario.Calls.Add($"provider.commit(cache={scenario.CacheState})");
            if (scenario.CommitFailure is not null)
                throw scenario.CommitFailure;

            SetStatus(DatabaseTransactionStatus.Committed);
            DisposeProviderResources();
        }

        public override void Rollback()
        {
            scenario.Calls.Add($"provider.rollback(cache={scenario.CacheState})");
            if (scenario.RollbackFailure is not null)
                throw scenario.RollbackFailure;

            SetStatus(DatabaseTransactionStatus.RolledBack);
            DisposeProviderResources();
        }

        public override void Dispose()
        {
            scenario.Calls.Add($"provider.dispose(cache={scenario.CacheState})");
            if (Status == DatabaseTransactionStatus.Open)
            {
                scenario.Calls.Add("provider.rollback-during-dispose");
                if (scenario.DisposeRollbackFailure is not null)
                    throw scenario.DisposeRollbackFailure;

                SetStatus(DatabaseTransactionStatus.RolledBack);
            }

            DisposeResource();
        }

        public override IDataLinqDataReader ExecuteReader(IDbCommand command)
        {
            EnsureOpen();
            return EmptyDataLinqDataReader.Instance;
        }

        public override IDataLinqDataReader ExecuteReader(string query)
        {
            EnsureOpen();
            return EmptyDataLinqDataReader.Instance;
        }

        public override object? ExecuteScalar(IDbCommand command) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();
        public override object? ExecuteScalar(string query) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();
        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();
        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();

        private void EnsureOpen()
        {
            if (Status == DatabaseTransactionStatus.Closed)
                SetStatus(DatabaseTransactionStatus.Open);
        }

        private void DisposeProviderResources()
        {
            scenario.Calls.Add($"provider.dispose(cache={scenario.CacheState})");
            DisposeResource();
        }

        private void DisposeResource()
        {
            scenario.Calls.Add("provider.resources.dispose");
            if (scenario.ResourceDisposalFailure is not null)
                throw scenario.ResourceDisposalFailure;
        }
    }

    private sealed class EmptyDataLinqDataReader : IDataLinqDataReader
    {
        public static EmptyDataLinqDataReader Instance { get; } = new();

        public bool ReadNextRow() => false;
        public void Dispose() { }
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

    private sealed class InjectedProviderException(string operation)
        : Exception($"Injected provider failure during {operation}.");
}
