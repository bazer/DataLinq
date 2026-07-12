using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DataLinq.Generators.Tests;

public sealed class ScalarConverterGeneratorTests : GeneratorTestBase
{
    [Test]
    public async Task ExplicitConverter_EmitsResolvedRuntimeMetadataAndSuppressesConvertedKeyFastPaths()
    {
        var source = Parse(
            """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            namespace ScalarGenerator;

            public readonly record struct CustomerId(int Value);

            public sealed class CustomerIdConverter : DataLinqScalarConverter<CustomerId, int>
            {
                public override int ToProvider(CustomerId value, in ScalarConversionContext context) => value.Value;
                public override CustomerId FromProvider(int value, in ScalarConversionContext context) => new(value);
            }

            [Database("scalar")]
            public partial class ScalarDb(DataSourceAccess dataSource) : IDatabaseModel<ScalarDb>
            {
                public DbRead<ScalarRow> Rows { get; } = new(dataSource);
            }

            [Table("scalar_rows")]
            public abstract partial class ScalarRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<ScalarRow, ScalarDb>(rowData, dataSource), ITableModel<ScalarDb>
            {
                [PrimaryKey]
                [Column("tenant")]
                public abstract int Tenant { get; }

                [PrimaryKey]
                [Column("id")]
                [ScalarConverter(typeof(CustomerIdConverter))]
                public abstract CustomerId Id { get; }
            }
            """);

        var (outputCompilation, diagnostics, generatedTrees) = RunGeneratorAgainstRuntimeWithDiagnostics([source]);
        var code = string.Join(Environment.NewLine, generatedTrees.Select(static tree => tree.ToString()));

        await AssertNoErrors(outputCompilation, diagnostics);
        await Assert.That(code).Contains("new global::DataLinq.Core.Factories.MetadataScalarConverterDraft(");
        await Assert.That(code).Contains("typeof(global::ScalarGenerator.CustomerId)");
        await Assert.That(code).Contains("typeof(global::System.Int32)");
        await Assert.That(code).Contains("static () => new global::ScalarGenerator.CustomerIdConverter()");
        await Assert.That(code).Contains("ScalarConverterOrigin.Property");
        await Assert.That(code).Contains("SourceLocation(new global::DataLinq.Metadata.CsFileDeclaration(\"ScalarConverterModel.cs\")");
        await Assert.That(code).Contains("new global::DataLinq.Attributes.ScalarConverterAttribute(typeof(global::ScalarGenerator.CustomerIdConverter))");
        await Assert.That(code).DoesNotContain("DataLinqPrimaryKey");
        await Assert.That(code).DoesNotContain("ProviderKeyRowStoreAccessor =");
        await Assert.That(code).Contains(
            "global::DataLinq.Instances.KeyFactory.CreateKeyFromModelValues([tenant, id], [DataLinqColumn_Tenant, DataLinqColumn_Id])");
        await Assert.That(code).Contains(
            "public static ScalarRow? Get(int tenant, CustomerId id, IDataSourceAccess dataSource)");
    }

    [Test]
    public async Task AssemblyRegistration_ResolvesWithoutSynthesizingAPropertyAttribute()
    {
        var source = Parse(
            """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            [assembly: ScalarConverterRegistration(typeof(ScalarGenerator.CustomerId), typeof(ScalarGenerator.CustomerIdConverter))]

            namespace ScalarGenerator;

            public readonly record struct CustomerId(int Value);

            public sealed class CustomerIdConverter : DataLinqScalarConverter<CustomerId, int>
            {
                public override int ToProvider(CustomerId value, in ScalarConversionContext context) => value.Value;
                public override CustomerId FromProvider(int value, in ScalarConversionContext context) => new(value);
            }

            [Database("scalar")]
            public partial class ScalarDb(DataSourceAccess dataSource) : IDatabaseModel<ScalarDb>
            {
                public DbRead<ScalarRow> Rows { get; } = new(dataSource);
            }

            [Table("scalar_rows")]
            public abstract partial class ScalarRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<ScalarRow, ScalarDb>(rowData, dataSource), ITableModel<ScalarDb>
            {
                [PrimaryKey, AutoIncrement, Column("id")]
                public abstract CustomerId? Id { get; }
            }
            """);

        var (outputCompilation, diagnostics, generatedTrees) = RunGeneratorAgainstRuntimeWithDiagnostics([source]);
        var code = string.Join(Environment.NewLine, generatedTrees.Select(static tree => tree.ToString()));

        await AssertNoErrors(outputCompilation, diagnostics);
        await Assert.That(code).Contains("ScalarConverterOrigin.AssemblyRegistration");
        await Assert.That(code).Contains("static () => new global::ScalarGenerator.CustomerIdConverter()");
        await Assert.That(code).Contains("CsNullable = true");
        await Assert.That(code).DoesNotContain("new global::DataLinq.Attributes.ScalarConverterAttribute(");
    }

