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
/// Verifies provider completion, rollback/disposal cleanup, and managed status-publication ordering.
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
    public async Task CommitProviderFailure_MarksOutcomeUnknownAndPreservesItThroughRollbackCleanup()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("commit");
        fixture.Scenario.CommitFailure = expected;
        var transactionBound = new TransactionBoundImmutable(
            new SourceOnlyRowData(fixture.Cache.Table),
            fixture.Transaction);

        var observed = CaptureException(fixture.Transaction.Commit);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsTrue();
        await AssertInvalidOwnership(
            fixture,
            MutableTransactionOutcome.CommitOutcomeUnknown,
            MutableInvalidationReason.CommitOutcomeUnknown);
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo("provider.commit(cache=present)");

        var callsBeforeGates = fixture.Scenario.Calls.ToArray();
        var readFailure = CaptureException(() => _ = transactionBound.GetReadSource());
        var writeFailure = CaptureException(() => fixture.Transaction.Delete(
            new TransactionBoundImmutable(
                new SourceOnlyRowData(fixture.Cache.Table),
                fixture.Transaction.Provider.ReadOnlyAccess)));
        var repeatedCommitFailure = CaptureException(fixture.Transaction.Commit);

        foreach (var gateFailure in new[] { readFailure, writeFailure, repeatedCommitFailure })
        {
            await Assert.That(gateFailure).IsTypeOf<InvalidOperationException>();
            await Assert.That(gateFailure.Message).Contains("provider commit call failed");
        }

        await Assert.That(fixture.Scenario.Calls).IsEquivalentTo(callsBeforeGates);

        var rollbackFailure = CaptureException(fixture.Transaction.Rollback);

        await Assert.That(rollbackFailure).IsTypeOf<InvalidOperationException>();
        await Assert.That(rollbackFailure.Message).Contains("cannot report a definite rollback");
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await AssertInvalidOwnership(
            fixture,
            MutableTransactionOutcome.CommitOutcomeUnknown,
            MutableInvalidationReason.CommitOutcomeUnknown);
        await AssertFinalizedStatusObservation(
            fixture,
            MutableTransactionOutcome.CommitOutcomeUnknown,
            MutableInvalidationReason.CommitOutcomeUnknown);
    }

    [Test]
    public async Task CommitDisposalFailure_MarksOutcomeUnknownAndAllowsOnlyDisposalCleanup()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("commit disposal");
        fixture.Scenario.ResourceDisposalFailure = expected;
        var transactionBound = new TransactionBoundImmutable(
            new SourceOnlyRowData(fixture.Cache.Table),
            fixture.Transaction);

        var observed = CaptureException(fixture.Transaction.Commit);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsTrue();
        await AssertInvalidOwnership(
            fixture,
            MutableTransactionOutcome.CommitOutcomeUnknown,
            MutableInvalidationReason.CommitOutcomeUnknown);
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.commit(cache=present) -> provider.dispose(cache=present) -> provider.resources.dispose");

        var callsBeforeGates = fixture.Scenario.Calls.ToArray();
        var fallbackFailure = CaptureException(() => _ = transactionBound.GetReadSource());
        var writeFailure = CaptureException(() => fixture.Transaction.Delete(
            new TransactionBoundImmutable(
                new SourceOnlyRowData(fixture.Cache.Table),
                fixture.Transaction.Provider.ReadOnlyAccess)));
        var rollbackFailure = CaptureException(fixture.Transaction.Rollback);

        foreach (var gateFailure in new[] { fallbackFailure, writeFailure, rollbackFailure })
        {
            await Assert.That(gateFailure).IsTypeOf<InvalidOperationException>();
            await Assert.That(gateFailure.Message).Contains("provider commit call failed");
            await Assert.That(gateFailure.Message).Contains("only Dispose() remains legal");
        }

        await Assert.That(fixture.Scenario.Calls).IsEquivalentTo(callsBeforeGates);

        fixture.Scenario.ResourceDisposalFailure = null;
        fixture.Transaction.Dispose();

        await Assert.That(fixture.Transaction.IsDisposed).IsTrue();
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await AssertInvalidOwnership(
            fixture,
            MutableTransactionOutcome.CommitOutcomeUnknown,
            MutableInvalidationReason.CommitOutcomeUnknown);
    }

    [Test]
    public async Task Rollback_ProviderCompletesBeforeFinalizedStatusPublication()
    {
        using var fixture = CreateFixture();

        fixture.Transaction.Rollback();

        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await AssertInvalidOwnership(
            fixture,
            MutableTransactionOutcome.RolledBack,
            MutableInvalidationReason.RolledBack);
        await AssertFinalizedStatusObservation(
            fixture,
            MutableTransactionOutcome.RolledBack,
            MutableInvalidationReason.RolledBack);
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.rollback(cache=present) -> provider.dispose(cache=present) -> provider.resources.dispose -> status.RolledBack");
    }

    [Test]
    public async Task RollbackProviderFailure_InvalidatesOwnershipCleansCacheAndGatesManagedOperations()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("rollback");
        fixture.Scenario.RollbackFailure = expected;

        var observed = CaptureException(fixture.Transaction.Rollback);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await AssertInvalidOwnership(
            fixture,
            MutableTransactionOutcome.RollbackOutcomeUnknown,
            MutableInvalidationReason.RollbackOutcomeUnknown);
        await AssertManagedCompletionContext(
            expected,
            fixture.Transaction,
            "Rollback",
            MutableInvalidationReason.RollbackOutcomeUnknown);
        await Assert.That(fixture.Scenario.StatusObservations).IsEmpty();
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo("provider.rollback(cache=present)");

        var callsBeforeGates = fixture.Scenario.Calls.ToArray();
        var readFailure = CaptureException(() =>
            fixture.Transaction.GetFromQuery<Employee>("SELECT 1").ToArray());
        var writeFailure = CaptureException(() => fixture.Transaction.Delete(
            new TransactionBoundImmutable(
                new SourceOnlyRowData(fixture.Cache.Table),
                fixture.Transaction.Provider.ReadOnlyAccess)));
        var commitFailure = CaptureException(fixture.Transaction.Commit);
        var secondRollbackFailure = CaptureException(fixture.Transaction.Rollback);

        foreach (var gateFailure in new[]
        {
            readFailure,
            writeFailure,
            commitFailure,
            secondRollbackFailure
        })
        {
            await Assert.That(gateFailure is InvalidOperationException).IsTrue();
            await Assert.That(gateFailure.Message)
                .Contains("rollback outcome is unknown");
            await Assert.That(gateFailure.Message)
                .Contains("Only Dispose() remains legal");
        }

        await Assert.That(fixture.Scenario.Calls).IsEquivalentTo(callsBeforeGates);

        fixture.Transaction.Dispose();

        await Assert.That(fixture.Transaction.IsDisposed).IsTrue();
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await AssertInvalidOwnership(
            fixture,
            MutableTransactionOutcome.RollbackOutcomeUnknown,
            MutableInvalidationReason.RollbackOutcomeUnknown);
    }

    [Test]
    public async Task RollbackDisposalFailure_InvalidatesOwnershipAndCleansCacheBeforeRethrowing()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("rollback disposal");
        fixture.Scenario.ResourceDisposalFailure = expected;

        var observed = CaptureException(fixture.Transaction.Rollback);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await AssertInvalidOwnership(
            fixture,
            MutableTransactionOutcome.RolledBack,
            MutableInvalidationReason.RolledBack);
        await AssertManagedCompletionContext(
            expected,
            fixture.Transaction,
            "Rollback",
            MutableInvalidationReason.RolledBack);
        await AssertFinalizedStatusObservation(
            fixture,
            MutableTransactionOutcome.RolledBack,
            MutableInvalidationReason.RolledBack);
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.rollback(cache=present) -> provider.dispose(cache=present) -> provider.resources.dispose -> status.RolledBack");
    }

    [Test]
    public async Task Dispose_CleansAndInvalidatesBeforeFinalizedRolledBackStatusPublication()
    {
        using var fixture = CreateFixture();

        fixture.Transaction.Dispose();

        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await AssertInvalidOwnership(
            fixture,
            MutableTransactionOutcome.OpenTransactionDisposed,
            MutableInvalidationReason.OpenTransactionDisposed);
        await AssertFinalizedStatusObservation(
            fixture,
            MutableTransactionOutcome.OpenTransactionDisposed,
            MutableInvalidationReason.OpenTransactionDisposed);
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.dispose(cache=present) -> provider.rollback-during-dispose -> provider.resources.dispose -> status.RolledBack");
    }

    [Test]
    public async Task DisposeRollbackFailure_InvalidatesOwnershipAndKeepsCacheClean()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("dispose rollback");
        fixture.Scenario.DisposeRollbackFailure = expected;

        var observed = CaptureException(fixture.Transaction.Dispose);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await AssertInvalidOwnership(
            fixture,
            MutableTransactionOutcome.OpenTransactionDisposed,
            MutableInvalidationReason.OpenTransactionDisposed);
        await AssertManagedCompletionContext(
            expected,
            fixture.Transaction,
            "Dispose",
            MutableInvalidationReason.OpenTransactionDisposed);
        await Assert.That(fixture.Scenario.StatusObservations).IsEmpty();
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.dispose(cache=present) -> provider.rollback-during-dispose");
    }

    [Test]
    public async Task DisposeResourceFailure_InvalidatesOwnershipAndKeepsCacheClean()
    {
        using var fixture = CreateFixture();
        var expected = new InjectedProviderException("resource disposal");
        fixture.Scenario.ResourceDisposalFailure = expected;

        var observed = CaptureException(fixture.Transaction.Dispose);

        await Assert.That(observed).IsSameReferenceAs(expected);
        await Assert.That(fixture.Transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Cache.IsTransactionInCache(fixture.Transaction)).IsFalse();
        await AssertInvalidOwnership(
            fixture,
            MutableTransactionOutcome.OpenTransactionDisposed,
            MutableInvalidationReason.OpenTransactionDisposed);
        await AssertManagedCompletionContext(
            expected,
            fixture.Transaction,
            "Dispose",
            MutableInvalidationReason.OpenTransactionDisposed);
        await AssertFinalizedStatusObservation(
            fixture,
            MutableTransactionOutcome.OpenTransactionDisposed,
            MutableInvalidationReason.OpenTransactionDisposed);
        await Assert.That(Describe(fixture.Scenario.Calls)).IsEqualTo(
            "provider.dispose(cache=present) -> provider.rollback-during-dispose -> provider.resources.dispose -> status.RolledBack");
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
        var ownershipLifecycle = MutableLifecycle.New();
        ownershipLifecycle.ValidateHydratedAdvance();
        ownershipLifecycle.AdvanceHydrated(transaction.MutableOwnership);
        transaction.OnStatusChanged += (_, args) =>
        {
            scenario.Calls.Add($"status.{args.Status}");
            scenario.StatusObservations.Add(new StatusObservation(
                args.Status,
                cache.IsTransactionInCache(transaction),
                transaction.MutableOwnership.Outcome,
                ownershipLifecycle.Snapshot));
        };

        // A public lookup is enough to create the transaction cache scope. The fake reader
        // returns no rows because these tests only need the lifecycle entry, not row data.
        _ = cache.GetRow(int.MaxValue, transaction);

        if (transaction.Status != DatabaseTransactionStatus.Open || !cache.IsTransactionInCache(transaction))
        {
            provider.Dispose();
            throw new InvalidOperationException("The fault-injection fixture did not establish an open cached transaction.");
        }

        scenario.Calls.Clear();
        scenario.StatusObservations.Clear();
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

    private static async Task AssertInvalidOwnership(
        Fixture fixture,
        MutableTransactionOutcome expectedOutcome,
        MutableInvalidationReason expectedReason)
    {
        await Assert.That(fixture.Transaction.MutableOwnership.Outcome)
            .IsEqualTo(expectedOutcome);
        await Assert.That(fixture.OwnershipLifecycle.Snapshot.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(fixture.OwnershipLifecycle.Snapshot.TransactionOwner).IsNull();
        await Assert.That(fixture.OwnershipLifecycle.Snapshot.InvalidationReason)
            .IsEqualTo(expectedReason);
    }

    private static async Task AssertFinalizedStatusObservation(
        Fixture fixture,
        MutableTransactionOutcome expectedOutcome,
        MutableInvalidationReason expectedReason)
    {
        await Assert.That(fixture.Scenario.StatusObservations).Count().IsEqualTo(1);
        var observation = fixture.Scenario.StatusObservations.Single();
        await Assert.That(observation.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(observation.TransactionCachePresent).IsFalse();
        await Assert.That(observation.OwnershipOutcome).IsEqualTo(expectedOutcome);
        await Assert.That(observation.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(observation.Lifecycle.InvalidationReason).IsEqualTo(expectedReason);
    }

    private static async Task AssertManagedCompletionContext(
        Exception exception,
        Transaction transaction,
        string operation,
        MutableInvalidationReason expectedReason)
    {
        await Assert.That(exception.Data["DataLinq.TransactionId"])
            .IsEqualTo(transaction.TransactionID);
        await Assert.That(exception.Data["DataLinq.CompletionOperation"])
            .IsEqualTo(operation);
        await Assert.That(
            exception.Data["DataLinq.LocalFinalizationAttempted"] is true)
            .IsTrue();
        await Assert.That(exception.Data["DataLinq.MutableInvalidationReason"])
            .IsEqualTo(expectedReason.ToString());
        await Assert.That(exception.Data.Contains("DataLinq.SecondaryCompletionFailures"))
            .IsFalse();
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
        public List<StatusObservation> StatusObservations { get; } = [];
        public Func<bool> IsTransactionCached { get; set; } = static () => false;
        public Exception? CommitFailure { get; set; }
        public Exception? RollbackFailure { get; set; }
        public Exception? DisposeRollbackFailure { get; set; }
        public Exception? ResourceDisposalFailure { get; set; }

        public string CacheState => IsTransactionCached() ? "present" : "absent";
    }

    private sealed record StatusObservation(
        DatabaseTransactionStatus Status,
        bool TransactionCachePresent,
        MutableTransactionOutcome OwnershipOutcome,
        MutableLifecycleSnapshot Lifecycle);

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
