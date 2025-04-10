using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using ThrowAway.Extensions;
using Xunit;

namespace DataLinq.Tests.Core; // Note the namespace includes .Core

public class MetadataFactoryTests
{
    // Helper to create a basic structure for testing
    private (DatabaseDefinition db, TableModel tableModel, ModelDefinition model, TableDefinition table) CreateTestHierarchy(string dbName = "TestDb", string tableName = "TestTable", string modelName = "TestModel", bool isView = false)
    {
        var iTableModel = new CsTypeDeclaration("ITableModel", "TestNamespace", ModelCsType.Interface);
        var iViewModel = new CsTypeDeclaration("IViewModel", "TestNamespace", ModelCsType.Interface);
        var dbCsType = new CsTypeDeclaration(dbName, "TestNamespace", ModelCsType.Class);
        var modelCsType = new CsTypeDeclaration(modelName, "TestNamespace", ModelCsType.Class);

        var db = new DatabaseDefinition(dbName, dbCsType);
        var model = new ModelDefinition(modelCsType);
        if (isView)
            model.SetInterfaces([iViewModel]); // Set as view if needed
        else
            model.SetInterfaces([iTableModel]); // Set as table

        // ParseTable creates the TableDefinition internally based on ModelDefinition attributes
        var table = MetadataFactory.ParseTable(model);
        var tableModel = new TableModel(modelName + "s", db, model, table); // Links model and table

        db.SetTableModels([tableModel]); // Link tableModel back to db

        return (db, tableModel, model, table);
    }

    // Helper to create a value property linked to a model
    private ValueProperty CreateTestValueProperty(ModelDefinition model, string propName, Type csType, List<Attribute> attributes)
    {
        var csTypeDecl = new CsTypeDeclaration(csType);
        var vp = new ValueProperty(propName, csTypeDecl, model, attributes);
        model.AddProperty(vp); // Add property to model
        return vp;
    }

    // --- Helper Method for Multi-Table Setup ---

    // Creates a DatabaseDefinition with two tables: Users (PK UserId) and Orders (PK OrderId, FK UserId)
    private DatabaseDefinition CreateMultiTableDatabaseForRelationTests()
    {
        var iTableModel = new CsTypeDeclaration("ITableModel", "TestNamespace", ModelCsType.Interface);
        var iViewModel = new CsTypeDeclaration("IViewModel", "TestNamespace", ModelCsType.Interface);
        var dbCsType = new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class);
        var userCsType = new CsTypeDeclaration("User", "TestNamespace", ModelCsType.Class);
        var orderCsType = new CsTypeDeclaration("Order", "TestNamespace", ModelCsType.Class);

        var db = new DatabaseDefinition("TestDb", dbCsType);

