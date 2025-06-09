// In src/DataLinq.Tests/Core/MetadataTransformerTests.cs

using System;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using ThrowAway.Extensions;
using Xunit;

namespace DataLinq.Tests.Core
{
    public class MetadataTransformerTests
    {
        #region Test Helper Methods

        // Represents what might be parsed directly from a DB schema (raw names)
        private DatabaseDefinition CreateDestinationDatabase(string dbName = "MyDatabase", string tableDbName = "my_table", string modelName = "raw_table")
        {
            var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
            var dbCsType = new CsTypeDeclaration(dbName, "RawNamespace", ModelCsType.Class);
            var modelCsType = new CsTypeDeclaration(modelName, "RawNamespace", ModelCsType.Class);

            var db = new DatabaseDefinition(dbName, dbCsType);
            var model = new ModelDefinition(modelCsType);
            model.SetInterfaces([iTableModel]);

            var table = new TableDefinition(tableDbName);
            var tableModel = new TableModel(modelName + "s", db, model, table);

            // 1. Define columns for the main table
            var col1Vp = new ValueProperty("col1_db", new CsTypeDeclaration(typeof(int)), model, [new ColumnAttribute("col1_db"), new PrimaryKeyAttribute()]);
            var col2Vp = new ValueProperty("col2_db", new CsTypeDeclaration(typeof(int)), model, [new ColumnAttribute("col2_db")]);
            var col3Vp = new ValueProperty("status_db", new CsTypeDeclaration(typeof(int)), model, [new ColumnAttribute("status_db")]);
            model.AddProperty(col1Vp); model.AddProperty(col2Vp); model.AddProperty(col3Vp);

            var col1 = table.ParseColumn(col1Vp);
            var col2 = table.ParseColumn(col2Vp);
            var col3 = table.ParseColumn(col3Vp);
            table.SetColumns([col1, col2, col3]);

            // 2. Define the "Other" table that we are relating to
            var otherModel = new ModelDefinition(new CsTypeDeclaration("OtherRaw", "RawNamespace", ModelCsType.Class));
            var otherTable = new TableDefinition("other_table");
            var otherTableModel = new TableModel("OtherRaws", db, otherModel, otherTable);
            var otherIdVp = new ValueProperty("other_id", new CsTypeDeclaration(typeof(int)), otherModel, [new ColumnAttribute("other_id"), new PrimaryKeyAttribute()]);
            otherModel.AddProperty(otherIdVp);
            var otherIdCol = otherTable.ParseColumn(otherIdVp);
            otherTable.SetColumns([otherIdCol]);

            // 3. Create a complete, two-sided relationship
            var destRelation = new RelationDefinition("FK_DestConstraint", RelationType.OneToMany);
            var destFkIndex = new ColumnIndex("placeholder_fk_index", IndexCharacteristic.ForeignKey, IndexType.BTREE, [col1]);
            var destPkIndex = new ColumnIndex("placeholder_pk_index", IndexCharacteristic.PrimaryKey, IndexType.BTREE, [otherIdCol]);

            destRelation.ForeignKey = new RelationPart(destFkIndex, destRelation, RelationPartType.ForeignKey, "rel_prop_db");
            destRelation.CandidateKey = new RelationPart(destPkIndex, destRelation, RelationPartType.CandidateKey, "OtherRaws");

            var relProp = new RelationProperty("rel_prop_db", otherModel.CsType, model, []);
            relProp.SetRelationPart(destRelation.ForeignKey);
            model.AddProperty(relProp);

            db.SetTableModels([tableModel, otherTableModel]);
            return db;
        }

        // Represents what might be parsed from developer source code (intended names, attributes)
        // In src/DataLinq.Tests/Core/MetadataTransformerTests.cs

