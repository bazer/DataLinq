using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class GeneratorFileFactoryTests
{
    [Test]
    public async Task CreateModelFiles_DatabaseModel_EmitsOnlyGeneratedDatabaseMetadataBootstrapHook()
    {
        var database = CreateDatabaseWithDefaultValue(
            propertyName: "Name",
            propertyType: new CsTypeDeclaration(typeof(string)),
            defaultValue: "generated");

        var generatedFile = new GeneratorFileFactory(new GeneratorFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "GeneratorDb.DataLinqMetadata.cs");

        await Assert.That(generatedFile.contents.Contains(
            "public partial class GeneratorDb : global::DataLinq.Interfaces.IDataLinqGeneratedDatabaseModel<GeneratorDb>"))
            .IsTrue();
        await Assert.That(generatedFile.contents.Contains(
            "public static global::DataLinq.Metadata.GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() =>"))
            .IsTrue();
        await Assert.That(generatedFile.contents.Contains(
            "new(\"GeneratorModels\", typeof(global::TestNamespace.GeneratorModel), typeof(global::TestNamespace.ImmutableGeneratorModel), typeof(global::TestNamespace.MutableGeneratorModel), new global::System.Func<global::DataLinq.Instances.IRowData, global::DataLinq.Interfaces.IDataSourceAccess, global::DataLinq.Instances.IImmutableInstance>(global::TestNamespace.ImmutableGeneratorModel.NewDataLinqImmutableInstance), global::DataLinq.Metadata.TableType.Table),"))
            .IsTrue();
        await Assert.That(generatedFile.contents.Contains(
            "public static global::DataLinq.Metadata.GeneratedTableModelDeclaration[] GetDataLinqGeneratedTableModels() =>"))
            .IsFalse();
    }

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

    [Test]
    public async Task CreateModelFiles_Comments_EmitXmlDocsInGeneratedTypes()
    {
        var database = CreateDatabaseWithDefaultValue(
            propertyName: "Name",
            propertyType: new CsTypeDeclaration(typeof(string)),
            defaultValue: "generated",
            includeComments: true);

        var generatedFile = new GeneratorFileFactory(new GeneratorFileFactoryOptions())
            .CreateModelFiles(database)
            .Single(file => file.path == "GeneratorModel.cs");

        await Assert.That(generatedFile.contents).Contains("/// Generated model &amp; docs");
        await Assert.That(generatedFile.contents).Contains("/// Generated property &lt;name&gt;");
    }

    private static DatabaseDefinition CreateDatabaseWithDefaultValue(
        string propertyName,
        CsTypeDeclaration propertyType,
        object defaultValue,
        EnumProperty? enumProperty = null,
        bool includeComments = false)
    {
        var propertyAttributes = new Attribute[] { new ColumnAttribute(propertyName.ToLowerInvariant()), new DefaultAttribute(defaultValue) };
        if (includeComments)
            propertyAttributes = [.. propertyAttributes, new CommentAttribute("Generated property <name>")];

        var modelAttributes = includeComments
            ? new Attribute[] { new CommentAttribute("Generated model & docs") }
            : [];

        var draft = new MetadataDatabaseDraft(
            "GeneratorDb",
            new CsTypeDeclaration("GeneratorDb", "TestNamespace", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "GeneratorModels",
                    new MetadataModelDraft(new CsTypeDeclaration("GeneratorModel", "TestNamespace", ModelCsType.Class))
                    {
                        Attributes = modelAttributes,
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                propertyName,
                                propertyType,
                                new MetadataColumnDraft(propertyName.ToLowerInvariant())
                                {
                                    PrimaryKey = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.MySQL, "int")]
                                })
                            {
                                Attributes = propertyAttributes,
                                EnumProperty = enumProperty
                            }
                        ]
                    },
                    new MetadataTableDraft("generator_table"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }
}
