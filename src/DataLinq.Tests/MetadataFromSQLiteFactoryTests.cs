using System;
using System.IO;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Config;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.SQLite; // Namespace for the factory
using Xunit;

namespace DataLinq.Tests
{
    /*
        Fixture & Connection: Similar setup to MySQL, using IClassFixture and extracting the SQLite connection details.

        Database Path: It's crucial to get the correct, absolute path to the .db file for the factory. The code attempts to resolve it relative to the DataLinqConfig's BasePath first, then falls back to the test execution directory. Ensure your .db file is correctly copied to the output directory (your .csproj likely handles this).

        ParseEmployeesDb Helper: Adjusted to pass the file path as the dbName argument to the factory, as SQLite operates on files.

        Schema Introspection: The underlying factory (MetadataFromSQLiteFactory) uses PRAGMA table_info, PRAGMA foreign_key_list, PRAGMA index_list, PRAGMA index_info, and queries sqlite_master instead of information_schema. The tests verify the results of this parsing.

        Type Affinities: SQLite uses type affinities (INTEGER, TEXT, REAL, BLOB, NUMERIC). Tests check that the factory maps these correctly to C# types (e.g., INTEGER -> int, TEXT -> string or DateOnly, REAL -> double, BLOB -> byte[]).

        AutoIncrement: Checks that the factory correctly identifies AUTOINCREMENT columns by inspecting the CREATE TABLE statement in sqlite_master.

        Foreign Keys: Verifies ForeignKey flags and attributes based on pragma_foreign_key_list. Note that SQLite FK constraints might not have explicit names unless defined with CONSTRAINT.

        Indices: Verifies Index attributes based on pragma_index_list/pragma_index_info. Checks unique constraints.

        Views: Verifies TableType.View and that the view's CREATE VIEW statement is captured from sqlite_master.

        Filtering: Tests remain similar, verifying that the Tables and Views options filter correctly based on names found in sqlite_master.
     */


    // Inherit from IClassFixture to get the DatabaseFixture instance
    public class MetadataFromSQLiteFactoryTests : IClassFixture<DatabaseFixture>
    {
        private readonly DatabaseFixture _fixture;
        private readonly DataLinqDatabaseConnection _sqliteConnection;
        private readonly string _sqliteDbPath;

        public MetadataFromSQLiteFactoryTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
            // Get the SQLite connection details from the fixture
            _sqliteConnection = _fixture.EmployeeConnections.SingleOrDefault(c => c.Type == DatabaseType.SQLite)
                ?? throw new InvalidOperationException("SQLite connection not found in fixture. Ensure SQLite tests are configured.");

            if (_sqliteConnection.ConnectionString.Path == null)
            {
                throw new InvalidOperationException("SQLite connection string does not contain a valid path. Ensure the connection string is correctly configured.");
            }

            // Determine the absolute path to the database file for the factory
            _sqliteDbPath = Path.GetFullPath(_sqliteConnection.ConnectionString.Path); // Get rooted path relative to config
            if (!File.Exists(_sqliteDbPath))
            {
                // Attempt to find it relative to the test execution directory if not found relative to config
                var altPath = Path.Combine(Directory.GetCurrentDirectory(), _sqliteConnection.ConnectionString.Path);
                if (File.Exists(altPath))
                {
                    _sqliteDbPath = altPath;
                }
                else
                {
                    throw new FileNotFoundException($"SQLite database file not found at expected paths: '{_sqliteDbPath}' or '{altPath}'. Ensure it's copied to the test output or referenced correctly.");
                }
            }
        }

        // Helper to run the parser with default options
        private DatabaseDefinition ParseEmployeesDb(MetadataFromDatabaseFactoryOptions? options = null)
        {
            var factory = new MetadataFromSQLiteFactory(options ?? new MetadataFromDatabaseFactoryOptions());
            var result = factory.ParseDatabase(
                _sqliteConnection.DatabaseConfig.Name, // "employees" from config
                _sqliteConnection.DatabaseConfig.CsType, // "EmployeesDb" from config
                _sqliteConnection.DatabaseConfig.Namespace, // "DataLinq.Tests.Models.Employees"
                _sqliteDbPath, // Use the actual file path as DB name for SQLite factory
                _sqliteConnection.ConnectionString.Original); // Connection string

            Assert.True(result.HasValue, result.HasFailed ? result.Failure.ToString() : "Parsing failed");
            return result.Value;
        }

