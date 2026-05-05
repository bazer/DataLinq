using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
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

    private static DatabaseDefinition CreateRelationDraft()
    {
        const string foreignKeyName = "FK_Order_User";

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
}