    [Test]
    public async Task ExplicitGuidConverter_ResolvesAndEmitsGuidStorageAfterScalarMetadata()
    {
        var source = Parse(
            """
            using System;
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            namespace ScalarGenerator;

            public readonly record struct CustomerId(Guid Value);

            public sealed class CustomerIdConverter : DataLinqScalarConverter<CustomerId, Guid>
            {
                public override Guid ToProvider(CustomerId value, in ScalarConversionContext context) => value.Value;
                public override CustomerId FromProvider(Guid value, in ScalarConversionContext context) => new(value);
            }

            [Database("scalar")]
            public partial class ScalarDb(DataSourceAccess dataSource) : IDatabaseModel<ScalarDb>
            {
                public DbRead<ScalarRow> Rows { get; } = new(dataSource);
            }

            [Table("scalar_rows")]
            public abstract partial class ScalarRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<ScalarRow, ScalarDb>(rowData, dataSource), ITableModel<ScalarDb>
            {
                [PrimaryKey]
                [Type(DatabaseType.MySQL, "binary", 16)]
                [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)]
                [Column("id")]
                [ScalarConverter(typeof(CustomerIdConverter))]
                public abstract CustomerId Id { get; }
            }
            """);

        var (outputCompilation, diagnostics, generatedTrees) = RunGeneratorAgainstRuntimeWithDiagnostics([source]);
        var code = string.Join(Environment.NewLine, generatedTrees.Select(static tree => tree.ToString()));

        await AssertNoErrors(outputCompilation, diagnostics);
        await Assert.That(code).Contains("typeof(global::System.Guid)");
        await Assert.That(code).Contains(
            "new global::DataLinq.Metadata.GuidStorageDefinition(global::DataLinq.DatabaseType.MySQL, global::DataLinq.Attributes.GuidStorageFormat.Binary16Rfc4122, true)");
    }

    [Test]
    public async Task AssemblyRegisteredGuidConverter_ResolvesAndEmitsGuidStorageAfterScalarMetadata()
    {
        var source = Parse(
            """
            using System;
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            [assembly: ScalarConverterRegistration(typeof(ScalarGenerator.CustomerId), typeof(ScalarGenerator.CustomerIdConverter))]

            namespace ScalarGenerator;

            public readonly record struct CustomerId(Guid Value);

            public sealed class CustomerIdConverter : DataLinqScalarConverter<CustomerId, Guid>
            {
                public override Guid ToProvider(CustomerId value, in ScalarConversionContext context) => value.Value;
                public override CustomerId FromProvider(Guid value, in ScalarConversionContext context) => new(value);
            }

            [Database("scalar")]
            public partial class ScalarDb(DataSourceAccess dataSource) : IDatabaseModel<ScalarDb>
            {
                public DbRead<ScalarRow> Rows { get; } = new(dataSource);
            }

            [Table("scalar_rows")]
            public abstract partial class ScalarRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<ScalarRow, ScalarDb>(rowData, dataSource), ITableModel<ScalarDb>
            {
                [PrimaryKey]
                [Type(DatabaseType.SQLite, "TEXT")]
                [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Text36)]
                [Column("id")]
                public abstract CustomerId Id { get; }
            }
            """);

        var (outputCompilation, diagnostics, generatedTrees) = RunGeneratorAgainstRuntimeWithDiagnostics([source]);
        var code = string.Join(Environment.NewLine, generatedTrees.Select(static tree => tree.ToString()));

        await AssertNoErrors(outputCompilation, diagnostics);
        await Assert.That(code).Contains("ScalarConverterOrigin.AssemblyRegistration");
        await Assert.That(code).Contains("typeof(global::System.Guid)");
        await Assert.That(code).Contains(
            "new global::DataLinq.Metadata.GuidStorageDefinition(global::DataLinq.DatabaseType.SQLite, global::DataLinq.Attributes.GuidStorageFormat.Text36, true)");
        await Assert.That(code).DoesNotContain("new global::DataLinq.Attributes.ScalarConverterAttribute(");
    }

