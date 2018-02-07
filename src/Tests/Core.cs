using System;
using System.Linq;
using Slim.Metadata;
using Tests.Models;
using Xunit;

namespace Tests
{
    [Collection("Database")]
    public class Core
    {
        private DatabaseFixture fixture;

        public Core(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void TestMetadataFromInterfaceFactory()
        {
            var database = MetadataFromInterfaceFactory.ParseDatabase(typeof(employeesDb));

            Assert.NotEmpty(database.Tables);
            Assert.Equal(10, database.Tables.Count);
            Assert.Equal(2, database.Tables.Count(x => x.Type == TableType.View));

            var employees = database.Tables.Single(x => x.Name == "employees");

            Assert.Equal(6, employees.Columns.Count);

            var emp_no = employees.Columns.Single(x => x.Name == "emp_no");
            Assert.True(emp_no.PrimaryKey);
            Assert.Equal("int", emp_no.DbType);
            Assert.Equal(typeof(int), emp_no.CsType);
            Assert.Equal("int", emp_no.CsTypeName);
        }

        [Fact]
        public void TestLoadingData()
        {
            var dept_emp = employeesDb.current_dept_emp.Where(x => x.dept_no == Guid.NewGuid()).Single();

            employeesDb.titles.Get(4);
            
        }
    }
}
