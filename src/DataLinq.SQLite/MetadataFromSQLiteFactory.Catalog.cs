using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using DataLinq.Validation;

namespace DataLinq.SQLite;

public partial class MetadataFromSQLiteFactory
{
    // SQLite reserves sqlite_ for internal schema objects, including ANALYZE
    // statistics and the AUTOINCREMENT sequence. GLOB keeps '_' literal.
    private const string RuntimeCatalogSql = "SELECT type, tbl_name, sql FROM sqlite_master WHERE type IN ('table', 'view') AND tbl_name NOT GLOB 'sqlite_*' ORDER BY tbl_name";

    // Snapshot catalog rows while their reader is alive. Both execution modes
    // use the same local interpretation, warning and unsupported-feature policy.
    private sealed record CatalogIndex(string Name, string Origin, IndexCharacteristic Characteristic, bool Partial)
    {
        internal static CatalogIndex Read(IDataLinqDataReader row) => new(row.GetString(0), row.GetString(1),
            row.GetInt32(2) == 1 ? IndexCharacteristic.Unique : IndexCharacteristic.Simple, row.GetInt32(3) == 1);
    }

    private sealed record CatalogIndexColumn(int Id, string? Name, bool Descending)
    {
        internal static CatalogIndexColumn Read(IDataLinqDataReader row) => new(row.GetInt32(1),
            row.IsDbNull(2) ? null : row.GetString(2), row.GetInt32(3) == 1);
    }

    private sealed record CatalogRelation(string Name, int Ordinal, string? Table, string? From, string? To,
        ReferentialAction OnUpdate, ReferentialAction OnDelete)
    {
        internal static CatalogRelation Read(IDataLinqDataReader row) => new(row.GetString(0), row.GetInt32(1),
            row.IsDbNull(2) ? null : row.GetString(2), row.IsDbNull(3) ? null : row.GetString(3),
            row.IsDbNull(4) ? null : row.GetString(4), ParseReferentialAction(row.GetString(5)), ParseReferentialAction(row.GetString(6)));
    }

    private static string IndexListSql(string table) => $"SELECT name, origin, \"unique\", partial FROM pragma_index_list({QuoteSqlLiteral(table)})";
    private static string IndexColumnsSql(string index) => $"SELECT seqno, cid, name, \"desc\", \"key\" FROM pragma_index_xinfo({QuoteSqlLiteral(index)}) WHERE \"key\" = 1 ORDER BY seqno";
    private static string RelationsSql(string table) => $"SELECT id, seq, \"table\", \"from\", \"to\", on_update, on_delete FROM pragma_foreign_key_list({QuoteSqlLiteral(table)})";

    private bool ShouldReadIndexColumns(SQLiteProviderTableDraft table, CatalogIndex index)
    {
        if (index.Origin == "pk") return false;
        if (!index.Partial) return true;
        ReportLimitation(table.DbName, index.Name, SchemaReadAspect.Index, SchemaReadLimitationReason.PartialIndex,
            $"Warning: Skipping unsupported SQLite partial index '{index.Name}' on table '{table.DbName}'.");
        return false;
    }

