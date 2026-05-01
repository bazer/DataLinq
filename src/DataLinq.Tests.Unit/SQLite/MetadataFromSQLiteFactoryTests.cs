using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.SQLite;

public class MetadataFromSQLiteFactoryTests
{
    [Test]
    public async Task ParseDatabase_BuildsExpectedTablesViewsAndColumns()
    {
        using var fixture = SqliteMetadataFixture.CreateEmployeesSchema();

        var databaseDefinition = fixture.ParseDatabase();
        var employees = databaseDefinition.TableModels.Single(tm => tm.Table.DbName == "employees").Table;
        var currentDeptEmp = databaseDefinition.TableModels.Single(tm => tm.Table.DbName == "current_dept_emp").Table;

        await Assert.That(databaseDefinition.CsType.Name).IsEqualTo("EmployeesDb");
        await Assert.That(databaseDefinition.Name).IsEqualTo("employees");
        await Assert.That(databaseDefinition.CsType.Namespace).IsEqualTo("DataLinq.Tests.Models.Employees");
        await Assert.That(databaseDefinition.TableModels.Length).IsEqualTo(5);
        await Assert.That(databaseDefinition.TableModels.Count(tm => tm.Table.Type == TableType.Table)).IsEqualTo(3);
        await Assert.That(databaseDefinition.TableModels.Count(tm => tm.Table.Type == TableType.View)).IsEqualTo(2);

        await Assert.That(employees.Columns.Length).IsEqualTo(7);
        await Assert.That(employees.Model.CsType.Name).IsEqualTo("employees");
        await Assert.That(currentDeptEmp.Type).IsEqualTo(TableType.View);
    }

    [Test]
    public async Task ParseDatabase_IncludeFilter_ReturnsOnlyRequestedObjects()
    {
        using var fixture = SqliteMetadataFixture.CreateEmployeesSchema();

        var tablesOnly = fixture.ParseDatabase(options: new MetadataFromDatabaseFactoryOptions { Include = ["departments"] });
        var viewsOnly = fixture.ParseDatabase(options: new MetadataFromDatabaseFactoryOptions { Include = ["current_dept_emp"] });

        await Assert.That(tablesOnly.TableModels.Length).IsEqualTo(1);
        await Assert.That(tablesOnly.TableModels[0].Table.DbName).IsEqualTo("departments");
        await Assert.That(tablesOnly.TableModels[0].Table.Type).IsEqualTo(TableType.Table);

        await Assert.That(viewsOnly.TableModels.Length).IsEqualTo(1);
        await Assert.That(viewsOnly.TableModels[0].Table.DbName).IsEqualTo("current_dept_emp");
        await Assert.That(viewsOnly.TableModels[0].Table.Type).IsEqualTo(TableType.View);
    }

    [Test]
    public async Task ParseColumns_MapsSQLiteTypesAndDefaultNames()
    {
        using var fixture = SqliteMetadataFixture.CreateEmployeesSchema();

        var employees = fixture.ParseDatabase().TableModels.Single(tm => tm.Table.DbName == "employees").Table;
        var empNo = employees.Columns.Single(c => c.DbName == "emp_no");
        var birthDate = employees.Columns.Single(c => c.DbName == "birth_date");
        var gender = employees.Columns.Single(c => c.DbName == "gender");
        var isDeleted = employees.Columns.Single(c => c.DbName == "is_deleted");

        await Assert.That(empNo.PrimaryKey).IsTrue();
        await Assert.That(empNo.AutoIncrement).IsTrue();
        await Assert.That(empNo.ValueProperty.CsType.Name).IsEqualTo("int");
        await Assert.That(empNo.GetDbTypeFor(DatabaseType.SQLite)!.Name).IsEqualTo("integer");

        await Assert.That(birthDate.ValueProperty.CsType.Name).IsEqualTo("DateOnly");
        await Assert.That(birthDate.GetDbTypeFor(DatabaseType.SQLite)!.Name).IsEqualTo("text");

        await Assert.That(gender.ValueProperty.CsType.Name).IsEqualTo("int");
        await Assert.That(gender.GetDbTypeFor(DatabaseType.SQLite)!.Name).IsEqualTo("integer");

        await Assert.That(isDeleted.Nullable).IsTrue();
        await Assert.That(isDeleted.ValueProperty.CsType.Name).IsEqualTo("bool");
        await Assert.That(isDeleted.ValueProperty.CsNullable).IsTrue();
    }

