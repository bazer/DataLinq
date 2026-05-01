using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public partial class TestDb : IDatabaseModel
{
    public TestDb(DataSourceAccess dsa) { }
}

public class SyntaxParserTests
{
    private static (SyntaxParser parser, AttributeSyntax attributeSyntax) GetAttributeSyntax(string attributeCode)
    {
        var code = $@"
using DataLinq.Attributes;
using DataLinq.Metadata;
using System;

namespace TestNamespace;

{attributeCode}
public class DummyClass {{}}
";
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetCompilationUnitRoot();
        var attributeSyntax = root.DescendantNodes().OfType<AttributeSyntax>().First();
        var allDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();

        return (new SyntaxParser(allDeclarations), attributeSyntax);
    }

    private static (SyntaxParser parser, PropertyDeclarationSyntax propertySyntax, ModelDefinition model) GetPropertySyntax(string propertyCode, string modelInterfaces = "ITableModel<TestDb>")
    {
        var code = $@"
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;
using System;
using System.Collections.Generic;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel {{ public TestDb(DataSourceAccess dsa) {{}} }}

public abstract partial class TestModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<TestModel, TestDb>(rowData, dataSource), {modelInterfaces}
{{
    {propertyCode}
}}
";
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetCompilationUnitRoot();
        var propertySyntax = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().First();
        var classSyntax = root.DescendantNodes().OfType<TypeDeclarationSyntax>().First(x => x.Identifier.Text == "TestModel");
        var allDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
        var parser = new SyntaxParser(allDeclarations);
        var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = parser.ParseTableModel(db, classSyntax, "TestModels").ValueOrException().Model;

        return (parser, propertySyntax, model);
    }

    private static (SyntaxParser parser, TypeDeclarationSyntax typeSyntax) GetTypeSyntax(string classCode)
    {
        var code = $@"
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;
using System;
using System.Collections.Generic;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel {{ public TestDb(DataSourceAccess dsa) {{}} }}

{classCode}
";
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetCompilationUnitRoot();
        var typeSyntax = root.DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .First(ts => ts is not ClassDeclarationSyntax cds || cds.Identifier.ValueText != nameof(TestDb));
        var allDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();

        return (new SyntaxParser(allDeclarations), typeSyntax);
    }