        // User Table
        var userModel = new ModelDefinition(userCsType);
        userModel.SetInterfaces([iTableModel]);
        var userTable = MetadataFactory.ParseTable(userModel).ValueOrException(); // Assume Table("users") if needed
        userTable.SetDbName("users"); // Set explicitly if not via attribute
        var userTableModel = new TableModel("Users", db, userModel, userTable);
        var userIdVp = CreateTestValueProperty(userModel, "UserId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("user_id")]);
        var userNameVp = CreateTestValueProperty(userModel, "UserName", typeof(string), [new ColumnAttribute("user_name")]);
        MetadataFactory.ParseColumn(userTable, userIdVp);
        MetadataFactory.ParseColumn(userTable, userNameVp);
        // Manually link columns back if not done in helper
        userTable.SetColumns([userIdVp.Column, userNameVp.Column]);

        // Order Table
        var orderModel = new ModelDefinition(orderCsType);
        orderModel.SetInterfaces([iTableModel]);
        var orderTable = MetadataFactory.ParseTable(orderModel).ValueOrException(); // Assume Table("orders")
        orderTable.SetDbName("orders");
        var orderTableModel = new TableModel("Orders", db, orderModel, orderTable);
        var orderIdVp = CreateTestValueProperty(orderModel, "OrderId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("order_id")]);
        var orderUserIdVp = CreateTestValueProperty(orderModel, "CustomerId", typeof(int), [new ForeignKeyAttribute("users", "user_id", "FK_Order_User"), new ColumnAttribute("customer_id")]);
        var orderAmountVp = CreateTestValueProperty(orderModel, "Amount", typeof(decimal), [new ColumnAttribute("amount")]);
        MetadataFactory.ParseColumn(userTable, orderIdVp);
        MetadataFactory.ParseColumn(userTable, orderUserIdVp);
        MetadataFactory.ParseColumn(userTable, orderAmountVp);
        // Manually link columns back
        orderTable.SetColumns([orderIdVp.Column, orderUserIdVp.Column, orderAmountVp.Column]);
        orderUserIdVp.Column.SetForeignKey(true); // Ensure FK is set

        // Add RelationProperties (normally done by factory parsing RelationAttribute)
        // Order -> User (Many-to-One)
        var orderToUserRelProp = new RelationProperty("User", userCsType, orderModel, [new RelationAttribute("users", "user_id", "FK_Order_User")]);
        orderModel.AddProperty(orderToUserRelProp);
        // User -> Orders (One-to-Many)
        var userToOrdersRelProp = new RelationProperty("Orders", orderCsType, userModel, [new RelationAttribute("orders", "customer_id", "FK_Order_User")]); // Relation points *to* the FK column
        userModel.AddProperty(userToOrdersRelProp);


        db.SetTableModels([userTableModel, orderTableModel]);
        return db;
    }

    [Fact]
    public void TestParseTableAttribute()
    {
        // Arrange
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new TableAttribute("my_table")]);

        // Act
        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException(); // Re-parse with attribute

