using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;

namespace DataLinq.Validation;

internal enum SchemaReadAspect { Unknown, Column, ColumnDefault, Index, ForeignKey, ColumnNullability }
internal enum SchemaReadLimitationReason
{
    GeneratedColumn, HiddenColumn, PartialIndex, ExpressionIndex, DescendingIndex,
    PrefixLengthIndex, InvisibleIndex, IgnoredIndex, UnsupportedDefault, BlobDefault,
    ZeroDateDefault, UnrepresentedIndexColumn, UnrepresentedForeignKeySource,
    UnrepresentedForeignKeyTarget, ExternalForeignKeyTarget, UnreportedViewNullability
}

internal sealed class SchemaReadLimitation
{
    internal SchemaReadLimitation(string? table, string? name, SchemaReadAspect aspect,
        SchemaReadLimitationReason reason, string message,
        IReadOnlyList<string>? foreignColumns = null, string? targetTable = null, IReadOnlyList<string>? targetColumns = null,
        string? comparisonName = null, ReferentialAction? onUpdate = null, ReferentialAction? onDelete = null)
    {
        Table = table;
        Name = name;
        ComparisonName = comparisonName ?? name;
        Aspect = aspect;
        Reason = reason;
        ForeignColumns = Array.AsReadOnly(foreignColumns?.ToArray() ?? []);
        TargetTable = targetTable;
        TargetColumns = Array.AsReadOnly(targetColumns?.ToArray() ?? []);
        OnUpdate = onUpdate;
        OnDelete = onDelete;
        Diagnostic = new(DataLinqDiagnosticSeverity.Warning, DLFailureType.NotImplemented, message,
            objectPath: table is null ? null : name is null ? table : $"{table}.{name}",
            contextMessages: Array.AsReadOnly(Array.Empty<string>()));
    }

    internal string? Table { get; }
    internal string? Name { get; }
    internal string? ComparisonName { get; }
    internal SchemaReadAspect Aspect { get; }
    internal SchemaReadLimitationReason Reason { get; }
    internal IReadOnlyList<string> ForeignColumns { get; }
    internal string? TargetTable { get; }
    internal IReadOnlyList<string> TargetColumns { get; }
    internal ReferentialAction? OnUpdate { get; }
    internal ReferentialAction? OnDelete { get; }
    internal DataLinqDiagnosticIssue Diagnostic { get; }
}

// Owned by one captured runtime read. The completed snapshot is immutable and
// can be published only after the metadata coordinator has settled successfully.
internal sealed class SchemaReadLimitations
{
    private readonly List<SchemaReadLimitation> items = [];

    internal void Add(SchemaReadLimitation issue) => items.Add(issue);

    internal SchemaComparisonEvidence Capture(SchemaIdentifierRules identifiers, IEnumerable<string> comparedTables)
    {
        var tables = new HashSet<string>(comparedTables, identifiers.TableNames);
        var ordered = items.Where(issue => issue.Table is null || tables.Contains(issue.Table))
            .OrderBy(issue => issue.Table, StringComparer.Ordinal)
            .ThenBy(issue => issue.Aspect)
            .ThenBy(issue => issue.Name, StringComparer.Ordinal)
            .ThenBy(issue => issue.Reason)
            .ThenBy(issue => issue.Diagnostic.Message, StringComparer.Ordinal);
        var snapshot = new List<SchemaReadLimitation>();
        foreach (var issue in ordered)
            if (!snapshot.Any(previous => Equivalent(previous, issue, identifiers))) snapshot.Add(issue);
        return new(identifiers, snapshot);
    }

    private static bool Equivalent(SchemaReadLimitation left, SchemaReadLimitation right, SchemaIdentifierRules identifiers) =>
        left.Aspect == right.Aspect && left.Reason == right.Reason && identifiers.TableNames.Equals(left.Table, right.Table) &&
        left.OnUpdate == right.OnUpdate && left.OnDelete == right.OnDelete &&
        ObjectNames(left.Aspect, identifiers).Equals(left.Name, right.Name) &&
        ObjectNames(left.Aspect, identifiers).Equals(left.ComparisonName, right.ComparisonName) &&
        StringComparer.Ordinal.Equals(left.Diagnostic.Message, right.Diagnostic.Message) &&
        identifiers.TableNames.Equals(left.TargetTable, right.TargetTable) &&
        left.ForeignColumns.SequenceEqual(right.ForeignColumns, identifiers.ColumnNames) &&
        left.TargetColumns.SequenceEqual(right.TargetColumns, identifiers.ColumnNames);

