using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
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
#pragma warning disable CS0618 // These tests intentionally exercise legacy SyntaxParser mutable metadata outputs.
    private static TableModel ParseMutableTableModel(
        SyntaxParser parser,
        DatabaseDefinition database,
        TypeDeclarationSyntax typeSyntax,
        string csPropertyName) =>
        parser.ParseTableModel(database, typeSyntax, csPropertyName).ValueOrException();

    private static ThrowAway.Option<TableModel, IDLOptionFailure> ParseMutableTableModelResult(
        SyntaxParser parser,
        DatabaseDefinition database,
        TypeDeclarationSyntax typeSyntax,
        string csPropertyName) =>
        parser.ParseTableModel(database, typeSyntax, csPropertyName);

    private static PropertyDefinition ParseMutableProperty(
        SyntaxParser parser,
        PropertyDeclarationSyntax propertySyntax,
        ModelDefinition model) =>
        parser.ParseProperty(propertySyntax, model).ValueOrException();

    private static ThrowAway.Option<PropertyDefinition, IDLOptionFailure> ParseMutablePropertyResult(
        SyntaxParser parser,
        PropertyDeclarationSyntax propertySyntax,
        ModelDefinition model) =>
        parser.ParseProperty(propertySyntax, model);
#pragma warning restore CS0618

    [Test]
    public async Task IsModelInterface_QualifiedNames_ReturnsTrue()
    {
        await Assert.That(SyntaxParser.IsModelInterface("DataLinq.Interfaces.IDatabaseModel")).IsTrue();
        await Assert.That(SyntaxParser.IsModelInterface("DataLinq.Interfaces.IDatabaseModel<TestNamespace.TestDb>")).IsTrue();
        await Assert.That(SyntaxParser.IsModelInterface("global::DataLinq.Interfaces.ITableModel<TestNamespace.TestDb>")).IsTrue();
        await Assert.That(SyntaxParser.IsModelInterface(SyntaxFactory.ParseTypeName("DataLinq.Interfaces.IViewModel<TestNamespace.TestDb>"))).IsTrue();
    }

    [Test]
    public async Task IsModelInterface_LookalikeNames_ReturnsFalse()
    {
        await Assert.That(SyntaxParser.IsModelInterface("DataLinq.Interfaces.IDatabaseModelBackup")).IsFalse();
        await Assert.That(SyntaxParser.IsModelInterface("global::DataLinq.Interfaces.ITableModelBackup<TestNamespace.TestDb>")).IsFalse();
        await Assert.That(SyntaxParser.IsModelInterface(SyntaxFactory.ParseTypeName("DataLinq.Interfaces.IViewModelBackup<TestNamespace.TestDb>"))).IsFalse();
    }

    [Test]
    public async Task IsModelInterface_InvalidGenericArity_ReturnsFalse()
    {
        await Assert.That(SyntaxParser.IsModelInterface("DataLinq.Interfaces.IDatabaseModel<TestNamespace.TestDb, TestNamespace.OtherDb>")).IsFalse();
        await Assert.That(SyntaxParser.IsModelInterface("global::DataLinq.Interfaces.ITableModel<TestNamespace.TestDb, TestNamespace.OtherDb>")).IsFalse();
        await Assert.That(SyntaxParser.IsModelInterface(SyntaxFactory.ParseTypeName("DataLinq.Interfaces.IViewModel<>"))).IsFalse();
        await Assert.That(SyntaxParser.IsModelInterface("DataLinq.Instances.IModelInstance<TestNamespace.TestDb, TestNamespace.OtherDb>")).IsFalse();
    }

    [Test]
    public async Task MutableMetadataParserOutputs_AreMarkedObsolete()
    {
        var missingMethods = new (string MethodName, Type[] ParameterTypes)[]
            {
                (nameof(SyntaxParser.ParseTableModel), [typeof(DatabaseDefinition), typeof(TypeDeclarationSyntax), typeof(string)]),
                (nameof(SyntaxParser.ParseProperty), [typeof(PropertyDeclarationSyntax), typeof(ModelDefinition)])
            }
            .Select(item => FindMissingObsoleteMethod(item.MethodName, item.ParameterTypes))
            .OfType<string>()
            .ToArray();

        await Assert.That(missingMethods).IsEmpty();
    }

    private static string? FindMissingObsoleteMethod(string methodName, Type[] parameterTypes)
    {
        var method = typeof(SyntaxParser).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        return method?.GetCustomAttribute<ObsoleteAttribute>() is null
            ? $"{nameof(SyntaxParser)}.{methodName}({string.Join(", ", parameterTypes.Select(parameterType => parameterType.Name))})"
            : null;
    }

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
        var model = ParseMutableTableModel(parser, db, classSyntax, "TestModels").Model;

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
    public async Task ParseAttributeSyntax_Comment()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Comment(""Human-readable comment"")]");
        var attribute = (CommentAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.DatabaseType).IsEqualTo(DatabaseType.Default);
        await Assert.That(attribute.Text).IsEqualTo("Human-readable comment");
    }

    [Test]
    public async Task ParseAttributeSyntax_ProviderComment()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Comment(DatabaseType.MySQL, ""MySQL comment"")]");
        var attribute = (CommentAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.DatabaseType).IsEqualTo(DatabaseType.MySQL);
        await Assert.That(attribute.Text).IsEqualTo("MySQL comment");
    }

    [Test]
    public async Task ParseAttributeSyntax_Comment_UnescapesStringLiteral()
    {
        var (parser, syntax) = GetAttributeSyntax("[Comment(\"Comment with \\\"quotes\\\"\")]");
        var attribute = (CommentAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.Text).IsEqualTo("Comment with \"quotes\"");
    }

    [Test]
    public async Task ParseAttributeSyntax_Check()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Check(""CK_positive"", ""`amount` >= 0"")]");
        var attribute = (CheckAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.DatabaseType).IsEqualTo(DatabaseType.Default);
        await Assert.That(attribute.Name).IsEqualTo("CK_positive");
        await Assert.That(attribute.Expression).IsEqualTo("`amount` >= 0");
    }

    [Test]
    public async Task ParseAttributeSyntax_ProviderCheck()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Check(DatabaseType.MySQL, ""CK_positive"", ""`amount` >= 0"")]");
        var attribute = (CheckAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.DatabaseType).IsEqualTo(DatabaseType.MySQL);
        await Assert.That(attribute.Name).IsEqualTo("CK_positive");
        await Assert.That(attribute.Expression).IsEqualTo("`amount` >= 0");
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
        await Assert.That(attribute.Ordinal).IsNull();
    }

    [Test]
    public async Task ParseAttributeSyntax_ForeignKeyWithOrdinal()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[ForeignKey(""OtherTable"", ""OtherId"", ""FK_Name"", 2)]");
        var attribute = (ForeignKeyAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.Table).IsEqualTo("OtherTable");
        await Assert.That(attribute.Column).IsEqualTo("OtherId");
        await Assert.That(attribute.Name).IsEqualTo("FK_Name");
        await Assert.That(attribute.Ordinal).IsEqualTo(2);
    }

    [Test]
    public async Task ParseAttributeSyntax_ForeignKeyWithReferentialActions()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[ForeignKey(""OtherTable"", ""OtherId"", ""FK_Name"", 2, ReferentialAction.Cascade, ReferentialAction.SetNull)]");
        var attribute = (ForeignKeyAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.Table).IsEqualTo("OtherTable");
        await Assert.That(attribute.Column).IsEqualTo("OtherId");
        await Assert.That(attribute.Name).IsEqualTo("FK_Name");
        await Assert.That(attribute.Ordinal).IsEqualTo(2);
        await Assert.That(attribute.OnUpdate).IsEqualTo(ReferentialAction.Cascade);
        await Assert.That(attribute.OnDelete).IsEqualTo(ReferentialAction.SetNull);
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
    public async Task ParseAttributeSyntax_RelationWithColumnArray()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Relation(""OtherTable"", new string[] { ""TenantId"", ""OrderNo"" }, ""RelName"")]");
        var attribute = (RelationAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.Table).IsEqualTo("OtherTable");
        await Assert.That(attribute.Columns).IsEquivalentTo(["TenantId", "OrderNo"]);
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
    public async Task ParseAttributeSyntax_DefaultSql()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[DefaultSql(DatabaseType.MySQL, ""(json_object())"")]");
        var attribute = (DefaultSqlAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.DatabaseType).IsEqualTo(DatabaseType.MySQL);
        await Assert.That(attribute.Expression).IsEqualTo("(json_object())");
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
    public async Task ParseAttributeSyntax_Interface_GenericQualifiedType_NormalizesTypeName()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Interface<TestNamespace.IMyThing>]");
        var attribute = (InterfaceAttribute)parser.ParseAttribute(syntax).ValueOrException();

        await Assert.That(attribute.GenerateInterface).IsTrue();
        await Assert.That(attribute.Name).IsEqualTo("IMyThing");
    }

    [Test]
    public async Task ParseAttributeSyntax_InterfaceGenericWithMultipleTypeArguments_ReturnsInvalidArgumentFailure()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[Interface<IFirstThing, ISecondThing>]");

        var result = parser.ParseAttribute(syntax);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidArgument);
        await Assert.That(failure.Message).Contains("exactly one type argument");
    }

    [Test]
    public async Task ParseAttributeSyntax_InterfaceLookalike_ReturnsNotImplementedFailure()
    {
        var (parser, syntax) = GetAttributeSyntax(@"[InterfaceBackup]");

        var result = parser.ParseAttribute(syntax);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.NotImplemented);
        await Assert.That(failure.Message).Contains("InterfaceBackup");
    }

    [Test]
    public async Task ParsePropertySyntax_Value()
    {
        var (parser, syntax, model) = GetPropertySyntax(@"[Column(""my_col""), Nullable] public string? Name { get; }");
        var property = (ValueProperty)ParseMutableProperty(parser, syntax, model);

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
        var property = (RelationProperty)ParseMutableProperty(parser, syntax, model);

        await Assert.That(property.PropertyName).IsEqualTo("Related");
        await Assert.That(property.CsType.Name).IsEqualTo("Other");
        await Assert.That(property.Attributes.Any(a => a is RelationAttribute)).IsTrue();
    }

    [Test]
    public async Task ParsePropertySyntax_QualifiedRelationTypes_NormalizesTypeName()
    {
        var (manyParser, manySyntax, manyModel) = GetPropertySyntax(
            @"[Relation(""orders"", ""user_id"", ""FK_Order_User"")] public DataLinq.Interfaces.IImmutableRelation<TestNamespace.OrderModel> Orders { get; }");
        var (oneParser, oneSyntax, oneModel) = GetPropertySyntax(
            @"[Relation(""users"", ""id"", ""FK_Order_User"")] public global::TestNamespace.UserModel User { get; }");

        var manyProperty = (RelationProperty)ParseMutableProperty(manyParser, manySyntax, manyModel);
        var oneProperty = (RelationProperty)ParseMutableProperty(oneParser, oneSyntax, oneModel);

        await Assert.That(manyProperty.CsType.Name).IsEqualTo("IImmutableRelation<OrderModel>");
        await Assert.That(oneProperty.CsType.Name).IsEqualTo("UserModel");
    }

    [Test]
    public async Task ParsePropertySyntax_Enum()
    {
        var (parser, syntax, model) = GetPropertySyntax(@"[Column(""status_col""), Enum(""Active"", ""Inactive"")] public StatusEnum Status { get; }");
        var property = (ValueProperty)ParseMutableProperty(parser, syntax, model);

        await Assert.That(property.PropertyName).IsEqualTo("Status");
        await Assert.That(property.CsType.Name).IsEqualTo("StatusEnum");
        await Assert.That(property.EnumProperty.HasValue).IsTrue();
        await Assert.That(property.EnumProperty!.Value.CsValuesOrDbValues.Count).IsEqualTo(2);
        await Assert.That(property.EnumProperty!.Value.CsValuesOrDbValues[0].name).IsEqualTo("Active");
        await Assert.That(property.EnumProperty!.Value.CsValuesOrDbValues[1].name).IsEqualTo("Inactive");
    }

    [Test]
    public async Task ParsePropertySyntax_TopLevelEnumInSameFile_PopulatesCsEnumValues()
    {
        const string code = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public enum StatusEnum
{
    Active = 1,
    Inactive = 2
}

public partial class TestDb : IDatabaseModel { public TestDb(DataSourceAccess dsa) {} }

public abstract partial class TestModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<TestModel, TestDb>(rowData, dataSource), ITableModel<TestDb>
{
    [Column("status_col"), Enum("Active", "Inactive")] public StatusEnum Status { get; }
}
""";
        var syntaxTree = CSharpSyntaxTree.ParseText(code, path: "Model.cs");
        var root = syntaxTree.GetCompilationUnitRoot();
        var propertySyntax = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var classSyntax = root.DescendantNodes().OfType<TypeDeclarationSyntax>().Single(x => x.Identifier.Text == "TestModel");
        var modelSyntaxes = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
        var enumSyntaxes = root.DescendantNodes().OfType<EnumDeclarationSyntax>().ToImmutableArray();
        var parser = new SyntaxParser(modelSyntaxes, enumSyntaxes);
        var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = ParseMutableTableModel(parser, db, classSyntax, "TestModels").Model;

        var property = (ValueProperty)ParseMutableProperty(parser, propertySyntax, model);

        await Assert.That(property.EnumProperty.HasValue).IsTrue();
        await Assert.That(property.EnumProperty!.Value.CsEnumValues.Select(x => x.name).ToArray()).IsEquivalentTo(["Active", "Inactive"]);
        await Assert.That(property.EnumProperty!.Value.DeclaredInClass).IsFalse();
        await Assert.That(property.EnumProperty!.Value.DeclaredInModelFile).IsTrue();
    }

    [Test]
    public async Task ParsePropertySyntax_ExternalEnumDeclaration_MarksEnumAsExternal()
    {
        const string modelCode = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel { public TestDb(DataSourceAccess dsa) {} }

public abstract partial class TestModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<TestModel, TestDb>(rowData, dataSource), ITableModel<TestDb>
{
    [Column("status_col"), Enum("Active", "Inactive")] public StatusEnum Status { get; }
}
""";
        const string enumCode = """
namespace TestNamespace;

public enum StatusEnum
{
    Active = 1,
    Inactive = 2
}
""";
        var modelTree = CSharpSyntaxTree.ParseText(modelCode, path: "Model.cs");
        var enumTree = CSharpSyntaxTree.ParseText(enumCode, path: "Enums.cs");
        var modelRoot = modelTree.GetCompilationUnitRoot();
        var enumRoot = enumTree.GetCompilationUnitRoot();
        var propertySyntax = modelRoot.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var classSyntax = modelRoot.DescendantNodes().OfType<TypeDeclarationSyntax>().Single(x => x.Identifier.Text == "TestModel");
        var modelSyntaxes = modelRoot.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
        var enumSyntaxes = enumRoot.DescendantNodes().OfType<EnumDeclarationSyntax>().ToImmutableArray();
        var parser = new SyntaxParser(modelSyntaxes, enumSyntaxes);
        var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = ParseMutableTableModel(parser, db, classSyntax, "TestModels").Model;

        var property = (ValueProperty)ParseMutableProperty(parser, propertySyntax, model);

        await Assert.That(property.CsType.Name).IsEqualTo("StatusEnum");
        await Assert.That(property.EnumProperty.HasValue).IsTrue();
        await Assert.That(property.EnumProperty!.Value.DbEnumValues.Select(x => x.name).ToArray()).IsEquivalentTo(["Active", "Inactive"]);
        await Assert.That(property.EnumProperty!.Value.CsEnumValues.Select(x => x.name).ToArray()).IsEquivalentTo(["Active", "Inactive"]);
        await Assert.That(property.EnumProperty!.Value.DeclaredInModelFile).IsFalse();
    }

    [Test]
    public async Task ParsePropertySyntax_DuplicateExternalEnumDeclaration_MarksSameFileEnumAsExternal()
    {
        const string modelCode = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public enum StatusEnum
{
    Active = 1,
    Inactive = 2
}

public partial class TestDb : IDatabaseModel { public TestDb(DataSourceAccess dsa) {} }

public abstract partial class TestModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<TestModel, TestDb>(rowData, dataSource), ITableModel<TestDb>
{
    [Column("status_col"), Enum("Active", "Inactive")] public StatusEnum Status { get; }
}
""";
        const string enumCode = """
namespace TestNamespace;

public enum StatusEnum
{
    Active = 1,
    Inactive = 2
}
""";
        var modelTree = CSharpSyntaxTree.ParseText(modelCode, path: "Model.cs");
        var enumTree = CSharpSyntaxTree.ParseText(enumCode, path: "Enums.cs");
        var modelRoot = modelTree.GetCompilationUnitRoot();
        var enumRoot = enumTree.GetCompilationUnitRoot();
        var propertySyntax = modelRoot.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var classSyntax = modelRoot.DescendantNodes().OfType<TypeDeclarationSyntax>().Single(x => x.Identifier.Text == "TestModel");
        var modelSyntaxes = modelRoot.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
        var enumSyntaxes = modelRoot.DescendantNodes().OfType<EnumDeclarationSyntax>()
            .Concat(enumRoot.DescendantNodes().OfType<EnumDeclarationSyntax>())
            .ToImmutableArray();
        var parser = new SyntaxParser(modelSyntaxes, enumSyntaxes);
        var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class));
        var model = ParseMutableTableModel(parser, db, classSyntax, "TestModels").Model;

        var property = (ValueProperty)ParseMutableProperty(parser, propertySyntax, model);

        await Assert.That(property.EnumProperty.HasValue).IsTrue();
        await Assert.That(property.EnumProperty!.Value.CsEnumValues.Select(x => x.name).ToArray()).IsEquivalentTo(["Active", "Inactive"]);
        await Assert.That(property.EnumProperty!.Value.DeclaredInModelFile).IsFalse();
    }

    [Test]
    public async Task ParsePropertySyntax_MultipleEnumAttributes_ReturnsInvalidModelFailure()
    {
        var (parser, _, model) = GetPropertySyntax(@"[Column(""id""), PrimaryKey] public int Id { get; }");
        var syntax = (PropertyDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
            @"[Column(""status_col""), Enum(""Active""), Enum(""Inactive"")] public StatusEnum Status { get; }")!;

        var result = ParseMutablePropertyResult(parser, syntax, model);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Status");
        await Assert.That(failure.Message).Contains("multiple Enum attributes");
        await Assert.That(failure.Message).DoesNotContain("[Exception]");
    }

    [Test]
    public async Task ParsePropertySyntax_DefaultValue_PopulatesSourceInfo()
    {
        var (parser, syntax, model) = GetPropertySyntax(@"[Column(""kontotexten""), Default(56)] public string Kontotexten { get; }");
        var property = (ValueProperty)ParseMutableProperty(parser, syntax, model);

        await Assert.That(property.SourceInfo.HasValue).IsTrue();
        await Assert.That(property.SourceInfo!.Value.DefaultValueExpressionSpan.HasValue).IsTrue();

        var propertySpan = new TextSpan(property.SourceInfo.Value.PropertySpan.Start, property.SourceInfo.Value.PropertySpan.Length);
        var defaultSpanInfo = property.SourceInfo.Value.DefaultValueExpressionSpan!.Value;
        var defaultSpan = new TextSpan(defaultSpanInfo.Start, defaultSpanInfo.Length);

        await Assert.That(syntax.ToString()).IsEqualTo(syntax.SyntaxTree.GetText().ToString(propertySpan));
        await Assert.That(syntax.SyntaxTree.GetText().ToString(defaultSpan)).IsEqualTo("56");
    }

    [Test]
    public async Task ParsePropertySyntax_UnsupportedTypeSyntax_ReturnsInvalidModelFailure()
    {
        var (parser, _, model) = GetPropertySyntax(@"[Column(""id""), PrimaryKey] public abstract int Id { get; }");
        var syntax = (PropertyDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(
            @"[Column(""callback""), PrimaryKey] public abstract delegate*<void> Callback { get; }")!;

        var result = ParseMutablePropertyResult(parser, syntax, model);

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("Callback");
        await Assert.That(failure.Message).Contains("unsupported C# type syntax");
        await Assert.That(failure.Message).DoesNotContain("[Exception]");
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
        var tableModel = ParseMutableTableModel(parser, db, syntax, "MyModels");
        var model = tableModel.Model;

        await Assert.That(model.CsType.Name).IsEqualTo("MyModel");
        await Assert.That(model.CsType.Namespace).IsEqualTo("TestNamespace");
        await Assert.That(model.Attributes.Any(a => a is TableAttribute ta && ta.Name == "my_models")).IsTrue();
        await Assert.That(model.ValueProperties.Count).IsEqualTo(2);
        await Assert.That(model.ValueProperties.ContainsKey("Id")).IsTrue();
        await Assert.That(model.ValueProperties.ContainsKey("Name")).IsTrue();
        await Assert.That(model.RelationProperties).IsEmpty();
        await Assert.That(model.Usings.Any(u => u.FullNamespaceName == "DataLinq.Attributes")).IsTrue();
        await Assert.That(model.OriginalInterfaces.Single(i => i.Name == "ITableModel<TestDb>").ModelCsType).IsEqualTo(ModelCsType.Interface);
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
        var model = ParseMutableTableModel(parser, db, syntax, "MyModels").Model;

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
        var model = ParseMutableTableModel(parser, db, syntax, "MyModels").Model;
        var index = model.Attributes.OfType<IndexAttribute>().Single();

        await Assert.That(index.Name).IsEqualTo("idx_multi");
        await Assert.That(index.Characteristic).IsEqualTo(IndexCharacteristic.Unique);
        await Assert.That(index.Columns).IsEquivalentTo(["first_col", "second_col"]);
    }

    [Test]
    public async Task ParseModelSyntax_UnsupportedBaseTypeSyntax_ReturnsInvalidModelFailure()
    {
        var (parser, syntax) = GetTypeSyntax(
            """
[Table("my_models")]
public abstract partial class MyModel : delegate*<void>, ITableModel<TestDb>
{
    [Column("id"), PrimaryKey] public abstract int Id { get; }
}
""");
        var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration(typeof(TestDb)));

        var result = ParseMutableTableModelResult(parser, db, syntax, "MyModels");

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.Aggregation);

        var failureMessage = failure.ToString();
        await Assert.That(failureMessage).Contains("MyModel");
        await Assert.That(failureMessage).Contains("delegate*<void>");
        await Assert.That(failureMessage).Contains("unsupported C# type syntax");
        await Assert.That(failureMessage).DoesNotContain("[Exception]");
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
        await Assert.That(result.classSyntax!.Identifier.Text).IsEqualTo("Employee");
    }

    [Test]
    public async Task GetTableTypeSyntax_QualifiedDbRead_ReturnsReferencedModel()
    {
        const string code = """
using DataLinq.Interfaces;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel
{
    public DataLinq.DbRead<Employee> Employees { get; }
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
        await Assert.That(result.classSyntax!.Identifier.Text).IsEqualTo("Employee");
    }

    [Test]
    public async Task GetTableTypeSyntax_QualifiedModelTypeArgument_ReturnsReferencedModel()
    {
        const string code = """
using DataLinq.Interfaces;
using DataLinq;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel
{
    public DbRead<TestNamespace.Employee> Employees { get; }
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
        await Assert.That(result.classSyntax!.Identifier.Text).IsEqualTo("Employee");
    }

    [Test]
    public async Task ParseTableModelDraft_QualifiedTableInterface_ReturnsTableDraft()
    {
        const string code = """
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;

namespace TestNamespace;

public partial class TestDb : DataLinq.Interfaces.IDatabaseModel
{
    public TestDb(DataSourceAccess dataSource) { }
}

[Table("users")]
public abstract partial class UserModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<UserModel, TestNamespace.TestDb>(rowData, dataSource), DataLinq.Interfaces.ITableModel<TestNamespace.TestDb>
{
    [Column("id"), PrimaryKey] public abstract int Id { get; }
}
""";
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetCompilationUnitRoot();
        var modelSyntax = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == "UserModel");
        var allDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
        var parser = new SyntaxParser(allDeclarations);

        var result = parser.ParseTableModelDraft(
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class),
            modelSyntax,
            "Users").ValueOrException();

        await Assert.That(result.Table.Type).IsEqualTo(TableType.Table);
        await Assert.That(result.Table.DbName).IsEqualTo("users");
        await Assert.That(result.Model.OriginalInterfaces.Single().Name).IsEqualTo("ITableModel<TestDb>");
    }

    [Test]
    public async Task ParseTableModelDraft_ModelContractWithMultipleTypeArguments_ReturnsInvalidModelFailure()
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
}

[Table("users")]
public abstract partial class UserModel(IRowData rowData, IDataSourceAccess dataSource) : Immutable<UserModel, TestDb>(rowData, dataSource), ITableModel<TestDb, OtherDb>
{
    [Column("id"), PrimaryKey] public abstract int Id { get; }
}
""";
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetCompilationUnitRoot();
        var modelSyntax = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.Text == "UserModel");
        var allDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
        var parser = new SyntaxParser(allDeclarations);

        var result = parser.ParseTableModelDraft(
            new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class),
            modelSyntax,
            "Users");

        await Assert.That(result.HasValue).IsFalse();
        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("ITableModel<TestDb, OtherDb>");
        await Assert.That(failure.Message).Contains("exactly one database type argument");
    }
}
