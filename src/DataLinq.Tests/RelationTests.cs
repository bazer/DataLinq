using System;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.Tests.Models;
using Xunit;

namespace DataLinq.Tests
{
    public class RelationTests : IClassFixture<DatabaseFixture>
    {
        private readonly DatabaseFixture fixture;

        public RelationTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void LazyLoadSingleValue()
        {
            var manager = fixture.employeesDb.Query().Managers.Single(x => x.dept_no == "d005" && x.emp_no == 4923);

            Assert.NotNull(manager.Department);
            Assert.Equal("d005", manager.Department.DeptNo);
        }

        [Fact]
        public void LazyLoadList()
        {
            var department = fixture.employeesDb.Query().Departments.Single(x => x.DeptNo == "d005");

            Assert.NotNull(department.Managers);
            Assert.NotEmpty(department.Managers);
            Assert.True(10 < department.Managers.Count());
            Assert.Equal("d005", department.Managers.First().Department.DeptNo);
        }

        [Fact]
        public void EmptyList()
        {
            var employee = fixture.employeesDb.Query().Employees.Single(x => x.emp_no == 1000);

            Assert.NotNull(employee.dept_manager);
            Assert.Empty(employee.dept_manager);
        }
    }
}