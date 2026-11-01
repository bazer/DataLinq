using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.Validation;
using ThrowAway;

namespace DataLinq.SQLite;

public partial class MetadataFromSQLiteFactory
{
    private SchemaReadLimitations? limitations;

    private bool NativeNamesEqual(string? left, string? right) =>
        (limitations is null ? StringComparer.Ordinal : SchemaIdentifierRules.SQLite.TableNames).Equals(left, right);

    private void ReportRelationLimitation(SQLiteProviderTableDraft table, CatalogRelation row, IReadOnlyList<CatalogRelation>? group,
        SchemaReadLimitationReason reason, string message)
    {
        var columns = (group ?? new[] { row }).OrderBy(part => part.Ordinal).ToArray();
        limitations?.Add(new(table.DbName, row.Name, SchemaReadAspect.ForeignKey, reason, message,
            columns.All(part => part.From is not null) ? columns.Select(part => part.From!).ToArray() : null,
            row.Table, columns.All(part => !string.IsNullOrWhiteSpace(part.To)) ? columns.Select(part => part.To!).ToArray() : null,
            onUpdate: row.OnUpdate, onDelete: row.OnDelete));
        options.Log?.Invoke(message);
    }

    private void ReportLimitation(string table, string? name, SchemaReadAspect aspect, SchemaReadLimitationReason reason, string message,
        string? comparisonName = null)
    {
        limitations?.Add(new(table, name, aspect, reason, message, comparisonName: comparisonName));
        options.Log?.Invoke(message);
    }

    private void ValidateNativeRelationCompleteness(SQLiteProviderDatabaseDraft database, SQLiteProviderTableDraft table,
        IReadOnlyList<CatalogRelation> relations, List<IDLOptionFailure> failures)
    {
        if (limitations is null) return;
        foreach (var group in relations.GroupBy(row => row.Name))
        {
            if (group.Count() < 2 || group.Any(row => string.IsNullOrWhiteSpace(row.Table) ||
                string.IsNullOrWhiteSpace(row.From) || string.IsNullOrWhiteSpace(row.To))) continue;
            var represented = group.Count(row => table.Columns.Any(column => NativeNamesEqual(column.Column.DbName, row.From)) &&
                database.TableModels.Any(target => NativeNamesEqual(target.Table.DbName, row.Table) &&
                    target.Table.Columns.Any(column => NativeNamesEqual(column.Column.DbName, row.To))));
            if (represented != 0 && represented != group.Count())
                failures.Add(DLOptionFailure.Fail(DLFailureType.InvalidModel,
                    $"SQLite foreign key '{group.Key}' on table '{table.DbName}' is only partially representable. A complete relationship cannot be acquired because one or more columns were not imported."));
        }
    }

    private (bool Omitted, Option<SQLiteProviderValuePropertyDraft, IDLOptionFailure> Column) ReadNativeColumn(
        SQLiteProviderTableDraft table, IDataLinqDataReader row, string? definition)
    {
        if (limitations is not null)
        {
            if (table.Type == TableType.View)
                ReportLimitation(table.DbName, row.GetString(1), SchemaReadAspect.ColumnNullability,
                    SchemaReadLimitationReason.UnreportedViewNullability,
                    $"Warning: SQLite metadata cannot establish nullability of view column '{table.DbName}.{row.GetString(1)}'.");
            var hidden = row.GetInt32(row.GetOrdinal("hidden"));
            if (hidden is < 0 or > 3)
                return (false, DLOptionFailure.Fail(DLFailureType.InvalidModel, $"Unknown SQLite column visibility value '{hidden}' in table '{table.DbName}'."));
            if (hidden != 0)
            {
                var name = row.GetString(1);
                var kind = hidden == 1 ? "hidden" : "generated";
                ReportLimitation(table.DbName, name, SchemaReadAspect.Column,
                    hidden == 1 ? SchemaReadLimitationReason.HiddenColumn : SchemaReadLimitationReason.GeneratedColumn,
                    $"Warning: Skipping unsupported SQLite {kind} column '{table.DbName}.{name}'.");
                return (true, default);
            }
        }
        return (false, ParseColumn(table, row, definition));
    }
}
