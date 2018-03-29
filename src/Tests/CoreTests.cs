using System;
using System.Linq;
using Slim.Metadata;
using Slim.MySql;
using Tests.Models;
using Xunit;

namespace Tests
{
    [Collection("Database")]
    public class CoreTests
    {
        private DatabaseFixture fixture;

        public CoreTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void TestMetadataFromInterfaceFactory()
        {
            TestDatabase(MetadataFromInterfaceFactory.ParseDatabase(typeof(employeesDb)), true);
        }

        [Fact]
        public void TestMetadataFromSqlFactory()
        {
            TestDatabase(MetadataFromSqlFactory.ParseDatabase("employees", fixture.information_schema), false);
        }

        private void TestDatabase(Database database, bool testCsType)
        {
            Assert.NotEmpty(database.Tables);
            Assert.Equal(10, database.Tables.Count);
            Assert.Equal(2, database.Tables.Count(x => x.Type == TableType.View));
            Assert.Equal(6, database.Constraints.Count);
            Assert.Contains(database.Tables, x => x.Columns.Any(y => y.Constraints.Any()));

            var employees = database.Tables.Single(x => x.DbName == "employees");

            Assert.Equal(6, employees.Columns.Count);

            var emp_no = employees.Columns.Single(x => x.DbName == "emp_no");
            Assert.True(emp_no.PrimaryKey);
            Assert.Equal("int", emp_no.DbType);
            Assert.Equal("int", emp_no.CsTypeName);

            if (testCsType)
            {
                Assert.Equal(typeof(int), emp_no.CsType);
                Assert.DoesNotContain(database.Tables, x => x.CsType == null);
            }
        }
    }
}