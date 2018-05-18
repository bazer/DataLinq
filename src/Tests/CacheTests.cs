using System;
using System.Linq;
using Slim.Metadata;
using Tests.Models;
using Xunit;

namespace Tests
{
    [Collection("Database")]
    public class CacheTests
    {
        private readonly DatabaseFixture fixture;

        public CacheTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void CheckIndexDuplicates()
        {
            var employee = fixture.employeesDb.employees.Single(x => x.emp_no == 10004);

            Assert.NotNull(employee);
            Assert.NotEmpty(employee.salaries);
            Assert.Equal(16, employee.salaries.Count());
            Assert.Equal(1, employee.salaries.Count(x => x.from_date == employee.salaries.First().from_date));

            var column = fixture.employeesDb_provider.Database
                .Tables.Single(x => x.DbName == "salaries")
                .Columns.Single(x => x.DbName == "emp_no");

            Assert.Single(column.Index);
            Assert.Equal(16, column.Index[10004].Length);
        }

        [Fact]
        public void CheckRowDuplicates()
        {
            var employee = fixture.employeesDb.employees.Single(x => x.emp_no == 10010);

            Assert.NotNull(employee);
            Assert.NotEmpty(employee.dept_emp);
            Assert.Equal(2, employee.dept_emp.Count());

            var dept = fixture.employeesDb.departments.Single(x => x.dept_no == "d002");
            Assert.NotNull(dept);
            Assert.NotEmpty(dept.dept_emp);
            Assert.Equal(17346, dept.dept_emp.Count());

            var dept6 = fixture.employeesDb.departments.Single(x => x.dept_no == "d006");
            Assert.NotNull(dept6);
            Assert.NotEmpty(dept6.dept_emp);
            Assert.Equal(20117, dept6.dept_emp.Count());

            var table = fixture.employeesDb_provider.Database
                .Tables.Single(x => x.DbName == "dept_emp");

            Assert.Equal(20117 + 17346 + 2 - 1, table.Cache.RowCount);
        }
    }
}