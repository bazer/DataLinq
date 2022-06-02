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

        public QueryTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void ToList()
        {
            Assert.Equal(9, fixture.employeesDb.Query().Departments.ToList().Count);
        }

        [Fact]
        public void Count()
        {
            Assert.Equal(9, fixture.employeesDb.Query().Departments.Count());
        }

        [Fact]
        public void SimpleWhere()
        {
            var where = fixture.employeesDb.Query().Departments.Where(x => x.DeptNo == "d005").ToList();
            Assert.Single(where);
            Assert.Equal("d005", where[0].DeptNo);
        }

        [Fact]
        public void SimpleWhereReverse()
        {
            var where = fixture.employeesDb.Query().Departments.Where(x => "d005" == x.DeptNo).ToList();
            Assert.Single(where);
            Assert.Equal("d005", where[0].DeptNo);
        }

        [Fact]
        public void SimpleWhereNot()
        {
            var where = fixture.employeesDb.Query().Departments.Where(x => x.DeptNo != "d005").ToList();
            Assert.Equal(8, where.Count);
            Assert.DoesNotContain(where, x => x.DeptNo == "d005");
        }

        [Fact]
        public void WhereAndToList()
        {
            var where = fixture.employeesDb.Query().dept_manager.Where(x => x.dept_no == "d004" && x.from_date > DateOnly.Parse("1990-01-01")).ToList();
            Assert.Equal(2, where.Count);
        }

        [Fact]
        public void WhereAndCount()
        {
            var where = fixture.employeesDb.Query().dept_manager.Where(x => x.dept_no == "d004" && x.from_date > DateOnly.Parse("1990-01-01"));
            Assert.Equal(2, where.Count());
        }

        [Fact]
        public void Single()
        {
            var dept = fixture.employeesDb.Query().Departments.Single(x => x.DeptNo == "d005");
            Assert.NotNull(dept);
            Assert.Equal("d005", dept.DeptNo);
        }

        [Fact]
        public void SingleOrDefault()
        {
            var dept = fixture.employeesDb.Query().Departments.SingleOrDefault(x => x.DeptNo == "d005");
            Assert.NotNull(dept);
            Assert.Equal("d005", dept.DeptNo);
        }

        [Fact]
        public void SingleOrDefaultNull()
        {
            var dept = fixture.employeesDb.Query().Departments.SingleOrDefault(x => x.DeptNo == "1234");
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
            Assert.Equal(71046, salary.salary);
        }

        [Fact]
        public void FirstOrDefault()
        {
            var salary = fixture.employeesDb.Query().salaries.FirstOrDefault(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.Equal(71046, salary.salary);
        }

        [Fact]
        public void FirstOrderBy()
        {
            var salary = fixture.employeesDb.Query().salaries.OrderBy(x => x.salary).First(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.Equal(70001, salary.salary);
        }

        [Fact]
        public void FirstOrDefaultOrderBy()
        {
            var salary = fixture.employeesDb.Query().salaries.OrderBy(x => x.salary).FirstOrDefault(x => x.salary > 70000);
            Assert.NotNull(salary);
            Assert.Equal(70001, salary.salary);
        }

        [Fact]
        public void LastOrDefaultOrderBy()
        {
            var dept = fixture.employeesDb.Query().Departments.OrderByDescending(x => x.Name).LastOrDefault(x => x.DeptNo != "d009");
            Assert.NotNull(dept);
            Assert.Equal("d005", dept.DeptNo);
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
            Assert.True(fixture.employeesDb.Query().Departments.Any(x => x.DeptNo == "d005"));
            Assert.True(fixture.employeesDb.Query().Departments.Where(x => x.DeptNo == "d005").Any());
            Assert.False(fixture.employeesDb.Query().Departments.Any(x => x.DeptNo == "not_existing"));
            Assert.False(fixture.employeesDb.Query().Departments.Where(x => x.DeptNo == "not_existing").Any());
        }

        [Fact]
        public void OrderBy()
        {
            var deptByDeptNo = fixture.employeesDb.Query().Departments.OrderBy(x => x.DeptNo);
            Assert.Equal("d001", deptByDeptNo.First().DeptNo);
            Assert.Equal("d001", deptByDeptNo.FirstOrDefault().DeptNo);
            Assert.Equal("d009", deptByDeptNo.Last().DeptNo);
            Assert.Equal("d009", deptByDeptNo.LastOrDefault().DeptNo);
        }

        [Fact]
        public void OrderBySelect()
        {
            var deptByDeptNo = fixture.employeesDb.Query().Departments.OrderBy(x => x.DeptNo).Select(x => x.DeptNo);
            Assert.Equal("d001", deptByDeptNo.First());
            Assert.Equal("d001", deptByDeptNo.FirstOrDefault());
            Assert.Equal("d009", deptByDeptNo.Last());
            Assert.Equal("d009", deptByDeptNo.LastOrDefault());
        }

        [Fact]
        public void OrderBySelectAnonymous()
        {
            var deptByDeptNo = fixture.employeesDb.Query().Departments.OrderBy(x => x.DeptNo).Select(x => new
            {
                no = x.DeptNo,
                name = x.Name
            });
            Assert.Equal("d001", deptByDeptNo.First().no);
            Assert.Equal("d001", deptByDeptNo.FirstOrDefault().no);
            Assert.Equal("d009", deptByDeptNo.Last().no);
            Assert.Equal("d009", deptByDeptNo.LastOrDefault().no);
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