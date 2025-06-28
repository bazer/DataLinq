using System.Collections.Immutable;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories; // Where SyntaxParser resides
using DataLinq.Interfaces;
using DataLinq.Metadata;
using DataLinq.Mutation;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DataLinq.Tests.Core
{
    public partial class TestDb : IDatabaseModel
    {
        // Add a constructor if your DatabaseDefinition creation expects one,
        // otherwise an empty class might suffice if typeof() is all you need.
        // Since your real DB likely needs DataSourceAccess, let's add it.
        public TestDb(DataSourceAccess dsa) { }
    }


    /*
       Helper Methods:

            GetAttributeSyntax: Parses code containing just an attribute and extracts the AttributeSyntax node.

            GetPropertySyntax: Parses code defining a property within a dummy class and extracts the PropertyDeclarationSyntax node. It also creates a dummy ModelDefinition needed for context by ParseProperty.

            GetTypeSyntax: Parses code defining a class/interface and extracts the TypeDeclarationSyntax.

            SyntaxParser Instantiation: Crucially, the SyntaxParser needs the collection of all relevant TypeDeclarationSyntax nodes from the compilation/trees being considered. This is why the helpers parse the whole dummy code snippet and pass the resulting array to the SyntaxParser constructor.

        Attribute Tests: Each test focuses on a specific attribute, providing the C# code snippet for it, parsing it, and asserting that the correct Attribute object with the right properties is returned by SyntaxParser.ParseAttribute.

        Property Tests: These use GetPropertySyntax to get the syntax node and then call SyntaxParser.ParseProperty, verifying the resulting ValueProperty or RelationProperty has the correct name, type (CsTypeDeclaration), nullability, and parsed attributes. The Enum test also checks the EnumProperty struct.

        Model Tests: These use GetTypeSyntax and then call SyntaxParser.ParseTableModel (which internally calls ParseModel). They verify the overall ModelDefinition structure – name, namespace, attributes, properties, usings, and that the associated TableModel and TableDefinition are created.

        GetTableType Test: Specifically tests the helper function used to extract the model type (Employee) from a DbRead<Employee> property declaration.

        Dummy Classes/DB: The helpers create minimal necessary dummy classes (TestDb, TestModel) within the parsed code strings to satisfy the type constraints (ITableModel<TestDb>, etc.) required by your actual model base classes/interfaces.

        Error Handling: The tests assume successful parsing (Assert.True(result.HasValue)). You could add separate tests to check how the parser handles invalid syntax or missing information, expecting a failed Option.
     */

    public class SyntaxParserTests
    {
        // Helper to get the first AttributeSyntax of a specific name from code
        // (This one is likely fine as attributes often don't directly reference TestDb)
        private (SyntaxParser parser, AttributeSyntax attributeSyntax) GetAttributeSyntax(string attributeCode)
        {
            string code = $@"
using DataLinq.Attributes;
using DataLinq.Metadata; // For DatabaseType etc. if needed in attributes
using System; // For other types if needed

namespace TestNamespace;

{attributeCode} // e.g., [Table(""my_table"")]
public class DummyClass {{}}
";
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetCompilationUnitRoot();
            var attributeSyntax = root.DescendantNodes().OfType<AttributeSyntax>().First();
            var allDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
            var parser = new SyntaxParser(allDeclarations);
            return (parser, attributeSyntax);
        }

        // Helper to get the first PropertyDeclarationSyntax from code
        // *** CORRECTED: Added TestDb definition ***
        private (SyntaxParser parser, PropertyDeclarationSyntax propertySyntax, ModelDefinition model) GetPropertySyntax(string propertyCode, string modelInterfaces = "ITableModel<TestDb>")
        {
            string code = $@"
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;
using System;
using System.Collections.Generic; // For IEnumerable if needed

namespace TestNamespace;

// ***** ADDED Dummy DB model for context *****
public partial class TestDb : IDatabaseModel {{ public TestDb(DataSourceAccess dsa) {{}} }} // Added constructor
// ******************************************

public abstract partial class TestModel(RowData rowData, DataSourceAccess dataSource) : Immutable<TestModel, TestDb>(rowData, dataSource), {modelInterfaces} // Added base class and constructor params
{{
    {propertyCode} // e.g., [Column(""my_col"")] public abstract string MyProp {{ get; }} // Made abstract
}}
";
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetCompilationUnitRoot();
            var propertySyntax = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().First();
            var classSyntax = root.DescendantNodes().OfType<TypeDeclarationSyntax>().First(x => x.Identifier.Text == "TestModel");
            var allDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
            var parser = new SyntaxParser(allDeclarations);

            var dbCsType = new CsTypeDeclaration("TestDb", "TestNamespace", ModelCsType.Class);
            var db = new DatabaseDefinition("TestDb", dbCsType);
            var modelResult = parser.ParseTableModel(db, classSyntax, "TestModels");
            Assert.True(modelResult.HasValue, modelResult.HasFailed ? modelResult.Failure.ToString() : "Model parsing failed unexpectedly."); // Better assert message


            return (parser, propertySyntax, modelResult.Value.Model);
        }

        // Helper to get the first TypeDeclarationSyntax (class/interface) from code
        // *** CORRECTED: Added TestDb definition ***
        private (SyntaxParser parser, TypeDeclarationSyntax typeSyntax) GetTypeSyntax(string classCode)
        {
            string code = $@"
using DataLinq.Attributes;
using DataLinq.Interfaces;
using DataLinq.Instances;
using DataLinq.Mutation;
using System;
using System.Collections.Generic;

namespace TestNamespace;

// ***** ADDED Dummy DB model for context *****
public partial class TestDb : IDatabaseModel {{ public TestDb(DataSourceAccess dsa) {{}} }} // Added constructor
// ******************************************

{classCode} // e.g., [Table(""Test"")] public abstract partial class MyModel : ITableModel<TestDb> {{...}} // Added abstract/partial/constructor
";
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetCompilationUnitRoot();
            // Ensure we get the target class, not TestDb
            var typeSyntax = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                                 .First(ts => ts is ClassDeclarationSyntax cds && !cds.Identifier.ValueText.EndsWith("TestDb"));
            var allDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
            var parser = new SyntaxParser(allDeclarations);
            return (parser, typeSyntax);
        }

        // --- Attribute Parsing Tests ---

        [Fact]
        public void TestParseAttributeSyntax_Table()
        {
            var (parser, syntax) = GetAttributeSyntax(@"[Table(""my_table"")]");
            var result = parser.ParseAttribute(syntax);
            Assert.True(result.HasValue);
            var attribute = Assert.IsType<TableAttribute>(result.Value);
            Assert.Equal("my_table", attribute.Name);
        }

        [Fact]
        public void TestParseAttributeSyntax_Column()
        {
            var (parser, syntax) = GetAttributeSyntax(@"[Column(""my_col"")]");
            var result = parser.ParseAttribute(syntax);
            Assert.True(result.HasValue);
            var attribute = Assert.IsType<ColumnAttribute>(result.Value);
            Assert.Equal("my_col", attribute.Name);
        }

        [Fact]
        public void TestParseAttributeSyntax_PrimaryKey()
        {
            var (parser, syntax) = GetAttributeSyntax(@"[PrimaryKey]");
            var result = parser.ParseAttribute(syntax);
            Assert.True(result.HasValue);
            Assert.IsType<PrimaryKeyAttribute>(result.Value);
        }

        [Fact]
        public void TestParseAttributeSyntax_ForeignKey()
        {
            var (parser, syntax) = GetAttributeSyntax(@"[ForeignKey(""OtherTable"", ""OtherId"", ""FK_Name"")]");
            var result = parser.ParseAttribute(syntax);
            Assert.True(result.HasValue);
            var attribute = Assert.IsType<ForeignKeyAttribute>(result.Value);
            Assert.Equal("OtherTable", attribute.Table);
            Assert.Equal("OtherId", attribute.Column);
            Assert.Equal("FK_Name", attribute.Name);
        }

        [Fact]
        public void TestParseAttributeSyntax_Relation()
        {
            var (parser, syntax) = GetAttributeSyntax(@"[Relation(""OtherTable"", ""FkCol"", ""RelName"")]");
            var result = parser.ParseAttribute(syntax);
            Assert.True(result.HasValue);
            var attribute = Assert.IsType<RelationAttribute>(result.Value);
            Assert.Equal("OtherTable", attribute.Table);
            Assert.Single(attribute.Columns);
            Assert.Equal("FkCol", attribute.Columns[0]);
            Assert.Equal("RelName", attribute.Name);
        }

        [Fact]
        public void TestParseAttributeSyntax_Index_Simple()
        {
            var (parser, syntax) = GetAttributeSyntax(@"[Index(""idx_test"", IndexCharacteristic.Simple)]");
            var result = parser.ParseAttribute(syntax);
            Assert.True(result.HasValue);
            var attribute = Assert.IsType<IndexAttribute>(result.Value);
            Assert.Equal("idx_test", attribute.Name);
            Assert.Equal(IndexCharacteristic.Simple, attribute.Characteristic);
            Assert.Equal(IndexType.BTREE, attribute.Type); // Default
            Assert.Empty(attribute.Columns);
        }

        [Fact]
        public void TestParseAttributeSyntax_Index_MultiColumn()
        {
            var (parser, syntax) = GetAttributeSyntax(@"[Index(""idx_multi"", IndexCharacteristic.Unique, IndexType.HASH, ""col1"", ""col2"")]");
            var result = parser.ParseAttribute(syntax);
            Assert.True(result.HasValue);
            var attribute = Assert.IsType<IndexAttribute>(result.Value);
            Assert.Equal("idx_multi", attribute.Name);
            Assert.Equal(IndexCharacteristic.Unique, attribute.Characteristic);
            Assert.Equal(IndexType.HASH, attribute.Type);
            Assert.Equal(2, attribute.Columns.Length);
            Assert.Equal("col1", attribute.Columns[0]);
            Assert.Equal("col2", attribute.Columns[1]);
        }

        [Fact]
        public void TestParseAttributeSyntax_Type_SpecificDb()
        {
            var (parser, syntax) = GetAttributeSyntax(@"[Type(DatabaseType.MySQL, ""VARCHAR"", 100)]");
            var result = parser.ParseAttribute(syntax);
            Assert.True(result.HasValue);
            var attribute = Assert.IsType<TypeAttribute>(result.Value);
            Assert.Equal(DatabaseType.MySQL, attribute.DatabaseType);
            Assert.Equal("VARCHAR", attribute.Name);
            Assert.Equal((ulong)100, attribute.Length);
        }

        [Fact]
        public void TestParseAttributeSyntax_DefaultValue_String()
        {
            var (parser, syntax) = GetAttributeSyntax(@"[Default(""Hello"")]");
            var result = parser.ParseAttribute(syntax);
            Assert.True(result.HasValue);
            var attribute = Assert.IsType<DefaultAttribute>(result.Value);
            Assert.Equal("Hello", attribute.Value);
        }

        [Fact]
        public void TestParseAttributeSyntax_DefaultValue_Timestamp()
        {
            var (parser, syntax) = GetAttributeSyntax(@"[DefaultCurrentTimestamp]");
            var result = parser.ParseAttribute(syntax);
            Assert.True(result.HasValue);
            Assert.IsType<DefaultCurrentTimestampAttribute>(result.Value);
        }

        [Fact]
        public void TestParseAttributeSyntax_Interface_Named()
        {
            var (parser, syntax) = GetAttributeSyntax(@"[Interface(""IMyThing"")]");
            var result = parser.ParseAttribute(syntax);
            Assert.True(result.HasValue);
            var attribute = Assert.IsType<InterfaceAttribute>(result.Value);
            Assert.True(attribute.GenerateInterface);
            Assert.Equal("IMyThing", attribute.Name);
        }

        // --- Property Parsing Tests ---

        [Fact]
        public void TestParsePropertySyntax_Value()
        {
            var (parser, syntax, model) = GetPropertySyntax(@"[Column(""my_col""), Nullable] public string? Name { get; }");
            var result = parser.ParseProperty(syntax, model);
            Assert.True(result.HasValue);
            var prop = Assert.IsType<ValueProperty>(result.Value);
            Assert.Equal("Name", prop.PropertyName);
            Assert.Equal("string", prop.CsType.Name);
            Assert.True(prop.CsNullable);
            Assert.Contains(prop.Attributes, a => a is ColumnAttribute ca && ca.Name == "my_col");
            Assert.Contains(prop.Attributes, a => a is NullableAttribute);
        }

        [Fact]
        public void TestParsePropertySyntax_Relation()
        {
            var (parser, syntax, model) = GetPropertySyntax(@"[Relation(""Other"", ""FkId"")] public Other Related { get; }");
            var result = parser.ParseProperty(syntax, model);
            Assert.True(result.HasValue);
            var prop = Assert.IsType<RelationProperty>(result.Value);
            Assert.Equal("Related", prop.PropertyName);
            Assert.Equal("Other", prop.CsType.Name); // Type is Other
            Assert.Contains(prop.Attributes, a => a is RelationAttribute);
        }

        [Fact]
        public void TestParsePropertySyntax_Enum()
        {
            var (parser, syntax, model) = GetPropertySyntax(@"[Column(""status_col""), Enum(""Active"", ""Inactive"")] public StatusEnum Status { get; }");
            var result = parser.ParseProperty(syntax, model);
            Assert.True(result.HasValue);
            var prop = Assert.IsType<ValueProperty>(result.Value);
            Assert.Equal("Status", prop.PropertyName);
            Assert.Equal("StatusEnum", prop.CsType.Name);
            Assert.NotNull(prop.EnumProperty); // Enum property struct should be created
            Assert.Equal(2, prop.EnumProperty.Value.CsValuesOrDbValues.Count);
            Assert.Equal("Active", prop.EnumProperty.Value.CsValuesOrDbValues[0].name);
            Assert.Equal("Inactive", prop.EnumProperty.Value.CsValuesOrDbValues[1].name);
        }

        // --- Model Parsing Tests ---

        [Fact]
        public void TestParseModelSyntax_Basic()
        {
            var (parser, syntax) = GetTypeSyntax(
@"
[Table(""my_models"")]
public abstract partial class MyModel : ITableModel<TestDb>
{
    [Column(""id""), PrimaryKey] public abstract int Id { get; }
    [Column(""name"")] public abstract string Name { get; }
}
");
            var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration(typeof(TestDb)));
            var result = parser.ParseTableModel(db, syntax, "MyModels");
            Assert.True(result.HasValue);
            var tableModel = result.Value;
            var model = tableModel.Model;

            Assert.Equal("MyModel", model.CsType.Name);
            Assert.Equal("TestNamespace", model.CsType.Namespace);
            Assert.Contains(model.Attributes, a => a is TableAttribute ta && ta.Name == "my_models");
            Assert.Equal(2, model.ValueProperties.Count);
            Assert.True(model.ValueProperties.ContainsKey("Id"));
            Assert.True(model.ValueProperties.ContainsKey("Name"));
            Assert.Empty(model.RelationProperties);
            Assert.Contains(model.Usings, u => u.FullNamespaceName == "DataLinq.Attributes"); // Check for some usings
            Assert.NotNull(tableModel.Table);
            Assert.Equal("my_models", tableModel.Table.DbName);
        }

        [Fact]
        public void TestParseModelSyntax_WithInterfaceAttribute()
        {
            var (parser, syntax) = GetTypeSyntax(
@"
[Table(""my_models"")]
[Interface(""IMySpecialModel"")]
public abstract partial class MyModel : ITableModel<TestDb>
{
    [Column(""id"")] public abstract int Id { get; }
}
");
            var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration(typeof(TestDb)));
            var result = parser.ParseTableModel(db, syntax, "MyModels");
            Assert.True(result.HasValue);
            var model = result.Value.Model;

            Assert.NotNull(model.ModelInstanceInterface);
            Assert.Equal("IMySpecialModel", model.ModelInstanceInterface.Value.Name);
            Assert.Equal("TestNamespace", model.ModelInstanceInterface.Value.Namespace);
        }

        // --- Helper Function Parsing Test ---

        [Fact]
        public void TestGetTableTypeSyntax()
        {
            string code = @"
using DataLinq.Interfaces;
using DataLinq;

namespace TestNamespace;

public partial class TestDb : IDatabaseModel
{
    public DbRead<Employee> Employees { get; } // Target property
}

public abstract partial class Employee : ITableModel<TestDb> { } // Referenced model
";
            var syntaxTree = CSharpSyntaxTree.ParseText(code);
            var root = syntaxTree.GetCompilationUnitRoot();
            var propertySyntax = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().First(p => p.Identifier.Text == "Employees");
            var modelSyntaxes = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToImmutableArray();
            var parser = new SyntaxParser(modelSyntaxes);
            var db = new DatabaseDefinition("TestDb", new CsTypeDeclaration(typeof(TestDb))); // Provide DB context

            var result = parser.GetTableType(propertySyntax, modelSyntaxes.ToList());

            Assert.True(result.HasValue);
            Assert.Equal("Employees", result.Value.csPropertyName); // Check property name is kept
            Assert.NotNull(result.Value.classSyntax);
            Assert.Equal("Employee", result.Value.classSyntax.Identifier.Text); // Check correct model type syntax found
        }
    }
}