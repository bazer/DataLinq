using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Logging;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Query;

namespace DataLinq.Tests.Unit.Core;

public sealed class TransactionMutationFailureTests
{
    [Test]
    public async Task CommandConstructionFailure_PoisonsAndExcludesCandidate()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(401, "before");
        mutable["Value"] = "after";
        var expected = new InjectedMutationException("command construction");
        fixture.Scenario.CommandFailure = expected;

        var observed = Capture<InjectedMutationException>(() => transaction.Update(mutable));

        await Assert.That(observed).IsSameReferenceAs(expected);
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.ProviderStatement,
            expected);
        await AssertInvalidMutation(mutable, expectedValue: "after");
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(1);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(0);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task CommandPreparationMutableDrift_PoisonsBeforeProviderExecution()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(400, "before");
        mutable["Value"] = "captured";
        fixture.Scenario.CommandCreated = () => mutable["Value"] = "command callback";

        var observed = Capture<InvalidOperationException>(() => transaction.Update(mutable));

        await Assert.That(observed.Message).Contains(
            "assignments changed during provider command preparation");
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.ProviderStatement,
            observed);
        await Assert.That(mutable["Value"]).IsEqualTo("command callback");
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(1);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task ProviderStatementFailure_PoisonsAndExcludesCandidate()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(402, "before");
        mutable["Value"] = "after";
        var expected = new InjectedMutationException("provider statement");
        fixture.Scenario.EnqueueNonQueryFailure(expected);

        var observed = Capture<InjectedMutationException>(() => transaction.Update(mutable));

        await Assert.That(observed).IsSameReferenceAs(expected);
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.ProviderStatement,
            expected);
        await AssertInvalidMutation(mutable, expectedValue: "after");
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(1);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task GeneratedIdDecodeFailure_PoisonsBeforeCacheOrTouch()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateNewAutoMutable("keep assignment");
        fixture.Scenario.ScalarResult = long.MaxValue;

        var observed = Capture<GeneratedValueDecodingException>(() =>
            transaction.Insert(mutable));

        await AssertPoisoned(
            transaction,
            TransactionFailureStage.Hydration,
            observed);
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
        await Assert.That(mutable["Id"]).IsNull();
        await Assert.That(mutable["Value"]).IsEqualTo("keep assignment");
        await Assert.That(mutable.GetChanges()).IsNotEmpty();
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.ScalarExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task TransactionCacheNotificationFailure_PoisonsAfterStatementAndExcludesCandidate()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(403, "before");
        mutable["Value"] = "after";
        var expected = new InjectedMutationException("transaction cache notification");
        var notification = new ThrowingNotification(expected);
        fixture.RowCache.SubscribeToChanges(notification, transaction);

        var observed = Capture<InjectedMutationException>(() => transaction.Update(mutable));

        await Assert.That(observed).IsSameReferenceAs(expected);
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.PendingCacheApplication,
            expected);
        await AssertInvalidMutation(mutable, expectedValue: "after");
        await Assert.That(notification.ClearCalls).IsEqualTo(1);
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);

        transaction.Rollback();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(1);
        await Assert.That(fixture.RowCache.IsTransactionInCache(transaction)).IsFalse();
    }

    [Test]
    public async Task RowReloadMiss_PoisonsAfterCacheApplicationAndExcludesCandidate()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(404, "before");
        mutable["Value"] = "after";

        var observed = Capture<ModelLoadFailureException>(() => transaction.Update(mutable));

        await AssertPoisoned(
            transaction,
            TransactionFailureStage.Hydration,
            observed);
        await AssertInvalidMutation(mutable, expectedValue: "after");
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(1);
    }

    [Test]
    public async Task EarlierSuccessfulDeleteThenFailedUpdate_RetainsOnlySuccessAndGatesReuse()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(405, "delete");
        var failed = fixture.CreateExistingMutable(406, "before");
        failed["Value"] = "after";
        var expected = new InjectedMutationException("second statement");
        fixture.Scenario.EnqueueNonQueryResult(1);
        fixture.Scenario.EnqueueNonQueryFailure(expected);
        var deferredRead = transaction.GetFromQuery<TransactionMutationGuardRow>(
            "SELECT id, value FROM transaction_mutation_guard_rows");

        transaction.Delete(deleted);
        var successfulChange = transaction.Changes.Single();
        var observed = Capture<InjectedMutationException>(() => transaction.Update(failed));

        await Assert.That(observed).IsSameReferenceAs(expected);
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.ProviderStatement,
            expected);
        await Assert.That(transaction.Changes).Count().IsEqualTo(1);
        await Assert.That(transaction.Changes.Single()).IsSameReferenceAs(successfulChange);
        await Assert.That(successfulChange.Model).IsSameReferenceAs(deleted);
        await Assert.That(transaction.TouchedMutables).Count().IsEqualTo(1);
        await Assert.That(transaction.TouchedMutables.Single()).IsSameReferenceAs(deleted);
        await Assert.That(deleted.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(deleted.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
        await AssertInvalidMutation(failed, expectedValue: "after");

        var countsBeforeGates = fixture.Scenario.SnapshotCounts();
        var readFailure = Capture<TransactionPoisonedException>(() => deferredRead.ToArray());
        var queryRootFailure = Capture<TransactionPoisonedException>(() => transaction.Query());
        var callbackInvoked = false;
        var writeFailure = Capture<TransactionPoisonedException>(() =>
            transaction.Update(failed, _ => callbackInvoked = true));
        var commitFailure = Capture<TransactionPoisonedException>(transaction.Commit);

        await AssertPoisonDiagnostic(readFailure, transaction.TransactionID);
        await AssertPoisonDiagnostic(queryRootFailure, transaction.TransactionID);
        await AssertPoisonDiagnostic(writeFailure, transaction.TransactionID);
        await AssertPoisonDiagnostic(commitFailure, transaction.TransactionID);
        await Assert.That(callbackInvoked).IsFalse();
        await Assert.That(fixture.Scenario.SnapshotCounts()).IsEqualTo(countsBeforeGates);

        transaction.Rollback();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(1);
    }

    [Test]
    public async Task PoisonedTransaction_DisposeRemainsLegalAndIdempotent()
    {
        using var fixture = new ScriptedFixture();
        var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(407, "before");
        mutable["Value"] = "after";
        fixture.Scenario.EnqueueNonQueryFailure(
            new InjectedMutationException("dispose recovery"));
        _ = Capture<InjectedMutationException>(() => transaction.Update(mutable));

        transaction.Dispose();
        transaction.Dispose();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(1);
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
    }

    [Test]
    public async Task StateChangeChangedAfterCapture_RejectsWithoutPoisoning()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(408, "before");
        mutable["Value"] = "captured";
        var stateChange = new StateChange(
            mutable,
            fixture.RowTable,
            TransactionChangeType.Update);

        mutable["Value"] = "later";

        var observed = Capture<MutationGuardException>(() =>
            stateChange.ExecuteQuery(transaction));

        await Assert.That(observed.Message).Contains(
            "assignments changed after this state change was captured");
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(mutable["Value"]).IsEqualTo("later");
    }

    [Test]
    public async Task SuccessfulImmutableDeleteStateChange_CanBeInspectedButCannotExecuteTwice()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var immutable = fixture.CreateImmutable(418, "delete once");
        var stateChange = new StateChange(
            immutable,
            fixture.RowTable,
            TransactionChangeType.Delete);

        stateChange.ExecuteQuery(transaction);
        _ = stateChange.GetQuery(transaction);
        using var command = stateChange.GetDbCommand(transaction);
        var observed = Capture<MutationGuardException>(() =>
            stateChange.ExecuteQuery(transaction));

        await Assert.That(observed.Message).Contains(
            "state change has already started provider execution");
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Changes).Count().IsEqualTo(1);
        await Assert.That(transaction.Changes.Single()).IsSameReferenceAs(stateChange);
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);

        transaction.Rollback();
    }

    [Test]
    public async Task StateChangeArrayChangedInPlaceAfterCapture_RejectsWithoutPoisoning()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingBinaryMutable(
            [0x01, 0x02],
            [0x10, 0x20]);
        var assignedPayload = new byte[] { 0x30, 0x40 };
        mutable["Payload"] = assignedPayload;
        var stateChange = new StateChange(
            mutable,
            fixture.BinaryTable,
            TransactionChangeType.Update);

        assignedPayload[0] = 0x31;

        var observed = Capture<MutationGuardException>(() =>
            stateChange.ExecuteQuery(transaction));

        await Assert.That(observed.Message).Contains(
            "assignments changed after this state change was captured");
        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(fixture.Scenario.CommandCreations).IsEqualTo(0);
        await Assert.That(((byte[])mutable["Payload"]!).SequenceEqual(
            new byte[] { 0x31, 0x40 })).IsTrue();
    }

    [Test]
    public async Task MutationCacheCallback_ReentrantManagedOperationsAreRejected()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var deleted = fixture.CreateExistingMutable(409, "delete");
        var reentrantUpdate = fixture.CreateExistingMutable(410, "before");
        reentrantUpdate["Value"] = "after";
        var notification = new ReentrantTransactionNotification(
            transaction,
            reentrantUpdate);
        fixture.RowCache.SubscribeToChanges(notification, transaction);

        transaction.Delete(deleted);

        await Assert.That(notification.ClearCalls).IsEqualTo(1);
        await Assert.That(notification.Failures).Count().IsEqualTo(5);
        foreach (var failure in notification.Failures)
        {
            await Assert.That(failure).IsTypeOf<InvalidOperationException>();
            await Assert.That(failure.Message).Contains(
                "another managed transaction operation is being finalized");
        }

        await Assert.That(transaction.IsPoisoned).IsFalse();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(transaction.Changes).Count().IsEqualTo(1);
        await Assert.That(transaction.Changes.Single().Model).IsSameReferenceAs(deleted);
        await Assert.That(transaction.TouchedMutables).Count().IsEqualTo(1);
        await Assert.That(transaction.TouchedMutables.Single()).IsSameReferenceAs(deleted);
        await Assert.That(reentrantUpdate.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(reentrantUpdate["Value"]).IsEqualTo("after");
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(0);
        await Assert.That(fixture.Scenario.Rollbacks).IsEqualTo(0);
        await Assert.That(fixture.Scenario.Disposals).IsEqualTo(0);

        transaction.Rollback();
    }

    [Test]
    public async Task MutationCacheCallback_DirectCurrentMutableDriftPoisonsBeforeHydration()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(415, "before");
        mutable["Value"] = "statement";
        var notification = new MutatingNotification(
            () => mutable["Value"] = "callback");
        fixture.RowCache.SubscribeToChanges(notification, transaction);

        var observed = Capture<InvalidOperationException>(() => transaction.Update(mutable));

        await Assert.That(observed.Message).Contains(
            "assignments changed while the transaction-local cache effect was being applied");
        await AssertPoisoned(
            transaction,
            TransactionFailureStage.PendingCacheApplication,
            observed);
        await Assert.That(notification.ClearCalls).IsEqualTo(1);
        await Assert.That(mutable["Value"]).IsEqualTo("callback");
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(1);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task PoisonedTransaction_AfterRollbackReportsTerminalState()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(411, "before");
        mutable["Value"] = "after";
        fixture.Scenario.EnqueueNonQueryFailure(
            new InjectedMutationException("terminal diagnostics"));
        _ = Capture<InjectedMutationException>(() => transaction.Update(mutable));

        transaction.Rollback();

        var readFailure = Capture<InvalidOperationException>(() => transaction.Query());
        var writeFailure = Capture<MutationGuardException>(() => transaction.Update(mutable));
        var commitFailure = Capture<InvalidOperationException>(transaction.Commit);
        var rollbackFailure = Capture<InvalidOperationException>(transaction.Rollback);

        await Assert.That(readFailure is TransactionPoisonedException).IsFalse();
        await Assert.That(commitFailure is TransactionPoisonedException).IsFalse();
        await Assert.That(rollbackFailure is TransactionPoisonedException).IsFalse();
        await Assert.That(readFailure.Message).Contains("already rolled back");
        await Assert.That(writeFailure.Message).Contains("already rolled back");
        await Assert.That(commitFailure.Message).Contains("already rolled back");
        await Assert.That(rollbackFailure.Message).Contains("already rolled back");
    }

    [Test]
    public async Task RawDatabaseAccess_RemainsOutsideManagedPoisonGates()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(412, "before");
        mutable["Value"] = "after";
        fixture.Scenario.EnqueueNonQueryFailure(
            new InjectedMutationException("raw escape characterization"));
        _ = Capture<InjectedMutationException>(() => transaction.Update(mutable));
        var managedFailure = Capture<TransactionPoisonedException>(transaction.Commit);

        var rawAffectedRows = transaction.DatabaseAccess.ExecuteNonQuery(
            new ScriptedDbCommand());
        transaction.DatabaseAccess.Commit();

        await Assert.That(managedFailure.Message).Contains(
            "Low-level DatabaseAccess and underlying IDbTransaction handles are outside this managed guard");
        await Assert.That(rawAffectedRows).IsEqualTo(1);
        await Assert.That(fixture.Scenario.NonQueryExecutions).IsEqualTo(2);
        await Assert.That(fixture.Scenario.Commits).IsEqualTo(1);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
    }

    [Test]
    public async Task PoisonedTransaction_FirstNullForeignKeyAccessIsRejected()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(414, "before");
        mutable["Value"] = "after";
        fixture.Scenario.EnqueueNonQueryFailure(
            new InjectedMutationException("null foreign key gate"));
        _ = Capture<InjectedMutationException>(() => transaction.Update(mutable));
        var foreignKey = new ImmutableForeignKey<TransactionMutationGuardOtherRow>(
            DataLinqKey.Null,
            transaction,
            property: null!);

        var observed = Capture<TransactionPoisonedException>(() => _ = foreignKey.Value);

        await AssertPoisonDiagnostic(observed, transaction.TransactionID);
        await Assert.That(fixture.Scenario.ReaderExecutions).IsEqualTo(0);
    }

    [Test]
    public async Task ExecutedStateChange_FreezesRelationImpactKeys()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(413, "before");
        mutable["Value"] = "executed";
        var stateChange = new StateChange(
            mutable,
            fixture.RowTable,
            TransactionChangeType.Update);
        var valueIndex = fixture.RowTable.ColumnIndices.Single(
            index => index.Name == "idx_transaction_mutation_guard_value");

        stateChange.ExecutePreflightedQuery(transaction);
        mutable["Value"] = "later";

        await Assert.That(stateChange.GetCurrentRelationKey(valueIndex).GetValue(0))
            .IsEqualTo("executed");
        await Assert.That(mutable["Value"]).IsEqualTo("later");
    }

    [Test]
    public async Task SuccessfulRelationKeyFinalization_UsesAuthoritativeReloadValues()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(416, "before");
        mutable["Value"] = "statement";
        var stateChange = new StateChange(
            mutable,
            fixture.RowTable,
            TransactionChangeType.Update);
        var valueIndex = fixture.RowTable.ColumnIndices.Single(
            index => index.Name == "idx_transaction_mutation_guard_value");

        stateChange.ExecutePreflightedQuery(transaction);
        var authoritative = fixture.CreateImmutable(416, "database-generated");
        stateChange.FinalizeSuccessfulRelationKeys(authoritative);
        mutable["Value"] = "later";

        await Assert.That(stateChange.GetCurrentRelationKey(valueIndex).GetValue(0))
            .IsEqualTo("database-generated");
        await Assert.That(mutable["Value"]).IsEqualTo("later");
    }

    [Test]
    public async Task DeleteImpactKey_UsesStoredBaselineInsteadOfUnsavedMutableValue()
    {
        using var fixture = new ScriptedFixture();
        using var transaction = fixture.Database.Transaction();
        var mutable = fixture.CreateExistingMutable(417, "stored");
        mutable["Value"] = "unsaved";
        var valueIndex = fixture.RowTable.ColumnIndices.Single(
            index => index.Name == "idx_transaction_mutation_guard_value");

        transaction.Delete(mutable);
        var stateChange = transaction.Changes.Single();

        await Assert.That(stateChange.GetCurrentRelationKey(valueIndex).GetValue(0))
            .IsEqualTo("stored");
        await Assert.That(mutable["Value"]).IsEqualTo("unsaved");
    }

    private static async Task AssertPoisoned(
        Transaction transaction,
        TransactionFailureStage expectedStage,
        Exception expectedCause)
    {
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(transaction.Failure).IsNotNull();
        await Assert.That(transaction.Failure!.Stage).IsEqualTo(expectedStage);
        await Assert.That(transaction.Failure.Cause).IsSameReferenceAs(expectedCause);
    }

    private static async Task AssertInvalidMutation(
        Mutable<TransactionMutationGuardRow> mutable,
        string expectedValue)
    {
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
        await Assert.That(mutable["Value"]).IsEqualTo(expectedValue);
        await Assert.That(mutable.GetChanges()).IsNotEmpty();
    }

    private static async Task AssertPoisonDiagnostic(
        TransactionPoisonedException exception,
        uint transactionId)
    {
        await Assert.That(exception.Message).Contains($"transaction {transactionId}");
        await Assert.That(exception.Message).Contains("Rollback() or Dispose()");
        await Assert.That(exception.Message).DoesNotContain("Data Source=");
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

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class ScriptedFixture : IDisposable
    {
        internal ScriptedFixture()
        {
            Scenario = new ScriptedMutationScenario();
            Provider = new ScriptedMutationProvider(Scenario);
            Database = new ScriptedDatabase(Provider);
            RowTable = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardRow)).Table;
            BinaryTable = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardBinaryRow)).Table;
            AutoTable = Provider.Metadata.GetTableModel(typeof(TransactionMutationGuardAutoRow)).Table;
            RowCache = Provider.GetTableCache(RowTable);
        }

        internal ScriptedMutationScenario Scenario { get; }
        internal ScriptedMutationProvider Provider { get; }
        internal ScriptedDatabase Database { get; }
        internal TableDefinition RowTable { get; }
        internal TableDefinition BinaryTable { get; }
        internal TableDefinition AutoTable { get; }
        internal TableCache RowCache { get; }

        internal Mutable<TransactionMutationGuardRow> CreateExistingMutable(
            int id,
            string value) =>
            new(CreateImmutable(id, value));

        internal TransactionMutationGuardRow CreateImmutable(
            int id,
            string value) =>
            InstanceFactory.NewImmutableRow<TransactionMutationGuardRow>(
                new ScriptedRowData(RowTable, id, value),
                Provider.ReadOnlyAccess);

        internal Mutable<TransactionMutationGuardAutoRow> CreateNewAutoMutable(
            string value)
        {
            var mutable = new Mutable<TransactionMutationGuardAutoRow>();
            mutable["Value"] = value;
            return mutable;
        }

        internal Mutable<TransactionMutationGuardBinaryRow> CreateExistingBinaryMutable(
            byte[] id,
            byte[] payload) =>
            new(InstanceFactory.NewImmutableRow<TransactionMutationGuardBinaryRow>(
                new ScriptedBinaryRowData(BinaryTable, id, payload),
                Provider.ReadOnlyAccess));

        public void Dispose() => Database.Dispose();
    }

    private sealed class ScriptedDatabase(ScriptedMutationProvider provider)
        : Database<TransactionMutationGuardDb>(provider);

    private readonly record struct ScriptedCounts(
        int CommandCreations,
        int NonQueryExecutions,
        int ScalarExecutions,
        int ReaderExecutions,
        int Commits,
        int Rollbacks,
        int Disposals);

    private sealed class ScriptedMutationScenario
    {
        private readonly Queue<object> nonQuerySteps = [];

        internal Exception? CommandFailure { get; set; }
        internal Action? CommandCreated { get; set; }
        internal object? ScalarResult { get; set; } = 1L;
        internal int CommandCreations { get; set; }
        internal int NonQueryExecutions { get; set; }
        internal int ScalarExecutions { get; set; }
        internal int ReaderExecutions { get; set; }
        internal int Commits { get; set; }
        internal int Rollbacks { get; set; }
        internal int Disposals { get; set; }

        internal void EnqueueNonQueryResult(int result) => nonQuerySteps.Enqueue(result);
        internal void EnqueueNonQueryFailure(Exception exception) => nonQuerySteps.Enqueue(exception);

        internal int ExecuteNonQuery()
        {
            NonQueryExecutions++;
            if (nonQuerySteps.Count == 0)
                return 1;

            var step = nonQuerySteps.Dequeue();
            if (step is Exception exception)
                throw exception;

            return (int)step;
        }

        internal ScriptedCounts SnapshotCounts() =>
            new(
                CommandCreations,
                NonQueryExecutions,
                ScalarExecutions,
                ReaderExecutions,
                Commits,
                Rollbacks,
                Disposals);
    }

    private sealed class ScriptedMutationProvider : DatabaseProvider<TransactionMutationGuardDb>
    {
        private readonly ScriptedMutationScenario scenario;
        private readonly ScriptedDatabaseAccess databaseAccess;
        private readonly ScriptedWriter writer = new();

        internal ScriptedMutationProvider(ScriptedMutationScenario scenario)
            : base(
                "scripted-transaction-mutation-failure-tests",
                DatabaseType.SQLite,
                DataLinqLoggingConfiguration.NullConfiguration,
                "transaction-mutation-failure-tests")
        {
            this.scenario = scenario;
            databaseAccess = new ScriptedDatabaseAccess(this);
        }

        public override IDatabaseProviderConstants Constants { get; } = new ScriptedProviderConstants();
        public override DatabaseAccess DatabaseAccess => databaseAccess;

        public override IDbCommand ToDbCommand(IQuery query)
        {
            scenario.CommandCreations++;
            if (scenario.CommandFailure is not null)
                throw scenario.CommandFailure;

            var command = new ScriptedDbCommand();
            scenario.CommandCreated?.Invoke();
            return command;
        }

        public override IDataLinqDataWriter GetWriter() => writer;

        public override DatabaseTransaction GetNewDatabaseTransaction(TransactionType type) =>
            new ScriptedDatabaseTransaction(this, type, scenario);

        public override DatabaseTransaction AttachDatabaseTransaction(
            IDbTransaction dbTransaction,
            TransactionType type) =>
            throw new NotSupportedException();

        public override string GetLastIdQuery() => throw new NotSupportedException();
        public override string GetSqlForFunction(SqlFunctionType functionType, string columnName, object[]? arguments) => throw new NotSupportedException();
        public override string GetOperatorSql(Operator @operator) => throw new NotSupportedException();
        public override Sql GetParameter(Sql sql, string key, object? value) => throw new NotSupportedException();
        public override Sql GetParameterValue(Sql sql, string key) => throw new NotSupportedException();
        public override string GetParameterName(Operator relation, string[] key) => throw new NotSupportedException();
        public override Sql GetParameterComparison(Sql sql, string field, Operator @operator, string[] prefix) => throw new NotSupportedException();
        public override Sql GetLimitOffset(Sql sql, int? limit, int? offset) => throw new NotSupportedException();
        public override Sql GetTableName(Sql sql, string tableName, string? alias = null) => throw new NotSupportedException();
        public override Sql GetCreateSql() => throw new NotSupportedException();
        public override bool DatabaseExists(string? databaseName = null) => throw new NotSupportedException();
        public override bool TableExists(string tableName, string? databaseName = null) => throw new NotSupportedException();
        public override bool FileOrServerExists() => throw new NotSupportedException();
        public override IDbConnection GetDbConnection() => throw new NotSupportedException();
    }

    private sealed class ScriptedDatabaseAccess(IDatabaseProvider provider) : DatabaseAccess(provider)
    {
        public override IDataLinqDataReader ExecuteReader(IDbCommand command) => throw new NotSupportedException();
        public override IDataLinqDataReader ExecuteReader(string query) => throw new NotSupportedException();
        public override object? ExecuteScalar(IDbCommand command) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(IDbCommand command) => throw new NotSupportedException();
        public override object? ExecuteScalar(string query) => throw new NotSupportedException();
        public override T ExecuteScalar<T>(string query) => throw new NotSupportedException();
        public override int ExecuteNonQuery(IDbCommand command) => throw new NotSupportedException();
        public override int ExecuteNonQuery(string query) => throw new NotSupportedException();
    }

    private sealed class ScriptedProviderConstants : IDatabaseProviderConstants
    {
        public string ParameterSign => "@";
        public string LastInsertCommand => string.Empty;
        public string EscapeCharacter => "\"";
        public bool SupportsMultipleDatabases => false;
    }

    private sealed class ScriptedWriter : IDataLinqDataWriter
    {
        public object? ConvertValue(ColumnDefinition column, object? value) => value;
    }

    private sealed class ScriptedDbCommand : IDbCommand
    {
        [AllowNull]
        public string CommandText { get; set; } = "SCRIPTED MUTATION";
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
        public void Dispose() { }
    }

    private sealed class ScriptedDatabaseTransaction : DatabaseTransaction
    {
        private readonly ScriptedMutationScenario scenario;

        internal ScriptedDatabaseTransaction(
            IDatabaseProvider provider,
            TransactionType type,
            ScriptedMutationScenario scenario)
            : base(provider, type)
        {
            this.scenario = scenario;
            SetStatus(DatabaseTransactionStatus.Open);
        }

        public override IDataLinqDataReader ExecuteReader(IDbCommand command)
        {
            scenario.ReaderExecutions++;
            return EmptyReader.Instance;
        }

        public override IDataLinqDataReader ExecuteReader(string query)
        {
            scenario.ReaderExecutions++;
            return EmptyReader.Instance;
        }

        public override object? ExecuteScalar(IDbCommand command)
        {
            scenario.ScalarExecutions++;
            return scenario.ScalarResult;
        }

        public override T ExecuteScalar<T>(IDbCommand command) =>
            (T)Convert.ChangeType(ExecuteScalar(command)!, typeof(T));

        public override object? ExecuteScalar(string query)
        {
            scenario.ScalarExecutions++;
            return scenario.ScalarResult;
        }

        public override T ExecuteScalar<T>(string query) =>
            (T)Convert.ChangeType(ExecuteScalar(query)!, typeof(T));

        public override int ExecuteNonQuery(IDbCommand command) =>
            scenario.ExecuteNonQuery();

        public override int ExecuteNonQuery(string query) =>
            scenario.ExecuteNonQuery();

        public override void Commit()
        {
            scenario.Commits++;
            SetStatus(DatabaseTransactionStatus.Committed);
        }

        public override void Rollback()
        {
            scenario.Rollbacks++;
            SetStatus(DatabaseTransactionStatus.RolledBack);
        }

        public override void Dispose()
        {
            scenario.Disposals++;
            if (Status == DatabaseTransactionStatus.Open)
                SetStatus(DatabaseTransactionStatus.RolledBack);
        }
    }

    private sealed class ScriptedRowData(
        TableDefinition table,
        int id,
        string value) : IRowData
    {
        public TableDefinition Table { get; } = table;

        public object? this[ColumnDefinition column] =>
            column.DbName switch
            {
                "id" => id,
                "value" => value,
                _ => null
            };

        public object? this[int columnIndex] => this[Table.Columns[columnIndex]];
        public object? GetValue(ColumnDefinition column) => this[column];
        public object? GetValue(int columnIndex) => this[columnIndex];
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => this[column]);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
            GetColumnAndValues(Table.Columns);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(
                column,
                this[column]));
    }

    private sealed class ScriptedBinaryRowData(
        TableDefinition table,
        byte[] id,
        byte[] payload) : IRowData
    {
        public TableDefinition Table { get; } = table;

        public object? this[ColumnDefinition column] =>
            column.DbName switch
            {
                "id" => id,
                "payload" => payload,
                _ => null
            };

        public object? this[int columnIndex] => this[Table.Columns[columnIndex]];
        public object? GetValue(ColumnDefinition column) => this[column];
        public object? GetValue(int columnIndex) => this[columnIndex];
        public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => this[column]);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
            GetColumnAndValues(Table.Columns);
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
            IEnumerable<ColumnDefinition> columns) =>
            columns.Select(column => new KeyValuePair<ColumnDefinition, object?>(
                column,
                this[column]));
    }

    private sealed class EmptyReader : IDataLinqDataReader
    {
        internal static EmptyReader Instance { get; } = new();

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

    private sealed class ThrowingNotification(Exception exception) : ICacheNotification
    {
        internal int ClearCalls { get; private set; }

        public void Clear()
        {
            ClearCalls++;
            throw exception;
        }
    }

    private sealed class ReentrantTransactionNotification(
        Transaction<TransactionMutationGuardDb> transaction,
        Mutable<TransactionMutationGuardRow> mutable) : ICacheNotification
    {
        internal int ClearCalls { get; private set; }
        internal List<Exception> Failures { get; } = [];

        public void Clear()
        {
            ClearCalls++;
            Failures.Add(Capture<InvalidOperationException>(transaction.Commit));
            Failures.Add(Capture<InvalidOperationException>(() => _ = transaction.Query()));
            Failures.Add(Capture<InvalidOperationException>(() => transaction.Update(mutable)));
            Failures.Add(Capture<InvalidOperationException>(transaction.Rollback));
            Failures.Add(Capture<InvalidOperationException>(transaction.Dispose));
        }
    }

    private sealed class MutatingNotification(Action mutation) : ICacheNotification
    {
        internal int ClearCalls { get; private set; }

        public void Clear()
        {
            ClearCalls++;
            mutation();
        }
    }

    private sealed class InjectedMutationException(string stage)
        : Exception($"Injected mutation failure during {stage}.");
}
