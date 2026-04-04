using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesStringMemberTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_ToUpperMatchesDepartment(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(String_ToUpperMatchesDepartment),
            EmployeesSeedMode.Bogus);

        var (_, department) = SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Departments
            .ToList()
            .Where(x => x.Name.ToUpper() == department.Name.ToUpper())
            .Select(x => x.DeptNo)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Departments
            .Where(x => x.Name.ToUpper() == department.Name.ToUpper())
            .Select(x => x.DeptNo)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result.Length).IsGreaterThan(0);
        await Assert.That(result).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_ToLowerMatchesDepartment(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(String_ToLowerMatchesDepartment),
            EmployeesSeedMode.Bogus);

        var (_, department) = SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Departments
            .ToList()
            .Where(x => x.Name.ToLower() == department.Name.ToLower())
            .Select(x => x.DeptNo)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Departments
            .Where(x => x.Name.ToLower() == department.Name.ToLower())
            .Select(x => x.DeptNo)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result.Length).IsGreaterThan(0);
        await Assert.That(result).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_TrimMatchesInsertedEmployee(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(String_TrimMatchesInsertedEmployee),
            EmployeesSeedMode.Bogus);

        var (employee, _) = SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Single(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Trim() == "John");
        var result = databaseScope.Database.Query().Employees
            .Single(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Trim() == "John");

        await Assert.That(result.emp_no).IsEqualTo(expected.emp_no);
        await Assert.That(result.emp_no).IsEqualTo(employee.emp_no);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_SubstringMatchesInsertedEmployee(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(String_SubstringMatchesInsertedEmployee),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.last_name.Substring(1, 4) == "even")
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.last_name.Substring(1, 4) == "even")
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result.Length).IsGreaterThan(0);
        await Assert.That(result).IsEquivalentTo(expected);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_IsNullOrEmptyFalseFiltersEmptyString(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(String_IsNullOrEmptyFalseFiltersEmptyString),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && !string.IsNullOrEmpty(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && !string.IsNullOrEmpty(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Contains(2011)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_IsNullOrEmptyTrueReturnsOnlyEmptyString(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(String_IsNullOrEmptyTrueReturnsOnlyEmptyString),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && string.IsNullOrEmpty(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && string.IsNullOrEmpty(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(2011);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_IsNullOrWhiteSpaceFalseFiltersWhitespaceRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(String_IsNullOrWhiteSpaceFalseFiltersWhitespaceRows),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && !string.IsNullOrWhiteSpace(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && !string.IsNullOrWhiteSpace(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Contains(2011)).IsFalse();
        await Assert.That(result.Contains(2012)).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_IsNullOrWhiteSpaceTrueReturnsEmptyAndWhitespaceRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(String_IsNullOrWhiteSpaceTrueReturnsEmptyAndWhitespaceRows),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && string.IsNullOrWhiteSpace(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && string.IsNullOrWhiteSpace(x.first_name))
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(2);
        await Assert.That(result.Contains(2011)).IsTrue();
        await Assert.That(result.Contains(2012)).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_LengthMatchesInsertedEmployee(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(String_LengthMatchesInsertedEmployee),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Length == 6)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Length == 6)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(2010);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task String_TrimLengthMatchesInsertedEmployee(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(String_TrimLengthMatchesInsertedEmployee),
            EmployeesSeedMode.Bogus);

        SetupStringTestData(databaseScope.Database);
        var expected = databaseScope.Database.Query().Employees
            .ToList()
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Trim().Length == 4)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var result = databaseScope.Database.Query().Employees
            .Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value) && x.first_name.Trim().Length == 4)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(result).IsEquivalentTo(expected);
        await Assert.That(result.Length).IsEqualTo(1);
        await Assert.That(result[0]).IsEqualTo(2010);
    }

    private static (Employee employee, Department department) SetupStringTestData(Database<EmployeesDb> employeesDatabase)
    {
        employeesDatabase.Commit(transaction =>
        {
            foreach (var employee in transaction.Query().Employees.Where(x => StringTestEmployeeNumbers.Contains(x.emp_no!.Value)).ToList())
                transaction.Delete(employee);

            transaction.Insert(new MutableEmployee
            {
                emp_no = 2010,
                first_name = " John ",
                last_name = " Doe ",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.M,
                IsDeleted = true
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2011,
                first_name = string.Empty,
                last_name = "Devenshoe",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.F,
                IsDeleted = false
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2012,
                first_name = "   ",
                last_name = "Noname",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.M,
                IsDeleted = null
            });
        });

        var employee = employeesDatabase.Query().Employees.First(x => x.emp_no == 2010);
        var department = employeesDatabase.Query().Departments.First(x => x.DeptNo == "d005");

        return (employee, department);
    }

    private static readonly HashSet<int> StringTestEmployeeNumbers = [2010, 2011, 2012];
}
