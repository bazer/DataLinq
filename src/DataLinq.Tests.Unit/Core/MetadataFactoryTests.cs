using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;
using ThrowAway.Extensions;
using Attribute = System.Attribute;

#pragma warning disable CS0618 // These tests intentionally build legacy metadata fixtures while Workstream C keeps compatibility mutators.

namespace DataLinq.Tests.Unit.Core;

public class MetadataFactoryTests
{
    private (DatabaseDefinition db, TableModel tableModel, ModelDefinition model, TableDefinition table) CreateTestHierarchy(
        string dbName = "TestDb",
        string tableName = "TestTable",
        string modelName = "TestModel",
        bool isView = false)
    {
        var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
        var iViewModel = new CsTypeDeclaration("IViewModel", "DataLinq.Interfaces", ModelCsType.Interface);
        var dbCsType = new CsTypeDeclaration(dbName, "TestNamespace", ModelCsType.Class);
        var modelCsType = new CsTypeDeclaration(modelName, "TestNamespace", ModelCsType.Class);

        var db = new DatabaseDefinition(dbName, dbCsType);
        var model = new ModelDefinition(modelCsType);
        model.SetInterfaces(isView ? [iViewModel] : [iTableModel]);

        var table = MetadataFactory.ParseTable(model).ValueOrException();
        table.SetDbName(tableName);
        var tableModel = new TableModel(modelName + "s", db, model, table);
        db.SetTableModels([tableModel]);

        return (db, tableModel, model, table);
    }

    private ValueProperty CreateTestValueProperty(ModelDefinition model, string propertyName, Type csType, List<Attribute> attributes)
    {
        var csTypeDecl = new CsTypeDeclaration(csType);
        var valueProperty = new ValueProperty(propertyName, csTypeDecl, model, attributes);
        model.AddProperty(valueProperty);
        return valueProperty;
    }

    private DatabaseDefinition CreateMultiTableDatabaseForRelationTests()
    {
        const string foreignKeyName = "FK_Order_User";

        var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
        var dbCsType = new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class);
        var userCsType = new CsTypeDeclaration("User", "TestNamespace", ModelCsType.Class);
        var orderCsType = new CsTypeDeclaration("Order", "TestNamespace", ModelCsType.Class);

        var db = new DatabaseDefinition("TestDb", dbCsType);

        var userModel = new ModelDefinition(userCsType);
        userModel.SetInterfaces([iTableModel]);
        var userTable = MetadataFactory.ParseTable(userModel).ValueOrException();
        userTable.SetDbName("users");
        var userTableModel = new TableModel("Users", db, userModel, userTable);
        var userIdProperty = CreateTestValueProperty(userModel, "UserId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("user_id")]);
        var userNameProperty = CreateTestValueProperty(userModel, "UserName", typeof(string), [new ColumnAttribute("user_name")]);
        var userIdColumn = MetadataFactory.ParseColumn(userTable, userIdProperty);
        var userNameColumn = MetadataFactory.ParseColumn(userTable, userNameProperty);
        userTable.SetColumns([userIdColumn, userNameColumn]);

        var orderModel = new ModelDefinition(orderCsType);
        orderModel.SetInterfaces([iTableModel]);
        var orderTable = MetadataFactory.ParseTable(orderModel).ValueOrException();
        orderTable.SetDbName("orders");
        var orderTableModel = new TableModel("Orders", db, orderModel, orderTable);
        var orderIdProperty = CreateTestValueProperty(orderModel, "OrderId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("order_id")]);
        var orderUserIdProperty = CreateTestValueProperty(orderModel, "CustomerId", typeof(int), [new ForeignKeyAttribute("users", "user_id", foreignKeyName), new ColumnAttribute("customer_id")]);
        var orderAmountProperty = CreateTestValueProperty(orderModel, "Amount", typeof(decimal), [new ColumnAttribute("amount")]);
        var orderIdColumn = MetadataFactory.ParseColumn(orderTable, orderIdProperty);
        var orderUserIdColumn = MetadataFactory.ParseColumn(orderTable, orderUserIdProperty);
        var orderAmountColumn = MetadataFactory.ParseColumn(orderTable, orderAmountProperty);
        orderTable.SetColumns([orderIdColumn, orderUserIdColumn, orderAmountColumn]);

        db.SetTableModels([userTableModel, orderTableModel]);

        MetadataFactory.ParseIndices(db);
        MetadataFactory.ParseRelations(db);

        return db;
    }

    private DatabaseDefinition CreateDuplicateRelationNameDatabaseForRelationTests()
    {
        var iTableModel = new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface);
        var dbCsType = new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class);
        var accountCsType = new CsTypeDeclaration("Account", "TestNamespace", ModelCsType.Class);
        var invoiceCsType = new CsTypeDeclaration("Invoice", "TestNamespace", ModelCsType.Class);