    private void ParseIndexColumns(SQLiteProviderTableDraft table, CatalogIndex index, IEnumerable<CatalogIndexColumn> rows)
    {
        var parts = rows.ToArray();
        var comparisonName = index.Name;
        if (index.Name.StartsWith("sqlite_autoindex", StringComparison.Ordinal) && parts.Length > 0 && parts.All(row => row.Name is not null))
            comparisonName = parts.Length == 1 ? parts[0].Name! : $"{table.DbName}_{string.Join("_", parts.Select(row => row.Name))}_{(index.Characteristic == IndexCharacteristic.Unique ? "unique" : index.Characteristic.ToString().ToLowerInvariant())}";
        var columns = new List<SQLiteProviderValuePropertyDraft>();
        foreach (var row in parts)
        {
            if (row.Id < 0 || row.Name is null)
            {
                ReportLimitation(table.DbName, index.Name, SchemaReadAspect.Index, SchemaReadLimitationReason.ExpressionIndex,
                    $"Warning: Skipping unsupported SQLite expression index '{index.Name}' on table '{table.DbName}'.", comparisonName);
                return;
            }
            if (row.Descending)
            {
                ReportLimitation(table.DbName, index.Name, SchemaReadAspect.Index, SchemaReadLimitationReason.DescendingIndex,
                    $"Warning: Skipping unsupported SQLite descending index '{index.Name}' on table '{table.DbName}'.", comparisonName);
                return;
            }
            var column = table.Columns.SingleOrDefault(x => NativeNamesEqual(x.Column.DbName, row.Name));
            if (column is null)
            {
                ReportLimitation(table.DbName, index.Name, SchemaReadAspect.Index, SchemaReadLimitationReason.UnrepresentedIndexColumn,
                    $"Warning: Skipping SQLite index '{index.Name}' on table '{table.DbName}' because column '{row.Name}' was not imported.", comparisonName);
                return;
            }
            columns.Add(column);
        }
        if (columns.Count == 0) return;
        var name = index.Name.StartsWith("sqlite_autoindex", StringComparison.Ordinal)
            ? GetAutoIndexName(table, columns, index.Characteristic) : index.Name;
        var names = columns.Select(x => x.Column.DbName).ToArray();
        foreach (var column in columns) column.Attributes.Add(new IndexAttribute(name, index.Characteristic, IndexType.BTREE, names));
    }

    private void ParseRelation(SQLiteProviderDatabaseDraft database, SQLiteProviderTableDraft table,
        CatalogRelation row, List<IDLOptionFailure> failures, IReadOnlyList<CatalogRelation>? group = null)
    {
        if (string.IsNullOrWhiteSpace(row.Table) || string.IsNullOrWhiteSpace(row.From))
        {
            failures.Add(DLOptionFailure.Fail(DLFailureType.InvalidModel,
                $"Malformed SQLite foreign-key metadata row in table '{table.DbName}': referenced table and source column are required."));
            return;
        }
        var column = table.Columns.SingleOrDefault(x => NativeNamesEqual(x.Column.DbName, row.From));
        if (column is null)
        {
            if (limitations is not null)
                ReportRelationLimitation(table, row, group, SchemaReadLimitationReason.UnrepresentedForeignKeySource,
                    $"Warning: Skipping foreign key '{row.Name}' on table '{table.DbName}' because source column '{row.From}' was not imported.");
            return;
        }
        var target = database.TableModels.SingleOrDefault(x => NativeNamesEqual(x.Table.DbName, row.Table))?.Table;
        if (target is null)
        {
            ReportRelationLimitation(table, row, group, SchemaReadLimitationReason.UnrepresentedForeignKeyTarget,
                $"Warning: Skipping foreign key '{row.Name}' on table '{table.DbName}' because referenced table '{row.Table}' was not imported.");
            return;
        }
        var to = row.To;
        if (string.IsNullOrWhiteSpace(to))
        {
            var keys = target.Columns.Where(x => x.Column.PrimaryKey).ToArray();
            if (keys.Length != 1)
            {
                failures.Add(DLOptionFailure.Fail(DLFailureType.InvalidModel,
                    $"SQLite foreign key '{row.Name}' on table '{table.DbName}' omits the referenced column, but referenced table '{row.Table}' does not have exactly one imported primary-key column."));
                return;
            }
            to = keys[0].Column.DbName;
        }
        var targetColumn = target.Columns.SingleOrDefault(x => NativeNamesEqual(x.Column.DbName, to));
        if (targetColumn is null)
        {
            ReportRelationLimitation(table, row, group, SchemaReadLimitationReason.UnrepresentedForeignKeyTarget,
                $"Warning: Skipping foreign key '{row.Name}' on table '{table.DbName}' because referenced column '{row.Table}.{to}' was not imported.");
            return;
        }
        column.Column.ForeignKey = true;
        column.Attributes.Add(new ForeignKeyAttribute(limitations is null ? row.Table : target.DbName,
            limitations is null ? to : targetColumn.Column.DbName, row.Name, row.Ordinal, row.OnUpdate, row.OnDelete));
    }
}
