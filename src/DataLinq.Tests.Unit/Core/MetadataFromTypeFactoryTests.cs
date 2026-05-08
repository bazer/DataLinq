using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using DataLinq.Tests.Models.Allround;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
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
    public async Task ParseDatabase_GeneratedMetadataHook_ReplacesRuntimeMetadataReflection()
    {
        var databaseDefinition = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(BootstrapHookDb)).ValueOrException();

        await Assert.That(databaseDefinition.TableModels.Length).IsEqualTo(1);

        var tableModel = databaseDefinition.TableModels.Single();
        await Assert.That(tableModel.CsPropertyName).IsEqualTo("Rows");
        await Assert.That(tableModel.Model.CsType.Type).IsEqualTo(typeof(BootstrapHookRow));
        await Assert.That(tableModel.Table.DbName).IsEqualTo("bootstrap_rows");
        await Assert.That(((ViewDefinition)tableModel.Table).Definition).IsEqualTo("select id from bootstrap_rows");
    }

    [Test]
    public async Task ParseDatabase_ModelWithAdditionalApplicationInterface_SelectsGeneratedModelInstanceInterface()
    {
        var databaseDefinition = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(RuntimeInterfaceSelectionDb)).ValueOrException();
        var model = databaseDefinition.TableModels.Single().Model;

        await Assert.That(model.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(model.ModelInstanceInterface!.Value.Name).IsEqualTo("IRuntimeInterfaceSelectionRow");
        await Assert.That(model.OriginalInterfaces.Any(x => x.Name == nameof(IOrdinaryRuntimeInterface))).IsTrue();
    }

    [Test]
    public async Task ParseModel_NullableReferenceRelation_PreservesNullableAnnotation()
    {
        var databaseDefinition = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(AllroundBenchmark)).ValueOrException();
        var userProfileModel = databaseDefinition.TableModels.Single(tm => tm.Model.CsType.Type == typeof(Userprofile)).Model;

        await Assert.That(userProfileModel.RelationProperties["users"].CsNullable).IsTrue();
        await Assert.That(userProfileModel.RelationProperties["usercontacts"].CsNullable).IsFalse();
    }

    [Test]
    public async Task ParseDatabase_OldGeneratedTableBootstrapOnly_ReturnsMissingGeneratedMetadataHookFailure()
    {
        var result = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(typeof(OldBootstrapHookOnlyDb));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(result.Failure.ToString()!).Contains(typeof(OldBootstrapHookOnlyDb).FullName!);
        await Assert.That(result.Failure.ToString()!).Contains("GetDataLinqGeneratedMetadata");
        await Assert.That(result.Failure.ToString()!).Contains("missing the generated complete DataLinq metadata hook");
        await Assert.That(result.Failure.ToString()!).Contains("Regenerate");
    }

    [Test]
    public Task ParseDatabase_GeneratedMetadataHookWrongReturnType_ReturnsInvalidModelFailure() =>
        AssertGeneratedMetadataFailure(
            typeof(WrongGeneratedModelHookReturnTypeDb),
            typeof(WrongGeneratedModelHookReturnTypeDb).FullName!,
            "GetDataLinqGeneratedMetadata",
            "must return",
            typeof(MetadataDatabaseDraft).FullName!,
            "Regenerate");

    [Test]
    public Task ParseDatabase_NullGeneratedMetadataPayload_ReturnsInvalidModelFailure() =>
        AssertGeneratedMetadataFailure(
            typeof(NullGeneratedMetadataPayloadDb),
            typeof(NullGeneratedMetadataPayloadDb).FullName!,
            "GetDataLinqGeneratedMetadata",
            "null metadata payload",
            "Regenerate");

    [Test]
    public Task ParseDatabase_UnreadableGeneratedMetadataPayload_ReturnsInvalidModelFailure() =>
        AssertGeneratedMetadataFailure(
            typeof(UnreadableGeneratedMetadataPayloadDb),
            typeof(UnreadableGeneratedMetadataPayloadDb).FullName!,
            "GetDataLinqGeneratedMetadata",
            "could not be read",
            "InvalidOperationException",
            "simulated generated metadata failure",
            "Regenerate");

    [Test]
    public Task ParseDatabase_NullGeneratedTableModel_ReturnsInvalidModelFailure() =>
        AssertGeneratedMetadataFailure(
            typeof(NullGeneratedTableModelDb),
            typeof(NullGeneratedTableModelDb).FullName!,
            "GetDataLinqGeneratedMetadata",
            "invalid metadata",
            "contains a null table model draft",
            "Regenerate");

    [Test]
    public Task ParseDatabase_GeneratedMetadataFactoryValidationFailure_ReturnsInvalidModelFailure() =>
        AssertGeneratedMetadataFailure(
            typeof(FactoryValidationFailureDb),
            typeof(FactoryValidationFailureDb).FullName!,
            "GetDataLinqGeneratedMetadata",
            "invalid metadata",
            "invalid_rows",
            "missing a primary key",
            "Regenerate");

    [Test]
    public async Task GeneratedStartupPath_DoesNotUseRuntimeModelReflectionDiscovery()
    {
        var repositoryRoot = RepositoryLayout.FindRepositoryRoot();
        var factoryPath = Path.Combine(repositoryRoot, "src", "DataLinq", "Metadata", "MetadataFromTypeFactory.cs");
        var factorySource = File.ReadAllText(factoryPath);

        await Assert.That(factorySource).DoesNotContain("GetCustomAttributes");
        await Assert.That(factorySource).DoesNotContain("GetProperties");
        await Assert.That(factorySource).DoesNotContain("GetInterfaces");
        await Assert.That(factorySource).DoesNotContain("NullabilityInfoContext");
    }

    [Test]
    public async Task GeneratedDatabaseModelDeclaration_TableModels_ReturnsDefensiveCopy()
    {
        var tableModel = new GeneratedTableModelDeclaration(
            "Rows",
            typeof(GeneratedDeclarationValidationRow),
            typeof(ImmutableGeneratedDeclarationValidationRow),
            null,
            new Func<IRowData, IDataSourceAccess, IImmutableInstance>((_, _) => null!),
            TableType.View);
        var source = new[] { tableModel };

        var declaration = new GeneratedDatabaseModelDeclaration(source);
        source[0] = default;

        var returned = declaration.TableModels;
        returned[0] = default;

        await Assert.That(declaration.TableModels.Single().CsPropertyName).IsEqualTo("Rows");
        await Assert.That(declaration.TryValidate(typeof(BootstrapHookDb)).HasValue).IsTrue();
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

    private static async Task AssertGeneratedMetadataFailure(Type databaseType, params string[] expectedMessageFragments)
    {
        var result = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel(databaseType);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);

        var failureMessage = result.Failure.ToString()!;
        foreach (var fragment in expectedMessageFragments)
            await Assert.That(failureMessage).Contains(fragment);
    }
}