        // Assert
        Assert.Equal("my_table", tableDefinition.DbName);
        Assert.Equal(TableType.Table, tableDefinition.Type);
    }

    [Fact]
    public void TestParseViewAttribute()
    {
        // Arrange
        var (_, _, model, _) = CreateTestHierarchy(isView: true);
        model.SetAttributes([new ViewAttribute("my_view")]);

        // Act
        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException();

        // Assert
        Assert.Equal("my_view", tableDefinition.DbName);
        Assert.IsType<ViewDefinition>(tableDefinition);
        Assert.Equal(TableType.View, tableDefinition.Type);
    }

    [Fact]
    public void TestParseDefinitionAttribute()
    {
        // Arrange
        var (_, _, model, _) = CreateTestHierarchy(isView: true);
        var definitionSql = "SELECT Id FROM OtherTable";
        model.SetAttributes([new ViewAttribute("my_view"), new DefinitionAttribute(definitionSql)]);


        // Act
        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException();

        // Assert
        Assert.IsType<ViewDefinition>(tableDefinition);
        var viewDefinition = (ViewDefinition)tableDefinition;
        Assert.Equal(definitionSql, viewDefinition.Definition);
    }


    [Fact]
    public void TestParseColumnAttribute()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var vp = CreateTestValueProperty(model, "CsPropName", typeof(string), [new ColumnAttribute("db_col_name")]);

        // Act
        var columnDefinition = table.ParseColumn(vp); // This method links vp and col

        // Assert
        Assert.Equal("db_col_name", columnDefinition.DbName);
        Assert.Same(vp, columnDefinition.ValueProperty);
        Assert.Same(columnDefinition, vp.Column); // Check back-link
    }

    [Fact]
    public void TestParsePrimaryKeyAttribute()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var vp = CreateTestValueProperty(model, "Id", typeof(int), [new PrimaryKeyAttribute()]);

        // Act
        var columnDefinition = table.ParseColumn(vp);
        // Need to manually add the parsed column to the table's collection for PK check
        table.SetColumns([columnDefinition]);

        // Assert
        Assert.True(columnDefinition.PrimaryKey);
        Assert.Single(table.PrimaryKeyColumns);
        Assert.Same(columnDefinition, table.PrimaryKeyColumns[0]);
    }

    [Fact]
    public void TestParseAutoIncrementAttribute()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var vp = CreateTestValueProperty(model, "Id", typeof(int), [new AutoIncrementAttribute()]);

        // Act
        var columnDefinition = table.ParseColumn(vp);

        // Assert
        Assert.True(columnDefinition.AutoIncrement);
    }

    [Fact]
    public void TestParseNullableAttribute()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var vp = CreateTestValueProperty(model, "Name", typeof(string), [new NullableAttribute()]);

        // Act
        var columnDefinition = table.ParseColumn(vp);

        // Assert
        Assert.True(columnDefinition.Nullable);
    }

    [Fact]
    public void TestParseTypeAttribute_SpecificDb()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var attribute = new TypeAttribute(DatabaseType.MySQL, "VARCHAR", 255);
        var vp = CreateTestValueProperty(model, "Name", typeof(string), [attribute]);

        // Act
        var columnDefinition = table.ParseColumn(vp);

        // Assert
        Assert.Single(columnDefinition.DbTypes);
        var dbType = columnDefinition.DbTypes[0];
        Assert.Equal(DatabaseType.MySQL, dbType.DatabaseType);
        Assert.Equal("VARCHAR", dbType.Name);
        Assert.Equal(255, dbType.Length);
        Assert.Null(dbType.Decimals);
        Assert.Null(dbType.Signed);
    }

    [Fact]
    public void TestParseTypeAttribute_DefaultDb()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        // Example: A generic type without specifying DB
        var attribute = new TypeAttribute("TEXT");
        var vp = CreateTestValueProperty(model, "Description", typeof(string), [attribute]);

        // Act
        var columnDefinition = table.ParseColumn(vp);

        // Assert
        Assert.Single(columnDefinition.DbTypes);
        var dbType = columnDefinition.DbTypes[0];
        Assert.Equal(DatabaseType.Default, dbType.DatabaseType); // Default type assumed
        Assert.Equal("TEXT", dbType.Name);
        Assert.Null(dbType.Length); // Length might be implicit for TEXT
    }

    [Fact]
    public void TestParseTypeAttribute_LengthAndDecimals()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var attribute = new TypeAttribute(DatabaseType.MySQL, "DECIMAL", 10, 2);
        var vp = CreateTestValueProperty(model, "Price", typeof(decimal), [attribute]);

        // Act
        var columnDefinition = table.ParseColumn(vp);

        // Assert
        Assert.Single(columnDefinition.DbTypes);
        var dbType = columnDefinition.DbTypes[0];
        Assert.Equal(DatabaseType.MySQL, dbType.DatabaseType);
        Assert.Equal("DECIMAL", dbType.Name);
        Assert.Equal(10, dbType.Length);
        Assert.Equal(2, dbType.Decimals);
    }

    [Fact]
    public void TestParseTypeAttribute_Signed()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var attribute = new TypeAttribute(DatabaseType.MySQL, "INT", false); // Unsigned INT
        var vp = CreateTestValueProperty(model, "Counter", typeof(uint), [attribute]);

        // Act
        var columnDefinition = table.ParseColumn(vp);

        // Assert
        Assert.Single(columnDefinition.DbTypes);
        var dbType = columnDefinition.DbTypes[0];
        Assert.Equal(DatabaseType.MySQL, dbType.DatabaseType);
        Assert.Equal("INT", dbType.Name);
        Assert.False(dbType.Signed);
    }

    [Fact]
    public void TestParseEnumAttribute()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        // Note: The C# type needs to be correctly identified as an enum later,
        // but the attribute itself just holds string values.
        // Let's pretend the property type handling assigns Enum CsType.
        var enumCsType = new CsTypeDeclaration("MyEnum", "TestNamespace", ModelCsType.Enum);
        var attributes = new List<Attribute> { new EnumAttribute("Value1", "Value2") };
        var vp = new ValueProperty("Status", enumCsType, model, attributes);
        // Manually setting EnumProperty as it's normally set by specific factories
        vp.SetEnumProperty(new EnumProperty(enumValues: attributes.OfType<EnumAttribute>().First().Values.Select((name, index) => (name, index + 1)).ToList()));
        model.AddProperty(vp);
        var columnDefinition = table.ParseColumn(vp); // Re-parse column with EnumProperty set

        // Act
        // Parsing happens implicitly in Arrange when creating vp and columnDefinition

        // Assert
        Assert.NotNull(vp.EnumProperty);
        Assert.Equal(2, vp.EnumProperty.Value.EnumValues.Count);
        Assert.Equal("Value1", vp.EnumProperty.Value.EnumValues[0].name);
        Assert.Equal(1, vp.EnumProperty.Value.EnumValues[0].value); // Assuming 1-based index
        Assert.Equal("Value2", vp.EnumProperty.Value.EnumValues[1].name);
        Assert.Equal(2, vp.EnumProperty.Value.EnumValues[1].value);
    }

    [Fact]
    public void TestParseDefaultValueAttribute_String()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var defaultValue = "DefaultName";
        var attributes = new List<Attribute> { new DefaultAttribute(defaultValue) };
        var vp = CreateTestValueProperty(model, "Name", typeof(string), attributes);

        // Act
        var columnDefinition = table.ParseColumn(vp);

        // Assert
        Assert.True(vp.HasDefaultValue());
        var defaultAttr = vp.GetDefaultAttribute();
        Assert.NotNull(defaultAttr);
        Assert.Equal(defaultValue, defaultAttr.Value);
    }

    [Fact]
    public void TestParseDefaultValueAttribute_Int()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var defaultValue = 123;
        var attributes = new List<Attribute> { new DefaultAttribute(defaultValue) };
        var vp = CreateTestValueProperty(model, "Count", typeof(int), attributes);

        // Act
        var columnDefinition = table.ParseColumn(vp);

        // Assert
        Assert.True(vp.HasDefaultValue());
        var defaultAttr = vp.GetDefaultAttribute();
        Assert.NotNull(defaultAttr);
        Assert.Equal(defaultValue, defaultAttr.Value);
    }

    [Fact]
    public void TestParseDefaultCurrentTimestampAttribute()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var attributes = new List<Attribute> { new DefaultCurrentTimestampAttribute() };
        var vp = CreateTestValueProperty(model, "CreatedAt", typeof(DateTime), attributes);

        // Act
        var columnDefinition = table.ParseColumn(vp);

        // Assert
        Assert.True(vp.HasDefaultValue());
        var defaultAttr = vp.GetDefaultAttribute();
        Assert.NotNull(defaultAttr);
        Assert.IsType<DefaultCurrentTimestampAttribute>(defaultAttr);
        Assert.Equal(DynamicFunctions.CurrentTimestamp, defaultAttr.Value);
    }

    [Fact]
    public void TestGetDefaultValue_Timestamp()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var attributes = new List<Attribute> { new DefaultCurrentTimestampAttribute() };
        var vp = CreateTestValueProperty(model, "CreatedAt", typeof(DateTime), attributes);
        table.ParseColumn(vp); // Ensure column is parsed

        // Act
        var defaultValueString = vp.GetDefaultValue();

        // Assert
        Assert.Equal("DateTime.Now", defaultValueString); // Or specific DB function name if formatting changes
    }

    [Fact]
    public void TestGetDefaultValue_String()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var attributes = new List<Attribute> { new DefaultAttribute("MyDefault") };
        var vp = CreateTestValueProperty(model, "Name", typeof(string), attributes);
        table.ParseColumn(vp);

        // Act
        var defaultValueString = vp.GetDefaultValue();

        // Assert
        Assert.Equal("MyDefault", defaultValueString);
    }

    [Fact]
    public void TestParseForeignKeyAttribute()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var attribute = new ForeignKeyAttribute("ReferencedTable", "ReferencedColumn", "FK_ConstraintName");
        var vp = CreateTestValueProperty(model, "ReferenceId", typeof(int), [attribute]);

        // Act
        var columnDefinition = table.ParseColumn(vp);

        // Assert
        Assert.True(columnDefinition.ForeignKey);
        // We can't fully test the relation linking here, as that happens in ParseRelations.
        // We just check that the attribute is present on the ValueProperty.
        var fkAttribute = vp.Attributes.OfType<ForeignKeyAttribute>().Single();
        Assert.Equal("ReferencedTable", fkAttribute.Table);
        Assert.Equal("ReferencedColumn", fkAttribute.Column);
        Assert.Equal("FK_ConstraintName", fkAttribute.Name);
    }

    [Fact]
    public void TestParseIndexAttribute_Simple()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var attribute = new IndexAttribute("idx_name", IndexCharacteristic.Simple, IndexType.BTREE); // Simple B-Tree index
        var vp = CreateTestValueProperty(model, "Name", typeof(string), [attribute]);

        // Act
        var columnDefinition = table.ParseColumn(vp);
        // Need to explicitly call ParseIndices to populate ColumnIndices on the table
        table.SetColumns([columnDefinition]); // Make sure column is part of the table
        MetadataFactory.ParseIndices(model.Database);

        // Assert
        var indexAttribute = vp.Attributes.OfType<IndexAttribute>().Single();
        Assert.Equal("idx_name", indexAttribute.Name);
        Assert.Equal(IndexCharacteristic.Simple, indexAttribute.Characteristic);
        Assert.Equal(IndexType.BTREE, indexAttribute.Type);
        Assert.Empty(indexAttribute.Columns); // Columns specified here are for multi-column index definitions

        // Verify the ColumnIndex object created on the TableDefinition
        Assert.Single(table.ColumnIndices);
        var columnIndex = table.ColumnIndices[0];
        Assert.Equal("idx_name", columnIndex.Name);
        Assert.Equal(IndexCharacteristic.Simple, columnIndex.Characteristic);
        Assert.Equal(IndexType.BTREE, columnIndex.Type);
        Assert.Single(columnIndex.Columns);
        Assert.Same(columnDefinition, columnIndex.Columns[0]);
    }

    [Fact]
    public void TestParseIndexAttribute_Unique()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy();
        var attribute = new IndexAttribute("uq_email", IndexCharacteristic.Unique); // Unique index
        var vp = CreateTestValueProperty(model, "Email", typeof(string), [attribute]);

        // Act
        var columnDefinition = table.ParseColumn(vp);
        table.SetColumns([columnDefinition]);
        MetadataFactory.ParseIndices(model.Database);

        // Assert
        var indexAttribute = vp.Attributes.OfType<IndexAttribute>().Single();
        Assert.Equal(IndexCharacteristic.Unique, indexAttribute.Characteristic);
        Assert.Equal(IndexType.BTREE, indexAttribute.Type); // Default type

        Assert.Single(table.ColumnIndices);
        var columnIndex = table.ColumnIndices[0];
        Assert.Equal("uq_email", columnIndex.Name);
        Assert.Equal(IndexCharacteristic.Unique, columnIndex.Characteristic);
        Assert.Single(columnIndex.Columns);
    }

    // We will test multi-column indices more thoroughly in TestParseIndices_MultiColumn

    [Fact]
    public void TestParseRelationAttribute()
    {
        // Arrange
        var (_, _, model, _) = CreateTestHierarchy();
        var attributes = new List<Attribute> { new RelationAttribute("OtherTable", "OtherId", "FK_RelationName") };
        // Create a mock RelationProperty (usually created by factory)
        var relationProperty = new RelationProperty("RelatedItems", new CsTypeDeclaration(typeof(IEnumerable<>)), model, attributes);
        model.AddProperty(relationProperty);

        // Act
        // Parsing happens when creating the RelationProperty

        // Assert
        var relationAttribute = relationProperty.Attributes.OfType<RelationAttribute>().Single();
        Assert.Equal("OtherTable", relationAttribute.Table);
        Assert.Single(relationAttribute.Columns);
        Assert.Equal("OtherId", relationAttribute.Columns[0]);
        Assert.Equal("FK_RelationName", relationAttribute.Name);
        // Full relation linking is tested in TestParseRelations_*
    }

    [Fact]
    public void TestParseUseCacheAttribute_Table()
    {
        // Arrange
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new TableAttribute("my_table"), new UseCacheAttribute(true)]);

        // Act
        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException();

        // Assert
        Assert.True(tableDefinition.explicitUseCache.HasValue);
        Assert.True(tableDefinition.explicitUseCache.Value);
        // Note: tableDefinition.UseCache depends on the DatabaseDefinition's setting if explicitUseCache is null
    }

    [Fact]
    public void TestParseUseCacheAttribute_Database()
    {
        // Arrange
        var (db, _, _, _) = CreateTestHierarchy();
        db.SetAttributes([new UseCacheAttribute(true)]);

        // Act
        db.ParseAttributes(); // Process database attributes

        // Assert
        Assert.True(db.UseCache);
    }

    [Fact]
    public void TestParseCacheLimitAttribute_Table()
    {
        // Arrange
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new TableAttribute("my_table"), new CacheLimitAttribute(CacheLimitType.Rows, 1000)]);

        // Act
        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException();

        // Assert
        Assert.Single(tableDefinition.CacheLimits);
        Assert.Equal(CacheLimitType.Rows, tableDefinition.CacheLimits[0].limitType);
        Assert.Equal(1000, tableDefinition.CacheLimits[0].amount);
    }

    [Fact]
    public void TestParseCacheLimitAttribute_Database()
    {
        // Arrange
        var (db, _, _, _) = CreateTestHierarchy();
        db.SetAttributes([new CacheLimitAttribute(CacheLimitType.Megabytes, 512)]);

        // Act
        db.ParseAttributes();

        // Assert
        Assert.Single(db.CacheLimits);
        Assert.Equal(CacheLimitType.Megabytes, db.CacheLimits[0].limitType);
        Assert.Equal(512, db.CacheLimits[0].amount);
    }

    [Fact]
    public void TestParseIndexCacheAttribute_Table()
    {
        // Arrange
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new TableAttribute("my_table"), new IndexCacheAttribute(IndexCacheType.MaxAmountRows, 5000)]);

        // Act
        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException();

        // Assert
        Assert.Single(tableDefinition.IndexCache);
        Assert.Equal(IndexCacheType.MaxAmountRows, tableDefinition.IndexCache[0].indexCacheType);
        Assert.Equal(5000, tableDefinition.IndexCache[0].amount);
    }

    [Fact]
    public void TestParseIndexCacheAttribute_Database()
    {
        // Arrange
        var (db, _, _, _) = CreateTestHierarchy();
        db.SetAttributes([new IndexCacheAttribute(IndexCacheType.All)]);

        // Act
        db.ParseAttributes();

        // Assert
        Assert.Single(db.IndexCache);
        Assert.Equal(IndexCacheType.All, db.IndexCache[0].indexCacheType);
        Assert.Null(db.IndexCache[0].amount);
    }

    [Fact]
    public void TestParseCacheCleanupAttribute_Database()
    {
        // Arrange
        var (db, _, _, _) = CreateTestHierarchy();
        db.SetAttributes([new CacheCleanupAttribute(CacheCleanupType.Hours, 1)]);

        // Act
        db.ParseAttributes();

        // Assert
        Assert.Single(db.CacheCleanup);
        Assert.Equal(CacheCleanupType.Hours, db.CacheCleanup[0].cleanupType);
        Assert.Equal(1, db.CacheCleanup[0].amount);
    }

    [Fact]
    public void TestParseInterfaceAttribute_Simple()
    {
        // Arrange
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new InterfaceAttribute()]); // Generate default interface ITestModel

        // Act
        // The attribute is stored on the ModelDefinition, actual interface type isn't created here
        // but factories like MetadataFromFileFactory use this info.

        // Assert
        var interfaceAttribute = model.Attributes.OfType<InterfaceAttribute>().Single();
        Assert.True(interfaceAttribute.GenerateInterface);
        Assert.Null(interfaceAttribute.Name); // Name is null, implies default generation
    }

    [Fact]
    public void TestParseInterfaceAttribute_Named()
    {
        // Arrange
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new InterfaceAttribute("IMyCustomInterface")]);

        // Act
        // Parsing happens in Arrange

        // Assert
        var interfaceAttribute = model.Attributes.OfType<InterfaceAttribute>().Single();
        Assert.True(interfaceAttribute.GenerateInterface);
        Assert.Equal("IMyCustomInterface", interfaceAttribute.Name);
    }

    [Fact]
    public void TestParseInterfaceAttribute_Generic()
    {
        // Arrange
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new InterfaceAttribute<IDisposable>()]); // Name derived from typeof(T)

        // Act
        // Parsing happens in Arrange

        // Assert
        var interfaceAttribute = model.Attributes.OfType<InterfaceAttribute<IDisposable>>().Single();
        Assert.True(interfaceAttribute.GenerateInterface);
        Assert.Equal("IDisposable", interfaceAttribute.Name);
    }

    [Fact]
    public void TestParseInterfaceAttribute_NoGenerate()
    {
        // Arrange
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new InterfaceAttribute(generateInterface: false)]);

        // Act
        // Parsing happens in Arrange

        // Assert
        var interfaceAttribute = model.Attributes.OfType<InterfaceAttribute>().Single();
        Assert.False(interfaceAttribute.GenerateInterface);
        Assert.Null(interfaceAttribute.Name);
    }

    [Fact]
    public void TestParseIndices_MultiColumn()
    {
        // Arrange
        var (_, _, model, table) = CreateTestHierarchy(tableName: "MultiIndexTable");
        // Index spanning two columns
        var attributesCol1 = new List<Attribute> { new IndexAttribute("idx_multi", IndexCharacteristic.Simple, IndexType.BTREE, "col1", "col2"), new ColumnAttribute("col1") };
        var attributesCol2 = new List<Attribute> { new IndexAttribute("idx_multi", IndexCharacteristic.Simple, IndexType.BTREE, "col1", "col2"), new ColumnAttribute("col2") };
        var vp1 = CreateTestValueProperty(model, "Prop1", typeof(string), attributesCol1);
        var vp2 = CreateTestValueProperty(model, "Prop2", typeof(int), attributesCol2);
        // Update table columns after creating properties
        table.SetColumns([vp1.Column, vp2.Column]);

        // Act
        MetadataFactory.ParseIndices(model.Database);

        // Assert
        Assert.Single(table.ColumnIndices);
        var index = table.ColumnIndices[0];
        Assert.Equal("idx_multi", index.Name);
        Assert.Equal(2, index.Columns.Count);
        Assert.Contains(vp1.Column, index.Columns);
        Assert.Contains(vp2.Column, index.Columns);
        // Check order if specified/important (depends on how ParseIndices stores them)
        Assert.Same(vp1.Column, index.Columns.Single(c => c.DbName == "col1"));
        Assert.Same(vp2.Column, index.Columns.Single(c => c.DbName == "col2"));
    }

    [Fact]
    public void TestParseRelations_OneToMany_And_BackReference()
    {
        // Arrange
        var db = CreateMultiTableDatabaseForRelationTests();
        var userTable = db.TableModels.Single(tm => tm.Table.DbName == "users").Table;
        var orderTable = db.TableModels.Single(tm => tm.Table.DbName == "orders").Table;
        var userPkCol = userTable.Columns.Single(c => c.DbName == "user_id");
        var orderFkCol = orderTable.Columns.Single(c => c.DbName == "customer_id");

        // Act
        MetadataFactory.ParseRelations(db); // This should create RelationDefinitions and link RelationParts

        // Assert
        // Check Order -> User relationship (Many-to-One)
        var orderToUserRelProp = orderTable.Model.RelationProperties["User"];
        Assert.NotNull(orderToUserRelProp.RelationPart);
        Assert.Equal(RelationPartType.ForeignKey, orderToUserRelProp.RelationPart.Type); // Order side is FK
        Assert.Equal("FK_Order_User", orderToUserRelProp.RelationPart.Relation.ConstraintName);
        Assert.Same(orderFkCol, orderToUserRelProp.RelationPart.ColumnIndex.Columns.Single()); // FK Col Index

        var orderUserCandidatePart = orderToUserRelProp.RelationPart.GetOtherSide();
        Assert.Equal(RelationPartType.CandidateKey, orderUserCandidatePart.Type); // User side is PK/Candidate
        Assert.Same(userPkCol, orderUserCandidatePart.ColumnIndex.Columns.Single()); // PK Col Index

        // Check User -> Orders relationship (One-to-Many)
        var userToOrdersRelProp = userTable.Model.RelationProperties["Orders"];
        Assert.NotNull(userToOrdersRelProp.RelationPart);
        Assert.Equal(RelationPartType.CandidateKey, userToOrdersRelProp.RelationPart.Type); // User side is PK/Candidate
        Assert.Equal("FK_Order_User", userToOrdersRelProp.RelationPart.Relation.ConstraintName);
        Assert.Same(userPkCol, userToOrdersRelProp.RelationPart.ColumnIndex.Columns.Single());

        var userOrderForeignKeyPart = userToOrdersRelProp.RelationPart.GetOtherSide();
        Assert.Equal(RelationPartType.ForeignKey, userOrderForeignKeyPart.Type); // Order side is FK
        Assert.Same(orderFkCol, userOrderForeignKeyPart.ColumnIndex.Columns.Single());

        // Check if the relations point to each other correctly
        Assert.Same(orderToUserRelProp.RelationPart.Relation, userToOrdersRelProp.RelationPart.Relation);
        Assert.Same(orderUserCandidatePart, userToOrdersRelProp.RelationPart);
        Assert.Same(userOrderForeignKeyPart, orderToUserRelProp.RelationPart);
    }

    [Fact]
    public void TestParseRelations_ImplicitIndices()
    {
        // Arrange
        var db = CreateMultiTableDatabaseForRelationTests();
        var userTable = db.TableModels.Single(tm => tm.Table.DbName == "users").Table;
        var orderTable = db.TableModels.Single(tm => tm.Table.DbName == "orders").Table;
        var userPkCol = userTable.Columns.Single(c => c.DbName == "user_id");
        var orderFkCol = orderTable.Columns.Single(c => c.DbName == "customer_id");

        // Make sure no explicit indices exist yet for these columns
        userTable.ColumnIndices.Clear();
        orderTable.ColumnIndices.Clear();

        // Act
        MetadataFactory.ParseRelations(db);

        // Assert
        // Check implicit Primary Key index on User table
        var userPkIndex = userTable.ColumnIndices.SingleOrDefault(idx => idx.Characteristic == IndexCharacteristic.PrimaryKey);
        Assert.NotNull(userPkIndex);
        Assert.Equal($"{userTable.DbName}_primary_key", userPkIndex.Name); // Assuming this naming convention
        Assert.Single(userPkIndex.Columns);
        Assert.Same(userPkCol, userPkIndex.Columns[0]);

        // Check implicit Foreign Key index on Order table
        var orderFkIndex = orderTable.ColumnIndices.SingleOrDefault(idx => idx.Characteristic == IndexCharacteristic.ForeignKey && idx.Columns.Contains(orderFkCol));
        Assert.NotNull(orderFkIndex);
        Assert.Equal(orderFkCol.DbName, orderFkIndex.Name); // Assuming name defaults to column name
        Assert.Single(orderFkIndex.Columns);
        Assert.Same(orderFkCol, orderFkIndex.Columns[0]);

        // Check if relation parts are linked to these implicit indices
        var orderToUserRelProp = orderTable.Model.RelationProperties["User"];
        Assert.Same(orderFkIndex, orderToUserRelProp.RelationPart.ColumnIndex);
        Assert.Same(userPkIndex, orderToUserRelProp.RelationPart.GetOtherSide().ColumnIndex);
    }
}