    [Test]
    public async Task ParseAttributeSyntax_Table()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Table(""my_table"")]");
        var attribute = parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute).IsTypeOf<TableAttribute>();
        await Assert.That(((TableAttribute)attribute).Name).IsEqualTo("my_table");
    }

    [Test]
    public async Task ParseAttributeSyntax_Column()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Column(""my_col"")]");
        var attribute = parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute).IsTypeOf<ColumnAttribute>();
        await Assert.That(((ColumnAttribute)attribute).Name).IsEqualTo("my_col");
    }

    [Test]
    public async Task ParseAttributeSyntax_PrimaryKey()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[PrimaryKey]");
        var attribute = parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute).IsTypeOf<PrimaryKeyAttribute>();
    }

    [Test]
    public async Task ParseAttributeSyntax_ForeignKey()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[ForeignKey(""OtherTable"", ""OtherId"", ""FK_Name"")]");
        var attribute = (ForeignKeyAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.Table).IsEqualTo("OtherTable");
        await Assert.That(attribute.Column).IsEqualTo("OtherId");
        await Assert.That(attribute.Name).IsEqualTo("FK_Name");
    }

    [Test]
    public async Task ParseAttributeSyntax_Relation()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Relation(""OtherTable"", ""FkCol"", ""RelName"")]");
        var attribute = (RelationAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.Table).IsEqualTo("OtherTable");
        await Assert.That(attribute.Columns.Length).IsEqualTo(1);
        await Assert.That(attribute.Columns[0]).IsEqualTo("FkCol");
        await Assert.That(attribute.Name).IsEqualTo("RelName");
    }

    [Test]
    public async Task ParseAttributeSyntax_Index_Simple()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Index(""idx_test"", IndexCharacteristic.Simple)]");
        var attribute = (IndexAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.Name).IsEqualTo("idx_test");
        await Assert.That(attribute.Characteristic).IsEqualTo(IndexCharacteristic.Simple);
        await Assert.That(attribute.Type).IsEqualTo(IndexType.BTREE);
        await Assert.That(attribute.Columns).IsEmpty();
    }

    [Test]
    public async Task ParseAttributeSyntax_Index_MultiColumn()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Index(""idx_multi"", IndexCharacteristic.Unique, IndexType.HASH, ""col1"", ""col2"")]");
        var attribute = (IndexAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.Name).IsEqualTo("idx_multi");
        await Assert.That(attribute.Characteristic).IsEqualTo(IndexCharacteristic.Unique);
        await Assert.That(attribute.Type).IsEqualTo(IndexType.HASH);
        await Assert.That(attribute.Columns.Length).IsEqualTo(2);
        await Assert.That(attribute.Columns[0]).IsEqualTo("col1");
        await Assert.That(attribute.Columns[1]).IsEqualTo("col2");
    }

    [Test]
    public async Task ParseAttributeSyntax_Type_SpecificDb()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Type(DatabaseType.MySQL, ""VARCHAR"", 100)]");
        var attribute = (TypeAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.DatabaseType).IsEqualTo(DatabaseType.MySQL);
        await Assert.That(attribute.Name).IsEqualTo("VARCHAR");
        await Assert.That(attribute.Length).IsEqualTo((ulong)100);
    }

    [Test]
    public async Task ParseAttributeSyntax_DefaultValue_String()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Default(""Hello"")]");
        var attribute = (DefaultAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.Value).IsEqualTo("Hello");
        await Assert.That(attribute.CodeExpression).IsEqualTo(@"""Hello""");
    }

    [Test]
    public async Task ParseAttributeSyntax_DefaultValue_EnumExpression_PreservesCodeExpression()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Default(StatusEnum.Active)]");
        var attribute = (DefaultAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.Value).IsEqualTo("StatusEnum.Active");
        await Assert.That(attribute.CodeExpression).IsEqualTo("StatusEnum.Active");
    }

    [Test]
    public async Task ParseAttributeSyntax_DefaultValue_Timestamp()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[DefaultCurrentTimestamp]");
        var attribute = parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute).IsTypeOf<DefaultCurrentTimestampAttribute>();
    }

    [Test]
    public async Task ParseAttributeSyntax_DefaultValue_NewUUID()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[DefaultNewUUID]");
        var attribute = parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute).IsTypeOf<DefaultNewUUIDAttribute>();
    }

    [Test]
    public async Task ParseAttributeSyntax_Interface_Named()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Interface(""IMyThing"")]");
        var attribute = (InterfaceAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.GenerateInterface).IsTrue();
        await Assert.That(attribute.Name).IsEqualTo("IMyThing");
    }

    [Test]
    public async Task ParsePropertySyntax_Value()
    {
        var (parser, syntax, model) = GetPropertySyntax(@"[Column(""my_col""), Nullable] public string? Name { get; }");
        var property = (ValueProperty)parser.ParseProperty(syntax, model).ValueOrException();

        await Assert.That(property.PropertyName).IsEqualTo("Name");
        await Assert.That(property.CsType.Name).IsEqualTo("string");
        await Assert.That(property.CsNullable).IsTrue();
        await Assert.That(property.Attributes.Any(a => a is ColumnAttribute ca && ca.Name == "my_col")).IsTrue();
        await Assert.That(property.Attributes.Any(a => a is NullableAttribute)).IsTrue();
    }

    [Test]
    public async Task ParsePropertySyntax_Relation()
    {
        var (parser, syntax, model) = GetPropertySyntax(@"[Relation(""Other"", ""FkId"")] public Other Related { get; }");
        var property = (RelationProperty)parser.ParseProperty(syntax, model).ValueOrException();

        await Assert.That(property.PropertyName).IsEqualTo("Related");
        await Assert.That(property.CsType.Name).IsEqualTo("Other");
        await Assert.That(property.Attributes.Any(a => a is RelationAttribute)).IsTrue();
    }

    [Test]
    public async Task ParsePropertySyntax_Enum()
    {
        var (parser, syntax, model) = GetPropertySyntax(@"[Column(""status_col""), Enum(""Active"", ""Inactive"")] public StatusEnum Status { get; }");
        var property = (ValueProperty)parser.ParseProperty(syntax, model).ValueOrException();

        await Assert.That(property.PropertyName).IsEqualTo("Status");
        await Assert.That(property.CsType.Name).IsEqualTo("StatusEnum");
        await Assert.That(property.EnumProperty.HasValue).IsTrue();
        await Assert.That(property.EnumProperty!.Value.CsValuesOrDbValues.Count).IsEqualTo(2);
        await Assert.That(property.EnumProperty!.Value.CsValuesOrDbValues[0].name).IsEqualTo("Active");
        await Assert.That(property.EnumProperty!.Value.CsValuesOrDbValues[1].name).IsEqualTo("Inactive");
    }

    [Test]
    public async Task ParsePropertySyntax_DefaultValue_PopulatesSourceInfo()
    {
        var (parser, syntax, model) = GetPropertySyntax(@"[Column(""kontotexten""), Default(56)] public string Kontotexten { get; }");
        var property = (ValueProperty)parser.ParseProperty(syntax, model).ValueOrException();

        await Assert.That(property.SourceInfo.HasValue).IsTrue();
        await Assert.That(property.SourceInfo!.Value.DefaultValueExpressionSpan.HasValue).IsTrue();

        var propertySpan = new TextSpan(property.SourceInfo.Value.PropertySpan.Start, property.SourceInfo.Value.PropertySpan.Length);
        var defaultSpanInfo = property.SourceInfo.Value.DefaultValueExpressionSpan!.Value;
        var defaultSpan = new TextSpan(defaultSpanInfo.Start, defaultSpanInfo.Length);

        await Assert.That(syntax.ToString()).IsEqualTo(syntax.SyntaxTree.GetText().ToString(propertySpan));
        await Assert.That(syntax.SyntaxTree.GetText().ToString(defaultSpan)).IsEqualTo("56");
    }

    [Test]
    public async Task ParseModelSyntax_Basic()
    {
        var (parser, syntax) = GetTypeSyntax(
            """
[Table("my_models")]
public abstract partial class MyModel : ITableModel<TestDb>
{
    [Column("id"), PrimaryKey] public abstract int Id { get; }
    [Column("name")] public abstract string Name { get; }
}
""");
        var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration(typeof(TestDb)));
        var tableModel = parser.ParseTableModel(db, syntax, "MyModels").ValueOrException();
        var model = tableModel.Model;

        await Assert.That(model.CsType.Name).IsEqualTo("MyModel");
        await Assert.That(model.CsType.Namespace).IsEqualTo("TestNamespace");
        await Assert.That(model.Attributes.Any(a => a is TableAttribute ta && ta.Name == "my_models")).IsTrue();
        await Assert.That(model.ValueProperties.Count).IsEqualTo(2);
        await Assert.That(model.ValueProperties.ContainsKey("Id")).IsTrue();
        await Assert.That(model.ValueProperties.ContainsKey("Name")).IsTrue();
        await Assert.That(model.RelationProperties).IsEmpty();
        await Assert.That(model.Usings.Any(u => u.FullNamespaceName == "DataLinq.Attributes")).IsTrue();
        await Assert.That(tableModel.Table).IsNotNull();
        await Assert.That(tableModel.Table.DbName).IsEqualTo("my_models");
    }

    [Test]
    public async Task ParseModelSyntax_WithInterfaceAttribute()
    {
        var (parser, syntax) = GetTypeSyntax(
            """
[Table("my_models")]
[Interface("IMySpecialModel")]
public abstract partial class MyModel : ITableModel<TestDb>
{
    [Column("id")] public abstract int Id { get; }
}
""");
        var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration(typeof(TestDb)));
        var model = parser.ParseTableModel(db, syntax, "MyModels").ValueOrException().Model;

        await Assert.That(model.ModelInstanceInterface.HasValue).IsTrue();
        await Assert.That(model.ModelInstanceInterface!.Value.Name).IsEqualTo("IMySpecialModel");
        await Assert.That(model.ModelInstanceInterface!.Value.Namespace).IsEqualTo("TestNamespace");
    }

    [Test]
    public async Task ParseModelSyntax_WithClassLevelIndexAttribute()
    {
        var (parser, syntax) = GetTypeSyntax(
            """
[Table("my_models")]
[Index("idx_multi", IndexCharacteristic.Unique, IndexType.BTREE, "first_col", "second_col")]
public abstract partial class MyModel : ITableModel<TestDb>
{
    [Column("first_col")] public abstract int First { get; }
    [Column("second_col")] public abstract int Second { get; }
}
""");
        var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration(typeof(TestDb)));
        var model = parser.ParseTableModel(db, syntax, "MyModels").ValueOrException().Model;
        var index = model.Attributes.OfType<IndexAttribute>().Single();

        await Assert.That(index.Name).IsEqualTo("idx_multi");
        await Assert.That(index.Characteristic).IsEqualTo(IndexCharacteristic.Unique);
        await Assert.That(index.Columns).IsEquivalentTo(["first_col", "second_col"]);
    }

    [Test]
    public async Task GetTableTypeSyntax()
    {
        const string code = """
using DataLinq.Interfaces;
using DataLinq;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel
{
    public DbRead<Employee> Employees { get; }
}

public abstract partial class Employee : ITableModel<TestDb> { }
""";
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetCompilationUnitRoot();
        var propertySyntax = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().First(p => p.Identifier.Text == "Employees");
        var modelSyntaxes = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
        var parser = new SyntaxParser(modelSyntaxes);

        var result = parser.GetTableType(propertySyntax, modelSyntaxes.ToList()).ValueOrException();

        await Assert.That(result.csPropertyName).IsEqualTo("Employees");
        await Assert.That(result.classSyntax).IsNotNull();
        await Assert.That(result.classSyntax.Identifier.Text).IsEqualTo("Employee");
    }
}
