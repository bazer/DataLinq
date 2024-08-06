using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Tests.Models;
using Xunit;

namespace DataLinq.Tests;

public class QueryTests : BaseTests
{
    private string lastDepartmentName;

    private void SharedSetup(Database<EmployeesDb> employeesDb)
    {
        lastDepartmentName = $"d{employeesDb.Query().Departments.Count():000}";
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void ToList(Database<EmployeesDb> employeesDb)
    {
        Assert.True(10 < employeesDb.Query().Departments.ToList().Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void ToListView(Database<EmployeesDb> employeesDb)
    {
        Assert.NotEmpty(employeesDb.Query().current_dept_emp.ToList());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Count(Database<EmployeesDb> employeesDb)
    {
        Assert.True(10 < employeesDb.Query().Departments.Count());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void CountView(Database<EmployeesDb> employeesDb)
    {
        Assert.NotEqual(0, employeesDb.Query().dept_emp_latest_date.Count());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhere(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Departments.Where(x => x.DeptNo == "d005").ToList();
        Assert.Single(where);
        Assert.Equal("d005", where[0].DeptNo);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereReverse(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Departments.Where(x => "d005" == x.DeptNo).ToList();
        Assert.Single(where);
        Assert.Equal("d005", where[0].DeptNo);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereNot(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Departments.Where(x => x.DeptNo != "d005").ToList();
        Assert.Equal(employeesDb.Query().Departments.Count() - 1, where.Count);
        Assert.DoesNotContain(where, x => x.DeptNo == "d005");
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereStartsWith(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Departments.Where(x => x.DeptNo.StartsWith("d00")).ToList();
        Assert.Equal(9, where.Count);
        Assert.DoesNotContain(where, x => x.DeptNo == "d010");
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereStartsWithAndToList(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => x.dept_fk.StartsWith("d00") && x.from_date > DateOnly.Parse("2010-01-01")).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => x.dept_fk.StartsWith("d00") && x.from_date > DateOnly.Parse("2010-01-01")), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereNotStartsWithAndToList(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && x.from_date > DateOnly.Parse("2010-01-01")).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && x.from_date > DateOnly.Parse("2010-01-01")), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereNotStartsWithAndStartsWithAndToList(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && x.from_date > DateOnly.Parse("2010-01-01")).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && x.from_date > DateOnly.Parse("2010-01-01")), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereNotStartsWithAndStartsWithAndNotToList(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && !(x.from_date > DateOnly.Parse("2010-01-01"))).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && !(x.from_date > DateOnly.Parse("2010-01-01"))), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereNotStartsWithAndGroupNotStartsWithToList(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && (x.from_date > DateOnly.Parse("2010-01-01")))).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && (x.from_date > DateOnly.Parse("2010-01-01")))), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereNotStartsWithAndGroupNotStartsWithAndNotToList(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && !(x.from_date > DateOnly.Parse("2010-01-01")))).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && !(x.from_date > DateOnly.Parse("2010-01-01")))), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereNotStartsWithOrGroupNotStartsWithToList(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") && (x.from_date > DateOnly.Parse("2010-01-01")))).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") && (x.from_date > DateOnly.Parse("2010-01-01")))), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereNotStartsWithAndGroupNotStartsWithOrNotToList(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") || !(x.from_date > DateOnly.Parse("2010-01-01")))).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") || !(x.from_date > DateOnly.Parse("2010-01-01")))), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereNotStartsWithOrGroupNotStartsWithOrNotToList(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") || !(x.from_date > DateOnly.Parse("2010-01-01")))).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") || !(x.from_date > DateOnly.Parse("2010-01-01")))), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereTwoPropertiesEquals(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => x.emp_no == x.emp_no).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => x.emp_no == x.emp_no).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereTwoPropertiesNotEquals(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => x.emp_no != x.emp_no).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => x.emp_no != x.emp_no).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereIntEnumEqualsBackwards(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => ManagerType.FestiveManager == x.Type).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => ManagerType.FestiveManager == x.Type).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereIntEnumNotEqualsBackwards(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => ManagerType.AssistantManager != x.Type).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => ManagerType.AssistantManager != x.Type).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereIntEnumEquals(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => x.Type == ManagerType.FestiveManager).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => x.Type == ManagerType.FestiveManager).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereIntEnumNotEquals(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => x.Type != ManagerType.AssistantManager).ToList();
        Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => x.Type != ManagerType.AssistantManager).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereValueEnumTypeEquals(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Employees.Where(x => x.gender.Value == Employee.Employeegender.M).ToList();
        Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => x.gender.Value == Employee.Employeegender.M).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereValueEnumTypeNotEquals(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Employees.Where(x => x.gender.Value != Employee.Employeegender.M).ToList();
        Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => x.gender.Value != Employee.Employeegender.M).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereValueEqualsNegated(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Employees.Where(x => !(x.gender.Value == Employee.Employeegender.F)).ToList();
        Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => !(x.gender.Value == Employee.Employeegender.F)).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereValueNotEqualsNegated(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Employees.Where(x => !(x.gender.Value != Employee.Employeegender.F)).ToList();
        Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => !(x.gender.Value != Employee.Employeegender.F)).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereHasValue(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Employees.Where(x => x.gender.HasValue).ToList();
        Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => x.gender.HasValue).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereNotHasValue(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Employees.Where(x => !x.gender.HasValue).ToList();
        Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => !x.gender.HasValue).Count(), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereNotStartsWith(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Departments.Where(x => !x.DeptNo.StartsWith("d00")).ToList();
        Assert.Equal(11, where.Count);
        Assert.DoesNotContain(where, x => x.DeptNo == "d001");
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereEndsWith(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Departments.Where(x => x.DeptNo.EndsWith("2")).ToList();
        Assert.Equal(2, where.Count);
        Assert.DoesNotContain(where, x => x.DeptNo == "d010");
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereNotEndsWith(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Departments.Where(x => !x.DeptNo.EndsWith("2")).ToList();
        Assert.Equal(18, where.Count);
        Assert.DoesNotContain(where, x => x.DeptNo == "d002");
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereContains(Database<EmployeesDb> employeesDb)
    {
        var ids = new[] { "d001", "d002", "d003" };
        var where = employeesDb.Query().Departments.Where(x => ids.Contains(x.DeptNo)).ToList();

        Assert.Equal(ids.Length, where.Count);
        foreach (var id in ids)
        {
            Assert.Contains(where, x => x.DeptNo == id);
        }
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SimpleWhereNotContains(Database<EmployeesDb> employeesDb)
    {
        var ids = new[] { "d001", "d002", "d003" };
        var where = employeesDb.Query().Departments.Where(x => !ids.Contains(x.DeptNo)).ToList();

        Assert.True(where.Count > ids.Length);
        foreach (var id in ids)
        {
            Assert.DoesNotContain(where, x => x.DeptNo == id);
        }
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereContainsWithList(Database<EmployeesDb> employeesDb)
    {
        var ids = new List<string> { "d001", "d002", "d003" };
        var where = employeesDb.Query().Departments.Where(x => ids.Contains(x.DeptNo)).ToList();

        Assert.Equal(ids.Count, where.Count);
        foreach (var id in ids)
        {
            Assert.Contains(where, x => x.DeptNo == id);
        }
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereContainsWithHashSet(Database<EmployeesDb> employeesDb)
    {
        var ids = new HashSet<string> { "d001", "d002", "d003" };
        var where = employeesDb.Query().Departments.Where(x => ids.Contains(x.DeptNo)).ToList();

        Assert.Equal(ids.Count, where.Count);
        foreach (var id in ids)
        {
            Assert.Contains(where, x => x.DeptNo == id);
        }
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereMultipleContains(Database<EmployeesDb> employeesDb)
    {
        var deptIds = new[] { "d001", "d002", "d003" };
        var empIds = new[] { 5, 2668, 100 };
        var where = employeesDb.Query().DepartmentEmployees
            .Where(x => deptIds.Contains(x.dept_no) && empIds.Contains(x.emp_no))
            .ToList();

        Assert.Equal(deptIds.Length, where.Count);
        foreach (var id in deptIds)
        {
            Assert.Contains(where, x => x.dept_no == id);
        }
        foreach (var id in empIds)
        {
            Assert.Contains(where, x => x.emp_no == id);
        }
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereContainsAndStartsWith(Database<EmployeesDb> employeesDb)
    {
        var deptIds = new[] { "d001", "d002", "d003" };
        var where = employeesDb.Query().DepartmentEmployees
            .Where(x => deptIds.Contains(x.dept_no) && x.dept_no.EndsWith("02"))
            .ToList();

        Assert.Contains(where, x => x.dept_no == "d002");
        Assert.DoesNotContain(where, x => x.dept_no == "d001");
        Assert.DoesNotContain(where, x => x.dept_no == "d003");
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereContainsAndGreaterThan(Database<EmployeesDb> employeesDb)
    {
        var deptIds = new[] { "d001", "d002", "d003" };
        var where = employeesDb.Query().DepartmentEmployees
            .Where(x => deptIds.Contains(x.dept_no) && x.emp_no >= 1000 && x.emp_no <= 2000)
            .ToList();

        Assert.True(where.Count >= deptIds.Length);
        foreach (var id in deptIds)
        {
            Assert.Contains(where, x => x.dept_no == id);
            Assert.DoesNotContain(where, x => x.emp_no < 1000 || x.emp_no > 2000);
        }
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereMultipleContainsAndStartsWith(Database<EmployeesDb> employeesDb)
    {
        var deptIds = new[] { "d001", "d002", "d003" };
        var empIds = new[] { 5, 2668, 100 };
        var where = employeesDb.Query().DepartmentEmployees
            .Where(x => deptIds.Contains(x.dept_no) && empIds.Contains(x.emp_no) && x.dept_no.StartsWith("d"))
            .ToList();

        Assert.Equal(deptIds.Length, where.Count);
        foreach (var id in deptIds)
        {
            Assert.Contains(where, x => x.dept_no == id);
        }
        foreach (var id in empIds)
        {
            Assert.Contains(where, x => x.emp_no == id);
        }
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TestContainsAndNullableBool(Database<EmployeesDb> employeesDb)
    {
        int?[] empIds = [5, 2668, 10, 100];

        foreach (var title in employeesDb.Query().Employees.Where(x => empIds.Contains(x.emp_no)))
            employeesDb.Update(title, x => x.IsDeleted = null);

        foreach (var title in employeesDb.Query().Employees.Where(x => x.emp_no == 100))
            employeesDb.Update(title, x => x.IsDeleted = true);

        //Should return all rows except the one with emp_no == 100
        var result = employeesDb.Query().Employees
            .Where(x => empIds.Contains(x.emp_no) && x.IsDeleted != true)
            .ToList();

        var resultList = employeesDb.Query().Employees.ToList()
            .Where(x => empIds.Contains(x.emp_no) && x.IsDeleted != true)
            .ToList();

        Assert.NotEmpty(result);
        Assert.DoesNotContain(result, x => x.emp_no == 100);
        Assert.Equal(resultList, result);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereAndToList(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => x.dept_fk == "d004" && x.from_date > DateOnly.Parse("2010-01-01")).ToList();
        Assert.NotEqual(employeesDb.Query().Managers.Count(x => x.dept_fk == "d004"), where.Count);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void WhereAndCount(Database<EmployeesDb> employeesDb)
    {
        var where = employeesDb.Query().Managers.Where(x => x.dept_fk == "d004" && x.from_date > DateOnly.Parse("2010-01-01"));
        Assert.NotEqual(employeesDb.Query().Managers.Count(x => x.dept_fk == "d004"), where.Count());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Single(Database<EmployeesDb> employeesDb)
    {
        var dept = employeesDb.Query().Departments.Single(x => x.DeptNo == "d005");
        Assert.NotNull(dept);
        Assert.Equal("d005", dept.DeptNo);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SingleOrDefault(Database<EmployeesDb> employeesDb)
    {
        var dept = employeesDb.Query().Departments.SingleOrDefault(x => x.DeptNo == "d005");
        Assert.NotNull(dept);
        Assert.Equal("d005", dept.DeptNo);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SingleOrDefaultNull(Database<EmployeesDb> employeesDb)
    {
        var dept = employeesDb.Query().Departments.SingleOrDefault(x => x.DeptNo == "1234");
        Assert.Null(dept);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SingleThrow(Database<EmployeesDb> employeesDb)
    {
        Assert.Throws<InvalidOperationException>(() => employeesDb.Query().salaries.Single(x => x.salary > 70000));
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SingleOrDefaultThrow(Database<EmployeesDb> employeesDb)
    {
        Assert.Throws<InvalidOperationException>(() => employeesDb.Query().salaries.SingleOrDefault(x => x.salary > 70000));
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void First(Database<EmployeesDb> employeesDb)
    {
        var salary = employeesDb.Query().salaries.First(x => x.salary > 70000);
        Assert.NotNull(salary);
        Assert.True(70000 <= salary.salary);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void FirstOrDefault(Database<EmployeesDb> employeesDb)
    {
        var salary = employeesDb.Query().salaries.FirstOrDefault(x => x.salary > 70000);
        Assert.NotNull(salary);
        Assert.True(70000 <= salary.salary);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void FirstOrderBy(Database<EmployeesDb> employeesDb)
    {
        var salary = employeesDb.Query().salaries.OrderBy(x => x.salary).First(x => x.salary > 70000);
        Assert.NotNull(salary);
        Assert.True(70000 <= salary.salary);
        Assert.NotEqual(salary.salary, employeesDb.Query().salaries.First(x => x.salary > 70000).salary);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void FirstOrDefaultOrderBy(Database<EmployeesDb> employeesDb)
    {
        var salary = employeesDb.Query().salaries.OrderBy(x => x.salary).FirstOrDefault(x => x.salary > 70000);
        Assert.NotNull(salary);
        Assert.True(70000 <= salary.salary);
        Assert.NotEqual(salary.salary, employeesDb.Query().salaries.FirstOrDefault(x => x.salary > 70000).salary);
        Assert.NotEqual(salary.salary, employeesDb.Query().salaries.OrderBy(x => x.salary).LastOrDefault(x => x.salary > 70000).salary);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void LastOrDefaultOrderBy(Database<EmployeesDb> employeesDb)
    {
        var salary = employeesDb.Query().salaries.OrderByDescending(x => x.salary).LastOrDefault(x => x.salary > 70000);
        Assert.NotNull(salary);
        Assert.True(70000 <= salary.salary);
        Assert.NotEqual(salary.salary, employeesDb.Query().salaries.FirstOrDefault(x => x.salary > 70000).salary);
        Assert.NotEqual(salary.salary, employeesDb.Query().salaries.OrderByDescending(x => x.salary).FirstOrDefault(x => x.salary > 70000).salary);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void FirstOrDefaultNull(Database<EmployeesDb> employeesDb)
    {
        var salary = employeesDb.Query().salaries.FirstOrDefault(x => x.salary < 10000);
        Assert.Null(salary);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void LastOrDefaultNull(Database<EmployeesDb> employeesDb)
    {
        var salary = employeesDb.Query().salaries.LastOrDefault(x => x.salary < 10000);
        Assert.Null(salary);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void Any(Database<EmployeesDb> employeesDb)
    {
        Assert.True(employeesDb.Query().Departments.Any(x => x.DeptNo == "d005"));
        Assert.True(employeesDb.Query().Departments.Where(x => x.DeptNo == "d005").Any());
        Assert.False(employeesDb.Query().Departments.Any(x => x.DeptNo == "not_existing"));
        Assert.False(employeesDb.Query().Departments.Where(x => x.DeptNo == "not_existing").Any());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void OrderBy(Database<EmployeesDb> employeesDb)
    {
        SharedSetup(employeesDb);

        var deptByDeptNo = employeesDb.Query().Departments.OrderBy(x => x.DeptNo);
        Assert.Equal("d001", deptByDeptNo.First().DeptNo);
        Assert.Equal("d001", deptByDeptNo.FirstOrDefault().DeptNo);
        Assert.Equal(lastDepartmentName, deptByDeptNo.Last().DeptNo);
        Assert.Equal(lastDepartmentName, deptByDeptNo.LastOrDefault().DeptNo);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void OrderBySelect(Database<EmployeesDb> employeesDb)
    {
        SharedSetup(employeesDb);

        var deptByDeptNo = employeesDb.Query().Departments.OrderBy(x => x.DeptNo).Select(x => x.DeptNo);
        Assert.Equal("d001", deptByDeptNo.First());
        Assert.Equal("d001", deptByDeptNo.FirstOrDefault());
        Assert.Equal(lastDepartmentName, deptByDeptNo.Last());
        Assert.Equal(lastDepartmentName, deptByDeptNo.LastOrDefault());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void OrderBySelectAnonymous(Database<EmployeesDb> employeesDb)
    {
        SharedSetup(employeesDb);

        var deptByDeptNo = employeesDb.Query().Departments.OrderBy(x => x.DeptNo).Select(x => new
        {
            no = x.DeptNo,
            name = x.Name
        });
        Assert.Equal("d001", deptByDeptNo.First().no);
        Assert.Equal("d001", deptByDeptNo.FirstOrDefault().no);
        Assert.Equal(lastDepartmentName, deptByDeptNo.Last().no);
        Assert.Equal(lastDepartmentName, deptByDeptNo.LastOrDefault().no);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TakeAndSkip(Database<EmployeesDb> employeesDb)
    {
        var tenEmployees = employeesDb.Query().Employees.Take(10).ToList();
        Assert.Equal(10, tenEmployees.Count);

        var tenEmployeesSkip1 = employeesDb.Query().Employees.Skip(1).Take(10).ToList();
        Assert.Equal(10, tenEmployeesSkip1.Count);
        Assert.Equal(tenEmployees[1], tenEmployeesSkip1[0]);
        Assert.Same(tenEmployees[1], tenEmployeesSkip1[0]);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SkipAndTakeWithOrderBy(Database<EmployeesDb> employeesDb)
    {
        var employeesOrderedOrm = employeesDb.Query().Employees.OrderBy(e => e.birth_date).Skip(5).Take(10).ToList();
        var employeesOrderedList = employeesDb.Query().Employees.ToList().OrderBy(e => e.birth_date).Skip(5).Take(10).ToList();

        Assert.Equal(employeesOrderedList, employeesOrderedOrm);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SkipAndTakeWithOrderByDescending(Database<EmployeesDb> employeesDb)
    {
        var employeesOrderedDescOrm = employeesDb.Query().Employees.OrderByDescending(e => e.birth_date).Skip(5).Take(10).ToList();
        var employeesOrderedDescList = employeesDb.Query().Employees.ToList().OrderByDescending(e => e.birth_date).Skip(5).Take(10).ToList();

        Assert.Equal(employeesOrderedDescList, employeesOrderedDescOrm);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SkipWithOrderBy(Database<EmployeesDb> employeesDb)
    {
        var employeesSkippedOrm = employeesDb.Query().Employees.OrderBy(e => e.birth_date).Skip(10).ToList();
        var employeesSkippedList = employeesDb.Query().Employees.ToList().OrderBy(e => e.birth_date).Skip(10).ToList();

        Assert.Equal(employeesSkippedList, employeesSkippedOrm);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TakeWithOrderByDescending(Database<EmployeesDb> employeesDb)
    {
        var topEmployeesOrm = employeesDb.Query().Employees.OrderByDescending(e => e.hire_date).Take(5).ToList();
        var topEmployeesList = employeesDb.Query().Employees.ToList().OrderByDescending(e => e.hire_date).Take(5).ToList();

        Assert.Equal(topEmployeesList, topEmployeesOrm);
    }


    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void ComplexQueryWithTakeSkipAndMultipleOrderings(Database<EmployeesDb> employeesDb)
    {
        var complexQueryResultOrm = employeesDb.Query().Employees
            .OrderBy(e => e.first_name)
            .ThenByDescending(e => e.birth_date)
            .Skip(5)
            .Take(10)
            .ToList();

        var complexQueryResultList = employeesDb.Query().Employees.ToList()
            .OrderBy(e => e.first_name)
            .ThenByDescending(e => e.birth_date)
            .Skip(5)
            .Take(10)
            .ToList();

        Assert.Equal(complexQueryResultList, complexQueryResultOrm);
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TakeLastThrowsNotImplementedException(Database<EmployeesDb> employeesDb)
    {
        Assert.Throws<NotSupportedException>(() =>
            employeesDb.Query().Employees.TakeLast(5).ToList());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SkipLastThrowsNotImplementedException(Database<EmployeesDb> employeesDb)
    {
        Assert.Throws<NotSupportedException>(() =>
            employeesDb.Query().Employees.SkipLast(5).ToList());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void TakeWhileThrowsNotImplementedException(Database<EmployeesDb> employeesDb)
    {
        Assert.Throws<NotSupportedException>(() =>
            employeesDb.Query().Employees.TakeWhile(e => e.first_name.StartsWith("A")).ToList());
    }

    [Theory]
    [MemberData(nameof(GetEmployees))]
    public void SkipWhileThrowsNotImplementedException(Database<EmployeesDb> employeesDb)
    {
        Assert.Throws<NotSupportedException>(() =>
            employeesDb.Query().Employees.SkipWhile(e => e.first_name.StartsWith("A")).ToList());
    }
}