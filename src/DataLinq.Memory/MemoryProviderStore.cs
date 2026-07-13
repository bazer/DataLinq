using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Memory;

/// <summary>
/// Immutable table-state owner. Materialized model instances never enter this store.
/// </summary>
internal sealed class MemoryProviderStore
{
    private readonly DatabaseDefinition metadata;
    private readonly ConcurrentDictionary<TableDefinition, MemoryTableState> tables = new();

    internal MemoryProviderStore(DatabaseDefinition metadata)
    {
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    internal void SeedCanonical(
        TableDefinition table,
        IEnumerable<object?[]> canonicalProviderRows)
    {
        ValidateOwnedTable(table);
        ArgumentNullException.ThrowIfNull(canonicalProviderRows);

        var state = MemoryTableState.Create(table, canonicalProviderRows);
        if (!tables.TryAdd(table, state))
        {
            throw new MemorySeedException(
                $"Memory table '{table.DbName}' has already been seeded. The read-only spike publishes each table exactly once.");
        }
    }

    internal bool TryGet(
        TableDefinition table,
        DataLinqKey canonicalProviderKey,
        out CanonicalProviderValueRow? row)
    {
        ValidateOwnedTable(table);
        if (tables.TryGetValue(table, out var state))
            return state.TryGet(canonicalProviderKey, out row);

        row = null;
        return false;
    }

    internal IReadOnlyList<CanonicalProviderValueRow> GetRows(TableDefinition table)
    {
        ValidateOwnedTable(table);
        return tables.TryGetValue(table, out var state)
            ? state.Rows
            : Array.Empty<CanonicalProviderValueRow>();
    }

    internal int GetRowCount(TableDefinition table)
    {
        ValidateOwnedTable(table);
        return tables.TryGetValue(table, out var state) ? state.Rows.Count : 0;
    }

    private void ValidateOwnedTable(TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (!ReferenceEquals(table.Database, metadata))
        {
            throw new ArgumentException(
                $"Table '{table.DbName}' is not owned by memory database '{metadata.DbName}'.",
                nameof(table));
        }
    }
}

internal sealed class MemoryTableState
{
    private readonly IReadOnlyList<CanonicalProviderValueRow> rows;
    private readonly IReadOnlyDictionary<DataLinqKey, int> primaryKeyOrdinals;

    private MemoryTableState(
        CanonicalProviderValueRow[] rows,
        Dictionary<DataLinqKey, int> primaryKeyOrdinals)
    {
        this.rows = Array.AsReadOnly(rows);
        this.primaryKeyOrdinals = primaryKeyOrdinals;
    }

    internal IReadOnlyList<CanonicalProviderValueRow> Rows => rows;

    internal static MemoryTableState Create(
        TableDefinition table,
        IEnumerable<object?[]> canonicalProviderRows)
    {
        if (!table.IsFrozen)
        {
            throw new MemorySeedException(
                $"Memory table '{table.DbName}' must use frozen generated metadata before seed publication.");
        }

        if (table.PrimaryKeyColumns.Count == 0)
        {
            throw new MemorySeedException(
                $"Memory table '{table.DbName}' has no primary key. The vertical spike requires keyed table state.");
        }

        var rows = new List<CanonicalProviderValueRow>();
        var primaryKeyOrdinals = new Dictionary<DataLinqKey, int>();
        var rowOrdinal = 0;

        foreach (var values in canonicalProviderRows)
        {
            CanonicalProviderValueRow row;
            DataLinqKey primaryKey;
            try
            {
                if (values is null)
                {
                    throw new ArgumentException(
                        "A canonical memory seed row cannot be null.",
                        nameof(canonicalProviderRows));
                }

                row = CanonicalProviderValueRow.Create(table, values);
                if (!row.TryCreateCanonicalPrimaryKey(out primaryKey))
                {
                    throw new InvalidOperationException(
                        $"Canonical memory seed row for table '{table.DbName}' has no primary-key identity.");
                }
            }
            catch (Exception exception) when (
                exception is not MemorySeedException and
                not OperationCanceledException and
                not OutOfMemoryException and
                not AccessViolationException)
            {
                throw new MemorySeedException(
                    $"Canonical memory seed row {rowOrdinal} for table '{table.DbName}' is invalid. {exception.Message}",
                    exception);
            }

            if (!primaryKeyOrdinals.TryAdd(primaryKey, rowOrdinal))
            {
                throw new MemorySeedException(
                    $"Canonical memory seed for table '{table.DbName}' contains a duplicate primary key at row {rowOrdinal}; the first row is {primaryKeyOrdinals[primaryKey]}.");
            }

            rows.Add(row);
            rowOrdinal++;
        }

        return new MemoryTableState(rows.ToArray(), primaryKeyOrdinals);
    }

    internal bool TryGet(
        DataLinqKey canonicalProviderKey,
        out CanonicalProviderValueRow? row)
    {
        if (primaryKeyOrdinals.TryGetValue(canonicalProviderKey, out var rowOrdinal))
        {
            row = rows[rowOrdinal];
            return true;
        }

        row = null;
        return false;
    }
}

internal sealed class MemorySeedException : InvalidOperationException
{
    internal MemorySeedException(string message)
        : base(message)
    {
    }

    internal MemorySeedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
