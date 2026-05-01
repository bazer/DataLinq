using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataLinq.Attributes;
using DataLinq.Metadata;

namespace DataLinq.Tools;

public sealed record SchemaMigrationSnapshot(
    int FormatVersion,
    string DatabaseName,
    string DatabaseType,
    string ModelType,
    string? ModelNamespace,
    string GeneratedAtUtc,
    IReadOnlyList<SchemaMigrationSnapshotTable> Tables)
{
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static SchemaMigrationSnapshot FromDatabase(
        DatabaseDefinition database,
        DatabaseType databaseType,
        DateTimeOffset? generatedAtUtc = null)
    {
        return new SchemaMigrationSnapshot(
            CurrentFormatVersion,
            database.DbName,
            databaseType.ToString(),
            database.CsType.Name,
            database.CsType.Namespace,
            (generatedAtUtc ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            database.TableModels
                .Where(x => !x.IsStub && x.Table.Type == TableType.Table)
                .OrderBy(x => x.Table.DbName, StringComparer.Ordinal)
                .Select(x => SchemaMigrationSnapshotTable.FromTable(x.Table, databaseType))
                .ToArray());
    }

    public static SchemaMigrationSnapshot FromJson(string json) =>
        JsonSerializer.Deserialize<SchemaMigrationSnapshot>(json, JsonOptions)
            ?? throw new InvalidOperationException("Could not deserialize schema migration snapshot.");

    public string ToJson() =>
        JsonSerializer.Serialize(this, JsonOptions) + Environment.NewLine;
}

public sealed record SchemaMigrationSnapshotTable(
    string Name,
    IReadOnlyList<SchemaMigrationSnapshotColumn> Columns,
    IReadOnlyList<SchemaMigrationSnapshotIndex> Indexes,
    IReadOnlyList<SchemaMigrationSnapshotForeignKey> ForeignKeys,
    IReadOnlyList<SchemaMigrationSnapshotCheck> Checks,
    string? Comment)
{
    public static SchemaMigrationSnapshotTable FromTable(TableDefinition table, DatabaseType databaseType)
    {
        return new SchemaMigrationSnapshotTable(
            table.DbName,
            table.Columns
                .OrderBy(x => x.Index)
                .ThenBy(x => x.DbName, StringComparer.Ordinal)
                .Select(x => SchemaMigrationSnapshotColumn.FromColumn(x, databaseType))
                .ToArray(),
            table.ColumnIndices
                .Where(x => x.Characteristic is IndexCharacteristic.Simple or IndexCharacteristic.Unique)
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(SchemaMigrationSnapshotIndex.FromIndex)
                .ToArray(),
            table.ColumnIndices
                .SelectMany(x => x.RelationParts)
                .Where(x => x.Type == RelationPartType.ForeignKey)
                .Select(x => x.Relation)
                .Distinct()
                .Where(x => x.ForeignKey?.ColumnIndex != null && x.CandidateKey?.ColumnIndex != null)
                .OrderBy(x => x.ConstraintName, StringComparer.Ordinal)
                .Select(SchemaMigrationSnapshotForeignKey.FromRelation)
                .ToArray(),
            GetEffectiveChecks(table, databaseType)
                .OrderBy(x => x.Name, StringComparer.Ordinal)
                .Select(x => new SchemaMigrationSnapshotCheck(x.Name, x.Expression))
                .ToArray(),
            GetEffectiveComment(table.Model.Attributes, databaseType));
    }

    private static IEnumerable<CheckAttribute> GetEffectiveChecks(TableDefinition table, DatabaseType databaseType)
    {
        var checks = table.Model.Attributes.OfType<CheckAttribute>().ToList();
        var typed = checks.Where(x => x.DatabaseType == databaseType).ToList();

        return typed.Count > 0
            ? typed
            : checks.Where(x => x.DatabaseType == DatabaseType.Default);
    }

    private static string? GetEffectiveComment(IEnumerable<Attribute> attributes, DatabaseType databaseType)
    {
        var comments = attributes.OfType<CommentAttribute>().ToList();

        return comments.FirstOrDefault(x => x.DatabaseType == databaseType)?.Text
            ?? comments.FirstOrDefault(x => x.DatabaseType == DatabaseType.Default)?.Text;
    }
}

public sealed record SchemaMigrationSnapshotColumn(
    string Name,
    string CsType,
    SchemaMigrationSnapshotColumnType? DbType,
    bool Nullable,
    bool PrimaryKey,
    bool AutoIncrement,
    string? Default,
    string? Comment)
{
    public static SchemaMigrationSnapshotColumn FromColumn(ColumnDefinition column, DatabaseType databaseType)
    {
        return new SchemaMigrationSnapshotColumn(
            column.DbName,
            column.ValueProperty.CsType.Name,
            SchemaMigrationSnapshotColumnType.FromColumnType(column.GetDbTypeFor(databaseType)),
            column.Nullable,
            column.PrimaryKey,
            column.AutoIncrement,
            FormatDefault(column.ValueProperty.GetDefaultAttribute()),
            GetEffectiveComment(column.ValueProperty.Attributes, databaseType));
    }

    private static string? FormatDefault(DefaultAttribute? attribute)
    {
        if (attribute == null)
            return null;

        var value = attribute.Value switch
        {
            null => "",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => attribute.Value.ToString()
        };

        return $"{attribute.GetType().Name}|{value}";
    }

    private static string? GetEffectiveComment(IEnumerable<Attribute> attributes, DatabaseType databaseType)
    {
        var comments = attributes.OfType<CommentAttribute>().ToList();

        return comments.FirstOrDefault(x => x.DatabaseType == databaseType)?.Text
            ?? comments.FirstOrDefault(x => x.DatabaseType == DatabaseType.Default)?.Text;
    }
}

public sealed record SchemaMigrationSnapshotColumnType(
    string DatabaseType,
    string Name,
    ulong? Length,
    uint? Decimals,
    bool? Signed)
{
    public static SchemaMigrationSnapshotColumnType? FromColumnType(DatabaseColumnType? columnType)
    {
        return columnType == null
            ? null
            : new SchemaMigrationSnapshotColumnType(
                columnType.DatabaseType.ToString(),
                columnType.Name,
                columnType.Length,
                columnType.Decimals,
                columnType.Signed);
    }
}

public sealed record SchemaMigrationSnapshotIndex(
    string Name,
    string Characteristic,
    string Type,
    IReadOnlyList<string> Columns)
{
    public static SchemaMigrationSnapshotIndex FromIndex(ColumnIndex index)
    {
        return new SchemaMigrationSnapshotIndex(
            index.Name,
            index.Characteristic.ToString(),
            index.Type.ToString(),
            index.Columns.Select(x => x.DbName).ToArray());
    }
}

public sealed record SchemaMigrationSnapshotForeignKey(
    string Name,
    string Table,
    IReadOnlyList<string> Columns,
    string PrincipalTable,
    IReadOnlyList<string> PrincipalColumns)
{
    public static SchemaMigrationSnapshotForeignKey FromRelation(RelationDefinition relation)
    {
        return new SchemaMigrationSnapshotForeignKey(
            relation.ConstraintName,
            relation.ForeignKey.ColumnIndex.Table.DbName,
            relation.ForeignKey.ColumnIndex.Columns.Select(x => x.DbName).ToArray(),
            relation.CandidateKey.ColumnIndex.Table.DbName,
            relation.CandidateKey.ColumnIndex.Columns.Select(x => x.DbName).ToArray());
    }
}

public sealed record SchemaMigrationSnapshotCheck(string Name, string Expression);
