using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataLinq.Attributes;
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

        foreach (var modelTable in modelTables)
        {
            var databaseTable = databaseTables
                .FirstOrDefault(x => tableComparer.Equals(x.Table.DbName, modelTable.Table.DbName));

            if (databaseTable == null)
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
            var modelTable = modelTables
                .FirstOrDefault(x => tableComparer.Equals(x.Table.DbName, databaseTable.Table.DbName));

            if (modelTable != null)
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
        var columnComparer = options.Capabilities.ColumnNameComparer;
        var modelColumns = modelTable.Columns
            .OrderBy(x => x.DbName, StringComparer.Ordinal)
            .ToList();
        var databaseColumns = databaseTable.Columns
            .OrderBy(x => x.DbName, StringComparer.Ordinal)
            .ToList();

        foreach (var modelColumn in modelColumns)
        {
            var databaseColumn = databaseColumns
                .FirstOrDefault(x => columnComparer.Equals(x.DbName, modelColumn.DbName));

            if (databaseColumn != null)
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
            var modelColumn = modelColumns
                .FirstOrDefault(x => columnComparer.Equals(x.DbName, databaseColumn.DbName));

            if (modelColumn != null)
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
            AddMismatchIfDifferent(
                differences,
                SchemaDifferenceKind.ColumnTypeMismatch,
                path,
                "type",
                FormatDbType(modelColumn.GetDbTypeFor(options.Capabilities.DatabaseType)),
                FormatDbType(databaseColumn.GetDbTypeFor(options.Capabilities.DatabaseType)),
                modelColumn,
                databaseColumn);
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
                FormatDefault(modelColumn.ValueProperty.GetDefaultAttribute()),
                FormatDefault(databaseColumn.ValueProperty.GetDefaultAttribute()),
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

        foreach (var modelForeignKey in modelForeignKeys.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (databaseForeignKeys.ContainsKey(modelForeignKey.Key))
                continue;

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

        return string.Join(
            "|",
            type.DatabaseType,
            type.Name.ToLowerInvariant(),
            type.Length?.ToString(CultureInfo.InvariantCulture) ?? "",
            type.Decimals?.ToString(CultureInfo.InvariantCulture) ?? "",
            type.Signed?.ToString(CultureInfo.InvariantCulture) ?? "");
    }

    private string? FormatDefault(DefaultAttribute? attribute)
    {
        if (attribute == null)
            return null;

        if (attribute is DefaultSqlAttribute defaultSql)
        {
            if (defaultSql.DatabaseType != DatabaseType.Default &&
                defaultSql.DatabaseType != options.Capabilities.DatabaseType)
                return null;

            return $"{attribute.GetType().Name}|{defaultSql.DatabaseType}|{NormalizeSql(defaultSql.Expression)}";
        }

        var value = attribute.Value switch
        {
            null => "",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => attribute.Value.ToString()
        };

        return $"{attribute.GetType().Name}|{value}";
    }

    private static string NormalizeSql(string sql) =>
        string.Join(" ", sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<TableModel> GetComparableTables(DatabaseDefinition database)
    {
        return database.TableModels
            .Where(x => !x.IsStub && x.Table.Type is TableType.Table or TableType.View);
    }

    private static string FormatObjectKind(TableDefinition table) =>
        table.Type == TableType.View ? "view" : "table";

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
