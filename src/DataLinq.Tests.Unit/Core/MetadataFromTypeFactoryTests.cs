using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Tests.Models.Employees;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class MetadataFromTypeFactoryTests
{
    [Test]
    public async Task ParseModel_Employee_BuildsExpectedValueAndRelationMetadata()
    {
        var databaseDefinition = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();
        var employeeModel = databaseDefinition.TableModels.Single(tm => tm.Model.CsType.Type == typeof(Employee)).Model;

        await Assert.That(employeeModel.CsType.Name).IsEqualTo("Employee");
        await Assert.That(employeeModel.CsType.Type).IsEqualTo(typeof(Employee));
        await Assert.That(employeeModel.Table.Type).IsEqualTo(TableType.Table);
        await Assert.That(employeeModel.Table.DbName).IsEqualTo("employees");

        await AssertValueProperty(employeeModel, "emp_no", "int", true);
        await AssertValueProperty(employeeModel, "birth_date", "DateOnly", false);
        await AssertValueProperty(employeeModel, "first_name", "string", false);
        await AssertValueProperty(employeeModel, "last_name", "string", false);
        await AssertValueProperty(employeeModel, "gender", "Employeegender", false);
        await AssertValueProperty(employeeModel, "hire_date", "DateOnly", false);
        await AssertValueProperty(employeeModel, "IsDeleted", "bool", true);

        var empNoProperty = employeeModel.ValueProperties["emp_no"];
        await Assert.That(empNoProperty.Attributes.Any(a => a is PrimaryKeyAttribute)).IsTrue();
        await Assert.That(empNoProperty.Attributes.Any(a => a is AutoIncrementAttribute)).IsTrue();
        await Assert.That(empNoProperty.Attributes.Any(a => a is ColumnAttribute c && c.Name == "emp_no")).IsTrue();
        await Assert.That(empNoProperty.Attributes.Any(a => a is TypeAttribute)).IsTrue();

        var genderProperty = employeeModel.ValueProperties["gender"];
        await Assert.That(genderProperty.Attributes.Any(a => a is EnumAttribute)).IsTrue();
        await Assert.That(genderProperty.EnumProperty.HasValue).IsTrue();
        await Assert.That(genderProperty.EnumProperty!.Value.CsEnumValues.Count).IsEqualTo(2);
        await Assert.That(genderProperty.EnumProperty!.Value.CsEnumValues[0].name).IsEqualTo("M");

        await AssertRelationProperty(employeeModel, "dept_emp", "Dept_emp");
        await AssertRelationProperty(employeeModel, "dept_manager", "Manager");
        await AssertRelationProperty(employeeModel, "salaries", "Salaries");
        await AssertRelationProperty(employeeModel, "titles", "Titles");

        await Assert.That(employeeModel.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(employeeModel.ModelInstanceInterface!.Value.Name).IsEqualTo("IEmployee");
    }

    [Test]
    public async Task ParseModel_Department_BuildsExpectedMetadata()
    {
        var databaseDefinition = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();
        var departmentModel = databaseDefinition.TableModels.Single(tm => tm.Model.CsType.Type == typeof(Department)).Model;

        await Assert.That(departmentModel.CsType.Name).IsEqualTo("Department");
        await Assert.That(departmentModel.Table.DbName).IsEqualTo("departments");

        await AssertValueProperty(departmentModel, "DeptNo", "string", false);
        await AssertValueProperty(departmentModel, "Name", "string", false);

        var deptNoProperty = departmentModel.ValueProperties["DeptNo"];
        await Assert.That(deptNoProperty.Attributes.Any(a => a is PrimaryKeyAttribute)).IsTrue();
        await Assert.That(deptNoProperty.Attributes.Any(a => a is ColumnAttribute c && c.Name == "dept_no")).IsTrue();

        var nameProperty = departmentModel.ValueProperties["Name"];
        await Assert.That(nameProperty.Attributes.Any(a => a is ColumnAttribute c && c.Name == "dept_name")).IsTrue();
        await Assert.That(nameProperty.Attributes.Any(a => a is IndexAttribute i && i.Name == "dept_name" && i.Characteristic == IndexCharacteristic.Unique)).IsTrue();

        await AssertRelationProperty(departmentModel, "DepartmentEmployees", "Dept_emp");
        await AssertRelationProperty(departmentModel, "Managers", "Manager");

        await Assert.That(departmentModel.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(departmentModel.ModelInstanceInterface!.Value.Name).IsEqualTo("IDepartmentWithChangedName");
    }

    [Test]
    public async Task ParseDatabase_EmployeesDb_BuildsExpectedDatabaseMetadata()
    {
        var databaseDefinition = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(EmployeesDb)).ValueOrException();

        await Assert.That(databaseDefinition.CsType.Name).IsEqualTo("EmployeesDb");
        await Assert.That(databaseDefinition.DbName).IsEqualTo("employees");
        await Assert.That(databaseDefinition.UseCache).IsTrue();
        await Assert.That(databaseDefinition.CacheLimits.Any(cl => cl.limitType == CacheLimitType.Megabytes && cl.amount == 200)).IsTrue();
        await Assert.That(databaseDefinition.CacheLimits.Any(cl => cl.limitType == CacheLimitType.Minutes && cl.amount == 60)).IsTrue();
        await Assert.That(databaseDefinition.CacheCleanup.Any(cc => cc.cleanupType == CacheCleanupType.Minutes && cc.amount == 30)).IsTrue();

        await Assert.That(databaseDefinition.TableModels.Length).IsEqualTo(8);
        await Assert.That(databaseDefinition.TableModels.Any(tm => tm.Table.DbName == "employees" && tm.Model.CsType.Type == typeof(Employee))).IsTrue();
        await Assert.That(databaseDefinition.TableModels.Any(tm => tm.Table.DbName == "departments" && tm.Model.CsType.Type == typeof(Department))).IsTrue();
        await Assert.That(databaseDefinition.TableModels.Any(tm => tm.Table.DbName == "dept-emp" && tm.Model.CsType.Type == typeof(Dept_emp))).IsTrue();
        await Assert.That(databaseDefinition.TableModels.Any(tm => tm.Table.DbName == "dept_manager" && tm.Model.CsType.Type == typeof(Manager))).IsTrue();
        await Assert.That(databaseDefinition.TableModels.Any(tm => tm.Table.DbName == "salaries" && tm.Model.CsType.Type == typeof(Salaries))).IsTrue();
        await Assert.That(databaseDefinition.TableModels.Any(tm => tm.Table.DbName == "titles" && tm.Model.CsType.Type == typeof(Titles))).IsTrue();
        await Assert.That(databaseDefinition.TableModels.Any(tm => tm.Table.DbName == "current_dept_emp" && tm.Model.CsType.Type == typeof(current_dept_emp) && tm.Table.Type == TableType.View)).IsTrue();
        await Assert.That(databaseDefinition.TableModels.Any(tm => tm.Table.DbName == "dept_emp_latest_date" && tm.Model.CsType.Type == typeof(dept_emp_latest_date) && tm.Table.Type == TableType.View)).IsTrue();

        var employeeModel = databaseDefinition.TableModels.Single(tm => tm.Table.DbName == "employees").Model;
        var deptEmpModel = databaseDefinition.TableModels.Single(tm => tm.Table.DbName == "dept-emp").Model;

        var employeeToDeptEmp = employeeModel.RelationProperties["dept_emp"];
        var deptEmpToEmployee = deptEmpModel.RelationProperties["employees"];

        await Assert.That(employeeToDeptEmp.RelationPart).IsNotNull();
        await Assert.That(deptEmpToEmployee.RelationPart).IsNotNull();
        await Assert.That(ReferenceEquals(employeeToDeptEmp.RelationPart!.Relation, deptEmpToEmployee.RelationPart!.Relation)).IsTrue();
        await Assert.That(employeeToDeptEmp.RelationPart.Type).IsEqualTo(RelationPartType.CandidateKey);
        await Assert.That(deptEmpToEmployee.RelationPart.Type).IsEqualTo(RelationPartType.ForeignKey);
        await Assert.That(ReferenceEquals(employeeToDeptEmp.RelationPart, deptEmpToEmployee.RelationPart.GetOtherSide())).IsTrue();
        await Assert.That(ReferenceEquals(deptEmpToEmployee.RelationPart, employeeToDeptEmp.RelationPart.GetOtherSide())).IsTrue();

        var departmentsTable = databaseDefinition.TableModels.Single(tm => tm.Table.DbName == "departments").Table;
        var nameColumn = departmentsTable.Columns.Single(c => c.DbName == "dept_name");
        var uniqueIndex = departmentsTable.ColumnIndices.Single(idx => idx.Name == "dept_name");

        await Assert.That(uniqueIndex.Characteristic).IsEqualTo(IndexCharacteristic.Unique);
        await Assert.That(uniqueIndex.Columns.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(nameColumn, uniqueIndex.Columns[0])).IsTrue();
    }

    [Test]
    public async Task ParseDatabase_GeneratedTableBootstrap_ReplacesDatabasePropertyReflection()
    {
        var databaseDefinition = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(BootstrapHookDb)).ValueOrException();

        await Assert.That(databaseDefinition.TableModels.Length).IsEqualTo(1);

        var tableModel = databaseDefinition.TableModels.Single();
        await Assert.That(tableModel.CsPropertyName).IsEqualTo("Rows");
        await Assert.That(tableModel.Model.CsType.Type).IsEqualTo(typeof(BootstrapHookRow));
        await Assert.That(tableModel.Table.DbName).IsEqualTo("bootstrap_rows");
    }

    [Test]
    public async Task ParseDatabase_OldGeneratedTableBootstrapOnly_ReturnsMissingGeneratedModelHookFailure()
    {
        var result = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(OldBootstrapHookOnlyDb));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.Failure.ToString()!).Contains(typeof(OldBootstrapHookOnlyDb).FullName!);
        await Assert.That(result.Failure.ToString()!).Contains("GetDataLinqGeneratedModel");
        await Assert.That(result.Failure.ToString()!).Contains("missing the generated DataLinq metadata hook");
    }

    private static async Task AssertValueProperty(ModelDefinition model, string propertyName, string expectedTypeName, bool? expectedNullable = null)
    {
        await Assert.That(model.ValueProperties.ContainsKey(propertyName)).IsTrue();

        var property = model.ValueProperties[propertyName];
        await Assert.That(property.CsType.Name).IsEqualTo(expectedTypeName);

        if (expectedNullable.HasValue)
            await Assert.That(property.CsNullable).IsEqualTo(expectedNullable.Value);

        await Assert.That(property.Column).IsNotNull();
        await Assert.That(ReferenceEquals(property, property.Column!.ValueProperty)).IsTrue();
    }

    private static async Task AssertRelationProperty(ModelDefinition model, string propertyName, string expectedTypeName)
    {
        await Assert.That(model.RelationProperties.ContainsKey(propertyName)).IsTrue();

        var property = model.RelationProperties[propertyName];
        var matchesExpectedType = property.CsType.Name == expectedTypeName || property.CsType.Name.Contains($"<{expectedTypeName}>", StringComparison.Ordinal);

        await Assert.That(matchesExpectedType).IsTrue();
    }
}

