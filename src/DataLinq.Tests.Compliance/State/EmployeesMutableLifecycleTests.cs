using System;
using System.Linq;
using System.Threading.Tasks;
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
}
