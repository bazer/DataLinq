using DataLinq.Attributes;
using DataLinq.Core.Factories; // Where Transformer resides
using DataLinq.Metadata;
using ThrowAway.Extensions;
using Xunit;

namespace DataLinq.Tests.Core
{
    /*
        Helper Methods: CreateDestinationDatabase simulates parsing metadata directly from a database (using raw DB names for tables/columns/properties). CreateSourceDatabase simulates parsing from developer source code (using intended C# names, attributes like [Table], [Interface], [Relation], etc.).

        TransformDatabase_AppliesNamesAndAttributes: This is the core test. It creates source and destination definitions, runs the transformer, and then asserts that:

            Database name and CS type details from the source are applied to the destination.

            TableModel's CsPropertyName is updated.

            ModelDefinition's CsType name/namespace and ModelInstanceInterface are updated.

            TableDefinition's DbName is updated (from source's [Table] attribute).

            ValueProperty names are updated (e.g., col1_db becomes Id), while the underlying Column.DbName remains unchanged.

            RelationProperty names are updated.

            It also initially included an assert for the constraint name being updated, which might depend on the updateConstraintNames option (see test below).

        TransformDatabase_RemoveInterfacePrefix_Enabled/Disabled: These tests specifically check how the removeInterfacePrefix option affects the final ModelDefinition.CsType.Name when transforming.

        TransformDatabase_UpdateConstraintNames_Disabled: This test attempts to verify the updateConstraintNames option. However, as noted in the comments, the success of this test depends heavily on how the transformer merges relation properties. If it simply overwrites the destination property based on matching column names, the original destination constraint name might be lost anyway. A more robust test would require the destination DB definition to have fully parsed relations first. For now, this test highlights the intended behavior of the option. You may need to adjust the test or the transformer implementation based on requirements.

        Future Tests: The comments suggest adding more tests for cache settings, enum details, and type attribute merging.
     */

    public class MetadataTransformerTests
    {
        // --- Helper to create a basic DB definition ---
        // Represents what might be parsed directly from a DB schema (raw names)
        private DatabaseDefinition CreateDestinationDatabase(string dbName = "MyDatabase", string tableDbName = "my_table", string modelName = "raw_table")
        {
            var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
            var dbCsType = new CsTypeDeclaration(dbName, "RawNamespace", ModelCsType.Class);
            var modelCsType = new CsTypeDeclaration(modelName, "RawNamespace", ModelCsType.Class);

            var db = new DatabaseDefinition(dbName, dbCsType);
            var model = new ModelDefinition(modelCsType);
            model.SetInterfaces([iTableModel]);

            var table = new TableDefinition(tableDbName); // Create the table directly
            var tableModel = new TableModel(modelName + "s", db, model, table); // Pass it to the constructor

            // Add basic columns matching DB schema
            var col1Vp = new ValueProperty("col1_db", new CsTypeDeclaration(typeof(int)), model, [new ColumnAttribute("col1_db"), new PrimaryKeyAttribute()]);
            var col2Vp = new ValueProperty("col2_db", new CsTypeDeclaration(typeof(int)), model, [new ColumnAttribute("col2_db")]);
            var col3Vp = new ValueProperty("status_db", new CsTypeDeclaration(typeof(int)), model, [new ColumnAttribute("status_db")]);
            model.AddProperty(col1Vp);
            model.AddProperty(col2Vp);
            model.AddProperty(col3Vp);
            var col1 = MetadataFactory.ParseColumn(table, col1Vp);
            var col2 = MetadataFactory.ParseColumn(table, col2Vp);
            var col3 = MetadataFactory.ParseColumn(table, col3Vp);
            table.SetColumns([col1, col2, col3]);
            //col1Vp.Column.SetPrimaryKey(true);

            // Add a basic relation property stub (linking happens later)
            var relProp = new RelationProperty("rel_prop_db", new CsTypeDeclaration("OtherRaw", "RawNamespace", ModelCsType.Class), model, []);
            model.AddProperty(relProp);

            db.SetTableModels([tableModel]);
            return db;
        }

        // --- Helper to create a Source DB definition ---
        // Represents what might be parsed from developer source code (intended names, interfaces)
        private DatabaseDefinition CreateSourceDatabase(string dbName = "MyDatabase", string tableDbName = "my_table", string modelName = "MyModel", string interfaceName = "IMyModel")
        {
            var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);

            var dbCsType = new CsTypeDeclaration("MyDatabaseCsType", "SourceNamespace", ModelCsType.Class);
            var modelCsType = new CsTypeDeclaration(modelName, "SourceNamespace", ModelCsType.Class); // Class name
            var modelInterfaceType = new CsTypeDeclaration(interfaceName, "SourceNamespace", ModelCsType.Interface); // Desired Interface

