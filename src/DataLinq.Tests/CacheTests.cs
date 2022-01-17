using System;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.MySql;
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
            fixture.employeesDb.Provider.State.ClearCache();
        }

        [Fact]
        public void CheckIndexDuplicates()
        {
            var employee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == 10004);

            Assert.NotNull(employee);
            Assert.NotEmpty(employee.salaries);
            Assert.Equal(16, employee.salaries.Count());
            Assert.Equal(1, employee.salaries.Count(x => x.from_date == employee.salaries.First().from_date));

            var column = fixture.employeesDb.Provider.Metadata
                .Tables.Single(x => x.DbName == "salaries")
                .Columns.Single(x => x.DbName == "emp_no");

            Assert.Single(column.Index);
            Assert.Equal(16, column.Index[10004].Length);
        }

        [Fact]
        public void CheckRowDuplicates()
        {
            for (var i = 0; i < 100; i++)
            {
                var employee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == 10010);

                Assert.NotNull(employee);
                Assert.NotEmpty(employee.dept_emp);
                Assert.Equal(2, employee.dept_emp.Count());

                var dept = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d002");
                Assert.NotNull(dept);
                Assert.NotEmpty(dept.dept_emp);
                Assert.Equal(17346, dept.dept_emp.Count());

                var dept6 = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d006");
                Assert.NotNull(dept6);
                Assert.NotEmpty(dept6.dept_emp);
                Assert.Equal(20117, dept6.dept_emp.Count());

                var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "dept_emp");

                Assert.Equal(20117 + 17346 + 2 - 1, table.Cache.RowCount);
            }
        }

        [Fact]
        public void TimeLimit()
        {
            var employee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == 10010);
            Assert.Equal(2, employee.dept_emp.Count());

            var ticks = DateTime.Now.Ticks;

            var dept = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d002");
            Assert.Equal(17346, dept.dept_emp.Count());

            var ticks2 = DateTime.Now.Ticks;

            var dept6 = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d006");
            Assert.Equal(20117, dept6.dept_emp.Count());

            var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "dept_emp");

            Assert.Equal(20117 + 17346 + 2 - 1, table.Cache.RowCount);

            var tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsInsertedBeforeTick(ticks)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Equal(2, tables.Count);
            Assert.Equal("employees", tables[0].table.Table.DbName);
            Assert.Equal(1, tables[0].numRows);
            Assert.Equal("dept_emp", tables[1].table.Table.DbName);
            Assert.Equal(2, tables[1].numRows);
            Assert.Equal(20117 + 17346 - 1, table.Cache.RowCount);

            tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsInsertedBeforeTick(ticks2)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Equal(2, tables.Count);
            Assert.Equal("departments", tables[0].table.Table.DbName);
            Assert.Equal(1, tables[0].numRows);
            Assert.Equal("dept_emp", tables[1].table.Table.DbName);
            Assert.Equal(17346, tables[1].numRows);
            Assert.Equal(20117 - 1, table.Cache.RowCount);

            tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsInsertedBeforeTick(DateTime.Now.Ticks)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Equal(2, tables.Count);
            Assert.Equal("departments", tables[0].table.Table.DbName);
            Assert.Equal(1, tables[0].numRows);
            Assert.Equal("dept_emp", tables[1].table.Table.DbName);
            Assert.Equal(20117 - 1, tables[1].numRows);
            Assert.Equal(0, table.Cache.RowCount);

            tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsInsertedBeforeTick(DateTime.Now.Ticks)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Empty(tables);
        }

        [Fact]
        public void RowLimit()
        {
            var dept = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d007");
            Assert.Equal(52245, dept.dept_emp.Count());

            var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "dept_emp");

            Assert.Equal(52245, table.Cache.RowCount);

            var tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsByLimit(CacheLimitType.Rows, 10000)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Single(tables);
            Assert.Equal("dept_emp", tables[0].table.Table.DbName);
            Assert.Equal(42245, tables[0].numRows);
            Assert.Equal(10000, table.Cache.RowCount);
        }

        [Fact]
        public void SizeLimit()
        {
            var dept = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d007");
            var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "dept_emp");

            Assert.Equal(52245, dept.dept_emp.Count());
            Assert.True(table.Cache.TotalBytes > 0);

            var tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsByLimit(CacheLimitType.Megabytes, 1)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Single(tables);
            Assert.Equal("dept_emp", tables[0].table.Table.DbName);
            Assert.True(tables[0].numRows > 0);
            Assert.True(table.Cache.TotalBytes <= 1024 * 1024);
        }
    }
}