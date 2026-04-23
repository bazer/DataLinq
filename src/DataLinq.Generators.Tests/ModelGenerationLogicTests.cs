using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DataLinq.Generators.Tests;

public class ModelGenerationLogicTests : GeneratorTestBase
{
    private const string InvalidDefaultValueSourcePath = @"D:\git\DataLinq\src\DataLinq.Generators.Tests\TestModels\InvalidDefaultModel.cs";
    private const string InvalidCacheLimitSourcePath = @"D:\git\DataLinq\src\DataLinq.Generators.Tests\TestModels\InvalidCacheLimitModel.cs";
    private const string InvalidForeignKeySourcePath = @"D:\git\DataLinq\src\DataLinq.Generators.Tests\TestModels\InvalidForeignKeyModel.cs";

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

    private const string InvalidDefaultValueTestModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestInvalidDefaultNamespace;

    public partial class InvalidDefaultDb : IDatabaseModel
    {
        public InvalidDefaultDb(DataSourceAccess dsa){}
        public DbRead<InvalidDefaultModel> Betalningar { get; }
    }

    [Table(""betalningar"")]
    public abstract partial class InvalidDefaultModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<InvalidDefaultModel, InvalidDefaultDb>(rowData, dataSource), ITableModel<InvalidDefaultDb>
    {
        [PrimaryKey, AutoIncrement, Column(""betal_id"")]
        public abstract int? BetalId { get; }

        [Column(""kontotexten"")]
        [Default(56)]
        public abstract string Kontotexten { get; }
    }";

    private const string InvalidCacheLimitModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestInvalidCacheLimitNamespace;

    [CacheLimit((CacheLimitType)999, 1)]
    public partial class InvalidCacheLimitDb : IDatabaseModel
    {
        public InvalidCacheLimitDb(DataSourceAccess dsa){}
        public DbRead<InvalidCacheLimitModel> Rows { get; }
    }

