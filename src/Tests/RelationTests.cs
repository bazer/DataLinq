using System;
using System.Linq;
using Slim.Metadata;
using Tests.Models;
using Xunit;

namespace Tests
{
    [Collection("Database")]
    public class RelationTests
    {
        private readonly DatabaseFixture fixture;

        public RelationTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void LazyLoadSingleValue()
        {
            var manager = fixture.employeesDb.dept_manager.Single(x => x.dept_no == "d005" && x.emp_no == 110511);

            Assert.NotNull(manager.departments);
            Assert.Equal("d005", manager.departments.dept_no);
        }

        [Fact]
        public void LazyLoadList()
        {
            var department = fixture.employeesDb.departments.Single(x => x.dept_no == "d005");

            Assert.NotNull(department.dept_manager);
            Assert.NotEmpty(department.dept_manager);
            Assert.Equal(2, department.dept_manager.Count());
            Assert.Equal("d005", department.dept_manager.First().departments.dept_no);
        }
    }
}