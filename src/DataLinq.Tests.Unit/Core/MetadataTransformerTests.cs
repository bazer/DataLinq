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
    private DatabaseDefinition CreateDestinationDatabase(string dbName = "MyDatabase", string tableDbName = "my_table", string modelName = "raw_table", bool includeStatusDefault = false)
    {
        var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
        var dbCsType = new CsTypeDeclaration(dbName, "RawNamespace", ModelCsType.Class);
        var modelCsType = new CsTypeDeclaration(modelName, "RawNamespace", ModelCsType.Class);

        var db = new DatabaseDefinition(dbName, dbCsType);
        var model = new ModelDefinition(modelCsType);
        model.SetInterfaces([iTableModel]);

        var table = new TableDefinition(tableDbName);
        var tableModel = new TableModel(modelName + "s", db, model, table);

        var col1Property = new ValueProperty("col1_db", new CsTypeDeclaration(typeof(int)), model, [new ColumnAttribute("col1_db"), new PrimaryKeyAttribute()]);
        var col2Property = new ValueProperty("col2_db", new CsTypeDeclaration(typeof(int)), model, [new ColumnAttribute("col2_db")]);
        var statusAttributes = includeStatusDefault
            ? new Attribute[] { new ColumnAttribute("status_db"), new DefaultAttribute(1) }
            : [new ColumnAttribute("status_db")];
        var statusProperty = new ValueProperty("status_db", new CsTypeDeclaration(typeof(int)), model, statusAttributes);
        model.AddProperty(col1Property);
        model.AddProperty(col2Property);
        model.AddProperty(statusProperty);

        var col1 = table.ParseColumn(col1Property);
        var col2 = table.ParseColumn(col2Property);
        var col3 = table.ParseColumn(statusProperty);
        table.SetColumns([col1, col2, col3]);

        var otherModel = new ModelDefinition(new CsTypeDeclaration("OtherRaw", "RawNamespace", ModelCsType.Class));
        var otherTable = new TableDefinition("other_table");
        var otherTableModel = new TableModel("OtherRaws", db, otherModel, otherTable);
        var otherIdProperty = new ValueProperty("other_id", new CsTypeDeclaration(typeof(int)), otherModel, [new ColumnAttribute("other_id"), new PrimaryKeyAttribute()]);
        otherModel.AddProperty(otherIdProperty);
        var otherIdColumn = otherTable.ParseColumn(otherIdProperty);
        otherTable.SetColumns([otherIdColumn]);

        var relation = new RelationDefinition("FK_DestConstraint", RelationType.OneToMany);
        var foreignKeyIndex = new ColumnIndex("placeholder_fk_index", IndexCharacteristic.ForeignKey, IndexType.BTREE, [col1]);
        var primaryKeyIndex = new ColumnIndex("placeholder_pk_index", IndexCharacteristic.PrimaryKey, IndexType.BTREE, [otherIdColumn]);

        relation.ForeignKey = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, "rel_prop_db");
        relation.CandidateKey = new RelationPart(primaryKeyIndex, relation, RelationPartType.CandidateKey, "OtherRaws");

        var relationProperty = new RelationProperty("rel_prop_db", otherModel.CsType, model, []);
        relationProperty.SetRelationPart(relation.ForeignKey);
        model.AddProperty(relationProperty);

        db.SetTableModels([tableModel, otherTableModel]);
        return db;
    }

    private DatabaseDefinition CreateSourceDatabase(string dbName = "MyDatabase", string tableDbName = "my_table", string modelName = "MyModel", string interfaceName = "IMyModel", bool includeEnumDefault = false)
    {
        var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
        var dbCsType = new CsTypeDeclaration("MyDatabaseCsType", "SourceNamespace", ModelCsType.Class);
        var modelCsType = new CsTypeDeclaration(modelName, "SourceNamespace", ModelCsType.Class);
        var interfaceType = new CsTypeDeclaration(interfaceName, "SourceNamespace", ModelCsType.Interface);

        var db = new DatabaseDefinition(dbName, dbCsType);
        db.SetAttributes([new DatabaseAttribute(dbName)]);

        var model = new ModelDefinition(modelCsType);
        model.SetInterfaces([iTableModel]);
        model.SetAttributes([new TableAttribute(tableDbName), new InterfaceAttribute(interfaceName)]);
        model.SetModelInstanceInterface(interfaceType);

        var table = MetadataFactory.ParseTable(model).Value;
        var tableModel = new TableModel("MyModels", db, model, table);

        var col1Property = CreateTestValueProperty(model, "Id", typeof(int), [new ColumnAttribute("col1_db"), new PrimaryKeyAttribute()]);
        var col2Property = CreateTestValueProperty(model, "Name", typeof(string), [new ColumnAttribute("col2_db")]);
        col2Property.SetCsNullable(true);

        var enumType = new CsTypeDeclaration("MyStatusEnum", "SourceNamespace", ModelCsType.Enum);
        var statusAttributes = includeEnumDefault
            ? new Attribute[]
            {
                new ColumnAttribute("status_db"),
                new EnumAttribute("Active", "Inactive"),
                new DefaultAttribute("MyStatusEnum.Inactive").SetCodeExpression("MyStatusEnum.Inactive")
            }
            : [new ColumnAttribute("status_db"), new EnumAttribute("Active", "Inactive")];
        var statusProperty = new ValueProperty("Status", enumType, model, statusAttributes);
        statusProperty.SetEnumProperty(new EnumProperty(
            enumValues: [("Active", 1), ("Inactive", 2)],
            csEnumValues: [("Active", 1), ("Inactive", 2)],
            declaredInClass: false));

        var col1 = table.ParseColumn(col1Property);
        var col2 = table.ParseColumn(col2Property);
        var statusColumn = table.ParseColumn(statusProperty);
        table.SetColumns([col1, col2, statusColumn]);

        var otherModel = new ModelDefinition(new CsTypeDeclaration("MyOtherModel", "SourceNamespace", ModelCsType.Class));
        var otherTable = new TableDefinition("other_table");
        var otherTableModel = new TableModel("MyOtherModels", db, otherModel, otherTable);
        var otherIdProperty = new ValueProperty("OtherId", new CsTypeDeclaration(typeof(int)), otherModel, [new ColumnAttribute("other_id"), new PrimaryKeyAttribute()]);
        otherModel.AddProperty(otherIdProperty);
        var otherIdColumn = otherTable.ParseColumn(otherIdProperty);
        otherTable.SetColumns([otherIdColumn]);

        var relation = new RelationDefinition("FK_FROM_SOURCE", RelationType.OneToMany);
        var foreignKeyIndex = new ColumnIndex("placeholder_fk_index", IndexCharacteristic.ForeignKey, IndexType.BTREE, [col1]);
        var primaryKeyIndex = new ColumnIndex("placeholder_pk_index", IndexCharacteristic.PrimaryKey, IndexType.BTREE, [otherIdColumn]);

        relation.ForeignKey = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, "RelatedItems");
        relation.CandidateKey = new RelationPart(primaryKeyIndex, relation, RelationPartType.CandidateKey, "MyModel");

        var relationProperty = new RelationProperty("RelatedItems", otherModel.CsType, model, [new RelationAttribute("other_table", "other_id", "FK_FROM_SOURCE")]);
        relationProperty.SetRelationPart(relation.ForeignKey);

        model.AddProperty(col1Property);
        model.AddProperty(col2Property);
        model.AddProperty(statusProperty);
        model.AddProperty(relationProperty);

        db.SetTableModels([tableModel, otherTableModel]);
        return db;
    }

    private static ValueProperty CreateTestValueProperty(ModelDefinition model, string propertyName, Type csType, Attribute[] attributes)
    {
        return new ValueProperty(propertyName, new CsTypeDeclaration(csType), model, attributes);
    }

    [Test]
    public async Task TransformDatabase_AppliesNamesAndAttributes()
    {
        var sourceDatabase = CreateSourceDatabase(modelName: "SourceModel");
        var destinationDatabase = CreateDestinationDatabase();
        var transformer = new MetadataTransformer(new MetadataTransformerOptions());

        transformer.TransformDatabase(sourceDatabase, destinationDatabase);

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
    public async Task TransformDatabase_RemoveInterfacePrefix_Enabled()
    {
        var sourceDatabase = CreateSourceDatabase(modelName: "SrcModel", interfaceName: "ISrcModel");
        var destinationDatabase = CreateDestinationDatabase(modelName: "dest_model");
        var transformer = new MetadataTransformer(new MetadataTransformerOptions(removeInterfacePrefix: true));

        transformer.TransformDatabase(sourceDatabase, destinationDatabase);

        await Assert.That(destinationDatabase.TableModels[0].Model.CsType.Name).IsEqualTo("SrcModel");
    }

    [Test]
    public async Task TransformDatabase_RemoveInterfacePrefix_Disabled()
    {
        var sourceDatabase = CreateSourceDatabase(modelName: "ISrcModelAsClass", interfaceName: "ISrcModel");
        var destinationDatabase = CreateDestinationDatabase(modelName: "dest_model");
        var transformer = new MetadataTransformer(new MetadataTransformerOptions(removeInterfacePrefix: false));

        transformer.TransformDatabase(sourceDatabase, destinationDatabase);

        await Assert.That(destinationDatabase.TableModels[0].Model.CsType.Name).IsEqualTo("ISrcModelAsClass");
    }

    [Test]
    public async Task OverwriteTypes_False_PreservesSourceType()
    {
        var sourceDatabase = CreateSourceDatabase();
        var destinationDatabase = CreateDestinationDatabase();
        var transformer = new MetadataTransformer(new MetadataTransformerOptions { OverwritePropertyTypes = false });

        transformer.TransformDatabase(sourceDatabase, destinationDatabase);

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

        transformer.TransformDatabase(sourceDatabase, destinationDatabase);

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

        transformer.TransformDatabase(sourceDatabase, destinationDatabase);

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

        transformer.TransformDatabase(sourceDatabase, destinationDatabase);

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

        transformer.TransformDatabase(sourceDatabase, destinationDatabase);

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

        transformer.TransformDatabase(sourceDatabase, destinationDatabase);

        var relationProperty = destinationDatabase.TableModels[0].Model.RelationProperties["RelatedItems"];

        await Assert.That(relationProperty.PropertyName).IsEqualTo("RelatedItems");
        await Assert.That(relationProperty.RelationPart).IsNotNull();
        await Assert.That(relationProperty.RelationPart!.Relation.ConstraintName).IsEqualTo("FK_DestConstraint");
    }
}
