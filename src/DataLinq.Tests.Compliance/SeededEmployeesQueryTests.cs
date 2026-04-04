using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class SeededEmployeesQueryTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SeededDepartments_HaveExpectedCountAndLookupBehavior(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(SeededDepartments_HaveExpectedCountAndLookupBehavior),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;

        await Assert.That(employeesDatabase.Query().Departments.Count()).IsEqualTo(20);

        var departments = employeesDatabase.Query().Departments
            .Where(x => x.DeptNo == "d005")
            .ToList();

        await Assert.That(departments.Count).IsEqualTo(1);
        await Assert.That(departments[0].DeptNo).IsEqualTo("d005");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SeededEmployees_CanQueryViewsAndPrimaryKeyLookups(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(SeededEmployees_CanQueryViewsAndPrimaryKeyLookups),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;

        var latestDepartmentAssignments = employeesDatabase.Query().dept_emp_latest_date.Count();
        var currentDepartmentAssignments = employeesDatabase.Query().current_dept_emp.ToList();
        var department = employeesDatabase.Get<Department>(new StringKey("d005"));

        await Assert.That(latestDepartmentAssignments).IsGreaterThan(0);
        await Assert.That(currentDepartmentAssignments.Count).IsGreaterThan(0);
        await Assert.That(department).IsNotNull();
        await Assert.That(department!.DeptNo).IsEqualTo("d005");
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task SeededDepartments_LazyLoadManagers(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(SeededDepartments_LazyLoadManagers),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var department = Department.Get("d005", employeesDatabase);

        await Assert.That(department).IsNotNull();
        await Assert.That(department!.Managers).IsNotNull();

        var managers = department.Managers.ToList();

        await Assert.That(managers.Count).IsGreaterThan(0);
        await Assert.That(managers.All(x => x.Department.DeptNo == "d005")).IsTrue();
    }
}