        private DatabaseDefinition CreateSourceDatabase(string dbName = "MyDatabase", string tableDbName = "my_table", string modelName = "MyModel", string interfaceName = "IMyModel")
        {
            var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
            var dbCsType = new CsTypeDeclaration("MyDatabaseCsType", "SourceNamespace", ModelCsType.Class);
            var modelCsType = new CsTypeDeclaration(modelName, "SourceNamespace", ModelCsType.Class);
            var modelInterfaceType = new CsTypeDeclaration(interfaceName, "SourceNamespace", ModelCsType.Interface);

            var db = new DatabaseDefinition(dbName, dbCsType);
            db.SetAttributes([new DatabaseAttribute(dbName)]);

            var model = new ModelDefinition(modelCsType);
            model.SetInterfaces([iTableModel]);
            model.SetAttributes([new TableAttribute(tableDbName), new InterfaceAttribute(interfaceName)]);
            model.SetModelInstanceInterface(modelInterfaceType);

            var table = MetadataFactory.ParseTable(model).Value;
            var tableModel = new TableModel("MyModels", db, model, table);

            // --- REORDERED LOGIC STARTS HERE ---

            // 1. Create all ValueProperty objects
            var col1Vp = CreateTestValueProperty(model, "Id", typeof(int), [new ColumnAttribute("col1_db"), new PrimaryKeyAttribute()]);
            // ... (col2Vp, statusVp setup as before)
            var col2Vp = CreateTestValueProperty(model, "Name", typeof(string), [new ColumnAttribute("col2_db")]);
            col2Vp.SetCsNullable(true);
            var enumCsType = new CsTypeDeclaration("MyStatusEnum", "SourceNamespace", ModelCsType.Enum);
            var statusVp = new ValueProperty("Status", enumCsType, model, [new ColumnAttribute("status_db"), new EnumAttribute("Active", "Inactive")]);
            statusVp.SetEnumProperty(new EnumProperty(
                enumValues: new[] { ("Active", 1), ("Inactive", 2) },
                csEnumValues: new[] { ("Active", 1), ("Inactive", 2) },
                declaredInClass: false));

            // 2. Parse them and set them on the table
            var col1 = table.ParseColumn(col1Vp);
            var col2 = table.ParseColumn(col2Vp);
            var statusCol = table.ParseColumn(statusVp);
            table.SetColumns([col1, col2, statusCol]);

            // 3. Define the "Other" table that we are relating to
            var otherModel = new ModelDefinition(new CsTypeDeclaration("MyOtherModel", "SourceNamespace", ModelCsType.Class));
            var otherTable = new TableDefinition("other_table");
            var otherTableModel = new TableModel("MyOtherModels", db, otherModel, otherTable);
            var otherIdVp = new ValueProperty("OtherId", new CsTypeDeclaration(typeof(int)), otherModel, [new ColumnAttribute("other_id"), new PrimaryKeyAttribute()]);
            otherModel.AddProperty(otherIdVp);
            var otherIdCol = otherTable.ParseColumn(otherIdVp);
            otherTable.SetColumns([otherIdCol]);

            // 4. Create the complete, two-sided relationship
            var srcRelation = new RelationDefinition("FK_FROM_SOURCE", RelationType.OneToMany);
            var srcFkIndex = new ColumnIndex("placeholder_fk_index", IndexCharacteristic.ForeignKey, IndexType.BTREE, [col1]);
            var srcPkIndex = new ColumnIndex("placeholder_pk_index", IndexCharacteristic.PrimaryKey, IndexType.BTREE, [otherIdCol]);

            srcRelation.ForeignKey = new RelationPart(srcFkIndex, srcRelation, RelationPartType.ForeignKey, "RelatedItems");
            srcRelation.CandidateKey = new RelationPart(srcPkIndex, srcRelation, RelationPartType.CandidateKey, "MyModel");

            var relProp = new RelationProperty("RelatedItems", otherModel.CsType, model, [new RelationAttribute("other_table", "other_id", "FK_FROM_SOURCE")]);
            relProp.SetRelationPart(srcRelation.ForeignKey);

            // 5. Add all properties to the model at the end
            model.AddProperty(col1Vp);
            model.AddProperty(col2Vp);
            model.AddProperty(statusVp);
            model.AddProperty(relProp);

            db.SetTableModels([tableModel, otherTableModel]);
            return db;
        }

        private ValueProperty CreateTestValueProperty(ModelDefinition model, string propName, Type csType, Attribute[] attributes)
        {
            var csTypeDecl = new CsTypeDeclaration(csType);
            return new ValueProperty(propName, csTypeDecl, model, attributes);
        }

