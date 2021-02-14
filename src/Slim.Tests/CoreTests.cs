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
            TestDatabase(MetadataFromSqlFactory.ParseDatabase("employees", fixture.information_schema.Query()), false);
        }

        private void TestDatabase(DatabaseMetadata database, bool testCsType)
        {
            Assert.NotEmpty(database.Tables);
            Assert.Equal(8, database.Tables.Count);
            Assert.Equal(2, database.Tables.Count(x => x.Type == TableType.View));
            Assert.Equal(6, database.Relations.Count);
            Assert.Contains(database.Tables, x => x.Columns.Any(y => y.RelationParts.Any()));

            var employees = database.Tables.Single(x => x.DbName == "employees");

            Assert.Equal(6, employees.Columns.Count);

            var emp_no = employees.Columns.Single(x => x.DbName == "emp_no");
            Assert.True(emp_no.PrimaryKey);
            Assert.True(emp_no.AutoIncrement);
            Assert.Equal("int", emp_no.DbType);
            Assert.Equal("int", emp_no.ValueProperty.CsTypeName);

            if (testCsType)
            {
                Assert.Equal(typeof(int), emp_no.ValueProperty.CsType);
                Assert.DoesNotContain(database.Tables, x => x.Model.CsType == null);
            }
        }
    }
}