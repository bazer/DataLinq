using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Mutation;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace DataLinq.Tests.Compliance;

[ParallelLimiter<EmployeesTransactionLifecycleParallelLimit>]
public class EmployeesTransactionLifecycleTests
{
    private readonly EmployeesTestData _employees = new();
    private const int ConcurrentTransactionCount = 4;

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_AttachExternalTransactionExposesUncommittedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_AttachExternalTransactionExposesUncommittedRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;

        using IDbConnection dbConnection = employeesDatabase.Provider.GetDbConnection();
        dbConnection.Open();
        using var dbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);

        var command = employeesDatabase
            .From("departments")
            .Set("dept_no", "d099")
            .Set("dept_name", "Transactions")
            .InsertQuery()
            .ToDbCommand();

        command.Connection = dbConnection;
        command.Transaction = dbTransaction;
        command.ExecuteNonQuery();

        using var transaction = employeesDatabase.AttachTransaction(dbTransaction);

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);

        var department = transaction.Query().Departments.Single(x => x.DeptNo == "d099");
        var outsideTransactionCount = employeesDatabase.Query().Departments.Count(x => x.DeptNo == "d099");

        await Assert.That(department.Name).IsEqualTo("Transactions");

        if (employeesDatabase.DatabaseType == DatabaseType.SQLite)
            await Assert.That(outsideTransactionCount).IsEqualTo(1);
        else
            await Assert.That(outsideTransactionCount).IsEqualTo(0);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_AttachExternalMutationAndCommit_PersistsChanges(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_AttachExternalMutationAndCommit_PersistsChanges),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employeeNumber = 999700;

        foreach (var existingEmployee in employeesDatabase.Query().Employees.Where(x => x.emp_no == employeeNumber).ToList())
            employeesDatabase.Delete(existingEmployee);

        var employee = employeesDatabase.Query().Employees.SingleOrDefault(x => x.emp_no == employeeNumber)?.Mutate()
            ?? _employees.NewEmployee(employeeNumber);
        employee.first_name = "Bob";
        employeesDatabase.Save(employee);

        var mutableEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber).Mutate();
        await Assert.That(mutableEmployee.first_name).IsEqualTo("Bob");

        using IDbConnection dbConnection = employeesDatabase.Provider.GetDbConnection();
        dbConnection.Open();
        using var dbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);
        using var transaction = employeesDatabase.AttachTransaction(dbTransaction);

        mutableEmployee.first_name = "Rick";
        transaction.Save(mutableEmployee);
        dbTransaction.Commit();
        transaction.Commit();

        var persistedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber);
        await Assert.That(persistedEmployee.first_name).IsEqualTo("Rick");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_DbCommandInsideTransactionReadsInsertedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_DbCommandInsideTransactionReadsInsertedRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;

        using var transaction = employeesDatabase.Transaction();
        transaction.Insert(new MutableDepartment
        {
            DeptNo = "d099",
            Name = "Transactions"
        });

        var command = employeesDatabase
            .From("departments")
            .Where("dept_no")
            .EqualTo("d099")
            .SelectQuery()
            .ToDbCommand();

        var dbTransaction = transaction.DatabaseAccess.DbTransaction;
        command.Connection = dbTransaction.Connection;
        command.Transaction = dbTransaction;

        using var reader = command.ExecuteReader();
        var rows = 0;

        while (reader.Read())
        {
            rows++;
            await Assert.That(reader.GetString(0)).IsEqualTo("d099");
            await Assert.That(reader.GetString(1)).IsEqualTo("Transactions");
        }

        await Assert.That(rows).IsEqualTo(1);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_InsertAutoIncrement_AssignsPrimaryKeyAndPersists(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_InsertAutoIncrement_AssignsPrimaryKeyAndPersists),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.NewEmployee();

        await Assert.That(employee.HasPrimaryKeysSet()).IsFalse();

        using (var transaction = employeesDatabase.Transaction())
        {
            transaction.Insert(employee);

            await Assert.That(employee.emp_no).IsNotNull();
            await Assert.That(employee.HasPrimaryKeysSet()).IsTrue();

            var transactionEmployee = transaction.Query().Employees.Single(x => x.emp_no == employee.emp_no);
            await Assert.That(transactionEmployee.birth_date.ToShortDateString()).IsEqualTo(employee.birth_date.ToShortDateString());
            await Assert.That(transactionEmployee.HasPrimaryKeysSet()).IsTrue();

            transaction.Commit();
        }

        var persistedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employee.emp_no);
        await Assert.That(persistedEmployee.birth_date.ToShortDateString()).IsEqualTo(employee.birth_date.ToShortDateString());
        await Assert.That(persistedEmployee.HasPrimaryKeysSet()).IsTrue();

        employeesDatabase.Delete(persistedEmployee);
        await Assert.That(employeesDatabase.Query().Employees.Any(x => x.emp_no == employee.emp_no)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_InsertAndUpdateAutoIncrement_PersistsUpdatedValues(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_InsertAndUpdateAutoIncrement_PersistsUpdatedValues),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.NewEmployee();
        MutableEmployee mutableEmployee;

        using (var transaction = employeesDatabase.Transaction())
        {
            mutableEmployee = transaction.Insert(employee).Mutate();

            await Assert.That(employee.emp_no).IsNotNull();
            await Assert.That(employee.HasPrimaryKeysSet()).IsTrue();

            transaction.Commit();
        }

        mutableEmployee.birth_date = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));

        using (var transaction = employeesDatabase.Transaction())
        {
            transaction.Update(mutableEmployee);
            await Assert.That(mutableEmployee.HasPrimaryKeysSet()).IsTrue();
            transaction.Commit();
        }

        var persistedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employee.emp_no);
        await Assert.That(persistedEmployee.birth_date).IsEqualTo(mutableEmployee.birth_date);
        await Assert.That(persistedEmployee.HasPrimaryKeysSet()).IsTrue();

        employeesDatabase.Delete(persistedEmployee);
        await Assert.That(employeesDatabase.Query().Employees.Any(x => x.emp_no == employee.emp_no)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_UpdateWithoutChanges_ReturnsEquivalentModel(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_UpdateWithoutChanges_ReturnsEquivalentModel),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999795, employeesDatabase);
        var mutable = employee.Mutate();

        await Assert.That(mutable.GetChanges()).IsEmpty();

        var updatedEmployee = mutable.Update(employeesDatabase);
        await Assert.That(updatedEmployee).IsEqualTo(employee);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_ImplicitUpdate_PersistsChanges(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_ImplicitUpdate_PersistsChanges),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999998, employeesDatabase);
        var originalBirthDate = employee.birth_date;
        var mutable = employee.Mutate();
        var newBirthDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));

        mutable.birth_date = newBirthDate;
        var updatedEmployee = employeesDatabase.Update(mutable);
        var persistedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employee.emp_no);

        await Assert.That(ReferenceEquals(updatedEmployee, persistedEmployee)).IsFalse();
        await Assert.That(persistedEmployee.birth_date.ToShortDateString()).IsNotEqualTo(originalBirthDate.ToShortDateString());
        await Assert.That(persistedEmployee.birth_date.ToShortDateString()).IsEqualTo(mutable.birth_date.ToShortDateString());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_ExplicitUpdate_PersistsChangesAfterCommit(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_ExplicitUpdate_PersistsChangesAfterCommit),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999997, employeesDatabase);
        var originalBirthDate = employee.birth_date;
        var mutable = employee.Mutate();
        var newBirthDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));

        mutable.birth_date = newBirthDate;

        using var transaction = employeesDatabase.Transaction();
        var updatedEmployee = transaction.Update(mutable);
        var transactionEmployee = transaction.Query().Employees.Single(x => x.emp_no == employee.emp_no);

        await Assert.That(ReferenceEquals(updatedEmployee, transactionEmployee)).IsTrue();

        transaction.Commit();

        var persistedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employee.emp_no);

        await Assert.That(ReferenceEquals(updatedEmployee, persistedEmployee)).IsFalse();
        await Assert.That(persistedEmployee.birth_date.ToShortDateString()).IsNotEqualTo(originalBirthDate.ToShortDateString());
        await Assert.That(persistedEmployee.birth_date.ToShortDateString()).IsEqualTo(mutable.birth_date.ToShortDateString());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_Rollback_RestoresOriginalValues(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_Rollback_RestoresOriginalValues),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999996, employeesDatabase);
        var originalBirthDate = employee.birth_date;
        var mutable = employee.Mutate();
        var newBirthDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));

        mutable.birth_date = newBirthDate;

        using var transaction = employeesDatabase.Transaction();
        var updatedEmployee = transaction.Update(mutable);
        var transactionEmployee = transaction.Query().Employees.Single(x => x.emp_no == employee.emp_no);

        await Assert.That(ReferenceEquals(updatedEmployee, transactionEmployee)).IsTrue();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);

        transaction.Rollback();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.RolledBack);

        var persistedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employee.emp_no);

        await Assert.That(ReferenceEquals(updatedEmployee, persistedEmployee)).IsFalse();
        await Assert.That(persistedEmployee.birth_date.ToShortDateString()).IsNotEqualTo(mutable.birth_date.ToShortDateString());
        await Assert.That(persistedEmployee.birth_date.ToShortDateString()).IsEqualTo(originalBirthDate.ToShortDateString());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_DoubleCommitAndRollbackGuards_ThrowAfterCommit(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_DoubleCommitAndRollbackGuards_ThrowAfterCommit),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999995, employeesDatabase);
        var mutable = employee.Mutate();

        mutable.birth_date = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));

        using var transaction = employeesDatabase.Transaction();
        _ = transaction.Update(mutable);

        transaction.Commit();

        await AssertThrows<Exception>(() => transaction.Commit());
        await AssertThrows<Exception>(() => transaction.Rollback());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_DoubleRollbackAndCommitGuards_ThrowAfterRollback(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_DoubleRollbackAndCommitGuards_ThrowAfterRollback),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999994, employeesDatabase);
        var mutable = employee.Mutate();

        mutable.birth_date = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));

        using var transaction = employeesDatabase.Transaction();
        _ = transaction.Update(mutable);

        transaction.Rollback();

        await AssertThrows<Exception>(() => transaction.Rollback());
        await AssertThrows<Exception>(() => transaction.Commit());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_CacheIsIsolatedPerTransaction(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_CacheIsIsolatedPerTransaction),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999991, employeesDatabase);
        var transactions = new Transaction<EmployeesDb>[ConcurrentTransactionCount];

        try
        {
            for (var i = 0; i < transactions.Length; i++)
            {
                transactions[i] = employeesDatabase.Transaction(TransactionType.ReadAndWrite);
                var currentEmployee = transactions[i].Query().Employees.Single(x => x.emp_no == employee.emp_no);
                var sameTransactionEmployee = transactions[i].Query().Employees.Single(x => x.emp_no == employee.emp_no);

                await Assert.That(ReferenceEquals(currentEmployee, sameTransactionEmployee)).IsTrue();

                if (i > 0)
                {
                    var previousTransactionEmployee = transactions[i - 1].Query().Employees.Single(x => x.emp_no == employee.emp_no);
                    await Assert.That(ReferenceEquals(currentEmployee, previousTransactionEmployee)).IsFalse();
                }
            }
        }
        finally
        {
            foreach (var transaction in transactions.Where(x => x is not null))
                transaction.Dispose();
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_SaveShortcut_PersistsChanges(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_SaveShortcut_PersistsChanges),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999800, employeesDatabase).Mutate();
        var newBirthDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));

        var persistedEmployee = employee.Save(x => x.birth_date = newBirthDate, employeesDatabase);

        await Assert.That(persistedEmployee.emp_no).IsEqualTo(999800);
        await Assert.That(persistedEmployee.birth_date.ToShortDateString()).IsEqualTo(newBirthDate.ToShortDateString());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_InsertRelations_PersistsAfterCommit(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_InsertRelations_PersistsAfterCommit),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999799, employeesDatabase);

        foreach (var existingSalary in employee.salaries.ToList())
            employeesDatabase.Delete(existingSalary);

        using (var transaction = employeesDatabase.Transaction())
        {
            await Assert.That(employee.salaries).IsEmpty();

            var newSalary = new MutableSalaries
            {
                emp_no = employee.emp_no!.Value,
                salary = 50000,
                FromDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
                ToDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20))
            };

            transaction.Insert(newSalary);

            if (employeesDatabase.DatabaseType == DatabaseType.SQLite)
                await Assert.That(employee.salaries.Count).IsEqualTo(1);
            else
                await Assert.That(employee.salaries).IsEmpty();

            transaction.Commit();
        }

        await Assert.That(employee.salaries.Count).IsEqualTo(1);
        employeesDatabase.Delete(employee.salaries.First());
        await Assert.That(employee.salaries).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_InsertRelationsWithinTransaction_MaintainsGraphIdentity(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_InsertRelationsWithinTransaction_MaintainsGraphIdentity),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999798, employeesDatabase);

        foreach (var existingSalary in employee.salaries.ToList())
            employeesDatabase.Delete(existingSalary);

        using (var transaction = employeesDatabase.Transaction())
        {
            var transactionEmployee = transaction.Query().Employees.Single(x => x.emp_no == employee.emp_no);
            await Assert.That(transactionEmployee.salaries).IsEmpty();

            var newSalary = new MutableSalaries
            {
                emp_no = transactionEmployee.emp_no!.Value,
                salary = 50000,
                FromDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
                ToDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20))
            };

            var insertedSalary = transaction.Insert(newSalary);

            await Assert.That(insertedSalary).IsNotNull();
            await Assert.That(insertedSalary.employees).IsNotNull();
            await Assert.That(transactionEmployee.salaries.Count).IsEqualTo(1);
            await Assert.That(ReferenceEquals(insertedSalary, insertedSalary.employees.salaries.First())).IsTrue();
            await Assert.That(ReferenceEquals(insertedSalary, transactionEmployee.salaries.First().employees.salaries.First())).IsTrue();
            await Assert.That(ReferenceEquals(transactionEmployee, transactionEmployee.salaries.First().employees)).IsTrue();
            await Assert.That(ReferenceEquals(transactionEmployee, insertedSalary.employees.salaries.First().employees)).IsTrue();

            transaction.Commit();
        }

        await Assert.That(employee.salaries.Count).IsEqualTo(1);
        employeesDatabase.Delete(employee.salaries.First());
        await Assert.That(employee.salaries).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_InsertRelationsReadAfterCommit_ClearsTransactionCache(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_InsertRelationsReadAfterCommit_ClearsTransactionCache),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999797, employeesDatabase);

        foreach (var existingSalary in employee.salaries.ToList())
            employeesDatabase.Delete(existingSalary);

        var table = employeesDatabase.Provider.Metadata
            .TableModels.Single(x => x.Table.DbName == "salaries").Table;
        var cache = employeesDatabase.Provider.State.Cache.TableCaches[table];

        using var transaction = employeesDatabase.Transaction();

        await Assert.That(cache.IsTransactionInCache(transaction)).IsFalse();
        await Assert.That(cache.GetTransactionRows(transaction)).IsEmpty();

        var transactionEmployee = transaction.Query().Employees.Single(x => x.emp_no == employee.emp_no);
        await Assert.That(transactionEmployee.salaries).IsEmpty();

        var salary = transaction.Insert(new MutableSalaries
        {
            emp_no = transactionEmployee.emp_no!.Value,
            salary = 50000,
            FromDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)),
            ToDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20))
        });

        await Assert.That(cache.IsTransactionInCache(transaction)).IsTrue();
        await Assert.That(cache.GetTransactionRows(transaction).Count()).IsEqualTo(1);

        transaction.Commit();

        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(cache.IsTransactionInCache(transaction)).IsFalse();
        await Assert.That(cache.GetTransactionRows(transaction)).IsEmpty();
        await Assert.That(salary.employees.salaries.First()).IsEqualTo(salary);
        await Assert.That(transactionEmployee.salaries.First().employees.salaries.First()).IsEqualTo(salary);
        await Assert.That(transactionEmployee.salaries.First().employees).IsEqualTo(transactionEmployee);
        await Assert.That(salary.employees.salaries.First().employees).IsEqualTo(transactionEmployee);

        await Assert.That(employee.salaries.Count).IsEqualTo(1);
        employeesDatabase.Delete(employee.salaries.First());
        await Assert.That(employee.salaries).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Transaction_ReSavingOriginalMutable_PreservesEarlierPersistedChanges(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Transaction_ReSavingOriginalMutable_PreservesEarlierPersistedChanges),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = _employees.GetOrCreateEmployee(999796, employeesDatabase).Mutate();
        var newBirthDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));

        var firstSave = employee.Save(x => x.birth_date = newBirthDate, employeesDatabase);
        await Assert.That(firstSave.emp_no).IsEqualTo(999796);
        await Assert.That(firstSave.birth_date).IsEqualTo(newBirthDate);

        var newHireDate = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        var secondSave = employee.Save(x => x.hire_date = newHireDate, employeesDatabase);

        await Assert.That(secondSave.emp_no).IsEqualTo(999796);
        await Assert.That(secondSave.birth_date).IsEqualTo(newBirthDate);
        await Assert.That(secondSave.hire_date).IsEqualTo(newHireDate);
    }

    private static async Task AssertThrows<TException>(Action action)
        where TException : Exception
    {
        var threw = false;

        try
        {
            action();
        }
        catch (TException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }
}

public sealed class EmployeesTransactionLifecycleParallelLimit : IParallelLimit
{
    public int Limit => 1;
}