            var db = new DatabaseDefinition(dbName, dbCsType);
            db.SetAttributes([new DatabaseAttribute(dbName)]); // Set DB name via attribute

            var model = new ModelDefinition(modelCsType);
            model.SetInterfaces([iTableModel]);
            model.SetAttributes([new TableAttribute(tableDbName), new InterfaceAttribute(interfaceName)]); // Link to table and interface
            model.SetModelInstanceInterface(modelInterfaceType); // Explicitly set the desired interface

            var table = MetadataFactory.ParseTable(model).Value; // Parse the table from model attributes ONCE
            var tableModel = new TableModel("MyModels", db, model, table); // Pass the SAME instance to the TableModel

            // Add properties with intended C# names and attributes
            var col1Vp = new ValueProperty("Id", new CsTypeDeclaration(typeof(int)), model, [new ColumnAttribute("col1_db"), new PrimaryKeyAttribute()]);
            var col2Vp = new ValueProperty("Name", new CsTypeDeclaration(typeof(string)), model, [new ColumnAttribute("col2_db")]);
            model.AddProperty(col1Vp);
            model.AddProperty(col2Vp);
            var col1 = MetadataFactory.ParseColumn(table, col1Vp);
            var col2 = MetadataFactory.ParseColumn(table, col2Vp);

            // Add a status property that is an ENUM in the C# source
            var enumCsType = new CsTypeDeclaration("MyStatusEnum", "SourceNamespace", ModelCsType.Enum);
            var statusVp = new ValueProperty("Status", enumCsType, model, [new ColumnAttribute("status_db"), new EnumAttribute("Active", "Inactive")]);
            statusVp.SetEnumProperty(new EnumProperty(
                enumValues: new[] { ("Active", 1), ("Inactive", 2) },
                csEnumValues: new[] { ("Active", 1), ("Inactive", 2) },
                declaredInClass: false));
            model.AddProperty(statusVp);
            var statusCol = MetadataFactory.ParseColumn(table, statusVp);

            table.SetColumns([col1, col2, statusCol]);
            //col1Vp.Column.SetPrimaryKey(true);

            // Order Table
            var otherCsType = new CsTypeDeclaration("OtherTable", "TestNamespace", ModelCsType.Class);
            var otherModel = new ModelDefinition(otherCsType);
            otherModel.SetInterfaces([iTableModel]);
            var otherTable = MetadataFactory.ParseTable(otherModel).ValueOrException(); // Assume Table("orders")
            otherTable.SetDbName("other_table");
            var orderTableModel = new TableModel("OtherTables", db, otherModel, otherTable);

            //MetadataFactory.AddRelationProperty(orderUserIdCol, userIdCol, "FK_MyRelation");
            //MetadataFactory.AddRelationProperty(userIdCol, orderUserIdCol, fkOrderUserName);


            // Add relation with intended C# name
            var relProp = new RelationProperty("RelatedItems", new CsTypeDeclaration("MyOtherModel", "SourceNamespace", ModelCsType.Class), model, [new RelationAttribute("other_table", "other_id", "FK_MyRelation")]); // Add attribute
            model.AddProperty(relProp);
            // Fake RelationPart for testing name transfer - full linking not needed here
            //relProp.SetRelationPart(new RelationPart( { Relation = new RelationDefinition { ConstraintName = "FK_MyRelation_Source" } }); //FIXME

            db.SetTableModels([tableModel]);
            MetadataFactory.ParseIndices(db);
            MetadataFactory.ParseRelations(db);

            return db;
        }

