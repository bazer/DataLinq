using System;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.MySql; // Namespace for the factory
using Xunit;

namespace DataLinq.Tests
{
    /*
        IClassFixture<DatabaseFixture>: We use a class fixture to inject the DatabaseFixture. This ensures the database connection is set up once for all tests in this class.

        Constructor: Gets the specific MySQL connection details from the fixture. Throws if MySQL isn't configured in the fixture.

        ParseEmployeesDb Helper: A helper method to reduce boilerplate for calling the MetadataFromMySqlFactory.ParseDatabase method with the correct parameters derived from the fixture/config. It includes an assertion to fail the test immediately if parsing itself fails. It accepts optional MetadataFromDatabaseFactoryOptions for filtering tests.

        TestParseDatabase_Employees_MySql: Performs an end-to-end test, parsing the entire schema and checking top-level properties (names, counts of tables/views).

        TestParseTable_SpecificTable_MySql: Uses the Tables option in MetadataFromDatabaseFactoryOptions to parse only the departments table and verifies the result.

        TestParseColumn_SpecificColumn_MySql: Parses the full DB but then drills down into specific columns (emp_no, birth_date, gender, IsDeleted) on the employees table to verify their specific properties (PK, AutoIncrement, Nullability, C# Type Mapping, DB Type Name, Enum values) were correctly inferred from the MySQL schema.

        TestParseRelations_MySql: Parses the full DB and then checks specific ColumnDefinitions (dept-emp.emp_no, dept-emp.dept_no) to ensure the ForeignKey flag is set and the correct [ForeignKey] attribute was added based on information_schema.KEY_COLUMN_USAGE. It also checks that corresponding RelationProperty entries were likely created (though their full linking is tested better via TypeFactory or SyntaxFactory tests).

        TestParseIndices_MySql: Parses the full DB and checks a known index (the unique index on departments.dept_name) to ensure the correct [Index] attribute was added based on information_schema.STATISTICS.

        TestViewParsing_MySql: Parses the full DB and specifically checks a view (current_dept_emp) to ensure it's identified as TableType.View and that its SQL definition was captured.

        TestTableFiltering_MySql_* Tests: These explicitly test the Tables and Views filtering options passed to the factory, ensuring only the specified objects are included in the resulting DatabaseDefinition. Includes a test for a non-existent table to verify failure.    
    */

    // Inherit from IClassFixture to get the DatabaseFixture instance
    public class MetadataFromMySqlFactoryTests : IClassFixture<DatabaseFixture>
    {
        private readonly DatabaseFixture _fixture;
        private readonly DataLinqDatabaseConnection _mySqlConnection;

        public MetadataFromMySqlFactoryTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
            // Get the MySQL connection details from the fixture
            _mySqlConnection = _fixture.EmployeeConnections.SingleOrDefault(c => c.Type == DatabaseType.MySQL)
                ?? throw new InvalidOperationException("MySQL connection not found in fixture. Ensure MySQL tests are configured.");
        }

        // Helper to run the parser with default options
        private DatabaseDefinition ParseEmployeesDb(MetadataFromDatabaseFactoryOptions? options = null)
        {
            var factory = new MetadataFromMySqlFactory(options ?? new MetadataFromDatabaseFactoryOptions());
            var result = factory.ParseDatabase(
                _mySqlConnection.DatabaseConfig.Name, // "employees" from config
                _mySqlConnection.DatabaseConfig.CsType, // "EmployeesDb" from config
                _mySqlConnection.DatabaseConfig.Namespace, // "DataLinq.Tests.Models.Employees"
                _mySqlConnection.DataSourceName, // The actual DB name, e.g., "employees"
                _mySqlConnection.ConnectionString.Original);

            Assert.True(result.HasValue, result.HasFailed ? result.Failure.ToString() : "Parsing failed");
            return result.Value;
        }

