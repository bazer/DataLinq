using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DataLinq.Tests.Unit.Core;

public class MetadataFromModelsFactoryTests
{
    [Test]
    public async Task ReadSyntaxTrees_SimpleDb_ParsesExpectedDatabaseShape()
    {
        const string code = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;
using System;
using System.Collections.Generic;

namespace TestNamespace;

public partial interface ITestDb : IDatabaseModel { }

[Database("test_db_from_syntax")]
public partial class TestDb : ITestDb
{
    public TestDb(DataSourceAccess dataSource) { }
    public DbRead<UserModel> Users { get; }
    public DbRead<OrderModel> Orders { get; }
}

public partial interface IUserModel { }

[Table("users")]
[Interface<IUserModel>]
public abstract partial class UserModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<UserModel, ITestDb>(rowData, dataSource), ITableModel<TestDb>
{
    [Column("id"), PrimaryKey] public abstract int Id { get; }
    [Column("name")] public abstract string Name { get; }
    [Relation("orders", "user_id", "FK_Order_User")] public abstract IImmutableRelation<OrderModel> Orders { get; }
}

public partial interface IOrderModel { }

[Table("orders")]
[Interface<IOrderModel>]
public abstract partial class OrderModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<OrderModel, ITestDb>(rowData, dataSource), ITableModel<TestDb>
{
    [Column("order_id"), PrimaryKey] public abstract int OrderId { get; }
    [Column("user_id"), ForeignKey("users", "id", "FK_Order_User")] public abstract int UserId { get; }
    [Column("amount")] public abstract decimal Amount { get; }
    [Relation("users", "id", "FK_Order_User")] public abstract UserModel User { get; }
}
""";

        var declarations = GetSyntaxDeclarations(code);
        var factory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions());
        var resultList = factory.ReadSyntaxTrees(declarations);

        await Assert.That(resultList.Count).IsEqualTo(1);
        await Assert.That(resultList[0].HasValue).IsTrue();

        var databaseDefinition = resultList[0].Value;
        await Assert.That(databaseDefinition.CsType.Name).IsEqualTo("TestDb");
        await Assert.That(databaseDefinition.DbName).IsEqualTo("test_db_from_syntax");
        await Assert.That(databaseDefinition.TableModels.Length).IsEqualTo(2);

        var userTableModel = databaseDefinition.TableModels.Single(tm => tm.CsPropertyName == "Users");
        await Assert.That(userTableModel.Model.CsType.Name).IsEqualTo("UserModel");
        await Assert.That(userTableModel.Table.DbName).IsEqualTo("users");
        await Assert.That(userTableModel.Model.ValueProperties.Count).IsEqualTo(2);
        await Assert.That(userTableModel.Model.RelationProperties.Count).IsEqualTo(1);
        await Assert.That(userTableModel.Model.ValueProperties.ContainsKey("Id")).IsTrue();
        await Assert.That(userTableModel.Model.ValueProperties["Id"].Column.PrimaryKey).IsTrue();
        await Assert.That(userTableModel.Model.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(userTableModel.Model.ModelInstanceInterface!.Value.Name).IsEqualTo("IUserModel");

        var orderTableModel = databaseDefinition.TableModels.Single(tm => tm.CsPropertyName == "Orders");
        await Assert.That(orderTableModel.Model.CsType.Name).IsEqualTo("OrderModel");
        await Assert.That(orderTableModel.Table.DbName).IsEqualTo("orders");
        await Assert.That(orderTableModel.Model.ValueProperties.Count).IsEqualTo(3);
        await Assert.That(orderTableModel.Model.RelationProperties.Count).IsEqualTo(1);
        await Assert.That(orderTableModel.Model.ValueProperties.ContainsKey("OrderId")).IsTrue();
        await Assert.That(orderTableModel.Model.ValueProperties["OrderId"].Column.PrimaryKey).IsTrue();
        await Assert.That(orderTableModel.Model.ValueProperties.ContainsKey("UserId")).IsTrue();
        await Assert.That(orderTableModel.Model.ValueProperties["UserId"].Column.ForeignKey).IsTrue();
        await Assert.That(orderTableModel.Model.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(orderTableModel.Model.ModelInstanceInterface!.Value.Name).IsEqualTo("IOrderModel");

        MetadataFactory.ParseIndices(databaseDefinition);
        MetadataFactory.ParseRelations(databaseDefinition);

        await Assert.That(userTableModel.Model.RelationProperties["Orders"].RelationPart).IsNotNull();
        await Assert.That(orderTableModel.Model.RelationProperties["User"].RelationPart).IsNotNull();
        await Assert.That(ReferenceEquals(
            userTableModel.Model.RelationProperties["Orders"].RelationPart.Relation,
            orderTableModel.Model.RelationProperties["User"].RelationPart.Relation)).IsTrue();
    }

    [Test]
    public async Task ReadSyntaxTrees_SourceDraftPreservesSourceLocations()
    {
        const string code = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public partial interface ITestDb : IDatabaseModel { }

[Database("source_db")]
public partial class TestDb : ITestDb
{
    public TestDb(DataSourceAccess dataSource) { }
    public DbRead<UserModel> Users { get; }
}

[Table("users")]
public abstract partial class UserModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<UserModel, ITestDb>(rowData, dataSource), ITableModel<TestDb>
{
    [Column("id"), PrimaryKey] public abstract int Id { get; }
    [Column("score"), Default(56)] public abstract int Score { get; }
}
""";

        var syntaxTree = CSharpSyntaxTree.ParseText(code, path: "/DataLinq/tests/SourceDraftModels.cs");
        var root = syntaxTree.GetCompilationUnitRoot();
        var declarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
        var factory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions());
        var databaseDefinition = factory.ReadSyntaxTrees(declarations).Single().Value;

        await Assert.That(databaseDefinition.CsFile.HasValue).IsTrue();
        await Assert.That(databaseDefinition.GetAttributeSourceLocation(databaseDefinition.Attributes.OfType<DatabaseAttribute>().Single()).HasValue).IsTrue();

        var userModel = databaseDefinition.TableModels.Single().Model;
        await Assert.That(userModel.CsFile.HasValue).IsTrue();
        await Assert.That(userModel.GetAttributeSourceLocation(userModel.Attributes.OfType<TableAttribute>().Single()).HasValue).IsTrue();

        var scoreProperty = userModel.ValueProperties["Score"];
        await Assert.That(scoreProperty.SourceInfo.HasValue).IsTrue();
        await Assert.That(scoreProperty.SourceInfo!.Value.DefaultValueExpressionSpan.HasValue).IsTrue();
        await Assert.That(scoreProperty.GetAttributeSourceLocation(scoreProperty.Attributes.OfType<DefaultAttribute>().Single()).HasValue).IsTrue();

        var defaultSpanInfo = scoreProperty.SourceInfo.Value.DefaultValueExpressionSpan!.Value;
        var defaultSpan = new TextSpan(defaultSpanInfo.Start, defaultSpanInfo.Length);
        await Assert.That(syntaxTree.GetText().ToString(defaultSpan)).IsEqualTo("56");
    }

    private static ImmutableArray<TypeDeclarationSyntax> GetSyntaxDeclarations(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetCompilationUnitRoot();
        return root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
    }
}
