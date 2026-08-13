using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
using TUnit.Core;

namespace DataLinq.Tests.Compliance;

[ParallelLimiter<EmployeesTransactionLifecycleParallelLimit>]
public sealed class EmployeesMutationGuardTests
{
    private readonly EmployeesTestData employees = new();

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CrossProviderGuards_RejectUpdateSaveAndImmutableDeleteBeforeTargetStateChanges(
        TestProviderDescriptor provider)
    {
        const int employeeNumber = 997101;
        const string sourceFirstName = "Source";
        const string targetFirstName = "Target";
        const string updateSecret = "cross-provider-update-secret";
        const string saveSecret = "cross-provider-save-secret";

        using var sourceScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(CrossProviderGuards_RejectUpdateSaveAndImmutableDeleteBeforeTargetStateChanges));
        using var targetScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(CrossProviderGuards_RejectUpdateSaveAndImmutableDeleteBeforeTargetStateChanges));

        var sourceDatabase = sourceScope.Database;
        var targetDatabase = targetScope.Database;
        var sourceNew = employees.NewEmployee(employeeNumber);
        sourceNew.first_name = sourceFirstName;
        var sourceEmployee = sourceDatabase.Insert(sourceNew);
        var targetNew = employees.NewEmployee(employeeNumber);
        targetNew.first_name = targetFirstName;
        var targetEmployee = targetDatabase.Insert(targetNew);

        await Assert.That(sourceDatabase.Provider).IsNotSameReferenceAs(targetDatabase.Provider);

        var updateMutable = sourceEmployee.Mutate();
        updateMutable.first_name = updateSecret;
        var updateLifecycle = updateMutable.Lifecycle;
        var saveMutable = sourceEmployee.Mutate();
        saveMutable.last_name = saveSecret;
        var saveLifecycle = saveMutable.Lifecycle;

        using var targetTransaction = targetDatabase.Transaction();
        var updateException = Capture<MutationGuardException>(
            () => targetTransaction.Update(updateMutable));
        var saveException = Capture<MutationGuardException>(
            () => targetTransaction.Save(saveMutable));
        var deleteException = Capture<MutationGuardException>(
            () => targetTransaction.Delete(sourceEmployee));

        await Assert.That(targetTransaction.Changes).IsEmpty();
        await Assert.That(targetTransaction.Status).IsEqualTo(DatabaseTransactionStatus.Closed);
        await Assert.That(updateMutable.Lifecycle).IsEqualTo(updateLifecycle);
        await Assert.That(saveMutable.Lifecycle).IsEqualTo(saveLifecycle);
        await Assert.That(updateMutable.first_name).IsEqualTo(updateSecret);
        await Assert.That(saveMutable.last_name).IsEqualTo(saveSecret);
        await Assert.That(updateMutable.GetChanges()).Count().IsEqualTo(1);
        await Assert.That(saveMutable.GetChanges()).Count().IsEqualTo(1);

        await AssertCrossProviderDiagnostic(
            updateException,
            updateSecret,
            targetScope.Connection.ConnectionString);
        await AssertCrossProviderDiagnostic(
            saveException,
            saveSecret,
            targetScope.Connection.ConnectionString);
        await AssertCrossProviderDiagnostic(
            deleteException,
            sourceFirstName,
            targetScope.Connection.ConnectionString);

        targetTransaction.Commit();
        targetDatabase.Provider.State.ClearCache();
        sourceDatabase.Provider.State.ClearCache();

        var persistedTarget = targetDatabase.Query().Employees
            .Single(x => x.emp_no == employeeNumber);
        var persistedSource = sourceDatabase.Query().Employees
            .Single(x => x.emp_no == employeeNumber);

        await Assert.That(persistedTarget.first_name).IsEqualTo(targetFirstName);
        await Assert.That(persistedTarget.last_name).IsEqualTo(targetEmployee.last_name);
        await Assert.That(persistedSource.first_name).IsEqualTo(sourceFirstName);
        await Assert.That(persistedSource.last_name).IsEqualTo(sourceEmployee.last_name);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CrossTransactionGuards_RejectDirtyCleanAndImplicitReuseButAllowOwningTransaction(
        TestProviderDescriptor provider)
    {
        const int employeeNumber = 997102;
        const string firstWrite = "Owner write";
        const string pendingSecret = "Owner last";

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(CrossTransactionGuards_RejectDirtyCleanAndImplicitReuseButAllowOwningTransaction));
        var database = databaseScope.Database;
        var employee = database.Insert(employees.NewEmployee(employeeNumber));
        var mutable = employee.Mutate();
        var table = database.Provider.Metadata.GetTableModel(typeof(Employee)).Table;
        var cache = database.Provider.GetTableCache(table);

        using var owningTransaction = database.Transaction();
        using var wrongTransaction = database.Transaction();

        mutable.first_name = firstWrite;
        _ = owningTransaction.Update(mutable);

        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(mutable.Lifecycle.TransactionOwner)
            .IsSameReferenceAs(owningTransaction.MutableOwnership);

        mutable.last_name = pendingSecret;
        var dirtyException = Capture<MutationGuardException>(
            () => wrongTransaction.Update(mutable));
        var implicitException = Capture<MutationGuardException>(
            () => database.Save(mutable));

        await Assert.That(wrongTransaction.Changes).IsEmpty();
        await Assert.That(wrongTransaction.Status).IsEqualTo(DatabaseTransactionStatus.Closed);
        await Assert.That(cache.IsTransactionInCache(wrongTransaction)).IsFalse();
        await Assert.That(mutable.last_name).IsEqualTo(pendingSecret);
        await Assert.That(mutable.GetChanges()).Count().IsEqualTo(1);
        await Assert.That(mutable.Lifecycle.TransactionOwner)
            .IsSameReferenceAs(owningTransaction.MutableOwnership);
        await AssertTransactionOwnerDiagnostic(dirtyException, pendingSecret);
        await AssertTransactionOwnerDiagnostic(implicitException, pendingSecret);

        var owningResult = owningTransaction.Update(mutable);

        await Assert.That(owningResult.first_name).IsEqualTo(firstWrite);
        await Assert.That(owningResult.last_name).IsEqualTo(pendingSecret);
        await Assert.That(mutable.GetChanges()).IsEmpty();
        await Assert.That(owningTransaction.Changes).Count().IsEqualTo(2);

        var cleanException = Capture<MutationGuardException>(
            () => wrongTransaction.Update(mutable));

        await Assert.That(wrongTransaction.Changes).IsEmpty();
        await Assert.That(wrongTransaction.Status).IsEqualTo(DatabaseTransactionStatus.Closed);
        await Assert.That(cache.IsTransactionInCache(wrongTransaction)).IsFalse();
        await Assert.That(mutable.GetChanges()).IsEmpty();
        await Assert.That(mutable.Lifecycle.TransactionOwner)
            .IsSameReferenceAs(owningTransaction.MutableOwnership);
        await AssertTransactionOwnerDiagnostic(cleanException, pendingSecret);

        wrongTransaction.Commit();
        owningTransaction.Commit();

        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(mutable.Lifecycle.TransactionOwner).IsNull();

        database.Provider.State.ClearCache();
        var persisted = database.Query().Employees.Single(x => x.emp_no == employeeNumber);
        await Assert.That(persisted.first_name).IsEqualTo(firstWrite);
        await Assert.That(persisted.last_name).IsEqualTo(pendingSecret);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PrimaryKeyGuards_RejectKeyOnlyAndKeyPlusValueWithoutChangingEitherRow(
        TestProviderDescriptor provider)
    {
        const int firstEmployeeNumber = 997201;
        const int secondEmployeeNumber = 997202;
        const string firstName = "Key source";
        const string secondName = "Key target";
        const string valueSecret = "primary-key-value-secret";

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(PrimaryKeyGuards_RejectKeyOnlyAndKeyPlusValueWithoutChangingEitherRow));
        var database = databaseScope.Database;
        var firstNew = employees.NewEmployee(firstEmployeeNumber);
        firstNew.first_name = firstName;
        var first = database.Insert(firstNew);
        var secondNew = employees.NewEmployee(secondEmployeeNumber);
        secondNew.first_name = secondName;
        var second = database.Insert(secondNew);
        var keyOnly = first.Mutate();
        keyOnly.emp_no = secondEmployeeNumber;
        var keyAndValue = first.Mutate();
        keyAndValue.emp_no = secondEmployeeNumber;
        keyAndValue.first_name = valueSecret;

        using var transaction = database.Transaction();
        var keyOnlyException = Capture<MutationGuardException>(
            () => transaction.Update(keyOnly));
        var keyAndValueException = Capture<MutationGuardException>(
            () => transaction.Save(keyAndValue));

        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Closed);
        await Assert.That(keyOnly.GetChanges()).Count().IsEqualTo(1);
        await Assert.That(keyAndValue.GetChanges()).Count().IsEqualTo(2);
        await AssertPrimaryKeyDiagnostic(keyOnlyException, valueSecret);
        await AssertPrimaryKeyDiagnostic(keyAndValueException, valueSecret);

        transaction.Commit();
        database.Provider.State.ClearCache();

        var persistedFirst = database.Query().Employees
            .Single(x => x.emp_no == firstEmployeeNumber);
        var persistedSecond = database.Query().Employees
            .Single(x => x.emp_no == secondEmployeeNumber);

        await Assert.That(persistedFirst.first_name).IsEqualTo(firstName);
        await Assert.That(persistedFirst.last_name).IsEqualTo(first.last_name);
        await Assert.That(persistedSecond.first_name).IsEqualTo(secondName);
        await Assert.That(persistedSecond.last_name).IsEqualTo(second.last_name);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ReadOnlyGuards_RejectAllMutationRoutesAndLeaveTheTransactionReadable(
        TestProviderDescriptor provider)
    {
        const int existingEmployeeNumber = 997301;
        const int insertEmployeeNumber = 997302;
        const int convenienceInsertEmployeeNumber = 997303;
        const string updateSecret = "read-only-update-secret";
        const string saveSecret = "read-only-save-secret";

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(ReadOnlyGuards_RejectAllMutationRoutesAndLeaveTheTransactionReadable));
        var database = databaseScope.Database;
        var existingNew = employees.NewEmployee(existingEmployeeNumber);
        existingNew.first_name = "Read baseline";
        var existing = database.Insert(existingNew);
        var insertMutable = employees.NewEmployee(insertEmployeeNumber);
        var updateMutable = existing.Mutate();
        updateMutable.first_name = updateSecret;
        var saveMutable = existing.Mutate();
        saveMutable.last_name = saveSecret;

        using var transaction = database.Transaction(TransactionType.ReadOnly);
        var insertException = Capture<MutationGuardException>(
            () => transaction.Insert(insertMutable));
        var updateException = Capture<MutationGuardException>(
            () => transaction.Update(updateMutable));
        var saveException = Capture<MutationGuardException>(
            () => transaction.Save(saveMutable));
        var deleteException = Capture<MutationGuardException>(
            () => transaction.Delete(existing));

        await Assert.That(transaction.Changes).IsEmpty();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Closed);
        await Assert.That(insertMutable.IsNew()).IsTrue();
        await Assert.That(updateMutable.first_name).IsEqualTo(updateSecret);
        await Assert.That(saveMutable.last_name).IsEqualTo(saveSecret);
        await AssertReadOnlyDiagnostic(insertException, updateSecret, saveSecret);
        await AssertReadOnlyDiagnostic(updateException, updateSecret, saveSecret);
        await AssertReadOnlyDiagnostic(saveException, updateSecret, saveSecret);
        await AssertReadOnlyDiagnostic(deleteException, updateSecret, saveSecret);

        var readBack = transaction.Query().Employees
            .Single(x => x.emp_no == existingEmployeeNumber);
        await Assert.That(readBack.first_name).IsEqualTo(existing.first_name);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        transaction.Commit();

        var convenienceMutable = employees.NewEmployee(convenienceInsertEmployeeNumber);
        var convenienceException = Capture<MutationGuardException>(
            () => database.Insert(convenienceMutable, TransactionType.ReadOnly));
        await AssertReadOnlyDiagnostic(convenienceException, updateSecret, saveSecret);
        await Assert.That(convenienceMutable.IsNew()).IsTrue();

        database.Provider.State.ClearCache();
        var persisted = database.Query().Employees
            .Single(x => x.emp_no == existingEmployeeNumber);
        await Assert.That(persisted.first_name).IsEqualTo(existing.first_name);
        await Assert.That(persisted.last_name).IsEqualTo(existing.last_name);
        await Assert.That(database.Query().Employees.Any(x => x.emp_no == insertEmployeeNumber))
            .IsFalse();
        await Assert.That(database.Query().Employees.Any(x => x.emp_no == convenienceInsertEmployeeNumber))
            .IsFalse();
    }

    private static async Task AssertCrossProviderDiagnostic(
        MutationGuardException exception,
        string forbiddenValue,
        string connectionString)
    {
        await Assert.That(exception.Message).Contains("different provider instance");
        await Assert.That(exception.Message).Contains("employees");
        await Assert.That(exception.Message).Contains("before provider command execution");
        await Assert.That(exception.Message).DoesNotContain(forbiddenValue);
        await Assert.That(exception.Message).DoesNotContain(connectionString);
    }

    private static async Task AssertTransactionOwnerDiagnostic(
        MutationGuardException exception,
        string forbiddenValue)
    {
        await Assert.That(exception.Message).Contains("unresolved transaction");
        await Assert.That(exception.Message).Contains("employees");
        await Assert.That(exception.Message).Contains("before provider command execution");
        await Assert.That(exception.Message).DoesNotContain(forbiddenValue);
    }

    private static async Task AssertPrimaryKeyDiagnostic(
        MutationGuardException exception,
        string forbiddenValue)
    {
        await Assert.That(exception.Message).Contains("Primary-key column(s) 'emp_no'");
        await Assert.That(exception.Message).Contains("before provider command execution");
        await Assert.That(exception.Message).DoesNotContain(forbiddenValue);
    }

    private static async Task AssertReadOnlyDiagnostic(
        MutationGuardException exception,
        params string[] forbiddenValues)
    {
        await Assert.That(exception.Message).Contains("read-only");
        await Assert.That(exception.Message).Contains("before provider command execution");
        foreach (var forbiddenValue in forbiddenValues)
            await Assert.That(exception.Message).DoesNotContain(forbiddenValue);
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
