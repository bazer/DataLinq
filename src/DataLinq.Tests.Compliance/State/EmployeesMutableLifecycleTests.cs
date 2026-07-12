using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class EmployeesMutableLifecycleTests
{
    private readonly EmployeesTestData employees = new();

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task OriginCapture_PreservesExactProviderAndUnresolvedTransactionTokenAfterReadSourceNormalization(
        TestProviderDescriptor provider)
    {
        using var firstScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(OriginCapture_PreservesExactProviderAndUnresolvedTransactionTokenAfterReadSourceNormalization));
        using var secondScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(OriginCapture_PreservesExactProviderAndUnresolvedTransactionTokenAfterReadSourceNormalization));

        var committedEmployee = firstScope.Database.Query().Employees.OrderBy(x => x.emp_no).First();
        var committedMutable = committedEmployee.Mutate();

        await Assert.That(committedMutable.Lifecycle.RowKind).IsEqualTo(MutableRowKind.Existing);
        await Assert.That(committedMutable.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(committedMutable.Lifecycle.ProviderOwner)
            .IsSameReferenceAs(firstScope.Database.Provider);
        await Assert.That(committedMutable.Lifecycle.ProviderOwner)
            .IsNotSameReferenceAs(secondScope.Database.Provider);
        await Assert.That(committedMutable.Lifecycle.TransactionOwner).IsNull();

        using var transaction = firstScope.Database.Transaction();
        var transactionEmployee = transaction.Query().Employees
            .Single(x => x.emp_no == committedEmployee.emp_no);
        var capturedOrigin = ((IImmutableBaselineOrigin)transactionEmployee).BaselineOrigin;

        await Assert.That(capturedOrigin.ProviderOwner).IsSameReferenceAs(firstScope.Database.Provider);
        await Assert.That(capturedOrigin.TransactionOwner).IsSameReferenceAs(transaction.MutableOwnership);

        // Deliberately complete only the provider transaction. The public read-source API performs
        // its legacy terminal normalization, but mutable provenance must not infer a safe commit from
        // DatabaseTransactionStatus alone. Only Transaction.Commit() marks the ownership token after
        // cache publication and transaction-cache cleanup.
        transaction.DatabaseAccess.Commit();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.Unresolved);
        await Assert.That(transactionEmployee.GetReadSource())
            .IsSameReferenceAs(firstScope.Database.Provider.ReadOnlyAccess);

        var originAfterPublicNormalization =
            ((IImmutableBaselineOrigin)transactionEmployee).BaselineOrigin;
        var mutableAfterPublicNormalization = transactionEmployee.Mutate();

        await Assert.That(originAfterPublicNormalization.TransactionOwner)
            .IsSameReferenceAs(transaction.MutableOwnership);
        await Assert.That(mutableAfterPublicNormalization.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(mutableAfterPublicNormalization.Lifecycle.ProviderOwner)
            .IsSameReferenceAs(firstScope.Database.Provider);
        await Assert.That(mutableAfterPublicNormalization.Lifecycle.TransactionOwner)
            .IsSameReferenceAs(transaction.MutableOwnership);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task TransactionHydration_BindsInsertUpdateAndDeleteToExactTokenThenNormalizesAfterCommit(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(TransactionHydration_BindsInsertUpdateAndDeleteToExactTokenThenNormalizesAfterCommit));
        var database = databaseScope.Database;
        var existing = database.Insert(employees.NewEmployee(990001));
        var deletable = database.Insert(employees.NewEmployee(990002));
        var updateMutable = existing.Mutate();
        var equalUpdatePeer = existing.Mutate();
        var deleteMutable = deletable.Mutate();
        var equalDeletePeer = deletable.Mutate();
        var insertMutable = employees.NewEmployee();
        var updateHash = updateMutable.GetHashCode();
        var deleteHash = deleteMutable.GetHashCode();

        using var transaction = database.Transaction();

        updateMutable.first_name = "Tx update";
        var updated = transaction.Update(updateMutable);
        var inserted = transaction.Insert(insertMutable);
        transaction.Delete(deleteMutable);

        await AssertTransactionLocal(updateMutable.Lifecycle, database.Provider, transaction);
        await AssertTransactionLocal(insertMutable.Lifecycle, database.Provider, transaction);
        await AssertTransactionLocal(deleteMutable.Lifecycle, database.Provider, transaction);
        await Assert.That(deleteMutable.Lifecycle.RowKind).IsEqualTo(MutableRowKind.Deleted);
        await Assert.That(updateMutable.GetImmutableInstance()).IsSameReferenceAs(updated);
        await Assert.That(insertMutable.GetImmutableInstance()).IsSameReferenceAs(inserted);
        await Assert.That(updateMutable.GetChanges()).IsEmpty();
        await Assert.That(insertMutable.GetChanges()).IsEmpty();
        await Assert.That(insertMutable.IsNew()).IsFalse();
        await Assert.That(insertMutable.emp_no).IsNotNull();

        var deletedReset = Capture<InvalidOperationException>(deleteMutable.Reset);
        await Assert.That(deletedReset.Message).Contains("deleted");
        await Assert.That(deleteMutable.IsDeleted()).IsTrue();

        // Provenance is deliberately absent from equality and hashing. Persisted identity remains
        // the primary key even while the baseline is transaction-local or terminal-deleted.
        await Assert.That(updateMutable.Equals(equalUpdatePeer)).IsTrue();
        await Assert.That(updateMutable.GetHashCode()).IsEqualTo(updateHash);
        await Assert.That(deleteMutable.Equals(equalDeletePeer)).IsTrue();
        await Assert.That(deleteMutable.GetHashCode()).IsEqualTo(deleteHash);

        transaction.Commit();

        await Assert.That(transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.Committed);
        await AssertCommitted(updateMutable.Lifecycle, database.Provider, MutableRowKind.Existing);
        await AssertCommitted(insertMutable.Lifecycle, database.Provider, MutableRowKind.Existing);
        await AssertCommitted(deleteMutable.Lifecycle, database.Provider, MutableRowKind.Deleted);
        await Assert.That(updateMutable.Equals(equalUpdatePeer)).IsTrue();
        await Assert.That(updateMutable.GetHashCode()).IsEqualTo(updateHash);
        await Assert.That(deleteMutable.Equals(equalDeletePeer)).IsTrue();
        await Assert.That(deleteMutable.GetHashCode()).IsEqualTo(deleteHash);

        var persistedUpdate = database.Query().Employees.Single(x => x.emp_no == existing.emp_no);
        await Assert.That(persistedUpdate.first_name).IsEqualTo("Tx update");
        await Assert.That(database.Query().Employees.Any(x => x.emp_no == inserted.emp_no)).IsTrue();
        await Assert.That(database.Query().Employees.Any(x => x.emp_no == deletable.emp_no)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public Task OwnedRollback_InvalidatesInsertUpdateDeleteAndPreservesCommittedState(
        TestProviderDescriptor provider) =>
        AssertOwnedUncommittedFinalization(
            provider,
            nameof(OwnedRollback_InvalidatesInsertUpdateDeleteAndPreservesCommittedState),
            idBase: 990011,
            disposeOpenTransaction: false);

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public Task OwnedOpenDispose_InvalidatesInsertUpdateDeleteAndPreservesCommittedState(
        TestProviderDescriptor provider) =>
        AssertOwnedUncommittedFinalization(
            provider,
            nameof(OwnedOpenDispose_InvalidatesInsertUpdateDeleteAndPreservesCommittedState),
            idBase: 990014,
            disposeOpenTransaction: true);

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task OwnedRollback_DiscardsTransactionScopedSubscriber(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(OwnedRollback_DiscardsTransactionScopedSubscriber),
            EmployeesSeedMode.Bogus);
        var database = databaseScope.Database;
        var original = database.Insert(employees.NewEmployee(990017));
        var later = database.Insert(employees.NewEmployee(990018));
        var employeeCache = GetEmployeeCache(database);
        var transactionScoped = new CountingNotification();
        var committedScoped = new CountingNotification();

        using (var transaction = database.Transaction())
        {
            var mutable = original.Mutate();
            mutable.first_name = "tx pending";
            transaction.Update(mutable);
            employeeCache.SubscribeToChanges(transactionScoped, transaction);
            employeeCache.SubscribeToChanges(committedScoped);

            transaction.Rollback();

            await Assert.That(transactionScoped.ClearCalls).IsEqualTo(0);
            await Assert.That(committedScoped.ClearCalls).IsEqualTo(0);
        }

        var laterMutable = later.Mutate();
        laterMutable.first_name = "committed";
        database.Update(laterMutable);

        await Assert.That(transactionScoped.ClearCalls).IsEqualTo(0);
        await Assert.That(committedScoped.ClearCalls).IsEqualTo(1);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task AttachedExternallyCommittedThenWrapperRollback_ReportsOutcomeUnknown(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(AttachedExternallyCommittedThenWrapperRollback_ReportsOutcomeUnknown),
            EmployeesSeedMode.Bogus);
        var database = databaseScope.Database;
        using IDbConnection connection = database.Provider.GetDbConnection();
        connection.Open();
        using var externalTransaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        var transaction = database.AttachTransaction(externalTransaction);
        Exception? disposeFailure = null;

        try
        {
            var transactionEmployee = transaction.Query().Employees.OrderBy(x => x.emp_no).First();
            var transactionMutable = transactionEmployee.Mutate();
            var employeeCache = GetEmployeeCache(database);

            externalTransaction.Commit();

            var failure = Capture<Exception>(transaction.Rollback);

            await Assert.That(transaction.MutableOwnership.Outcome)
                .IsEqualTo(MutableTransactionOutcome.RollbackOutcomeUnknown);
            await AssertInvalid(
                transactionMutable.Lifecycle,
                MutableInvalidationReason.RollbackOutcomeUnknown,
                MutableRowKind.Existing);
            await Assert.That(employeeCache.IsTransactionInCache(transaction)).IsFalse();
            await Assert.That(failure.Data["DataLinq.MutableInvalidationReason"])
                .IsEqualTo(MutableInvalidationReason.RollbackOutcomeUnknown.ToString());

            var fallbackFailure = Capture<InvalidOperationException>(() =>
                _ = transactionEmployee.GetReadSource());
            await Assert.That(fallbackFailure.Message).Contains("fresh committed");
        }
        finally
        {
            try
            {
                transaction.Dispose();
            }
            catch (Exception exception)
            {
                // Some adapters keep the completed external transaction attached and reject the
                // wrapper's disposal-time rollback. That provider failure is legal, but it must
                // not replace the already established outcome-unknown ownership classification.
                disposeFailure = exception;
            }
        }

        await Assert.That(transaction.IsDisposed).IsTrue();
        await Assert.That(transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.RollbackOutcomeUnknown);
        if (disposeFailure is not null)
        {
            await Assert.That(disposeFailure.Data["DataLinq.MutableInvalidationReason"])
                .IsEqualTo(MutableInvalidationReason.RollbackOutcomeUnknown.ToString());
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Reset_PreservesTransactionProvenanceAndRejectsAReplacementOwner(
        TestProviderDescriptor provider)
    {
        using var firstScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Reset_PreservesTransactionProvenanceAndRejectsAReplacementOwner));
        using var secondScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Reset_PreservesTransactionProvenanceAndRejectsAReplacementOwner));

        var committedFirst = firstScope.Database.Query().Employees.OrderBy(x => x.emp_no).First();
        var committedSecondProvider = secondScope.Database.Query().Employees
            .Single(x => x.emp_no == committedFirst.emp_no);

        using var transaction = firstScope.Database.Transaction();
        var transactionEmployee = transaction.Query().Employees
            .Single(x => x.emp_no == committedFirst.emp_no);
        var transactionMutable = transactionEmployee.Mutate();
        var originalFirstName = transactionMutable.first_name;
        var owner = transactionMutable.Lifecycle.TransactionOwner;

        transactionMutable.first_name = "Discard this assignment";
        transactionMutable.Reset();

        await Assert.That(transactionMutable.first_name).IsEqualTo(originalFirstName);
        await Assert.That(transactionMutable.GetChanges()).IsEmpty();
        await Assert.That(transactionMutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(transactionMutable.Lifecycle.TransactionOwner).IsSameReferenceAs(owner);

        var transactionOwnerReplacement = Capture<InvalidOperationException>(
            () => transactionMutable.Reset(committedFirst));

        await Assert.That(transactionOwnerReplacement.Message).Contains("another owner");
        await Assert.That(transactionMutable.Lifecycle.TransactionOwner).IsSameReferenceAs(owner);

        var committedMutable = committedFirst.Mutate();
        var committedOwnerReplacement = Capture<InvalidOperationException>(
            () => committedMutable.Reset(committedSecondProvider));

        await Assert.That(committedOwnerReplacement.Message).Contains("another data source");
        await Assert.That(committedMutable.Lifecycle.ProviderOwner)
            .IsSameReferenceAs(firstScope.Database.Provider);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task InvalidLifecycle_PublicResetCannotClearReasonChangesOrPersistedIdentity(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(InvalidLifecycle_PublicResetCannotClearReasonChangesOrPersistedIdentity));
        var employee = databaseScope.Database.Query().Employees.OrderBy(x => x.emp_no).First();
        var invalid = employee.Mutate();
        var equalPeer = employee.Mutate();
        var originalHash = invalid.GetHashCode();

        invalid.first_name = "Keep for diagnostics";
        invalid.Invalidate(MutableInvalidationReason.MutationFailed);

        var assignmentReset = Capture<InvalidOperationException>(invalid.Reset);
        var baselineReset = Capture<InvalidOperationException>(() => invalid.Reset(employee));

        await Assert.That(assignmentReset.Message).Contains("MutationFailed");
        await Assert.That(baselineReset.Message).Contains("MutationFailed");
        await Assert.That(invalid.Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(invalid.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
        await Assert.That(invalid.GetChanges()).IsNotEmpty();
        await Assert.That(invalid.first_name).IsEqualTo("Keep for diagnostics");
        await Assert.That(invalid.Equals(equalPeer)).IsTrue();
        await Assert.That(invalid.GetHashCode()).IsEqualTo(originalHash);
    }

    private static async Task AssertTransactionLocal(
        MutableLifecycleSnapshot snapshot,
        DataLinq.Interfaces.IDatabaseProvider provider,
        Transaction transaction)
    {
        await Assert.That(snapshot.BaselineKind).IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(snapshot.ProviderOwner).IsSameReferenceAs(provider);
        await Assert.That(snapshot.TransactionOwner).IsSameReferenceAs(transaction.MutableOwnership);
        await Assert.That(snapshot.InvalidationReason).IsNull();
    }

    private static async Task AssertCommitted(
        MutableLifecycleSnapshot snapshot,
        DataLinq.Interfaces.IDatabaseProvider provider,
        MutableRowKind rowKind)
    {
        await Assert.That(snapshot.RowKind).IsEqualTo(rowKind);
        await Assert.That(snapshot.BaselineKind).IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(snapshot.ProviderOwner).IsSameReferenceAs(provider);
        await Assert.That(snapshot.TransactionOwner).IsNull();
        await Assert.That(snapshot.InvalidationReason).IsNull();
    }

    private async Task AssertOwnedUncommittedFinalization(
        TestProviderDescriptor provider,
        string testName,
        int idBase,
        bool disposeOpenTransaction)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            testName,
            EmployeesSeedMode.Bogus);
        var database = databaseScope.Database;
        var updateId = idBase;
        var deleteId = idBase + 1;
        var insertId = idBase + 2;
        database.Insert(employees.NewEmployee(updateId));
        database.Insert(employees.NewEmployee(deleteId));
        database.Provider.State.ClearCache();

        var committedUpdate = database.Query().Employees.Single(x => x.emp_no == updateId);
        var committedDelete = database.Query().Employees.Single(x => x.emp_no == deleteId);
        var originalFirstName = committedUpdate.first_name;
        var updateMutable = committedUpdate.Mutate();
        var deleteMutable = committedDelete.Mutate();
        var insertMutable = employees.NewEmployee(insertId);
        var employeeCache = GetEmployeeCache(database);
        var transaction = database.Transaction();
        RollbackObservation? observation = null;
        var expectedReason = disposeOpenTransaction
            ? MutableInvalidationReason.OpenTransactionDisposed
            : MutableInvalidationReason.RolledBack;
        var expectedOutcome = disposeOpenTransaction
            ? MutableTransactionOutcome.OpenTransactionDisposed
            : MutableTransactionOutcome.RolledBack;

        try
        {
            updateMutable.first_name = disposeOpenTransaction
                ? "tx disposed"
                : "tx rollback";
            transaction.Update(updateMutable);
            transaction.Insert(insertMutable);
            transaction.Delete(deleteMutable);

            transaction.OnStatusChanged += (_, args) =>
            {
                if (args.Status != DatabaseTransactionStatus.RolledBack)
                    return;

                observation = new RollbackObservation(
                    transaction.MutableOwnership.Outcome,
                    transaction.TouchedMutables.Count,
                    employeeCache.IsTransactionInCache(transaction),
                    updateMutable.Lifecycle,
                    insertMutable.Lifecycle,
                    deleteMutable.Lifecycle,
                    transaction.IsDisposed);
            };

            if (disposeOpenTransaction)
                transaction.Dispose();
            else
                transaction.Rollback();

            await Assert.That(transaction.Status)
                .IsEqualTo(DatabaseTransactionStatus.RolledBack);
            await Assert.That(transaction.MutableOwnership.Outcome)
                .IsEqualTo(expectedOutcome);
            await Assert.That(transaction.TouchedMutables).IsEmpty();
            await Assert.That(employeeCache.IsTransactionInCache(transaction)).IsFalse();
            await Assert.That(observation).IsNotNull();
            var finalizedObservation = observation ??
                throw new InvalidOperationException("The rolled-back status callback did not run.");
            await Assert.That(finalizedObservation.OwnershipOutcome).IsEqualTo(expectedOutcome);
            await Assert.That(finalizedObservation.TouchedMutableCount).IsEqualTo(0);
            await Assert.That(finalizedObservation.TransactionCachePresent).IsFalse();
            await Assert.That(finalizedObservation.WrapperDisposed).IsEqualTo(disposeOpenTransaction);
            await AssertInvalid(
                finalizedObservation.UpdateLifecycle,
                expectedReason,
                MutableRowKind.Existing);
            await AssertInvalid(
                finalizedObservation.InsertLifecycle,
                expectedReason,
                MutableRowKind.Existing);
            await AssertInvalid(
                finalizedObservation.DeleteLifecycle,
                expectedReason,
                MutableRowKind.Deleted);

            await AssertInvalid(updateMutable.Lifecycle, expectedReason, MutableRowKind.Existing);
            await AssertInvalid(insertMutable.Lifecycle, expectedReason, MutableRowKind.Existing);
            await AssertInvalid(deleteMutable.Lifecycle, expectedReason, MutableRowKind.Deleted);

            var resetFailure = Capture<InvalidOperationException>(deleteMutable.Reset);
            await Assert.That(resetFailure.Message).Contains(expectedReason.ToString());

            using var explicitRetry = database.Transaction();
            var explicitFailures = new[]
            {
                Capture<MutationGuardException>(() => explicitRetry.Update(updateMutable)),
                Capture<MutationGuardException>(() => explicitRetry.Insert(insertMutable)),
                Capture<MutationGuardException>(() => explicitRetry.Delete(deleteMutable))
            };
            var implicitFailures = new[]
            {
                Capture<MutationGuardException>(() => database.Update(updateMutable)),
                Capture<MutationGuardException>(() => database.Insert(insertMutable)),
                Capture<MutationGuardException>(() => database.Delete(deleteMutable))
            };

            foreach (var failure in explicitFailures.Concat(implicitFailures))
                await Assert.That(failure.Message).Contains("Materialize a fresh committed row");

            await Assert.That(employeeCache.IsTransactionInCache(explicitRetry)).IsFalse();

            var persistedUpdate = database.Query().Employees.Single(x => x.emp_no == updateId);
            var persistedDelete = database.Query().Employees.Single(x => x.emp_no == deleteId);
            await Assert.That(persistedUpdate.first_name).IsEqualTo(originalFirstName);
            await Assert.That(ReferenceEquals(committedUpdate, persistedUpdate)).IsTrue();
            await Assert.That(ReferenceEquals(committedDelete, persistedDelete)).IsTrue();
            await Assert.That(database.Query().Employees.Any(x => x.emp_no == insertId))
                .IsFalse();
        }
        finally
        {
            transaction.Dispose();
        }
    }

    private static async Task AssertInvalid(
        MutableLifecycleSnapshot snapshot,
        MutableInvalidationReason reason,
        MutableRowKind rowKind)
    {
        await Assert.That(snapshot.RowKind).IsEqualTo(rowKind);
        await Assert.That(snapshot.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(snapshot.TransactionOwner).IsNull();
        await Assert.That(snapshot.InvalidationReason).IsEqualTo(reason);
    }

    private static TableCache GetEmployeeCache(Database<EmployeesDb> database) =>
        database.Provider.GetTableCache(
            database.Provider.Metadata.GetTableModel(typeof(Employee)).Table);

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

    private sealed record RollbackObservation(
        MutableTransactionOutcome OwnershipOutcome,
        int TouchedMutableCount,
        bool TransactionCachePresent,
        MutableLifecycleSnapshot UpdateLifecycle,
        MutableLifecycleSnapshot InsertLifecycle,
        MutableLifecycleSnapshot DeleteLifecycle,
        bool WrapperDisposed);

    private sealed class CountingNotification : ICacheNotification
    {
        internal int ClearCalls { get; private set; }

        public void Clear() => ClearCalls++;
    }
}