public sealed class BootstrapHookDb : IDatabaseModel, IDataLinqGeneratedDatabaseModel<BootstrapHookDb>
{
    public static BootstrapHookDb NewDataLinqDatabase(IDataSourceAccess dataSource) => new();

    public static GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() =>
        new(
        [
            new(
                "Rows",
                typeof(BootstrapHookRow),
                typeof(ImmutableBootstrapHookRow),
                null,
                new Func<IRowData, IDataSourceAccess, IImmutableInstance>(ImmutableBootstrapHookRow.NewDataLinqImmutableInstance),
                TableType.View)
        ]);
}

public sealed class OldBootstrapHookOnlyDb : IDatabaseModel
{
    public static GeneratedTableModelDeclaration[] GetDataLinqGeneratedTableModels() =>
    [
        new(
            "Rows",
            typeof(OldBootstrapHookOnlyRow),
            typeof(ImmutableOldBootstrapHookOnlyRow),
            null,
            new Func<IRowData, IDataSourceAccess, IImmutableInstance>(ImmutableOldBootstrapHookOnlyRow.NewDataLinqImmutableInstance),
            TableType.View)
    ];
}

[View("bootstrap_rows")]
public abstract partial class BootstrapHookRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<BootstrapHookRow, BootstrapHookDb>(rowData, dataSource), IViewModel<BootstrapHookDb>
{
    [Column("id")]
    public abstract int Id { get; }
}

public partial class ImmutableBootstrapHookRow(IRowData rowData, IDataSourceAccess dataSource)
    : BootstrapHookRow(rowData, dataSource)
{
    public static IImmutableInstance NewDataLinqImmutableInstance(IRowData rowData, IDataSourceAccess dataSource) =>
        new ImmutableBootstrapHookRow(rowData, dataSource);

    public override int Id => (int)GetValue(nameof(Id));
}

[View("old_bootstrap_rows")]
public abstract partial class OldBootstrapHookOnlyRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<OldBootstrapHookOnlyRow, OldBootstrapHookOnlyDb>(rowData, dataSource), IViewModel<OldBootstrapHookOnlyDb>
{
    [Column("id")]
    public abstract int Id { get; }
}

public partial class ImmutableOldBootstrapHookOnlyRow(IRowData rowData, IDataSourceAccess dataSource)
    : OldBootstrapHookOnlyRow(rowData, dataSource)
{
    public static IImmutableInstance NewDataLinqImmutableInstance(IRowData rowData, IDataSourceAccess dataSource) =>
        new ImmutableOldBootstrapHookOnlyRow(rowData, dataSource);

    public override int Id => (int)GetValue(nameof(Id));
}