    [Test]
    public async Task ParseRelations_MarksForeignKeysAndCreatesRelationProperties()
    {
        using var fixture = SqliteMetadataFixture.CreateEmployeesSchema();

        var database = fixture.ParseDatabase();
        var deptEmpTable = database.TableModels.Single(tm => tm.Table.DbName == "dept-emp").Table;
        var employeesTable = database.TableModels.Single(tm => tm.Table.DbName == "employees").Table;
        var departmentsTable = database.TableModels.Single(tm => tm.Table.DbName == "departments").Table;

        var employeeForeignKey = deptEmpTable.Columns.Single(c => c.DbName == "emp_no");
        var departmentForeignKey = deptEmpTable.Columns.Single(c => c.DbName == "dept_no");

        await Assert.That(employeeForeignKey.ForeignKey).IsTrue();
        await Assert.That(departmentForeignKey.ForeignKey).IsTrue();

        var employeeAttribute = employeeForeignKey.ValueProperty.Attributes.OfType<ForeignKeyAttribute>().Single(a => a.Table == "employees");
        var departmentAttribute = departmentForeignKey.ValueProperty.Attributes.OfType<ForeignKeyAttribute>().Single(a => a.Table == "departments");

        await Assert.That(employeeAttribute.Column).IsEqualTo("emp_no");
        await Assert.That(departmentAttribute.Column).IsEqualTo("dept_no");

        await Assert.That(employeesTable.Model.RelationProperties.ContainsKey("dept_emp")).IsTrue();
        await Assert.That(departmentsTable.Model.RelationProperties.ContainsKey("dept_emp")).IsTrue();
        await Assert.That(deptEmpTable.Model.RelationProperties.ContainsKey("EmpNo")).IsTrue();
        await Assert.That(deptEmpTable.Model.RelationProperties.ContainsKey("DeptNo")).IsTrue();
    }

    [Test]
    public async Task ParseIndices_MapsUniqueIndexToColumnIndex()
    {
        using var fixture = SqliteMetadataFixture.CreateEmployeesSchema();

        var departmentsTable = fixture.ParseDatabase().TableModels.Single(tm => tm.Table.DbName == "departments").Table;
        var deptNameColumn = departmentsTable.Columns.Single(c => c.DbName == "dept_name");

        var indexAttribute = deptNameColumn.ValueProperty.Attributes.OfType<IndexAttribute>().Single(a => a.Characteristic == IndexCharacteristic.Unique);
        var columnIndex = departmentsTable.ColumnIndices.Single(ci => ci.Characteristic == IndexCharacteristic.Unique && ci.Columns.Contains(deptNameColumn));

        await Assert.That(indexAttribute.Type).IsEqualTo(IndexType.BTREE);
        await Assert.That(columnIndex.Columns.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(deptNameColumn, columnIndex.Columns[0])).IsTrue();
    }

    [Test]
    public async Task ParseIndices_CompositeIndex_PreservesOrderedColumns()
    {
        using var fixture = SqliteMetadataFixture.Create(
            """
            CREATE TABLE account (
                id INTEGER PRIMARY KEY,
                accounting_year INTEGER NOT NULL,
                account_number INTEGER NOT NULL,
                display_name TEXT NOT NULL
            );
            """,
            """
            CREATE UNIQUE INDEX idx_account_year_number ON account (accounting_year, account_number);
            """);

        var database = fixture.ParseDatabase("TempSqliteDb", "TempSqliteDb", "DataLinq.Tests");
        var table = database.TableModels.Single(tm => tm.Table.DbName == "account").Table;
        var index = table.ColumnIndices.Single(x => x.Name == "idx_account_year_number");
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "account.cs");

