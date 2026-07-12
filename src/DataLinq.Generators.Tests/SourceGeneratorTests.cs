using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public class SourceGeneratorTests : GeneratorTestBase
{
    private static string SyntaxTreesToString(IEnumerable<SyntaxTree> syntaxTrees)
        => string.Join(Environment.NewLine, syntaxTrees.Select(x => x.ToString()));

    private IEnumerable<SyntaxTree> GenerateCodeFromFolder(string[] directories, bool includeSubfolders)
    {
        var projectRoot = Directory.GetParent(Environment.CurrentDirectory)?.Parent?.Parent?.Parent;
        if (projectRoot is null)
            throw new InvalidOperationException("Could not locate the generator test project root.");

        var sources = directories
            .SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(projectRoot.FullName, directory),
                "*.cs",
                new EnumerationOptions { RecurseSubdirectories = includeSubfolders }))
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path));

        return RunGenerator(sources);
    }

    [Test]
    public async Task TestEmployees()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models/employees/gen"], true).ToList();
        var code = SyntaxTreesToString(syntax);

        await Assert.That(code.Contains("public partial class ImmutableEmployee", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public static IImmutableInstance NewDataLinqImmutableInstance(IRowData rowData, IDataSourceAccess dataSource) => new ImmutableEmployee(rowData, dataSource);", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("NewDataLinqReadImmutableInstance", StringComparison.Ordinal)).IsFalse();
        await Assert.That(code.Contains("ReadSourceImmutableFactory", StringComparison.Ordinal)).IsFalse();
        await Assert.That(code.Contains("public partial class EmployeesDb : global::DataLinq.Interfaces.IDatabaseModel<EmployeesDb>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("new global::DataLinq.Tests.Models.Employees.EmployeesDb(dataSource);", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public static EmployeesDb NewDataLinqReadDatabase(global::DataLinq.Interfaces.IDataLinqReadSource readSource)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("new global::DataLinq.Tests.Models.Employees.EmployeesDb(readSource);", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("(global::DataLinq.Mutation.DataSourceAccess)dataSource", StringComparison.Ordinal)).IsFalse();
        await Assert.That(code.Contains("public static global::DataLinq.Metadata.GeneratedDatabaseModelDeclaration GetDataLinqGeneratedModel() =>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public static global::DataLinq.Core.Factories.MetadataDatabaseDraft GetDataLinqGeneratedMetadata() =>", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public static void SetDataLinqGeneratedMetadata(global::DataLinq.Metadata.DatabaseDefinition metadata)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public static global::DataLinq.Metadata.GeneratedTableModelDeclaration[] GetDataLinqGeneratedTableModels() =>", StringComparison.Ordinal)).IsFalse();
        await Assert.That(code.Contains("new(\"Employees\", typeof(global::DataLinq.Tests.Models.Employees.Employee), typeof(global::DataLinq.Tests.Models.Employees.ImmutableEmployee), typeof(global::DataLinq.Tests.Models.Employees.MutableEmployee), new global::System.Func<global::DataLinq.Instances.IRowData, global::DataLinq.Interfaces.IDataSourceAccess, global::DataLinq.Instances.IImmutableInstance>(global::DataLinq.Tests.Models.Employees.ImmutableEmployee.NewDataLinqImmutableInstance), global::DataLinq.Metadata.TableType.Table),", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("new global::DataLinq.Core.Factories.MetadataValuePropertyDraft(", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("protected const int DataLinqColumnIndex_emp_no", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("internal static void SetDataLinqGeneratedModel(global::DataLinq.Metadata.ModelDefinition model)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("GetNullableValue(DataLinqColumnIndex_emp_no)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("emp_no.HasValue ? GetImmutableRelation<Dept_emp, int>(emp_no.Value, DataLinqRelation_dept_emp) : GetImmutableRelationFromKey<Dept_emp>(global::DataLinq.Instances.DataLinqKey.Null, DataLinqRelation_dept_emp)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("GetImmutableForeignKey<Department, string>(dept_no, DataLinqRelation_departments)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("private IImmutableForeignKey<Department>? _departments;", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("IImmutable<Employee>.GetByProviderKey(empNo, dataSource)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("IImmutable<Department>.GetByProviderKey(deptNo, dataSource)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("IImmutable<Employee>.Get(KeyFactory.CreateKeyFromValue(empNo), dataSource)", StringComparison.Ordinal)).IsFalse();
        await Assert.That(code.Contains("ProviderKeyRowStoreAccessor = new global::DataLinq.Tests.Models.Employees.Employee.DataLinqProviderKeyRowStoreAccessor()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("return cache.TryAddRow(providerKey, rowData.Size, row);", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public bool TryAddCanonicalRow(global::DataLinq.Cache.RowCache cache, global::DataLinq.Instances.DataLinqKey canonicalProviderKey, global::DataLinq.Instances.RowData rowData, global::DataLinq.Instances.IImmutableInstance row)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("if (!TryCreateDataLinqPrimaryKey(canonicalProviderKey, out var providerKey))", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("internal readonly record struct DataLinqPrimaryKey(string deptNo, int empNo) : IProviderKey", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("internal static bool TryCreateDataLinqPrimaryKey(IRowData rowData, out DataLinqPrimaryKey providerKey)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("internal static bool TryCreateDataLinqPrimaryKey(global::DataLinq.IDataLinqDataReader reader, global::System.Collections.Generic.IReadOnlyList<int> primaryKeyOrdinals, out int providerKey)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("internal sealed class DataLinqProviderKeyRowStoreAccessor : global::DataLinq.Instances.IProviderKeyDataReaderRowStoreAccessor", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public bool TryGetRow(global::DataLinq.Cache.TableCache tableCache, global::DataLinq.IDataLinqDataReader reader, global::System.Collections.Generic.IReadOnlyList<int> primaryKeyOrdinals, global::DataLinq.Interfaces.IDataSourceAccess dataSource, out global::DataLinq.Instances.IImmutableInstance? row)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("IImmutable<Dept_emp>.GetByProviderKey(new DataLinqPrimaryKey(deptNo, empNo), dataSource)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public bool TryGetRow(global::DataLinq.Cache.RowCache cache, global::DataLinq.Instances.DataLinqKey key, out global::DataLinq.Instances.IImmutableInstance? row)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public bool TryRemoveRow(global::DataLinq.Cache.RowCache cache, global::DataLinq.Instances.DataLinqKey key, out int numRowsRemoved)", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("IImmutable<Dept_emp>.Get(KeyFactory.CreateKeyFromValues([deptNo, empNo]), dataSource)", StringComparison.Ordinal)).IsFalse();
        await Assert.That(code.Contains("GetValue(nameof(", StringComparison.Ordinal)).IsFalse();
        await Assert.That(code.Contains("SetValue(nameof(", StringComparison.Ordinal)).IsFalse();
        await Assert.That(code.Contains("GetImmutableRelation<Dept_emp>(nameof(", StringComparison.Ordinal)).IsFalse();
        await Assert.That(code.Contains("GetImmutableForeignKey<Department>(nameof(", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task TestEmployeesOnlyDb()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models/employees/gen"], false).ToList();
        var code = SyntaxTreesToString(syntax);

        await Assert.That(string.IsNullOrEmpty(code)).IsTrue();
    }

    [Test]
    public async Task TestAllround()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models/Allround"], true).ToList();
        var code = SyntaxTreesToString(syntax);

        await Assert.That(code.Contains("public partial class ImmutablePayment", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public partial class MutablePayment", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("new global::DataLinq.Tests.Models.Allround.AllroundBenchmark(dataSource);", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("new global::DataLinq.Tests.Models.Allround.AllroundBenchmark(readSource);", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("(global::DataLinq.Mutation.DataSourceAccess)dataSource", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task TestAllroundOnlyDb()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models/Allround"], false).ToList();
        var code = SyntaxTreesToString(syntax);

        await Assert.That(string.IsNullOrEmpty(code)).IsTrue();
    }

    [Test]
    public async Task TestAllModels()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.Tests.Models/employees/gen", "DataLinq.Tests.Models/Allround"], true).ToList();
        var code = SyntaxTreesToString(syntax);

        await Assert.That(code.Contains("public partial class ImmutableLocation", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public partial class MutableLocation", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public partial class ImmutablePayment", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("public partial class MutableEmployee", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task TestPlatformSmoke()
    {
        var syntax = GenerateCodeFromFolder(["DataLinq.PlatformCompatibility.Smoke"], false).ToList();
        var code = SyntaxTreesToString(syntax);

        await Assert.That(code.Contains("new global::DataLinq.PlatformCompatibility.Smoke.PlatformSmokeDb(dataSource);", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("new global::DataLinq.PlatformCompatibility.Smoke.PlatformSmokeDb(readSource);", StringComparison.Ordinal)).IsTrue();
        await Assert.That(code.Contains("(global::DataLinq.Mutation.DataSourceAccess)dataSource", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task GeneratedMetadata_ReferencedCustomEnumWithoutDataLinqEnumMetadata_UsesRuntimeType()
    {
        var source = CSharpSyntaxTree.ParseText(
            """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;
            using DataLinq.Generators.Tests;

            namespace RuntimeTypeTest;

            [Database("runtime_type")]
            public partial class RuntimeTypeDb(DataSourceAccess dataSource) : IDatabaseModel<RuntimeTypeDb>
            {
                public DbRead<RuntimeTypeRow> Rows { get; } = new(dataSource);
            }

            [Table("runtime_type_rows")]
            public abstract partial class RuntimeTypeRow(IRowData rowData, IDataSourceAccess dataSource) : Immutable<RuntimeTypeRow, RuntimeTypeDb>(rowData, dataSource), ITableModel<RuntimeTypeDb>
            {
                [PrimaryKey]
                [Type(DatabaseType.MariaDB, "int", 11)]
                [Column("id")]
                public abstract int Id { get; }

                [Type(DatabaseType.MariaDB, "smallint", 5, false)]
                [Column("status")]
                public abstract ExternalNumericStatus Status { get; }
            }
            """,
            path: "RuntimeTypeSource.cs");

        var (_, diagnostics, generatedTrees) = RunGeneratorWithDiagnostics([source]);
        var code = SyntaxTreesToString(generatedTrees);

        var generatorErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        await Assert.That(generatorErrors).IsEmpty();
        await Assert.That(code).Contains("new global::DataLinq.Metadata.CsTypeDeclaration(typeof(global::DataLinq.Generators.Tests.ExternalNumericStatus))");
        await Assert.That(code).DoesNotContain("new global::DataLinq.Metadata.CsTypeDeclaration(\"ExternalNumericStatus\"");
    }

    [Test]
    public async Task GeneratedMetadata_GuidStorageDeclarations_ArePreservedAndCompile()
    {
        var source = CSharpSyntaxTree.ParseText(
            """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            namespace GuidStorageGeneratorTest;

            [Database("guid_storage")]
            public partial class GuidStorageDb(DataSourceAccess dataSource) : IDatabaseModel<GuidStorageDb>
            {
                public DbRead<GuidStorageRow> Rows { get; } = new(dataSource);
            }

            [Table("guid_storage_rows")]
            public abstract partial class GuidStorageRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<GuidStorageRow, GuidStorageDb>(rowData, dataSource), ITableModel<GuidStorageDb>
            {
                [PrimaryKey]
                [Type(DatabaseType.MySQL, "binary", 16)]
                [Type(DatabaseType.SQLite, "TEXT")]
                [GuidStorage(GuidStorageFormat.Text36)]
                [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)]
                [Column("id")]
                public abstract System.Guid Id { get; }
            }
            """,
            path: "GuidStorageGeneratorTest.cs");

        var (outputCompilation, generatorDiagnostics, generatedTrees) =
            RunGeneratorWithDiagnostics([source]);
        var code = SyntaxTreesToString(generatedTrees);
        var generatorErrors = generatorDiagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();
        var compilationErrors = outputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        await Assert.That(generatorErrors).IsEmpty();
        await Assert.That(compilationErrors).IsEmpty();
        await Assert.That(code).Contains(
            "new global::DataLinq.Attributes.GuidStorageAttribute(global::DataLinq.Attributes.GuidStorageFormat.Text36)");
        await Assert.That(code).Contains(
            "new global::DataLinq.Attributes.GuidStorageAttribute(global::DataLinq.DatabaseType.MySQL, global::DataLinq.Attributes.GuidStorageFormat.Binary16Rfc4122)");
        await Assert.That(code).Contains(
            "new global::DataLinq.Metadata.GuidStorageDefinition(global::DataLinq.DatabaseType.MySQL, global::DataLinq.Attributes.GuidStorageFormat.Binary16Rfc4122, true)");
        await Assert.That(code).Contains(
            "new global::DataLinq.Metadata.GuidStorageDefinition(global::DataLinq.DatabaseType.SQLite, global::DataLinq.Attributes.GuidStorageFormat.Text36, true)");
    }

    [Test]
    public async Task UnresolvedGuidStorage_BlocksCompilationAndSuppressesDatabaseOutput()
    {
        var source = CSharpSyntaxTree.ParseText(
            """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            namespace UnresolvedGuidStorageGeneratorTest;

            [Database("unresolved_guid_storage")]
            public partial class UnresolvedGuidStorageDb(DataSourceAccess dataSource)
                : IDatabaseModel<UnresolvedGuidStorageDb>
            {
                public DbRead<UnresolvedGuidStorageRow> Rows { get; } = new(dataSource);
            }

            [Table("unresolved_guid_storage_rows")]
            public abstract partial class UnresolvedGuidStorageRow(
                IRowData rowData,
                IDataSourceAccess dataSource)
                : Immutable<UnresolvedGuidStorageRow, UnresolvedGuidStorageDb>(rowData, dataSource),
                  ITableModel<UnresolvedGuidStorageDb>
            {
                #error DATALINQ_UUID_STORAGE_UNRESOLVED: choose the UUID byte order.
                [PrimaryKey]
                [Type(DatabaseType.MySQL, "binary", 16)]
                [GuidStorageUnresolved(DatabaseType.MySQL)]
                [Column("id")]
                public abstract System.Guid Id { get; }
            }
            """,
            path: "UnresolvedGuidStorageGeneratorTest.cs");

        var (outputCompilation, generatorDiagnostics, generatedTrees) =
            RunGeneratorWithDiagnostics([source]);
        var diagnostic = generatorDiagnostics.Single(x => x.Id == "DLG001");
        var generatedCode = SyntaxTreesToString(generatedTrees);

        await Assert.That(diagnostic.Severity).IsEqualTo(DiagnosticSeverity.Error);
        await Assert.That(diagnostic.GetMessage()).Contains("UUID storage");
        await Assert.That(diagnostic.GetMessage()).Contains("unresolved");
        await Assert.That(outputCompilation.GetDiagnostics()
            .Any(x => x.Id == "CS1029")).IsTrue();
        await Assert.That(generatedCode).DoesNotContain(
            "ImmutableUnresolvedGuidStorageRow");
        await Assert.That(generatedCode).DoesNotContain(
            "IDatabaseModel<UnresolvedGuidStorageDb>");
    }

    [Test]
    public async Task ReadSourceConstructor_EmitsNeutralFactoryAndCompiles()
    {
        var source = CSharpSyntaxTree.ParseText(
            """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            namespace NeutralReadSourceGeneratorTest;

            [Database("neutral_read_source")]
            public partial class NeutralReadSourceDb(IDataLinqReadSource readSource) : IDatabaseModel<NeutralReadSourceDb>
            {
                public DbRead<NeutralReadSourceRow> Rows { get; } = new(readSource);
            }

            [Table("neutral_read_source_rows")]
            public abstract partial class NeutralReadSourceRow(IRowData rowData, IDataLinqReadSource readSource)
                : Immutable<NeutralReadSourceRow, NeutralReadSourceDb>(rowData, readSource), ITableModel<NeutralReadSourceDb>
            {
                [PrimaryKey]
                [Type(DatabaseType.SQLite, "INTEGER")]
                [Column("id")]
                public abstract int Id { get; }
            }
            """,
            path: "NeutralReadSourceGeneratorTest.cs");

        var (outputCompilation, generatorDiagnostics, generatedTrees) = RunGeneratorWithDiagnostics([source]);
        var code = SyntaxTreesToString(generatedTrees);
        var generatorErrors = generatorDiagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();
        var compilationErrors = outputCompilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToList();

        await Assert.That(generatorErrors).IsEmpty();
        await Assert.That(compilationErrors).IsEmpty();
        await Assert.That(code).Contains(
            "public ImmutableNeutralReadSourceRow(IRowData rowData, IDataSourceAccess dataSource)");
        await Assert.That(code).Contains(
            "public static IImmutableInstance NewDataLinqImmutableInstance(IRowData rowData, IDataSourceAccess dataSource) => new ImmutableNeutralReadSourceRow(rowData, dataSource);");
        await Assert.That(code).Contains(
            "public ImmutableNeutralReadSourceRow(IRowData rowData, IDataLinqReadSource readSource)");
        await Assert.That(code).Contains(
            "public static IImmutableInstance NewDataLinqReadImmutableInstance(IRowData rowData, IDataLinqReadSource readSource) => new ImmutableNeutralReadSourceRow(rowData, readSource);");
        await Assert.That(code).Contains(
            "ReadSourceImmutableFactory = new global::System.Func<global::DataLinq.Instances.IRowData, global::DataLinq.Interfaces.IDataLinqReadSource, global::DataLinq.Instances.IImmutableInstance>(global::NeutralReadSourceGeneratorTest.ImmutableNeutralReadSourceRow.NewDataLinqReadImmutableInstance),");
        await Assert.That(code).Contains(
            "public static NeutralReadSourceDb NewDataLinqDatabase(global::DataLinq.Interfaces.IDataSourceAccess dataSource) =>");
        await Assert.That(code).Contains(
            "new global::NeutralReadSourceGeneratorTest.NeutralReadSourceDb(dataSource);");
        await Assert.That(code).Contains(
            "public static NeutralReadSourceDb NewDataLinqReadDatabase(global::DataLinq.Interfaces.IDataLinqReadSource readSource) =>");
        await Assert.That(code).Contains(
            "new global::NeutralReadSourceGeneratorTest.NeutralReadSourceDb(readSource);");
        await Assert.That(code).DoesNotContain("DynamicInvoke");
        await Assert.That(code).DoesNotContain("(IDataSourceAccess)readSource");
        await Assert.That(code).DoesNotContain("(global::DataLinq.Interfaces.IDataSourceAccess)readSource");
        await Assert.That(code).DoesNotContain("(global::DataLinq.Mutation.DataSourceAccess)dataSource");
    }
}

public enum ExternalNumericStatus : short
{
    Unknown = 0,
    Active = 1
}
