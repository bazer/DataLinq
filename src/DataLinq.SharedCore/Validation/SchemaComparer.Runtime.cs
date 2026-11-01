using System;
using System.Collections.Generic;
using System.Linq;
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
            (identifiers.ConstraintNames, key.ConstraintName),
            (identifiers.TableNames, key.ForeignKeyTable),
            (identifiers.TableNames, key.CandidateKeyTable),
            // Preserve sequence boundaries even when identifiers contain commas or pipes.
            (StringComparer.Ordinal, foreign.Columns.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        parts.AddRange(foreign.Columns.Select(column => (identifiers.ColumnNames, column.DbName)));
        parts.AddRange(candidate.Columns.Select(column => (identifiers.ColumnNames, column.DbName)));
        if (!shapeOnly)
        {
            parts.Add((StringComparer.Ordinal, key.OnUpdate.ToString()));
            parts.Add((StringComparer.Ordinal, key.OnDelete.ToString()));
        }
        return new(parts);
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
