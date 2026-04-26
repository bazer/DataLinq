using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Testing;

namespace DataLinq.Tests.MySql;

public class MetadataFromServerFactoryTests
{
    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_EmployeesSchema_BuildsExpectedTablesViewsAndColumns(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_EmployeesSchema_BuildsExpectedTablesViewsAndColumns),
            """
            CREATE TABLE departments (
                dept_no VARCHAR(4) PRIMARY KEY,
                dept_name VARCHAR(40) NOT NULL UNIQUE
            );
            """,
            """
            CREATE TABLE employees (
                emp_no INT PRIMARY KEY AUTO_INCREMENT,
                birth_date DATE NOT NULL,
                first_name VARCHAR(14) NOT NULL,
                last_name VARCHAR(16) NOT NULL,
                gender ENUM('M', 'F') NOT NULL,
                hire_date DATE NOT NULL,
                IsDeleted BIT(1) NULL
            );
            """,
            """
            CREATE TABLE `dept-emp` (
                emp_no INT NOT NULL,
                dept_no VARCHAR(4) NOT NULL,
                from_date DATE NOT NULL,
                to_date DATE NOT NULL,
                PRIMARY KEY (emp_no, dept_no),
                CONSTRAINT dept_emp_ibfk_1 FOREIGN KEY (emp_no) REFERENCES employees(emp_no),
                CONSTRAINT dept_emp_ibfk_2 FOREIGN KEY (dept_no) REFERENCES departments(dept_no)
            );
            """,
            """
            CREATE VIEW current_dept_emp AS
            SELECT emp_no, dept_no, from_date, to_date
            FROM `dept-emp`;
            """,
            """
            CREATE VIEW dept_emp_latest_date AS
            SELECT dept_no, emp_no, MAX(from_date) AS from_date
            FROM `dept-emp`
            GROUP BY dept_no, emp_no;
            """);

        var database = schema.ParseDatabase("employees", "EmployeesDb", "DataLinq.Tests.Models.Employees");
        var employees = database.TableModels.Single(tm => tm.Table.DbName == "employees").Table;
        var currentDeptEmp = database.TableModels.Single(tm => tm.Table.DbName == "current_dept_emp").Table;

        await Assert.That(database.CsType.Name).IsEqualTo("EmployeesDb");
        await Assert.That(database.DbName).IsEqualTo(schema.Connection.DataSourceName);
        await Assert.That(database.CsType.Namespace).IsEqualTo("DataLinq.Tests.Models.Employees");
        await Assert.That(database.TableModels.Length).IsEqualTo(5);
        await Assert.That(database.TableModels.Count(tm => tm.Table.Type == TableType.Table)).IsEqualTo(3);
        await Assert.That(database.TableModels.Count(tm => tm.Table.Type == TableType.View)).IsEqualTo(2);
        await Assert.That(employees.Columns.Length).IsEqualTo(7);
        await Assert.That(currentDeptEmp.Type).IsEqualTo(TableType.View);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseDatabase_IncludeFilter_ReturnsOnlyRequestedObjects(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_IncludeFilter_ReturnsOnlyRequestedObjects),
            "CREATE TABLE table1 (id INT PRIMARY KEY);",
            "CREATE TABLE table2 (id INT PRIMARY KEY);",
            "CREATE VIEW view1 AS SELECT * FROM table1;",
            "CREATE VIEW view2 AS SELECT * FROM table2;");

        var tablesOnly = schema.ParseDatabase("TestDb", "TestDb", "TestNs", new MetadataFromDatabaseFactoryOptions { Include = ["table1"] });
        var viewsOnly = schema.ParseDatabase("TestDb", "TestDb", "TestNs", new MetadataFromDatabaseFactoryOptions { Include = ["view2"] });

        await Assert.That(tablesOnly.TableModels.Length).IsEqualTo(1);
        await Assert.That(tablesOnly.TableModels[0].Table.DbName).IsEqualTo("table1");
        await Assert.That(tablesOnly.TableModels[0].Table.Type).IsEqualTo(TableType.Table);

        await Assert.That(viewsOnly.TableModels.Length).IsEqualTo(1);
        await Assert.That(viewsOnly.TableModels[0].Table.DbName).IsEqualTo("view2");
        await Assert.That(viewsOnly.TableModels[0].Table.Type).IsEqualTo(TableType.View);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseColumns_MapsExpectedTypesAndEnumMetadata(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseColumns_MapsExpectedTypesAndEnumMetadata),
            """
            CREATE TABLE employees (
                emp_no INT PRIMARY KEY AUTO_INCREMENT,
                birth_date DATE NOT NULL,
                first_name VARCHAR(14) NOT NULL,
                gender ENUM('M', 'F') NOT NULL,
                IsDeleted BIT(1) NULL
            );
            """);

        var employees = schema.ParseDatabase("employees", "EmployeesDb", "DataLinq.Tests.Models.Employees")
            .TableModels.Single(tm => tm.Table.DbName == "employees").Table;

        var empNo = employees.Columns.Single(c => c.DbName == "emp_no");
        var birthDate = employees.Columns.Single(c => c.DbName == "birth_date");
        var gender = employees.Columns.Single(c => c.DbName == "gender");
        var isDeleted = employees.Columns.Single(c => c.DbName == "IsDeleted");

        await Assert.That(empNo.PrimaryKey).IsTrue();
        await Assert.That(empNo.AutoIncrement).IsTrue();
        await Assert.That(empNo.Nullable).IsFalse();
        await Assert.That(empNo.ValueProperty.CsType.Name).IsEqualTo("int");
        await Assert.That(empNo.GetDbTypeFor(provider.DatabaseType)?.Name).IsEqualTo("int");

        await Assert.That(birthDate.ValueProperty.CsType.Name).IsEqualTo("DateOnly");
        await Assert.That(birthDate.GetDbTypeFor(provider.DatabaseType)?.Name).IsEqualTo("date");

        await Assert.That(gender.ValueProperty.CsType.Name.EndsWith("Value", StringComparison.Ordinal)).IsTrue();
        await Assert.That(gender.ValueProperty.EnumProperty is not null).IsTrue();
        await Assert.That(gender.ValueProperty.EnumProperty!.Value.DbEnumValues.Count).IsEqualTo(2);
        await Assert.That(gender.ValueProperty.EnumProperty!.Value.DbEnumValues.Any(x => x.name == "M")).IsTrue();
        await Assert.That(gender.ValueProperty.EnumProperty!.Value.DbEnumValues.Any(x => x.name == "F")).IsTrue();

        await Assert.That(isDeleted.Nullable).IsTrue();
        await Assert.That(isDeleted.ValueProperty.CsType.Name).IsEqualTo("bool");
        await Assert.That(isDeleted.ValueProperty.CsNullable).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveServerProviders))]
    public async Task ParseRelationsAndIndices_CreatesExpectedForeignKeysRelationPropertiesAndUniqueIndex(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseRelationsAndIndices_CreatesExpectedForeignKeysRelationPropertiesAndUniqueIndex),
            """
            CREATE TABLE departments (
                dept_no VARCHAR(4) PRIMARY KEY,
                dept_name VARCHAR(40) NOT NULL UNIQUE
            );
            """,
            """
            CREATE TABLE employees (
                emp_no INT PRIMARY KEY AUTO_INCREMENT
            );
            """,
            """
            CREATE TABLE `dept-emp` (
                emp_no INT NOT NULL,
                dept_no VARCHAR(4) NOT NULL,
                PRIMARY KEY (emp_no, dept_no),
                CONSTRAINT dept_emp_ibfk_1 FOREIGN KEY (emp_no) REFERENCES employees(emp_no),
                CONSTRAINT dept_emp_ibfk_2 FOREIGN KEY (dept_no) REFERENCES departments(dept_no)
            );
            """);

        var database = schema.ParseDatabase("employees", "EmployeesDb", "DataLinq.Tests.Models.Employees");
        var deptEmp = database.TableModels.Single(tm => tm.Table.DbName == "dept-emp").Table;
        var employees = database.TableModels.Single(tm => tm.Table.DbName == "employees").Table;
        var departments = database.TableModels.Single(tm => tm.Table.DbName == "departments").Table;

        var empFk = deptEmp.Columns.Single(c => c.DbName == "emp_no");
        var deptFk = deptEmp.Columns.Single(c => c.DbName == "dept_no");
        var deptName = departments.Columns.Single(c => c.DbName == "dept_name");

        await Assert.That(empFk.ForeignKey).IsTrue();
        await Assert.That(deptFk.ForeignKey).IsTrue();

        var empFkAttribute = empFk.ValueProperty.Attributes.OfType<ForeignKeyAttribute>().Single(a => a.Table == "employees");
        var deptFkAttribute = deptFk.ValueProperty.Attributes.OfType<ForeignKeyAttribute>().Single(a => a.Table == "departments");
        var indexAttribute = deptName.ValueProperty.Attributes.OfType<IndexAttribute>().Single(a => a.Characteristic == IndexCharacteristic.Unique);

        await Assert.That(empFkAttribute.Name).IsEqualTo("dept_emp_ibfk_1");
        await Assert.That(deptFkAttribute.Name).IsEqualTo("dept_emp_ibfk_2");
        await Assert.That(employees.Model.RelationProperties.ContainsKey("dept-emp")).IsTrue();
        await Assert.That(departments.Model.RelationProperties.ContainsKey("dept-emp")).IsTrue();
        await Assert.That(deptEmp.Model.RelationProperties.ContainsKey("EmpNo")).IsTrue();
        await Assert.That(deptEmp.Model.RelationProperties.ContainsKey("DeptNo")).IsTrue();
        await Assert.That(indexAttribute.Name).IsEqualTo("dept_name");
        await Assert.That(indexAttribute.Columns.Length).IsEqualTo(1);
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.MySqlProviders))]
    public async Task ParseDatabase_PrimaryKeyAlsoForeignKey_RemainsRequiredForGenerator(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_PrimaryKeyAlsoForeignKey_RemainsRequiredForGenerator),
            """
            CREATE TABLE user (
                id INT PRIMARY KEY AUTO_INCREMENT,
                username VARCHAR(255) NOT NULL
            );
            """,
            """
            CREATE TABLE user_profile (
                user_id INT PRIMARY KEY,
                bio TEXT,
                CONSTRAINT FK_Profile_User FOREIGN KEY (user_id) REFERENCES user(id)
            );
            """);

        var database = schema.ParseDatabase("PkFkDb", "PkFkDb", "TestNamespace");
        var profileModel = database.TableModels.Single(tm => tm.Table.DbName == "user_profile").Model;
        var userIdProperty = profileModel.ValueProperties.Values.Single(p => p.Column.DbName == "user_id");
        var generatorFactory = new GeneratorFileFactory(new GeneratorFileFactoryOptions());
        var methodInfo = typeof(GeneratorFileFactory).GetMethod("IsMutablePropertyRequired", BindingFlags.NonPublic | BindingFlags.Instance);

        await Assert.That(methodInfo).IsNotNull();

        var isRequired = (bool)methodInfo!.Invoke(generatorFactory, [userIdProperty])!;
        await Assert.That(isRequired).IsTrue();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.MySqlProviders))]
    public async Task ParseDatabase_RecursiveRelation_CreatesLinkedForeignKeyAndCandidateKeySides(TestProviderDescriptor provider)
    {
        using var schema = ServerSchemaDatabase.Create(
            provider,
            nameof(ParseDatabase_RecursiveRelation_CreatesLinkedForeignKeyAndCandidateKeySides),
            """
            CREATE TABLE employee (
                id INT PRIMARY KEY AUTO_INCREMENT,
                name VARCHAR(255),
                manager_id INT NULL,
                CONSTRAINT FK_Employee_Manager FOREIGN KEY (manager_id) REFERENCES employee(id)
            );
            """);

        var database = schema.ParseDatabase(
            "RecursiveDb",
            "RecursiveDb",
            "TestNamespace",
            new MetadataFromDatabaseFactoryOptions { CapitaliseNames = true });

        var employeeModel = database.TableModels.Single(tm => tm.Table.DbName == "employee").Model;
        var managerProperty = employeeModel.RelationProperties.Values.Single(p => p.PropertyName == "Manager");
        var subordinateProperty = employeeModel.RelationProperties.Values.Single(p => p.PropertyName == "Employee");

        await Assert.That(employeeModel.RelationProperties.Count).IsEqualTo(2);
        await Assert.That(managerProperty.RelationPart?.Type).IsEqualTo(RelationPartType.ForeignKey);
        await Assert.That(subordinateProperty.RelationPart?.Type).IsEqualTo(RelationPartType.CandidateKey);
        await Assert.That(managerProperty.CsType.Name.Contains("IImmutableRelation", StringComparison.Ordinal)).IsFalse();
        await Assert.That(subordinateProperty.CsType.Name.Contains("IImmutableRelation", StringComparison.Ordinal)).IsTrue();
        await Assert.That(ReferenceEquals(managerProperty.RelationPart!.Relation, subordinateProperty.RelationPart!.Relation)).IsTrue();
        await Assert.That(managerProperty.RelationPart.Relation.ConstraintName).IsEqualTo("FK_Employee_Manager");
        await Assert.That(ReferenceEquals(managerProperty.RelationPart.GetOtherSide(), subordinateProperty.RelationPart)).IsTrue();
        await Assert.That(ReferenceEquals(subordinateProperty.RelationPart.GetOtherSide(), managerProperty.RelationPart)).IsTrue();
    }
}