        #endregion

        #region General Transformation Tests

        // In src/DataLinq.Tests/Core/MetadataTransformerTests.cs

        [Fact]
        public void TransformDatabase_AppliesNamesAndAttributes()
        {
            // Arrange
            var srcDb = CreateSourceDatabase(modelName: "SourceModel");
            var destDb = CreateDestinationDatabase();
            var transformer = new MetadataTransformer(new MetadataTransformerOptions());

            // Act
            transformer.TransformDatabase(srcDb, destDb);

            // Assert
            // --- DATABASE LEVEL ---
            Assert.Equal("MyDatabase", destDb.DbName);
            Assert.Equal("MyDatabaseCsType", destDb.CsType.Name);
            Assert.Equal("SourceNamespace", destDb.CsType.Namespace);

            // --- NEW: Assert that both tables are present ---
            Assert.Equal(2, destDb.TableModels.Length);

            // --- TABLEMODEL & MODEL LEVEL for the main table ---
            var destTableModel = destDb.TableModels.FirstOrDefault(tm => tm.Table.DbName == "my_table");
            Assert.NotNull(destTableModel);
            Assert.Equal("MyModels", destTableModel.CsPropertyName); // The name of the collection property on the DB context

            var destModel = destTableModel.Model;
            Assert.Equal("SourceModel", destModel.CsType.Name);
            Assert.Equal("SourceNamespace", destModel.CsType.Namespace);
            Assert.NotNull(destModel.ModelInstanceInterface);
            Assert.Equal("IMyModel", destModel.ModelInstanceInterface.Value.Name);

            // --- TABLE DEFINITION LEVEL ---
            var destTable = destTableModel.Table;
            Assert.Equal("my_table", destTable.DbName);

            // --- VALUE PROPERTY LEVEL ---
            Assert.True(destModel.ValueProperties.ContainsKey("Id"));
            Assert.False(destModel.ValueProperties.ContainsKey("col1_db"));
            Assert.Equal("Id", destModel.ValueProperties["Id"].PropertyName);
            Assert.Equal("col1_db", destModel.ValueProperties["Id"].Column.DbName);

            Assert.True(destModel.ValueProperties.ContainsKey("Name"));
            Assert.Equal("Name", destModel.ValueProperties["Name"].PropertyName);
            Assert.Equal("col2_db", destModel.ValueProperties["Name"].Column.DbName);

            // --- RELATION PROPERTY LEVEL ---
            Assert.True(destModel.RelationProperties.ContainsKey("RelatedItems"));
            Assert.False(destModel.RelationProperties.ContainsKey("rel_prop_db"));
            Assert.Equal("RelatedItems", destModel.RelationProperties["RelatedItems"].PropertyName);
        }

        #endregion

        #region Interface Prefix Tests

        [Fact]
        public void TransformDatabase_RemoveInterfacePrefix_Enabled()
        {
            var srcDb = CreateSourceDatabase(modelName: "SrcModel", interfaceName: "ISrcModel");
            var destDb = CreateDestinationDatabase(modelName: "dest_model");
            var transformer = new MetadataTransformer(new MetadataTransformerOptions(removeInterfacePrefix: true));

            transformer.TransformDatabase(srcDb, destDb);

            var destModel = destDb.TableModels[0].Model;
            Assert.Equal("SrcModel", destModel.CsType.Name);
        }

        [Fact]
        public void TransformDatabase_RemoveInterfacePrefix_Disabled()
        {
            var srcDb = CreateSourceDatabase(modelName: "ISrcModelAsClass", interfaceName: "ISrcModel");
            var destDb = CreateDestinationDatabase(modelName: "dest_model");
            var transformer = new MetadataTransformer(new MetadataTransformerOptions(removeInterfacePrefix: false));

            transformer.TransformDatabase(srcDb, destDb);

            var destModel = destDb.TableModels[0].Model;
            Assert.Equal("ISrcModelAsClass", destModel.CsType.Name);
        }

        #endregion

        #region Property Type Overwriting Tests

