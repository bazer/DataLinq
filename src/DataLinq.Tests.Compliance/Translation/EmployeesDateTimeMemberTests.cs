using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesDateTimeMemberTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DateOnly_YearMatchesExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(DateOnly_YearMatchesExpectedRows),
            EmployeesSeedMode.Bogus);

        var (testDate, _, _) = GetTestDateAndTime(databaseScope.Database);
        var expectedCount = databaseScope.Database.Query().DepartmentEmployees
            .ToList()
            .Count(x => x.from_date.Year == testDate.Year);
        var resultCount = databaseScope.Database.Query().DepartmentEmployees
            .Where(x => x.from_date.Year == testDate.Year)
            .Count();

        await Assert.That(resultCount).IsEqualTo(expectedCount);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DateOnly_MonthMatchesExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(DateOnly_MonthMatchesExpectedRows),
            EmployeesSeedMode.Bogus);

        var (testDate, _, _) = GetTestDateAndTime(databaseScope.Database);
        var expectedCount = databaseScope.Database.Query().DepartmentEmployees
            .ToList()
            .Count(x => x.from_date.Month == testDate.Month);
        var resultCount = databaseScope.Database.Query().DepartmentEmployees
            .Where(x => x.from_date.Month == testDate.Month)
            .Count();

        await Assert.That(resultCount).IsEqualTo(expectedCount);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DateOnly_DayMatchesExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(DateOnly_DayMatchesExpectedRows),
            EmployeesSeedMode.Bogus);

        var (testDate, _, _) = GetTestDateAndTime(databaseScope.Database);
        var expectedCount = databaseScope.Database.Query().DepartmentEmployees
            .ToList()
            .Count(x => x.from_date.Day == testDate.Day);
        var resultCount = databaseScope.Database.Query().DepartmentEmployees
            .Where(x => x.from_date.Day == testDate.Day)
            .Count();

        await Assert.That(resultCount).IsEqualTo(expectedCount);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DateOnly_DayOfYearMatchesExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(DateOnly_DayOfYearMatchesExpectedRows),
            EmployeesSeedMode.Bogus);

        var (testDate, _, _) = GetTestDateAndTime(databaseScope.Database);
        var expectedCount = databaseScope.Database.Query().DepartmentEmployees
            .ToList()
            .Count(x => x.from_date.DayOfYear == testDate.DayOfYear);
        var resultCount = databaseScope.Database.Query().DepartmentEmployees
            .Where(x => x.from_date.DayOfYear == testDate.DayOfYear)
            .Count();

        await Assert.That(resultCount).IsEqualTo(expectedCount);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DateOnly_DayOfWeekMatchesExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(DateOnly_DayOfWeekMatchesExpectedRows),
            EmployeesSeedMode.Bogus);

        var (testDate, _, _) = GetTestDateAndTime(databaseScope.Database);
        var expectedCount = databaseScope.Database.Query().DepartmentEmployees
            .ToList()
            .Count(x => x.from_date.DayOfWeek == testDate.DayOfWeek);
        var resultCount = databaseScope.Database.Query().DepartmentEmployees
            .Where(x => x.from_date.DayOfWeek == testDate.DayOfWeek)
            .Count();

        await Assert.That(resultCount).IsEqualTo(expectedCount);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task TimeOnly_HourMatchesExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(TimeOnly_HourMatchesExpectedRows),
            EmployeesSeedMode.Bogus);

        var (_, testTime, _) = GetTestDateAndTime(databaseScope.Database);
        var expectedCount = databaseScope.Database.Query().Employees
            .ToList()
            .Count(x => x.last_login.HasValue && x.last_login.Value.Hour == testTime.Hour);
        var resultCount = databaseScope.Database.Query().Employees
            .Where(x => x.last_login!.Value.Hour == testTime.Hour)
            .Count();

        await Assert.That(resultCount).IsEqualTo(expectedCount);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DateTime_MinuteMatchesExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(DateTime_MinuteMatchesExpectedRows),
            EmployeesSeedMode.Bogus);

        var (_, _, testDateTime) = GetTestDateAndTime(databaseScope.Database);
        var expectedCount = databaseScope.Database.Query().Employees
            .ToList()
            .Count(x => x.created_at.HasValue && x.created_at.Value.Minute == testDateTime.Minute);
        var resultCount = databaseScope.Database.Query().Employees
            .Where(x => x.created_at!.Value.Minute == testDateTime.Minute)
            .Count();

        await Assert.That(resultCount).IsEqualTo(expectedCount);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DateTime_SecondMatchesExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(DateTime_SecondMatchesExpectedRows),
            EmployeesSeedMode.Bogus);

        var (_, _, testDateTime) = GetTestDateAndTime(databaseScope.Database);
        var expectedCount = databaseScope.Database.Query().Employees
            .ToList()
            .Count(x => x.created_at.HasValue && x.created_at.Value.Second == testDateTime.Second);
        var resultCount = databaseScope.Database.Query().Employees
            .Where(x => x.created_at!.Value.Second == testDateTime.Second)
            .Count();

        await Assert.That(resultCount).IsEqualTo(expectedCount);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task DateTime_MillisecondMatchesExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(DateTime_MillisecondMatchesExpectedRows),
            EmployeesSeedMode.Bogus);

        var (_, _, testDateTime) = GetTestDateAndTime(databaseScope.Database);
        var expectedCount = databaseScope.Database.Query().Employees
            .ToList()
            .Count(x => x.created_at.HasValue && x.created_at.Value.Millisecond == testDateTime.Millisecond);
        var resultCount = databaseScope.Database.Query().Employees
            .Where(x => x.created_at!.Value.Millisecond == testDateTime.Millisecond)
            .Count();

        await Assert.That(resultCount).IsEqualTo(expectedCount);
    }

    private static (DateOnly date, TimeOnly time, DateTime dateTime) GetTestDateAndTime(Database<EmployeesDb> employeesDatabase)
    {
        var firstEmployee = employeesDatabase.Query().Employees.OrderBy(x => x.emp_no).First();
        var firstDepartmentEmployee = employeesDatabase.Query().DepartmentEmployees.OrderBy(x => x.from_date).First();

        return (
            firstDepartmentEmployee.from_date,
            firstEmployee.last_login!.Value,
            firstEmployee.created_at!.Value);
    }
}