    [Test]
    public async Task GuidModelConvertedToNonGuidProviders_DoesNotResolveProvisionalUuidMetadata()
    {
        var source = Parse(
            """
            using System;
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            namespace ScalarGenerator;

            public sealed class GuidStringConverter : DataLinqScalarConverter<Guid, string>
            {
                public override string ToProvider(Guid value, in ScalarConversionContext context) => value.ToString("D");
                public override Guid FromProvider(string value, in ScalarConversionContext context) => Guid.ParseExact(value, "D");
            }

            public sealed class GuidBytesConverter : DataLinqScalarConverter<Guid, byte[]>
            {
                public override byte[] ToProvider(Guid value, in ScalarConversionContext context) => value.ToByteArray();
                public override Guid FromProvider(byte[] value, in ScalarConversionContext context) => new(value);
            }

            [Database("scalar")]
            public partial class ScalarDb(DataSourceAccess dataSource) : IDatabaseModel<ScalarDb>
            {
                public DbRead<ScalarRow> Rows { get; } = new(dataSource);
            }

            [Table("scalar_rows")]
            public abstract partial class ScalarRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<ScalarRow, ScalarDb>(rowData, dataSource), ITableModel<ScalarDb>
            {
                [PrimaryKey]
                [Type(DatabaseType.MySQL, "char", 36)]
                [Column("text_id")]
                [ScalarConverter(typeof(GuidStringConverter))]
                public abstract Guid TextId { get; }

                [Type(DatabaseType.SQLite, "BLOB")]
                [Column("blob_value")]
                [ScalarConverter(typeof(GuidBytesConverter))]
                public abstract Guid BlobValue { get; }
            }
            """);

        var (outputCompilation, diagnostics, generatedTrees) = RunGeneratorAgainstRuntimeWithDiagnostics([source]);
        var code = string.Join(Environment.NewLine, generatedTrees.Select(static tree => tree.ToString()));

        await AssertNoErrors(outputCompilation, diagnostics);
        await Assert.That(code).Contains("typeof(global::System.String)");
        await Assert.That(code).Contains("typeof(global::System.Byte[])");
        await Assert.That(code).DoesNotContain("new global::DataLinq.Metadata.GuidStorageDefinition(");
    }

    [Test]
    public async Task UserDefinedGuidName_IsNotTreatedAsSystemGuid()
    {
        var source = Parse(
            """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            namespace ScalarGenerator;

            public readonly record struct Guid(int Value);

            [Database("scalar")]
            public partial class ScalarDb(DataSourceAccess dataSource) : IDatabaseModel<ScalarDb>
            {
                public DbRead<ScalarRow> Rows { get; } = new(dataSource);
            }

            [Table("scalar_rows")]
            public abstract partial class ScalarRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<ScalarRow, ScalarDb>(rowData, dataSource), ITableModel<ScalarDb>
            {
                [PrimaryKey]
                [Type(DatabaseType.MySQL, "binary", 16)]
                [Column("id")]
                public abstract Guid Id { get; }
            }
            """);

        var (outputCompilation, diagnostics, generatedTrees) = RunGeneratorAgainstRuntimeWithDiagnostics([source]);
        var code = string.Join(Environment.NewLine, generatedTrees.Select(static tree => tree.ToString()));

        await AssertNoErrors(outputCompilation, diagnostics);
        await Assert.That(code).DoesNotContain("new global::DataLinq.Metadata.GuidStorageDefinition(");
    }

