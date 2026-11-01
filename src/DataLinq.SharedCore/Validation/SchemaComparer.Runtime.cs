using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.Metadata;

namespace DataLinq.Validation;

public sealed partial class SchemaComparer
{
    private readonly SchemaIdentifierRules? identifiers;
    private readonly ISchemaComparisonEvidence? evidence;

    internal SchemaComparer(SchemaIdentifierRules identifiers, ISchemaComparisonEvidence? evidence = null)
        : this(SchemaComparisonOptions.For(identifiers.DatabaseType))
    {
        this.identifiers = identifiers;
        this.evidence = evidence;
    }

    internal IReadOnlyList<SchemaDifference> CompareRuntime(IReadOnlyList<TableModel> model, IReadOnlyList<TableModel> database)
    {
        if (identifiers is null) throw new InvalidOperationException("Runtime comparison requires observed identifier rules.");
        ValidateRuntimeTables(model, modelInput: true);
        ValidateRuntimeTables(database, modelInput: false);
        var differences = new List<SchemaDifference>();
        CompareTables(model, database, differences);
        return differences.AsReadOnly();
    }

    private void ValidateRuntimeTables(IEnumerable<TableModel> tables, bool modelInput)
    {
        SchemaIdentifierRules.RequireUnambiguous(tables.Select(table => table.Table.DbName), identifiers!.TableNames, "table/view", modelInput);
        foreach (var table in tables.Select(table => table.Table))
        {
            SchemaIdentifierRules.RequireUnambiguous(table.Columns.Select(column => column.DbName), identifiers.ColumnNames, $"column in '{table.DbName}'", modelInput);
            SchemaIdentifierRules.RequireUnambiguous(GetComparableIndexes(table).Select(index => index.Name), identifiers.IndexNames, $"index in '{table.DbName}'", modelInput);
            SchemaIdentifierRules.RequireUnambiguous(GetComparableForeignKeys(table).Select(key => key.ConstraintName), identifiers.ConstraintNames, $"foreign key in '{table.DbName}'", modelInput);
            SchemaIdentifierRules.RequireUnambiguous(GetEffectiveChecks(table).Select(check => check.Name), identifiers.ConstraintNames, $"check in '{table.DbName}'", modelInput);
        }
    }

    private ComparisonKey IndexComparisonKey(IndexSignature index)
    {
        if (identifiers is null) return ComparisonKey.Ordinal(index.Key);
        var parts = new List<(StringComparer, string)>
        {
            (identifiers.IndexNames, index.Name),
            (StringComparer.Ordinal, index.Characteristic.ToString()),
            (StringComparer.Ordinal, index.Type.ToString())
        };
        parts.AddRange(index.Index.Columns.Select(column => (identifiers.ColumnNames, column.DbName)));
        return new(parts);
    }

