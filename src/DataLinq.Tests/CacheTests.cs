using DataLinq.Attributes;
using DataLinq.Tests.Models;
using System;
using System.Linq;
using Xunit;

namespace DataLinq.Tests
{
    public class CacheTests : IClassFixture<DatabaseFixture>
    {
        private readonly DatabaseFixture fixture;
        private readonly employees testEmployee;
        private readonly int testEmployeeDeptCount;
        private readonly int dept2Count;
        private readonly int dept6Count;
        private readonly int dept7Count;

        public CacheTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
            fixture.employeesDb.Provider.State.ClearCache();

            testEmployee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == 1010);
            testEmployeeDeptCount = testEmployee.dept_emp.Count();
            dept2Count = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d002").dept_emp.Count();
            dept6Count = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d006").dept_emp.Count();
            dept7Count = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d007").dept_emp.Count();

            fixture.employeesDb.Provider.State.Cache.ClearCache();
        }

        [Fact]
        public void CheckRowDuplicates()
        {
            for (var i = 0; i < 10; i++)
            {
                var employee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == testEmployee.emp_no);

                Assert.NotNull(employee);
                Assert.NotEmpty(employee.dept_emp);
                Assert.Equal(testEmployeeDeptCount, employee.dept_emp.Count());

                var dept = fixture.employeesDb.Query().Departments.Single(x => x.DeptNo == "d002");
                Assert.NotNull(dept);
                Assert.NotEmpty(dept.dept_emp);
                Assert.True(dept.dept_emp.Count() > 0);
                Assert.Equal(dept2Count, dept.dept_emp.Count());

                var dept6 = fixture.employeesDb.Query().Departments.Single(x => x.DeptNo == "d006");
                Assert.NotNull(dept6);
                Assert.NotEmpty(dept6.dept_emp);
                Assert.Equal(dept6Count, dept6.dept_emp.Count());

                var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "dept_emp");

                Assert.Equal(dept2Count + dept6Count + 2 - 1, fixture.employeesDb.Provider.GetTableCache(table).RowCount);
            }
        }

        [Fact]
        public void TimeLimit()
        {
            var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "dept_emp");

            var cache = fixture.employeesDb.Provider.GetTableCache(table);
            cache.ClearRows();

            var employee = fixture.employeesDb.Query().employees.Single(x => x.emp_no == testEmployee.emp_no);
            Assert.Equal(testEmployeeDeptCount, employee.dept_emp.Count());

            var ticks = DateTime.Now.Ticks;

            var dept = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d002");
            Assert.Equal(dept2Count, dept.dept_emp.Count());

            var ticks2 = DateTime.Now.Ticks;

            var dept6 = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d006");
            Assert.Equal(dept6Count, dept6.dept_emp.Count());
            Assert.Equal(dept2Count + dept6Count + 2 - 1, cache.RowCount);

            var tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsInsertedBeforeTick(ticks)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Equal(2, tables.Count);
            Assert.Equal("employees", tables[1].table.Table.DbName);
            Assert.Equal(1, tables[1].numRows);
            Assert.Equal("dept_emp", tables[0].table.Table.DbName);
            Assert.Equal(1, tables[0].numRows);
            Assert.Equal(dept2Count + dept6Count, cache.RowCount);

            tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsInsertedBeforeTick(ticks2)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Equal(2, tables.Count);
            Assert.Equal("departments", tables[0].table.Table.DbName);
            Assert.Equal(1, tables[0].numRows);
            Assert.Equal("dept_emp", tables[1].table.Table.DbName);
            Assert.Equal(dept2Count, tables[1].numRows);
            Assert.Equal(dept6Count, cache.RowCount);

            tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsInsertedBeforeTick(DateTime.Now.Ticks)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Equal(2, tables.Count);
            Assert.Equal("departments", tables[0].table.Table.DbName);
            Assert.Equal(1, tables[0].numRows);
            Assert.Equal("dept_emp", tables[1].table.Table.DbName);
            Assert.Equal(dept6Count, tables[1].numRows);
            Assert.Equal(0, cache.RowCount);

            tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsInsertedBeforeTick(DateTime.Now.Ticks)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Empty(tables);
        }

        [Fact]
        public void RowLimit()
        {
            var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "dept_emp");

            var cache = fixture.employeesDb.Provider.GetTableCache(table);
            cache.ClearRows();

            var dept = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d007");
            Assert.Equal(dept7Count, dept.dept_emp.Count());
            Assert.Equal(dept7Count, cache.RowCount);

            var tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsByLimit(CacheLimitType.Rows, 100)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Single(tables);
            Assert.Equal("dept_emp", tables[0].table.Table.DbName);
            Assert.Equal(dept7Count - 100, tables[0].numRows);
            Assert.Equal(100, cache.RowCount);
        }

        [Fact]
        public void SizeLimit()
        {
            var table = fixture.employeesDb.Provider.Metadata
                    .Tables.Single(x => x.DbName == "dept_emp");

            var cache = fixture.employeesDb.Provider.GetTableCache(table);
            cache.ClearRows();

            var dept = fixture.employeesDb.Query().departments.Single(x => x.dept_no == "d007");
            Assert.Equal(dept7Count, dept.dept_emp.Count());
            Assert.True(cache.TotalBytes > 0);

            var tables = fixture.employeesDb.Provider.State.Cache
                .RemoveRowsByLimit(CacheLimitType.Kilobytes, 10)
                .OrderBy(x => x.numRows)
                .ToList();

            Assert.Single(tables);
            Assert.Equal("dept_emp", tables[0].table.Table.DbName);
            Assert.True(tables[0].numRows > 0);
            Assert.True(cache.TotalBytes <= 1024 * 1024);
        }
    }
}