        [Fact]
        public void TestParseDatabase_Employees_MySql()
        {
            // Act
            var dbDefinition = ParseEmployeesDb();

            // Assert
            Assert.NotNull(dbDefinition);
            Assert.Equal(_mySqlConnection.DatabaseConfig.CsType, dbDefinition.CsType.Name);
            Assert.Equal(_mySqlConnection.DataSourceName, dbDefinition.DbName);
            Assert.Equal(_mySqlConnection.DatabaseConfig.Namespace, dbDefinition.CsType.Namespace);

            // Check counts (adjust if your test schema differs slightly)
            Assert.Equal(8, dbDefinition.TableModels.Length); // 6 tables + 2 views expected
            Assert.Equal(6, dbDefinition.TableModels.Count(tm => tm.Table.Type == TableType.Table));
            Assert.Equal(2, dbDefinition.TableModels.Count(tm => tm.Table.Type == TableType.View));

            // Spot check a table
            var employeesTableModel = dbDefinition.TableModels.SingleOrDefault(tm => tm.Table.DbName == "employees");
            Assert.NotNull(employeesTableModel);
            Assert.Equal("employees", employeesTableModel.Model.CsType.Name); // Default name from DB
            Assert.Equal(7, employeesTableModel.Table.Columns.Length);
        }

        [Fact]
        public void TestParseTable_SpecificTable_MySql()
        {
            // Arrange
            // Use filtering options to parse only the 'departments' table
            var options = new MetadataFromDatabaseFactoryOptions { Tables = ["departments"], Views = [] };

            // Act
            var dbDefinition = ParseEmployeesDb(options);

            // Assert
            Assert.Single(dbDefinition.TableModels);
            var deptTableModel = dbDefinition.TableModels[0];
            Assert.Equal("departments", deptTableModel.Table.DbName);
            Assert.Equal("departments", deptTableModel.Model.CsType.Name); // Default name
            Assert.Equal(2, deptTableModel.Table.Columns.Length);
            Assert.Contains(deptTableModel.Table.Columns, c => c.DbName == "dept_no");
            Assert.Contains(deptTableModel.Table.Columns, c => c.DbName == "dept_name");
        }

        [Fact]
        public void TestParseColumn_SpecificColumn_MySql()
        {
            // Arrange
            var dbDefinition = ParseEmployeesDb();
            var employeesTable = dbDefinition.TableModels.Single(tm => tm.Table.DbName == "employees").Table;

            // Act & Assert
            // emp_no (PK, AutoIncrement, int)
            var empNoCol = employeesTable.Columns.Single(c => c.DbName == "emp_no");
            Assert.True(empNoCol.PrimaryKey);
            Assert.True(empNoCol.AutoIncrement);
            Assert.False(empNoCol.Nullable);
            Assert.Equal("int", empNoCol.ValueProperty.CsType.Name); // Assuming int mapping
            var empNoDbType = empNoCol.GetDbTypeFor(DatabaseType.MySQL);
            Assert.NotNull(empNoDbType);
            Assert.Equal("int", empNoDbType.Name);

            // birth_date (date)
            var birthDateCol = employeesTable.Columns.Single(c => c.DbName == "birth_date");
            Assert.False(birthDateCol.PrimaryKey);
            Assert.False(birthDateCol.AutoIncrement);
            Assert.False(birthDateCol.Nullable);
            Assert.Equal("DateOnly", birthDateCol.ValueProperty.CsType.Name); // Assuming DateOnly mapping
            var birthDateDbType = birthDateCol.GetDbTypeFor(DatabaseType.MySQL);
            Assert.NotNull(birthDateDbType);
            Assert.Equal("date", birthDateDbType.Name);

            // gender (enum)
            var genderCol = employeesTable.Columns.Single(c => c.DbName == "gender");
            Assert.False(genderCol.Nullable);
            Assert.Equal("genderValue", genderCol.ValueProperty.CsType.Name); // Default mapping before potential rename
            Assert.NotNull(genderCol.ValueProperty.EnumProperty);
            Assert.Equal(2, genderCol.ValueProperty.EnumProperty.Value.DbEnumValues.Count); // M, F from DB schema
            Assert.Contains(genderCol.ValueProperty.EnumProperty.Value.DbEnumValues, ev => ev.name == "M");
            Assert.Contains(genderCol.ValueProperty.EnumProperty.Value.DbEnumValues, ev => ev.name == "F");
            var genderDbType = genderCol.GetDbTypeFor(DatabaseType.MySQL);
            Assert.NotNull(genderDbType);
            Assert.Equal("enum", genderDbType.Name);

            // IsDeleted (bit nullable) - Assuming it exists in your schema
            var isDeletedCol = employeesTable.Columns.SingleOrDefault(c => c.DbName == "IsDeleted");
            if (isDeletedCol != null) // Make test resilient if column doesn't exist
            {
                Assert.True(isDeletedCol.Nullable);
                Assert.Equal("bool", isDeletedCol.ValueProperty.CsType.Name);
                Assert.True(isDeletedCol.ValueProperty.CsNullable);
                var isDeletedDbType = isDeletedCol.GetDbTypeFor(DatabaseType.MySQL);
                Assert.NotNull(isDeletedDbType);
                Assert.Equal("bit", isDeletedDbType.Name); // MySQL BIT(1) often maps here
            }
        }