    [Table(""rows"")]
    public abstract partial class InvalidCacheLimitModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<InvalidCacheLimitModel, InvalidCacheLimitDb>(rowData, dataSource), ITableModel<InvalidCacheLimitDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }
    }";

    private const string InvalidForeignKeyModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestInvalidForeignKeyNamespace;

    public partial class InvalidForeignKeyDb : IDatabaseModel
    {
        public InvalidForeignKeyDb(DataSourceAccess dsa){}
        public DbRead<OrderModel> Orders { get; }
    }

    [Table(""orders"")]
    public abstract partial class OrderModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<OrderModel, InvalidForeignKeyDb>(rowData, dataSource), ITableModel<InvalidForeignKeyDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }

        [Column(""user_id""), ForeignKey(""missing_users"", ""id"", ""FK_Order_User"")]
        public abstract int UserId { get; }
    }";

    private static PropertyDeclarationSyntax GetPropertyFromType(TypeDeclarationSyntax typeDeclaration, string name)
        => typeDeclaration.Members.OfType<PropertyDeclarationSyntax>().Single(member => member.Identifier.ValueText == name);

    [Test]
    public async Task Property_WithDefault_ShouldBeNonNullable()
    {
        var inputTree = CSharpSyntaxTree.ParseText(DefaultValueTestModelSource);
        var generatedTrees = RunGenerator([inputTree]).ToList();

        var modelTree = generatedTrees.Single(t => Path.GetFileName(t.FilePath).EndsWith("TestDefaultModel.cs", StringComparison.Ordinal));
        var root = modelTree.GetCompilationUnitRoot();

        var @interface = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single(i => i.Identifier.ValueText == "ITestDefaultModel");
        var @class = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ImmutableTestDefaultModel");

        var countOnInterface = GetPropertyFromType(@interface, "Count");
        var idOnInterface = GetPropertyFromType(@interface, "Id");
        var optionalOnInterface = GetPropertyFromType(@interface, "OptionalValue");

        var countOnClass = GetPropertyFromType(@class, "Count");
        var idOnClass = GetPropertyFromType(@class, "Id");
        var optionalOnClass = GetPropertyFromType(@class, "OptionalValue");

        await Assert.That(countOnInterface.Type is not NullableTypeSyntax).IsTrue();
        await Assert.That(countOnClass.Type is not NullableTypeSyntax).IsTrue();
        await Assert.That(idOnInterface.Type is NullableTypeSyntax).IsTrue();
        await Assert.That(idOnClass.Type is NullableTypeSyntax).IsTrue();
        await Assert.That(optionalOnInterface.Type is NullableTypeSyntax).IsTrue();
        await Assert.That(optionalOnClass.Type is NullableTypeSyntax).IsTrue();
    }

    [Test]
    public async Task Property_WithDefault_ShouldNotBeNullable_WhenNullableContextIsDisabled()
    {
        var inputTree = CSharpSyntaxTree.ParseText(DefaultValueTestModelSource);
        var generatedTrees = RunGenerator([inputTree]).ToList();

        var modelTree = generatedTrees.Single(t => Path.GetFileName(t.FilePath).EndsWith("TestDefaultModel.cs", StringComparison.Ordinal));
        var root = modelTree.GetCompilationUnitRoot();

        var @interface = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single(i => i.Identifier.ValueText == "ITestDefaultModel");
        var @class = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ImmutableTestDefaultModel");

        var countOnInterface = GetPropertyFromType(@interface, "Count");
        var idOnInterface = GetPropertyFromType(@interface, "Id");
        var nameOnInterface = GetPropertyFromType(@interface, "Name");
        var optionalOnInterface = GetPropertyFromType(@interface, "OptionalValue");

        var countOnClass = GetPropertyFromType(@class, "Count");
        var idOnClass = GetPropertyFromType(@class, "Id");
        var nameOnClass = GetPropertyFromType(@class, "Name");
        var optionalOnClass = GetPropertyFromType(@class, "OptionalValue");

        await Assert.That(countOnInterface.Type is not NullableTypeSyntax).IsTrue();
        await Assert.That(countOnClass.Type is not NullableTypeSyntax).IsTrue();
        await Assert.That(idOnInterface.Type is NullableTypeSyntax).IsTrue();
        await Assert.That(idOnClass.Type is NullableTypeSyntax).IsTrue();
        await Assert.That(nameOnInterface.Type is not NullableTypeSyntax).IsTrue();
        await Assert.That(nameOnClass.Type is not NullableTypeSyntax).IsTrue();
        await Assert.That(optionalOnInterface.Type is NullableTypeSyntax).IsTrue();
        await Assert.That(optionalOnClass.Type is NullableTypeSyntax).IsTrue();
    }

    [Test]
    public async Task Property_WithDefault_ShouldNotBeNullable_WhenNullableContextIsEnabled()
    {
        var inputTree = CSharpSyntaxTree.ParseText(DefaultValueTestModelSource);
        var generatedTrees = RunGenerator([inputTree]).ToList();

        var modelTree = generatedTrees.Single(t => Path.GetFileName(t.FilePath).EndsWith("TestDefaultModel.cs", StringComparison.Ordinal));
        var root = modelTree.GetCompilationUnitRoot();

        var @interface = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Single(i => i.Identifier.ValueText == "ITestDefaultModel");
        var @class = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "ImmutableTestDefaultModel");

        var countOnInterface = GetPropertyFromType(@interface, "Count");
        var idOnInterface = GetPropertyFromType(@interface, "Id");
        var nameOnInterface = GetPropertyFromType(@interface, "Name");
        var optionalOnInterface = GetPropertyFromType(@interface, "OptionalValue");

        var countOnClass = GetPropertyFromType(@class, "Count");
        var idOnClass = GetPropertyFromType(@class, "Id");
        var nameOnClass = GetPropertyFromType(@class, "Name");
        var optionalOnClass = GetPropertyFromType(@class, "OptionalValue");

        await Assert.That(countOnInterface.Type is not NullableTypeSyntax).IsTrue();
        await Assert.That(countOnClass.Type is not NullableTypeSyntax).IsTrue();
        await Assert.That(idOnInterface.Type is NullableTypeSyntax).IsTrue();
        await Assert.That(idOnClass.Type is NullableTypeSyntax).IsTrue();
        await Assert.That(nameOnInterface.Type is not NullableTypeSyntax).IsTrue();
        await Assert.That(nameOnClass.Type is not NullableTypeSyntax).IsTrue();
        await Assert.That(optionalOnInterface.Type is NullableTypeSyntax).IsTrue();
        await Assert.That(optionalOnClass.Type is NullableTypeSyntax).IsTrue();
    }

    [Test]
    public async Task Property_WithInvalidDefault_ShouldReportDiagnosticOnSourceAttribute_AndSkipBrokenAssignment()
    {
        var inputTree = CSharpSyntaxTree.ParseText(InvalidDefaultValueTestModelSource, path: InvalidDefaultValueSourcePath);

        var (outputCompilation, diagnostics, generatedTrees) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnosticsWithId = diagnostics.Where(x => x.Id == "DLG003").ToList();
        await Assert.That(diagnosticsWithId.Count).IsEqualTo(1);

        var diagnostic = diagnosticsWithId.Single();
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(InvalidDefaultValueSourcePath);
        await Assert.That(inputTree.GetText().ToString(diagnostic.Location.SourceSpan)).IsEqualTo("56");
        await Assert.That(outputCompilation.GetDiagnostics().Any(x => x.Id == "CS0029")).IsFalse();

        var generatedCode = string.Join(Environment.NewLine, generatedTrees.Select(x => x.ToString()));
        await Assert.That(generatedCode.Contains("this.Kontotexten = 56;", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task InvalidMetadataAttribute_ShouldReportDiagnosticAtAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(InvalidCacheLimitModelSource, path: InvalidCacheLimitSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(InvalidCacheLimitSourcePath);
        await Assert.That(string.IsNullOrWhiteSpace(highlightedSource)).IsFalse();
        await Assert.That(highlightedSource.Contains("CacheLimit", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("Invalid CacheLimitType value", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task InvalidForeignKey_ShouldReportDiagnosticAtForeignKeyAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(InvalidForeignKeyModelSource, path: InvalidForeignKeySourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(InvalidForeignKeySourcePath);
        await Assert.That(highlightedSource.Contains("ForeignKey", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("missing_users", StringComparison.Ordinal)).IsTrue();
    }
}
