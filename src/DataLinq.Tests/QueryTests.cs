using System;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.Tests.Models;
using Xunit;

namespace DataLinq.Tests
{
    public class QueryTests : IClassFixture<DatabaseFixture>
    {
        private readonly DatabaseFixture fixture;
        private string lastDepartmentName;

        public QueryTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
            lastDepartmentName = $"d{fixture.employeesDb.Query().departments.Count():000}";
        }

        [Fact]
        public void ToList()
        {
            Assert.True(10 < fixture.employeesDb.Query().departments.ToList().Count);
        }

        [Fact]
        public void Count()
        {
            Assert.True(10 < fixture.employeesDb.Query().departments.Count());
        }

        [Fact]
        public void SimpleWhere()
        {
            var where = fixture.employeesDb.Query().departments.Where(x => x.dept_no == "d005").ToList();
            Assert.Single(where);
            Assert.Equal("d005", where[0].dept_no);
        }

        [Fact]
        public void SimpleWhereReverse()
        {
            var where = fixture.employeesDb.Query().departments.Where(x => "d005" == x.dept_no).ToList();
            Assert.Single(where);
            Assert.Equal("d005", where[0].dept_no);
        }

        [Fact]
        public void SimpleWhereNot()
        {
            var where = fixture.employeesDb.Query().departments.Where(x => x.dept_no != "d005").ToList();
            Assert.Equal(fixture.employeesDb.Query().departments.Count() - 1, where.Count);
            Assert.DoesNotContain(where, x => x.dept_no == "d005");
        }

        [Fact]
        public void WhereAndToList()
        {
            var where = fixture.employeesDb.Query().dept_manager.Where(x => x.dept_no == "d004" && x.from_date > DateOnly.Parse("2010-01-01")).ToList();
            Assert.NotEqual(fixture.employeesDb.Query().dept_manager.Count(x => x.dept_no == "d004"), where.Count);
        }

        [Fact]
        public void WhereAndCount()
        {
            var where = fixture.employeesDb.Query().dept_manager.Where(x => x.dept_no == "d004" && x.from_date > DateOnly.Parse("2010-01-01"));
            Assert.NotEqual(fixture.employeesDb.Query().dept_manager.Count(x => x.dept_no == "d004"), where.Count());
        }

        [Fact]
        public void Single()
        {
            var dept = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d005");
            Assert.NotNull(dept);
            Assert.Equal("d005", dept.dept_no);
        }

        [Fact]
        public void SingleOrDefault()
        {
            var dept = fixture.employeesDb.Query().departments.SingleOrDefault(x => x.dept_no == "d005");
            Assert.NotNull(dept);
            Assert.Equal("d005", dept.dept_no);
        }

        [Fact]
        public void SingleOrDefaultNull()
        {
            var dept = fixture.employeesDb.Query().departments.SingleOrDefault(x => x.dept_no == "1234");
            Assert.Null(dept);
        }

        [Fact]
        public void SingleThrow()
        {
            Assert.Throws<InvalidOperationException>(() => fixture.employeesDb.Query().salaries.Single(x => x.salary > 70000));
        }

        [Fact]
        public void SingleOrDefaultThrow()
        {
            Assert.Throws<InvalidOperationException>(() => fixture.employeesDb.Query().salaries.SingleOrDefault(x => x.salary > 70000));
        }

