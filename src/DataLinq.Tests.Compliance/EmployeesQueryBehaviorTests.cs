using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public class EmployeesQueryBehaviorTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_ToListAndCountOnTablesAndViews_WorkAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_ToListAndCountOnTablesAndViews_WorkAcrossProviders),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;

        await Assert.That(employeesDatabase.Query().Departments.ToList().Count).IsEqualTo(20);
        await Assert.That(employeesDatabase.Query().Departments.Count()).IsEqualTo(20);
        await Assert.That(employeesDatabase.Query().current_dept_emp.ToList().Count).IsGreaterThan(0);
        await Assert.That(employeesDatabase.Query().dept_emp_latest_date.Count()).IsGreaterThan(0);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_StaticGetAndSimpleDepartmentPredicates_WorkAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_StaticGetAndSimpleDepartmentPredicates_WorkAcrossProviders),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var department = employeesDatabase.Get<Department>(new StringKey("d005"));
        var missing = employeesDatabase.Get<Department>(new StringKey("xxxx"));
        var exact = employeesDatabase.Query().Departments.Where(x => x.DeptNo == "d005").ToList();
        var exactReverse = employeesDatabase.Query().Departments.Where(x => "d005" == x.DeptNo).ToList();
        var notExact = employeesDatabase.Query().Departments.Where(x => x.DeptNo != "d005").ToList();

        await Assert.That(department).IsNotNull();
        await Assert.That(department!.DeptNo).IsEqualTo("d005");
        await Assert.That(missing).IsNull();
        await Assert.That(exact.Count).IsEqualTo(1);
        await Assert.That(exactReverse.Count).IsEqualTo(1);
        await Assert.That(notExact.Count).IsEqualTo(19);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_DepartmentStringPredicates_WorkAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_DepartmentStringPredicates_WorkAcrossProviders),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;

        var startsWith = employeesDatabase.Query().Departments.Where(x => x.DeptNo.StartsWith("d00")).OrderBy(x => x.DeptNo).ToList();
        var notStartsWith = employeesDatabase.Query().Departments.Where(x => !x.DeptNo.StartsWith("d00")).OrderBy(x => x.DeptNo).ToList();
        var endsWith = employeesDatabase.Query().Departments.Where(x => x.DeptNo.EndsWith("2")).OrderBy(x => x.DeptNo).ToList();
        var notEndsWith = employeesDatabase.Query().Departments.Where(x => !x.DeptNo.EndsWith("2")).OrderBy(x => x.DeptNo).ToList();

        await Assert.That(startsWith.Select(x => x.DeptNo).ToArray()).IsEquivalentTo(new[] { "d001", "d002", "d003", "d004", "d005", "d006", "d007", "d008", "d009" });
        await Assert.That(notStartsWith.Select(x => x.DeptNo).ToArray()).IsEquivalentTo(new[] { "d010", "d011", "d012", "d013", "d014", "d015", "d016", "d017", "d018", "d019", "d020" });
        await Assert.That(endsWith.Select(x => x.DeptNo).ToArray()).IsEquivalentTo(new[] { "d002", "d012" });
        await Assert.That(notEndsWith.Count).IsEqualTo(18);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_ManagerStringAndDatePredicates_MatchInMemoryFiltering(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_ManagerStringAndDatePredicates_MatchInMemoryFiltering),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var managers = employeesDatabase.Query().Managers.ToList();
        var pivotDate = DateOnly.Parse("2010-01-01");

        await AssertManagerSequenceEqual(
            managers.Where(x => x.dept_fk.StartsWith("d00") && x.from_date > pivotDate).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList(),
            employeesDatabase.Query().Managers.Where(x => x.dept_fk.StartsWith("d00") && x.from_date > pivotDate).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => !x.dept_fk.StartsWith("d00") && x.from_date > pivotDate).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList(),
            employeesDatabase.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && x.from_date > pivotDate).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && x.from_date > pivotDate).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList(),
            employeesDatabase.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && x.from_date > pivotDate).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && x.from_date <= pivotDate).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList(),
            employeesDatabase.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && !(x.from_date > pivotDate)).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && x.from_date > pivotDate)).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList(),
            employeesDatabase.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && x.from_date > pivotDate)).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && x.from_date <= pivotDate)).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList(),
            employeesDatabase.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && !(x.from_date > pivotDate))).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") && x.from_date > pivotDate)).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList(),
            employeesDatabase.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") && x.from_date > pivotDate)).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") || x.from_date <= pivotDate)).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList(),
            employeesDatabase.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") || !(x.from_date > pivotDate))).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") || x.from_date <= pivotDate)).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList(),
            employeesDatabase.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") || !(x.from_date > pivotDate))).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_EnumPredicates_MatchInMemoryFiltering(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_EnumPredicates_MatchInMemoryFiltering),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var managers = employeesDatabase.Query().Managers.OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList();
        var employees = employeesDatabase.Query().Employees.OrderBy(x => x.emp_no).ToList();

        await AssertManagerSequenceEqual(
            managers.Where(x => ManagerType.FestiveManager == x.Type).ToList(),
            employeesDatabase.Query().Managers.Where(x => ManagerType.FestiveManager == x.Type).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => ManagerType.AssistantManager != x.Type).ToList(),
            employeesDatabase.Query().Managers.Where(x => ManagerType.AssistantManager != x.Type).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => x.Type == ManagerType.FestiveManager).ToList(),
            employeesDatabase.Query().Managers.Where(x => x.Type == ManagerType.FestiveManager).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => x.Type != ManagerType.AssistantManager).ToList(),
            employeesDatabase.Query().Managers.Where(x => x.Type != ManagerType.AssistantManager).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertEmployeeSequenceEqual(
            employees.Where(x => x.gender == Employee.Employeegender.M).ToList(),
            employeesDatabase.Query().Employees.Where(x => x.gender == Employee.Employeegender.M).OrderBy(x => x.emp_no).ToList());

        await AssertEmployeeSequenceEqual(
            employees.Where(x => x.gender != Employee.Employeegender.M).ToList(),
            employeesDatabase.Query().Employees.Where(x => x.gender != Employee.Employeegender.M).OrderBy(x => x.emp_no).ToList());

        await AssertEmployeeSequenceEqual(
            employees.Where(x => x.gender != Employee.Employeegender.F).ToList(),
            employeesDatabase.Query().Employees.Where(x => !(x.gender == Employee.Employeegender.F)).OrderBy(x => x.emp_no).ToList());

        await AssertEmployeeSequenceEqual(
            employees.Where(x => x.gender == Employee.Employeegender.F).ToList(),
            employeesDatabase.Query().Employees.Where(x => !(x.gender != Employee.Employeegender.F)).OrderBy(x => x.emp_no).ToList());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_DepartmentContainsCollections_WorkAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_DepartmentContainsCollections_WorkAcrossProviders),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var ids = new[] { "d001", "d002", "d003" };
        var listIds = new List<string>(ids);
        var setIds = new HashSet<string>(ids, StringComparer.Ordinal);

        var arrayResults = employeesDatabase.Query().Departments.Where(x => ids.Contains(x.DeptNo)).OrderBy(x => x.DeptNo).ToList();
        var negatedResults = employeesDatabase.Query().Departments.Where(x => !ids.Contains(x.DeptNo)).OrderBy(x => x.DeptNo).ToList();
        var listResults = employeesDatabase.Query().Departments.Where(x => listIds.Contains(x.DeptNo)).OrderBy(x => x.DeptNo).ToList();
        var setResults = employeesDatabase.Query().Departments.Where(x => setIds.Contains(x.DeptNo)).OrderBy(x => x.DeptNo).ToList();

        await Assert.That(arrayResults.Select(x => x.DeptNo).ToArray()).IsEquivalentTo(ids);
        await Assert.That(listResults.Select(x => x.DeptNo).ToArray()).IsEquivalentTo(ids);
        await Assert.That(setResults.Select(x => x.DeptNo).ToArray()).IsEquivalentTo(ids);
        await Assert.That(negatedResults.Count).IsEqualTo(17);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_MultipleContainsPredicates_MatchDynamicSeededRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_MultipleContainsPredicates_MatchDynamicSeededRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var selectedRows = employeesDatabase.Query().DepartmentEmployees
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .Take(3)
            .ToList();

        await Assert.That(selectedRows.Count).IsEqualTo(3);

        var departmentIds = selectedRows.Select(x => x.dept_no).Distinct().ToArray();
        var employeeIds = selectedRows.Select(x => x.emp_no).Distinct().ToArray();

        var expected = employeesDatabase.Query().DepartmentEmployees
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList()
            .Where(x => departmentIds.Contains(x.dept_no) && employeeIds.Contains(x.emp_no))
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList();

        var result = employeesDatabase.Query().DepartmentEmployees
            .Where(x => departmentIds.Contains(x.dept_no) && employeeIds.Contains(x.emp_no))
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList();

        await AssertDeptEmployeeSequenceEqual(expected, result);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_MultipleContainsWithAdditionalPredicates_MatchDynamicSeededRows(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_MultipleContainsWithAdditionalPredicates_MatchDynamicSeededRows),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var selectedRows = employeesDatabase.Query().DepartmentEmployees
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .Take(5)
            .ToList();

        var departmentIds = selectedRows.Select(x => x.dept_no).Distinct().ToArray();
        var employeeIds = selectedRows.Select(x => x.emp_no).Distinct().ToArray();

        var expectedStartsWith = employeesDatabase.Query().DepartmentEmployees
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList()
            .Where(x => departmentIds.Contains(x.dept_no) && employeeIds.Contains(x.emp_no) && x.dept_no.StartsWith("d"))
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList();

        var actualStartsWith = employeesDatabase.Query().DepartmentEmployees
            .Where(x => departmentIds.Contains(x.dept_no) && employeeIds.Contains(x.emp_no) && x.dept_no.StartsWith("d"))
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList();

        await AssertDeptEmployeeSequenceEqual(expectedStartsWith, actualStartsWith);

        var suffix = departmentIds.Select(x => x[^2..]).First();
        var expectedEndsWith = employeesDatabase.Query().DepartmentEmployees
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList()
            .Where(x => departmentIds.Contains(x.dept_no) && x.dept_no.EndsWith(suffix, StringComparison.Ordinal))
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList();

        var actualEndsWith = employeesDatabase.Query().DepartmentEmployees
            .Where(x => departmentIds.Contains(x.dept_no) && x.dept_no.EndsWith(suffix))
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList();

        await AssertDeptEmployeeSequenceEqual(expectedEndsWith, actualEndsWith);

        var minEmployeeId = employeeIds.Min();
        var maxEmployeeId = employeeIds.Max();
        var expectedRange = employeesDatabase.Query().DepartmentEmployees
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList()
            .Where(x => departmentIds.Contains(x.dept_no) && x.emp_no >= minEmployeeId && x.emp_no <= maxEmployeeId)
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList();

        var actualRange = employeesDatabase.Query().DepartmentEmployees
            .Where(x => departmentIds.Contains(x.dept_no) && x.emp_no >= minEmployeeId && x.emp_no <= maxEmployeeId)
            .OrderBy(x => x.emp_no)
            .ThenBy(x => x.dept_no)
            .ToList();

        await AssertDeptEmployeeSequenceEqual(expectedRange, actualRange);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_AnySingleAndFirstOperators_WorkAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_AnySingleAndFirstOperators_WorkAcrossProviders),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var singleDepartment = employeesDatabase.Query().Departments.Single(x => x.DeptNo == "d005");
        var singleOrDefaultDepartment = employeesDatabase.Query().Departments.SingleOrDefault(x => x.DeptNo == "d005");
        var missingDepartment = employeesDatabase.Query().Departments.SingleOrDefault(x => x.DeptNo == "1234");
        var richSalary = employeesDatabase.Query().salaries.First(x => x.salary > 70000);
        var richSalaryOrDefault = employeesDatabase.Query().salaries.FirstOrDefault(x => x.salary > 70000);
        var missingSalary = employeesDatabase.Query().salaries.FirstOrDefault(x => x.salary < 10000);
        var missingLastSalary = employeesDatabase.Query().salaries.LastOrDefault(x => x.salary < 10000);

        await Assert.That(singleDepartment.DeptNo).IsEqualTo("d005");
        await Assert.That(singleOrDefaultDepartment).IsNotNull();
        await Assert.That(singleOrDefaultDepartment!.DeptNo).IsEqualTo("d005");
        await Assert.That(missingDepartment).IsNull();
        await Assert.That(richSalary.salary).IsGreaterThanOrEqualTo(70000u);
        await Assert.That(richSalaryOrDefault).IsNotNull();
        await Assert.That(richSalaryOrDefault!.salary).IsGreaterThanOrEqualTo(70000u);
        await Assert.That(missingSalary).IsNull();
        await Assert.That(missingLastSalary).IsNull();
        await Assert.That(employeesDatabase.Query().Departments.Any(x => x.DeptNo == "d005")).IsTrue();
        await Assert.That(employeesDatabase.Query().Departments.Where(x => x.DeptNo == "d005").Any()).IsTrue();
        await Assert.That(employeesDatabase.Query().Departments.Any(x => x.DeptNo == "not_existing")).IsFalse();
        await Assert.That(employeesDatabase.Query().Departments.Where(x => x.DeptNo == "not_existing").Any()).IsFalse();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_OrderedFirstAndLastVariants_WorkAcrossProviders(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_OrderedFirstAndLastVariants_WorkAcrossProviders),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;

        var firstOrdered = employeesDatabase.Query().salaries.OrderBy(x => x.salary).First(x => x.salary > 70000);
        var firstOrDefaultOrdered = employeesDatabase.Query().salaries.OrderBy(x => x.salary).FirstOrDefault(x => x.salary > 70000);
        var lastOrDefaultOrdered = employeesDatabase.Query().salaries.OrderByDescending(x => x.salary).LastOrDefault(x => x.salary > 70000);
        var unorderedFirst = employeesDatabase.Query().salaries.First(x => x.salary > 70000);
        var descendingFirst = employeesDatabase.Query().salaries.OrderByDescending(x => x.salary).FirstOrDefault(x => x.salary > 70000);

        await Assert.That(firstOrdered).IsNotNull();
        await Assert.That(firstOrDefaultOrdered).IsNotNull();
        await Assert.That(lastOrDefaultOrdered).IsNotNull();
        await Assert.That(unorderedFirst).IsNotNull();
        await Assert.That(descendingFirst).IsNotNull();
        await Assert.That(firstOrdered.salary).IsGreaterThanOrEqualTo(70000u);
        await Assert.That(firstOrDefaultOrdered!.salary).IsEqualTo(firstOrdered.salary);
        await Assert.That(lastOrDefaultOrdered!.salary).IsEqualTo(firstOrdered.salary);
        await Assert.That(unorderedFirst.salary).IsNotEqualTo(firstOrdered.salary);
        await Assert.That(descendingFirst!.salary).IsNotEqualTo(firstOrdered.salary);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_OrderByProjectionAndPaging_MatchInMemoryBehavior(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_OrderByProjectionAndPaging_MatchInMemoryBehavior),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var lastDepartmentName = $"d{employeesDatabase.Query().Departments.Count():000}";

        var orderedDepartments = employeesDatabase.Query().Departments.OrderBy(x => x.DeptNo);
        var projectedDepartmentNumbers = employeesDatabase.Query().Departments.OrderBy(x => x.DeptNo).Select(x => x.DeptNo);
        var anonymousProjection = employeesDatabase.Query().Departments.OrderBy(x => x.DeptNo).Select(x => new { no = x.DeptNo, name = x.Name });

        await Assert.That(orderedDepartments.First().DeptNo).IsEqualTo("d001");
        await Assert.That(orderedDepartments.Last().DeptNo).IsEqualTo(lastDepartmentName);
        await Assert.That(projectedDepartmentNumbers.First()).IsEqualTo("d001");
        await Assert.That(projectedDepartmentNumbers.Last()).IsEqualTo(lastDepartmentName);
        await Assert.That(anonymousProjection.First().no).IsEqualTo("d001");
        await Assert.That(anonymousProjection.Last().no).IsEqualTo(lastDepartmentName);

        var firstElevenEmployees = employeesDatabase.Query().Employees.OrderBy(x => x.emp_no).Take(11).Select(x => x.emp_no).ToList();
        var skippedTenEmployees = employeesDatabase.Query().Employees
            .OrderBy(x => x.emp_no)
            .Where(x => firstElevenEmployees.Contains(x.emp_no))
            .Skip(1)
            .Take(10)
            .Select(x => x.emp_no)
            .ToList();

        await Assert.That(firstElevenEmployees.Count).IsEqualTo(11);
        await Assert.That(skippedTenEmployees.Count).IsEqualTo(10);
        await Assert.That(skippedTenEmployees[0]).IsEqualTo(firstElevenEmployees[1]);

        var orderedAscendingOrm = employeesDatabase.Query().Employees
            .Where(x => x.emp_no < 990000)
            .OrderBy(e => e.birth_date)
            .ThenBy(e => e.emp_no)
            .Skip(5)
            .Take(10)
            .ToList();

        var orderedAscendingList = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => x.emp_no < 990000)
            .OrderBy(e => e.birth_date)
            .ThenBy(e => e.emp_no)
            .Skip(5)
            .Take(10)
            .ToList();

        await AssertEmployeeSequenceEqual(orderedAscendingList, orderedAscendingOrm);

        var orderedDescendingOrm = employeesDatabase.Query().Employees
            .Where(x => x.emp_no < 990000)
            .OrderByDescending(e => e.birth_date)
            .ThenByDescending(e => e.emp_no)
            .Skip(5)
            .Take(10)
            .ToList();

        var orderedDescendingList = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => x.emp_no < 990000)
            .OrderByDescending(e => e.birth_date)
            .ThenByDescending(e => e.emp_no)
            .Skip(5)
            .Take(10)
            .ToList();

        await AssertEmployeeSequenceEqual(orderedDescendingList, orderedDescendingOrm);

        var skippedOrm = employeesDatabase.Query().Employees
            .Where(x => x.emp_no < 990000)
            .OrderBy(e => e.birth_date)
            .ThenBy(e => e.emp_no)
            .Skip(10)
            .ToList();

        var skippedList = employeesDatabase.Query().Employees
            .ToList()
            .Where(x => x.emp_no < 990000)
            .OrderBy(e => e.birth_date)
            .ThenBy(e => e.emp_no)
            .Skip(10)
            .ToList();

        await AssertEmployeeSequenceEqual(skippedList, skippedOrm);

        var topOrm = employeesDatabase.Query().Employees
            .OrderByDescending(e => e.hire_date)
            .Take(5)
            .ToList();

        var topList = employeesDatabase.Query().Employees
            .ToList()
            .OrderByDescending(e => e.hire_date)
            .Take(5)
            .ToList();

        await AssertEmployeeSequenceEqual(topList, topOrm);

        var complexOrm = employeesDatabase.Query().Employees
            .OrderBy(e => e.first_name)
            .ThenByDescending(e => e.birth_date)
            .Skip(5)
            .Take(10)
            .ToList();

        var complexList = employeesDatabase.Query().Employees
            .ToList()
            .OrderBy(e => e.first_name)
            .ThenByDescending(e => e.birth_date)
            .Skip(5)
            .Take(10)
            .ToList();

        await AssertEmployeeSequenceEqual(complexList, complexOrm);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_TwoPropertyComparisons_MatchInMemoryFiltering(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_TwoPropertyComparisons_MatchInMemoryFiltering),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;
        var managers = employeesDatabase.Query().Managers.OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList();
        var departmentEmployees = employeesDatabase.Query().DepartmentEmployees.OrderBy(x => x.emp_no).ThenBy(x => x.dept_no).ToList();

        await AssertManagerSequenceEqual(
            managers.Where(x => x.emp_no == x.emp_no).ToList(),
            employeesDatabase.Query().Managers.Where(x => x.emp_no == x.emp_no).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertManagerSequenceEqual(
            managers.Where(x => x.emp_no != x.emp_no).ToList(),
            employeesDatabase.Query().Managers.Where(x => x.emp_no != x.emp_no).OrderBy(x => x.emp_no).ThenBy(x => x.dept_fk).ToList());

        await AssertDeptEmployeeSequenceEqual(
            departmentEmployees.Where(x => x.emp_no == x.emp_no).Take(100).ToList(),
            employeesDatabase.Query().DepartmentEmployees.Where(x => x.emp_no == x.emp_no).OrderBy(x => x.emp_no).ThenBy(x => x.dept_no).Take(100).ToList());

        await AssertDeptEmployeeSequenceEqual(
            departmentEmployees.Where(x => x.emp_no != x.from_date.Day).ToList(),
            employeesDatabase.Query().DepartmentEmployees.Where(x => x.emp_no != x.from_date.Day).OrderBy(x => x.emp_no).ThenBy(x => x.dept_no).ToList());

        await AssertDeptEmployeeSequenceEqual(
            departmentEmployees.Where(x => x.emp_no > x.from_date.Day).ToList(),
            employeesDatabase.Query().DepartmentEmployees.Where(x => x.emp_no > x.from_date.Day).OrderBy(x => x.emp_no).ThenBy(x => x.dept_no).ToList());

        await AssertDeptEmployeeSequenceEqual(
            departmentEmployees.Where(x => x.emp_no <= x.from_date.Day).Take(100).ToList(),
            employeesDatabase.Query().DepartmentEmployees.Where(x => x.emp_no <= x.from_date.Day).OrderBy(x => x.emp_no).ThenBy(x => x.dept_no).Take(100).ToList());
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Query_UnsupportedTailAndWhileOperators_ThrowNotSupportedException(TestProviderDescriptor provider)
    {
        using var databaseScope = EmployeesTestDatabase.Create(
            provider,
            nameof(Query_UnsupportedTailAndWhileOperators_ThrowNotSupportedException),
            EmployeesSeedMode.Bogus);

        var employeesDatabase = databaseScope.Database;

        await AssertThrows<NotSupportedException>(() => employeesDatabase.Query().Employees.TakeLast(5).ToList());
        await AssertThrows<NotSupportedException>(() => employeesDatabase.Query().Employees.SkipLast(5).ToList());
        await AssertThrows<NotSupportedException>(() => employeesDatabase.Query().Employees.TakeWhile(e => e.first_name.StartsWith("A")).ToList());
        await AssertThrows<NotSupportedException>(() => employeesDatabase.Query().Employees.SkipWhile(e => e.first_name.StartsWith("A")).ToList());
    }

    private static async Task AssertEmployeeSequenceEqual(IReadOnlyList<Employee> expected, IReadOnlyList<Employee> actual)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            await Assert.That(actual[index].emp_no).IsEqualTo(expected[index].emp_no);
            await Assert.That(actual[index].first_name).IsEqualTo(expected[index].first_name);
            await Assert.That(actual[index].last_name).IsEqualTo(expected[index].last_name);
        }
    }

    private static async Task AssertManagerSequenceEqual(IReadOnlyList<Manager> expected, IReadOnlyList<Manager> actual)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            await Assert.That(actual[index].emp_no).IsEqualTo(expected[index].emp_no);
            await Assert.That(actual[index].dept_fk).IsEqualTo(expected[index].dept_fk);
            await Assert.That(actual[index].Type).IsEqualTo(expected[index].Type);
        }
    }

    private static async Task AssertDeptEmployeeSequenceEqual(IReadOnlyList<Dept_emp> expected, IReadOnlyList<Dept_emp> actual)
    {
        await Assert.That(actual.Count).IsEqualTo(expected.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            await Assert.That(actual[index].emp_no).IsEqualTo(expected[index].emp_no);
            await Assert.That(actual[index].dept_no).IsEqualTo(expected[index].dept_no);
            await Assert.That(actual[index].from_date).IsEqualTo(expected[index].from_date);
        }
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