    private static StringComparer ObjectNames(SchemaReadAspect aspect, SchemaIdentifierRules identifiers) => aspect switch
    {
        SchemaReadAspect.Column or SchemaReadAspect.ColumnDefault or SchemaReadAspect.ColumnNullability => identifiers.ColumnNames,
        SchemaReadAspect.Index => identifiers.IndexNames,
        SchemaReadAspect.ForeignKey => identifiers.ConstraintNames,
        _ => StringComparer.Ordinal
    };
}

internal sealed class SchemaComparisonEvidence : ISchemaComparisonEvidence
{
    private readonly SchemaIdentifierRules identifiers;
    private readonly IReadOnlyList<SchemaReadLimitation> limitations;

    internal SchemaComparisonEvidence(SchemaIdentifierRules identifiers, IEnumerable<SchemaReadLimitation> limitations)
    {
        this.identifiers = identifiers;
        this.limitations = Array.AsReadOnly(limitations.ToArray());
        Issues = Array.AsReadOnly(this.limitations.Select(issue => issue.Diagnostic).ToArray());
    }

    internal IReadOnlyList<DataLinqDiagnosticIssue> Issues { get; }

    public bool IsColumnOmitted(string table, string column) => Contains(SchemaReadAspect.Column, table, column, identifiers.ColumnNames);
    public bool IsDefaultOmitted(string table, string column) => Contains(SchemaReadAspect.ColumnDefault, table, column, identifiers.ColumnNames);
    public bool IsNullabilityOmitted(string table, string column) => Contains(SchemaReadAspect.ColumnNullability, table, column, identifiers.ColumnNames);
    public bool IsIndexOmitted(string table, string index) => Contains(SchemaReadAspect.Index, table, index, identifiers.IndexNames);

    private bool Contains(SchemaReadAspect aspect, string table, string name, StringComparer names) => limitations.Any(issue =>
        issue.Aspect == aspect && issue.Table is not null && issue.ComparisonName is not null &&
        identifiers.TableNames.Equals(issue.Table, table) && names.Equals(issue.ComparisonName, name));

    internal bool IsForeignKeyOmitted(RelationDefinition relation) => GetForeignKeyOmissions(relation).Count != 0;

    public IReadOnlyList<SchemaForeignKeyOmission> GetForeignKeyOmissions(RelationDefinition relation)
    {
        var source = relation.ForeignKey.ColumnIndex;
        var target = relation.CandidateKey.ColumnIndex;
        return limitations.Where(issue => issue.Aspect == SchemaReadAspect.ForeignKey && issue.Table is not null && issue.Name is not null &&
            issue.Reason != SchemaReadLimitationReason.ExternalForeignKeyTarget &&
            identifiers.TableNames.Equals(issue.Table, source.Table.DbName) &&
            // Even an omitted object can prove that the model points at a
            // different endpoint. Never hide that established disagreement.
            (issue.ForeignColumns.Count == 0 || issue.ForeignColumns.SequenceEqual(source.Columns.Select(column => column.DbName), identifiers.ColumnNames)) &&
            (issue.TargetTable is null || identifiers.TableNames.Equals(issue.TargetTable, target.Table.DbName)) &&
            (issue.TargetColumns.Count == 0 || issue.TargetColumns.SequenceEqual(target.Columns.Select(column => column.DbName), identifiers.ColumnNames)) &&
            (identifiers.DatabaseType != DatabaseType.SQLite
                ? issue.Name is not null && identifiers.ConstraintNames.Equals(issue.Name, relation.ConstraintName)
                : issue.ForeignColumns.Count != 0 && issue.TargetTable is not null && issue.TargetColumns.Count != 0))
            // One omitted constraint can produce multiple diagnostics, but
            // remains evidence for only one unrepresented relationship.
            .GroupBy(issue => issue.Name!, identifiers.ConstraintNames)
            .Select(group => new SchemaForeignKeyOmission(group.Key, group.First().OnUpdate, group.First().OnDelete)).ToArray();
    }
}
