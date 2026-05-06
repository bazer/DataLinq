using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public class MetadataDefinitionFactoryTests
{
    [Test]
    public async Task Build_RelationDraft_AssignsOrdinalsAndResolvesRelations()
    {
        var database = CreateRelationDraft();

        var built = new MetadataDefinitionFactory().Build(database).ValueOrException();

        var userTable = built.TableModels.Single(tm => tm.Table.DbName == "users").Table;
        var orderTable = built.TableModels.Single(tm => tm.Table.DbName == "orders").Table;

        await Assert.That(userTable.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 1]);
        await Assert.That(orderTable.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 1, 2]);
        await Assert.That(userTable.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.PrimaryKey)).IsTrue();
        await Assert.That(orderTable.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.ForeignKey && x.Name == "FK_Order_User")).IsTrue();

        var orderToUser = orderTable.Model.RelationProperties["Customer"];
        var userToOrders = userTable.Model.RelationProperties["Order"];

        await Assert.That(orderToUser.RelationPart.Type).IsEqualTo(RelationPartType.ForeignKey);
        await Assert.That(userToOrders.RelationPart.Type).IsEqualTo(RelationPartType.CandidateKey);
        await Assert.That(ReferenceEquals(orderToUser.RelationPart.Relation, userToOrders.RelationPart.Relation)).IsTrue();
    }

    [Test]
    public async Task Build_RelationDraft_FinalizesSnapshotWithoutMutatingDraft()
    {
        var database = CreateRelationDraft();
        var orderDraft = database.TableModels.Single(tm => tm.Table.DbName == "orders");

        var built = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database))
            .ValueOrException();

        var builtOrderTable = built.TableModels.Single(tm => tm.Table.DbName == "orders").Table;

        await Assert.That(ReferenceEquals(database, built)).IsFalse();
        await Assert.That(builtOrderTable.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 1, 2]);
        await Assert.That(builtOrderTable.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.ForeignKey)).IsTrue();
        await Assert.That(builtOrderTable.Model.RelationProperties).IsNotEmpty();

        await Assert.That(orderDraft.Table.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 0, 0]);
        await Assert.That(orderDraft.Table.ColumnIndices.Count).IsEqualTo(0);
        await Assert.That(orderDraft.Model.RelationProperties.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Build_TableModelRegisteredOnWrongDatabase_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var ownerDatabase = new DatabaseDefinition(
            "OwnerDb",
            new CsTypeDeclaration("OwnerDb", "TestNamespace", ModelCsType.Class));
        var tableModel = CreateTableModel(ownerDatabase, "Items", "Item", "items");
        AddValueProperties(
            tableModel.Model,
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));

        var targetDatabase = new DatabaseDefinition(
            "TargetDb",
            new CsTypeDeclaration("TargetDb", "TestNamespace", ModelCsType.Class));
        targetDatabase.SetTableModels([tableModel]);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(targetDatabase));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table model 'Items' is registered on database 'TargetDb'");
        await Assert.That(failure.Message).Contains("belongs to database 'OwnerDb'");
    }

    [Test]
    public async Task Build_PrimaryKeyColumnNotRegisteredOnTable_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var ghostPrimaryKey = new ColumnDefinition("ghost_pk", table);
        ghostPrimaryKey.SetPrimaryKey();

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("primary-key column 'ghost_pk'");
        await Assert.That(failure.Message).Contains("not registered on the table");
    }

    [Test]
    public async Task Build_PrimaryKeyFlagMissingTableRegistration_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var idColumn = table.Columns.Single(column => column.DbName == "id");
        table.RemovePrimaryKeyColumn(idColumn);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.id' is marked as a primary key");
        await Assert.That(failure.Message).Contains("not registered in the table primary-key columns");
    }

    [Test]
    public async Task Build_DuplicateTableModelPropertyNames_ReturnsInvalidModelFailure()
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var itemModel = CreateTableModel(database, "Items", "Item", "items");
        var archiveModel = CreateTableModel(database, "Items", "ArchiveItem", "archive_items");
        AddValueProperties(
            itemModel.Model,
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        AddValueProperties(
            archiveModel.Model,
            ("ArchiveId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("archive_id")]));
        database.SetTableModels([itemModel, archiveModel]);

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Duplicate table model property 'Items'");
        await Assert.That(failure.Message).Contains("items");
        await Assert.That(failure.Message).Contains("archive_items");
        await Assert.That(failure.Message).Contains("Item");
        await Assert.That(failure.Message).Contains("ArchiveItem");
    }

    [Test]
    public async Task Build_DuplicateTableModelReference_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var tableModel = database.TableModels.Single();
        database.SetTableModels([tableModel, tableModel]);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Duplicate table model property 'Items'");
        await Assert.That(failure.Message).Contains("items");
        await Assert.That(failure.Message).Contains("Item");
    }

    [Test]
    public async Task Build_DatabaseCacheLimitWithUnsupportedType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        database.CacheLimits.Add(((CacheLimitType)999, 1));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("unsupported cache limit type '999'");
    }

    [Test]
    public async Task Build_DatabaseCacheCleanupWithZeroAmount_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        database.CacheCleanup.Add((CacheCleanupType.Minutes, 0));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("Cache cleanup amounts must be greater than zero");
    }

    [Test]
    public async Task Build_TableCacheLimitWithNegativeAmount_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        table.CacheLimits.Add((CacheLimitType.Rows, -1));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table 'items'");
        await Assert.That(failure.Message).Contains("Cache limit amounts must be greater than zero");
    }

    [Test]
    public async Task Build_TableIndexCacheMaxRowsWithoutAmount_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        table.IndexCache.Add((IndexCacheType.MaxAmountRows, null));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table 'items'");
        await Assert.That(failure.Message).Contains("MaxAmountRows index-cache metadata without a row amount");
    }

    [Test]
    public async Task Build_CheckAttributeWithUnsupportedDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var model = database.TableModels.Single().Model;
        model.AddAttribute(new CheckAttribute((DatabaseType)999, "CK_items_id", "id > 0"));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Check attribute on model 'Item'");
        await Assert.That(failure.Message).Contains("unsupported database type '999'");
    }

    [Test]
    public async Task Build_CommentAttributeWithUnsupportedDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var property = database.TableModels.Single().Model.ValueProperties["Id"];
        property.AddAttribute(new CommentAttribute((DatabaseType)999, "id comment"));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Comment attribute on value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("unsupported database type '999'");
    }

    [Test]
    public async Task Build_DefaultSqlAttributeWithUnsupportedDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var property = database.TableModels.Single().Model.ValueProperties["Id"];
        property.AddAttribute(new DefaultSqlAttribute((DatabaseType)999, "0"));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Default SQL attribute on value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("unsupported database type '999'");
    }

    [Test]
    public async Task Build_DatabaseTypeWithInvalidCSharpName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        database.SetCsType(new CsTypeDeclaration("Bad Db", "TestNamespace", ModelCsType.Class));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Database 'TestDb'");
        await Assert.That(failure.Message).Contains("C# type name 'Bad Db'");
    }

    [Test]
    public async Task Build_TableModelPropertyWithInvalidCSharpName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        database.TableModels.Single().SetCsPropertyName("Bad Name");

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Table 'items'");
        await Assert.That(failure.Message).Contains("C# database property name 'Bad Name'");
    }

    [Test]
    public async Task Build_ModelTypeWithInvalidCSharpName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        database.TableModels.Single().Model.SetCsType(new CsTypeDeclaration("Bad Model", "TestNamespace", ModelCsType.Class));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Bad Model'");
        await Assert.That(failure.Message).Contains("C# type name 'Bad Model'");
    }

    [Test]
    public async Task Build_ModelUsingWithInvalidNamespace_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        database.TableModels.Single().Model.SetUsings([new ModelUsing("Bad Namespace")]);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item'");
        await Assert.That(failure.Message).Contains("using namespace 'Bad Namespace'");
    }

    [Test]
    public async Task Build_ModelUsingWithNullEntry_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        database.TableModels.Single().Model.SetUsings([null!]);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Model 'Item'");
        await Assert.That(failure.Message).Contains("contains a null using namespace");
    }

    [Test]
    public async Task Build_ValuePropertyWithInvalidCSharpName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var model = database.TableModels.Single().Model;
        var property = model.ValueProperties["Id"];
        model.ValueProperties.Remove("Id");
        property.SetPropertyName("Bad Name");
        model.ValueProperties.Add(property.PropertyName, property);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Bad Name'");
        await Assert.That(failure.Message).Contains("C# property name 'Bad Name'");
    }

    [Test]
    public async Task Build_ValuePropertyWithInvalidCSharpType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var property = database.TableModels.Single().Model.ValueProperties["Id"];
        property.SetCsType(new CsTypeDeclaration("Bad Type", "TestNamespace", ModelCsType.Class));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("C# type name 'Bad Type'");
    }

    [Test]
    public async Task Build_RelationPropertyWithInvalidCSharpType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var orderModel = database.TableModels.Single(x => x.Table.DbName == "orders").Model;
        orderModel.AddProperty(new RelationProperty(
            "Customer",
            new CsTypeDeclaration("Bad Type", "TestNamespace", ModelCsType.Class),
            orderModel,
            [new RelationAttribute("users", "user_id", "FK_Order_User")]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'Order.Customer'");
        await Assert.That(failure.Message).Contains("C# type name 'Bad Type'");
    }

    [Test]
    public async Task Build_GeneratedRelationPropertyWithInvalidCSharpName_ReturnsInvalidModelFailure()
    {
        var database = CreateRelationDraft();
        var orderTable = database.TableModels.Single(x => x.Table.DbName == "orders").Table;
        orderTable.Columns.Single(x => x.DbName == "customer_id").SetDbName("id");

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'Order.'");
        await Assert.That(failure.Message).Contains("C# property name ''");
    }

    [Test]
    public async Task Build_TableModelPropertyMatchingDatabaseType_RenamesBuiltDatabaseTypeWithoutMutatingDraft()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        database.TableModels.Single().SetCsPropertyName("TestDb");

        var built = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database))
            .ValueOrException();

        await Assert.That(ReferenceEquals(database, built)).IsFalse();
        await Assert.That(database.CsType.Name).IsEqualTo("TestDb");
        await Assert.That(built.CsType.Name).IsEqualTo("TestDbDb");
        await Assert.That(built.TableModels.Single().CsPropertyName).IsEqualTo("TestDb");
    }

    [Test]
    public async Task Build_RelationPropertyRegisteredUnderWrongKey_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var orderModel = database.TableModels.Single(tm => tm.Table.DbName == "orders").Model;
        var userModel = database.TableModels.Single(tm => tm.Table.DbName == "users").Model;
        var relationProperty = new RelationProperty(
            "Customer",
            userModel.CsType,
            orderModel,
            [new RelationAttribute("users", "user_id", "FK_Order_User")]);
        orderModel.AddProperty(relationProperty);
        relationProperty.SetPropertyName("Buyer");

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'Order.Buyer'");
        await Assert.That(failure.Message).Contains("registered under key 'Customer'");
    }

    [Test]
    public async Task Build_RelationPropertyRegisteredOnWrongModel_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var orderModel = database.TableModels.Single(tm => tm.Table.DbName == "orders").Model;
        var userModel = database.TableModels.Single(tm => tm.Table.DbName == "users").Model;
        orderModel.AddProperty(new RelationProperty(
            "Customer",
            userModel.CsType,
            userModel,
            [new RelationAttribute("users", "user_id", "FK_Order_User")]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'User.Customer'");
        await Assert.That(failure.Message).Contains("registered on model 'Order'");
        await Assert.That(failure.Message).Contains("belongs to model 'User'");
    }

    [Test]
    public async Task Build_ValueAndRelationPropertiesWithSameName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var orderModel = database.TableModels.Single(tm => tm.Table.DbName == "orders").Model;
        var userModel = database.TableModels.Single(tm => tm.Table.DbName == "users").Model;
        orderModel.AddProperty(new RelationProperty(
            "OrderId",
            userModel.CsType,
            orderModel,
            [new RelationAttribute("users", "user_id", "FK_Order_User")]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("contains both a value property and a relation property named 'OrderId'");
        await Assert.That(failure.Message).Contains("Order");
    }

    [Test]
    public async Task Build_RelationPropertyPointingAtOtherTableRelationPart_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var built = new MetadataDefinitionFactory().Build(CreateRelationDraft()).ValueOrException();
        var orderModel = built.TableModels.Single(tm => tm.Table.DbName == "orders").Model;
        var userModel = built.TableModels.Single(tm => tm.Table.DbName == "users").Model;
        var wrongSidePart = userModel.RelationProperties["Order"].RelationPart;
        orderModel.RelationProperties["Customer"].SetRelationPart(wrongSidePart);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(built));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Relation property 'Order.Customer'");
        await Assert.That(failure.Message).Contains("relation part on table 'users'");
        await Assert.That(failure.Message).Contains("own table 'orders'");
    }

    [Test]
    public async Task Build_MultipleDefaultAttributes_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]),
            ("Name", typeof(string), [new ColumnAttribute("name"), new DefaultAttribute("first"), new DefaultAttribute("second")]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Name'");
        await Assert.That(failure.Message).Contains("multiple default attributes");
    }

    [Test]
    public async Task Build_DefaultNewUuidOnNonGuidProperty_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]),
            ("Token", typeof(string), [new ColumnAttribute("token"), new DefaultNewUUIDAttribute()]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("DefaultNewUUIDAttribute can only be used with Guid properties");
        await Assert.That(failure.Message).Contains("Item.Token");
        await Assert.That(failure.Message).Contains("string");
    }

    [Test]
    public async Task Build_DefaultCurrentTimestampOnNonTemporalProperty_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]),
            ("IsReady", typeof(bool), [new ColumnAttribute("is_ready"), new DefaultCurrentTimestampAttribute()]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("DefaultCurrentTimestampAttribute can only be used");
        await Assert.That(failure.Message).Contains("Item.IsReady");
        await Assert.That(failure.Message).Contains("bool");
    }

    [Test]
    public async Task Build_DefaultValueIncompatibleWithPropertyType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]),
            ("Count", typeof(int), [new ColumnAttribute("count"), new DefaultAttribute("not-a-number")]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Default value for value property 'Item.Count'");
        await Assert.That(failure.Message).Contains("not compatible with C# type 'int'");
    }

    [Test]
    public async Task Build_DefaultNewUuidWithUnsupportedVersion_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]),
            ("PublicId", typeof(Guid), [new ColumnAttribute("public_id"), new DefaultNewUUIDAttribute((UUIDVersion)999)]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("DefaultNewUUIDAttribute on value property 'Item.PublicId'");
        await Assert.That(failure.Message).Contains("unsupported UUID version");
    }

    [Test]
    public async Task Build_ViewWithoutDefinition_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateViewDraft(definition: null);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("View 'active_items'");
        await Assert.That(failure.Message).Contains("ActiveItem");
        await Assert.That(failure.Message).Contains("missing a SQL definition");
    }

    [Test]
    public async Task Build_ViewWithDefinition_SucceedsWithoutPrimaryKey()
    {
        const string definition = "select name from active_items";
        var database = CreateViewDraft(definition);

        var built = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database))
            .ValueOrException();

        var view = (ViewDefinition)built.TableModels.Single().Table;
        await Assert.That(view.Definition).IsEqualTo(definition);
        await Assert.That(view.PrimaryKeyColumns).IsEmpty();
        await Assert.That(view.Columns.Select(x => x.Index).ToArray()).IsEquivalentTo([0]);
    }

    [Test]
    public async Task Build_ViewWithExplicitEmptyDefinition_SucceedsForProviderPlaceholder()
    {
        var database = CreateViewDraft("");

        var built = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database))
            .ValueOrException();

        var view = (ViewDefinition)built.TableModels.Single().Table;
        await Assert.That(view.Definition).IsEqualTo("");
        await Assert.That(view.Columns.Select(x => x.Index).ToArray()).IsEquivalentTo([0]);
    }

    [Test]
    public async Task Build_ColumnWithNullDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var column = database.TableModels.Single().Table.Columns.Single();
        column.AddDbType(null!);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.id'");
        await Assert.That(failure.Message).Contains("null database type");
    }

    [Test]
    public async Task Build_ColumnWithEmptyDatabaseTypeName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var column = database.TableModels.Single().Table.Columns.Single();
        column.AddDbType(new DatabaseColumnType(DatabaseType.MySQL, ""));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.id'");
        await Assert.That(failure.Message).Contains("empty database type name");
        await Assert.That(failure.Message).Contains("MySQL");
    }

    [Test]
    public async Task Build_ColumnWithUnsupportedDatabaseType_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var column = database.TableModels.Single().Table.Columns.Single();
        column.AddDbType(new DatabaseColumnType((DatabaseType)999, "int"));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.id'");
        await Assert.That(failure.Message).Contains("unsupported database type");
        await Assert.That(failure.Message).Contains("999");
    }

    [Test]
    public async Task Build_EnumClrTypeWithoutEnumMetadata_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var property = database.TableModels.Single().Model.ValueProperties["Id"];
        property.SetCsType(new CsTypeDeclaration(typeof(RuntimeStatus)));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Item.Id");
        await Assert.That(failure.Message).Contains("no enum metadata");
    }

    [Test]
    public async Task Build_EnumPropertyWithoutValues_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var property = database.TableModels.Single().Model.ValueProperties["Id"];
        property.SetCsType(new CsTypeDeclaration("StatusValue", "TestNamespace", ModelCsType.Enum));
        property.SetEnumProperty(new EnumProperty());

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Enum value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("at least one enum value");
    }

    [Test]
    public async Task Build_EnumPropertyWithInvalidMemberName_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var property = database.TableModels.Single().Model.ValueProperties["Id"];
        property.SetCsType(new CsTypeDeclaration("StatusValue", "TestNamespace", ModelCsType.Enum));
        property.SetEnumProperty(new EnumProperty(csEnumValues: [("Not Valid", 1)]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Enum value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("invalid C# enum member name 'Not Valid'");
    }

    [Test]
    public async Task Build_EnumPropertyWithDuplicateDatabaseValues_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var property = database.TableModels.Single().Model.ValueProperties["Id"];
        property.SetCsType(new CsTypeDeclaration("StatusValue", "TestNamespace", ModelCsType.Enum));
        property.SetEnumProperty(new EnumProperty(
            enumValues: [("PRI", 1), ("pri", 2)],
            csEnumValues: [("Primary", 1), ("AlternatePrimary", 2)]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Enum value property 'Item.Id'");
        await Assert.That(failure.Message).Contains("duplicate database enum value 'PRI'");
    }

    [Test]
    public async Task Build_EnumPropertyWithEmptyDatabaseValueAndValidCsName_Succeeds()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var property = database.TableModels.Single().Model.ValueProperties["Id"];
        property.SetCsType(new CsTypeDeclaration("StatusValue", "TestNamespace", ModelCsType.Enum));
        property.SetEnumProperty(new EnumProperty(
            enumValues: [("", 1), ("PRI", 2)],
            csEnumValues: [("Empty", 1), ("PRI", 2)]));

        var built = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database))
            .ValueOrException();

        var enumProperty = built.TableModels.Single().Model.ValueProperties["Id"].EnumProperty!.Value;
        await Assert.That(enumProperty.DbEnumValues.Select(x => x.name).ToArray()).IsEquivalentTo(["", "PRI"]);
        await Assert.That(enumProperty.CsEnumValues.Select(x => x.name).ToArray()).IsEquivalentTo(["Empty", "PRI"]);
    }

    [Test]
    public async Task Build_ExternalEnumPropertyWithEmptyDatabaseValueWithoutCsNames_Succeeds()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var property = database.TableModels.Single().Model.ValueProperties["Id"];
        property.SetCsType(new CsTypeDeclaration("COLUMN_KEY", "TestNamespace", ModelCsType.Enum));
        property.SetEnumProperty(new EnumProperty(
            enumValues: [("", 1), ("PRI", 2)],
            declaredInClass: false));

        var built = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database))
            .ValueOrException();

        var enumProperty = built.TableModels.Single().Model.ValueProperties["Id"].EnumProperty!.Value;
        await Assert.That(enumProperty.DbEnumValues.Select(x => x.name).ToArray()).IsEquivalentTo(["", "PRI"]);
        await Assert.That(enumProperty.CsEnumValues).IsEmpty();
    }

    [Test]
    public async Task Build_ColumnWithoutValueProperty_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var orphanColumn = new ColumnDefinition("ghost", table);
        table.SetColumns(table.Columns.Concat([orphanColumn]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Column 'items.ghost'");
        await Assert.That(failure.Message).Contains("has no value property");
    }

    [Test]
    public async Task Build_ValuePropertyReferencingUnregisteredColumn_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var tableModel = database.TableModels.Single();
        var ghostProperty = new ValueProperty(
            "Ghost",
            new CsTypeDeclaration(typeof(string)),
            tableModel.Model,
            [new ColumnAttribute("ghost")]);
        var ghostColumn = new ColumnDefinition("ghost", tableModel.Table);
        ghostColumn.SetValueProperty(ghostProperty);
        tableModel.Model.AddProperty(ghostProperty);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Value property 'Item.Ghost'");
        await Assert.That(failure.Message).Contains("not registered on the table");
    }

    [Test]
    public async Task Build_IndexAttachedToWrongTable_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var users = database.TableModels.Single(tm => tm.Table.DbName == "users").Table;
        var orders = database.TableModels.Single(tm => tm.Table.DbName == "orders").Table;
        var userId = users.Columns.Single(column => column.DbName == "user_id");
        orders.ColumnIndices.Add(new ColumnIndex("idx_wrong_table", IndexCharacteristic.Simple, IndexType.BTREE, [userId]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Index 'idx_wrong_table' is attached to table 'orders'");
        await Assert.That(failure.Message).Contains("belongs to table 'users'");
    }

    [Test]
    public async Task Build_IndexReferencingUnregisteredColumn_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var unregisteredColumn = new ColumnDefinition("ghost", table);
        table.ColumnIndices.Add(new ColumnIndex("idx_ghost", IndexCharacteristic.Simple, IndexType.BTREE, [unregisteredColumn]));

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Index 'idx_ghost' on table 'items'");
        await Assert.That(failure.Message).Contains("not registered on the table");
    }

    [Test]
    public async Task Build_ExistingRelationMissingCandidateKey_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var users = database.TableModels.Single(tm => tm.Table.DbName == "users");
        var orders = database.TableModels.Single(tm => tm.Table.DbName == "orders");
        var customerId = orders.Table.Columns.Single(column => column.DbName == "customer_id");
        var foreignKeyIndex = new ColumnIndex("FK_Broken", IndexCharacteristic.ForeignKey, IndexType.BTREE, [customerId]);
        orders.Table.ColumnIndices.Add(foreignKeyIndex);
        var relation = new RelationDefinition("FK_Broken", RelationType.OneToMany);
        var foreignKeyPart = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, "Customer");
        relation.ForeignKey = foreignKeyPart;
        foreignKeyIndex.RelationParts.Add(foreignKeyPart);
        var relationProperty = new RelationProperty(
            "Customer",
            users.Model.CsType,
            orders.Model,
            [new RelationAttribute("users", "user_id", "FK_Broken")]);
        relationProperty.SetRelationPart(foreignKeyPart);
        orders.Model.AddProperty(relationProperty);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Existing relation 'FK_Broken'");
        await Assert.That(failure.Message).Contains("missing a candidate-key part");
    }

    [Test]
    public async Task Build_ExistingRelationPartMissingIndexBackReference_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateRelationDraft();
        var users = database.TableModels.Single(tm => tm.Table.DbName == "users");
        var orders = database.TableModels.Single(tm => tm.Table.DbName == "orders");
        var userId = users.Table.Columns.Single(column => column.DbName == "user_id");
        var customerId = orders.Table.Columns.Single(column => column.DbName == "customer_id");
        var candidateKeyIndex = new ColumnIndex("users_primary_key", IndexCharacteristic.PrimaryKey, IndexType.BTREE, [userId]);
        var foreignKeyIndex = new ColumnIndex("FK_Broken", IndexCharacteristic.ForeignKey, IndexType.BTREE, [customerId]);
        users.Table.ColumnIndices.Add(candidateKeyIndex);
        orders.Table.ColumnIndices.Add(foreignKeyIndex);
        var relation = new RelationDefinition("FK_Broken", RelationType.OneToMany);
        var foreignKeyPart = new RelationPart(foreignKeyIndex, relation, RelationPartType.ForeignKey, "Customer");
        var candidateKeyPart = new RelationPart(candidateKeyIndex, relation, RelationPartType.CandidateKey, "Orders");
        relation.ForeignKey = foreignKeyPart;
        relation.CandidateKey = candidateKeyPart;
        candidateKeyIndex.RelationParts.Add(candidateKeyPart);
        var relationProperty = new RelationProperty(
            "Customer",
            users.Model.CsType,
            orders.Model,
            [new RelationAttribute("users", "user_id", "FK_Broken")]);
        relationProperty.SetRelationPart(foreignKeyPart);
        orders.Model.AddProperty(relationProperty);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Existing relation 'FK_Broken'");
        await Assert.That(failure.Message).Contains("foreign-key part is not registered");
    }

    [Test]
    public async Task Build_DuplicateColumnDraft_ReturnsInvalidModelFailure()
    {
        var database = CreateSingleTableDraft(
            ("FirstId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]),
            ("SecondId", typeof(int), [new ColumnAttribute("id")]));

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        var failureMessage = result.Failure.ToString()!;
        await Assert.That(failureMessage).Contains("Duplicate column definition for 'id'");
        await Assert.That(failureMessage).Contains("FirstId");
        await Assert.That(failureMessage).Contains("SecondId");
    }

    [Test]
    public async Task Build_DuplicateColumnReference_ReturnsInvalidModelFailureBeforeSnapshot()
    {
        var database = CreateSingleTableDraft(
            ("Id", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("id")]));
        var table = database.TableModels.Single().Table;
        var idColumn = table.Columns.Single();
        table.SetColumns([idColumn, idColumn]);

        var result = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database));

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Duplicate column definition for 'id'");
        await Assert.That(failure.Message).Contains("Id");
    }

    [Test]
    public async Task Build_TableWithoutPrimaryKey_ReturnsInvalidModelFailure()
    {
        var database = CreateSingleTableDraft(
            ("Name", typeof(string), [new ColumnAttribute("name")]));

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        var failureMessage = result.Failure.ToString()!;
        await Assert.That(failureMessage).Contains("missing a primary key");
        await Assert.That(failureMessage).Contains("items");
    }

    [Test]
    public async Task Build_DuplicateRelationPropertiesForSameForeignKey_ReturnsInvalidModelFailure()
    {
        var database = CreateRelationDraft();
        var userModel = database.TableModels.Single(tm => tm.Table.DbName == "users").Model;
        var orderModel = database.TableModels.Single(tm => tm.Table.DbName == "orders").Model;

        orderModel.AddProperty(new RelationProperty(
            "Customer",
            userModel.CsType,
            orderModel,
            [new RelationAttribute("users", "user_id", "FK_Order_User")]));
        orderModel.AddProperty(new RelationProperty(
            "Buyer",
            userModel.CsType,
            orderModel,
            [new RelationAttribute("users", "user_id", "FK_Order_User")]));

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        var failureMessage = failure.ToString()!;
        await Assert.That(failureMessage).Contains("Multiple relation properties");
        await Assert.That(failureMessage).Contains("FK_Order_User");
        await Assert.That(failureMessage).Contains("Customer");
        await Assert.That(failureMessage).Contains("Buyer");
    }

    [Test]
    public async Task Build_ForeignKeyWithEmptyConstraintName_ReturnsInvalidModelFailure()
    {
        var database = CreateRelationDraft(foreignKeyName: "");

        var result = new MetadataDefinitionFactory().Build(database);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        var failureMessage = failure.ToString()!;
        await Assert.That(failureMessage).Contains("could not create its index");
        await Assert.That(failureMessage).Contains("Index name cannot be empty");
        await Assert.That(failureMessage).Contains("orders.customer_id");
    }

    [Test]
    public async Task BuildProviderMetadata_ProviderStyleDraft_AssignsInterfacesOrdinalsAndPrimaryKeyIndex()
    {
        var database = CreateProviderStyleDraft();

        var built = new MetadataDefinitionFactory().BuildProviderMetadata(database).ValueOrException();

        var table = built.TableModels.Single().Table;

        await Assert.That(table.Model.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(table.Model.ModelInstanceInterface!.Value.Name).IsEqualTo("IItem");
        await Assert.That(table.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 1]);
        await Assert.That(table.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.PrimaryKey)).IsTrue();
    }

    [Test]
    public async Task BuildProviderMetadata_ProviderStyleDraft_FinalizesSnapshotWithoutMutatingDraft()
    {
        var database = CreateProviderStyleDraft();
        var draftTable = database.TableModels.Single().Table;
        var draftModel = database.TableModels.Single().Model;

        var built = new MetadataDefinitionFactory()
            .BuildProviderMetadata(MetadataDefinitionDraft.FromMutableMetadata(database))
            .ValueOrException();

        var builtTable = built.TableModels.Single().Table;

        await Assert.That(ReferenceEquals(database, built)).IsFalse();
        await Assert.That(builtTable.Model.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(builtTable.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 1]);
        await Assert.That(builtTable.ColumnIndices.Any(x => x.Characteristic == IndexCharacteristic.PrimaryKey)).IsTrue();

        await Assert.That(draftModel.ModelInstanceInterface.HasValue).IsFalse();
        await Assert.That(draftTable.Columns.Select(c => c.Index).ToArray()).IsEquivalentTo([0, 0]);
        await Assert.That(draftTable.ColumnIndices.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Build_StubTableDraft_PreservesStubFlagAndSkipsFinalization()
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var stubModel = new ModelDefinition(new CsTypeDeclaration("MissingModel", "TestNamespace", ModelCsType.Interface));
        stubModel.SetInterfaces([new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)]);
        var stubTableModel = new TableModel("MissingModels", database, stubModel, isStub: true);
        database.SetTableModels([stubTableModel]);

        var built = new MetadataDefinitionFactory()
            .Build(MetadataDefinitionDraft.FromMutableMetadata(database))
            .ValueOrException();

        await Assert.That(built.TableModels.Single().IsStub).IsTrue();
        await Assert.That(stubTableModel.IsStub).IsTrue();
    }

    private static DatabaseDefinition CreateRelationDraft(string foreignKeyName = "FK_Order_User")
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));

        var userModel = CreateTableModel(database, "Users", "User", "users").Model;
        AddValueProperties(
            userModel,
            ("UserId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("user_id")]),
            ("UserName", typeof(string), [new ColumnAttribute("user_name")]));

        var orderModel = CreateTableModel(database, "Orders", "Order", "orders").Model;
        AddValueProperties(
            orderModel,
            ("OrderId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("order_id")]),
            ("CustomerId", typeof(int), [new ForeignKeyAttribute("users", "user_id", foreignKeyName), new ColumnAttribute("customer_id")]),
            ("Amount", typeof(decimal), [new ColumnAttribute("amount")]));

        database.SetTableModels([
            userModel.TableModel,
            orderModel.TableModel
        ]);

        return database;
    }

    private static DatabaseDefinition CreateProviderStyleDraft()
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var table = new TableDefinition("items");
        var tableModel = new TableModel("Items", database, table, "Item");

        AddValueProperties(
            tableModel.Model,
            ("ItemId", typeof(int), [new PrimaryKeyAttribute(), new ColumnAttribute("item_id")]),
            ("Name", typeof(string), [new ColumnAttribute("name")]));

        database.SetTableModels([tableModel]);
        return database;
    }

    private static DatabaseDefinition CreateSingleTableDraft(params (string PropertyName, Type CsType, Attribute[] Attributes)[] properties)
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = CreateTableModel(database, "Items", "Item", "items").Model;

        AddValueProperties(model, properties);

        database.SetTableModels([model.TableModel]);
        return database;
    }

    private static DatabaseDefinition CreateViewDraft(string? definition)
    {
        var database = new DatabaseDefinition(
            "TestDb",
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = new ModelDefinition(new CsTypeDeclaration("ActiveItem", "TestNamespace", ModelCsType.Class));
        model.SetInterfaces([new CsTypeDeclaration("IViewModel", "DataLinq.Interfaces", ModelCsType.Interface)]);

        var table = (ViewDefinition)MetadataFactory.ParseTable(model).ValueOrException();
        table.SetDbName("active_items");
        if (definition != null)
            table.SetDefinition(definition);

        var tableModel = new TableModel("ActiveItems", database, model, table);
        AddValueProperties(
            model,
            ("Name", typeof(string), [new ColumnAttribute("name")]));

        database.SetTableModels([tableModel]);
        return database;
    }

    private static TableModel CreateTableModel(
        DatabaseDefinition database,
        string csPropertyName,
        string modelName,
        string tableName)
    {
        var model = new ModelDefinition(new CsTypeDeclaration(modelName, "TestNamespace", ModelCsType.Class));
        model.SetInterfaces([new CsTypeDeclaration("ITableModel", "DataLinq.Interfaces", ModelCsType.Interface)]);

        var table = MetadataFactory.ParseTable(model).ValueOrException();
        table.SetDbName(tableName);

        return new TableModel(csPropertyName, database, model, table);
    }

    private static void AddValueProperties(
        ModelDefinition model,
        params (string PropertyName, Type CsType, Attribute[] Attributes)[] properties)
    {
        var columns = properties
            .Select(property =>
            {
                var valueProperty = new ValueProperty(
                    property.PropertyName,
                    new CsTypeDeclaration(property.CsType),
                    model,
                    property.Attributes);
                model.AddProperty(valueProperty);
                return MetadataFactory.ParseColumn(model.Table, valueProperty);
            })
            .ToArray();

        model.Table.SetColumns(columns);
    }

    private enum RuntimeStatus
    {
        Active = 1,
    }
}
