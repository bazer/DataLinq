using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class MetadataTransformerTests
{
    private static DatabaseDefinition CreateDestinationDatabase(string dbName = "MyDatabase", string tableDbName = "my_table", string modelName = "raw_table", bool includeStatusDefault = false)
    {
        var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
        var dbCsType = new CsTypeDeclaration(dbName, "RawNamespace", ModelCsType.Class);
        var modelCsType = new CsTypeDeclaration(modelName, "RawNamespace", ModelCsType.Class);
        var otherModelCsType = new CsTypeDeclaration("OtherRaw", "RawNamespace", ModelCsType.Class);
        var statusAttributes = includeStatusDefault
            ? new Attribute[] { new ColumnAttribute("status_db"), new DefaultAttribute(1) }
            : [new ColumnAttribute("status_db")];

        return Build(new MetadataDatabaseDraft(dbName, dbCsType)
        {
            TableModels =
            [
                CreateTable(
                    modelName + "s",
                    modelCsType,
                    tableDbName,
                    [
                        CreateValueProperty(
                            "col1_db",
                            typeof(int),
                            "col1_db",
                            primaryKey: true,
                            attributes:
                            [
                                new ColumnAttribute("col1_db"),
                                new PrimaryKeyAttribute(),
                                new ForeignKeyAttribute("other_table", "other_id", "FK_DestConstraint")
                            ]),
                        CreateValueProperty(
                            "col2_db",
                            typeof(int),
                            "col2_db",
                            attributes: [new ColumnAttribute("col2_db")]),
                        CreateValueProperty(
                            "status_db",
                            typeof(int),
                            "status_db",
                            attributes: statusAttributes)
                    ],
                    originalInterfaces: [iTableModel],
                    relationProperties:
                    [
                        CreateRelationProperty(
                            "rel_prop_db",
                            otherModelCsType,
                            [new RelationAttribute("other_table", "other_id", "FK_DestConstraint")])
                    ]),
                CreateTable(
                    "OtherRaws",
                    otherModelCsType,
                    "other_table",
                    [
                        CreateValueProperty(
                            "other_id",
                            typeof(int),
                            "other_id",
                            primaryKey: true,
                            attributes: [new ColumnAttribute("other_id"), new PrimaryKeyAttribute()])
                    ])
            ]
        });
    }

    private static DatabaseDefinition CreateSourceDatabase(string dbName = "MyDatabase", string tableDbName = "my_table", string modelName = "MyModel", string interfaceName = "IMyModel", bool includeEnumDefault = false)
    {
        var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
        var dbCsType = new CsTypeDeclaration("MyDatabaseCsType", "SourceNamespace", ModelCsType.Class);
        var modelCsType = new CsTypeDeclaration(modelName, "SourceNamespace", ModelCsType.Class);
        var interfaceType = new CsTypeDeclaration(interfaceName, "SourceNamespace", ModelCsType.Interface);
        var enumType = new CsTypeDeclaration("MyStatusEnum", "SourceNamespace", ModelCsType.Enum);
        var statusAttributes = includeEnumDefault
            ? new Attribute[]
            {
                new ColumnAttribute("status_db"),
                new EnumAttribute("Active", "Inactive"),
                new DefaultAttribute("MyStatusEnum.Inactive").SetCodeExpression("MyStatusEnum.Inactive")
            }
            : [new ColumnAttribute("status_db"), new EnumAttribute("Active", "Inactive")];
        var statusEnum = new EnumProperty(
            enumValues: [("Active", 1), ("Inactive", 2)],
            csEnumValues: [("Active", 1), ("Inactive", 2)],
            declaredInClass: false);
        var otherModelCsType = new CsTypeDeclaration("MyOtherModel", "SourceNamespace", ModelCsType.Class);

        return Build(new MetadataDatabaseDraft(dbName, dbCsType)
        {
            Attributes = [new DatabaseAttribute(dbName)],
            TableModels =
            [
                CreateTable(
                    "MyModels",
                    modelCsType,
                    tableDbName,
                    [
                        CreateValueProperty(
                            "Id",
                            typeof(int),
                            "col1_db",
                            primaryKey: true,
                            attributes:
                            [
                                new ColumnAttribute("col1_db"),
                                new PrimaryKeyAttribute(),
                                new ForeignKeyAttribute("other_table", "other_id", "FK_FROM_SOURCE")
                            ]),
                        CreateValueProperty(
                            "Name",
                            typeof(string),
                            "col2_db",
                            csNullable: true,
                            attributes: [new ColumnAttribute("col2_db")]),
                        CreateValueProperty(
                            "Status",
                            enumType,
                            "status_db",
                            attributes: statusAttributes,
                            enumProperty: statusEnum)
                    ],
                    attributes: [new TableAttribute(tableDbName), new InterfaceAttribute(interfaceName)],
                    originalInterfaces: [iTableModel],
                    modelInstanceInterface: interfaceType,
                    relationProperties:
                    [
                        CreateRelationProperty(
                            "RelatedItems",
                            otherModelCsType,
                            [new RelationAttribute("other_table", "other_id", "FK_FROM_SOURCE")])
                    ]),
                CreateTable(
                    "MyOtherModels",
                    otherModelCsType,
                    "other_table",
                    [
                        CreateValueProperty(
                            "OtherId",
                            typeof(int),
                            "other_id",
                            primaryKey: true,
                            attributes: [new ColumnAttribute("other_id"), new PrimaryKeyAttribute()])
                    ])
            ]
        });
    }

    private static MetadataTableModelDraft CreateTable(
        string tablePropertyName,
        CsTypeDeclaration modelCsType,
        string tableName,
        MetadataValuePropertyDraft[] valueProperties,
        Attribute[]? attributes = null,
        CsTypeDeclaration[]? originalInterfaces = null,
        CsTypeDeclaration? modelInstanceInterface = null,
        MetadataRelationPropertyDraft[]? relationProperties = null)
    {
        return new MetadataTableModelDraft(
            tablePropertyName,
            new MetadataModelDraft(modelCsType)
            {
                Attributes = attributes ?? [],
                OriginalInterfaces = originalInterfaces ?? [],
                ModelInstanceInterface = modelInstanceInterface,
                ValueProperties = valueProperties,
                RelationProperties = relationProperties ?? []
            },
            new MetadataTableDraft(tableName));
    }

    private static MetadataValuePropertyDraft CreateValueProperty(
        string propertyName,
        Type csType,
        string columnName,
        bool primaryKey = false,
        bool csNullable = false,
        Attribute[]? attributes = null)
    {
        return CreateValueProperty(
            propertyName,
            new CsTypeDeclaration(csType),
            columnName,
            primaryKey,
            csNullable,
            attributes);
    }

    private static MetadataValuePropertyDraft CreateValueProperty(
        string propertyName,
        CsTypeDeclaration csType,
        string columnName,
        bool primaryKey = false,
        bool csNullable = false,
        Attribute[]? attributes = null,
        EnumProperty? enumProperty = null)
    {
        var propertyAttributes = attributes ?? [];

        return new MetadataValuePropertyDraft(
            propertyName,
            csType,
            new MetadataColumnDraft(columnName)
            {
                PrimaryKey = primaryKey,
                ForeignKey = propertyAttributes.Any(static x => x is ForeignKeyAttribute)
            })
        {
            Attributes = propertyAttributes,
            CsNullable = csNullable,
            EnumProperty = enumProperty
        };
    }

    private static MetadataRelationPropertyDraft CreateRelationProperty(
        string propertyName,
        CsTypeDeclaration csType,
        Attribute[] attributes)
    {
        return new MetadataRelationPropertyDraft(propertyName, csType)
        {
            Attributes = attributes
        };
    }

    private static DatabaseDefinition Build(MetadataDatabaseDraft draft)
    {
        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    [Test]
    public async Task TransformDatabaseSnapshot_AppliesNamesAndAttributes()
    {
        var sourceDatabase = CreateSourceDatabase(modelName: "SourceModel");
        var destinationDatabase = CreateDestinationDatabase();
        var transformer = new MetadataTransformer(new MetadataTransformerOptions());

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        await Assert.That(destinationDatabase.DbName).IsEqualTo("MyDatabase");
        await Assert.That(destinationDatabase.CsType.Name).IsEqualTo("MyDatabaseCsType");
        await Assert.That(destinationDatabase.CsType.Namespace).IsEqualTo("SourceNamespace");
        await Assert.That(destinationDatabase.TableModels.Length).IsEqualTo(2);

        var destinationTableModel = destinationDatabase.TableModels.First(tm => tm.Table.DbName == "my_table");
        await Assert.That(destinationTableModel.CsPropertyName).IsEqualTo("MyModels");

        var destinationModel = destinationTableModel.Model;
        await Assert.That(destinationModel.CsType.Name).IsEqualTo("SourceModel");
        await Assert.That(destinationModel.CsType.Namespace).IsEqualTo("SourceNamespace");
        await Assert.That(destinationModel.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(destinationModel.ModelInstanceInterface!.Value.Name).IsEqualTo("IMyModel");

        await Assert.That(destinationTableModel.Table.DbName).IsEqualTo("my_table");

        await Assert.That(destinationModel.ValueProperties.ContainsKey("Id")).IsTrue();
        await Assert.That(destinationModel.ValueProperties.ContainsKey("col1_db")).IsFalse();
        await Assert.That(destinationModel.ValueProperties["Id"].PropertyName).IsEqualTo("Id");
        await Assert.That(destinationModel.ValueProperties["Id"].Column.DbName).IsEqualTo("col1_db");

        await Assert.That(destinationModel.ValueProperties.ContainsKey("Name")).IsTrue();
        await Assert.That(destinationModel.ValueProperties["Name"].PropertyName).IsEqualTo("Name");
        await Assert.That(destinationModel.ValueProperties["Name"].Column.DbName).IsEqualTo("col2_db");

        await Assert.That(destinationModel.RelationProperties.ContainsKey("RelatedItems")).IsTrue();
        await Assert.That(destinationModel.RelationProperties.ContainsKey("rel_prop_db")).IsFalse();
        await Assert.That(destinationModel.RelationProperties["RelatedItems"].PropertyName).IsEqualTo("RelatedItems");
    }

    [Test]
    public async Task TransformDatabaseSnapshot_AppliesNamesWithoutMutatingDestination()
    {
        var sourceDatabase = CreateSourceDatabase(modelName: "SourceModel");
        var destinationDatabase = CreateDestinationDatabase();
        var transformer = new MetadataTransformer(new MetadataTransformerOptions());

        var transformedDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        await Assert.That(ReferenceEquals(transformedDatabase, destinationDatabase)).IsFalse();
        await Assert.That(destinationDatabase.CsType.Namespace).IsEqualTo("RawNamespace");
        await Assert.That(destinationDatabase.TableModels[0].Model.CsType.Name).IsEqualTo("raw_table");
        await Assert.That(destinationDatabase.TableModels[0].Model.ValueProperties.ContainsKey("col1_db")).IsTrue();
        await Assert.That(destinationDatabase.TableModels[0].Model.ValueProperties.ContainsKey("Id")).IsFalse();
        await Assert.That(destinationDatabase.TableModels[0].Model.RelationProperties.ContainsKey("rel_prop_db")).IsTrue();
        await Assert.That(destinationDatabase.TableModels[0].Model.RelationProperties.ContainsKey("RelatedItems")).IsFalse();

        var transformedTableModel = transformedDatabase.TableModels.First(tm => tm.Table.DbName == "my_table");
        await Assert.That(ReferenceEquals(transformedTableModel, destinationDatabase.TableModels[0])).IsFalse();
        await Assert.That(transformedDatabase.CsType.Namespace).IsEqualTo("SourceNamespace");
        await Assert.That(transformedTableModel.Model.CsType.Name).IsEqualTo("SourceModel");
        await Assert.That(transformedTableModel.Model.ValueProperties.ContainsKey("Id")).IsTrue();
        await Assert.That(transformedTableModel.Model.ValueProperties["Id"].Column.Table).IsSameReferenceAs(transformedTableModel.Table);
        await Assert.That(transformedTableModel.Model.RelationProperties.ContainsKey("RelatedItems")).IsTrue();
        await Assert.That(transformedTableModel.Model.RelationProperties["RelatedItems"].RelationPart).IsNotNull();
    }

    [Test]
    public async Task TransformDatabase_RemoveInterfacePrefix_Enabled()
    {
        var sourceDatabase = CreateSourceDatabase(modelName: "SrcModel", interfaceName: "ISrcModel");
        var destinationDatabase = CreateDestinationDatabase(modelName: "dest_model");
        var transformer = new MetadataTransformer(new MetadataTransformerOptions(removeInterfacePrefix: true));

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        await Assert.That(destinationDatabase.TableModels[0].Model.CsType.Name).IsEqualTo("SrcModel");
    }

    [Test]
    public async Task TransformDatabase_RemoveInterfacePrefix_Disabled()
    {
        var sourceDatabase = CreateSourceDatabase(modelName: "ISrcModelAsClass", interfaceName: "ISrcModel");
        var destinationDatabase = CreateDestinationDatabase(modelName: "dest_model");
        var transformer = new MetadataTransformer(new MetadataTransformerOptions(removeInterfacePrefix: false));

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        await Assert.That(destinationDatabase.TableModels[0].Model.CsType.Name).IsEqualTo("ISrcModelAsClass");
    }

    [Test]
    public async Task OverwriteTypes_False_PreservesSourceType()
    {
        var sourceDatabase = CreateSourceDatabase();
        var destinationDatabase = CreateDestinationDatabase();
        var transformer = new MetadataTransformer(new MetadataTransformerOptions { OverwritePropertyTypes = false });

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        var transformedProperty = destinationDatabase.TableModels[0].Model.ValueProperties["Name"];

        await Assert.That(transformedProperty.PropertyName).IsEqualTo("Name");
        await Assert.That(transformedProperty.CsType.Name).IsEqualTo("string");
        await Assert.That(transformedProperty.CsNullable).IsTrue();
        await Assert.That(transformedProperty.Column.DbName).IsEqualTo("col2_db");
    }

    [Test]
    public async Task OverwriteTypes_True_AppliesDatabaseTypeAndNullability()
    {
        var sourceDatabase = CreateSourceDatabase();
        var destinationDatabase = CreateDestinationDatabase();
        var transformer = new MetadataTransformer(new MetadataTransformerOptions { OverwritePropertyTypes = true });

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        var transformedProperty = destinationDatabase.TableModels[0].Model.ValueProperties["Name"];

        await Assert.That(transformedProperty.PropertyName).IsEqualTo("Name");
        await Assert.That(transformedProperty.CsType.Name).IsEqualTo("int");
        await Assert.That(transformedProperty.CsNullable).IsFalse();
        await Assert.That(transformedProperty.Column.DbName).IsEqualTo("col2_db");
    }

    [Test]
    public async Task OverwriteTypes_True_PreservesEnumType()
    {
        var sourceDatabase = CreateSourceDatabase();
        var destinationDatabase = CreateDestinationDatabase();
        var transformer = new MetadataTransformer(new MetadataTransformerOptions { OverwritePropertyTypes = true });

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        var transformedProperty = destinationDatabase.TableModels[0].Model.ValueProperties["Status"];

        await Assert.That(transformedProperty.PropertyName).IsEqualTo("Status");
        await Assert.That(transformedProperty.CsType.Name).IsEqualTo("MyStatusEnum");
        await Assert.That(transformedProperty.EnumProperty.HasValue).IsTrue();
        await Assert.That(transformedProperty.Column.DbName).IsEqualTo("status_db");
    }

    [Test]
    public async Task OverwriteTypes_True_PreservesSourceEnumDefaultExpression()
    {
        var sourceDatabase = CreateSourceDatabase(includeEnumDefault: true);
        var destinationDatabase = CreateDestinationDatabase(includeStatusDefault: true);
        var transformer = new MetadataTransformer(new MetadataTransformerOptions { OverwritePropertyTypes = true });

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(destinationDatabase)
            .Single(file => file.path == "MyModel.cs");

        await Assert.That(generatedFile.contents).Contains("[Default(MyStatusEnum.Inactive)]");
        await Assert.That(generatedFile.contents).DoesNotContain("[Default(1)]");
    }

    [Test]
    public async Task UpdateConstraintNames_True_AppliesSourceConstraintName()
    {
        var sourceDatabase = CreateSourceDatabase();
        var destinationDatabase = CreateDestinationDatabase();
        var transformer = new MetadataTransformer(new MetadataTransformerOptions { UpdateConstraintNames = true });

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        var relationProperty = destinationDatabase.TableModels[0].Model.RelationProperties["RelatedItems"];

        await Assert.That(relationProperty.PropertyName).IsEqualTo("RelatedItems");
        await Assert.That(relationProperty.RelationPart).IsNotNull();
        await Assert.That(relationProperty.RelationPart!.Relation.ConstraintName).IsEqualTo("FK_FROM_SOURCE");
    }

    [Test]
    public async Task UpdateConstraintNames_False_PreservesDestinationConstraintName()
    {
        var sourceDatabase = CreateSourceDatabase();
        var destinationDatabase = CreateDestinationDatabase();
        var transformer = new MetadataTransformer(new MetadataTransformerOptions { UpdateConstraintNames = false });

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        var relationProperty = destinationDatabase.TableModels[0].Model.RelationProperties["RelatedItems"];

        await Assert.That(relationProperty.PropertyName).IsEqualTo("RelatedItems");
        await Assert.That(relationProperty.RelationPart).IsNotNull();
        await Assert.That(relationProperty.RelationPart!.Relation.ConstraintName).IsEqualTo("FK_DestConstraint");
    }
}
