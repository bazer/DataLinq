using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using Microsoft.CodeAnalysis;

namespace DataLinq.SourceGenerators;

internal sealed class PreparedDatabaseInput(DatabaseEmissionInput? emission, IReadOnlyList<Diagnostic> diagnostics)
{
    public DatabaseEmissionInput? Emission { get; } = emission;
    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics;
}

internal sealed class DatabaseEmissionOutput(DatabaseDefinition database, GeneratedDatabaseEmissionResult result)
{
    public DatabaseDefinition Database { get; } = database;
    public GeneratedDatabaseEmissionResult Result { get; } = result;
}

/// <summary>
/// Comparable inputs to source formatting. Syntax-derived metadata is represented by its
/// declaration dependencies; facts resolved against Compilation are captured explicitly.
/// Keep this signature in sync with semantic preparation and GeneratorFileFactory options.
/// </summary>
internal sealed class DatabaseEmissionInput
{
    public DatabaseDefinition Database { get; }
    public GeneratorFileFactoryOptions DatabaseOptions { get; }
    public IReadOnlyDictionary<TableModel, GeneratorFileFactoryOptions> TableOptions { get; }
    public ImmutableArray<string> Signature { get; }
    public bool CanReuse { get; }

    public DatabaseEmissionInput(DatabaseDefinition database, GeneratorFileFactoryOptions databaseOptions,
        IReadOnlyDictionary<TableModel, GeneratorFileFactoryOptions> tableOptions,
        ImmutableArray<ModelDeclarationInput> declarations, ImmutableArray<EnumDeclarationInput> enums)
    {
        Database = database;
        DatabaseOptions = databaseOptions;
        TableOptions = tableOptions;
        var signature = ImmutableArray.CreateBuilder<string>();
        void Add(string? value) => signature.Add(value ?? "<null>");
        void AddType(CsTypeDeclaration type)
        {
            Add(type.Namespace); Add(type.Name); Add(type.ModelCsType.ToString()); Add(type.Type?.FullName);
        }
        void AddSnapshot(ModelDeclarationSnapshot snapshot)
        {
            Add(snapshot.Namespace); Add(snapshot.Name); Add(snapshot.StructuralText);
            Add(snapshot.NullableAnnotationsDisabled.ToString()); Add(snapshot.PropertyNullableAnnotationContext);
        }

        Add(database.Name);
        AddType(database.CsType);
        Add(databaseOptions.UseNullableReferenceTypes.ToString());
        Add(databaseOptions.SupportsReadSourceDatabaseConstruction.ToString());
        foreach (var name in databaseOptions.ReadSourceConstructorModelTypeNames.OrderBy(static name => name, StringComparer.Ordinal))
            Add(name);
        Add("/constructors");

        // New/renamed declarations can change name resolution or introduce ambiguities.
        foreach (var declaration in declarations)
        {
            Add(declaration.Snapshot.Namespace); Add(declaration.Snapshot.Name);
        }
        Add("/declaration-identities");

        var dependencies = new HashSet<(string Namespace, string Name)>
        {
            (database.CsType.Namespace, database.CsType.Name)
        };
        foreach (var table in database.TableModels)
            dependencies.Add((table.Model.CsType.Namespace, table.Model.CsType.Name));
        foreach (var declaration in declarations)
        {
            if (dependencies.Contains((declaration.Snapshot.Namespace, declaration.Snapshot.Name)))
                AddSnapshot(declaration.Snapshot);
        }
        CanReuse = dependencies.All(dependency => declarations.Any(declaration =>
            declaration.Snapshot.Namespace == dependency.Namespace && declaration.Snapshot.Name == dependency.Name));
        Add("/model-shapes");

        // Enum parsing can affect literal/default metadata. Conservatively retain every enum
        // until dependency tracking exists in SyntaxParser; unrelated methods still reuse.
        foreach (var declaration in enums)
            AddSnapshot(declaration.Snapshot);
        Add("/enum-shapes");
        foreach (var item in database.Usings)
            Add(item.FullNamespaceName);
        Add("/database-usings");

        foreach (var table in database.TableModels)
        {
            Add(table.Table.DbName); AddType(table.Model.CsType); Add(table.IsStub.ToString());
            foreach (var item in table.Model.Usings)
                Add(item.FullNamespaceName);
            Add("/model-usings");
            if (table.IsStub)
                continue;
            var options = tableOptions[table];
            Add(options.UseNullableReferenceTypes.ToString());
            foreach (var property in table.Model.ValueProperties.Values)
            {
                Add(property.PropertyName);
                Add(options.RuntimeValuePropertyTypeNames.TryGetValue(property, out var typeName) ? typeName : null);
                Add(options.SuppressedDefaultValueProperties.Contains(property).ToString());
                var mapping = property.Column.ScalarMapping;
                AddType(mapping.ModelCsType); AddType(mapping.ProviderCsType);
                Add(mapping.HasConverter.ToString());
                if (mapping.ConverterCsType is { } converter)
                    AddType(converter);
                Add(mapping.Origin.ToString());
                // These anchors are embedded in generated converter metadata.
                Add(mapping.SourceLocation?.File.Name);
                Add(mapping.SourceLocation?.Span?.Start.ToString(System.Globalization.CultureInfo.InvariantCulture));
                Add(mapping.SourceLocation?.Span?.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (var storage in property.Column.GuidStorageDefinitions)
                {
                    Add(storage.DatabaseType.ToString()); Add(storage.Format.ToString()); Add(storage.IsExplicit.ToString());
                }
                Add("/guid-storage");
                foreach (var provider in property.Column.UnresolvedGuidStorageProviders)
                    Add(provider.ToString());
                Add("/property");
            }
            Add("/table");
        }
        Signature = signature.ToImmutable();
    }
}

internal sealed class DatabaseEmissionInputComparer : IEqualityComparer<DatabaseEmissionInput>
{
    public static DatabaseEmissionInputComparer Instance { get; } = new();

    public bool Equals(DatabaseEmissionInput? x, DatabaseEmissionInput? y) => ReferenceEquals(x, y) ||
        x is { CanReuse: true } && y is { CanReuse: true } && x.Signature.SequenceEqual(y.Signature, StringComparer.Ordinal);

    public int GetHashCode(DatabaseEmissionInput obj)
    {
        unchecked
        {
            var hash = 17;
            foreach (var value in obj.Signature)
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(value);
            return hash;
        }
    }
}