        var db = new DatabaseDefinition("TestDb", dbCsType);

        var accountModel = new ModelDefinition(accountCsType);
        accountModel.SetInterfaces([iTableModel]);
        var accountTable = MetadataFactory.ParseTable(accountModel).ValueOrException();
        accountTable.SetDbName("account");
        var accountTableModel = new TableModel("Accounts", db, accountModel, accountTable);
        var accountIdProperty = CreateTestValueProperty(accountModel, "AccountId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("account_id")]);
        var accountIdColumn = MetadataFactory.ParseColumn(accountTable, accountIdProperty);
        accountTable.SetColumns([accountIdColumn]);

        var invoiceModel = new ModelDefinition(invoiceCsType);
        invoiceModel.SetInterfaces([iTableModel]);
        var invoiceTable = MetadataFactory.ParseTable(invoiceModel).ValueOrException();
        invoiceTable.SetDbName("invoice");
        var invoiceTableModel = new TableModel("Invoices", db, invoiceModel, invoiceTable);
        var invoiceIdProperty = CreateTestValueProperty(invoiceModel, "InvoiceId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("invoice_id")]);
        var createdByProperty = CreateTestValueProperty(invoiceModel, "CreatedByAccountId", typeof(int), [new ForeignKeyAttribute("account", "account_id", "FK_invoice_created_by"), new ColumnAttribute("created_by_account_id")]);
        var approvedByProperty = CreateTestValueProperty(invoiceModel, "ApprovedByAccountId", typeof(int), [new ForeignKeyAttribute("account", "account_id", "FK_invoice_approved_by"), new ColumnAttribute("approved_by_account_id")]);
        var invoiceIdColumn = MetadataFactory.ParseColumn(invoiceTable, invoiceIdProperty);
        var createdByColumn = MetadataFactory.ParseColumn(invoiceTable, createdByProperty);
        var approvedByColumn = MetadataFactory.ParseColumn(invoiceTable, approvedByProperty);
        invoiceTable.SetColumns([invoiceIdColumn, createdByColumn, approvedByColumn]);

        db.SetTableModels([accountTableModel, invoiceTableModel]);

        MetadataFactory.ParseIndices(db).ValueOrException();
        MetadataFactory.ParseRelations(db).ValueOrException();

        return db;
    }

    [Test]
    public async Task ParseTableAttribute_SetsDbNameAndType()
    {
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new TableAttribute("my_table")]);

        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException();

