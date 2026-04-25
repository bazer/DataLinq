using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;

namespace DataLinq.Tests.Unit.Core;

public class GeneratorFileFactoryTests
{
    [Test]
    public async Task CreateModelFiles_ImmutableModel_EmitsGeneratedFactoryHook()
    {
        var database = CreateDatabaseWithDefaultValue(
            propertyName: "Name",
            propertyType: new CsTypeDeclaration(typeof(string)),
            defaultValue: "generated");

        var generatedFile = new GeneratorFileFactory(new GeneratorFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "GeneratorModel.cs");

        await Assert.That(generatedFile.contents.Contains(
            "public static IImmutableInstance NewDataLinqImmutableInstance(IRowData rowData, IDataSourceAccess dataSource) => new ImmutableGeneratorModel(rowData, dataSource);"))
            .IsTrue();
    }

    [Test]
    public async Task CreateModelFiles_DefaultEnumValue_UsesEnumMember()
    {
        var database = CreateDatabaseWithDefaultValue(
            propertyName: "Status",
            propertyType: new CsTypeDeclaration("MyStatusEnum", "TestNamespace", ModelCsType.Enum),
            defaultValue: 2,
            enumProperty: new EnumProperty(
                enumValues: [("Active", 1), ("Inactive", 2)],
                csEnumValues: [("Active", 1), ("Inactive", 2)],
                declaredInClass: false));

        var generatedFile = new GeneratorFileFactory(new GeneratorFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "GeneratorModel.cs");

        await Assert.That(generatedFile.contents.Contains("this.Status = MyStatusEnum.Inactive;")).IsTrue();
        await Assert.That(generatedFile.contents.Contains("this.Status = 2;")).IsFalse();
    }

    [Test]
    public async Task CreateModelFiles_DefaultLongValue_UsesLongLiteral()
    {
        var database = CreateDatabaseWithDefaultValue(
            propertyName: "Count",
            propertyType: new CsTypeDeclaration(typeof(long)),
            defaultValue: 1);

        var generatedFile = new GeneratorFileFactory(new GeneratorFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "GeneratorModel.cs");

        await Assert.That(generatedFile.contents.Contains("this.Count = 1L;")).IsTrue();
        await Assert.That(generatedFile.contents.Contains("this.Count = 1;")).IsFalse();
    }

    private static DatabaseDefinition CreateDatabaseWithDefaultValue(
        string propertyName,
        CsTypeDeclaration propertyType,
        object defaultValue,
        EnumProperty? enumProperty = null)
    {
        var database = new DatabaseDefinition("GeneratorDb", new CsTypeDeclaration("GeneratorDb", "TestNamespace", ModelCsType.Class));
        var model = new ModelDefinition(new CsTypeDeclaration("GeneratorModel", "TestNamespace", ModelCsType.Class));
        var table = new TableDefinition("generator_table");
        var tableModel = new TableModel("GeneratorModels", database, model, table);

        var property = new ValueProperty(
            propertyName,
            propertyType,
            model,
            [new ColumnAttribute(propertyName.ToLowerInvariant()), new DefaultAttribute(defaultValue)]);

        if (enumProperty.HasValue)
            property.SetEnumProperty(enumProperty.Value);

        var column = new ColumnDefinition(propertyName.ToLowerInvariant(), table);
        column.AddDbType(new DatabaseColumnType(DatabaseType.MySQL, "int"));
        column.SetValueProperty(property);
        table.SetColumns([column]);
        model.AddProperty(property);

        database.SetTableModels([tableModel]);
        return database;
    }
}
