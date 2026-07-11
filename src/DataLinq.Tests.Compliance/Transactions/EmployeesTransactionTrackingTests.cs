using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
using TUnit.Core;

namespace DataLinq.Tests.Compliance;

[ParallelLimiter<EmployeesTransactionLifecycleParallelLimit>]
public sealed class EmployeesTransactionTrackingTests
{
    private readonly EmployeesTestData employees = new();

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SuccessfulMutations_RecordOrderedChangesAndOnlyLifecycleMutableReferences(
        TestProviderDescriptor provider)
    {
        const int updateEmployeeNumber = 996101;
        const int deleteEmployeeNumber = 996102;
        const int insertEmployeeNumber = 996103;
        const int immutableDeleteEmployeeNumber = 996104;

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(SuccessfulMutations_RecordOrderedChangesAndOnlyLifecycleMutableReferences),
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        var existing = database.Insert(employees.NewEmployee(updateEmployeeNumber));
        var deletable = database.Insert(employees.NewEmployee(deleteEmployeeNumber));
        var immutableDeletable = database.Insert(
            employees.NewEmployee(immutableDeleteEmployeeNumber));
        var updateMutable = existing.Mutate();
        var insertMutable = employees.NewEmployee(insertEmployeeNumber);
        var deleteMutable = deletable.Mutate();

        using var transaction = database.Transaction();

        await Assert.That(transaction.Changes).IsEmpty();
        await AssertTouchedExactly(transaction);

        updateMutable.first_name = "Tracked update";
        _ = transaction.Update(updateMutable);

        await AssertChangeExactly(
            transaction,
            index: 0,
            TransactionChangeType.Update,
            updateMutable);
        await AssertTouchedExactly(transaction, updateMutable);

        _ = transaction.Update(updateMutable);

        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await AssertTouchedExactly(transaction, updateMutable);

        _ = transaction.Insert(insertMutable);

        await AssertChangeExactly(
            transaction,
            index: 1,
            TransactionChangeType.Insert,
            insertMutable);
        await AssertTouchedExactly(transaction, updateMutable, insertMutable);

        transaction.Delete(deleteMutable);

        await AssertChangeExactly(
            transaction,
            index: 2,
            TransactionChangeType.Delete,
            deleteMutable);
        await AssertTouchedExactly(
            transaction,
            updateMutable,
            insertMutable,
            deleteMutable);

        transaction.Delete(immutableDeletable);

        await AssertChangeExactly(
            transaction,
            index: 3,
            TransactionChangeType.Delete,
            immutableDeletable);
        await AssertTouchedExactly(
            transaction,
            updateMutable,
            insertMutable,
            deleteMutable);

        transaction.Rollback();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task EqualButDistinctMutables_AreBothTrackedByReferenceIdentity(
        TestProviderDescriptor provider)
    {
        const int employeeNumber = 996201;

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(EqualButDistinctMutables_AreBothTrackedByReferenceIdentity),
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        var employee = database.Insert(employees.NewEmployee(employeeNumber));
        var firstMutable = employee.Mutate();
        var secondMutable = employee.Mutate();

        await Assert.That(firstMutable.Equals(secondMutable)).IsTrue();
        await Assert.That(ReferenceEquals(firstMutable, secondMutable)).IsFalse();

        using var transaction = database.Transaction();

        firstMutable.first_name = "Ref one";
        _ = transaction.Update(firstMutable);

        secondMutable.last_name = "Ref two";
        _ = transaction.Update(secondMutable);

        await Assert.That(transaction.Changes.Count).IsEqualTo(2);
        await AssertChangeExactly(
            transaction,
            index: 0,
            TransactionChangeType.Update,
            firstMutable);
        await AssertChangeExactly(
            transaction,
            index: 1,
            TransactionChangeType.Update,
            secondMutable);
        await AssertTouchedExactly(transaction, firstMutable, secondMutable);

        var transactionEmployee = transaction.Query().Employees
            .Single(x => x.emp_no == employeeNumber);
        await Assert.That(transactionEmployee.first_name).IsEqualTo("Ref one");
        await Assert.That(transactionEmployee.last_name).IsEqualTo("Ref two");

        transaction.Rollback();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RepeatedSameMutable_RecordsMultipleChangesButOneTouchedReference(
        TestProviderDescriptor provider)
    {
        const int employeeNumber = 996301;

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(RepeatedSameMutable_RecordsMultipleChangesButOneTouchedReference),
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        var mutable = employees.NewEmployee(employeeNumber);

        using var transaction = database.Transaction();

        var inserted = transaction.Insert(mutable);

        await Assert.That(mutable.IsNew()).IsFalse();
        await Assert.That(mutable.GetHashCode()).IsEqualTo(mutable.PrimaryKeys().GetHashCode());
        await Assert.That(mutable.Equals(inserted)).IsTrue();
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await AssertChangeExactly(
            transaction,
            index: 0,
            TransactionChangeType.Insert,
            mutable);
        await AssertTouchedExactly(transaction, mutable);

        mutable.first_name = "Second write";
        _ = transaction.Update(mutable);

        await Assert.That(transaction.Changes.Count).IsEqualTo(2);
        await AssertChangeExactly(
            transaction,
            index: 0,
            TransactionChangeType.Insert,
            mutable);
        await AssertChangeExactly(
            transaction,
            index: 1,
            TransactionChangeType.Update,
            mutable);
        await AssertTouchedExactly(transaction, mutable);

        var transactionEmployee = transaction.Query().Employees
            .Single(x => x.emp_no == employeeNumber);
        await Assert.That(transactionEmployee.first_name).IsEqualTo("Second write");

        transaction.Rollback();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CleanNoChangeUpdate_RecordsNeitherChangeNorTouchedMutable(
        TestProviderDescriptor provider)
    {
        const int employeeNumber = 996401;

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(CleanNoChangeUpdate_RecordsNeitherChangeNorTouchedMutable),
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        var employee = database.Insert(employees.NewEmployee(employeeNumber));
        var mutable = employee.Mutate();
        var lifecycleBefore = mutable.Lifecycle;

        using var transaction = database.Transaction();

        var result = transaction.Update(mutable);

        await Assert.That(result.emp_no).IsEqualTo(employeeNumber);
        await Assert.That(transaction.Changes).IsEmpty();
        await AssertTouchedExactly(transaction);
        await Assert.That(mutable.GetChanges()).IsEmpty();
        await Assert.That(mutable.Lifecycle).IsEqualTo(lifecycleBefore);

        transaction.Rollback();
    }

    private static async Task AssertChangeExactly(
        Transaction transaction,
        int index,
        TransactionChangeType expectedType,
        IModelInstance expectedModel)
    {
        await Assert.That(transaction.Changes.Count).IsGreaterThan(index);
        var change = transaction.Changes[index];
        await Assert.That(change.Type).IsEqualTo(expectedType);
        await Assert.That(change.Model).IsSameReferenceAs(expectedModel);
    }

    private static async Task AssertTouchedExactly(
        Transaction transaction,
        params IMutableInstance[] expectedMutables)
    {
        var touched = transaction.TouchedMutables.ToArray();

        await Assert.That(touched.Length).IsEqualTo(expectedMutables.Length);
        foreach (var expected in expectedMutables)
        {
            var referenceMatches = touched.Count(
                actual => ReferenceEquals(actual, expected));
            await Assert.That(referenceMatches).IsEqualTo(1);
        }
    }
}