    [Test]
    public async Task ExplicitConverter_OverridesAssemblyRegistration()
    {
        var source = Parse(
            """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            [assembly: ScalarConverterRegistration(typeof(ScalarGenerator.CustomerId), typeof(ScalarGenerator.DefaultCustomerIdConverter))]

            namespace ScalarGenerator;

            public readonly record struct CustomerId(int Value);
            public sealed class DefaultCustomerIdConverter : DataLinqScalarConverter<CustomerId, int>
            {
                public override int ToProvider(CustomerId value, in ScalarConversionContext context) => value.Value;
                public override CustomerId FromProvider(int value, in ScalarConversionContext context) => new(value);
            }
            public sealed class ExplicitCustomerIdConverter : DataLinqScalarConverter<CustomerId, long>
            {
                public override long ToProvider(CustomerId value, in ScalarConversionContext context) => value.Value;
                public override CustomerId FromProvider(long value, in ScalarConversionContext context) => new(checked((int)value));
            }

            [Database("scalar")]
            public partial class ScalarDb(DataSourceAccess dataSource) : IDatabaseModel<ScalarDb>
            {
                public DbRead<ScalarRow> Rows { get; } = new(dataSource);
            }
            [Table("scalar_rows")]
            public abstract partial class ScalarRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<ScalarRow, ScalarDb>(rowData, dataSource), ITableModel<ScalarDb>
            {
                [PrimaryKey, Column("id"), ScalarConverter(typeof(ExplicitCustomerIdConverter))]
                public abstract CustomerId Id { get; }
            }
            """);

        var (outputCompilation, diagnostics, generatedTrees) = RunGeneratorAgainstRuntimeWithDiagnostics([source]);
        var code = string.Join(Environment.NewLine, generatedTrees.Select(static tree => tree.ToString()));

        await AssertNoErrors(outputCompilation, diagnostics);
        await Assert.That(code).Contains("static () => new global::ScalarGenerator.ExplicitCustomerIdConverter()");
        await Assert.That(code).Contains("typeof(global::System.Int64)");
        await Assert.That(code).DoesNotContain("static () => new global::ScalarGenerator.DefaultCustomerIdConverter()");
    }

    [Test]
    public async Task ByteArrayCanonicalProvider_EmitsFullyQualifiedArrayTypeAndCompiles()
    {
        var source = Parse(
            """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            namespace ScalarGenerator;
            public readonly record struct BinaryId(string Value);
            public sealed class BinaryIdConverter : DataLinqScalarConverter<BinaryId, byte[]>
            {
                public override byte[] ToProvider(BinaryId value, in ScalarConversionContext context) => System.Convert.FromHexString(value.Value);
                public override BinaryId FromProvider(byte[] value, in ScalarConversionContext context) => new(System.Convert.ToHexString(value));
            }
            [Database("scalar")]
            public partial class ScalarDb(DataSourceAccess dataSource) : IDatabaseModel<ScalarDb>
            {
                public DbRead<ScalarRow> Rows { get; } = new(dataSource);
            }
            [Table("scalar_rows")]
            public abstract partial class ScalarRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<ScalarRow, ScalarDb>(rowData, dataSource), ITableModel<ScalarDb>
            {
                [PrimaryKey, Column("id"), ScalarConverter(typeof(BinaryIdConverter))]
                public abstract BinaryId Id { get; }
            }
            """);

        var (outputCompilation, diagnostics, generatedTrees) = RunGeneratorAgainstRuntimeWithDiagnostics([source]);
        var code = string.Join(Environment.NewLine, generatedTrees.Select(static tree => tree.ToString()));

        await AssertNoErrors(outputCompilation, diagnostics);
        await Assert.That(code).Contains("typeof(global::System.Byte[])");
    }

    [Test]
    public async Task DuplicateAssemblyRegistration_ReportsSecondDeclarationLocation()
    {
        var source = Parse(
            """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            [assembly: ScalarConverterRegistration(typeof(ScalarGenerator.CustomerId), typeof(ScalarGenerator.CustomerIdConverter))]
            [assembly: ScalarConverterRegistration(typeof(ScalarGenerator.CustomerId), typeof(ScalarGenerator.CustomerIdConverter))]

            namespace ScalarGenerator;
            public readonly record struct CustomerId(int Value);
            public sealed class CustomerIdConverter : DataLinqScalarConverter<CustomerId, int>
            {
                public override int ToProvider(CustomerId value, in ScalarConversionContext context) => value.Value;
                public override CustomerId FromProvider(int value, in ScalarConversionContext context) => new(value);
            }
            [Database("scalar")]
            public partial class ScalarDb(DataSourceAccess dataSource) : IDatabaseModel<ScalarDb>
            {
                public DbRead<ScalarRow> Rows { get; } = new(dataSource);
            }
            [Table("scalar_rows")]
            public abstract partial class ScalarRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<ScalarRow, ScalarDb>(rowData, dataSource), ITableModel<ScalarDb>
            {
                [PrimaryKey, Column("id")]
                public abstract CustomerId Id { get; }
            }
            """);

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([source]);
        var diagnostic = diagnostics.Single(candidate => candidate.Id == "DLG001");

        await Assert.That(diagnostic.GetMessage()).Contains("Duplicate scalar converter assembly registration");
        await Assert.That(diagnostic.Location.SourceSpan.Start).IsEqualTo(source.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.AttributeSyntax>().Where(attribute => attribute.Name.ToString().Contains("ScalarConverterRegistration", StringComparison.Ordinal)).ElementAt(1).SpanStart);
    }

