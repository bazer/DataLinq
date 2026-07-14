using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.Validation;

public sealed class SchemaComparer
{
    private readonly SchemaComparisonOptions options;

    public SchemaComparer(SchemaComparisonOptions options)
    {
        this.options = options;
    }

    public static IReadOnlyList<SchemaDifference> Compare(
        DatabaseDefinition model,
        DatabaseDefinition database,
        DatabaseType databaseType)
    {
        return new SchemaComparer(SchemaComparisonOptions.For(databaseType))
            .Compare(model, database);
    }

    public IReadOnlyList<SchemaDifference> Compare(DatabaseDefinition model, DatabaseDefinition database)
    {
        var differences = new List<SchemaDifference>();

        if (options.Capabilities.CompareTables)
            CompareTables(model, database, differences);

        return differences;
    }

    private void CompareTables(
        DatabaseDefinition model,
        DatabaseDefinition database,
        List<SchemaDifference> differences)
    {
        var tableComparer = options.Capabilities.TableNameComparer;
        var modelTables = GetComparableTables(model)
            .OrderBy(x => x.Table.DbName, StringComparer.Ordinal)
            .ToList();
        var databaseTables = GetComparableTables(database)
            .OrderBy(x => x.Table.DbName, StringComparer.Ordinal)
            .ToList();
        var modelTableLookup = BuildTableLookup(modelTables, tableComparer);
        var databaseTableLookup = BuildTableLookup(databaseTables, tableComparer);

        foreach (var modelTable in modelTables)
        {
            if (!databaseTableLookup.TryGetValue(modelTable.Table.DbName, out var databaseTable))
            {
                var objectKind = FormatObjectKind(modelTable.Table);
                differences.Add(new SchemaDifference(
                    SchemaDifferenceKind.MissingTable,
                    SchemaDifferenceSeverity.Error,
                    SchemaDifferenceSafety.Additive,
                    modelTable.Table.DbName,
                    $"Database is missing {objectKind} '{modelTable.Table.DbName}'.",
                    modelTable.Table,
                    null));
                continue;
            }

            if (modelTable.Table.Type != databaseTable.Table.Type)
            {
                differences.Add(new SchemaDifference(
                    SchemaDifferenceKind.TableTypeMismatch,
                    SchemaDifferenceSeverity.Error,
                    SchemaDifferenceSafety.Ambiguous,
                    modelTable.Table.DbName,
                    $"Database object type differs at '{modelTable.Table.DbName}'. Model: '{modelTable.Table.Type}', database: '{databaseTable.Table.Type}'.",
                    modelTable.Table,
                    databaseTable.Table));
            }

            if (options.Capabilities.CompareColumns)
                CompareColumns(modelTable.Table, databaseTable.Table, differences);

            if (modelTable.Table.Type == TableType.Table && databaseTable.Table.Type == TableType.Table)
            {
                if (options.Capabilities.CompareIndexes)
                    CompareIndexes(modelTable.Table, databaseTable.Table, differences);

                if (options.Capabilities.CompareForeignKeys)
                    CompareForeignKeys(modelTable.Table, databaseTable.Table, differences);

                if (options.Capabilities.CompareChecks)
                    CompareChecks(modelTable.Table, databaseTable.Table, differences);

                if (options.Capabilities.CompareComments)
                    CompareTableComment(modelTable.Table, databaseTable.Table, differences);
            }
        }

        foreach (var databaseTable in databaseTables)
        {
            if (modelTableLookup.ContainsKey(databaseTable.Table.DbName))
                continue;

            var objectKind = FormatObjectKind(databaseTable.Table);
            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.ExtraTable,
                SchemaDifferenceSeverity.Warning,
                SchemaDifferenceSafety.Destructive,
                databaseTable.Table.DbName,
                $"Database has extra {objectKind} '{databaseTable.Table.DbName}' that is not represented by the model.",
                null,
                databaseTable.Table));
        }
    }

    private void CompareColumns(
        TableDefinition modelTable,
        TableDefinition databaseTable,
        List<SchemaDifference> differences)
    {
        var columnComparison = ToStringComparison(options.Capabilities.ColumnNameComparison);
        var modelColumns = modelTable.Columns
            .OrderBy(x => x.DbName, StringComparer.Ordinal)
            .ToList();
        var databaseColumns = databaseTable.Columns
            .OrderBy(x => x.DbName, StringComparer.Ordinal)
            .ToList();

        foreach (var modelColumn in modelColumns)
        {
            if (databaseTable.TryGetColumnByDbName(modelColumn.DbName, columnComparison, out var databaseColumn))
            {
                CompareColumnShape(modelColumn, databaseColumn, differences);
                continue;
            }

            var path = $"{modelTable.DbName}.{modelColumn.DbName}";
            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.MissingColumn,
                SchemaDifferenceSeverity.Error,
                SchemaDifferenceSafety.Additive,
                path,
                $"Database table '{databaseTable.DbName}' is missing column '{modelColumn.DbName}'.",
                modelColumn,
                null));
        }

        foreach (var databaseColumn in databaseColumns)
        {
            if (modelTable.TryGetColumnByDbName(databaseColumn.DbName, columnComparison, out _))
                continue;

            var path = $"{databaseTable.DbName}.{databaseColumn.DbName}";
            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.ExtraColumn,
                SchemaDifferenceSeverity.Warning,
                SchemaDifferenceSafety.Destructive,
                path,
                $"Database table '{databaseTable.DbName}' has extra column '{databaseColumn.DbName}' that is not represented by the model.",
                null,
                databaseColumn));
        }
    }

    private void CompareColumnShape(
        ColumnDefinition modelColumn,
        ColumnDefinition databaseColumn,
        List<SchemaDifference> differences)
    {
        var path = $"{modelColumn.Table.DbName}.{modelColumn.DbName}";

        if (options.Capabilities.CompareColumnTypes)
        {
            var modelPhysicalType = EffectiveColumnTypeResolver.Resolve(
                modelColumn,
                options.Capabilities.DatabaseType);
            var databasePhysicalType = GetExactDatabaseType(databaseColumn);
            var modelTypeSignature = FormatDbType(modelPhysicalType);
            var databaseTypeSignature = FormatDbType(databasePhysicalType);

            AddMismatchIfDifferent(
                differences,
                SchemaDifferenceKind.ColumnTypeMismatch,
                path,
                "type",
                modelTypeSignature,
                databaseTypeSignature,
                modelColumn,
                databaseColumn);

            if (string.Equals(modelTypeSignature, databaseTypeSignature, StringComparison.Ordinal))
            {
                CompareCanonicalProviderTypeCompatibility(
                    modelColumn,
                    databaseColumn,
                    databasePhysicalType,
                    differences);

                CompareGuidStorage(
                    modelColumn,
                    databaseColumn,
                    modelPhysicalType,
                    databasePhysicalType,
                    differences);
            }
        }

        if (options.Capabilities.CompareNullability)
        {
            AddMismatchIfDifferent(
                differences,
                SchemaDifferenceKind.ColumnNullabilityMismatch,
                path,
                "nullability",
                modelColumn.Nullable,
                databaseColumn.Nullable,
                modelColumn,
                databaseColumn);
        }

        if (options.Capabilities.ComparePrimaryKeys)
        {
            AddMismatchIfDifferent(
                differences,
                SchemaDifferenceKind.ColumnPrimaryKeyMismatch,
                path,
                "primary key flag",
                modelColumn.PrimaryKey,
                databaseColumn.PrimaryKey,
                modelColumn,
                databaseColumn);
        }

        if (options.Capabilities.CompareAutoIncrement)
        {
            AddMismatchIfDifferent(
                differences,
                SchemaDifferenceKind.ColumnAutoIncrementMismatch,
                path,
                "auto-increment flag",
                modelColumn.AutoIncrement,
                databaseColumn.AutoIncrement,
                modelColumn,
                databaseColumn);
        }

        if (options.Capabilities.CompareDefaults)
        {
            AddMismatchIfDifferent(
                differences,
                SchemaDifferenceKind.ColumnDefaultMismatch,
                path,
                "default value",
                FormatDefault(modelColumn),
                FormatDefault(databaseColumn),
                modelColumn,
                databaseColumn);
        }

        if (options.Capabilities.CompareComments)
        {
            AddMismatchIfDifferent(
                differences,
                SchemaDifferenceKind.ColumnCommentMismatch,
                path,
                "comment",
                FormatComment(modelColumn.ValueProperty.Attributes),
                FormatComment(databaseColumn.ValueProperty.Attributes),
                modelColumn,
                databaseColumn,
                SchemaDifferenceSeverity.Info,
                SchemaDifferenceSafety.Informational);
        }
    }

    private void CompareIndexes(
        TableDefinition modelTable,
        TableDefinition databaseTable,
        List<SchemaDifference> differences)
    {
        var modelIndexes = GetComparableIndexes(modelTable)
            .ToDictionary(x => x.Key, StringComparer.Ordinal);
        var databaseIndexes = GetComparableIndexes(databaseTable)
            .ToDictionary(x => x.Key, StringComparer.Ordinal);

        foreach (var modelIndex in modelIndexes.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (databaseIndexes.ContainsKey(modelIndex.Key))
                continue;

            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.MissingIndex,
                GetMissingIndexSeverity(modelIndex),
                SchemaDifferenceSafety.Additive,
                $"{modelTable.DbName}.{modelIndex.Name}",
                $"Database table '{databaseTable.DbName}' is missing index '{modelIndex.Name}' on ({modelIndex.Columns}).",
                modelIndex.Index,
                null));
        }

        foreach (var databaseIndex in databaseIndexes.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (modelIndexes.ContainsKey(databaseIndex.Key))
                continue;

            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.ExtraIndex,
                SchemaDifferenceSeverity.Warning,
                SchemaDifferenceSafety.Destructive,
                $"{databaseTable.DbName}.{databaseIndex.Name}",
                $"Database table '{databaseTable.DbName}' has extra index '{databaseIndex.Name}' on ({databaseIndex.Columns}).",
                null,
                databaseIndex.Index));
        }
    }

    private void CompareForeignKeys(
        TableDefinition modelTable,
        TableDefinition databaseTable,
        List<SchemaDifference> differences)
    {
        var modelForeignKeys = GetComparableForeignKeys(modelTable)
            .ToDictionary(x => x.Key, StringComparer.Ordinal);
        var databaseForeignKeys = GetComparableForeignKeys(databaseTable)
            .ToDictionary(x => x.Key, StringComparer.Ordinal);
        var modelForeignKeysByShape = GetComparableForeignKeys(modelTable)
            .ToDictionary(x => x.ShapeKey, StringComparer.Ordinal);
        var databaseForeignKeysByShape = GetComparableForeignKeys(databaseTable)
            .ToDictionary(x => x.ShapeKey, StringComparer.Ordinal);

        foreach (var modelForeignKey in modelForeignKeys.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (databaseForeignKeys.ContainsKey(modelForeignKey.Key))
                continue;

            if (databaseForeignKeysByShape.TryGetValue(modelForeignKey.ShapeKey, out var databaseForeignKey))
            {
                differences.Add(new SchemaDifference(
                    SchemaDifferenceKind.ForeignKeyActionMismatch,
                    SchemaDifferenceSeverity.Error,
                    SchemaDifferenceSafety.Ambiguous,
                    $"{modelForeignKey.ForeignKeyTable}.{modelForeignKey.ConstraintName}",
                    $"Foreign key '{modelForeignKey.ConstraintName}' exists, but referential actions differ. Model: ON UPDATE {FormatReferentialAction(modelForeignKey.OnUpdate)}, ON DELETE {FormatReferentialAction(modelForeignKey.OnDelete)}; database: ON UPDATE {FormatReferentialAction(databaseForeignKey.OnUpdate)}, ON DELETE {FormatReferentialAction(databaseForeignKey.OnDelete)}.",
                    modelTable,
                    databaseTable));
                continue;
            }

            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.MissingForeignKey,
                SchemaDifferenceSeverity.Error,
                SchemaDifferenceSafety.Additive,
                $"{modelForeignKey.ForeignKeyTable}.{modelForeignKey.ConstraintName}",
                $"Database is missing foreign key '{modelForeignKey.ConstraintName}' from '{modelForeignKey.ForeignKeyTable}' ({modelForeignKey.ForeignKeyColumns}) to '{modelForeignKey.CandidateKeyTable}' ({modelForeignKey.CandidateKeyColumns}).",
                modelTable,
                null));
        }

        foreach (var databaseForeignKey in databaseForeignKeys.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (modelForeignKeys.ContainsKey(databaseForeignKey.Key))
                continue;

            if (modelForeignKeysByShape.ContainsKey(databaseForeignKey.ShapeKey))
                continue;

            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.ExtraForeignKey,
                SchemaDifferenceSeverity.Warning,
                SchemaDifferenceSafety.Destructive,
                $"{databaseForeignKey.ForeignKeyTable}.{databaseForeignKey.ConstraintName}",
                $"Database has extra foreign key '{databaseForeignKey.ConstraintName}' from '{databaseForeignKey.ForeignKeyTable}' ({databaseForeignKey.ForeignKeyColumns}) to '{databaseForeignKey.CandidateKeyTable}' ({databaseForeignKey.CandidateKeyColumns}).",
                null,
                databaseTable));
        }
    }

    private void CompareChecks(
        TableDefinition modelTable,
        TableDefinition databaseTable,
        List<SchemaDifference> differences)
    {
        var modelChecks = GetEffectiveChecks(modelTable)
            .ToDictionary(x => x.Key, StringComparer.Ordinal);
        var databaseChecks = GetEffectiveChecks(databaseTable)
            .ToDictionary(x => x.Key, StringComparer.Ordinal);

        foreach (var modelCheck in modelChecks.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (databaseChecks.ContainsKey(modelCheck.Key))
                continue;

            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.MissingCheck,
                SchemaDifferenceSeverity.Error,
                SchemaDifferenceSafety.Additive,
                $"{modelTable.DbName}.{modelCheck.Name}",
                $"Database table '{databaseTable.DbName}' is missing check constraint '{modelCheck.Name}'.",
                modelTable,
                null));
        }

        foreach (var databaseCheck in databaseChecks.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (modelChecks.ContainsKey(databaseCheck.Key))
                continue;

            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.ExtraCheck,
                SchemaDifferenceSeverity.Warning,
                SchemaDifferenceSafety.Destructive,
                $"{databaseTable.DbName}.{databaseCheck.Name}",
                $"Database table '{databaseTable.DbName}' has extra check constraint '{databaseCheck.Name}'.",
                null,
                databaseTable));
        }
    }

    private void CompareTableComment(
        TableDefinition modelTable,
        TableDefinition databaseTable,
        List<SchemaDifference> differences)
    {
        AddMismatchIfDifferent(
            differences,
            SchemaDifferenceKind.TableCommentMismatch,
            modelTable.DbName,
            "comment",
            FormatComment(modelTable.Model.Attributes),
            FormatComment(databaseTable.Model.Attributes),
            modelTable,
            databaseTable,
            SchemaDifferenceSeverity.Info,
            SchemaDifferenceSafety.Informational);
    }

    private void AddMismatchIfDifferent<T>(
        List<SchemaDifference> differences,
        SchemaDifferenceKind kind,
        string path,
        string label,
        T modelValue,
        T databaseValue,
        IDefinition modelDefinition,
        IDefinition databaseDefinition,
        SchemaDifferenceSeverity severity = SchemaDifferenceSeverity.Error,
        SchemaDifferenceSafety safety = SchemaDifferenceSafety.Ambiguous)
    {
        if (EqualityComparer<T>.Default.Equals(modelValue, databaseValue))
            return;

        differences.Add(new SchemaDifference(
            kind,
            severity,
            safety,
            path,
            $"Database {label} differs at '{path}'. Model: '{modelValue}', database: '{databaseValue}'.",
            modelDefinition,
            databaseDefinition));
    }

    private IEnumerable<IndexSignature> GetComparableIndexes(TableDefinition table)
    {
        return table.ColumnIndices
            .Where(x => x.Characteristic is IndexCharacteristic.Simple or IndexCharacteristic.Unique)
            .Select(index => new IndexSignature(
                index,
                index.Name,
                index.Characteristic,
                index.Type,
                string.Join(",", index.Columns.Select(x => x.DbName))));
    }

    private static SchemaDifferenceSeverity GetMissingIndexSeverity(IndexSignature index) =>
        index.Characteristic == IndexCharacteristic.Unique
            ? SchemaDifferenceSeverity.Error
            : SchemaDifferenceSeverity.Warning;

    private static IEnumerable<ForeignKeySignature> GetComparableForeignKeys(TableDefinition table)
    {
        return table.ColumnIndices
            .SelectMany(x => x.RelationParts)
            .Where(x => x.Type == RelationPartType.ForeignKey)
            .Select(x => x.Relation)
            .Distinct()
            .Where(x => x.ForeignKey?.ColumnIndex != null && x.CandidateKey?.ColumnIndex != null)
            .Select(relation => new ForeignKeySignature(
                relation.ConstraintName,
                relation.ForeignKey.ColumnIndex.Table.DbName,
                string.Join(",", relation.ForeignKey.ColumnIndex.Columns.Select(x => x.DbName)),
                relation.CandidateKey.ColumnIndex.Table.DbName,
                string.Join(",", relation.CandidateKey.ColumnIndex.Columns.Select(x => x.DbName)),
                relation.OnUpdate,
                relation.OnDelete));
    }

    private IEnumerable<CheckSignature> GetEffectiveChecks(TableDefinition table)
    {
        var checks = table.Model.Attributes.OfType<CheckAttribute>().ToList();
        var providerChecks = checks
            .Where(x => x.DatabaseType == options.Capabilities.DatabaseType)
            .ToList();
        var effectiveChecks = providerChecks.Count > 0
            ? providerChecks
            : checks
                .Where(x => x.DatabaseType == DatabaseType.Default)
                .ToList();

        return effectiveChecks
            .Select(x => new CheckSignature(x.Name, NormalizeSql(x.Expression)));
    }

    private string? FormatComment(IEnumerable<Attribute> attributes)
    {
        var comments = attributes.OfType<CommentAttribute>().ToList();
        var providerComments = comments
            .Where(x => x.DatabaseType == options.Capabilities.DatabaseType)
            .Select(x => x.Text)
            .ToList();
        var effectiveComments = providerComments.Count > 0
            ? providerComments
            : comments
                .Where(x => x.DatabaseType == DatabaseType.Default)
                .Select(x => x.Text)
                .ToList();

        return effectiveComments.Count == 0
            ? null
            : string.Join("\n", effectiveComments);
    }

    private string? FormatDbType(DatabaseColumnType? type)
    {
        if (type == null)
            return null;

        var name = type.Name.ToLowerInvariant();
        var length = type.Length;
        var signed = type.Signed;

        if (options.Capabilities.DatabaseType is DatabaseType.MySQL or DatabaseType.MariaDB &&
            IsIntegerDatabaseType(name))
        {
            if (name == "integer")
                name = "int";

            // MySQL/MariaDB integer display width is not storage semantics.
            // Information schema also represents ordinary signed integers as
            // Signed == null, while model fallback metadata historically uses
            // Signed == true. Only an explicit unsigned marker is meaningful.
            length = null;
            signed = signed == false ? false : null;
        }

        return string.Join(
            "|",
            type.DatabaseType,
            name,
            length?.ToString(CultureInfo.InvariantCulture) ?? "",
            type.Decimals?.ToString(CultureInfo.InvariantCulture) ?? "",
            signed?.ToString(CultureInfo.InvariantCulture) ?? "");
    }

    private void CompareCanonicalProviderTypeCompatibility(
        ColumnDefinition modelColumn,
        ColumnDefinition databaseColumn,
        DatabaseColumnType? databasePhysicalType,
        List<SchemaDifference> differences)
    {
        if (!modelColumn.HasScalarConverter ||
            databasePhysicalType is null ||
            modelColumn.ProviderClrType is not { } providerClrType ||
            (Nullable.GetUnderlyingType(providerClrType) ?? providerClrType) != typeof(int) ||
            IsCanonicalInt32Compatible(databasePhysicalType))
        {
            return;
        }

        var provider = options.Capabilities.DatabaseType;
        var path = $"{modelColumn.Table.DbName}.{modelColumn.DbName}";
        differences.Add(new SchemaDifference(
            SchemaDifferenceKind.ColumnCanonicalTypeMismatch,
            SchemaDifferenceSeverity.Error,
            SchemaDifferenceSafety.Ambiguous,
            path,
            $"Converter-backed canonical provider type '{typeof(int).FullName}' at '{path}' is incompatible with observed {provider} type '{FormatDbTypeDisplay(databasePhysicalType)}'. Use SQLite INTEGER or a signed MySQL/MariaDB INT column, or change the scalar converter's canonical provider type.",
            modelColumn,
            databaseColumn));
    }

    private bool IsCanonicalInt32Compatible(DatabaseColumnType type)
    {
        var provider = options.Capabilities.DatabaseType;
        if (provider == DatabaseType.SQLite)
        {
            return string.Equals(type.Name, "integer", StringComparison.OrdinalIgnoreCase) &&
                   !type.Decimals.HasValue;
        }

        return provider is DatabaseType.MySQL or DatabaseType.MariaDB &&
               (string.Equals(type.Name, "int", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type.Name, "integer", StringComparison.OrdinalIgnoreCase)) &&
               !type.Decimals.HasValue &&
               type.Signed != false;
    }

    private static bool IsIntegerDatabaseType(string name) =>
        name is "tinyint" or "smallint" or "mediumint" or "int" or "integer" or "bigint";

    private DatabaseColumnType? GetExactDatabaseType(ColumnDefinition column) =>
        column.DbTypes.FirstOrDefault(type =>
            type.DatabaseType == options.Capabilities.DatabaseType);

    private void CompareGuidStorage(
        ColumnDefinition modelColumn,
        ColumnDefinition databaseColumn,
        DatabaseColumnType? modelPhysicalType,
        DatabaseColumnType? databasePhysicalType,
        List<SchemaDifference> differences)
    {
        if (modelPhysicalType is null || databasePhysicalType is null)
            return;

        var provider = options.Capabilities.DatabaseType;
        var path = $"{modelColumn.Table.DbName}.{modelColumn.DbName}";

        if (HasDeferredBareGuidCandidate(modelColumn))
        {
            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved,
                SchemaDifferenceSeverity.Error,
                SchemaDifferenceSafety.Ambiguous,
                path,
                $"Model UUID storage intent is unresolved at '{path}' for {provider}. Deferred source metadata sees a direct 'Guid' property, but cannot prove that an assembly-registered scalar converter does not change its canonical provider type. Use authoritative compiled metadata, or declare provider-scoped GuidStorage only when the canonical type is truly Guid.",
                modelColumn,
                databaseColumn));
            return;
        }

        if (!HasModelGuidStorageIntent(modelColumn))
            return;

        var modelStorage = ResolveModelGuidStorage(modelColumn, modelPhysicalType, provider);
        var databaseStorage = ResolveDatabaseGuidStorage(databaseColumn, databasePhysicalType, provider);

        if (modelStorage.State == GuidStorageResolutionState.Incompatible)
        {
            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.ColumnGuidStorageFormatMismatch,
                SchemaDifferenceSeverity.Error,
                SchemaDifferenceSafety.Ambiguous,
                path,
                $"Model UUID storage format '{modelStorage.Format}' at '{path}' is incompatible with effective {provider} type '{FormatDbTypeDisplay(modelPhysicalType)}'. Align the GuidStorage declaration and physical Type mapping before validating this schema.",
                modelColumn,
                databaseColumn));
            return;
        }

        if (modelStorage.State == GuidStorageResolutionState.Unresolved)
        {
            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved,
                SchemaDifferenceSeverity.Error,
                SchemaDifferenceSafety.Ambiguous,
                path,
                $"Model UUID storage format is unresolved at '{path}' for {provider}. Physical type '{FormatDbTypeDisplay(modelPhysicalType)}' does not establish one safe UUID representation. Declare an explicit provider-scoped GuidStorage format before treating the schema as compatible.",
                modelColumn,
                databaseColumn));
            return;
        }

        if (databaseStorage.State == GuidStorageResolutionState.Incompatible)
        {
            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.ColumnGuidStorageFormatMismatch,
                SchemaDifferenceSeverity.Error,
                SchemaDifferenceSafety.Ambiguous,
                path,
                $"Database UUID storage metadata at '{path}' for {provider} claims format '{databaseStorage.Format}', which is incompatible with physical type '{FormatDbTypeDisplay(databasePhysicalType)}'. Refresh or correct the trusted database storage metadata before generating a migration.",
                modelColumn,
                databaseColumn));
            return;
        }

        if (databaseStorage.State == GuidStorageResolutionState.Unresolved)
        {
            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.ColumnGuidStorageFormatUnresolved,
                SchemaDifferenceSeverity.Error,
                SchemaDifferenceSafety.Ambiguous,
                path,
                $"Database UUID storage format is unresolved at '{path}' for {provider}. Model expects '{modelStorage.Format}', but physical type '{FormatDbTypeDisplay(databasePhysicalType)}' does not encode its UUID representation. Provide a trusted storage hint before treating the schema as compatible.",
                modelColumn,
                databaseColumn));
            return;
        }

        if (modelStorage.Format == databaseStorage.Format)
            return;

        differences.Add(new SchemaDifference(
            SchemaDifferenceKind.ColumnGuidStorageFormatMismatch,
            SchemaDifferenceSeverity.Error,
            SchemaDifferenceSafety.Ambiguous,
            path,
            $"Database UUID storage format differs at '{path}' for {provider}. Model: '{modelStorage.Format}', database: '{databaseStorage.Format}'. The SQL type '{FormatDbTypeDisplay(databasePhysicalType)}' is unchanged, but changing UUID representation requires a manual data migration; DataLinq will not generate an automatic rewrite.",
            modelColumn,
            databaseColumn));
    }

    private static GuidStorageResolution ResolveModelGuidStorage(
        ColumnDefinition column,
        DatabaseColumnType physicalType,
        DatabaseType provider)
    {
        if (column.IsGuidStorageUnresolvedFor(provider) ||
            column.ValueProperty.Attributes
                .OfType<GuidStorageUnresolvedAttribute>()
                .Any(attribute => attribute.DatabaseType == provider))
            return GuidStorageResolution.Unresolved;

        var definition = column.GetGuidStorageFor(provider);
        if (definition is not null)
            return CreateGuidStorageResolution(provider, physicalType, definition.Format, allowSchemaModifiers: false);

        var declarations = column.ValueProperty.Attributes.OfType<GuidStorageAttribute>().ToArray();
        var declaration = declarations.FirstOrDefault(attribute => attribute.DatabaseType == provider) ??
            declarations.FirstOrDefault(attribute => attribute.DatabaseType == DatabaseType.Default);
        if (declaration is not null)
            return CreateGuidStorageResolution(provider, physicalType, declaration.Format, allowSchemaModifiers: false);

        var inferred = GuidStoragePhysicalTypeResolver.InferCompatibilityDefault(
            provider,
            physicalType,
            allowSchemaModifiers: false);
        return inferred.HasValue
            ? GuidStorageResolution.Resolved(inferred.Value)
            : GuidStorageResolution.Unresolved;
    }

    private static bool HasModelGuidStorageIntent(ColumnDefinition column)
    {
        if (column.IsGuidColumn)
            return true;

        if (!IsDeferredDirectGuidSyntax(column) || HasDeferredScalarConverterMarker(column))
            return false;

        return column.ValueProperty.Attributes.Any(static attribute =>
            attribute is GuidStorageAttribute or GuidStorageUnresolvedAttribute);
    }

    private static bool HasDeferredBareGuidCandidate(ColumnDefinition column) =>
        IsDeferredDirectGuidSyntax(column) &&
        !HasDeferredScalarConverterMarker(column) &&
        !column.ValueProperty.Attributes.Any(static attribute =>
            attribute is GuidStorageAttribute or GuidStorageUnresolvedAttribute);

    private static bool IsDeferredDirectGuidSyntax(ColumnDefinition column) =>
        column.ProviderClrType is null &&
        !column.HasScalarConverter &&
        IsGuidSyntaxName(column.ProviderCsType.Name);

    private static bool IsGuidSyntaxName(string typeName)
    {
        const string globalAlias = "global::";
        var normalizedName = typeName.StartsWith(globalAlias, StringComparison.Ordinal)
            ? typeName.Substring(globalAlias.Length)
            : typeName;

        return string.Equals(normalizedName, nameof(Guid), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedName, typeof(Guid).FullName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasDeferredScalarConverterMarker(ColumnDefinition column) =>
        column.ValueProperty.Attributes.Any(static attribute =>
            attribute is ScalarConverterSourceAttribute);

    private static GuidStorageResolution ResolveDatabaseGuidStorage(
        ColumnDefinition column,
        DatabaseColumnType physicalType,
        DatabaseType provider)
    {
        if (column.IsGuidStorageUnresolvedFor(provider))
            return GuidStorageResolution.Unresolved;

        var definition = column.GetGuidStorageFor(provider);
        if (definition is not null)
        {
            var observableFormat = GuidStoragePhysicalTypeResolver.InferSchemaObservableFormat(
                provider,
                physicalType,
                allowSchemaModifiers: true);
            var declaredResolution = CreateGuidStorageResolution(
                provider,
                physicalType,
                definition.Format,
                allowSchemaModifiers: true);
            if (declaredResolution.State == GuidStorageResolutionState.Incompatible)
                return declaredResolution;

            if (definition.IsExplicit || definition.Format == observableFormat)
                return declaredResolution;

            return GuidStorageResolution.Unresolved;
        }

        var inferred = GuidStoragePhysicalTypeResolver.InferSchemaObservableFormat(
            provider,
            physicalType,
            allowSchemaModifiers: true);
        return inferred.HasValue
            ? GuidStorageResolution.Resolved(inferred.Value)
            : GuidStorageResolution.Unresolved;
    }

    private static GuidStorageResolution CreateGuidStorageResolution(
        DatabaseType provider,
        DatabaseColumnType physicalType,
        GuidStorageFormat format,
        bool allowSchemaModifiers) =>
        GuidStoragePhysicalTypeResolver.IsCompatible(
            provider,
            physicalType,
            format,
            allowSchemaModifiers)
            ? GuidStorageResolution.Resolved(format)
            : GuidStorageResolution.Incompatible(format);

    private static string FormatDbTypeDisplay(DatabaseColumnType type)
    {
        var parameters = new List<string>(2);
        if (type.Length.HasValue)
            parameters.Add(type.Length.Value.ToString(CultureInfo.InvariantCulture));
        if (type.Decimals.HasValue)
            parameters.Add(type.Decimals.Value.ToString(CultureInfo.InvariantCulture));

        var formatted = parameters.Count == 0
            ? type.Name
            : $"{type.Name}({string.Join(",", parameters)})";
        return type.Signed == false ? $"{formatted} unsigned" : formatted;
    }

    private string? FormatDefault(ColumnDefinition column)
    {
        var attribute = column.ValueProperty.GetDefaultAttribute();
        if (attribute == null)
            return null;

        if (attribute is DefaultSqlAttribute defaultSql)
        {
            if (defaultSql.DatabaseType != DatabaseType.Default &&
                defaultSql.DatabaseType != options.Capabilities.DatabaseType)
                return null;

            return $"{attribute.GetType().Name}|{defaultSql.DatabaseType}|{NormalizeSql(defaultSql.Expression)}";
        }

        if (TryGetEnumDefaultNumericValue(column.ValueProperty, attribute, out var enumNumericValue))
            return $"{attribute.GetType().Name}|{enumNumericValue.ToString(CultureInfo.InvariantCulture)}";

        if (attribute is DefaultNewUUIDAttribute defaultNewUuid)
            return $"{attribute.GetType().Name}|{defaultNewUuid.NewUUID}|{defaultNewUuid.Version}";

        var value = attribute.Value switch
        {
            null => "",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => attribute.Value.ToString()
        };

        var attributeTypeName = attribute is DefaultGuidAttribute
            ? nameof(DefaultAttribute)
            : attribute.GetType().Name;

        return $"{attributeTypeName}|{value}";
    }

    private static bool TryGetEnumDefaultNumericValue(
        ValueProperty property,
        DefaultAttribute attribute,
        out int numericValue)
    {
        if (!property.EnumProperty.HasValue)
        {
            numericValue = default;
            return false;
        }

        var enumProperty = property.EnumProperty.Value;
        if (attribute.Value is Enum enumValue)
        {
            numericValue = Convert.ToInt32(enumValue, CultureInfo.InvariantCulture);
            return true;
        }

        if (attribute.Value is string stringValue)
        {
            if (int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out numericValue))
                return true;

            var memberName = stringValue.Split('.').Last();
            var enumMatch = enumProperty.CsValuesOrDbValues.FirstOrDefault(x => x.name == memberName);
            if (enumMatch.name == null)
                enumMatch = enumProperty.DbValuesOrCsValues.FirstOrDefault(x => x.name == stringValue);

            if (enumMatch.name != null)
            {
                numericValue = enumMatch.value;
                return true;
            }
        }

        try
        {
            numericValue = Convert.ToInt32(attribute.Value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            numericValue = default;
            return false;
        }
    }

    private static string NormalizeSql(string sql) =>
        string.Join(" ", sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<TableModel> GetComparableTables(DatabaseDefinition database)
    {
        return database.TableModels
            .Where(x => !x.IsStub && x.Table.Type is TableType.Table or TableType.View);
    }

    private static Dictionary<string, TableModel> BuildTableLookup(
        IEnumerable<TableModel> tables,
        StringComparer comparer)
    {
        var lookup = new Dictionary<string, TableModel>(comparer);
        foreach (var table in tables)
        {
            if (!lookup.ContainsKey(table.Table.DbName))
                lookup.Add(table.Table.DbName, table);
        }

        return lookup;
    }

    private static StringComparison ToStringComparison(SchemaIdentifierComparison comparison) =>
        comparison == SchemaIdentifierComparison.OrdinalIgnoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string FormatObjectKind(TableDefinition table) =>
        table.Type == TableType.View ? "view" : "table";

    private static string FormatReferentialAction(ReferentialAction action) =>
        action == ReferentialAction.Unspecified
            ? "not specified"
            : action.ToString();

    private enum GuidStorageResolutionState
    {
        Resolved,
        Unresolved,
        Incompatible
    }

    private readonly record struct GuidStorageResolution(
        GuidStorageResolutionState State,
        GuidStorageFormat? Format)
    {
        public static GuidStorageResolution Unresolved =>
            new(GuidStorageResolutionState.Unresolved, null);

        public static GuidStorageResolution Resolved(GuidStorageFormat format) =>
            new(GuidStorageResolutionState.Resolved, format);

        public static GuidStorageResolution Incompatible(GuidStorageFormat format) =>
            new(GuidStorageResolutionState.Incompatible, format);
    }

    private sealed class IndexSignature
    {
        public IndexSignature(ColumnIndex index, string name, IndexCharacteristic characteristic, IndexType type, string columns)
        {
            Index = index;
            Name = name;
            Characteristic = characteristic;
            Type = type;
            Columns = columns;
        }

        public ColumnIndex Index { get; }
        public string Name { get; }
        public IndexCharacteristic Characteristic { get; }
        public IndexType Type { get; }
        public string Columns { get; }
        public string Key => $"{Name}|{Characteristic}|{Type}|{Columns}";
    }

    private sealed class ForeignKeySignature
    {
        public ForeignKeySignature(
            string constraintName,
            string foreignKeyTable,
            string foreignKeyColumns,
            string candidateKeyTable,
            string candidateKeyColumns,
            ReferentialAction onUpdate,
            ReferentialAction onDelete)
        {
            ConstraintName = constraintName;
            ForeignKeyTable = foreignKeyTable;
            ForeignKeyColumns = foreignKeyColumns;
            CandidateKeyTable = candidateKeyTable;
            CandidateKeyColumns = candidateKeyColumns;
            OnUpdate = onUpdate;
            OnDelete = onDelete;
        }

        public string ConstraintName { get; }
        public string ForeignKeyTable { get; }
        public string ForeignKeyColumns { get; }
        public string CandidateKeyTable { get; }
        public string CandidateKeyColumns { get; }
        public ReferentialAction OnUpdate { get; }
        public ReferentialAction OnDelete { get; }
        public string ShapeKey => $"{ConstraintName}|{ForeignKeyTable}|{ForeignKeyColumns}|{CandidateKeyTable}|{CandidateKeyColumns}";
        public string Key => $"{ConstraintName}|{ForeignKeyTable}|{ForeignKeyColumns}|{CandidateKeyTable}|{CandidateKeyColumns}|{OnUpdate}|{OnDelete}";
    }

    private sealed class CheckSignature
    {
        public CheckSignature(string name, string expression)
        {
            Name = name;
            Expression = expression;
        }

        public string Name { get; }
        public string Expression { get; }
        public string Key => $"{Name}|{Expression}";
    }
}
