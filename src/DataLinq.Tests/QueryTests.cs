using DataLinq.Tests.Models;
using System;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace DataLinq.Tests
{
    public class QueryTests : BaseTests
    {
        private string lastDepartmentName;

        private void SharedSetup(Database<Employees> employeesDb)
        {
            lastDepartmentName = $"d{employeesDb.Query().Departments.Count():000}";
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void ToList(Database<Employees> employeesDb)
        {
            Assert.True(10 < employeesDb.Query().Departments.ToList().Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void Count(Database<Employees> employeesDb)
        {
            Assert.True(10 < employeesDb.Query().Departments.Count());
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhere(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Departments.Where(x => x.DeptNo == "d005").ToList();
            Assert.Single(where);
            Assert.Equal("d005", where[0].DeptNo);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereReverse(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Departments.Where(x => "d005" == x.DeptNo).ToList();
            Assert.Single(where);
            Assert.Equal("d005", where[0].DeptNo);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereNot(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Departments.Where(x => x.DeptNo != "d005").ToList();
            Assert.Equal(employeesDb.Query().Departments.Count() - 1, where.Count);
            Assert.DoesNotContain(where, x => x.DeptNo == "d005");
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereStartsWith(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Departments.Where(x => x.DeptNo.StartsWith("d00")).ToList();
            Assert.Equal(9, where.Count);
            Assert.DoesNotContain(where, x => x.DeptNo == "d010");
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void WhereStartsWithAndToList(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => x.dept_fk.StartsWith("d00") && x.from_date > DateOnly.Parse("2010-01-01")).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => x.dept_fk.StartsWith("d00") && x.from_date > DateOnly.Parse("2010-01-01")), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void WhereNotStartsWithAndToList(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && x.from_date > DateOnly.Parse("2010-01-01")).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && x.from_date > DateOnly.Parse("2010-01-01")), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void WhereNotStartsWithAndStartsWithAndToList(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && x.from_date > DateOnly.Parse("2010-01-01")).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && x.from_date > DateOnly.Parse("2010-01-01")), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void WhereNotStartsWithAndStartsWithAndNotToList(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && !(x.from_date > DateOnly.Parse("2010-01-01"))).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && x.dept_fk.EndsWith("2") && !(x.from_date > DateOnly.Parse("2010-01-01"))), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void WhereNotStartsWithAndGroupNotStartsWithToList(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && (x.from_date > DateOnly.Parse("2010-01-01")))).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && (x.from_date > DateOnly.Parse("2010-01-01")))), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void WhereNotStartsWithAndGroupNotStartsWithAndNotToList(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && !(x.from_date > DateOnly.Parse("2010-01-01")))).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") && !(x.from_date > DateOnly.Parse("2010-01-01")))), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void WhereNotStartsWithOrGroupNotStartsWithToList(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") && (x.from_date > DateOnly.Parse("2010-01-01")))).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") && (x.from_date > DateOnly.Parse("2010-01-01")))), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void WhereNotStartsWithAndGroupNotStartsWithOrNotToList(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") || !(x.from_date > DateOnly.Parse("2010-01-01")))).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") && !(x.dept_fk.EndsWith("2") || !(x.from_date > DateOnly.Parse("2010-01-01")))), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void WhereNotStartsWithOrGroupNotStartsWithOrNotToList(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") || !(x.from_date > DateOnly.Parse("2010-01-01")))).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Count(x => !x.dept_fk.StartsWith("d00") || !(x.dept_fk.EndsWith("2") || !(x.from_date > DateOnly.Parse("2010-01-01")))), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereTwoPropertiesEquals(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => x.emp_no == x.emp_no).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => x.emp_no == x.emp_no).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereTwoPropertiesNotEquals(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => x.emp_no != x.emp_no).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => x.emp_no != x.emp_no).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereIntEnumEqualsBackwards(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => Manager.ManagerType.FestiveManager == x.Type).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => Manager.ManagerType.FestiveManager == x.Type).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereIntEnumNotEqualsBackwards(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => Manager.ManagerType.AssistantManager != x.Type).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => Manager.ManagerType.AssistantManager != x.Type).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereIntEnumEquals(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => x.Type == Manager.ManagerType.FestiveManager).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => x.Type == Manager.ManagerType.FestiveManager).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereIntEnumNotEquals(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => x.Type != Manager.ManagerType.AssistantManager).ToList();
            Assert.Equal(employeesDb.Query().Managers.ToList().Where(x => x.Type != Manager.ManagerType.AssistantManager).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereValueEnumTypeEquals(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Employees.Where(x => x.gender.Value == Employee.Employeegender.M).ToList();
            Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => x.gender.Value == Employee.Employeegender.M).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereValueEnumTypeNotEquals(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Employees.Where(x => x.gender.Value != Employee.Employeegender.M).ToList();
            Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => x.gender.Value != Employee.Employeegender.M).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereValueEqualsNegated(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Employees.Where(x => !(x.gender.Value == Employee.Employeegender.F)).ToList();
            Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => !(x.gender.Value == Employee.Employeegender.F)).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereValueNotEqualsNegated(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Employees.Where(x => !(x.gender.Value != Employee.Employeegender.F)).ToList();
            Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => !(x.gender.Value != Employee.Employeegender.F)).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereHasValue(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Employees.Where(x => x.gender.HasValue).ToList();
            Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => x.gender.HasValue).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereNotHasValue(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Employees.Where(x => !x.gender.HasValue).ToList();
            Assert.Equal(employeesDb.Query().Employees.ToList().Where(x => !x.gender.HasValue).Count(), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereNotStartsWith(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Departments.Where(x => !x.DeptNo.StartsWith("d00")).ToList();
            Assert.Equal(11, where.Count);
            Assert.DoesNotContain(where, x => x.DeptNo == "d001");
        }


        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereEndsWith(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Departments.Where(x => x.DeptNo.EndsWith("2")).ToList();
            Assert.Equal(2, where.Count);
            Assert.DoesNotContain(where, x => x.DeptNo == "d010");
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SimpleWhereNotEndsWith(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Departments.Where(x => !x.DeptNo.EndsWith("2")).ToList();
            Assert.Equal(18, where.Count);
            Assert.DoesNotContain(where, x => x.DeptNo == "d002");
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void WhereAndToList(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => x.dept_fk == "d004" && x.from_date > DateOnly.Parse("2010-01-01")).ToList();
            Assert.NotEqual(employeesDb.Query().Managers.Count(x => x.dept_fk == "d004"), where.Count);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void WhereAndCount(Database<Employees> employeesDb)
        {
            var where = employeesDb.Query().Managers.Where(x => x.dept_fk == "d004" && x.from_date > DateOnly.Parse("2010-01-01"));
            Assert.NotEqual(employeesDb.Query().Managers.Count(x => x.dept_fk == "d004"), where.Count());
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void Single(Database<Employees> employeesDb)
        {
            var dept = employeesDb.Query().Departments.Single(x => x.DeptNo == "d005");
            Assert.NotNull(dept);
            Assert.Equal("d005", dept.DeptNo);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SingleOrDefault(Database<Employees> employeesDb)
        {
            var dept = employeesDb.Query().Departments.SingleOrDefault(x => x.DeptNo == "d005");
            Assert.NotNull(dept);
            Assert.Equal("d005", dept.DeptNo);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SingleOrDefaultNull(Database<Employees> employeesDb)
        {
            var dept = employeesDb.Query().Departments.SingleOrDefault(x => x.DeptNo == "1234");
            Assert.Null(dept);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SingleThrow(Database<Employees> employeesDb)
        {
            Assert.Throws<InvalidOperationException>(() => employeesDb.Query().salaries.Single(x => x.salary > 70000));
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void SingleOrDefaultThrow(Database<Employees> employeesDb)
        {
            Assert.Throws<InvalidOperationException>(() => employeesDb.Query().salaries.SingleOrDefault(x => x.salary > 70000));
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void First(Database<Employees> employeesDb)
        {
            var salary = employeesDb.Query().salaries.First(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.True(70000 <= salary.salary);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void FirstOrDefault(Database<Employees> employeesDb)
        {
            var salary = employeesDb.Query().salaries.FirstOrDefault(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.True(70000 <= salary.salary);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void FirstOrderBy(Database<Employees> employeesDb)
        {
            var salary = employeesDb.Query().salaries.OrderBy(x => x.salary).First(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.True(70000 <= salary.salary);
            Assert.NotEqual(salary.salary, employeesDb.Query().salaries.First(x => x.salary > 70000).salary);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void FirstOrDefaultOrderBy(Database<Employees> employeesDb)
        {
            var salary = employeesDb.Query().salaries.OrderBy(x => x.salary).FirstOrDefault(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.True(70000 <= salary.salary);
            Assert.NotEqual(salary.salary, employeesDb.Query().salaries.FirstOrDefault(x => x.salary > 70000).salary);
            Assert.NotEqual(salary.salary, employeesDb.Query().salaries.OrderBy(x => x.salary).LastOrDefault(x => x.salary > 70000).salary);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void LastOrDefaultOrderBy(Database<Employees> employeesDb)
        {
            var salary = employeesDb.Query().salaries.OrderByDescending(x => x.salary).LastOrDefault(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.True(70000 <= salary.salary);
            Assert.NotEqual(salary.salary, employeesDb.Query().salaries.FirstOrDefault(x => x.salary > 70000).salary);
            Assert.NotEqual(salary.salary, employeesDb.Query().salaries.OrderByDescending(x => x.salary).FirstOrDefault(x => x.salary > 70000).salary);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void FirstOrDefaultNull(Database<Employees> employeesDb)
        {
            var salary = employeesDb.Query().salaries.FirstOrDefault(x => x.salary < 10000);
            Assert.Null(salary);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void LastOrDefaultNull(Database<Employees> employeesDb)
        {
            var salary = employeesDb.Query().salaries.LastOrDefault(x => x.salary < 10000);
            Assert.Null(salary);
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void Any(Database<Employees> employeesDb)
        {
            Assert.True(employeesDb.Query().Departments.Any(x => x.DeptNo == "d005"));
            Assert.True(employeesDb.Query().Departments.Where(x => x.DeptNo == "d005").Any());
            Assert.False(employeesDb.Query().Departments.Any(x => x.DeptNo == "not_existing"));
            Assert.False(employeesDb.Query().Departments.Where(x => x.DeptNo == "not_existing").Any());
        }

        [Theory]
        [MemberData(nameof(GetEmployees))]
        public void OrderBy(Database<Employees> employeesDb)
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
        public void OrderBySelect(Database<Employees> employeesDb)
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
        public void OrderBySelectAnonymous(Database<Employees> employeesDb)
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

        //[Fact]
        //public void Any()
        //{
        //    employeesDb.Query().departments.Select(x => x.dept_no)

        //    Assert.True();
        //    Assert.True(employeesDb.Query().departments.Where(x => x.dept_no == "d005").Any());
        //    Assert.False(employeesDb.Query().departments.Any(x => x.dept_no == "not_existing"));
        //    Assert.False(employeesDb.Query().departments.Where(x => x.dept_no == "not_existing").Any());
        //}
    }
}