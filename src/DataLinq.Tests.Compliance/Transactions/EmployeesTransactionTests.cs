using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesTransactionTests
{
    private readonly EmployeesTestData _employees = new();

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Insert_CommitsInsertedEmployeeAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(provider, nameof(Insert_CommitsInsertedEmployeeAcrossProviders));
        var employeesDatabase = databaseScope.Database;
        var employeeNumber = 999999;

        foreach (var existingEmployee in employeesDatabase.Query().Employees.Where(x => x.emp_no == employeeNumber).ToList())
            employeesDatabase.Delete(existingEmployee);

        var employee = _employees.NewEmployee(employeeNumber);
        await Assert.That(employee.HasPrimaryKeysSet()).IsTrue();

        using var transaction = employeesDatabase.Transaction();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Closed);

        transaction.OnStatusChanged += (sender, args) =>
        {
            if (!ReferenceEquals(transaction, sender))
                throw new InvalidOperationException("Transaction status sender mismatch.");

            if (!ReferenceEquals(transaction, args.Transaction))
                throw new InvalidOperationException("Transaction status event transaction mismatch.");

            if (transaction.Status != args.Status)
                throw new InvalidOperationException("Transaction status event reported the wrong status.");
        };

        transaction.Insert(employee);
        await Assert.That(employee.HasPrimaryKeysSet()).IsTrue();

        var transactionEmployee = transaction.Query().Employees.Single(x => x.emp_no == employeeNumber);
        await Assert.That(ReferenceEquals(employee, transactionEmployee)).IsFalse();
        await Assert.That(transactionEmployee.birth_date).IsEqualTo(employee.birth_date);

        var table = employeesDatabase.Provider.Metadata
            .TableModels.Single(x => x.Table.DbName == "employees").Table;
        var cache = employeesDatabase.Provider.State.Cache.TableCaches[table];

        await Assert.That(cache.IsTransactionInCache(transaction)).IsTrue();
        await Assert.That(cache.GetTransactionRows(transaction).Count()).IsEqualTo(1);
        await Assert.That(ReferenceEquals(transactionEmployee, cache.GetTransactionRows(transaction).First())).IsTrue();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Open);

        transaction.Commit();

        await Assert.That(cache.IsTransactionInCache(transaction)).IsFalse();
        await Assert.That(transaction.Status).IsEqualTo(DatabaseTransactionStatus.Committed);

        var persistedEmployee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber);
        await Assert.That(persistedEmployee.birth_date).IsEqualTo(employee.birth_date);
        await Assert.That(persistedEmployee).IsEqualTo(transactionEmployee);
        await Assert.That(ReferenceEquals(transactionEmployee, persistedEmployee)).IsFalse();
    }
}
