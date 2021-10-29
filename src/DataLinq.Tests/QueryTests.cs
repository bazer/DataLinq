using System;
using System.Linq;
using DataLinq.Metadata;
using Tests.Models;
using Xunit;

namespace Tests
{
    [Collection("Database")]
    public class QueryTests
    {
        private readonly DatabaseFixture fixture;

        public QueryTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void ToList()
        {
            Assert.Equal(9, fixture.employeesDb.Query().departments.ToList().Count);
        }

        [Fact]
        public void Count()
        {
            Assert.Equal(9, fixture.employeesDb.Query().departments.Count());
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
            Assert.Equal(8, where.Count);
            Assert.DoesNotContain(where, x => x.dept_no == "d005");
        }

        [Fact]
        public void WhereAndToList()
        {
            var where = fixture.employeesDb.Query().dept_manager.Where(x => x.dept_no == "d004" && x.from_date > DateTime.Parse("1990-01-01")).ToList();
            Assert.Equal(2, where.Count);
        }

        [Fact]
        public void WhereAndCount()
        {
            var where = fixture.employeesDb.Query().dept_manager.Where(x => x.dept_no == "d004" && x.from_date > DateTime.Parse("1990-01-01"));
            Assert.Equal(2, where.Count());
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
        public void FirstOrDefaultNull()
        {
            var salary = fixture.employeesDb.Query().salaries.FirstOrDefault(x => x.salary < 10000);
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
            Assert.Equal("d001", deptByDeptNo.FirstOrDefault().dept_no);
            Assert.Equal("d009", deptByDeptNo.Last().dept_no);
        }
    }
}