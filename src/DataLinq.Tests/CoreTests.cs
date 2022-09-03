using System;
using System.Linq;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.MySql.Models;
using DataLinq.Tests.Models;
using Xunit;

namespace DataLinq.Tests
{
    public class CoreTests : IClassFixture<DatabaseFixture>
    {
        private DatabaseFixture fixture;

        public CoreTests(DatabaseFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void TestMetadataFromFixture()
        {
            Assert.Equal(2, DatabaseMetadata.LoadedDatabases.Count);
            Assert.Contains(DatabaseMetadata.LoadedDatabases, x => x.Key == typeof(employeesDb));
            Assert.Contains(DatabaseMetadata.LoadedDatabases, x => x.Key == typeof(information_schema));
        }

        [Fact]
        public void TestMetadataFromInterfaceFactory()
        {
            TestDatabase(MetadataFromInterfaceFactory.ParseDatabase(typeof(employeesDb)), true);
        }

        [Fact]
        public void TestMetadataFromSqlFactory()
        {
            TestDatabase(MetadataFromSqlFactory.ParseDatabase(fixture.EmployeesDbName, fixture.information_schema.Query()), false);
        }

        private void TestDatabase(DatabaseMetadata database, bool testCsType)
        {
            Assert.NotEmpty(database.Tables);
            Assert.Equal(8, database.Tables.Count);
            Assert.Equal(2, database.Tables.Count(x => x.Type == TableType.View));
            Assert.Equal(6, database.Relations.Count);
            Assert.Contains(database.Tables, x => x.Columns.Any(y => y.RelationParts.Any()));

            var employees = database.Tables.Single(x => x.DbName == "employees");
            Assert.Same(employees, employees.Model.Table);
            Assert.Equal(6, employees.Columns.Count);

            var emp_no = employees.Columns.Single(x => x.DbName == "emp_no");
            Assert.True(emp_no.PrimaryKey);
            Assert.True(emp_no.AutoIncrement);
            Assert.Equal("int", emp_no.DbType);
            Assert.Equal("int", emp_no.ValueProperty.CsTypeName);

            var dept_name = database.Tables.Single(x => x.DbName == "departments").Columns.Single(x => x.DbName == "dept_name");
            Assert.False(dept_name.PrimaryKey);
            Assert.False(dept_name.AutoIncrement);
            Assert.Single(dept_name.ColumnIndices);
            Assert.Equal(IndexType.Unique, dept_name.ColumnIndices.Single().Type);
            Assert.Equal("dept_name", dept_name.ColumnIndices.Single().ConstraintName);
            Assert.Same(dept_name, dept_name.ColumnIndices.First().Columns.Single());

            if (testCsType)
            {
                Assert.Equal(typeof(int), emp_no.ValueProperty.CsType);
                Assert.DoesNotContain(database.Tables, x => x.Model.CsType == null);
            }
        }
    }
}