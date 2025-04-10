using System.Collections.Immutable;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Core.Factories.Models; // Where the factory resides
using DataLinq.Metadata;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DataLinq.Tests.Core
{
    // Dummy classes needed for typeof() in some base class definitions if used
    // Or for CsTypeDeclaration creation if not using typeof()
    public interface IDummyDb : DataLinq.Interfaces.IDatabaseModel { }
    public class DummyDb : IDummyDb { public DummyDb(DataLinq.Mutation.DataSourceAccess d) { } }


    /*
        Focuses on the ReadSyntaxTrees method.

        Uses a helper (GetSyntaxDeclarations) to parse C# code strings into the ImmutableArray<TypeDeclarationSyntax> the factory expects.

        The TestReadSyntaxTrees_SimpleDb test provides a representative C# code snippet with a database model, two table models, interfaces, attributes, and relations.

        It asserts that the factory correctly parses this structure into a DatabaseDefinition with the expected table/model names, property counts, primary/foreign keys, relation properties, and interface links.
     */

    public class MetadataFromModelsFactoryTests
    {
        private ImmutableArray<TypeDeclarationSyntax> GetSyntaxDeclarations(string code)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetCompilationUnitRoot();
            return root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
        }

        [Fact]
        public void TestReadSyntaxTrees_SimpleDb()
        {
            // Arrange
            string code = @"
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;
using System;
using System.Collections.Generic; // Required for IImmutableRelation

namespace TestNamespace;

// Base classes (simplified for testing)
// public abstract class Immutable<T, M>(RowData rowData, DataSourceAccess dataSource) : IImmutableInstance<M> where M : class, IDatabaseModel { }

public partial interface ITestDb : IDatabaseModel { } // Explicit interface

[Database(""test_db_from_syntax"")]
public partial class TestDb : ITestDb // Implement the specific interface
{
    public TestDb(DataSourceAccess dataSource) { /* ... */}
    public DbRead<UserModel> Users { get; }
    public DbRead<OrderModel> Orders { get; }
}

public partial interface IUserModel : ITableModel<ITestDb> { } // Interface for Model

[Table(""users"")]
[Interface<IUserModel>] // Link model to interface
public abstract partial class UserModel(RowData rowData, DataSourceAccess dataSource) : Immutable<UserModel, ITestDb>(rowData, dataSource), IUserModel // Use specific interface
{
    [Column(""id""), PrimaryKey] public abstract int Id { get; }
    [Column(""name"")] public abstract string Name { get; }
    [Relation(""orders"", ""user_id"", ""FK_Order_User"")] public abstract IImmutableRelation<OrderModel> Orders { get; } // Relation Property
}

public partial interface IOrderModel : ITableModel<ITestDb> { }

[Table(""orders"")]
[Interface<IOrderModel>]
public abstract partial class OrderModel(RowData rowData, DataSourceAccess dataSource) : Immutable<OrderModel, ITestDb>(rowData, dataSource), IOrderModel
{
    [Column(""order_id""), PrimaryKey] public abstract int OrderId { get; }
    [Column(""user_id""), ForeignKey(""users"", ""id"", ""FK_Order_User"")] public abstract int UserId { get; }
    [Column(""amount"")] public abstract decimal Amount { get; }
    [Relation(""users"", ""id"", ""FK_Order_User"")] public abstract UserModel User { get; } // Relation Property
}
";
            var declarations = GetSyntaxDeclarations(code);
            var factory = new MetadataFromModelsFactory(new MetadataFromInterfacesFactoryOptions());

            // Act
            // The factory returns a list of Option<DatabaseDefinition>, one for each IDatabaseModel found
            var resultList = factory.ReadSyntaxTrees(declarations);

            // Assert
            Assert.Single(resultList); // Expecting one database definition
            var dbOption = resultList[0];
            Assert.True(dbOption.HasValue, dbOption.HasFailed ? dbOption.Failure.ToString() : "DB parsing failed");
            var dbDefinition = dbOption.Value;

            Assert.Equal("TestDb", dbDefinition.CsType.Name); // Name derived from class implementing IDatabaseModel
            Assert.Equal("test_db_from_syntax", dbDefinition.DbName); // Name from attribute
            Assert.Equal(2, dbDefinition.TableModels.Length);

            // Verify User Table
            var userTableModel = dbDefinition.TableModels.SingleOrDefault(tm => tm.CsPropertyName == "Users");
            Assert.NotNull(userTableModel);
            Assert.Equal("UserModel", userTableModel.Model.CsType.Name);
            Assert.Equal("users", userTableModel.Table.DbName);
            Assert.Equal(2, userTableModel.Model.ValueProperties.Count);
            Assert.Single(userTableModel.Model.RelationProperties);
            Assert.True(userTableModel.Model.ValueProperties.ContainsKey("Id"));
            Assert.True(userTableModel.Model.ValueProperties["Id"].Column.PrimaryKey);
            Assert.NotNull(userTableModel.Model.ModelInstanceInterface); // Check interface generated/parsed
            Assert.Equal("IUserModel", userTableModel.Model.ModelInstanceInterface.Value.Name);


            // Verify Order Table
            var orderTableModel = dbDefinition.TableModels.SingleOrDefault(tm => tm.CsPropertyName == "Orders");
            Assert.NotNull(orderTableModel);
            Assert.Equal("OrderModel", orderTableModel.Model.CsType.Name);
            Assert.Equal("orders", orderTableModel.Table.DbName);
            Assert.Equal(3, orderTableModel.Model.ValueProperties.Count);
            Assert.Single(orderTableModel.Model.RelationProperties);
            Assert.True(orderTableModel.Model.ValueProperties.ContainsKey("OrderId"));
            Assert.True(orderTableModel.Model.ValueProperties["OrderId"].Column.PrimaryKey);
            Assert.True(orderTableModel.Model.ValueProperties.ContainsKey("UserId"));
            Assert.True(orderTableModel.Model.ValueProperties["UserId"].Column.ForeignKey);
            Assert.NotNull(orderTableModel.Model.ModelInstanceInterface);
            Assert.Equal("IOrderModel", orderTableModel.Model.ModelInstanceInterface.Value.Name);


            // Verify Relations (simple check, detailed tested elsewhere)
            // Manually call ParseRelations as the factory might not do it automatically anymore
            MetadataFactory.ParseIndices(dbDefinition); // Needed before relations
            MetadataFactory.ParseRelations(dbDefinition);
            Assert.NotNull(userTableModel.Model.RelationProperties["Orders"].RelationPart);
            Assert.NotNull(orderTableModel.Model.RelationProperties["User"].RelationPart);
            Assert.Same(userTableModel.Model.RelationProperties["Orders"].RelationPart.Relation,
                        orderTableModel.Model.RelationProperties["User"].RelationPart.Relation);
        }

        // Add more tests here if needed for edge cases specific to MetadataFromModelsFactory
    }
}