        [Fact]
        public void First()
        {
            var salary = fixture.employeesDb.Query().salaries.First(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.True(70000 <= salary.salary);
        }

        [Fact]
        public void FirstOrDefault()
        {
            var salary = fixture.employeesDb.Query().salaries.FirstOrDefault(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.True(70000 <= salary.salary);
        }

        [Fact]
        public void FirstOrderBy()
        {
            var salary = fixture.employeesDb.Query().salaries.OrderBy(x => x.salary).First(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.True(70000 <= salary.salary);
            Assert.NotEqual(salary.salary, fixture.employeesDb.Query().salaries.First(x => x.salary > 70000).salary);
        }

        [Fact]
        public void FirstOrDefaultOrderBy()
        {
            var salary = fixture.employeesDb.Query().salaries.OrderBy(x => x.salary).FirstOrDefault(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.True(70000 <= salary.salary);
            Assert.NotEqual(salary.salary, fixture.employeesDb.Query().salaries.FirstOrDefault(x => x.salary > 70000).salary);
            Assert.NotEqual(salary.salary, fixture.employeesDb.Query().salaries.OrderBy(x => x.salary).LastOrDefault(x => x.salary > 70000).salary);
        }

        [Fact]
        public void LastOrDefaultOrderBy()
        {
            var salary = fixture.employeesDb.Query().salaries.OrderByDescending(x => x.salary).LastOrDefault(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.True(70000 <= salary.salary);
            Assert.NotEqual(salary.salary, fixture.employeesDb.Query().salaries.FirstOrDefault(x => x.salary > 70000).salary);
            Assert.NotEqual(salary.salary, fixture.employeesDb.Query().salaries.OrderByDescending(x => x.salary).FirstOrDefault(x => x.salary > 70000).salary);
        }

        [Fact]
        public void FirstOrDefaultNull()
        {
            var salary = fixture.employeesDb.Query().salaries.FirstOrDefault(x => x.salary < 10000);
            Assert.Null(salary);
        }

        [Fact]
        public void LastOrDefaultNull()
        {
            var salary = fixture.employeesDb.Query().salaries.LastOrDefault(x => x.salary < 10000);
            Assert.Null(salary);
        }

        [Fact]
        public void Any()
        {
            Assert.True(fixture.employeesDb.Query().departments.Any(x => x.dept_no == "d005"));
            Assert.True(fixture.employeesDb.Query().departments.Where(x => x.dept_no == "d005").Any());
            Assert.False(fixture.employeesDb.Query().departments.Any(x => x.dept_no == "not_existing"));
            Assert.False(fixture.employeesDb.Query().departments.Where(x => x.dept_no == "not_existing").Any());
        }

        [Fact]
        public void OrderBy()
        {
            var deptByDeptNo = fixture.employeesDb.Query().departments.OrderBy(x => x.dept_no);
            Assert.Equal("d001", deptByDeptNo.First().dept_no);
            Assert.Equal("d001", deptByDeptNo.FirstOrDefault().dept_no);
            Assert.Equal(lastDepartmentName, deptByDeptNo.Last().dept_no);
            Assert.Equal(lastDepartmentName, deptByDeptNo.LastOrDefault().dept_no);
        }

        [Fact]
        public void OrderBySelect()
        {
            var deptByDeptNo = fixture.employeesDb.Query().departments.OrderBy(x => x.dept_no).Select(x => x.dept_no);
            Assert.Equal("d001", deptByDeptNo.First());
            Assert.Equal("d001", deptByDeptNo.FirstOrDefault());
            Assert.Equal(lastDepartmentName, deptByDeptNo.Last());
            Assert.Equal(lastDepartmentName, deptByDeptNo.LastOrDefault());
        }

        [Fact]
        public void OrderBySelectAnonymous()
        {
            var deptByDeptNo = fixture.employeesDb.Query().departments.OrderBy(x => x.dept_no).Select(x => new
            {
                no = x.dept_no,
                name = x.dept_name
            });
            Assert.Equal("d001", deptByDeptNo.First().no);
            Assert.Equal("d001", deptByDeptNo.FirstOrDefault().no);
            Assert.Equal(lastDepartmentName, deptByDeptNo.Last().no);
            Assert.Equal(lastDepartmentName, deptByDeptNo.LastOrDefault().no);
        }

        //[Fact]
        //public void Any()
        //{
        //    fixture.employeesDb.Query().departments.Select(x => x.dept_no)

        //    Assert.True();
        //    Assert.True(fixture.employeesDb.Query().departments.Where(x => x.dept_no == "d005").Any());
        //    Assert.False(fixture.employeesDb.Query().departments.Any(x => x.dept_no == "not_existing"));
        //    Assert.False(fixture.employeesDb.Query().departments.Where(x => x.dept_no == "not_existing").Any());
        //}
    }
}