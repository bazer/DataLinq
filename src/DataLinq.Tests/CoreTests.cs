using DataLinq.Config;
using DataLinq.Metadata;
using DataLinq.MySql.Models;
using DataLinq.Tests.Models;
using System.Linq;
using Xunit;

namespace DataLinq.Tests
{
    public class CoreTests : BaseTests
    {
        [Fact]
        public void TestMetadataFromFixture()
        {
            Assert.Equal(2, DatabaseMetadata.LoadedDatabases.Count);
            Assert.Contains(DatabaseMetadata.LoadedDatabases, x => x.Key == typeof(Employees));
            Assert.Contains(DatabaseMetadata.LoadedDatabases, x => x.Key == typeof(information_schema));
        }

        [Fact]
        public void TestMetadataFromInterfaceFactory()
        {
            TestDatabase(MetadataFromInterfaceFactory.ParseDatabaseFromDatabaseModel(typeof(Employees)), true);
        }

        [Theory]
        [MemberData(nameof(GetEmployeeConnections))]
        public void TestMetadataFromSqlFactory(DataLinqDatabaseConnection connection)
        {
            var factory = PluginHook.MetadataFromSqlFactories[connection.Type]
                .GetMetadataFromSqlFactory(new MetadataFromDatabaseFactoryOptions());

            var metadata = factory.ParseDatabase("employees", "Employees", connection.DatabaseName, connection.ConnectionString.Original);
            TestDatabase(metadata, false);
        }

        private void TestDatabase(DatabaseMetadata database, bool testCsType)
        {
            Assert.NotEmpty(database.TableModels);
            Assert.Equal(8, database.TableModels.Count);
            Assert.Equal(2, database.TableModels.Count(x => x.Table.Type == TableType.View));
            Assert.Equal(12, database.TableModels.Sum(x => x.Model.RelationProperties.Count()));
            Assert.Contains(database.TableModels, x => x.Table.Columns.Any(y => y.RelationParts.Any()));

            var employees = database.TableModels.Single(x => x.Table.DbName == "employees").Table;
            Assert.Same(employees, employees.Model.Table);
            Assert.Equal(6, employees.Columns.Count);

            var emp_no = employees.Columns.Single(x => x.DbName == "emp_no");
            Assert.True(emp_no.PrimaryKey);
            Assert.True(emp_no.AutoIncrement);
            Assert.Equal("int", emp_no.DbTypes.Single(x => x.DatabaseType == DatabaseType.MySQL).Name);
            //Assert.Equal("integer", emp_no.DbTypes.Single(x => x.DatabaseType == DatabaseType.SQLite).Name);
            Assert.Equal("int", emp_no.ValueProperty.CsTypeName);

            var dept_name = database.TableModels.Single(x => x.Table.DbName == "departments").Table.Columns.Single(x => x.DbName == "dept_name");
            Assert.Same("string", dept_name.ValueProperty.CsTypeName);
            Assert.False(dept_name.PrimaryKey);
            Assert.False(dept_name.AutoIncrement);
            Assert.Single(dept_name.ColumnIndices);
            Assert.Equal(IndexType.Unique, dept_name.ColumnIndices.Single().Type);
            Assert.Equal("dept_name", dept_name.ColumnIndices.Single().ConstraintName);
            Assert.Same(dept_name, dept_name.ColumnIndices.First().Columns.Single());

            if (testCsType)
            {
                Assert.Equal(typeof(int), emp_no.ValueProperty.CsType);
                Assert.DoesNotContain(database.TableModels, x => x.Model.CsType == null);
            }
        }
    }
}