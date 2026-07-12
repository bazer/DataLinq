using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
using TUnit.Core;

namespace DataLinq.Tests.Compliance;

[ParallelLimiter<EmployeesTransactionLifecycleParallelLimit>]
public sealed class EmployeesTransactionFailureTests
{
    private readonly EmployeesTestData employees = new();

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task ConstraintFailure_PoisonsAndRollsBackEarlierSuccessfulUpdate(
        TestProviderDescriptor provider)
    {
        const int updatedEmployeeNumber = 995101;
        const int duplicateEmployeeNumber = 995102;
        const int blockedEmployeeNumber = 995103;
        const int callbackEmployeeNumber = 995104;
        const string pendingFirstName = "Pending";
        const string duplicateFirstName = "Duplicate";

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(ConstraintFailure_PoisonsAndRollsBackEarlierSuccessfulUpdate),
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        var updatedEmployee = database.Insert(
            employees.NewEmployee(updatedEmployeeNumber));
        var duplicateTarget = database.Insert(
            employees.NewEmployee(duplicateEmployeeNumber));
        var originalUpdatedFirstName = updatedEmployee.first_name;
        var originalDuplicateFirstName = duplicateTarget.first_name;
        var updatedMutable = updatedEmployee.Mutate();
        var duplicateMutable = employees.NewEmployee(duplicateEmployeeNumber);
        duplicateMutable.first_name = duplicateFirstName;
        var table = database.Provider.Metadata.GetTableModel(typeof(Employee)).Table;
        var cacheSubscriber = new CountingCacheNotification();
        database.Provider.GetTableCache(table).SubscribeToChanges(cacheSubscriber);

        using var transaction = database.Transaction();

        updatedMutable.first_name = pendingFirstName;
        _ = transaction.Update(updatedMutable);

        var providerException = CaptureAny(
            () => transaction.Insert(duplicateMutable));

        await Assert.That(providerException is TransactionPoisonedException).IsFalse();
        await Assert.That(transaction.IsPoisoned).IsTrue();
        var failure = transaction.Failure ??
            throw new InvalidOperationException("The failed mutation did not poison the transaction.");
        await Assert.That(failure.Stage)
            .IsEqualTo(TransactionFailureStage.ProviderStatement);
        await Assert.That(failure.Cause).IsSameReferenceAs(providerException);
        await AssertSuccessfulChangeExactly(
            transaction,
            TransactionChangeType.Update,
            updatedMutable);
        await AssertTouchedExactly(transaction, updatedMutable);
        await AssertMutationFailed(updatedMutable);
        await AssertMutationFailed(duplicateMutable);
        await Assert.That(updatedMutable.first_name).IsEqualTo(pendingFirstName);
        await Assert.That(duplicateMutable.first_name).IsEqualTo(duplicateFirstName);
        await Assert.That(cacheSubscriber.ClearCount).IsEqualTo(0);

        var changesBeforeGates = transaction.SuccessfulChanges.ToArray();
        var touchedBeforeGates = transaction.TouchedMutables.ToArray();
        var callbackCalls = 0;

        var readException = Capture<TransactionPoisonedException>(
            () => transaction.Query().Employees.Any());
        var writeException = Capture<TransactionPoisonedException>(
            () => transaction.Insert(employees.NewEmployee(blockedEmployeeNumber)));
        var callbackException = Capture<TransactionPoisonedException>(
            () => transaction.Insert<Employee, MutableEmployee>(
                employees.NewEmployee(callbackEmployeeNumber),
                _ => callbackCalls++));
        var commitException = Capture<TransactionPoisonedException>(transaction.Commit);

        await AssertPoisonedDiagnostic(readException);
        await AssertPoisonedDiagnostic(writeException);
        await AssertPoisonedDiagnostic(callbackException);
        await AssertPoisonedDiagnostic(commitException);
        await Assert.That(callbackCalls).IsEqualTo(0);
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);
        await Assert.That(transaction.Failure).IsSameReferenceAs(failure);
        await Assert.That(transaction.SuccessfulChanges.Count)
            .IsEqualTo(changesBeforeGates.Length);
        await Assert.That(transaction.SuccessfulChanges[0])
            .IsSameReferenceAs(changesBeforeGates[0]);
        await Assert.That(transaction.TouchedMutables.Count)
            .IsEqualTo(touchedBeforeGates.Length);
        await Assert.That(transaction.TouchedMutables.Single())
            .IsSameReferenceAs(touchedBeforeGates[0]);
        await Assert.That(cacheSubscriber.ClearCount).IsEqualTo(0);

