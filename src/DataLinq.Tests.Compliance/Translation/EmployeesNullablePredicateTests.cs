using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesNullablePredicateTests
{
    private static readonly int[] EmployeeNumbers = [2020, 2021, 2022];
    private static readonly TimeOnly LoginA = new(9, 15, 0);
    private static readonly TimeOnly LoginB = new(10, 30, 0);
    private static readonly DateTime CreatedA = new(2024, 3, 4, 12, 34, 56, 789);
    private static readonly DateTime CreatedB = new(2024, 3, 5, 8, 30, 0, 123);

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NullableHasValuePredicates_MatchInMemoryFiltering(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(NullableHasValuePredicates_MatchInMemoryFiltering),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var rows = SetupNullablePredicateRows(employeesDatabase);

        var expectedHasLogin = rows
            .Where(x => x.last_login.HasValue)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualHasLogin = employeesDatabase.Query().Employees
            .Where(x => EmployeeNumbers.Contains(x.emp_no!.Value) && x.last_login.HasValue)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        var expectedMissingLogin = rows
            .Where(x => !x.last_login.HasValue)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualMissingLogin = employeesDatabase.Query().Employees
            .Where(x => EmployeeNumbers.Contains(x.emp_no!.Value) && !x.last_login.HasValue)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(actualHasLogin).IsEquivalentTo(expectedHasLogin);
        await Assert.That(actualHasLogin).IsEquivalentTo(new[] { 2020, 2022 });
        await Assert.That(actualMissingLogin).IsEquivalentTo(expectedMissingLogin);
        await Assert.That(actualMissingLogin).IsEquivalentTo(new[] { 2021 });
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NullableValueComparisons_MatchGuardedInMemoryFiltering(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(NullableValueComparisons_MatchGuardedInMemoryFiltering),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var rows = SetupNullablePredicateRows(employeesDatabase);

        var expectedLogin = rows
            .Where(x => x.last_login.HasValue && x.last_login.Value == LoginA)
            .Select(x => x.emp_no!.Value)
            .ToArray();
        var actualLogin = employeesDatabase.Query().Employees
            .Where(x => EmployeeNumbers.Contains(x.emp_no!.Value) && x.last_login.HasValue && x.last_login.Value == LoginA)
            .Select(x => x.emp_no!.Value)
            .ToArray();

        var expectedCreatedMinute = rows
            .Where(x => x.created_at.HasValue && x.created_at.Value.Minute == CreatedA.Minute)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualCreatedMinute = employeesDatabase.Query().Employees
            .Where(x => EmployeeNumbers.Contains(x.emp_no!.Value) && x.created_at.HasValue && x.created_at.Value.Minute == CreatedA.Minute)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(actualLogin).IsEquivalentTo(expectedLogin);
        await Assert.That(actualLogin).IsEquivalentTo(new[] { 2020 });
        await Assert.That(actualCreatedMinute).IsEquivalentTo(expectedCreatedMinute);
        await Assert.That(actualCreatedMinute).IsEquivalentTo(new[] { 2020 });
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NullableMixedEqualityPredicates_MatchCSharpNullSemantics(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(NullableMixedEqualityPredicates_MatchCSharpNullSemantics),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var rows = SetupNullablePredicateRows(employeesDatabase);

        var expectedEqual = rows
            .Where(x => x.last_login == LoginA)
            .Select(x => x.emp_no!.Value)
            .ToArray();
        var actualEqual = employeesDatabase.Query().Employees
            .Where(x => EmployeeNumbers.Contains(x.emp_no!.Value) && x.last_login == LoginA)
            .Select(x => x.emp_no!.Value)
            .ToArray();

        var expectedNotEqual = rows
            .Where(x => x.last_login != LoginA)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualNotEqual = employeesDatabase.Query().Employees
            .Where(x => EmployeeNumbers.Contains(x.emp_no!.Value) && x.last_login != LoginA)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();
        var actualReversedNotEqual = employeesDatabase.Query().Employees
            .Where(x => EmployeeNumbers.Contains(x.emp_no!.Value) && LoginA != x.last_login)
            .Select(x => x.emp_no!.Value)
            .OrderBy(x => x)
            .ToArray();

        await Assert.That(actualEqual).IsEquivalentTo(expectedEqual);
        await Assert.That(actualEqual).IsEquivalentTo(new[] { 2020 });
        await Assert.That(actualNotEqual).IsEquivalentTo(expectedNotEqual);
        await Assert.That(actualNotEqual).IsEquivalentTo(new[] { 2021, 2022 });
        await Assert.That(actualReversedNotEqual).IsEquivalentTo(expectedNotEqual);
    }

    private static Employee[] SetupNullablePredicateRows(Database<EmployeesDb> employeesDatabase)
    {
        employeesDatabase.Commit(transaction =>
        {
            foreach (var employee in transaction.Query().Employees.Where(x => EmployeeNumbers.Contains(x.emp_no!.Value)).ToList())
                transaction.Delete(employee);

            transaction.Insert(new MutableEmployee
            {
                emp_no = 2020,
                first_name = "Nullable",
                last_name = "LoginA",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.M,
                IsDeleted = true,
                last_login = LoginA,
                created_at = CreatedA
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2021,
                first_name = "Nullable",
                last_name = "Missing",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.F,
                IsDeleted = null,
                last_login = null,
                created_at = null
            });
            transaction.Insert(new MutableEmployee
            {
                emp_no = 2022,
                first_name = "Nullable",
                last_name = "LoginB",
                birth_date = new DateOnly(1990, 1, 1),
                hire_date = new DateOnly(2020, 1, 1),
                gender = Employee.Employeegender.M,
                IsDeleted = false,
                last_login = LoginB,
                created_at = CreatedB
            });
        });

        return employeesDatabase.Query().Employees
            .Where(x => EmployeeNumbers.Contains(x.emp_no!.Value))
            .OrderBy(x => x.emp_no)
            .ToArray();
    }
}