        [Fact]
        public void TestParseRelations_MySql()
        {
            // Arrange
            var dbDefinition = ParseEmployeesDb();
            var deptEmpTable = dbDefinition.TableModels.Single(tm => tm.Table.DbName == "dept-emp").Table;
            var employeesTable = dbDefinition.TableModels.Single(tm => tm.Table.DbName == "employees").Table;
            var departmentsTable = dbDefinition.TableModels.Single(tm => tm.Table.DbName == "departments").Table;

            // Act
            // Parsing happens during ParseDatabase call

            // Assert
            // Check Foreign Key flags and attributes from dept-emp to employees
            var fkEmpNoCol = deptEmpTable.Columns.Single(c => c.DbName == "emp_no");
            Assert.True(fkEmpNoCol.ForeignKey);
            var fkEmpAttr = fkEmpNoCol.ValueProperty.Attributes.OfType<ForeignKeyAttribute>().SingleOrDefault(a => a.Table == "employees");
            Assert.NotNull(fkEmpAttr);
            Assert.Equal("emp_no", fkEmpAttr.Column);
            Assert.Equal("dept_emp_ibfk_1", fkEmpAttr.Name);

            // Check Foreign Key flags and attributes from dept-emp to departments
            var fkDeptNoCol = deptEmpTable.Columns.Single(c => c.DbName == "dept_no");
            Assert.True(fkDeptNoCol.ForeignKey);
            var fkDeptAttr = fkDeptNoCol.ValueProperty.Attributes.OfType<ForeignKeyAttribute>().SingleOrDefault(a => a.Table == "departments");
            Assert.NotNull(fkDeptAttr);
            Assert.Equal("dept_no", fkDeptAttr.Column);
            Assert.Equal("dept_emp_ibfk_2", fkDeptAttr.Name);

            // Check Relation Properties were added (names are default based on table)
            Assert.Contains(employeesTable.Model.RelationProperties, rp => rp.Key == "dept_emp");
            Assert.Contains(departmentsTable.Model.RelationProperties, rp => rp.Key == "dept_emp");
            Assert.Contains(deptEmpTable.Model.RelationProperties, rp => rp.Key == "employees");
            Assert.Contains(deptEmpTable.Model.RelationProperties, rp => rp.Key == "departments");
        }

        [Fact]
        public void TestParseIndices_MySql()
        {
            // Arrange
            var dbDefinition = ParseEmployeesDb();
            var departmentsTable = dbDefinition.TableModels.Single(tm => tm.Table.DbName == "departments").Table;

            // Act
            // Parsing happens during ParseDatabase call

            // Assert
            // Check unique index on departments.dept_name
            var deptNameCol = departmentsTable.Columns.Single(c => c.DbName == "dept_name");
            var indexAttr = deptNameCol.ValueProperty.Attributes.OfType<IndexAttribute>().SingleOrDefault(a => a.Name == "dept_name"); // MySQL often names unique index same as column
            Assert.NotNull(indexAttr);
            Assert.Equal(IndexCharacteristic.Unique, indexAttr.Characteristic);
            Assert.Equal(IndexType.BTREE, indexAttr.Type); // Default assumed by MySQL usually
            Assert.Single(indexAttr.Columns); // Single column index
            Assert.Equal("dept_name", indexAttr.Columns[0]); // Check the column name
            Assert.Equal("dept_name", indexAttr.Name); // Check the index name
        }