        transaction.Rollback();

        await Assert.That(transaction.Status)
            .IsEqualTo(DatabaseTransactionStatus.RolledBack);

        database.Provider.State.ClearCache();
        var persistedUpdated = database.Query().Employees
            .Single(x => x.emp_no == updatedEmployeeNumber);
        var persistedDuplicateRows = database.Query().Employees
            .Where(x => x.emp_no == duplicateEmployeeNumber)
            .ToList();

        await Assert.That(persistedUpdated.first_name)
            .IsEqualTo(originalUpdatedFirstName);
        await Assert.That(persistedDuplicateRows.Count).IsEqualTo(1);
        await Assert.That(persistedDuplicateRows[0].first_name)
            .IsEqualTo(originalDuplicateFirstName);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicChangesMutation_CannotAlterPrivateCommitAuthority(
        TestProviderDescriptor provider)
    {
        const int updatedEmployeeNumber = 995201;
        const int forgedEmployeeNumber = 995202;
        const string committedFirstName = "Authority";

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(PublicChangesMutation_CannotAlterPrivateCommitAuthority),
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        var updatedEmployee = database.Insert(
            employees.NewEmployee(updatedEmployeeNumber));
        var forgedEmployee = database.Insert(
            employees.NewEmployee(forgedEmployeeNumber));
        var forgedOriginalLastName = forgedEmployee.last_name;
        var updatedMutable = updatedEmployee.Mutate();
        var forgedMutable = forgedEmployee.Mutate();
        var table = database.Provider.Metadata.GetTableModel(typeof(Employee)).Table;
        var cache = database.Provider.GetTableCache(table);
        var updatedSubscriber = new CountingCacheNotification();
        var forgedSubscriber = new CountingCacheNotification();
        cache.SubscribeToChanges(
            updatedSubscriber,
            transaction: null,
            relationKey: null,
            loadedPrimaryKeys: [updatedEmployee.PrimaryKeys()]);
        cache.SubscribeToChanges(
            forgedSubscriber,
            transaction: null,
            relationKey: null,
            loadedPrimaryKeys: [forgedEmployee.PrimaryKeys()]);

        using var transaction = database.Transaction();

        updatedMutable.first_name = committedFirstName;
        _ = transaction.Update(updatedMutable);
        var successfulChange = transaction.SuccessfulChanges.Single();

        forgedMutable.last_name = "Forged";
        var forgedChange = new StateChange(
            forgedMutable,
            table,
            TransactionChangeType.Update);
        var publicProjection = transaction.Changes;
        publicProjection.Clear();
        publicProjection.Add(forgedChange);

        await Assert.That(publicProjection.Count).IsEqualTo(1);
        await Assert.That(publicProjection[0]).IsSameReferenceAs(forgedChange);
        await Assert.That(transaction.SuccessfulChanges.Count).IsEqualTo(1);
        await Assert.That(transaction.SuccessfulChanges[0])
            .IsSameReferenceAs(successfulChange);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(transaction.Changes[0]).IsSameReferenceAs(successfulChange);
        await AssertTouchedExactly(transaction, updatedMutable);
        await Assert.That(updatedSubscriber.ClearCount).IsEqualTo(0);
        await Assert.That(forgedSubscriber.ClearCount).IsEqualTo(0);

        transaction.Commit();

        await Assert.That(updatedSubscriber.ClearCount).IsEqualTo(1);
        await Assert.That(forgedSubscriber.ClearCount).IsEqualTo(0);

        database.Provider.State.ClearCache();
        var persistedUpdated = database.Query().Employees
            .Single(x => x.emp_no == updatedEmployeeNumber);
        var persistedForged = database.Query().Employees
            .Single(x => x.emp_no == forgedEmployeeNumber);

        await Assert.That(persistedUpdated.first_name)
            .IsEqualTo(committedFirstName);
        await Assert.That(persistedForged.last_name)
            .IsEqualTo(forgedOriginalLastName);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicStateChangeExecuteQuery_MutableDeleteUsesTransactionAuthority(
        TestProviderDescriptor provider)
    {
        const int employeeNumber = 995301;

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(PublicStateChangeExecuteQuery_MutableDeleteUsesTransactionAuthority),
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        var employee = database.Insert(employees.NewEmployee(employeeNumber));
        var mutable = employee.Mutate();
        var table = database.Provider.Metadata.GetTableModel(typeof(Employee)).Table;
        var stateChange = new StateChange(
            mutable,
            table,
            TransactionChangeType.Delete);

        using var transaction = database.Transaction();

        stateChange.ExecuteQuery(transaction);

        await Assert.That(transaction.SuccessfulChanges.Count).IsEqualTo(1);
        await Assert.That(transaction.SuccessfulChanges[0])
            .IsSameReferenceAs(stateChange);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(transaction.Changes[0])
            .IsSameReferenceAs(stateChange);
        await AssertTouchedExactly(transaction, mutable);
        await Assert.That(mutable.Lifecycle.RowKind)
            .IsEqualTo(MutableRowKind.Deleted);
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(mutable.Lifecycle.TransactionOwner)
            .IsSameReferenceAs(transaction.MutableOwnership);

        transaction.Rollback();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task PublicStateChangeExecuteQuery_AutoIncrementInsertHydratesAndCommitsAuthoritativeBaseline(
        TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(PublicStateChangeExecuteQuery_AutoIncrementInsertHydratesAndCommitsAuthoritativeBaseline),
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        var mutable = employees.NewEmployee();
        var table = database.Provider.Metadata.GetTableModel(typeof(Employee)).Table;
        var stateChange = new StateChange(
            mutable,
            table,
            TransactionChangeType.Insert);

        await Assert.That(mutable.HasPrimaryKeysSet()).IsFalse();
        await Assert.That(stateChange.PrimaryKeys.IsNull).IsTrue();

        using var transaction = database.Transaction();

        stateChange.ExecuteQuery(transaction);

        var generatedEmployeeNumber = mutable.emp_no ??
            throw new InvalidOperationException("The provider did not generate an employee number.");
        var generatedPrimaryKeys = mutable.PrimaryKeys();
        var authoritative = mutable.GetImmutableInstance() ??
            throw new InvalidOperationException("The insert did not hydrate an authoritative baseline.");

        await Assert.That(stateChange.PrimaryKeys).IsEqualTo(generatedPrimaryKeys);
        await Assert.That(authoritative.PrimaryKeys()).IsEqualTo(generatedPrimaryKeys);
        await Assert.That(authoritative.emp_no).IsEqualTo(generatedEmployeeNumber);
        await Assert.That(authoritative.first_name).IsEqualTo(mutable.first_name);
        await Assert.That(authoritative.last_name).IsEqualTo(mutable.last_name);
        await Assert.That(authoritative.GetReadSource()).IsSameReferenceAs(transaction);
        await Assert.That(mutable.GetChanges()).IsEmpty();
        await Assert.That(mutable.Lifecycle.RowKind)
            .IsEqualTo(MutableRowKind.Existing);
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(mutable.Lifecycle.TransactionOwner)
            .IsSameReferenceAs(transaction.MutableOwnership);
        await Assert.That(transaction.SuccessfulChanges.Count).IsEqualTo(1);
        await Assert.That(transaction.SuccessfulChanges[0])
            .IsSameReferenceAs(stateChange);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(transaction.Changes[0]).IsSameReferenceAs(stateChange);
        await AssertTouchedExactly(transaction, mutable);

        transaction.Commit();

        await Assert.That(mutable.Lifecycle.RowKind)
            .IsEqualTo(MutableRowKind.Existing);
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(mutable.Lifecycle.TransactionOwner).IsNull();

        database.Provider.State.ClearCache();
        var persisted = database.Query().Employees
            .Single(x => x.emp_no == generatedEmployeeNumber);

        await Assert.That(persisted.PrimaryKeys()).IsEqualTo(generatedPrimaryKeys);
        await Assert.That(persisted.first_name).IsEqualTo(mutable.first_name);
        await Assert.That(persisted.last_name).IsEqualTo(mutable.last_name);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SuccessfulIndexedUpdate_FreezesExecutedKeyAndIgnoresLaterUnexecutedValueOnCommit(
        TestProviderDescriptor provider)
    {
        const string departmentNumber = "d995";
        const string originalName = "Original index value";
        const string executedName = "Executed index value";
        const string laterUnexecutedName = "Later unexecuted value";

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(SuccessfulIndexedUpdate_FreezesExecutedKeyAndIgnoresLaterUnexecutedValueOnCommit),
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        var department = database.Insert(new MutableDepartment
        {
            DeptNo = departmentNumber,
            Name = originalName
        });
        var mutable = department.Mutate();
        var table = database.Provider.Metadata.GetTableModel(typeof(Department)).Table;
        var nameIndex = table.ColumnIndices.Single(index => index.Name == "dept_name");

        mutable.Name = executedName;
        var stateChange = new StateChange(
            mutable,
            table,
            TransactionChangeType.Update);

        using var transaction = database.Transaction();

        stateChange.ExecuteQuery(transaction);

        await Assert.That(stateChange.GetCurrentRelationKey(nameIndex).GetValue(0))
            .IsEqualTo(executedName);
        await Assert.That(mutable.GetChanges()).IsEmpty();
        await Assert.That(transaction.SuccessfulChanges.Count).IsEqualTo(1);
        await Assert.That(transaction.SuccessfulChanges[0])
            .IsSameReferenceAs(stateChange);
        await AssertTouchedExactly(transaction, mutable);

        mutable.Name = laterUnexecutedName;

        await Assert.That(mutable.Name).IsEqualTo(laterUnexecutedName);
        await Assert.That(mutable.GetChanges().Single().Value)
            .IsEqualTo(laterUnexecutedName);
        await Assert.That(stateChange.GetCurrentRelationKey(nameIndex).GetValue(0))
            .IsEqualTo(executedName);

        transaction.Commit();

        database.Provider.State.ClearCache();
        var persisted = database.Query().Departments
            .Single(x => x.DeptNo == departmentNumber);

        await Assert.That(persisted.Name).IsEqualTo(executedName);
        await Assert.That(persisted.Name).IsNotEqualTo(laterUnexecutedName);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NullForeignKey_FirstAccessAfterPoisoningRejectsThroughManagedReadGate(
        TestProviderDescriptor provider)
    {
        const int duplicateEmployeeNumber = 995401;

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(NullForeignKey_FirstAccessAfterPoisoningRejectsThroughManagedReadGate),
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        _ = database.Insert(employees.NewEmployee(duplicateEmployeeNumber));
        var departmentRelation = database.Provider.Metadata
            .GetTableModel(typeof(Manager))
            .Model
            .RelationProperties[nameof(Manager.Department)];

        using var transaction = database.Transaction();
        var nullForeignKey = new ImmutableForeignKey<Department>(
            DataLinqKey.Null,
            transaction,
            departmentRelation);
        var providerException = CaptureAny(
            () => transaction.Insert(employees.NewEmployee(duplicateEmployeeNumber)));

        await Assert.That(providerException is TransactionPoisonedException).IsFalse();
        await Assert.That(transaction.IsPoisoned).IsTrue();
        await Assert.That(transaction.Failure).IsNotNull();
        await Assert.That(transaction.Failure!.Stage)
            .IsEqualTo(TransactionFailureStage.ProviderStatement);
        await Assert.That(transaction.Failure.Cause)
            .IsSameReferenceAs(providerException);

        var readException = Capture<TransactionPoisonedException>(
            () => _ = nullForeignKey.Value);

        await AssertPoisonedDiagnostic(readException);
        await Assert.That(transaction.SuccessfulChanges).IsEmpty();
        await Assert.That(transaction.TouchedMutables).IsEmpty();

        transaction.Rollback();
    }

    private static async Task AssertSuccessfulChangeExactly(
        Transaction transaction,
        TransactionChangeType expectedType,
        IModelInstance expectedModel)
    {
        await Assert.That(transaction.SuccessfulChanges.Count).IsEqualTo(1);
        var successful = transaction.SuccessfulChanges[0];
        await Assert.That(successful.Type).IsEqualTo(expectedType);
        await Assert.That(successful.Model).IsSameReferenceAs(expectedModel);
        await Assert.That(transaction.Changes.Count).IsEqualTo(1);
        await Assert.That(transaction.Changes[0]).IsSameReferenceAs(successful);
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

    private static async Task AssertMutationFailed(MutableEmployee mutable)
    {
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.MutationFailed);
    }

    private static async Task AssertPoisonedDiagnostic(
        TransactionPoisonedException exception)
    {
        await Assert.That(exception.Message).Contains("poisoned");
        await Assert.That(exception.Message).Contains("Rollback()");
        await Assert.That(exception.Message).Contains("Dispose()");
    }

    private static Exception CaptureAny(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Expected an exception.");
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

    private sealed class CountingCacheNotification : ICacheNotification
    {
        internal int ClearCount { get; private set; }

        public void Clear() => ClearCount++;
    }
}