        [Fact]
        public void OverwriteTypes_False_PreservesSourceType()
        {
            var srcDb = CreateSourceDatabase();
            var destDb = CreateDestinationDatabase();
            var transformerOptions = new MetadataTransformerOptions { OverwritePropertyTypes = false };
            var transformer = new MetadataTransformer(transformerOptions);

            transformer.TransformDatabase(srcDb, destDb);

            var transformedProperty = destDb.TableModels[0].Model.ValueProperties["Name"];

            // --- ADDED DETAIL ---
            Assert.Equal("Name", transformedProperty.PropertyName);
            Assert.Equal("string", transformedProperty.CsType.Name);
            Assert.True(transformedProperty.CsNullable, "Nullability from source should be preserved");
            Assert.Equal("col2_db", transformedProperty.Column.DbName);
        }

        [Fact]
        public void OverwriteTypes_True_AppliesDatabaseTypeAndNullability()
        {
            var srcDb = CreateSourceDatabase();
            var destDb = CreateDestinationDatabase();
            var transformerOptions = new MetadataTransformerOptions { OverwritePropertyTypes = true };
            var transformer = new MetadataTransformer(transformerOptions);

            transformer.TransformDatabase(srcDb, destDb);

            var transformedProperty = destDb.TableModels[0].Model.ValueProperties["Name"];

            // --- ADDED DETAIL ---
            Assert.Equal("Name", transformedProperty.PropertyName);
            Assert.Equal("int", transformedProperty.CsType.Name);
            Assert.False(transformedProperty.CsNullable, "Nullability from destination (NOT NULL) should be applied");
            Assert.Equal("col2_db", transformedProperty.Column.DbName);
        }

        [Fact]
        public void OverwriteTypes_True_PreservesEnumType()
        {
            var srcDb = CreateSourceDatabase();
            var destDb = CreateDestinationDatabase();
            var transformerOptions = new MetadataTransformerOptions { OverwritePropertyTypes = true };
            var transformer = new MetadataTransformer(transformerOptions);

            transformer.TransformDatabase(srcDb, destDb);

            var transformedProperty = destDb.TableModels[0].Model.ValueProperties["Status"];

            // --- ADDED DETAIL ---
            Assert.Equal("Status", transformedProperty.PropertyName);
            Assert.Equal("MyStatusEnum", transformedProperty.CsType.Name);
            Assert.NotNull(transformedProperty.EnumProperty);
            Assert.Equal("status_db", transformedProperty.Column.DbName);
        }

        #endregion

        #region Relation Constraint Name Tests

        [Fact]
        public void UpdateConstraintNames_True_AppliesSourceConstraintName()
        {
            var srcDb = CreateSourceDatabase();
            var destDb = CreateDestinationDatabase();
            var transformerOptions = new MetadataTransformerOptions { UpdateConstraintNames = true };
            var transformer = new MetadataTransformer(transformerOptions);

            transformer.TransformDatabase(srcDb, destDb);

            var transformedRelProp = destDb.TableModels[0].Model.RelationProperties["RelatedItems"];

            // --- ADDED DETAIL ---
            Assert.Equal("RelatedItems", transformedRelProp.PropertyName);
            Assert.NotNull(transformedRelProp.RelationPart);
            Assert.Equal("FK_FROM_SOURCE", transformedRelProp.RelationPart.Relation.ConstraintName);
        }

        [Fact]
        public void UpdateConstraintNames_False_PreservesDestinationConstraintName()
        {
            var srcDb = CreateSourceDatabase();
            var destDb = CreateDestinationDatabase();
            var transformerOptions = new MetadataTransformerOptions { UpdateConstraintNames = false };
            var transformer = new MetadataTransformer(transformerOptions);

            transformer.TransformDatabase(srcDb, destDb);

            var transformedRelProp = destDb.TableModels[0].Model.RelationProperties["RelatedItems"];

            // --- ADDED DETAIL ---
            Assert.Equal("RelatedItems", transformedRelProp.PropertyName);
            Assert.NotNull(transformedRelProp.RelationPart);
            Assert.Equal("FK_DestConstraint", transformedRelProp.RelationPart.Relation.ConstraintName);
        }

        #endregion
    }
}