        [Fact]
        public void TestViewParsing_MySql()
        {
            // Arrange
            var dbDefinition = ParseEmployeesDb();

            // Act
            var viewTableModel = dbDefinition.TableModels.SingleOrDefault(tm => tm.Table.DbName == "current_dept_emp");

            // Assert
            Assert.NotNull(viewTableModel);
            Assert.Equal(TableType.View, viewTableModel.Table.Type);
            var viewDefinition = Assert.IsType<ViewDefinition>(viewTableModel.Table);
            Assert.False(string.IsNullOrWhiteSpace(viewDefinition.Definition)); // Check definition was captured
            Assert.Contains("select", viewDefinition.Definition, System.StringComparison.OrdinalIgnoreCase);
            // Check columns of the view
            Assert.Equal(4, viewDefinition.Columns.Length);
            Assert.Contains(viewDefinition.Columns, c => c.DbName == "emp_no");
            Assert.Contains(viewDefinition.Columns, c => c.DbName == "dept_no");
        }

        [Fact]
        public void TestTableFiltering_MySql_TablesOnly()
        {
            // Arrange
            var options = new MetadataFromDatabaseFactoryOptions { Tables = ["employees", "salaries"], Views = [] }; // Only these two tables

            // Act
            var dbDefinition = ParseEmployeesDb(options);

            // Assert
            Assert.Equal(2, dbDefinition.TableModels.Length);
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "employees");
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "salaries");
            Assert.All(dbDefinition.TableModels, tm => Assert.Equal(TableType.Table, tm.Table.Type));
        }

        [Fact]
        public void TestTableFiltering_MySql_ViewsOnly()
        {
            // Arrange
            var options = new MetadataFromDatabaseFactoryOptions { Views = ["current_dept_emp"], Tables = [] }; // Only this view

            // Act
            var dbDefinition = ParseEmployeesDb(options);

            // Assert
            Assert.Single(dbDefinition.TableModels);
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "current_dept_emp");
            Assert.All(dbDefinition.TableModels, tm => Assert.Equal(TableType.View, tm.Table.Type));
        }

        [Fact]
        public void TestTableFiltering_MySql_SpecificTablesAndViews()
        {
            // Arrange
            var options = new MetadataFromDatabaseFactoryOptions
            {
                Tables = ["departments"],
                Views = ["dept_emp_latest_date"]
            };

            // Act
            var dbDefinition = ParseEmployeesDb(options);

            // Assert
            Assert.Equal(2, dbDefinition.TableModels.Length);
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "departments" && tm.Table.Type == TableType.Table);
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "dept_emp_latest_date" && tm.Table.Type == TableType.View);
        }

        [Fact]
        public void TestTableFiltering_MySql_MissingTable()
        {
            // Arrange
            var options = new MetadataFromDatabaseFactoryOptions { Tables = ["non_existent_table"] };
            var factory = new MetadataFromMySqlFactory(options);

            // Act
            var result = factory.ParseDatabase(
                 _mySqlConnection.DatabaseConfig.Name,
                 _mySqlConnection.DatabaseConfig.CsType,
                 _mySqlConnection.DatabaseConfig.Namespace,
                 _mySqlConnection.DataSourceName,
                 _mySqlConnection.ConnectionString.Original);

            // Assert
            // It should fail because the specified table wasn't found
            Assert.False(result.HasValue);
            Assert.NotNull(result.Failure.Value);
            Assert.Contains("Could not find the specified tables or views: non_existent_table", result.Failure.ToString());
        }
    }
}