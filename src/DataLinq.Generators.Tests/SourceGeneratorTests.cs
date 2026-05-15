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
        await Assert.That(code.Contains("public partial class EmployeesDb : global::DataLinq.Interfaces.IDatabaseModel<EmployeesDb>", StringComparison.Ordinal)).IsTrue();
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
}

public enum ExternalNumericStatus : short
{
    Unknown = 0,
    Active = 1
}
