using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Attributes;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;

namespace DataLinq.SQLite;

public partial class MetadataFromSQLiteFactory
{
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
        options.Log?.Invoke($"Warning: Skipping unsupported SQLite partial index '{index.Name}' on table '{table.DbName}'.");
        return false;
    }

    private void ParseIndexColumns(SQLiteProviderTableDraft table, CatalogIndex index, IEnumerable<CatalogIndexColumn> rows)
    {
        var columns = new List<SQLiteProviderValuePropertyDraft>();
        foreach (var row in rows)
        {
            if (row.Id < 0 || row.Name is null)
            {
                options.Log?.Invoke($"Warning: Skipping unsupported SQLite expression index '{index.Name}' on table '{table.DbName}'.");
                return;
            }
            if (row.Descending)
            {
                options.Log?.Invoke($"Warning: Skipping unsupported SQLite descending index '{index.Name}' on table '{table.DbName}'.");
                return;
            }
            var column = table.Columns.SingleOrDefault(x => x.Column.DbName == row.Name);
            if (column is null)
            {
                options.Log?.Invoke($"Warning: Skipping SQLite index '{index.Name}' on table '{table.DbName}' because column '{row.Name}' was not imported.");
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
        CatalogRelation row, List<IDLOptionFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(row.Table) || string.IsNullOrWhiteSpace(row.From))
        {
            failures.Add(DLOptionFailure.Fail(DLFailureType.InvalidModel,
                $"Malformed SQLite foreign-key metadata row in table '{table.DbName}': referenced table and source column are required."));
            return;
        }
        var column = table.Columns.SingleOrDefault(x => x.Column.DbName == row.From);
        if (column is null) return;
        var target = database.TableModels.SingleOrDefault(x => x.Table.DbName == row.Table)?.Table;
        if (target is null)
        {
            options.Log?.Invoke($"Warning: Skipping foreign key '{row.Name}' on table '{table.DbName}' because referenced table '{row.Table}' was not imported.");
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
        if (target.Columns.SingleOrDefault(x => x.Column.DbName == to) is null)
        {
            options.Log?.Invoke($"Warning: Skipping foreign key '{row.Name}' on table '{table.DbName}' because referenced column '{row.Table}.{to}' was not imported.");
            return;
        }
        column.Column.ForeignKey = true;
        column.Attributes.Add(new ForeignKeyAttribute(row.Table, to, row.Name, row.Ordinal, row.OnUpdate, row.OnDelete));
    }
}