    [Test]
    public async Task PrivateNestedConverter_IsRejectedBeforeGeneratedCodeCompilation()
    {
        var source = Parse(CreateInvalidConverterSource(
            "private sealed class InvalidConverter : DataLinqScalarConverter<CustomerId, int> { public override int ToProvider(CustomerId value, in ScalarConversionContext context) => value.Value; public override CustomerId FromProvider(int value, in ScalarConversionContext context) => new(value); }",
            "InvalidConverter"));

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([source]);
        var diagnostic = diagnostics.Single(candidate => candidate.Id == "DLG001");

        await Assert.That(diagnostic.GetMessage()).Contains("must be accessible from generated code");
        await Assert.That(diagnostic.Location).IsNotEqualTo(Location.None);
    }

    [Test]
    public async Task StructuredCanonicalProviderType_IsRejectedAtTheAttribute()
    {
        var source = Parse(CreateInvalidConverterSource(
            "public sealed class InvalidConverter : DataLinqScalarConverter<CustomerId, ProviderPayload> { public override ProviderPayload ToProvider(CustomerId value, in ScalarConversionContext context) => new(value.Value); public override CustomerId FromProvider(ProviderPayload value, in ScalarConversionContext context) => new(value.Value); } public readonly record struct ProviderPayload(int Value);",
            "InvalidConverter"));

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([source]);
        var diagnostic = diagnostics.Single(candidate => candidate.Id == "DLG001");

        await Assert.That(diagnostic.GetMessage()).Contains("structured canonical provider type");
        await Assert.That(diagnostic.Location).IsNotEqualTo(Location.None);
    }

    [Test]
    public async Task ConverterWithoutPublicParameterlessConstructor_IsRejectedAtTheAttribute()
    {
        var source = Parse(CreateInvalidConverterSource(
            "public sealed class InvalidConverter(int seed) : DataLinqScalarConverter<CustomerId, int> { public override int ToProvider(CustomerId value, in ScalarConversionContext context) => value.Value + seed; public override CustomerId FromProvider(int value, in ScalarConversionContext context) => new(value - seed); }",
            "InvalidConverter"));

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([source]);
        var diagnostic = diagnostics.Single(candidate => candidate.Id == "DLG001");

        await Assert.That(diagnostic.GetMessage()).Contains("public parameterless constructor");
        await Assert.That(diagnostic.Location).IsNotEqualTo(Location.None);
    }

    [Test]
    public async Task ConverterModelDirectionMismatch_IsRejectedAtTheAttribute()
    {
        var source = Parse(CreateInvalidConverterSource(
            "public readonly record struct OtherId(int Value); public sealed class InvalidConverter : DataLinqScalarConverter<OtherId, int> { public override int ToProvider(OtherId value, in ScalarConversionContext context) => value.Value; public override OtherId FromProvider(int value, in ScalarConversionContext context) => new(value); }",
            "InvalidConverter"));

        var (_, diagnostics, _) = RunGeneratorWithDiagnostics([source]);
        var diagnostic = diagnostics.Single(candidate => candidate.Id == "DLG001");

        await Assert.That(diagnostic.GetMessage()).Contains("converts model type");
        await Assert.That(diagnostic.GetMessage()).Contains("CustomerId");
        await Assert.That(diagnostic.Location).IsNotEqualTo(Location.None);
    }