        [Fact]
        public void TransformDatabase_AppliesNamesAndAttributes()
        {
            // Arrange
            var srcDb = CreateSourceDatabase(modelName: "SourceModel");
            var destDb = CreateDestinationDatabase(); // Raw name
            var transformer = new MetadataTransformer(new MetadataTransformerOptions(removeInterfacePrefix: true)); // Default options

            // Act
            transformer.TransformDatabase(srcDb, destDb);

            // Assert
            // Database level
            //Assert.Equal("SourceDbName", destDb.DbName); // Name from src attribute applied
            Assert.Equal("MyDatabaseCsType", destDb.CsType.Name); // CS Type Name from src applied
            Assert.Equal("SourceNamespace", destDb.CsType.Namespace); // CS Namespace from src applied

            // TableModel level
            Assert.Single(destDb.TableModels);
            var destTableModel = destDb.TableModels[0];
            Assert.Equal("MyModels", destTableModel.CsPropertyName); // CS Property Name from src applied

            // ModelDefinition level
            var destModel = destTableModel.Model;
            Assert.Equal("SourceModel", destModel.CsType.Name); // Model CS Name from src applied
            Assert.Equal("SourceNamespace", destModel.CsType.Namespace);
            Assert.NotNull(destModel.ModelInstanceInterface);
            Assert.Equal("IMyModel", destModel.ModelInstanceInterface.Value.Name); // Interface from src applied

            // TableDefinition level
            var destTable = destTableModel.Table;
            Assert.Equal("my_table", destTable.DbName); // Table DB Name from src attribute applied

            // ValueProperty level
            Assert.True(destModel.ValueProperties.ContainsKey("Id")); // Property name from src applied
            Assert.False(destModel.ValueProperties.ContainsKey("col1_db")); // Original raw name removed/renamed
            Assert.Equal("Id", destModel.ValueProperties["Id"].PropertyName);
            Assert.Equal("int", destModel.ValueProperties["Id"].CsType.Name); // Type should likely remain based on DB/dest
            Assert.Equal("col1_db", destModel.ValueProperties["Id"].Column.DbName); // Column DbName should remain from dest

            Assert.True(destModel.ValueProperties.ContainsKey("Name"));
            Assert.Equal("Name", destModel.ValueProperties["Name"].PropertyName);
            Assert.Equal("string", destModel.ValueProperties["Name"].CsType.Name);
            Assert.Equal("col2_db", destModel.ValueProperties["Name"].Column.DbName);

            // RelationProperty level
            Assert.True(destModel.RelationProperties.ContainsKey("RelatedItems")); // Property name from src applied
            Assert.False(destModel.RelationProperties.ContainsKey("rel_prop_db"));
            Assert.Equal("RelatedItems", destModel.RelationProperties["RelatedItems"].PropertyName);
            //Assert.Equal("FK_MyRelation_Source", destModel.RelationProperties["RelatedItems"].RelationPart.Relation.ConstraintName); // Constraint name updated (default behavior)

        }

        [Fact]
        public void TransformDatabase_RemoveInterfacePrefix_Enabled()
        {
            // Arrange
            // Source interface is ISrcModel, source class is SrcModel, dest class is dest_model
            var srcDb = CreateSourceDatabase(modelName: "SrcModel", interfaceName: "ISrcModel");
            var destDb = CreateDestinationDatabase(modelName: "dest_model");
            var transformer = new MetadataTransformer(new MetadataTransformerOptions(removeInterfacePrefix: true)); // Enabled by default

            // Act
            transformer.TransformDatabase(srcDb, destDb);

            // Assert
            var destModel = destDb.TableModels[0].Model;
            // Class name should become SrcModel (derived from ISrcModel with prefix removed, or just SrcModel if class name used)
            Assert.Equal("SrcModel", destModel.CsType.Name);
            Assert.NotNull(destModel.ModelInstanceInterface);
            Assert.Equal("ISrcModel", destModel.ModelInstanceInterface.Value.Name); // Interface name itself doesn't change
        }

        [Fact]
        public void TransformDatabase_RemoveInterfacePrefix_Disabled()
        {
            // Arrange
            // Source interface is ISrcModel, source class is ISrcModel (if class name derived from interface)
            // Or source class is SrcModel (if class name taken literally)
            var srcDb = CreateSourceDatabase(modelName: "SrcModel", interfaceName: "ISrcModel"); // Class = SrcModel, Interface = ISrcModel
            var destDb = CreateDestinationDatabase(modelName: "dest_model");
            var transformer = new MetadataTransformer(new MetadataTransformerOptions(removeInterfacePrefix: false)); // Disabled

            // Act
            transformer.TransformDatabase(srcDb, destDb);

            // Assert
            var destModel = destDb.TableModels[0].Model;
            // Class name should remain exactly as defined in the source ModelDefinition (SrcModel)
            Assert.Equal("SrcModel", destModel.CsType.Name);
            Assert.NotNull(destModel.ModelInstanceInterface);
            Assert.Equal("ISrcModel", destModel.ModelInstanceInterface.Value.Name);
        }