        [Fact]
        public void TestParseDatabase_Employees_SQLite()
        {
            // Act
            var dbDefinition = ParseEmployeesDb();

            // Assert
            Assert.NotNull(dbDefinition);
            Assert.Equal(_sqliteConnection.DatabaseConfig.CsType, dbDefinition.CsType.Name);
            // SQLite factory might derive DbName from the file path if not overridden by attribute
            // Let's check the Name from config instead for consistency
            Assert.Equal(_sqliteConnection.DatabaseConfig.Name, dbDefinition.Name);
            Assert.Equal(_sqliteConnection.DatabaseConfig.Namespace, dbDefinition.CsType.Namespace);

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
        public void TestParseTable_SpecificTable_SQLite()
        {
            // Arrange
            var options = new MetadataFromDatabaseFactoryOptions { Tables = ["departments"] };

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
        public void TestParseColumn_SpecificColumn_SQLite()
        {
            // Arrange
            var dbDefinition = ParseEmployeesDb();
            var employeesTable = dbDefinition.TableModels.Single(tm => tm.Table.DbName == "employees").Table;

            // Act & Assert
            // emp_no (PK, AutoIncrement, INTEGER)
            var empNoCol = employeesTable.Columns.Single(c => c.DbName == "emp_no");
            Assert.True(empNoCol.PrimaryKey);
            Assert.True(empNoCol.AutoIncrement); // Check AUTOINCREMENT detection
            Assert.False(empNoCol.Nullable);
            Assert.Equal("int", empNoCol.ValueProperty.CsType.Name);
            var empNoDbType = empNoCol.GetDbTypeFor(DatabaseType.SQLite);
            Assert.NotNull(empNoDbType);
            Assert.Equal("integer", empNoDbType.Name); // SQLite uses INTEGER affinity

            // birth_date (TEXT, mapped to DateOnly)
            var birthDateCol = employeesTable.Columns.Single(c => c.DbName == "birth_date");
            Assert.False(birthDateCol.Nullable);
            Assert.Equal("DateOnly", birthDateCol.ValueProperty.CsType.Name);
            var birthDateDbType = birthDateCol.GetDbTypeFor(DatabaseType.SQLite);
            Assert.NotNull(birthDateDbType);
            Assert.Equal("text", birthDateDbType.Name); // Stored as TEXT

            // gender (INTEGER, mapped to Enum)
            var genderCol = employeesTable.Columns.Single(c => c.DbName == "gender");
            Assert.False(genderCol.Nullable);
            Assert.Equal("enum", genderCol.ValueProperty.CsType.Name); // Default mapping
            var genderDbType = genderCol.GetDbTypeFor(DatabaseType.SQLite);
            Assert.NotNull(genderDbType);
            Assert.Equal("integer", genderDbType.Name); // Enums likely stored as INTEGER
                                                        // Note: SQLite doesn't have native ENUM type, so DbEnumValues might be empty unless populated by transformer

            // IsDeleted (INTEGER nullable, mapped to bool?)
            var isDeletedCol = employeesTable.Columns.SingleOrDefault(c => c.DbName == "IsDeleted");
            if (isDeletedCol != null)
            {
                Assert.True(isDeletedCol.Nullable); // pragma_table_info 'notnull' is 0 for nullable
                Assert.Equal("bool", isDeletedCol.ValueProperty.CsType.Name);
                Assert.True(isDeletedCol.ValueProperty.CsNullable);
                var isDeletedDbType = isDeletedCol.GetDbTypeFor(DatabaseType.SQLite);
                Assert.NotNull(isDeletedDbType);
                Assert.Equal("integer", isDeletedDbType.Name); // Booleans stored as INTEGER
            }
        }

        [Fact]
        public void TestParseRelations_SQLite()
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
            // SQLite FK constraints often don't have explicit names accessible easily via PRAGMA unless defined with CONSTRAINT name
            // We check the attribute structure based on PRAGMA results
            var fkEmpAttr = fkEmpNoCol.ValueProperty.Attributes.OfType<ForeignKeyAttribute>().SingleOrDefault(a => a.Table == "employees");
            Assert.NotNull(fkEmpAttr);
            Assert.Equal("emp_no", fkEmpAttr.Column);
            // Assert.Equal("0", fkEmpAttr.Name); // PRAGMA FK list often gives numeric ID, might vary. Check actual naming convention used by factory.


            // Check Foreign Key flags and attributes from dept-emp to departments
            var fkDeptNoCol = deptEmpTable.Columns.Single(c => c.DbName == "dept_no");
            Assert.True(fkDeptNoCol.ForeignKey);
            var fkDeptAttr = fkDeptNoCol.ValueProperty.Attributes.OfType<ForeignKeyAttribute>().SingleOrDefault(a => a.Table == "departments");
            Assert.NotNull(fkDeptAttr);
            Assert.Equal("dept_no", fkDeptAttr.Column);
            // Assert.Equal("1", fkDeptAttr.Name); // Might be 1 or another ID.

            // Check Relation Properties were added
            Assert.Contains(employeesTable.Model.RelationProperties, rp => rp.Key == "dept_emp");
            Assert.Contains(departmentsTable.Model.RelationProperties, rp => rp.Key == "dept_emp");
            Assert.Contains(deptEmpTable.Model.RelationProperties, rp => rp.Key == "employees");
            Assert.Contains(deptEmpTable.Model.RelationProperties, rp => rp.Key == "departments");
        }

        [Fact]
        public void TestParseIndices_SQLite()
        {
            // Arrange
            var dbDefinition = ParseEmployeesDb();
            var departmentsTable = dbDefinition.TableModels.Single(tm => tm.Table.DbName == "departments").Table;

            // Act
            // Parsing happens during ParseDatabase call

            // Assert
            // Check unique index on departments.dept_name
            var deptNameCol = departmentsTable.Columns.Single(c => c.DbName == "dept_name");
            // SQLite might name unique constraint indexes like "sqlite_autoindex_departments_1" or similar
            // Or it might be explicitly named if created with CREATE UNIQUE INDEX.
            // We check for an index attribute that has the Unique characteristic.
            var indexAttr = deptNameCol.ValueProperty.Attributes.OfType<IndexAttribute>().SingleOrDefault(a => a.Characteristic == IndexCharacteristic.Unique);
            Assert.NotNull(indexAttr);
            Assert.Equal(IndexCharacteristic.Unique, indexAttr.Characteristic);
            Assert.Equal(IndexType.BTREE, indexAttr.Type); // SQLite default

            // Verify the ColumnIndex object on the TableDefinition
            var columnIndex = departmentsTable.ColumnIndices.SingleOrDefault(ci => ci.Characteristic == IndexCharacteristic.Unique && ci.Columns.Contains(deptNameCol));
            Assert.NotNull(columnIndex);
            Assert.Single(columnIndex.Columns);
            Assert.Same(deptNameCol, columnIndex.Columns[0]);
        }

        [Fact]
        public void TestViewParsing_SQLite()
        {
            // Arrange
            var dbDefinition = ParseEmployeesDb();

            // Act
            var viewTableModel = dbDefinition.TableModels.SingleOrDefault(tm => tm.Table.DbName == "current_dept_emp");

            // Assert
            Assert.NotNull(viewTableModel);
            Assert.Equal(TableType.View, viewTableModel.Table.Type);
            var viewDefinition = Assert.IsType<ViewDefinition>(viewTableModel.Table);
            // SQLite stores the CREATE VIEW statement in sqlite_master.sql
            Assert.False(string.IsNullOrWhiteSpace(viewDefinition.Definition));
            Assert.Contains("SELECT", viewDefinition.Definition, System.StringComparison.OrdinalIgnoreCase); // Check it looks like a SELECT
                                                                                                             // Check columns of the view
            Assert.Equal(4, viewDefinition.Columns.Length);
            Assert.Contains(viewDefinition.Columns, c => c.DbName == "emp_no");
            Assert.Contains(viewDefinition.Columns, c => c.DbName == "dept_no");
        }

        [Fact]
        public void TestTableFiltering_SQLite_TablesOnly()
        {
            // Arrange
            var options = new MetadataFromDatabaseFactoryOptions { Tables = ["employees", "salaries"] };

            // Act
            var dbDefinition = ParseEmployeesDb(options);

            // Assert
            Assert.Equal(2, dbDefinition.TableModels.Length);
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "employees");
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "salaries");
            Assert.All(dbDefinition.TableModels, tm => Assert.Equal(TableType.Table, tm.Table.Type));
        }

        [Fact]
        public void TestTableFiltering_SQLite_ViewsOnly()
        {
            // Arrange
            var options = new MetadataFromDatabaseFactoryOptions { Views = ["current_dept_emp"] };

            // Act
            var dbDefinition = ParseEmployeesDb(options);

            // Assert
            Assert.Single(dbDefinition.TableModels);
            Assert.Contains(dbDefinition.TableModels, tm => tm.Table.DbName == "current_dept_emp");
            Assert.All(dbDefinition.TableModels, tm => Assert.Equal(TableType.View, tm.Table.Type));
        }
    }
}