    [Test]
    public async Task ClosedGenericConverterIdentity_IsRejectedWithFocusedDiagnostic()
    {
        var source = Parse(
            """
            using DataLinq;
            using DataLinq.Attributes;
            using DataLinq.Instances;
            using DataLinq.Interfaces;
            using DataLinq.Mutation;

            namespace ScalarGenerator;
            public readonly record struct GenericId<T>(T Value);
            public sealed class GenericIdConverter : DataLinqScalarConverter<GenericId<int>, int>
            {
                public override int ToProvider(GenericId<int> value, in ScalarConversionContext context) => value.Value;
                public override GenericId<int> FromProvider(int value, in ScalarConversionContext context) => new(value);
            }
            [Database("scalar")]
            public partial class ScalarDb(DataSourceAccess dataSource) : IDatabaseModel<ScalarDb>
            {
                public DbRead<ScalarRow> Rows { get; } = new(dataSource);
            }
            [Table("scalar_rows")]
            public abstract partial class ScalarRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<ScalarRow, ScalarDb>(rowData, dataSource), ITableModel<ScalarDb>
            {
                [PrimaryKey, Column("id"), ScalarConverter(typeof(GenericIdConverter))]
                public abstract GenericId<int> Id { get; }
            }
            """);

        var (_, diagnostics, _) = RunGeneratorAgainstRuntimeWithDiagnostics([source]);
        var diagnostic = diagnostics.Single(candidate => candidate.Id == "DLG001");

        await Assert.That(diagnostic.GetMessage()).Contains("Generic scalar converter identities are outside the 0.9 metadata boundary");
        await Assert.That(diagnostic.Location).IsNotEqualTo(Location.None);
    }

    private static SyntaxTree Parse(string source) =>
        CSharpSyntaxTree.ParseText(source, path: "ScalarConverterModel.cs");

    private static async Task AssertNoErrors(Compilation outputCompilation, ImmutableArray<Diagnostic> generatorDiagnostics)
    {
        var errors = generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        await Assert.That(errors).IsEmpty();
    }

    private static (Compilation outputCompilation, ImmutableArray<Diagnostic> diagnostics, IEnumerable<SyntaxTree> generatedTrees) RunGeneratorAgainstRuntimeWithDiagnostics(
        IEnumerable<SyntaxTree> syntaxTrees)
    {
        var sourceTrees = syntaxTrees.ToArray();
        var generatorAssembly = typeof(ModelGenerator).Assembly;
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly =>
                !assembly.IsDynamic &&
                !string.IsNullOrEmpty(assembly.Location) &&
                assembly != generatorAssembly &&
                !string.Equals(assembly.GetName().Name, "DataLinq", StringComparison.Ordinal))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = outputDirectory.Name;
        var configuration = outputDirectory.Parent?.Name
            ?? throw new InvalidOperationException("Could not resolve the generator test build configuration.");
        var runtimeAssemblyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "DataLinq", "bin", configuration, targetFramework, "DataLinq.dll"));
        if (!File.Exists(runtimeAssemblyPath))
            throw new FileNotFoundException("The runtime DataLinq reference required for generated-output compilation was not built.", runtimeAssemblyPath);

        references.Add(MetadataReference.CreateFromFile(runtimeAssemblyPath));

        var compilation = CSharpCompilation.Create(
            "ScalarConverterRuntimeCompilation",
            sourceTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        IIncrementalGenerator generator = new ModelGenerator();
        var driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedTrees = outputCompilation.SyntaxTrees
            .Where(tree => !sourceTrees.Any(source => source.FilePath == tree.FilePath && source.ToString() == tree.ToString()));
        return (outputCompilation, diagnostics, generatedTrees);
    }

    private static string CreateInvalidConverterSource(string converterDeclaration, string converterName) =>
        $$"""
        using DataLinq;
        using DataLinq.Attributes;
        using DataLinq.Instances;
        using DataLinq.Interfaces;
        using DataLinq.Mutation;

        namespace ScalarGenerator;

        public static class Container
        {
            public readonly record struct CustomerId(int Value);
            {{converterDeclaration}}

            [Database("scalar")]
            public partial class ScalarDb(DataSourceAccess dataSource) : IDatabaseModel<ScalarDb>
            {
                public DbRead<ScalarRow> Rows { get; } = new(dataSource);
            }
            [Table("scalar_rows")]
            public abstract partial class ScalarRow(IRowData rowData, IDataSourceAccess dataSource)
                : Immutable<ScalarRow, ScalarDb>(rowData, dataSource), ITableModel<ScalarDb>
            {
                [PrimaryKey, Column("id"), ScalarConverter(typeof({{converterName}}))]
                public abstract CustomerId Id { get; }
            }
        }
        """;
}