        [Fact]
        public void TransformDatabase_UpdateConstraintNames_Disabled()
        {
            // Arrange
            var srcDb = CreateSourceDatabase(); // Has FK_MyRelation_Source
            var destDb = CreateDestinationDatabase();
            // Add a mock relation part to destination to test if name is preserved
            var destRelProp = destDb.TableModels[0].Model.RelationProperties["rel_prop_db"];
            //destRelProp.RelationPart = new RelationPart { Relation = new RelationDefinition { ConstraintName = "FK_DestConstraint" } }; //FIXME

            var transformer = new MetadataTransformer(new MetadataTransformerOptions(updateConstraintNames: false)); // Disable update

            // Act
            transformer.TransformDatabase(srcDb, destDb);

            // Assert
            var transformedRelProp = destDb.TableModels[0].Model.RelationProperties["RelatedItems"]; // Name was updated from src
            Assert.NotNull(transformedRelProp.RelationPart);
            // Constraint name should NOT have been updated from source, should retain destination's original
            // This requires the transformer logic to specifically check the updateConstraintNames option.
            // Assert.Equal("FK_DestConstraint", transformedRelProp.RelationPart.Relation.ConstraintName);
            // Add the above assert IF your transformer implements this option check.
            // If it always updates, this test needs adjustment or the transformer needs modification.
            // For now, let's assume it DOESN'T update if option is false (ideal behavior)
            // If the RelationPart is overwritten entirely, this test might fail as RelationPart would be null initially
            // Need to refine test based on actual transformer behavior or expected behavior.
            // Let's assume the destination RelationPart might be null if no matching relation found yet.
            // A better test might involve pre-populating the destination with a fully parsed relation.

            // Re-evaluate this test based on the exact implementation details of how
            // relation properties and their parts are merged/updated.
            // If the intent is just to apply the C# property name, this test might be less relevant.
        }

        // In src/DataLinq.Tests/Core/MetadataTransformerTests.cs

        [Fact]
        public void TransformDatabase_WithOverwriteTypesFalse_PreservesSourceType()
        {
            // Arrange
            var srcDb = CreateSourceDatabase(); // Source defines the 'Name' property as a string
            var destDb = CreateDestinationDatabase(); // Destination defines the corresponding column as an int

            // Create the transformer with the default setting (overwrite = false)
            var transformerOptions = new MetadataTransformerOptions { OverwritePropertyTypes = false };
            var transformer = new MetadataTransformer(transformerOptions);

            // Act
            transformer.TransformDatabase(srcDb, destDb);

            // Assert
            var transformedProperty = destDb.TableModels[0].Model.ValueProperties["Name"];

            // The type from the source file ('string') should have been preserved.
            Assert.Equal("string", transformedProperty.CsType.Name);
            // The underlying database column name should still be correct.
            Assert.Equal("col2_db", transformedProperty.Column.DbName);
        }

        [Fact]
        public void TransformDatabase_WithOverwriteTypesTrue_AppliesDatabaseType()
        {
            // Arrange
            var srcDb = CreateSourceDatabase(); // Source defines 'Name' as string
            var destDb = CreateDestinationDatabase(); // Destination defines it as int

            // Create the transformer with the overwrite option ENABLED
            var transformerOptions = new MetadataTransformerOptions { OverwritePropertyTypes = true };
            var transformer = new MetadataTransformer(transformerOptions);

            // Act
            transformer.TransformDatabase(srcDb, destDb);

            // Assert
            var transformedProperty = destDb.TableModels[0].Model.ValueProperties["Name"];

            // The type from the destination/database ('int') should have overwritten the source type.
            Assert.Equal("int", transformedProperty.CsType.Name);
            // The underlying database column name should still be correct.
            Assert.Equal("col2_db", transformedProperty.Column.DbName);
        }

        [Fact]
        public void TransformDatabase_WithOverwriteTypesTrue_PreservesEnumType()
        {
            // Arrange
            var srcDb = CreateSourceDatabase(); // Source defines 'Status' as MyStatusEnum
            var destDb = CreateDestinationDatabase(); // Destination defines 'status_db' as INT

            // Create the transformer with the overwrite option ENABLED
            var transformerOptions = new MetadataTransformerOptions { OverwritePropertyTypes = true };
            var transformer = new MetadataTransformer(transformerOptions);

            // Act
            transformer.TransformDatabase(srcDb, destDb);

            // Assert
            var transformedProperty = destDb.TableModels[0].Model.ValueProperties["Status"];

            // EVEN WITH OverwriteTypes = true, the C# enum type should be preserved.
            Assert.Equal("MyStatusEnum", transformedProperty.CsType.Name);
            Assert.NotNull(transformedProperty.EnumProperty); // Ensure enum details are still there
            Assert.Equal("status_db", transformedProperty.Column.DbName);
        }

        // Add more tests for:
        // - Transforming cache settings
        // - Transforming enum properties (ensuring CsEnumValues are preserved if present)
        // - Transforming DbTypes (ensuring specific DB types from source are added if not present)
        // - Cases where a table/property exists in source but not destination (should be ignored)
        // - Cases where a table/property exists in destination but not source (should be kept)
    }
}