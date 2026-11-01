using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Validation;

// Selection never constructs a reduced metadata graph: selected owners retain
// finalized relations to objects outside the comparison scope.
internal sealed class SchemaComparisonScope
{
    private readonly TableModel[] modelTables;
    private readonly string[]? selectedNames;

    private SchemaComparisonScope(TableModel[] modelTables, string[]? selectedNames)
    {
        this.modelTables = modelTables;
        this.selectedNames = selectedNames;
    }

    internal static SchemaComparisonScope Capture(DatabaseDefinition model, DatabaseType type, IReadOnlyList<string>? include)
    {
        ArgumentNullException.ThrowIfNull(model);
        var tables = ComparableTables(model);
        if (include is null || include.Count == 0) return new(tables, null);
        var comparer = type == DatabaseType.SQLite ? SchemaIdentifierRules.SQLite.TableNames : StringComparer.Ordinal;
        var names = new HashSet<string>(comparer);
        foreach (var requested in include)
        {
            if (string.IsNullOrWhiteSpace(requested)) throw new ArgumentException("Include contains an empty database object name.", nameof(include));
            var matches = tables.Where(table => comparer.Equals(table.Table.DbName, requested)).ToArray();
            if (matches.Length > 1)
                throw new ArgumentException($"Include name '{requested}' resolves to multiple compiled model objects.", nameof(include));
            if (matches.Length == 0)
            {
                var hintComparer = type == DatabaseType.SQLite ? comparer : StringComparer.OrdinalIgnoreCase;
                var similar = tables.Where(table => hintComparer.Equals(table.Table.DbName, requested)).ToArray();
                var hint = similar.Length == 1 ? $" Use the exact compiled-model spelling '{similar[0].Table.DbName}'." : "";
                throw new ArgumentException($"Include name '{requested}' does not identify a compiled model table or view.{hint}", nameof(include));
            }
            names.Add(matches[0].Table.DbName);
        }
        return new(tables, names.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    internal (IReadOnlyList<TableModel> Model, IReadOnlyList<TableModel> Database) Resolve(
        DatabaseDefinition database, SchemaIdentifierRules identifiers)
    {
        bool Selected(TableModel table) => selectedNames is null || selectedNames.Contains(table.Table.DbName, identifiers.TableNames);
        var selectedModel = modelTables.Where(Selected).OrderBy(table => table.Table.DbName, StringComparer.Ordinal).ToArray();
        var databaseTables = ComparableTables(database);
        var selectedDatabase = databaseTables.Where(Selected).OrderBy(table => table.Table.DbName, StringComparer.Ordinal).ToArray();
        SchemaIdentifierRules.RequireUnambiguous(selectedModel.Select(table => table.Table.DbName), identifiers.TableNames, "table/view", model: true);
        SchemaIdentifierRules.RequireUnambiguous(selectedDatabase.Select(table => table.Table.DbName), identifiers.TableNames, "table/view", model: false);
        ValidateReferencedNames(modelTables, selectedModel, identifiers, model: true);
        ValidateReferencedNames(databaseTables, selectedDatabase, identifiers, model: false);
        return (Array.AsReadOnly(selectedModel), Array.AsReadOnly(selectedDatabase));
    }

    private static void ValidateReferencedNames(TableModel[] all, TableModel[] selected, SchemaIdentifierRules identifiers, bool model)
    {
        var references = selected.SelectMany(table => table.Table.ColumnIndices).SelectMany(index => index.RelationParts)
            .Where(part => part.Type == RelationPartType.ForeignKey).Select(part => part.Relation.CandidateKey.ColumnIndex).Distinct();
        foreach (var reference in references)
        {
            var candidates = all.Where(table => identifiers.TableNames.Equals(table.Table.DbName, reference.Table.DbName)).ToArray();
            SchemaIdentifierRules.RequireUnambiguous(candidates.Select(table => table.Table.DbName), identifiers.TableNames, "referenced table", model);
            foreach (var candidate in candidates)
                SchemaIdentifierRules.RequireUnambiguous(candidate.Table.Columns
                    .Where(column => reference.Columns.Any(target => identifiers.ColumnNames.Equals(column.DbName, target.DbName)))
                    .Select(column => column.DbName), identifiers.ColumnNames, $"referenced column in '{candidate.Table.DbName}'", model);
        }
    }

    private static TableModel[] ComparableTables(DatabaseDefinition database) => database.TableModels
        .Where(table => !table.IsStub && table.Table.Type is TableType.Table or TableType.View).ToArray();
}