    private ComparisonKey ForeignKeyComparisonKey(ForeignKeySignature key, bool shapeOnly)
    {
        if (identifiers is null) return ComparisonKey.Ordinal(shapeOnly ? key.ShapeKey : key.Key);
        var foreign = key.Relation.ForeignKey.ColumnIndex;
        var candidate = key.Relation.CandidateKey.ColumnIndex;
        var parts = new List<(StringComparer, string)>
        {
            (identifiers.TableNames, key.ForeignKeyTable),
            (identifiers.TableNames, key.CandidateKeyTable),
            // Preserve sequence boundaries even when identifiers contain commas or pipes.
            (StringComparer.Ordinal, foreign.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        // SQLite's PRAGMA reports a catalog ordinal, not the SQL constraint name.
        // Match relationships by endpoints/actions while preserving multiplicity.
        if (identifiers.DatabaseType != DatabaseType.SQLite)
            parts.Insert(0, (identifiers.ConstraintNames, key.ConstraintName));
        parts.AddRange(foreign.Columns.Select(column => (identifiers.ColumnNames, column.DbName)));
        parts.AddRange(candidate.Columns.Select(column => (identifiers.ColumnNames, column.DbName)));
        if (!shapeOnly)
        {
            parts.Add((StringComparer.Ordinal, RuntimeAction(key.OnUpdate).ToString()));
            parts.Add((StringComparer.Ordinal, RuntimeAction(key.OnDelete).ToString()));
        }
        return new(parts);
    }

    // Provider GetCreateSql uses restrict=true for omitted DataLinq actions.
    // Explicit NO ACTION remains distinct; this does not assume a storage engine.
    private static ReferentialAction RuntimeAction(ReferentialAction action) =>
        action == ReferentialAction.Unspecified ? ReferentialAction.Restrict : action;

    private void CompareRuntimeForeignKeys(TableDefinition modelTable, TableDefinition databaseTable, List<SchemaDifference> differences)
    {
        var model = GetComparableForeignKeys(modelTable).OrderBy(key => key.Key, StringComparer.Ordinal).ToList();
        var database = GetComparableForeignKeys(databaseTable).OrderBy(key => key.Key, StringComparer.Ordinal).ToList();
        // Consume all exact matches before pairing changed actions. Otherwise a
        // duplicate shape could steal an exact match from a later declaration.
        var unmatched = new List<ForeignKeySignature>();
        foreach (var key in model)
        {
            var match = database.FindIndex(candidate => ForeignKeyComparisonKey(key, false).Equals(ForeignKeyComparisonKey(candidate, false)));
            if (match >= 0) database.RemoveAt(match);
            else unmatched.Add(key);
        }
        var unrepresented = new List<ForeignKeySignature>();
        foreach (var key in unmatched)
        {
            var match = database.FindIndex(candidate => ForeignKeyComparisonKey(key, true).Equals(ForeignKeyComparisonKey(candidate, true)));
            if (match >= 0)
            {
                var live = database[match];
                database.RemoveAt(match);
                differences.Add(new(SchemaDifferenceKind.ForeignKeyActionMismatch, SchemaDifferenceSeverity.Error, SchemaDifferenceSafety.Ambiguous,
                    $"{key.ForeignKeyTable}.{key.ConstraintName}",
                    $"Foreign key '{key.ConstraintName}' exists, but referential actions differ. Model: ON UPDATE {FormatReferentialAction(key.OnUpdate)}, ON DELETE {FormatReferentialAction(key.OnDelete)}; database: ON UPDATE {FormatReferentialAction(live.OnUpdate)}, ON DELETE {FormatReferentialAction(live.OnDelete)}.", modelTable, databaseTable));
            }
            else
            {
                unrepresented.Add(key);
            }
        }
        var omissionsUsed = new HashSet<string>(identifiers!.ConstraintNames);
        // Consume compatible observed actions before pairing disagreements, and
        // consume each observed constraint only once across duplicate shapes.
        // Prefer specific evidence over unknown actions for every declaration.
        for (var known = 2; known >= 0; known--)
            foreach (var key in unrepresented.ToArray())
            {
                var omitted = evidence?.GetForeignKeyOmissions(key.Relation).FirstOrDefault(candidate =>
                    !omissionsUsed.Contains(candidate.Identity) &&
                    (candidate.OnUpdate.HasValue ? 1 : 0) + (candidate.OnDelete.HasValue ? 1 : 0) == known &&
                    (!candidate.OnUpdate.HasValue || RuntimeAction(key.OnUpdate) == candidate.OnUpdate) &&
                    (!candidate.OnDelete.HasValue || RuntimeAction(key.OnDelete) == candidate.OnDelete));
                if (omitted is not null)
                {
                    omissionsUsed.Add(omitted.Identity);
                    unrepresented.Remove(key);
                }
            }
        foreach (var key in unrepresented)
        {
            var omitted = evidence?.GetForeignKeyOmissions(key.Relation).FirstOrDefault(candidate => !omissionsUsed.Contains(candidate.Identity));
            if (omitted is not null)
            {
                omissionsUsed.Add(omitted.Identity);
                differences.Add(new(SchemaDifferenceKind.ForeignKeyActionMismatch, SchemaDifferenceSeverity.Error, SchemaDifferenceSafety.Ambiguous,
                    $"{key.ForeignKeyTable}.{key.ConstraintName}",
                    $"Foreign key '{key.ConstraintName}' has observed referential actions that differ despite an unrepresented endpoint. Model: ON UPDATE {FormatReferentialAction(key.OnUpdate)}, ON DELETE {FormatReferentialAction(key.OnDelete)}; database: ON UPDATE {(omitted.OnUpdate is { } update ? FormatReferentialAction(update) : "unknown")}, ON DELETE {(omitted.OnDelete is { } delete ? FormatReferentialAction(delete) : "unknown")}.", modelTable, databaseTable));
            }
            else
                differences.Add(new(SchemaDifferenceKind.MissingForeignKey, SchemaDifferenceSeverity.Error, SchemaDifferenceSafety.Additive,
                    $"{key.ForeignKeyTable}.{key.ConstraintName}",
                    $"Database is missing foreign key '{key.ConstraintName}' from '{key.ForeignKeyTable}' ({key.ForeignKeyColumns}) to '{key.CandidateKeyTable}' ({key.CandidateKeyColumns}).", modelTable, null));
        }
        foreach (var key in database)
            differences.Add(new(SchemaDifferenceKind.ExtraForeignKey, SchemaDifferenceSeverity.Warning, SchemaDifferenceSafety.Destructive,
                $"{key.ForeignKeyTable}.{key.ConstraintName}",
                $"Database has extra foreign key '{key.ConstraintName}' from '{key.ForeignKeyTable}' ({key.ForeignKeyColumns}) to '{key.CandidateKeyTable}' ({key.CandidateKeyColumns}).", null, databaseTable));
    }

    private ComparisonKey CheckComparisonKey(CheckSignature check) => identifiers is null
        ? ComparisonKey.Ordinal(check.Key)
        : new(new[] { (identifiers.ConstraintNames, check.Name), (StringComparer.Ordinal, check.Expression) });

    // Compare individual identifiers, never case-fold an entire signature, SQL
    // expression or literal. The legacy path retains its original ordinal keys.
    private sealed class ComparisonKey : IEquatable<ComparisonKey>
    {
        private readonly (StringComparer Comparer, string Value)[] parts;
        internal ComparisonKey(IEnumerable<(StringComparer, string)> parts) => this.parts = parts.ToArray();
        internal static ComparisonKey Ordinal(string value) => new(new[] { (StringComparer.Ordinal, value) });
        public bool Equals(ComparisonKey? other)
        {
            if (other is null || parts.Length != other.parts.Length) return false;
            for (var i = 0; i < parts.Length; i++)
                if (!parts[i].Comparer.Equals(other.parts[i].Comparer) || !parts[i].Comparer.Equals(parts[i].Value, other.parts[i].Value)) return false;
            return true;
        }
        public override bool Equals(object? obj) => obj is ComparisonKey other && Equals(other);
        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var part in parts) hash = unchecked(hash * 31 + part.Comparer.GetHashCode(part.Value));
            return hash;
        }
    }
}
