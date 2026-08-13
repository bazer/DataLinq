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
    private static DatabaseDefinition CreateDestinationDatabase(string dbName = "MyDatabase", string tableDbName = "my_table", string modelName = "raw_table", bool includeStatusDefault = false, bool includeStatusEnum = false, bool nullableForeignKeyRelation = false)
    {
        var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
        var dbCsType = new CsTypeDeclaration(dbName, "RawNamespace", ModelCsType.Class);
        var modelCsType = new CsTypeDeclaration(modelName, "RawNamespace", ModelCsType.Class);
        var otherModelCsType = new CsTypeDeclaration("OtherRaw", "RawNamespace", ModelCsType.Class);
        var idAttributes = nullableForeignKeyRelation
            ? new Attribute[] { new ColumnAttribute("col1_db"), new PrimaryKeyAttribute() }
            : [new ColumnAttribute("col1_db"), new PrimaryKeyAttribute(), new ForeignKeyAttribute("other_table", "other_id", "FK_DestConstraint")];
        var relatedIdAttributes = nullableForeignKeyRelation
            ? new Attribute[] { new ColumnAttribute("col2_db"), new ForeignKeyAttribute("other_table", "other_id", "FK_DestConstraint") }
            : [new ColumnAttribute("col2_db")];
        var statusAttributes = includeStatusDefault
            ? new Attribute[] { new ColumnAttribute("status_db"), new DefaultAttribute(1) }
            : [new ColumnAttribute("status_db")];
        var statusType = includeStatusEnum
            ? new CsTypeDeclaration("StatusDb", "RawNamespace", ModelCsType.Enum)
            : new CsTypeDeclaration(typeof(int));
        var statusEnum = includeStatusEnum
            ? new EnumProperty(enumValues: [("Active", 1), ("Inactive", 2)])
            : (EnumProperty?)null;

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
                            attributes: idAttributes),
                        CreateValueProperty(
                            "col2_db",
                            typeof(int),
                            "col2_db",
                            csNullable: nullableForeignKeyRelation,
                            columnNullable: nullableForeignKeyRelation,
                            attributes: relatedIdAttributes),
                        CreateValueProperty(
                            "status_db",
                            statusType,
                            "status_db",
                            attributes: statusAttributes,
                            enumProperty: statusEnum,
                            dbTypes:
                            [
                                new DatabaseColumnType(
                                    DataLinq.DatabaseType.MariaDB,
                                    includeStatusEnum ? "enum" : "int")
                            ])
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

    private static DatabaseDefinition CreateSourceDatabase(string dbName = "MyDatabase", string tableDbName = "my_table", string modelName = "MyModel", string interfaceName = "IMyModel", bool includeEnumDefault = false, bool useExternalStatusEnum = false, bool includeUsings = false, string databaseCsTypeName = "MyDatabaseCsType", bool nullableForeignKeyRelation = false)
    {
        var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
        var dbCsType = new CsTypeDeclaration(databaseCsTypeName, "SourceNamespace", ModelCsType.Class);
        var modelCsType = new CsTypeDeclaration(modelName, "SourceNamespace", ModelCsType.Class);
        var interfaceType = new CsTypeDeclaration(interfaceName, "SourceNamespace", ModelCsType.Interface);
        var enumType = new CsTypeDeclaration("MyStatusEnum", "SourceNamespace", ModelCsType.Enum);
        var statusType = useExternalStatusEnum
            ? new CsTypeDeclaration("ExternalStatus", "ExternalNamespace", ModelCsType.Class)
            : enumType;
        var statusAttributes = includeEnumDefault
            ? new Attribute[]
            {
                new ColumnAttribute("status_db"),
                new EnumAttribute("Active", "Inactive"),
                new DefaultAttribute("MyStatusEnum.Inactive", "MyStatusEnum.Inactive")
            }
            : [new ColumnAttribute("status_db"), new EnumAttribute("Active", "Inactive")];
        var statusEnum = new EnumProperty(
            enumValues: [("Active", 1), ("Inactive", 2)],
            csEnumValues: useExternalStatusEnum ? [] : [("Active", 1), ("Inactive", 2)],
            declaredInClass: false,
            declaredInModelFile: !useExternalStatusEnum);
        var otherModelCsType = new CsTypeDeclaration("MyOtherModel", "SourceNamespace", ModelCsType.Class);
        var idAttributes = nullableForeignKeyRelation
            ? new Attribute[] { new ColumnAttribute("col1_db"), new PrimaryKeyAttribute() }
            : [new ColumnAttribute("col1_db"), new PrimaryKeyAttribute(), new ForeignKeyAttribute("other_table", "other_id", "FK_FROM_SOURCE")];
        var secondPropertyName = nullableForeignKeyRelation ? "OtherId" : "Name";
        var secondPropertyType = nullableForeignKeyRelation
            ? new CsTypeDeclaration(typeof(int))
            : new CsTypeDeclaration(typeof(string));
        var secondPropertyAttributes = nullableForeignKeyRelation
            ? new Attribute[] { new ColumnAttribute("col2_db"), new ForeignKeyAttribute("other_table", "other_id", "FK_FROM_SOURCE") }
            : [new ColumnAttribute("col2_db")];

        return Build(new MetadataDatabaseDraft(dbName, dbCsType)
        {
            Attributes = [new DatabaseAttribute(dbName)],
            Usings = includeUsings ? [new ModelUsing("Source.Database.Helpers")] : [],
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
                            attributes: idAttributes),
                        CreateValueProperty(
                            secondPropertyName,
                            secondPropertyType,
                            "col2_db",
                            csNullable: true,
                            columnNullable: nullableForeignKeyRelation,
                            attributes: secondPropertyAttributes),
                        CreateValueProperty(
                            "Status",
                            statusType,
                            "status_db",
                            attributes: statusAttributes,
                            enumProperty: statusEnum)
                    ],
                    attributes: [new TableAttribute(tableDbName), new InterfaceAttribute(interfaceName)],
                    usings: includeUsings ? [new ModelUsing("Source.Model.Helpers")] : null,
                    originalInterfaces: [iTableModel],
                    modelInstanceInterface: interfaceType,
                    relationProperties:
                    [
                        CreateRelationProperty(
                            "RelatedItems",
                            otherModelCsType,
                            [new RelationAttribute("other_table", "other_id", "FK_FROM_SOURCE")],
                            csNullable: nullableForeignKeyRelation)
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
        ModelUsing[]? usings = null,
        CsTypeDeclaration[]? originalInterfaces = null,
        CsTypeDeclaration? modelInstanceInterface = null,
        MetadataRelationPropertyDraft[]? relationProperties = null)
    {
        return new MetadataTableModelDraft(
            tablePropertyName,
            new MetadataModelDraft(modelCsType)
            {
                Attributes = attributes ?? [],
                Usings = usings ?? [],
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
        bool columnNullable = false,
        Attribute[]? attributes = null,
        EnumProperty? enumProperty = null,
        DatabaseColumnType[]? dbTypes = null)
    {
        return CreateValueProperty(
            propertyName,
            new CsTypeDeclaration(csType),
            columnName,
            primaryKey,
            csNullable,
            columnNullable,
            attributes,
            enumProperty,
            dbTypes);
    }

    private static MetadataValuePropertyDraft CreateValueProperty(
        string propertyName,
        CsTypeDeclaration csType,
        string columnName,
        bool primaryKey = false,
        bool csNullable = false,
        bool columnNullable = false,
        Attribute[]? attributes = null,
        EnumProperty? enumProperty = null,
        DatabaseColumnType[]? dbTypes = null)
    {
        var propertyAttributes = attributes ?? [];

        return new MetadataValuePropertyDraft(
            propertyName,
            csType,
            new MetadataColumnDraft(columnName)
            {
                DbTypes = dbTypes ?? [],
                PrimaryKey = primaryKey,
                ForeignKey = propertyAttributes.Any(static x => x is ForeignKeyAttribute),
                Nullable = columnNullable
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
        Attribute[] attributes,
        bool csNullable = false)
    {
        return new MetadataRelationPropertyDraft(propertyName, csType)
        {
            Attributes = attributes,
            CsNullable = csNullable
        };
    }

    private static DatabaseDefinition Build(MetadataDatabaseDraft draft)
    {
        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static DatabaseDefinition CreateGuidStorageTransformDatabase(bool source)
    {
        Attribute[] attributes = source
            ?
            [
                new ColumnAttribute("id"),
                new PrimaryKeyAttribute(),
                new GuidStorageAttribute(GuidStorageFormat.Text36),
                new GuidStorageAttribute(
                    DatabaseType.MySQL,
                    GuidStorageFormat.Binary16Rfc4122)
            ]
            :
            [
                new ColumnAttribute("id"),
                new PrimaryKeyAttribute(),
                new GuidStorageAttribute(
                    DatabaseType.MySQL,
                    GuidStorageFormat.Binary16LittleEndian),
                new GuidStorageAttribute(
                    DatabaseType.SQLite,
                    GuidStorageFormat.Text36)
            ];

        return Build(new MetadataDatabaseDraft(
            "GuidTransformDb",
            new CsTypeDeclaration(
                source ? "GuidTransformDb" : "guid_transform_db",
                source ? "SourceNamespace" : "RawNamespace",
                ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(
                        source ? "GuidTransformRow" : "guid_transform_row",
                        source ? "SourceNamespace" : "RawNamespace",
                        ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                source ? "Id" : "id",
                                new CsTypeDeclaration(typeof(Guid)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes =
                                    [
                                        new DatabaseColumnType(DatabaseType.MySQL, "binary", 16),
                                        new DatabaseColumnType(DatabaseType.SQLite, "TEXT")
                                    ]
                                })
                            {
                                Attributes = attributes
                            }
                        ]
                    },
                    new MetadataTableDraft("guid_transform_rows"))
            ]
        });
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
        await Assert.That(destinationModel.OriginalInterfaces.Select(x => x.Name).ToArray()).IsEquivalentTo(["ITableModel"]);
        await Assert.That(destinationModel.OriginalInterfaces.All(x => x.ModelCsType == ModelCsType.Interface)).IsTrue();

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
    public async Task TransformDatabaseSnapshot_GuidStorage_SourceWinsPerProviderAndPreservesOthers()
    {
        var sourceDatabase = CreateGuidStorageTransformDatabase(source: true);
        var destinationDatabase = CreateGuidStorageTransformDatabase(source: false);
        var transformer = new MetadataTransformer(new MetadataTransformerOptions());

        var transformed = transformer.TransformDatabaseSnapshot(
            sourceDatabase,
            destinationDatabase);
        var declarations = transformed.TableModels[0]
            .Model.ValueProperties["Id"]
            .Attributes
            .OfType<GuidStorageAttribute>()
            .OrderBy(x => x.DatabaseType)
            .ToArray();
        var transformedColumn = transformed.TableModels[0].Table.Columns.Single();

        await Assert.That(declarations.Length).IsEqualTo(3);
        await Assert.That(declarations[0].DatabaseType).IsEqualTo(DatabaseType.Default);
        await Assert.That(declarations[0].Format).IsEqualTo(GuidStorageFormat.Text36);
        await Assert.That(declarations[1].DatabaseType).IsEqualTo(DatabaseType.MySQL);
        await Assert.That(declarations[1].Format)
            .IsEqualTo(GuidStorageFormat.Binary16Rfc4122);
        await Assert.That(declarations[2].DatabaseType).IsEqualTo(DatabaseType.SQLite);
        await Assert.That(declarations[2].Format).IsEqualTo(GuidStorageFormat.Text36);
        await Assert.That(transformedColumn.GuidStorageDefinitions).IsEmpty();
        await Assert.That(transformed.IsFrozen).IsFalse();
    }

    [Test]
    public async Task TransformDatabaseSnapshot_UnresolvedGuidStorageMarker_SurvivesRegeneration()
    {
        var sourceDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: true));
        var sourceProperty = sourceDatabase.TableModels[0].Model.ValueProperties["Id"];
        sourceProperty.SetAttributesCore(
            sourceProperty.Attributes
                .Where(static x => x is not GuidStorageAttribute)
                .Append(new GuidStorageUnresolvedAttribute(DatabaseType.MySQL)));
        sourceProperty.Column.SetGuidStorageDefinitionsCore([]);
        sourceDatabase.Freeze();

        var destinationDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: false));
        destinationDatabase.TableModels[0].Table.Columns[0]
            .SetUnresolvedGuidStorageProvidersCore([DatabaseType.MySQL]);
        destinationDatabase.Freeze();

        var transformed = new MetadataTransformer(new MetadataTransformerOptions())
            .TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);
        var transformedProperty = transformed.TableModels[0].Model.ValueProperties["Id"];
        var transformedColumn = transformedProperty.Column;
        var modelSource = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(transformed)
            .Single(file => file.path == "GuidTransformRow.cs")
            .contents;

        await Assert.That(transformedProperty.Attributes
            .OfType<GuidStorageUnresolvedAttribute>()
            .Select(static x => x.DatabaseType)
            .ToArray()).IsEquivalentTo([DatabaseType.MySQL]);
        await Assert.That(transformedColumn.UnresolvedGuidStorageProviders.ToArray())
            .IsEquivalentTo([DatabaseType.MySQL]);
        await Assert.That(transformedProperty.Attributes
            .OfType<GuidStorageAttribute>()
            .Any(x =>
                x.DatabaseType == DatabaseType.Default ||
                x.DatabaseType == DatabaseType.MySQL)).IsFalse();
        await Assert.That(modelSource).Contains("#error DATALINQ_UUID_STORAGE_UNRESOLVED");
        await Assert.That(modelSource).Contains("[GuidStorageUnresolved(DatabaseType.MySQL)]");
    }

    [Test]
    public async Task TransformDatabaseSnapshot_NewProviderAmbiguity_IsUnionedWithExistingMarker()
    {
        var sourceDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: true));
        var sourceProperty = sourceDatabase.TableModels[0].Model.ValueProperties["Id"];
        sourceProperty.SetAttributesCore(
            sourceProperty.Attributes
                .Where(static x =>
                    x is not GuidStorageAttribute &&
                    x is not GuidStorageUnresolvedAttribute)
                .Append(new GuidStorageUnresolvedAttribute(DatabaseType.MySQL)));
        sourceProperty.Column.SetGuidStorageDefinitionsCore([]);
        sourceDatabase.Freeze();

        var destinationDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: false));
        var destinationProperty = destinationDatabase.TableModels[0]
            .Model.ValueProperties["id"];
        destinationProperty.SetAttributesCore(
            destinationProperty.Attributes.Where(
                static x => x is not GuidStorageAttribute));
        destinationProperty.Column.SetDbTypesCore(
            destinationProperty.Column.DbTypes.Concat(
            [
                new DatabaseColumnType(DatabaseType.MariaDB, "binary", 16)
            ]));
        destinationProperty.Column.SetUnresolvedGuidStorageProvidersCore(
            [DatabaseType.MySQL, DatabaseType.MariaDB]);
        destinationDatabase.Freeze();

        var transformed = new MetadataTransformer(new MetadataTransformerOptions())
            .TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);
        var transformedProperty = transformed.TableModels[0].Model.ValueProperties["Id"];
        var unresolvedProviders = transformedProperty.Attributes
            .OfType<GuidStorageUnresolvedAttribute>()
            .Select(static x => x.DatabaseType)
            .ToArray();

        await Assert.That(unresolvedProviders)
            .IsEquivalentTo([DatabaseType.MySQL, DatabaseType.MariaDB]);
        await Assert.That(transformedProperty.Column
            .UnresolvedGuidStorageProviders
            .ToArray())
            .IsEquivalentTo([DatabaseType.MySQL, DatabaseType.MariaDB]);
    }

    [Test]
    public async Task TransformDatabaseSnapshot_ResolvedProviderType_RemovesStaleUnresolvedMarker()
    {
        var sourceDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: true));
        var sourceProperty = sourceDatabase.TableModels[0].Model.ValueProperties["Id"];
        sourceProperty.SetAttributesCore(
            sourceProperty.Attributes
                .Where(static x =>
                    x is not GuidStorageAttribute &&
                    x is not GuidStorageUnresolvedAttribute)
                .Append(new GuidStorageUnresolvedAttribute(DatabaseType.MySQL)));
        sourceProperty.Column.SetGuidStorageDefinitionsCore([]);
        sourceDatabase.Freeze();

        var destinationDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: false));
        var destinationProperty = destinationDatabase.TableModels[0]
            .Model.ValueProperties["id"];
        destinationProperty.SetAttributesCore(
            destinationProperty.Attributes.Where(
                static x => x is not GuidStorageAttribute));
        destinationProperty.Column.SetDbTypesCore(
            [new DatabaseColumnType(DatabaseType.MySQL, "char", 36)]);
        destinationProperty.Column.SetUnresolvedGuidStorageProvidersCore([]);
        destinationDatabase.Freeze();

        var transformed = new MetadataTransformer(new MetadataTransformerOptions())
            .TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);
        var transformedProperty = transformed.TableModels[0].Model.ValueProperties["Id"];

        await Assert.That(transformedProperty.Attributes
            .OfType<GuidStorageUnresolvedAttribute>()).IsEmpty();
        await Assert.That(transformedProperty.Column
            .UnresolvedGuidStorageProviders).IsEmpty();
    }

    [Test]
    public async Task TransformDatabaseSnapshot_InternalSnapshotAmbiguity_IsNotReclassifiedAsLegacyPolicy()
    {
        var sourceDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: true));
        var sourceProperty = sourceDatabase.TableModels[0].Model.ValueProperties["Id"];
        sourceProperty.SetAttributesCore(
            sourceProperty.Attributes.Where(
                static x =>
                    x is not GuidStorageAttribute &&
                    x is not GuidStorageUnresolvedAttribute));
        sourceProperty.Column.SetGuidStorageDefinitionsCore([]);
        sourceProperty.Column.SetUnresolvedGuidStorageProvidersCore(
            [DatabaseType.MySQL]);
        sourceDatabase.Freeze();

        var destinationDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: false));
        var destinationProperty = destinationDatabase.TableModels[0]
            .Model.ValueProperties["id"];
        destinationProperty.SetAttributesCore(
            destinationProperty.Attributes.Where(
                static x => x is not GuidStorageAttribute));
        destinationProperty.Column.SetDbTypesCore(
            [new DatabaseColumnType(DatabaseType.MySQL, "binary", 16)]);
        destinationProperty.Column.SetUnresolvedGuidStorageProvidersCore(
            [DatabaseType.MySQL]);
        destinationDatabase.Freeze();

        var transformed = new MetadataTransformer(new MetadataTransformerOptions())
            .TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);
        var transformedProperty = transformed.TableModels[0].Model.ValueProperties["Id"];

        await Assert.That(transformedProperty.Attributes
            .OfType<GuidStorageUnresolvedAttribute>()
            .Select(static x => x.DatabaseType)
            .ToArray()).IsEquivalentTo([DatabaseType.MySQL]);
        await Assert.That(transformedProperty.Column
            .UnresolvedGuidStorageProviders
            .ToArray()).IsEquivalentTo([DatabaseType.MySQL]);
    }

    [Test]
    public async Task TransformDatabaseSnapshot_TextSourceType_DoesNotResolveNewBinaryAmbiguity()
    {
        var sourceDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: true));
        var sourceProperty = sourceDatabase.TableModels[0].Model.ValueProperties["Id"];
        sourceProperty.SetAttributesCore(
            sourceProperty.Attributes.Where(
                static x =>
                    x is not GuidStorageAttribute &&
                    x is not GuidStorageUnresolvedAttribute));
        sourceProperty.Column.SetDbTypesCore(
            [new DatabaseColumnType(DatabaseType.MySQL, "char", 36)]);
        sourceProperty.Column.SetGuidStorageDefinitionsCore([]);
        sourceDatabase.Freeze();

        var destinationDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: false));
        var destinationProperty = destinationDatabase.TableModels[0]
            .Model.ValueProperties["id"];
        destinationProperty.SetAttributesCore(
            destinationProperty.Attributes.Where(
                static x => x is not GuidStorageAttribute));
        destinationProperty.Column.SetDbTypesCore(
            [new DatabaseColumnType(DatabaseType.MySQL, "binary", 16)]);
        destinationProperty.Column.SetUnresolvedGuidStorageProvidersCore(
            [DatabaseType.MySQL]);
        destinationDatabase.Freeze();

        var transformed = new MetadataTransformer(new MetadataTransformerOptions())
            .TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        await Assert.That(transformed.TableModels[0].Model.ValueProperties["Id"]
            .Attributes
            .OfType<GuidStorageUnresolvedAttribute>()
            .Select(static x => x.DatabaseType)
            .ToArray()).IsEquivalentTo([DatabaseType.MySQL]);
    }

    [Test]
    public async Task TransformDatabaseSnapshot_LegacyBinarySourceType_SuppliesCompatibilityPolicy()
    {
        var sourceDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: true));
        var sourceProperty = sourceDatabase.TableModels[0].Model.ValueProperties["Id"];
        sourceProperty.SetAttributesCore(
            sourceProperty.Attributes.Where(
                static x =>
                    x is not GuidStorageAttribute &&
                    x is not GuidStorageUnresolvedAttribute));
        sourceProperty.Column.SetDbTypesCore(
            [new DatabaseColumnType(DatabaseType.MySQL, "binary", 16)]);
        sourceProperty.Column.SetGuidStorageDefinitionsCore([]);
        sourceDatabase.Freeze();

        var destinationDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: false));
        var destinationProperty = destinationDatabase.TableModels[0]
            .Model.ValueProperties["id"];
        destinationProperty.SetAttributesCore(
            destinationProperty.Attributes.Where(
                static x => x is not GuidStorageAttribute));
        destinationProperty.Column.SetDbTypesCore(
            [new DatabaseColumnType(DatabaseType.MySQL, "binary", 16)]);
        destinationProperty.Column.SetUnresolvedGuidStorageProvidersCore(
            [DatabaseType.MySQL]);
        destinationDatabase.Freeze();

        var transformed = new MetadataTransformer(new MetadataTransformerOptions())
            .TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);
        var transformedProperty = transformed.TableModels[0].Model.ValueProperties["Id"];

        await Assert.That(transformedProperty.Attributes
            .OfType<GuidStorageUnresolvedAttribute>()).IsEmpty();
        await Assert.That(transformedProperty.Column
            .UnresolvedGuidStorageProviders).IsEmpty();
    }

    [Test]
    public async Task TransformDatabaseSnapshot_ExplicitBinaryPolicy_ReplacesUnresolvedMarker()
    {
        var sourceDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: true));
        var sourceProperty = sourceDatabase.TableModels[0].Model.ValueProperties["Id"];
        sourceProperty.SetAttributesCore(
            sourceProperty.Attributes
                .Where(static x =>
                    x is not GuidStorageAttribute &&
                    x is not GuidStorageUnresolvedAttribute)
                .Append(new GuidStorageAttribute(
                    DatabaseType.MySQL,
                    GuidStorageFormat.Binary16Rfc4122)));
        sourceProperty.Column.SetDbTypesCore(
            [new DatabaseColumnType(DatabaseType.MySQL, "binary", 16)]);
        sourceProperty.Column.SetGuidStorageDefinitionsCore([]);
        sourceDatabase.Freeze();

        var destinationDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: false));
        var destinationProperty = destinationDatabase.TableModels[0]
            .Model.ValueProperties["id"];
        destinationProperty.SetAttributesCore(
            destinationProperty.Attributes.Where(
                static x => x is not GuidStorageAttribute));
        destinationProperty.Column.SetDbTypesCore(
            [new DatabaseColumnType(DatabaseType.MySQL, "binary", 16)]);
        destinationProperty.Column.SetUnresolvedGuidStorageProvidersCore(
            [DatabaseType.MySQL]);
        destinationDatabase.Freeze();

        var transformed = new MetadataTransformer(new MetadataTransformerOptions())
            .TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);
        var transformedProperty = transformed.TableModels[0].Model.ValueProperties["Id"];

        await Assert.That(transformedProperty.Attributes
            .OfType<GuidStorageUnresolvedAttribute>()).IsEmpty();
        await Assert.That(transformedProperty.Attributes
            .OfType<GuidStorageAttribute>()
            .Single(x => x.DatabaseType == DatabaseType.MySQL)
            .Format).IsEqualTo(GuidStorageFormat.Binary16Rfc4122);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, true)]
    public async Task TransformDatabaseSnapshot_NonGuidSourceRemap_RespectsOverwritePolicy(
        bool overwritePropertyTypes,
        bool expectUnresolved)
    {
        var sourceDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: true));
        var sourceProperty = sourceDatabase.TableModels[0].Model.ValueProperties["Id"];
        sourceProperty.SetCsTypeCore(new CsTypeDeclaration(typeof(byte[])));
        sourceProperty.SetAttributesCore(
            sourceProperty.Attributes.Where(
                static x =>
                    x is not GuidStorageAttribute &&
                    x is not GuidStorageUnresolvedAttribute));
        sourceProperty.Column.SetScalarMappingCore(
            ColumnScalarMapping.Identity(new CsTypeDeclaration(typeof(byte[]))));
        sourceProperty.Column.SetDbTypesCore(
            [new DatabaseColumnType(DatabaseType.SQLite, "BLOB")]);
        sourceProperty.Column.SetGuidStorageDefinitionsCore([]);
        sourceDatabase.Freeze();

        var destinationDatabase = MetadataDefinitionSnapshot.Copy(
            CreateGuidStorageTransformDatabase(source: false));
        var destinationProperty = destinationDatabase.TableModels[0]
            .Model.ValueProperties["id"];
        destinationProperty.SetAttributesCore(
            destinationProperty.Attributes.Where(
                static x => x is not GuidStorageAttribute));
        destinationProperty.Column.SetDbTypesCore(
            [new DatabaseColumnType(DatabaseType.SQLite, "BLOB")]);
        destinationProperty.Column.SetUnresolvedGuidStorageProvidersCore(
            [DatabaseType.SQLite]);
        destinationDatabase.Freeze();

        var transformed = new MetadataTransformer(new MetadataTransformerOptions
            {
                OverwritePropertyTypes = overwritePropertyTypes
            })
            .TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);
        var transformedProperty = transformed.TableModels[0].Model.ValueProperties["Id"];
        var hasUnresolved = transformedProperty.Attributes
            .OfType<GuidStorageUnresolvedAttribute>()
            .Any();

        await Assert.That(hasUnresolved).IsEqualTo(expectUnresolved);
        await Assert.That(transformedProperty.CsType.Name)
            .IsEqualTo(overwritePropertyTypes
                ? new CsTypeDeclaration(typeof(Guid)).Name
                : new CsTypeDeclaration(typeof(byte[])).Name);
    }

    [Test]
    public async Task TransformDatabaseSnapshot_PreservesNullableForeignKeyRelation()
    {
        var sourceDatabase = CreateSourceDatabase(nullableForeignKeyRelation: true);
        var destinationDatabase = CreateDestinationDatabase(nullableForeignKeyRelation: true);
        var transformer = new MetadataTransformer(new MetadataTransformerOptions());

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        var destinationModel = destinationDatabase.TableModels.First(tm => tm.Table.DbName == "my_table").Model;
        var relationProperty = destinationModel.RelationProperties["RelatedItems"];
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions { UseNullableReferenceTypes = true })
            .CreateModelFiles(destinationDatabase)
            .Single(file => file.path == "MyModel.cs");

        await Assert.That(relationProperty.RelationPart).IsNotNull();
        await Assert.That(relationProperty.RelationPart!.Type).IsEqualTo(RelationPartType.ForeignKey);
        await Assert.That(relationProperty.CsNullable).IsTrue();
        await Assert.That(generatedFile.contents).Contains("public abstract MyOtherModel? RelatedItems { get; }");
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
    public async Task TransformDatabaseSnapshot_PreservesSourceNamespacesAndUsings()
    {
        var sourceDatabase = CreateSourceDatabase(includeUsings: true);
        var destinationDatabase = CreateDestinationDatabase();
        var transformer = new MetadataTransformer(new MetadataTransformerOptions());

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        var destinationTableModel = destinationDatabase.TableModels.First(tm => tm.Table.DbName == "my_table");

        await Assert.That(destinationDatabase.CsType.Namespace).IsEqualTo("SourceNamespace");
        await Assert.That(destinationDatabase.Usings.Select(x => x.FullNamespaceName).ToArray()).Contains("Source.Database.Helpers");
        await Assert.That(destinationTableModel.Model.CsType.Namespace).IsEqualTo("SourceNamespace");
        await Assert.That(destinationTableModel.Model.Usings.Select(x => x.FullNamespaceName).ToArray()).Contains("Source.Model.Helpers");
    }

    [Test]
    public async Task TransformDatabaseSnapshot_PreservesSourceNamespaceWhenTypeNamesMatch()
    {
        var sourceDatabase = CreateSourceDatabase(
            modelName: "raw_table",
            databaseCsTypeName: "MyDatabase");
        var destinationDatabase = CreateDestinationDatabase();
        var transformer = new MetadataTransformer(new MetadataTransformerOptions());

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        var destinationTableModel = destinationDatabase.TableModels.First(tm => tm.Table.DbName == "my_table");

        await Assert.That(destinationDatabase.CsType.Name).IsEqualTo("MyDatabase");
        await Assert.That(destinationDatabase.CsType.Namespace).IsEqualTo("SourceNamespace");
        await Assert.That(destinationTableModel.Model.CsType.Name).IsEqualTo("raw_table");
        await Assert.That(destinationTableModel.Model.CsType.Namespace).IsEqualTo("SourceNamespace");
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
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(destinationDatabase)
            .Single(file => file.path == "MyModel.cs");

        await Assert.That(transformedProperty.PropertyName).IsEqualTo("Status");
        await Assert.That(transformedProperty.CsType.Name).IsEqualTo("MyStatusEnum");
        await Assert.That(transformedProperty.EnumProperty.HasValue).IsTrue();
        await Assert.That(transformedProperty.Column.DbName).IsEqualTo("status_db");
        await Assert.That(generatedFile.contents).Contains("public enum MyStatusEnum");
        await Assert.That(generatedFile.contents).DoesNotContain("[Enum(");
    }

    [Test]
    public async Task OverwriteTypes_True_PreservesExternalEnumReferenceWithoutGeneratingEnum()
    {
        var sourceDatabase = CreateSourceDatabase(useExternalStatusEnum: true);
        var destinationDatabase = CreateDestinationDatabase(includeStatusEnum: true);
        var transformer = new MetadataTransformer(new MetadataTransformerOptions { OverwritePropertyTypes = true });

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        var transformedProperty = destinationDatabase.TableModels[0].Model.ValueProperties["Status"];
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(destinationDatabase)
            .Single(file => file.path == "MyModel.cs");

        await Assert.That(transformedProperty.PropertyName).IsEqualTo("Status");
        await Assert.That(transformedProperty.CsType.Name).IsEqualTo("ExternalStatus");
        await Assert.That(transformedProperty.EnumProperty.HasValue).IsTrue();
        await Assert.That(transformedProperty.EnumProperty!.Value.DeclaredInModelFile).IsFalse();
        await Assert.That(generatedFile.contents).Contains("public abstract ExternalStatus Status { get; }");
        await Assert.That(generatedFile.contents).Contains(@"[Enum(""Active"", ""Inactive"")]");
        await Assert.That(generatedFile.contents).DoesNotContain("public enum ExternalStatus");
    }

    [Test]
    public async Task OverwriteTypes_True_PreservesExternalEnumReferenceForIntegerColumnWithoutEnumAttribute()
    {
        var sourceDatabase = CreateSourceDatabase(useExternalStatusEnum: true);
        var destinationDatabase = CreateDestinationDatabase(includeStatusEnum: false);
        var transformer = new MetadataTransformer(new MetadataTransformerOptions { OverwritePropertyTypes = true });

        destinationDatabase = transformer.TransformDatabaseSnapshot(sourceDatabase, destinationDatabase);

        var transformedProperty = destinationDatabase.TableModels[0].Model.ValueProperties["Status"];
        var generatedFile = new ModelFileFactory(new ModelFileFactoryOptions())
            .CreateModelFiles(destinationDatabase)
            .Single(file => file.path == "MyModel.cs");

        await Assert.That(transformedProperty.PropertyName).IsEqualTo("Status");
        await Assert.That(transformedProperty.CsType.Name).IsEqualTo("ExternalStatus");
        await Assert.That(transformedProperty.EnumProperty.HasValue).IsTrue();
        await Assert.That(generatedFile.contents).Contains("public abstract ExternalStatus Status { get; }");
        await Assert.That(generatedFile.contents).DoesNotContain("[Enum(");
        await Assert.That(generatedFile.contents).DoesNotContain("public enum ExternalStatus");
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
