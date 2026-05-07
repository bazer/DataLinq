using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models;
using DataLinq.ErrorHandling;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ThrowAway.Extensions;

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
    [Column("score"), DataLinq.Attributes.DefaultAttribute(56)] public abstract int Score { get; }
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

    [Test]
    public async Task ReadSyntaxTrees_QualifiedDbReadType_ParsesTableModel()
    {
        const string code = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel
{
    public TestDb(DataSourceAccess dataSource) { }
    public DataLinq.DbRead<UserModel> Users { get; }
}

[Table("users")]
public abstract partial class UserModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<UserModel, TestDb>(rowData, dataSource), ITableModel<TestDb>
{
    [Column("id"), PrimaryKey] public abstract int Id { get; }
}
""";

        var declarations = GetSyntaxDeclarations(code);
        var factory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions());

        var databaseDefinition = factory.ReadSyntaxTrees(declarations).Single().Value;

        await Assert.That(databaseDefinition.TableModels.Length).IsEqualTo(1);
        await Assert.That(databaseDefinition.TableModels.Single().CsPropertyName).IsEqualTo("Users");
        await Assert.That(databaseDefinition.TableModels.Single().Model.CsType.Name).IsEqualTo("UserModel");
    }

    [Test]
    public async Task ReadSyntaxTrees_QualifiedModelInterfaces_ParsesDatabaseAndTableModel()
    {
        const string code = """
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public partial class TestDb : DataLinq.Interfaces.IDatabaseModel
{
    public TestDb(DataSourceAccess dataSource) { }
    public DbRead<UserModel> Users { get; }
}

[Table("users")]
public abstract partial class UserModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<UserModel, TestNamespace.TestDb>(rowData, dataSource), DataLinq.Interfaces.ITableModel<TestNamespace.TestDb>
{
    [Column("id"), PrimaryKey] public abstract int Id { get; }
}
""";

        var declarations = GetSyntaxDeclarations(code);
        var factory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions());
        var resultList = factory.ReadSyntaxTrees(declarations);

        await Assert.That(resultList.Count).IsEqualTo(1);
        await Assert.That(resultList.Single().HasValue).IsTrue();

        var databaseDefinition = resultList.Single().Value;
        await Assert.That(databaseDefinition.CsType.Name).IsEqualTo("TestDb");
        await Assert.That(databaseDefinition.TableModels.Length).IsEqualTo(1);
        await Assert.That(databaseDefinition.TableModels.Single().CsPropertyName).IsEqualTo("Users");
        await Assert.That(databaseDefinition.TableModels.Single().Model.OriginalInterfaces.Single().Name).IsEqualTo("ITableModel<TestDb>");
    }

    [Test]
    public async Task ReadSyntaxTrees_QualifiedAttributeNames_ParsesModelProperties()
    {
        const string code = """
using DataLinq;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel
{
    public TestDb(DataSourceAccess dataSource) { }
    public DbRead<UserModel> Users { get; }
}

public partial interface IUserModel { }

[DataLinq.Attributes.TableAttribute("users")]
[DataLinq.Attributes.InterfaceAttribute<IUserModel>]
public abstract partial class UserModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<UserModel, TestDb>(rowData, dataSource), ITableModel<TestDb>
{
    [DataLinq.Attributes.ColumnAttribute("id"), DataLinq.Attributes.PrimaryKeyAttribute] public abstract int Id { get; }
}
""";

        var declarations = GetSyntaxDeclarations(code);
        var factory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions());

        var databaseDefinition = factory.ReadSyntaxTrees(declarations).Single().Value;
        var tableModel = databaseDefinition.TableModels.Single();

        await Assert.That(tableModel.Table.DbName).IsEqualTo("users");
        await Assert.That(tableModel.Model.ModelInstanceInterface!.Value.Name).IsEqualTo("IUserModel");
        await Assert.That(tableModel.Model.ValueProperties.Count).IsEqualTo(1);
        await Assert.That(tableModel.Model.ValueProperties["Id"].Column.DbName).IsEqualTo("id");
        await Assert.That(tableModel.Model.ValueProperties["Id"].Column.PrimaryKey).IsTrue();
    }

    [Test]
    public async Task ReadSyntaxTrees_UnsupportedPropertyTypeSyntax_ReturnsInvalidModelFailure()
    {
        const string code = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel
{
    public TestDb(DataSourceAccess dataSource) { }
    public DbRead<UnsafeModel> UnsafeRows { get; }
}

[Table("unsafe_rows")]
public abstract partial class UnsafeModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<UnsafeModel, TestDb>(rowData, dataSource), ITableModel<TestDb>
{
    [Column("callback"), PrimaryKey] public abstract delegate*<void> Callback { get; }
}
""";

        var declarations = GetSyntaxDeclarations(code);
        var factory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions());

        var result = factory.ReadSyntaxTrees(declarations).Single();

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsNotEqualTo(DLFailureType.Exception);

        var failureMessage = failure.ToString();
        await Assert.That(failureMessage).Contains("Callback");
        await Assert.That(failureMessage).Contains("unsupported C# type syntax");
        await Assert.That(failureMessage).DoesNotContain("[Exception]");
    }

    [Test]
    public async Task ReadSyntaxTrees_UnsupportedModelBaseTypeSyntax_ReturnsInvalidModelFailure()
    {
        const string code = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel
{
    public TestDb(DataSourceAccess dataSource) { }
    public DbRead<UnsafeModel> UnsafeRows { get; }
}

[Table("unsafe_rows")]
public abstract partial class UnsafeModel(IRowData rowData, IDataSourceAccess dataSource) : delegate*<void>, ITableModel<TestDb>
{
    [Column("id"), PrimaryKey] public abstract int Id { get; }
}
""";

        var declarations = GetSyntaxDeclarations(code);
        var factory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions());

        var result = factory.ReadSyntaxTrees(declarations).Single();

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsNotEqualTo(DLFailureType.Exception);

        var failureMessage = failure.ToString();
        await Assert.That(failureMessage).Contains("UnsafeModel");
        await Assert.That(failureMessage).Contains("delegate*<void>");
        await Assert.That(failureMessage).Contains("unsupported C# type syntax");
        await Assert.That(failureMessage).DoesNotContain("[Exception]");
    }

    [Test]
    public async Task ReadSyntaxTrees_ReferencedMalformedDeclarationWithoutModelInterface_ReturnsInvalidModelFailure()
    {
        const string code = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel
{
    public TestDb(DataSourceAccess dataSource) { }
    public DbRead<UnsafeModel> UnsafeRows { get; }
}

[Table("unsafe_rows")]
public abstract partial class UnsafeModel(IRowData rowData, IDataSourceAccess dataSource) : delegate*<void>
{
    [Column("id"), PrimaryKey] public abstract int Id { get; }
}
""";

        var declarations = GetSyntaxDeclarations(code);
        var factory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions());

        var result = factory.ReadSyntaxTrees(declarations).Single();

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsNotEqualTo(DLFailureType.Exception);

        var failureMessage = failure.ToString();
        await Assert.That(failureMessage).Contains("UnsafeModel");
        await Assert.That(failureMessage).Contains("delegate*<void>");
        await Assert.That(failureMessage).Contains("unsupported C# type syntax");
        await Assert.That(failureMessage).DoesNotContain("[Exception]");
    }

    private static ImmutableArray<TypeDeclarationSyntax> GetSyntaxDeclarations(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetCompilationUnitRoot();
        return root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
    }
}
