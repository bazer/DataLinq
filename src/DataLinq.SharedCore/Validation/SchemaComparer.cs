using System;
using System.Collections.Generic;
using System.Linq;
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
                differences.Add(new SchemaDifference(
                    SchemaDifferenceKind.MissingTable,
                    SchemaDifferenceSeverity.Error,
                    SchemaDifferenceSafety.Additive,
                    modelTable.Table.DbName,
                    $"Database is missing table '{modelTable.Table.DbName}'.",
                    modelTable.Table,
                    null));
                continue;
            }

            if (options.Capabilities.CompareColumns)
                CompareColumns(modelTable.Table, databaseTable.Table, differences);
        }

        foreach (var databaseTable in databaseTables)
        {
            var modelTable = modelTables
                .FirstOrDefault(x => tableComparer.Equals(x.Table.DbName, databaseTable.Table.DbName));

            if (modelTable != null)
                continue;

            differences.Add(new SchemaDifference(
                SchemaDifferenceKind.ExtraTable,
                SchemaDifferenceSeverity.Warning,
                SchemaDifferenceSafety.Destructive,
                databaseTable.Table.DbName,
                $"Database has extra table '{databaseTable.Table.DbName}' that is not represented by the model.",
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
                continue;

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

    private static IEnumerable<TableModel> GetComparableTables(DatabaseDefinition database)
    {
        return database.TableModels
            .Where(x => !x.IsStub && x.Table.Type == TableType.Table);
    }
}