public sealed class BootstrapHookDb : IDatabaseModel, IDataLinqGeneratedDatabaseModel<BootstrapHookDb>
{
    public static BootstrapHookDb NewDataLinqDatabase(IDataSourceAccess dataSource) => new();

    public static MetadataDatabaseDraft GetDataLinqGeneratedMetadata() =>
        GeneratedMetadataTestDrafts.CreateBootstrapDraft();

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

public sealed class RuntimeInterfaceSelectionDb : IDatabaseModel, IDataLinqGeneratedDatabaseModel<RuntimeInterfaceSelectionDb>
{
    public static RuntimeInterfaceSelectionDb NewDataLinqDatabase(IDataSourceAccess dataSource) => new();

    public static MetadataDatabaseDraft GetDataLinqGeneratedMetadata() =>
        GeneratedMetadataTestDrafts.CreateRuntimeInterfaceSelectionDraft();

    public static GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() =>
        new(
        [
            new(
                "Rows",
                typeof(RuntimeInterfaceSelectionRow),
                typeof(ImmutableRuntimeInterfaceSelectionRow),
                null,
                new Func<IRowData, IDataSourceAccess, IImmutableInstance>(ImmutableRuntimeInterfaceSelectionRow.NewDataLinqImmutableInstance),
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

public sealed class WrongGeneratedModelHookReturnTypeDb : IDatabaseModel
{
    public static GeneratedTableModelDeclaration[] GetDataLinqGeneratedMetadata() => [];
}

public sealed class NullGeneratedMetadataPayloadDb : IDatabaseModel
{
    public static MetadataDatabaseDraft GetDataLinqGeneratedMetadata() => null!;
}

public sealed class UnreadableGeneratedMetadataPayloadDb : IDatabaseModel
{
    public static MetadataDatabaseDraft GetDataLinqGeneratedMetadata() =>
        throw new InvalidOperationException("simulated generated metadata failure");
}

public sealed class NullGeneratedTableModelDb : IDatabaseModel
{
    public static MetadataDatabaseDraft GetDataLinqGeneratedMetadata() =>
        new("NullGeneratedTableModelDb", new CsTypeDeclaration(typeof(NullGeneratedTableModelDb)))
        {
            TableModels = [null!]
        };
}

public sealed class FactoryValidationFailureDb : IDatabaseModel
{
    public static MetadataDatabaseDraft GetDataLinqGeneratedMetadata() =>
        GeneratedMetadataTestDrafts.CreateFactoryValidationFailureDraft();
}

public sealed class DefaultGeneratedDeclarationDb : IDatabaseModel
{
    public static GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() => default;
}

public sealed class NullTableModelsGeneratedDeclarationDb : IDatabaseModel
{
    public static GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() =>
        new((GeneratedTableModelDeclaration[])null!);
}

public sealed class MissingImmutableTypeDb : IDatabaseModel
{
    public static GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() =>
        new(
        [
            new(
                "Rows",
                typeof(GeneratedDeclarationValidationRow),
                null!,
                null,
                new Func<IRowData, IDataSourceAccess, IImmutableInstance>((_, _) => null!),
                TableType.View)
        ]);
}

public sealed class MissingMutableTableTypeDb : IDatabaseModel
{
    public static GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() =>
        new(
        [
            new(
                "Rows",
                typeof(GeneratedDeclarationValidationRow),
                typeof(ImmutableGeneratedDeclarationValidationRow),
                null,
                new Func<IRowData, IDataSourceAccess, IImmutableInstance>((_, _) => null!),
                TableType.Table)
        ]);
}

public sealed class MissingImmutableFactoryDb : IDatabaseModel
{
    public static GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() =>
        new(
        [
            new(
                "Rows",
                typeof(GeneratedDeclarationValidationRow),
                typeof(ImmutableGeneratedDeclarationValidationRow),
                null,
                null!,
                TableType.View)
        ]);
}

public sealed class WrongImmutableFactoryShapeDb : IDatabaseModel
{
    public static GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() =>
        new(
        [
            new(
                "Rows",
                typeof(GeneratedDeclarationValidationRow),
                typeof(ImmutableGeneratedDeclarationValidationRow),
                null,
                new Func<IRowData, IImmutableInstance>(_ => null!),
                TableType.View)
        ]);
}

public sealed class GeneratedDeclarationValidationRow
{
}

public sealed class ImmutableGeneratedDeclarationValidationRow
{
}

public sealed class MutableGeneratedDeclarationValidationRow
{
}

internal static class GeneratedMetadataTestDrafts
{
    public static MetadataDatabaseDraft CreateBootstrapDraft() =>
        new("BootstrapHookDb", new CsTypeDeclaration(typeof(BootstrapHookDb)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(BootstrapHookRow)))
                    {
                        ImmutableType = new CsTypeDeclaration(typeof(ImmutableBootstrapHookRow)),
                        ImmutableFactory = new Func<IRowData, IDataSourceAccess, IImmutableInstance>(ImmutableBootstrapHookRow.NewDataLinqImmutableInstance),
                        OriginalInterfaces =
                        [
                            new CsTypeDeclaration("IViewModel<BootstrapHookDb>", "DataLinq.Interfaces", ModelCsType.Interface)
                        ],
                        Attributes =
                        [
                            new DefinitionAttribute("select id from bootstrap_rows"),
                            new ViewAttribute("bootstrap_rows")
                        ],
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id"))
                            {
                                Attributes = [new ColumnAttribute("id")],
                                CsSize = sizeof(int)
                            }
                        ]
                    },
                    new MetadataTableDraft("bootstrap_rows")
                    {
                        Type = TableType.View,
                        Definition = "select id from bootstrap_rows"
                    })
            ]
        };

