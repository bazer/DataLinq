using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesRelationAndThreadingTests
{
    private readonly EmployeesTestData _employees = new();
    private const int ServerParallelIterationCount = 24;
    private const int LocalParallelIterationCount = 100;
    private const int ServerParallelTransactionCount = 6;
    private const int LocalParallelTransactionCount = 10;

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Relations_ManagerDepartmentLazyLoad_ResolvesSingleValue(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Relations_ManagerDepartmentLazyLoad_ResolvesSingleValue),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var manager = employeesDatabase.Query().Managers
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_fk)
            .First();

        await Assert.That(manager.Department).IsNotNull();
        await Assert.That(manager.Department!.DeptNo).IsEqualTo(manager.dept_fk);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Relations_DepartmentManagersLazyLoad_ResolvesCollection(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Relations_DepartmentManagersLazyLoad_ResolvesCollection),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var department = employeesDatabase.Query().Departments
            .ToList()
            .Select(x => new { Department = x, Managers = x.Managers.ToList() })
            .First(x => x.Managers.Count > 0);

        await Assert.That(department.Managers).IsNotEmpty();
        await Assert.That(department.Managers.All(x => x.Department.DeptNo == department.Department.DeptNo)).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Relations_EmployeesWithoutManagerLinks_ExposeEmptyCollections(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Relations_EmployeesWithoutManagerLinks_ExposeEmptyCollections),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employee = employeesDatabase.Query().Employees
            .ToList()
            .First(x => !x.dept_manager.Any());

        await Assert.That(employee.dept_manager).IsEmpty();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Threading_ParallelRelationTraversal_KeepsGraphsConsistent(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Threading_ParallelRelationTraversal_KeepsGraphsConsistent),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var amount = GetParallelIterationCount(provider);
        var employees = employeesDatabase.Query().Employees
            .Where(x => x.emp_no <= amount)
            .OrderBy(x => x.emp_no)
            .ToList();

        await Assert.That(employees.Count).IsEqualTo(amount);

        Parallel.For(0, amount, index =>
        {
            var employee = employees[index];

            if (employee.dept_emp.Count == 0)
                throw new InvalidOperationException($"Collection dept_emp is empty for employee '{employee.emp_no}'.");

            foreach (var departmentEmployee in employee.dept_emp)
            {
                if (departmentEmployee.employees is null || !Equals(departmentEmployee.employees, employee))
                    throw new InvalidOperationException($"Employee relation was not preserved for employee '{employee.emp_no}'.");

                if (departmentEmployee.departments is null)
                    throw new InvalidOperationException($"Department relation is null for employee '{employee.emp_no}'.");

                if (departmentEmployee.departments.DepartmentEmployees.Count == 0)
                    throw new InvalidOperationException($"DepartmentEmployees is empty for department '{departmentEmployee.departments.DeptNo}'.");

                if (departmentEmployee.departments.Managers is null)
                    throw new InvalidOperationException($"Managers relation is null for department '{departmentEmployee.departments.DeptNo}'.");
            }
        });
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Threading_ParallelReads_ReturnExpectedRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Threading_ParallelReads_ReturnExpectedRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var iterations = GetParallelIterationCount(provider);

        var employeeNumbers = employeesDatabase.Query().Employees
            .OrderBy(x => x.emp_no)
            .Take(5)
            .Select(x => x.emp_no!.Value)
            .ToArray();

        Parallel.For(0, iterations, _ =>
        {
            foreach (var employeeNumber in employeeNumbers)
                AssertEmployeeNumber(employeesDatabase, employeeNumber);
        });

        await Assert.That(employeeNumbers.Length).IsEqualTo(5);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Threading_ParallelTransactionCommits_PersistIndependentUpdates(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Threading_ParallelTransactionCommits_PersistIndependentUpdates),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var employeeNumbers = Enumerable.Range(999980, GetParallelTransactionCount(provider)).ToArray();
        var originalBirthDates = new Dictionary<int, DateOnly>();
        var updatedBirthDates = new Dictionary<int, DateOnly>();

        foreach (var employeeNumber in employeeNumbers)
        {
            var employee = _employees.GetOrCreateEmployee(employeeNumber, employeesDatabase);
            originalBirthDates[employeeNumber] = employee.birth_date;
            updatedBirthDates[employeeNumber] = _employees.RandomDate(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20));
        }

        Parallel.ForEach(employeeNumbers, employeeNumber =>
        {
            var mutable = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber).Mutate();
            mutable.birth_date = updatedBirthDates[employeeNumber];

            using var transaction = employeesDatabase.Transaction();
            _ = transaction.Update(mutable);
            transaction.Commit();
        });

        foreach (var employeeNumber in employeeNumbers)
        {
            var employee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber);
            await Assert.That(employee.birth_date.ToShortDateString()).IsNotEqualTo(originalBirthDates[employeeNumber].ToShortDateString());
            await Assert.That(employee.birth_date.ToShortDateString()).IsEqualTo(updatedBirthDates[employeeNumber].ToShortDateString());
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Threading_ParallelManagerLazyLoads_ResolveDepartments(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Threading_ParallelManagerLazyLoads_ResolveDepartments),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var departments = employeesDatabase.Query().Managers
            .ToList()
            .Select(x => x.dept_fk)
            .Distinct()
            .OrderBy(x => x)
            .Take(10)
            .ToArray();

        Parallel.ForEach(departments, departmentNumber =>
        {
            var manager = employeesDatabase.Query().Managers.First(x => x.dept_fk == departmentNumber);

            if (manager.Department is null || manager.Department.DeptNo != departmentNumber)
                throw new InvalidOperationException($"Manager relation did not resolve department '{departmentNumber}'.");
        });

        await Assert.That(departments.Length).IsGreaterThan(0);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Threading_ParallelDepartmentLazyLoads_ResolveManagerCollections(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Threading_ParallelDepartmentLazyLoads_ResolveManagerCollections),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var iterations = GetParallelIterationCount(provider);
        var departmentNumber = employeesDatabase.Query().Departments
            .ToList()
            .Select(x => new { x.DeptNo, ManagerCount = x.Managers.Count })
            .First(x => x.ManagerCount > 0)
            .DeptNo;

        Parallel.For(0, iterations, _ =>
        {
            var department = employeesDatabase.Query().Departments.Single(x => x.DeptNo == departmentNumber);

            if (department.Managers is null || department.Managers.Count == 0)
                throw new InvalidOperationException($"Managers were not resolved for department '{departmentNumber}'.");

            if (department.Managers.First().Department.DeptNo != departmentNumber)
                throw new InvalidOperationException($"Manager relation did not round-trip department '{departmentNumber}'.");
        });

        await Assert.That(departmentNumber).IsNotNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Threading_ParallelSnapshots_AdvanceCacheTimestamps(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(Threading_ParallelSnapshots_AdvanceCacheTimestamps),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        Parallel.For(0, GetParallelIterationCount(provider), _ =>
        {
            List<Salaries> salaries;

            do
            {
                var salaryLow = Random.Shared.Next(0, 200000);
                var salaryHigh = Random.Shared.Next(salaryLow, 200000);

                var snapshot = employeesDatabase.Provider.State.Cache.MakeSnapshot();
                salaries = employeesDatabase.Query().salaries
                    .Where(x => x.salary > salaryLow && x.salary < salaryHigh)
                    .Take(10)
                    .ToList();
                var snapshot2 = employeesDatabase.Provider.State.Cache.MakeSnapshot();

                if (!(snapshot.Timestamp < snapshot2.Timestamp))
                    throw new InvalidOperationException("Cache snapshot timestamps did not advance.");
            }
            while (salaries.Count == 0);
        });

        var snapshotTimestamp = employeesDatabase.Provider.State.Cache.MakeSnapshot().Timestamp;
        await Assert.That(snapshotTimestamp > DateTime.MinValue).IsTrue();
    }

    private static int GetParallelIterationCount(TestProviderDescriptor provider)
        => provider.IsServerDatabase ? ServerParallelIterationCount : LocalParallelIterationCount;

    private static int GetParallelTransactionCount(TestProviderDescriptor provider)
        => provider.IsServerDatabase ? ServerParallelTransactionCount : LocalParallelTransactionCount;

    private static void AssertEmployeeNumber(Database<EmployeesDb> employeesDatabase, int employeeNumber)
    {
        var employee = employeesDatabase.Query().Employees.Single(x => x.emp_no == employeeNumber);

        if (employee.emp_no != employeeNumber)
            throw new InvalidOperationException($"Expected employee '{employeeNumber}' but got '{employee.emp_no}'.");
    }
}