        await Assert.That(tableDefinition.DbName).IsEqualTo("my_table");
        await Assert.That(tableDefinition.Type).IsEqualTo(TableType.Table);
    }

    [Test]
    public async Task ParseViewAttribute_SetsViewNameAndType()
    {
        var (_, _, model, _) = CreateTestHierarchy(isView: true);
        model.SetAttributes([new ViewAttribute("my_view")]);

        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException();

        await Assert.That(tableDefinition.DbName).IsEqualTo("my_view");
        await Assert.That(tableDefinition).IsTypeOf<ViewDefinition>();
        await Assert.That(tableDefinition.Type).IsEqualTo(TableType.View);
    }

    [Test]
    public async Task ParseDefinitionAttribute_SetsViewDefinition()
    {
        var (_, _, model, _) = CreateTestHierarchy(isView: true);
        const string definitionSql = "SELECT Id FROM OtherTable";
        model.SetAttributes([new ViewAttribute("my_view"), new DefinitionAttribute(definitionSql)]);

        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException();
        var viewDefinition = (ViewDefinition)tableDefinition;

        await Assert.That(viewDefinition.Definition).IsEqualTo(definitionSql);
    }

    [Test]
    public async Task ParseColumnAttribute_SetsDbNameAndBackReference()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "CsPropName", typeof(string), [new ColumnAttribute("db_col_name")]);

        var columnDefinition = table.ParseColumn(valueProperty);

        await Assert.That(columnDefinition.DbName).IsEqualTo("db_col_name");
        await Assert.That(ReferenceEquals(valueProperty, columnDefinition.ValueProperty)).IsTrue();
        await Assert.That(ReferenceEquals(columnDefinition, valueProperty.Column)).IsTrue();
    }

    [Test]
    public async Task ParsePrimaryKeyAttribute_MarksPrimaryKey()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Id", typeof(int), [new PrimaryKeyAttribute()]);

        var columnDefinition = table.ParseColumn(valueProperty);
        table.SetColumns([columnDefinition]);

        await Assert.That(columnDefinition.PrimaryKey).IsTrue();
        await Assert.That(table.PrimaryKeyColumns.Length).IsEqualTo(1);
        await Assert.That(ReferenceEquals(columnDefinition, table.PrimaryKeyColumns[0])).IsTrue();
    }

    [Test]
    public async Task ParseAutoIncrementAttribute_MarksColumn()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Id", typeof(int), [new AutoIncrementAttribute()]);

        var columnDefinition = table.ParseColumn(valueProperty);

        await Assert.That(columnDefinition.AutoIncrement).IsTrue();
    }

    [Test]
    public async Task ParseNullableAttribute_MarksColumnNullable()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Name", typeof(string), [new NullableAttribute()]);

        var columnDefinition = table.ParseColumn(valueProperty);

        await Assert.That(columnDefinition.Nullable).IsTrue();
    }

    [Test]
    public async Task ParseTypeAttribute_ForSpecificDatabase_SetsDbType()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Name", typeof(string), [new TypeAttribute(DatabaseType.MySQL, "VARCHAR", 255)]);

        var columnDefinition = table.ParseColumn(valueProperty);
        var dbType = columnDefinition.DbTypes.Single();

        await Assert.That(dbType.DatabaseType).IsEqualTo(DatabaseType.MySQL);
        await Assert.That(dbType.Name).IsEqualTo("VARCHAR");
        await Assert.That(dbType.Length).IsEqualTo((ulong)255);
        await Assert.That(dbType.Decimals).IsNull();
        await Assert.That(dbType.Signed).IsNull();
    }

    [Test]
    public async Task ParseTypeAttribute_ForDefaultDatabase_SetsDefaultDbType()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Description", typeof(string), [new TypeAttribute("TEXT")]);

        var columnDefinition = table.ParseColumn(valueProperty);
        var dbType = columnDefinition.DbTypes.Single();

        await Assert.That(dbType.DatabaseType).IsEqualTo(DatabaseType.Default);
        await Assert.That(dbType.Name).IsEqualTo("TEXT");
        await Assert.That(dbType.Length).IsNull();
    }

    [Test]
    public async Task ParseTypeAttribute_WithLengthAndDecimals_SetsScale()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Price", typeof(decimal), [new TypeAttribute(DatabaseType.MySQL, "DECIMAL", 10, 2)]);

        var columnDefinition = table.ParseColumn(valueProperty);
        var dbType = columnDefinition.DbTypes.Single();

        await Assert.That(dbType.DatabaseType).IsEqualTo(DatabaseType.MySQL);
        await Assert.That(dbType.Name).IsEqualTo("DECIMAL");
        await Assert.That(dbType.Length).IsEqualTo((ulong)10);
        await Assert.That(dbType.Decimals).IsEqualTo((uint)2);
    }

    [Test]
    public async Task ParseTypeAttribute_WithSignedFlag_SetsSigned()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Counter", typeof(uint), [new TypeAttribute(DatabaseType.MySQL, "INT", false)]);

        var columnDefinition = table.ParseColumn(valueProperty);
        var dbType = columnDefinition.DbTypes.Single();

        await Assert.That(dbType.DatabaseType).IsEqualTo(DatabaseType.MySQL);
        await Assert.That(dbType.Name).IsEqualTo("INT");
        await Assert.That(dbType.Signed).IsFalse();
    }

    [Test]
    public async Task ParseEnumAttribute_BuildsEnumProperty()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var enumType = new CsTypeDeclaration("MyEnum", "TestNamespace", ModelCsType.Enum);
        var attributes = new List<Attribute> { new EnumAttribute("Value1", "Value2") };
        var valueProperty = new ValueProperty("Status", enumType, model, attributes);
        valueProperty.SetEnumProperty(new EnumProperty(enumValues: attributes.OfType<EnumAttribute>().Single().Values.Select((name, index) => (name, index + 1))));
        model.AddProperty(valueProperty);
        table.ParseColumn(valueProperty);

        await Assert.That(valueProperty.EnumProperty).IsNotNull();
        await Assert.That(valueProperty.EnumProperty!.Value.CsValuesOrDbValues.Count).IsEqualTo(2);
        await Assert.That(valueProperty.EnumProperty.Value.CsValuesOrDbValues[0].name).IsEqualTo("Value1");
        await Assert.That(valueProperty.EnumProperty.Value.CsValuesOrDbValues[0].value).IsEqualTo(1);
        await Assert.That(valueProperty.EnumProperty.Value.CsValuesOrDbValues[1].name).IsEqualTo("Value2");
        await Assert.That(valueProperty.EnumProperty.Value.CsValuesOrDbValues[1].value).IsEqualTo(2);
    }

    [Test]
    public async Task TryAttachValueProperty_UnknownCsType_ReturnsInvalidModelFailure()
    {
        var (_, _, _, table) = CreateTestHierarchy();
        var column = new ColumnDefinition("unknown_type", table);

        var result = MetadataFactory.TryAttachValueProperty(column, "MissingClrType", capitaliseNames: true);

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.ToString()!).Contains("Unsupported C# type 'MissingClrType'");
        await Assert.That(failure.ToString()!).Contains("TestTable.unknown_type");
    }

    [Test]
    public async Task ParseDefaultValueAttribute_ForString_SetsDefault()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Name", typeof(string), [new DefaultAttribute("DefaultName")]);

        table.ParseColumn(valueProperty);
        var defaultAttribute = valueProperty.GetDefaultAttribute();

        await Assert.That(valueProperty.HasDefaultValue()).IsTrue();
        await Assert.That(defaultAttribute).IsNotNull();
        await Assert.That(defaultAttribute!.Value).IsEqualTo("DefaultName");
    }

    [Test]
    public async Task ParseDefaultValueAttribute_ForInt_SetsDefault()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Count", typeof(int), [new DefaultAttribute(123)]);

        table.ParseColumn(valueProperty);
        var defaultAttribute = valueProperty.GetDefaultAttribute();

        await Assert.That(valueProperty.HasDefaultValue()).IsTrue();
        await Assert.That(defaultAttribute).IsNotNull();
        await Assert.That(defaultAttribute!.Value).IsEqualTo(123);
    }

    [Test]
    public async Task ParseDefaultCurrentTimestampAttribute_SetsDynamicDefault()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "CreatedAt", typeof(DateTime), [new DefaultCurrentTimestampAttribute()]);

        table.ParseColumn(valueProperty);
        var defaultAttribute = valueProperty.GetDefaultAttribute();

        await Assert.That(defaultAttribute).IsTypeOf<DefaultCurrentTimestampAttribute>();
        await Assert.That(defaultAttribute!.Value).IsEqualTo(DynamicFunctions.CurrentTimestamp);
    }

    [Test]
    public async Task ParseDefaultNewUuidAttribute_SetsDynamicDefaultAndVersion()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "ID", typeof(Guid), [new DefaultNewUUIDAttribute()]);

        table.ParseColumn(valueProperty);
        var defaultAttribute = valueProperty.GetDefaultAttribute();

        await Assert.That(defaultAttribute).IsTypeOf<DefaultNewUUIDAttribute>();
        await Assert.That(defaultAttribute!.Value).IsEqualTo(DynamicFunctions.NewUUID);
        await Assert.That(((DefaultNewUUIDAttribute)defaultAttribute).Version).IsEqualTo(UUIDVersion.Version7);
    }

    [Test]
    public async Task GetDefaultValue_ForTimestamp_FormatsAsNow()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "CreatedAt", typeof(DateTime), [new DefaultCurrentTimestampAttribute()]);
        table.ParseColumn(valueProperty);

        await Assert.That(valueProperty.GetDefaultValue()).IsEqualTo("DateTime.Now");
    }

    [Test]
    public async Task GetDefaultValue_ForUuid_FormatsAsVersion7()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "ID", typeof(Guid), [new DefaultNewUUIDAttribute()]);
        table.ParseColumn(valueProperty);

        await Assert.That(valueProperty.GetDefaultValue()).IsEqualTo("Guid.CreateVersion7()");
    }

    [Test]
    public async Task GetDefaultValue_ForString_ReturnsLiteralValue()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Name", typeof(string), [new DefaultAttribute("MyDefault")]);
        table.ParseColumn(valueProperty);

        await Assert.That(valueProperty.GetDefaultValue()).IsEqualTo("MyDefault");
    }

    [Test]
    public async Task ParseForeignKeyAttribute_MarksColumnAndPreservesMetadata()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "ReferenceId", typeof(int), [new ForeignKeyAttribute("ReferencedTable", "ReferencedColumn", "FK_ConstraintName")]);

        var columnDefinition = table.ParseColumn(valueProperty);
        var foreignKeyAttribute = valueProperty.Attributes.OfType<ForeignKeyAttribute>().Single();

        await Assert.That(columnDefinition.ForeignKey).IsTrue();
        await Assert.That(foreignKeyAttribute.Table).IsEqualTo("ReferencedTable");
        await Assert.That(foreignKeyAttribute.Column).IsEqualTo("ReferencedColumn");
        await Assert.That(foreignKeyAttribute.Name).IsEqualTo("FK_ConstraintName");
    }

    [Test]
    public async Task ParseIndexAttribute_Simple_CreatesColumnIndex()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Name", typeof(string), [new IndexAttribute("idx_name", IndexCharacteristic.Simple, IndexType.BTREE)]);

        var columnDefinition = table.ParseColumn(valueProperty);
        table.SetColumns([columnDefinition]);
        MetadataFactory.ParseIndices(model.Database);

        var indexAttribute = valueProperty.Attributes.OfType<IndexAttribute>().Single();
        var columnIndex = table.ColumnIndices.Single();

        await Assert.That(indexAttribute.Name).IsEqualTo("idx_name");
        await Assert.That(indexAttribute.Characteristic).IsEqualTo(IndexCharacteristic.Simple);
        await Assert.That(indexAttribute.Type).IsEqualTo(IndexType.BTREE);
        await Assert.That(indexAttribute.Columns.Count).IsEqualTo(0);

        await Assert.That(columnIndex.Name).IsEqualTo("idx_name");
        await Assert.That(columnIndex.Characteristic).IsEqualTo(IndexCharacteristic.Simple);
        await Assert.That(columnIndex.Type).IsEqualTo(IndexType.BTREE);
        await Assert.That(columnIndex.Columns.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(columnDefinition, columnIndex.Columns[0])).IsTrue();
    }

    [Test]
    public async Task ParseIndexAttribute_Unique_CreatesUniqueIndex()
    {
        var (_, _, model, table) = CreateTestHierarchy();
        var valueProperty = CreateTestValueProperty(model, "Email", typeof(string), [new IndexAttribute("uq_email", IndexCharacteristic.Unique)]);

        var columnDefinition = table.ParseColumn(valueProperty);
        table.SetColumns([columnDefinition]);
        MetadataFactory.ParseIndices(model.Database);

        var indexAttribute = valueProperty.Attributes.OfType<IndexAttribute>().Single();
        var columnIndex = table.ColumnIndices.Single();

        await Assert.That(indexAttribute.Characteristic).IsEqualTo(IndexCharacteristic.Unique);
        await Assert.That(indexAttribute.Type).IsEqualTo(IndexType.BTREE);
        await Assert.That(columnIndex.Name).IsEqualTo("uq_email");
        await Assert.That(columnIndex.Characteristic).IsEqualTo(IndexCharacteristic.Unique);
        await Assert.That(columnIndex.Columns.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ParseRelationAttribute_PreservesRelationMetadata()
    {
        var (_, _, model, _) = CreateTestHierarchy();
        var relationProperty = new RelationProperty(
            "RelatedItems",
            new CsTypeDeclaration(typeof(IEnumerable<>)),
            model,
            [new RelationAttribute("OtherTable", "OtherId", "FK_RelationName")]);
        model.AddProperty(relationProperty);

        var relationAttribute = relationProperty.Attributes.OfType<RelationAttribute>().Single();

        await Assert.That(relationAttribute.Table).IsEqualTo("OtherTable");
        await Assert.That(relationAttribute.Columns.Count).IsEqualTo(1);
        await Assert.That(relationAttribute.Columns[0]).IsEqualTo("OtherId");
        await Assert.That(relationAttribute.Name).IsEqualTo("FK_RelationName");
    }

    [Test]
    public async Task ParseUseCacheAttribute_OnTable_SetsExplicitUseCache()
    {
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new TableAttribute("my_table"), new UseCacheAttribute(true)]);

        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException();

        await Assert.That(tableDefinition.explicitUseCache.HasValue).IsTrue();
        await Assert.That(tableDefinition.explicitUseCache!.Value).IsTrue();
    }

    [Test]
    public async Task ParseUseCacheAttribute_OnDatabase_SetsDatabaseCacheFlag()
    {
        var (db, _, _, _) = CreateTestHierarchy();
        db.SetAttributes([new UseCacheAttribute(true)]);

        db.ParseAttributes();

        await Assert.That(db.UseCache).IsTrue();
    }

    [Test]
    public async Task ParseCacheLimitAttribute_OnTable_SetsLimit()
    {
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new TableAttribute("my_table"), new CacheLimitAttribute(CacheLimitType.Rows, 1000)]);

        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException();

        await Assert.That(tableDefinition.CacheLimits.Count).IsEqualTo(1);
        await Assert.That(tableDefinition.CacheLimits[0].limitType).IsEqualTo(CacheLimitType.Rows);
        await Assert.That(tableDefinition.CacheLimits[0].amount).IsEqualTo(1000);
    }

    [Test]
    public async Task ParseCacheLimitAttribute_OnDatabase_SetsLimit()
    {
        var (db, _, _, _) = CreateTestHierarchy();
        db.SetAttributes([new CacheLimitAttribute(CacheLimitType.Megabytes, 512)]);

        db.ParseAttributes();

        await Assert.That(db.CacheLimits.Count).IsEqualTo(1);
        await Assert.That(db.CacheLimits[0].limitType).IsEqualTo(CacheLimitType.Megabytes);
        await Assert.That(db.CacheLimits[0].amount).IsEqualTo(512);
    }

    [Test]
    public async Task ParseIndexCacheAttribute_OnTable_SetsPolicy()
    {
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new TableAttribute("my_table"), new IndexCacheAttribute(IndexCacheType.MaxAmountRows, 5000)]);

        var tableDefinition = MetadataFactory.ParseTable(model).ValueOrException();

        await Assert.That(tableDefinition.IndexCache.Count).IsEqualTo(1);
        await Assert.That(tableDefinition.IndexCache[0].indexCacheType).IsEqualTo(IndexCacheType.MaxAmountRows);
        await Assert.That(tableDefinition.IndexCache[0].amount).IsEqualTo(5000);
    }

    [Test]
    public async Task ParseIndexCacheAttribute_OnDatabase_SetsPolicy()
    {
        var (db, _, _, _) = CreateTestHierarchy();
        db.SetAttributes([new IndexCacheAttribute(IndexCacheType.All)]);

        db.ParseAttributes();

        await Assert.That(db.IndexCache.Count).IsEqualTo(1);
        await Assert.That(db.IndexCache[0].indexCacheType).IsEqualTo(IndexCacheType.All);
        await Assert.That(db.IndexCache[0].amount).IsNull();
    }

    [Test]
    public async Task ParseCacheCleanupAttribute_OnDatabase_SetsPolicy()
    {
        var (db, _, _, _) = CreateTestHierarchy();
        db.SetAttributes([new CacheCleanupAttribute(CacheCleanupType.Hours, 1)]);

        db.ParseAttributes();

        await Assert.That(db.CacheCleanup.Count).IsEqualTo(1);
        await Assert.That(db.CacheCleanup[0].cleanupType).IsEqualTo(CacheCleanupType.Hours);
        await Assert.That(db.CacheCleanup[0].amount).IsEqualTo(1);
    }

    [Test]
    public async Task ParseInterfaceAttribute_DefaultInterface_PreservesIntent()
    {
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new InterfaceAttribute()]);

        var interfaceAttribute = model.Attributes.OfType<InterfaceAttribute>().Single();

        await Assert.That(interfaceAttribute.GenerateInterface).IsTrue();
        await Assert.That(interfaceAttribute.Name).IsNull();
    }

    [Test]
    public async Task ParseInterfaceAttribute_NamedInterface_PreservesName()
    {
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new InterfaceAttribute("IMyCustomInterface")]);

        var interfaceAttribute = model.Attributes.OfType<InterfaceAttribute>().Single();

        await Assert.That(interfaceAttribute.GenerateInterface).IsTrue();
        await Assert.That(interfaceAttribute.Name).IsEqualTo("IMyCustomInterface");
    }

    [Test]
    public async Task ParseInterfaceAttribute_GenericInterface_PreservesTypeName()
    {
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new InterfaceAttribute<IDisposable>()]);

        var interfaceAttribute = model.Attributes.OfType<InterfaceAttribute<IDisposable>>().Single();

        await Assert.That(interfaceAttribute.GenerateInterface).IsTrue();
        await Assert.That(interfaceAttribute.Name).IsEqualTo("IDisposable");
    }

    [Test]
    public async Task ParseInterfaceAttribute_NoGenerate_PreservesFlag()
    {
        var (_, _, model, _) = CreateTestHierarchy();
        model.SetAttributes([new InterfaceAttribute(generateInterface: false)]);

        var interfaceAttribute = model.Attributes.OfType<InterfaceAttribute>().Single();

        await Assert.That(interfaceAttribute.GenerateInterface).IsFalse();
        await Assert.That(interfaceAttribute.Name).IsNull();
    }

    [Test]
    public async Task ParseIndices_MultiColumn_BuildsSingleIndex()
    {
        var (_, _, model, table) = CreateTestHierarchy(tableName: "MultiIndexTable");
        var property1 = CreateTestValueProperty(model, "Prop1", typeof(string), [new IndexAttribute("idx_multi", IndexCharacteristic.Simple, IndexType.BTREE, "col1", "col2"), new ColumnAttribute("col1")]);
        var property2 = CreateTestValueProperty(model, "Prop2", typeof(int), [new IndexAttribute("idx_multi", IndexCharacteristic.Simple, IndexType.BTREE, "col1", "col2"), new ColumnAttribute("col2")]);

        var property1Column = MetadataFactory.ParseColumn(table, property1);
        var property2Column = MetadataFactory.ParseColumn(table, property2);
        table.SetColumns([property1Column, property2Column]);

        MetadataFactory.ParseIndices(model.Database);
        var index = table.ColumnIndices.Single();

        await Assert.That(index.Name).IsEqualTo("idx_multi");
        await Assert.That(index.Columns.Count).IsEqualTo(2);
        await Assert.That(index.Columns.Contains(property1.Column!)).IsTrue();
        await Assert.That(index.Columns.Contains(property2.Column!)).IsTrue();
        await Assert.That(ReferenceEquals(property1.Column, index.Columns.Single(c => c.DbName == "col1"))).IsTrue();
        await Assert.That(ReferenceEquals(property2.Column, index.Columns.Single(c => c.DbName == "col2"))).IsTrue();
    }

    [Test]
    public async Task ParseIndices_ClassLevelMultiColumn_BuildsSingleIndex()
    {
        var (_, _, model, table) = CreateTestHierarchy(tableName: "MultiIndexTable");
        model.SetAttributes([new IndexAttribute("idx_multi", IndexCharacteristic.Unique, IndexType.BTREE, "col1", "col2")]);
        var property1 = CreateTestValueProperty(model, "Prop1", typeof(string), [new ColumnAttribute("col1")]);
        var property2 = CreateTestValueProperty(model, "Prop2", typeof(int), [new ColumnAttribute("col2")]);

        var property1Column = MetadataFactory.ParseColumn(table, property1);
        var property2Column = MetadataFactory.ParseColumn(table, property2);
        table.SetColumns([property1Column, property2Column]);

        MetadataFactory.ParseIndices(model.Database).ValueOrException();
        var index = table.ColumnIndices.Single();

        await Assert.That(index.Name).IsEqualTo("idx_multi");
        await Assert.That(index.Characteristic).IsEqualTo(IndexCharacteristic.Unique);
        await Assert.That(index.Columns.Select(x => x.DbName).ToArray()).IsEquivalentTo(["col1", "col2"]);
    }

    [Test]
    public async Task ParseIndices_MissingColumnDiagnostic_NamesDatabaseColumnContract()
    {
        var (_, _, model, table) = CreateTestHierarchy(tableName: "MultiIndexTable");
        model.SetAttributes([new IndexAttribute("idx_multi", IndexCharacteristic.Unique, IndexType.BTREE, "Prop1", "col2")]);
        var property1 = CreateTestValueProperty(model, "Prop1", typeof(string), [new ColumnAttribute("col1")]);
        var property2 = CreateTestValueProperty(model, "Prop2", typeof(int), [new ColumnAttribute("col2")]);

        var property1Column = MetadataFactory.ParseColumn(table, property1);
        var property2Column = MetadataFactory.ParseColumn(table, property2);
        table.SetColumns([property1Column, property2Column]);

        var result = MetadataFactory.ParseIndices(model.Database);

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.Message).Contains("IndexAttribute.Columns expects database column names, not C# property names.");
        await Assert.That(failure.Message).Contains("Prop1");
    }

    [Test]
    public async Task ColumnIndex_EmptyColumns_ThrowsDomainValidationMessage()
    {
        InvalidOperationException? exception = null;
        try
        {
            _ = new ColumnIndex("idx_empty", IndexCharacteristic.Simple, IndexType.BTREE, []);
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).IsEqualTo("An index should have at least one column.");
    }

    [Test]
    public async Task ParseRelations_OneToManyAndBackReference_LinksBothSides()
    {
        var db = CreateMultiTableDatabaseForRelationTests();
        var userTable = db.TableModels.Single(tm => tm.Table.DbName == "users").Table;
        var orderTable = db.TableModels.Single(tm => tm.Table.DbName == "orders").Table;
        var userPrimaryKeyColumn = userTable.Columns.Single(c => c.DbName == "user_id");
        var orderForeignKeyColumn = orderTable.Columns.Single(c => c.DbName == "customer_id");

        var orderToUserRelation = orderTable.Model.RelationProperties["Customer"];
        var orderUserCandidatePart = orderToUserRelation.RelationPart!.GetOtherSide();

        await Assert.That(orderToUserRelation.RelationPart.Type).IsEqualTo(RelationPartType.ForeignKey);
        await Assert.That(ReferenceEquals(orderForeignKeyColumn, orderToUserRelation.RelationPart.ColumnIndex.Columns.Single())).IsTrue();
        await Assert.That(orderUserCandidatePart.Type).IsEqualTo(RelationPartType.CandidateKey);
        await Assert.That(ReferenceEquals(userPrimaryKeyColumn, orderUserCandidatePart.ColumnIndex.Columns.Single())).IsTrue();

        var userToOrdersRelation = userTable.Model.RelationProperties["Order"];
        var userOrderForeignKeyPart = userToOrdersRelation.RelationPart!.GetOtherSide();

        await Assert.That(userToOrdersRelation.RelationPart.Type).IsEqualTo(RelationPartType.CandidateKey);
        await Assert.That(ReferenceEquals(userPrimaryKeyColumn, userToOrdersRelation.RelationPart.ColumnIndex.Columns.Single())).IsTrue();
        await Assert.That(userOrderForeignKeyPart.Type).IsEqualTo(RelationPartType.ForeignKey);
        await Assert.That(ReferenceEquals(orderForeignKeyColumn, userOrderForeignKeyPart.ColumnIndex.Columns.Single())).IsTrue();

        await Assert.That(ReferenceEquals(orderToUserRelation.RelationPart.Relation, userToOrdersRelation.RelationPart.Relation)).IsTrue();
        await Assert.That(ReferenceEquals(orderUserCandidatePart, userToOrdersRelation.RelationPart)).IsTrue();
        await Assert.That(ReferenceEquals(userOrderForeignKeyPart, orderToUserRelation.RelationPart)).IsTrue();
    }

    [Test]
    public async Task ParseRelations_CreatesImplicitIndicesForPrimaryAndForeignKeys()
    {
        var db = CreateMultiTableDatabaseForRelationTests();
        var userTable = db.TableModels.Single(tm => tm.Table.DbName == "users").Table;
        var orderTable = db.TableModels.Single(tm => tm.Table.DbName == "orders").Table;
        var userPrimaryKeyColumn = userTable.Columns.Single(c => c.DbName == "user_id");
        var orderForeignKeyColumn = orderTable.Columns.Single(c => c.DbName == "customer_id");

        var userPrimaryKeyIndex = userTable.ColumnIndices.Single(idx => idx.Characteristic == IndexCharacteristic.PrimaryKey);
        var orderForeignKeyIndex = orderTable.ColumnIndices.Single(idx => idx.Characteristic == IndexCharacteristic.ForeignKey && idx.Columns.Contains(orderForeignKeyColumn));
        var orderToUserRelation = orderTable.Model.RelationProperties["Customer"];

        await Assert.That(ReferenceEquals(userPrimaryKeyColumn, userPrimaryKeyIndex.Columns[0])).IsTrue();
        await Assert.That(ReferenceEquals(orderForeignKeyColumn, orderForeignKeyIndex.Columns[0])).IsTrue();
        await Assert.That(ReferenceEquals(orderForeignKeyIndex, orderToUserRelation.RelationPart!.ColumnIndex)).IsTrue();
        await Assert.That(ReferenceEquals(userPrimaryKeyIndex, orderToUserRelation.RelationPart.GetOtherSide().ColumnIndex)).IsTrue();
    }

    [Test]
    public async Task ParseRelations_MultipleForeignKeysToSameTable_DerivesCandidateSideNamesFromConstraints()
    {
        var db = CreateDuplicateRelationNameDatabaseForRelationTests();
        var account = db.TableModels.Single(tm => tm.Table.DbName == "account").Table;
        var invoice = db.TableModels.Single(tm => tm.Table.DbName == "invoice").Table;

        await Assert.That(invoice.Model.RelationProperties.Keys.OrderBy(x => x).ToArray())
            .IsEquivalentTo(["ApprovedByAccount", "CreatedByAccount"]);
        await Assert.That(account.Model.RelationProperties.Keys.OrderBy(x => x).ToArray())
            .IsEquivalentTo(["InvoiceApprovedBy", "InvoiceCreatedBy"]);
        await Assert.That(account.Model.RelationProperties.ContainsKey("Invoice")).IsFalse();
        await Assert.That(account.Model.RelationProperties.Keys.Any(x => x.StartsWith("Invoice_", StringComparison.Ordinal))).IsFalse();

        var createdBy = account.Model.RelationProperties["InvoiceCreatedBy"];
        var approvedBy = account.Model.RelationProperties["InvoiceApprovedBy"];

        await Assert.That(createdBy.RelationPart.Relation.ConstraintName).IsEqualTo("FK_invoice_created_by");
        await Assert.That(approvedBy.RelationPart.Relation.ConstraintName).IsEqualTo("FK_invoice_approved_by");
    }
}
