using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesNullableBooleanTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NullableBool_EqualsTrue(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(NullableBool_EqualsTrue),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employeeNumbers = SetupNullableBoolTestData(employeesDatabase);

        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => employeeNumbers.Contains(x.emp_no!.Value) && x.IsDeleted == true)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => employeeNumbers.Contains(x.emp_no!.Value) && x.IsDeleted == true)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(2001);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NullableBool_NotEqualsTrue(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(NullableBool_NotEqualsTrue),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employeeNumbers = SetupNullableBoolTestData(employeesDatabase);

        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => employeeNumbers.Contains(x.emp_no!.Value) && x.IsDeleted != true)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => employeeNumbers.Contains(x.emp_no!.Value) && x.IsDeleted != true)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(2);
        await Assert.That(result.Contains(2002)).IsTrue();
        await Assert.That(result.Contains(2003)).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NullableBool_EqualsFalse(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(NullableBool_EqualsFalse),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employeeNumbers = SetupNullableBoolTestData(employeesDatabase);

        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => employeeNumbers.Contains(x.emp_no!.Value) && x.IsDeleted == false)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => employeeNumbers.Contains(x.emp_no!.Value) && x.IsDeleted == false)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(2002);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NullableBool_NotEqualsFalse(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(NullableBool_NotEqualsFalse),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employeeNumbers = SetupNullableBoolTestData(employeesDatabase);

        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => employeeNumbers.Contains(x.emp_no!.Value) && x.IsDeleted != false)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => employeeNumbers.Contains(x.emp_no!.Value) && x.IsDeleted != false)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(2);
        await Assert.That(result.Contains(2001)).IsTrue();
        await Assert.That(result.Contains(2003)).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NullableBool_EqualsNull(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(NullableBool_EqualsNull),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employeeNumbers = SetupNullableBoolTestData(employeesDatabase);

        var expected = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => employeeNumbers.Contains(x.emp_no!.Value) && x.IsDeleted == null)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        var result = employeesDatabase.Query().Employees
            .Where(x => employeeNumbers.Contains(x.emp_no!.Value) && x.IsDeleted == null)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(2003);
    }

    private static IReadOnlySet<int> SetupNullableBoolTestData(Database<EmployeesDb> employeesDatabase)
    {
        var employeeNumbers = new HashSet<int> { 2001, 2002, 2003 };

        employeesDatabase.Commit(transaction =>
        {
            foreach (var employee in transaction.Query().Employees.Where(x => employeeNumbers.Contains(x.emp_no!.Value)).ToList())
                transaction.Delete(employee);

            transaction.Insert(new MutableEmployee
            {
                emp_no = 2001,
                first_name = "Test",
                last_name = "True",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.M,
                IsDeleted = true
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2002,
                first_name = "Test",
                last_name = "False",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.F,
                IsDeleted = false
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2003,
                first_name = "Test",
                last_name = "Null",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.M,
                IsDeleted = null
            });
        });

        return employeeNumbers;
    }
}
