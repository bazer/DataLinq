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
            var manager = fixture.employeesDb.Query().dept_manager.Single(x => x.dept_no == "d005" && x.emp_no == 110511);

            Assert.NotNull(manager.departments);
            Assert.Equal("d005", manager.departments.dept_no);
        }

        [Fact]
        public void LazyLoadList()
        {
            var department = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d005");

            Assert.NotNull(department.dept_manager);
            Assert.NotEmpty(department.dept_manager);
            Assert.Equal(2, department.dept_manager.Count());
            Assert.Equal("d005", department.dept_manager.First().departments.dept_no);
        }

        [Fact]
        public void EmptyList()
        {
            var employee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == 10001);

            Assert.NotNull(employee.dept_manager);
            Assert.Empty(employee.dept_manager);
        }
    }
}