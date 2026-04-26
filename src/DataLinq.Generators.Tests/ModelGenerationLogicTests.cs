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
    private static readonly string InvalidDefaultValueSourcePath = GeneratorTestPaths.TestModel("InvalidDefaultModel.cs");
    private static readonly string InvalidCacheLimitSourcePath = GeneratorTestPaths.TestModel("InvalidCacheLimitModel.cs");
    private static readonly string InvalidCacheLimitAmountSourcePath = GeneratorTestPaths.TestModel("InvalidCacheLimitAmountModel.cs");
    private static readonly string InvalidTypeAttributeSourcePath = GeneratorTestPaths.TestModel("InvalidTypeAttributeModel.cs");
    private static readonly string InvalidInterfaceAttributeSourcePath = GeneratorTestPaths.TestModel("InvalidInterfaceAttributeModel.cs");
    private static readonly string InvalidEnumValueSourcePath = GeneratorTestPaths.TestModel("InvalidEnumValueModel.cs");
    private static readonly string MissingNamespaceSourcePath = GeneratorTestPaths.TestModel("MissingNamespaceModel.cs");
    private static readonly string InvalidForeignKeySourcePath = GeneratorTestPaths.TestModel("InvalidForeignKeyModel.cs");
    private static readonly string DuplicateTableSourcePath = GeneratorTestPaths.TestModel("DuplicateTableModel.cs");
    private static readonly string DuplicateDatabaseSourcePath = GeneratorTestPaths.TestModel("DuplicateDatabaseModel.cs");
    private static readonly string InvalidIndexSourcePath = GeneratorTestPaths.TestModel("InvalidIndexModel.cs");
    private static readonly string InvalidIndexTypeSourcePath = GeneratorTestPaths.TestModel("InvalidIndexTypeModel.cs");
    private static readonly string EmptyIndexNameSourcePath = GeneratorTestPaths.TestModel("EmptyIndexNameModel.cs");
    private static readonly string DuplicateColumnSourcePath = GeneratorTestPaths.TestModel("DuplicateColumnModel.cs");
    private static readonly string MissingPrimaryKeySourcePath = GeneratorTestPaths.TestModel("MissingPrimaryKeyModel.cs");
    private static readonly string InvalidRelationSourcePath = GeneratorTestPaths.TestModel("InvalidRelationModel.cs");
    private static readonly string InvalidTablePropertySourcePath = GeneratorTestPaths.TestModel("InvalidTablePropertyModel.cs");
    private static readonly string MissingTableModelSourcePath = GeneratorTestPaths.TestModel("MissingTableModel.cs");

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

    private const string InvalidCacheLimitAmountModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestInvalidCacheLimitAmountNamespace;

    [CacheLimit(CacheLimitType.Rows, 1.5)]
    public partial class InvalidCacheLimitAmountDb : IDatabaseModel
    {
        public InvalidCacheLimitAmountDb(DataSourceAccess dsa){}
        public DbRead<InvalidCacheLimitAmountModel> Rows { get; }
    }

    [Table(""rows"")]
    public abstract partial class InvalidCacheLimitAmountModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<InvalidCacheLimitAmountModel, InvalidCacheLimitAmountDb>(rowData, dataSource), ITableModel<InvalidCacheLimitAmountDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }
    }";

    private const string InvalidTypeAttributeModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestInvalidTypeAttributeNamespace;

    public partial class InvalidTypeAttributeDb : IDatabaseModel
    {
        public InvalidTypeAttributeDb(DataSourceAccess dsa){}
        public DbRead<InvalidTypeAttributeModel> Rows { get; }
    }

    [Table(""rows"")]
    public abstract partial class InvalidTypeAttributeModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<InvalidTypeAttributeModel, InvalidTypeAttributeDb>(rowData, dataSource), ITableModel<InvalidTypeAttributeDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }

        [Column(""name"")]
        [Type(DatabaseType.MySQL, ""VARCHAR"", ""not_length"")]
        public abstract string Name { get; }
    }";

    private const string InvalidInterfaceAttributeModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestInvalidInterfaceAttributeNamespace;

    public partial class InvalidInterfaceAttributeDb : IDatabaseModel
    {
        public InvalidInterfaceAttributeDb(DataSourceAccess dsa){}
        public DbRead<InvalidInterfaceAttributeModel> Rows { get; }
    }

    public partial interface IInvalidInterfaceAttributeModel {}

    [Table(""rows"")]
    [Interface<IInvalidInterfaceAttributeModel>(""maybe"")]
    public abstract partial class InvalidInterfaceAttributeModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<InvalidInterfaceAttributeModel, InvalidInterfaceAttributeDb>(rowData, dataSource), ITableModel<InvalidInterfaceAttributeDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }
    }";

    private const string InvalidEnumValueModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestInvalidEnumValueNamespace;

    public partial class InvalidEnumValueDb : IDatabaseModel
    {
        public InvalidEnumValueDb(DataSourceAccess dsa){}
        public DbRead<InvalidEnumValueModel> Rows { get; }
    }

    [Table(""rows"")]
    public abstract partial class InvalidEnumValueModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<InvalidEnumValueModel, InvalidEnumValueDb>(rowData, dataSource), ITableModel<InvalidEnumValueDb>
    {
        public enum RowStatus
        {
            Active = 1 + 1,
            Archived
        }

        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }

        [Column(""status"")]
        [Enum(""Active"", ""Archived"")]
        public abstract RowStatus Status { get; }
    }";

    private const string MissingNamespaceModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    public partial class MissingNamespaceDb : IDatabaseModel
    {
        public MissingNamespaceDb(DataSourceAccess dsa){}
        public DbRead<MissingNamespaceModel> Rows { get; }
    }

    [Table(""rows"")]
    public abstract partial class MissingNamespaceModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<MissingNamespaceModel, MissingNamespaceDb>(rowData, dataSource), ITableModel<MissingNamespaceDb>
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

    private const string DuplicateTableModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestDuplicateTableNamespace;

    public partial class DuplicateTableDb : IDatabaseModel
    {
        public DuplicateTableDb(DataSourceAccess dsa){}
        public DbRead<UserModel> Users { get; }
        public DbRead<ArchivedUserModel> ArchivedUsers { get; }
    }

    [Table(""users"")]
    public abstract partial class UserModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<UserModel, DuplicateTableDb>(rowData, dataSource), ITableModel<DuplicateTableDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }
    }

    [Table(""users"")]
    public abstract partial class ArchivedUserModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<ArchivedUserModel, DuplicateTableDb>(rowData, dataSource), ITableModel<DuplicateTableDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }
    }";

    private const string DuplicateDatabaseModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestDuplicateDatabaseNamespace;

    [Database(""duplicate_db"")]
    public partial class FirstDuplicateDb : IDatabaseModel
    {
        public FirstDuplicateDb(DataSourceAccess dsa){}
        public DbRead<FirstRowModel> FirstRows { get; }
    }

    [Database(""duplicate_db"")]
    public partial class SecondDuplicateDb : IDatabaseModel
    {
        public SecondDuplicateDb(DataSourceAccess dsa){}
        public DbRead<SecondRowModel> SecondRows { get; }
    }

    [Table(""first_rows"")]
    public abstract partial class FirstRowModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<FirstRowModel, FirstDuplicateDb>(rowData, dataSource), ITableModel<FirstDuplicateDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }
    }

    [Table(""second_rows"")]
    public abstract partial class SecondRowModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<SecondRowModel, SecondDuplicateDb>(rowData, dataSource), ITableModel<SecondDuplicateDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }
    }";

    private const string InvalidIndexModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestInvalidIndexNamespace;

    public partial class InvalidIndexDb : IDatabaseModel
    {
        public InvalidIndexDb(DataSourceAccess dsa){}
        public DbRead<IndexedRowModel> IndexedRows { get; }
    }

    [Table(""indexed_rows"")]
    public abstract partial class IndexedRowModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<IndexedRowModel, InvalidIndexDb>(rowData, dataSource), ITableModel<InvalidIndexDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }

        [Column(""name"")]
        [Index(""idx_missing_column"", IndexCharacteristic.Simple, IndexType.BTREE, ""name"", ""missing_column"")]
        public abstract string Name { get; }
    }";

    private const string InvalidIndexTypeModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestInvalidIndexTypeNamespace;

    public partial class InvalidIndexTypeDb : IDatabaseModel
    {
        public InvalidIndexTypeDb(DataSourceAccess dsa){}
        public DbRead<InvalidIndexTypeRowModel> Rows { get; }
    }

    [Table(""rows"")]
    public abstract partial class InvalidIndexTypeRowModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<InvalidIndexTypeRowModel, InvalidIndexTypeDb>(rowData, dataSource), ITableModel<InvalidIndexTypeDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }

        [Column(""name"")]
        [Index(""idx_invalid_fulltext"", IndexCharacteristic.Unique, IndexType.FULLTEXT)]
        public abstract string Name { get; }
    }";

    private const string EmptyIndexNameModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestEmptyIndexNameNamespace;

    public partial class EmptyIndexNameDb : IDatabaseModel
    {
        public EmptyIndexNameDb(DataSourceAccess dsa){}
        public DbRead<EmptyIndexNameRowModel> Rows { get; }
    }

    [Table(""rows"")]
    public abstract partial class EmptyIndexNameRowModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<EmptyIndexNameRowModel, EmptyIndexNameDb>(rowData, dataSource), ITableModel<EmptyIndexNameDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }

        [Column(""name"")]
        [Index("""", IndexCharacteristic.Simple)]
        public abstract string Name { get; }
    }";

    private const string DuplicateColumnModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestDuplicateColumnNamespace;

    public partial class DuplicateColumnDb : IDatabaseModel
    {
        public DuplicateColumnDb(DataSourceAccess dsa){}
        public DbRead<DuplicateColumnRowModel> Rows { get; }
    }

    [Table(""duplicate_column_rows"")]
    public abstract partial class DuplicateColumnRowModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<DuplicateColumnRowModel, DuplicateColumnDb>(rowData, dataSource), ITableModel<DuplicateColumnDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }

        [Column(""name"")]
        public abstract string FirstName { get; }

        [Column(""name"")]
        public abstract string DisplayName { get; }
    }";

    private const string MissingPrimaryKeyModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestMissingPrimaryKeyNamespace;

    public partial class MissingPrimaryKeyDb : IDatabaseModel
    {
        public MissingPrimaryKeyDb(DataSourceAccess dsa){}
        public DbRead<NoPrimaryKeyRowModel> Rows { get; }
    }

    [Table(""no_primary_key_rows"")]
    public abstract partial class NoPrimaryKeyRowModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<NoPrimaryKeyRowModel, MissingPrimaryKeyDb>(rowData, dataSource), ITableModel<MissingPrimaryKeyDb>
    {
        [Column(""name"")]
        public abstract string Name { get; }
    }";

    private const string InvalidRelationModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestInvalidRelationNamespace;

    public partial class InvalidRelationDb : IDatabaseModel
    {
        public InvalidRelationDb(DataSourceAccess dsa){}
        public DbRead<UserRelationModel> Users { get; }
        public DbRead<OrderRelationModel> Orders { get; }
    }

    [Table(""users"")]
    public abstract partial class UserRelationModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<UserRelationModel, InvalidRelationDb>(rowData, dataSource), ITableModel<InvalidRelationDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }

        [Relation(""orders"", ""wrong_user_id"", ""FK_Order_User"")]
        public abstract IImmutableRelation<OrderRelationModel> Orders { get; }
    }

    [Table(""orders"")]
    public abstract partial class OrderRelationModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<OrderRelationModel, InvalidRelationDb>(rowData, dataSource), ITableModel<InvalidRelationDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }

        [Column(""user_id""), ForeignKey(""users"", ""id"", ""FK_Order_User"")]
        public abstract int UserId { get; }
    }";

    private const string InvalidTablePropertyModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Instances;
    using DataLinq.Mutation;
    using DataLinq;
    using System.Collections.Generic;

    namespace TestInvalidTablePropertyNamespace;

    public partial class InvalidTablePropertyDb : IDatabaseModel
    {
        public InvalidTablePropertyDb(DataSourceAccess dsa){}
        public DbRead<List<TablePropertyRowModel>> Rows { get; }
    }

    [Table(""table_property_rows"")]
    public abstract partial class TablePropertyRowModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<TablePropertyRowModel, InvalidTablePropertyDb>(rowData, dataSource), ITableModel<InvalidTablePropertyDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }
    }";

    private const string MissingTableModelSource = @"
    using DataLinq.Attributes;
    using DataLinq.Interfaces;
    using DataLinq.Mutation;
    using DataLinq;

    namespace TestMissingTableModelNamespace;

    public partial class MissingTableModelDb : IDatabaseModel
    {
        public MissingTableModelDb(DataSourceAccess dsa){}
        public DbRead<ExistingRowModel> ExistingRows { get; }
        public DbRead<MissingRowModel> MissingRows { get; }
    }

    [Table(""existing_rows"")]
    public abstract partial class ExistingRowModel(IRowData rowData, IDataSourceAccess dataSource)
        : Immutable<ExistingRowModel, MissingTableModelDb>(rowData, dataSource), ITableModel<MissingTableModelDb>
    {
        [PrimaryKey, AutoIncrement, Column(""id"")]
        public abstract int? Id { get; }
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
    public async Task InvalidCacheLimitAmount_ShouldReportDiagnosticAtAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(InvalidCacheLimitAmountModelSource, path: InvalidCacheLimitAmountSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(InvalidCacheLimitAmountSourcePath);
        await Assert.That(highlightedSource).IsEqualTo("CacheLimit(CacheLimitType.Rows, 1.5)");
        await Assert.That(diagnostic.GetMessage().Contains("Invalid cache limit amount", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("1.5", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task InvalidTypeAttributeArgument_ShouldReportDiagnosticAtTypeAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(InvalidTypeAttributeModelSource, path: InvalidTypeAttributeSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(InvalidTypeAttributeSourcePath);
        await Assert.That(highlightedSource).IsEqualTo(@"Type(DatabaseType.MySQL, ""VARCHAR"", ""not_length"")");
        await Assert.That(diagnostic.GetMessage().Contains("Invalid TypeAttribute length or signed value", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("not_length", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task InvalidInterfaceAttributeArgument_ShouldReportDiagnosticAtInterfaceAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(InvalidInterfaceAttributeModelSource, path: InvalidInterfaceAttributeSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(InvalidInterfaceAttributeSourcePath);
        await Assert.That(highlightedSource).IsEqualTo(@"Interface<IInvalidInterfaceAttributeModel>(""maybe"")");
        await Assert.That(diagnostic.GetMessage().Contains("Invalid InterfaceAttribute generateInterface value", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("maybe", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task InvalidEnumValue_ShouldReportDiagnosticAtEnumValueLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(InvalidEnumValueModelSource, path: InvalidEnumValueSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(InvalidEnumValueSourcePath);
        await Assert.That(highlightedSource).IsEqualTo("1 + 1");
        await Assert.That(diagnostic.GetMessage().Contains("Invalid enum value", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("Active", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task MissingNamespace_ShouldReportModelFileGenerationDiagnosticAtModelLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(MissingNamespaceModelSource, path: MissingNamespaceSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG002");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(MissingNamespaceSourcePath);
        await Assert.That(highlightedSource.Contains("class MissingNamespaceModel", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("Namespace is missing", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("MissingNamespaceModel", StringComparison.Ordinal)).IsTrue();
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

    [Test]
    public async Task DuplicateTableNames_ShouldReportDiagnosticAtDuplicateTableAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(DuplicateTableModelSource, path: DuplicateTableSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(DuplicateTableSourcePath);
        await Assert.That(highlightedSource).IsEqualTo(@"Table(""users"")");
        await Assert.That(diagnostic.GetMessage().Contains("Duplicate table definition", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("ArchivedUserModel", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task DuplicateDatabaseNames_ShouldReportDiagnosticAtDuplicateDatabaseAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(DuplicateDatabaseModelSource, path: DuplicateDatabaseSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(DuplicateDatabaseSourcePath);
        await Assert.That(highlightedSource).IsEqualTo(@"Database(""duplicate_db"")");
        await Assert.That(diagnostic.GetMessage().Contains("Duplicate database definition", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("SecondDuplicateDb", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task InvalidIndexColumn_ShouldReportDiagnosticAtIndexAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(InvalidIndexModelSource, path: InvalidIndexSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(InvalidIndexSourcePath);
        await Assert.That(highlightedSource.Contains("Index", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("missing_column", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("idx_missing_column", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task InvalidIndexTypeCombination_ShouldReportDiagnosticAtIndexAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(InvalidIndexTypeModelSource, path: InvalidIndexTypeSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(InvalidIndexTypeSourcePath);
        await Assert.That(highlightedSource).IsEqualTo(@"Index(""idx_invalid_fulltext"", IndexCharacteristic.Unique, IndexType.FULLTEXT)");
        await Assert.That(diagnostic.GetMessage().Contains("A FULLTEXT index cannot be a primary key or unique", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task EmptyIndexName_ShouldReportDiagnosticAtIndexAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(EmptyIndexNameModelSource, path: EmptyIndexNameSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(EmptyIndexNameSourcePath);
        await Assert.That(highlightedSource).IsEqualTo(@"Index("""", IndexCharacteristic.Simple)");
        await Assert.That(diagnostic.GetMessage().Contains("Index name cannot be empty", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task DuplicateColumnNames_ShouldReportDiagnosticAtDuplicateColumnAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(DuplicateColumnModelSource, path: DuplicateColumnSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(DuplicateColumnSourcePath);
        await Assert.That(highlightedSource).IsEqualTo(@"Column(""name"")");
        await Assert.That(diagnostic.GetMessage().Contains("Duplicate column definition", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("DisplayName", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task MissingPrimaryKey_ShouldReportDiagnosticAtTableAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(MissingPrimaryKeyModelSource, path: MissingPrimaryKeySourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(MissingPrimaryKeySourcePath);
        await Assert.That(highlightedSource).IsEqualTo(@"Table(""no_primary_key_rows"")");
        await Assert.That(diagnostic.GetMessage().Contains("missing a primary key", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task InvalidRelation_ShouldReportDiagnosticAtRelationAttributeLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(InvalidRelationModelSource, path: InvalidRelationSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(InvalidRelationSourcePath);
        await Assert.That(highlightedSource).IsEqualTo(@"Relation(""orders"", ""wrong_user_id"", ""FK_Order_User"")");
        await Assert.That(diagnostic.GetMessage().Contains("could not be resolved", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("wrong_user_id", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task InvalidTableProperty_ShouldReportDiagnosticAtDbReadPropertyLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(InvalidTablePropertyModelSource, path: InvalidTablePropertySourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(InvalidTablePropertySourcePath);
        await Assert.That(highlightedSource.Contains("DbRead<List<TablePropertyRowModel>>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("must use a simple model type argument", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("List<TablePropertyRowModel>", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task MissingTableModel_ShouldReportDiagnosticAtDbReadPropertyLocation()
    {
        var inputTree = CSharpSyntaxTree.ParseText(MissingTableModelSource, path: MissingTableModelSourcePath);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([inputTree]);

        var diagnostic = diagnostics.Single(x => x.Id == "DLG001");
        var highlightedSource = inputTree.GetText().ToString(diagnostic.Location.SourceSpan);
        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.Location.GetLineSpan().Path).IsEqualTo(MissingTableModelSourcePath);
        await Assert.That(highlightedSource.Contains("DbRead<MissingRowModel>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("MissingRows", StringComparison.Ordinal)).IsTrue();
        await Assert.That(diagnostic.GetMessage().Contains("MissingRowModel", StringComparison.Ordinal)).IsTrue();
    }
}