        await Assert.That(index.Characteristic).IsEqualTo(IndexCharacteristic.Unique);
        await Assert.That(index.Columns.Select(x => x.DbName).SequenceEqual(["accounting_year", "account_number"])).IsTrue();
        await Assert.That(generatedFile.contents).Contains("[Index(\"idx_account_year_number\", IndexCharacteristic.Unique, IndexType.BTREE, \"accounting_year\", \"account_number\")]");
        await Assert.That(generatedFile.contents).DoesNotContain("[Index(\"idx_account_year_number\", IndexCharacteristic.Unique, IndexType.BTREE, \"accounting_year\", \"account_number\")]\n    [Column(\"accounting_year\")]");
    }

    [Test]
    public async Task ParseIndices_UnsupportedSQLiteIndexShapesWarnAndSkip()
    {
        using var fixture = SqliteMetadataFixture.Create(
            """
            CREATE TABLE unsupported_indexes (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                count INTEGER NOT NULL
            );
            """,
            """
            CREATE INDEX idx_unsupported_expression ON unsupported_indexes (lower(name));
            """,
            """
            CREATE INDEX idx_unsupported_partial ON unsupported_indexes (name) WHERE count > 0;
            """,
            """
            CREATE INDEX idx_unsupported_descending ON unsupported_indexes (name DESC);
            """);
        var warnings = new List<string>();

        var table = fixture.ParseDatabase("TempSqliteDb", "TempSqliteDb", "DataLinq.Tests", new MetadataFromDatabaseFactoryOptions
        {
            Log = warnings.Add
        }).TableModels.Single().Table;

        await Assert.That(table.ColumnIndices.Any(x => x.Name.StartsWith("idx_unsupported", StringComparison.Ordinal))).IsFalse();
        await Assert.That(warnings.Any(x => x.Contains("Skipping unsupported SQLite expression index 'idx_unsupported_expression'", StringComparison.Ordinal))).IsTrue();
        await Assert.That(warnings.Any(x => x.Contains("Skipping unsupported SQLite partial index 'idx_unsupported_partial'", StringComparison.Ordinal))).IsTrue();
        await Assert.That(warnings.Any(x => x.Contains("Skipping unsupported SQLite descending index 'idx_unsupported_descending'", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task ParseView_CapturesDefinitionAndColumns()
    {
        using var fixture = SqliteMetadataFixture.CreateEmployeesSchema();

        var view = (ViewDefinition)fixture.ParseDatabase().TableModels.Single(tm => tm.Table.DbName == "current_dept_emp").Table;

        await Assert.That(view.Definition).Contains("SELECT");
        await Assert.That(view.Columns.Length).IsEqualTo(4);
        await Assert.That(view.Columns.Any(c => c.DbName == "emp_no")).IsTrue();
        await Assert.That(view.Columns.Any(c => c.DbName == "dept_no")).IsTrue();
    }

    [Test]
    public async Task ParseDefaults_TypedDefaultsAreImported()
    {
        using var fixture = SqliteMetadataFixture.Create(
            """
            CREATE TABLE default_values (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                count INTEGER NOT NULL DEFAULT '0',
                is_deleted INTEGER DEFAULT '1',
                amount REAL NOT NULL DEFAULT (1.5),
                note TEXT NOT NULL DEFAULT '0',
                display_name TEXT NOT NULL DEFAULT 'abc',
                birth_date TEXT NOT NULL DEFAULT '2024-01-02',
                alarm_time TEXT NOT NULL DEFAULT '12:34:56',
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                created_date TEXT NOT NULL DEFAULT CURRENT_DATE,
                created_time TEXT NOT NULL DEFAULT CURRENT_TIME
            );
            """);

        var table = fixture.ParseDatabase("TempSqliteDb", "TempSqliteDb", "DataLinq.Tests").TableModels.Single().Table;

        await Assert.That(table.Columns.Single(c => c.DbName == "count").ValueProperty.GetDefaultAttribute()!.Value).IsEqualTo(0);
        await Assert.That((bool)table.Columns.Single(c => c.DbName == "is_deleted").ValueProperty.GetDefaultAttribute()!.Value).IsTrue();
        await Assert.That(table.Columns.Single(c => c.DbName == "amount").ValueProperty.GetDefaultAttribute()!.Value).IsEqualTo(1.5D);
        await Assert.That(table.Columns.Single(c => c.DbName == "note").ValueProperty.GetDefaultAttribute()!.Value).IsEqualTo("0");
        await Assert.That(table.Columns.Single(c => c.DbName == "display_name").ValueProperty.GetDefaultAttribute()!.Value).IsEqualTo("abc");
        await Assert.That(table.Columns.Single(c => c.DbName == "birth_date").ValueProperty.GetDefaultAttribute()!.Value).IsEqualTo(new DateOnly(2024, 1, 2));
        await Assert.That(table.Columns.Single(c => c.DbName == "alarm_time").ValueProperty.GetDefaultAttribute()!.Value).IsEqualTo(new TimeOnly(12, 34, 56));
        await Assert.That(table.Columns.Single(c => c.DbName == "created_at").ValueProperty.GetDefaultAttribute()).IsTypeOf<DefaultCurrentTimestampAttribute>();
        await Assert.That(table.Columns.Single(c => c.DbName == "created_date").ValueProperty.GetDefaultAttribute()).IsTypeOf<DefaultCurrentTimestampAttribute>();
        await Assert.That(table.Columns.Single(c => c.DbName == "created_time").ValueProperty.GetDefaultAttribute()).IsTypeOf<DefaultCurrentTimestampAttribute>();
    }

    [Test]
    public async Task ParseDefaults_UnsupportedExpressionWarnsAndSkips()
    {
        using var fixture = SqliteMetadataFixture.Create(
            """
            CREATE TABLE unsupported_defaults (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                count INTEGER NOT NULL DEFAULT (0 + 1)
            );
            """);
        var warnings = new List<string>();

        var database = fixture.ParseDatabase("TempSqliteDb", "TempSqliteDb", "DataLinq.Tests", new MetadataFromDatabaseFactoryOptions
        {
            Log = warnings.Add
        });
        var property = database.TableModels.Single().Table.Columns.Single(c => c.DbName == "count").ValueProperty;

        await Assert.That(property.GetDefaultAttribute()).IsNull();
        await Assert.That(warnings.Any(x => x.Contains("Skipping unsupported SQLite default", StringComparison.Ordinal) && x.Contains("unsupported_defaults.count", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task CreateTables_EmitsParsedDefaults()
    {
        using var fixture = SqliteMetadataFixture.Create(
            """
            CREATE TABLE generated_defaults (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                count INTEGER NOT NULL DEFAULT '0',
                is_deleted INTEGER DEFAULT '1',
                display_name TEXT NOT NULL DEFAULT 'abc',
                created_date TEXT NOT NULL DEFAULT CURRENT_DATE,
                created_time TEXT NOT NULL DEFAULT CURRENT_TIME,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        var database = fixture.ParseDatabase("TempSqliteDb", "TempSqliteDb", "DataLinq.Tests");
        var sqlResult = new SqlFromSQLiteFactory().GetCreateTables(database, foreignKeyRestrict: false).ValueOrException().Text;

        await Assert.That(sqlResult).Contains("DEFAULT 0");
        await Assert.That(sqlResult).Contains("DEFAULT 1");
        await Assert.That(sqlResult).Contains("DEFAULT 'abc'");
        await Assert.That(sqlResult).Contains("DEFAULT CURRENT_DATE");
        await Assert.That(sqlResult).Contains("DEFAULT CURRENT_TIME");
        await Assert.That(sqlResult).Contains("DEFAULT CURRENT_TIMESTAMP");
    }

    private sealed class SqliteMetadataFixture : IDisposable
    {
        private SqliteMetadataFixture(string databasePath)
        {
            DatabasePath = databasePath;
        }

        public string DatabasePath { get; }

        public static SqliteMetadataFixture Create(params string[] statements)
        {
            var path = Path.Combine(Path.GetTempPath(), $"datalinq-sqlite-metadata-{Guid.NewGuid():N}.db");
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();

            foreach (var statement in statements)
            {
                using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }

            return new SqliteMetadataFixture(path);
        }

        public static SqliteMetadataFixture CreateEmployeesSchema()
        {
            return Create(
                """
                CREATE TABLE departments (
                    dept_no TEXT PRIMARY KEY,
                    dept_name TEXT NOT NULL UNIQUE
                );
                """,
                """
                CREATE TABLE employees (
                    "emp_no" INTEGER PRIMARY KEY AUTOINCREMENT,
                    "birth_date" TEXT NOT NULL,
                    "first_name" TEXT NOT NULL,
                    "last_name" TEXT NOT NULL,
                    "gender" INTEGER NOT NULL,
                    "hire_date" TEXT NOT NULL,
                    "is_deleted" INTEGER
                );
                """,
                """
                CREATE TABLE "dept-emp" (
                    emp_no INTEGER NOT NULL,
                    dept_no TEXT NOT NULL,
                    from_date TEXT NOT NULL,
                    to_date TEXT NOT NULL,
                    PRIMARY KEY (emp_no, dept_no),
                    FOREIGN KEY (emp_no) REFERENCES employees(emp_no),
                    FOREIGN KEY (dept_no) REFERENCES departments(dept_no)
                );
                """,
                """
                CREATE VIEW current_dept_emp AS
                SELECT emp_no, dept_no, from_date, to_date
                FROM "dept-emp";
                """,
                """
                CREATE VIEW dept_emp_latest_date AS
                SELECT dept_no, emp_no, MAX(from_date) AS from_date
                FROM "dept-emp"
                GROUP BY dept_no, emp_no;
                """);
        }

        public DatabaseDefinition ParseDatabase(
            string databaseName = "employees",
            string csTypeName = "EmployeesDb",
            string csNamespace = "DataLinq.Tests.Models.Employees",
            MetadataFromDatabaseFactoryOptions? options = null)
        {
            return new MetadataFromSQLiteFactory(options ?? new MetadataFromDatabaseFactoryOptions())
                .ParseDatabase(
                    databaseName,
                    csTypeName,
                    csNamespace,
                    DatabasePath,
                    $"Data Source={DatabasePath}")
                .ValueOrException();
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(DatabasePath))
                    File.Delete(DatabasePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