    public static MetadataDatabaseDraft CreateRuntimeInterfaceSelectionDraft() =>
        new("RuntimeInterfaceSelectionDb", new CsTypeDeclaration(typeof(RuntimeInterfaceSelectionDb)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(RuntimeInterfaceSelectionRow)))
                    {
                        ImmutableType = new CsTypeDeclaration(typeof(ImmutableRuntimeInterfaceSelectionRow)),
                        ImmutableFactory = new Func<IRowData, IDataSourceAccess, IImmutableInstance>(ImmutableRuntimeInterfaceSelectionRow.NewDataLinqImmutableInstance),
                        ModelInstanceInterface = new CsTypeDeclaration(nameof(IRuntimeInterfaceSelectionRow), typeof(IRuntimeInterfaceSelectionRow).Namespace!, ModelCsType.Interface),
                        OriginalInterfaces =
                        [
                            new CsTypeDeclaration(nameof(IOrdinaryRuntimeInterface), typeof(IOrdinaryRuntimeInterface).Namespace!, ModelCsType.Interface),
                            new CsTypeDeclaration("IViewModel<RuntimeInterfaceSelectionDb>", "DataLinq.Interfaces", ModelCsType.Interface)
                        ],
                        Attributes =
                        [
                            new DefinitionAttribute("select id from runtime_interface_rows"),
                            new ViewAttribute("runtime_interface_rows"),
                            new InterfaceAttribute(nameof(IRuntimeInterfaceSelectionRow))
                        ],
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id"))
                            {
                                Attributes = [new ColumnAttribute("id")],
                                CsSize = sizeof(int)
                            }
                        ]
                    },
                    new MetadataTableDraft("runtime_interface_rows")
                    {
                        Type = TableType.View,
                        Definition = "select id from runtime_interface_rows"
                    })
            ]
        };

    public static MetadataDatabaseDraft CreateFactoryValidationFailureDraft() =>
        new("FactoryValidationFailureDb", new CsTypeDeclaration(typeof(FactoryValidationFailureDb)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration("InvalidRow", typeof(FactoryValidationFailureDb).Namespace!, ModelCsType.Class))
                    {
                        OriginalInterfaces =
                        [
                            new CsTypeDeclaration("ITableModel<FactoryValidationFailureDb>", "DataLinq.Interfaces", ModelCsType.Interface)
                        ],
                        Attributes = [new TableAttribute("invalid_rows")],
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id"))
                            {
                                Attributes = [new ColumnAttribute("id")],
                                CsSize = sizeof(int)
                            }
                        ]
                    },
                    new MetadataTableDraft("invalid_rows")
                    {
                        Type = TableType.Table
                    })
            ]
        };
}

[Definition("select id from bootstrap_rows")]
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

public interface IOrdinaryRuntimeInterface
{
}

public partial interface IRuntimeInterfaceSelectionRow : IModelInstance<RuntimeInterfaceSelectionDb>
{
}

[Definition("select id from runtime_interface_rows")]
[View("runtime_interface_rows")]
[Interface<IRuntimeInterfaceSelectionRow>]
public abstract partial class RuntimeInterfaceSelectionRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<RuntimeInterfaceSelectionRow, RuntimeInterfaceSelectionDb>(rowData, dataSource),
    IOrdinaryRuntimeInterface,
    IRuntimeInterfaceSelectionRow,
    IViewModel<RuntimeInterfaceSelectionDb>
{
    [Column("id")]
    public abstract int Id { get; }
}

public partial class ImmutableRuntimeInterfaceSelectionRow(IRowData rowData, IDataSourceAccess dataSource)
    : RuntimeInterfaceSelectionRow(rowData, dataSource)
{
    public static IImmutableInstance NewDataLinqImmutableInstance(IRowData rowData, IDataSourceAccess dataSource) =>
        new ImmutableRuntimeInterfaceSelectionRow(rowData, dataSource);

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
