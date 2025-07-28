using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DataLinq.Generators.Tests;

public class ModelGenerationLogicTests : GeneratorTestBase
{
    private const string DefaultValueTestModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;
    using System;

    namespace TestDefaultValueNamespace;

    public partial class TestDefaultDb : IDatabaseModel 
    { 
        public TestDefaultDb(DataSourceAccess dsa){} 
        public DbRead<TestDefaultModel> TestDefaults { get; } 
    }

    public partial interface ITestDefaultModel {}

    [Table(""test_defaults"")]
    [Interface<ITestDefaultModel>]
    public abstract partial class TestDefaultModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<TestDefaultModel, TestDefaultDb>(rowData, dataSource), ITableModel<TestDefaultDb>
    {
        [Column(""count"")]
        [Default(0)]
        public abstract int Count { get; }

        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }

        [Column(""name"")]
        public abstract string Name { get; }

        [Nullable, Column(""optional_value"")]
        public abstract int? OptionalValue { get; }
    }";

    private PropertyDeclarationSyntax GetPropertyFromType(TypeDeclarationSyntax typeDecl, string name)
    {
        return typeDecl.Members.OfType<PropertyDeclarationSyntax>().Single(m => m.Identifier.ValueText == name);
    }

    [Fact]
    public void Property_WithDefault_ShouldBeNonNullable()
    {
        // Arrange
        var inputTree = CSharpSyntaxTree.ParseText(DefaultValueTestModelSource);
        var generatedTrees = RunGenerator(new[] { inputTree }).ToList();

        var modelTree = generatedTrees.Single(t => Path.GetFileName(t.FilePath).EndsWith("TestDefaultModel.cs"));
        var root = modelTree.GetCompilationUnitRoot();

        var @interface = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single(i => i.Identifier.ValueText == "ITestDefaultModel");
        var @class = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ImmutableTestDefaultModel");

        // Act
        var countOnInterface = GetPropertyFromType(@interface, "Count");
        var idOnInterface = GetPropertyFromType(@interface, "Id");
        var optionalOnInterface = GetPropertyFromType(@interface, "OptionalValue");

        var countOnClass = GetPropertyFromType(@class, "Count");
        var idOnClass = GetPropertyFromType(@class, "Id");
        var optionalOnClass = GetPropertyFromType(@class, "OptionalValue");

        // Assert
        Assert.IsNotType<NullableTypeSyntax>(countOnInterface.Type);
        Assert.IsNotType<NullableTypeSyntax>(countOnClass.Type);

        Assert.IsType<NullableTypeSyntax>(idOnInterface.Type);
        Assert.IsType<NullableTypeSyntax>(idOnClass.Type);

        Assert.IsType<NullableTypeSyntax>(optionalOnInterface.Type);
        Assert.IsType<NullableTypeSyntax>(optionalOnClass.Type);
    }

    [Fact]
    public void Property_WithDefault_ShouldNotBeNullable_WhenNullableContextIsDisabled()
    {
        // Arrange
        var inputTree = CSharpSyntaxTree.ParseText(DefaultValueTestModelSource);
        var generatedTrees = RunGenerator(new[] { inputTree }).ToList();

        var modelTree = generatedTrees.Single(t => Path.GetFileName(t.FilePath).EndsWith("TestDefaultModel.cs"));
        var root = modelTree.GetCompilationUnitRoot();

        var @interface = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single(i => i.Identifier.ValueText == "ITestDefaultModel");
        var @class = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ImmutableTestDefaultModel");

        // Act
        var countOnInterface = GetPropertyFromType(@interface, "Count");
        var idOnInterface = GetPropertyFromType(@interface, "Id");
        var nameOnInterface = GetPropertyFromType(@interface, "Name");
        var optionalOnInterface = GetPropertyFromType(@interface, "OptionalValue");

        var countOnClass = GetPropertyFromType(@class, "Count");
        var idOnClass = GetPropertyFromType(@class, "Id");
        var nameOnClass = GetPropertyFromType(@class, "Name");
        var optionalOnClass = GetPropertyFromType(@class, "OptionalValue");

        // Assert
        Assert.IsNotType<NullableTypeSyntax>(countOnInterface.Type); // int
        Assert.IsNotType<NullableTypeSyntax>(countOnClass.Type);   // int

        Assert.IsType<NullableTypeSyntax>(idOnInterface.Type);     // int?
        Assert.IsType<NullableTypeSyntax>(idOnClass.Type);       // int?

        Assert.IsNotType<NullableTypeSyntax>(nameOnInterface.Type);  // string
        Assert.IsNotType<NullableTypeSyntax>(nameOnClass.Type);    // string

        Assert.IsType<NullableTypeSyntax>(optionalOnInterface.Type); // int?
        Assert.IsType<NullableTypeSyntax>(optionalOnClass.Type);   // int?
    }

    [Fact]
    public void Property_WithDefault_ShouldNotBeNullable_WhenNullableContextIsEnabled()
    {
        // Arrange
        var inputTree = CSharpSyntaxTree.ParseText(DefaultValueTestModelSource);
        var generatedTrees = RunGenerator(new[] { inputTree }).ToList();

        var modelTree = generatedTrees.Single(t => Path.GetFileName(t.FilePath).EndsWith("TestDefaultModel.cs"));
        var root = modelTree.GetCompilationUnitRoot();

        var @interface = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single(i => i.Identifier.ValueText == "ITestDefaultModel");
        var @class = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ImmutableTestDefaultModel");

        // Act
        var countOnInterface = GetPropertyFromType(@interface, "Count");
        var idOnInterface = GetPropertyFromType(@interface, "Id");
        var nameOnInterface = GetPropertyFromType(@interface, "Name");
        var optionalOnInterface = GetPropertyFromType(@interface, "OptionalValue");

        var countOnClass = GetPropertyFromType(@class, "Count");
        var idOnClass = GetPropertyFromType(@class, "Id");
        var nameOnClass = GetPropertyFromType(@class, "Name");
        var optionalOnClass = GetPropertyFromType(@class, "OptionalValue");

        // Assert
        Assert.IsNotType<NullableTypeSyntax>(countOnInterface.Type); // int
        Assert.IsNotType<NullableTypeSyntax>(countOnClass.Type);   // int

        Assert.IsType<NullableTypeSyntax>(idOnInterface.Type);     // int?
        Assert.IsType<NullableTypeSyntax>(idOnClass.Type);       // int?

        // string is a reference type, so it won't be NullableTypeSyntax, 
        // but the compiler will enforce nullability based on the project setting.
        Assert.IsNotType<NullableTypeSyntax>(nameOnInterface.Type);
        Assert.IsNotType<NullableTypeSyntax>(nameOnClass.Type);

        Assert.IsType<NullableTypeSyntax>(optionalOnInterface.Type); // int?
        Assert.IsType<NullableTypeSyntax>(optionalOnClass.Type);   // int?
    }
}