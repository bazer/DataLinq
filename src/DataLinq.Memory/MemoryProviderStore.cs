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
    private readonly object seedGate = new();
    private readonly HashSet<TableDefinition> seedingTables = [];

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

        Publish(
            table,
            () => MemoryTableState.Create(table, canonicalProviderRows));
    }

    internal void SeedModelValues(
        TableDefinition table,
        IEnumerable<object?[]> modelRows)
    {
        ValidateOwnedTable(table);
        ArgumentNullException.ThrowIfNull(modelRows);

        Publish(
            table,
            () => MemoryTableState.CreateModelValues(table, modelRows));
    }

    internal void SeedModels<TModel>(
        TableDefinition table,
        IEnumerable<Mutable<TModel>> models)
        where TModel : class, IImmutableInstance
    {
        ValidateOwnedTable(table);
        ArgumentNullException.ThrowIfNull(models);

        Publish(
            table,
            () => MemoryTableState.CreateModels(table, models));
    }

    private void Publish(
        TableDefinition table,
        Func<MemoryTableState> createState)
    {
        lock (seedGate)
        {
            if (tables.ContainsKey(table))
                throw AlreadySeeded(table);
            if (!seedingTables.Add(table))
                throw new MemorySeedException(
                    $"Memory table '{table.DbName}' is already being seeded. The read-only memory backend permits only one seed publication attempt per table at a time.");
        }

        try
        {
            var state = createState();

            lock (seedGate)
            {
                if (!tables.TryAdd(table, state))
                    throw AlreadySeeded(table);
            }
        }
        finally
        {
            lock (seedGate)
            {
                seedingTables.Remove(table);
            }
        }
    }

    private static MemorySeedException AlreadySeeded(TableDefinition table) =>
        new($"Memory table '{table.DbName}' has already been seeded. The read-only memory backend publishes each table exactly once.");

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
    private const string ModelSeedSourceName = "memory.seed";
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
        => CreateCore(
            table,
            canonicalProviderRows,
            valuesAreModelValues: false,
            nameof(canonicalProviderRows));

    internal static MemoryTableState CreateModelValues(
        TableDefinition table,
        IEnumerable<object?[]> modelRows)
        => CreateCore(
            table,
            modelRows,
            valuesAreModelValues: true,
            nameof(modelRows));

    internal static MemoryTableState CreateModels<TModel>(
        TableDefinition table,
        IEnumerable<Mutable<TModel>> models)
        where TModel : class, IImmutableInstance
    {
        var modelRows = SnapshotModels(table, models);
        return CreateCore(
            table,
            modelRows,
            valuesAreModelValues: true,
            nameof(models));
    }

    private static IReadOnlyList<object?[]> SnapshotModels<TModel>(
        TableDefinition table,
        IEnumerable<Mutable<TModel>> models)
        where TModel : class, IImmutableInstance
    {
        var rows = new List<object?[]>();
        var rowOrdinal = 0;

        var enumerator = GetModelEnumerator(table, models);
        Exception? primaryFailure = null;
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = enumerator.MoveNext();
                }
                catch (Exception exception) when (ShouldWrapSeedFailure(exception))
                {
                    throw new MemorySeedException(
                        $"Generated mutable memory seed enumeration for table '{table.DbName}' failed before row {rowOrdinal}.");
                }

                if (!hasNext)
                    return rows;

                Mutable<TModel> model;
                try
                {
                    model = enumerator.Current;
                }
                catch (Exception exception) when (ShouldWrapSeedFailure(exception))
                {
                    throw new MemorySeedException(
                        $"Generated mutable memory seed enumeration for table '{table.DbName}' could not read row {rowOrdinal}.");
                }

                if (model is null)
                {
                    throw new MemorySeedException(
                        $"Generated mutable memory seed row {rowOrdinal} for table '{table.DbName}' cannot be null.");
                }

                if (model.IsDeleted())
                {
                    throw new MemorySeedException(
                        $"Generated mutable memory seed row {rowOrdinal} for table '{table.DbName}' cannot be marked as deleted.");
                }

                var rowData = model.GetRowData();
                if (!ReferenceEquals(rowData.Table, table))
                {
                    throw new MemorySeedException(
                        $"Generated mutable memory seed row {rowOrdinal} belongs to table '{rowData.Table.DbName}', not memory table '{table.DbName}'.");
                }

                try
                {
                    var values = new object?[table.ColumnCount];
                    for (var ordinal = 0; ordinal < values.Length; ordinal++)
                        values[ordinal] = rowData.GetValue(ordinal);

                    rows.Add(values);
                    rowOrdinal++;
                }
                catch (Exception exception) when (ShouldWrapSeedFailure(exception))
                {
                    throw new MemorySeedException(
                        $"Generated mutable memory seed row {rowOrdinal} for table '{table.DbName}' could not be read through its model-value accessors.");
                }
            }
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                enumerator.Dispose();
            }
            catch (Exception exception)
            {
                if (!ShouldWrapSeedFailure(exception))
                    throw;

                if (primaryFailure is null)
                {
                    throw new MemorySeedException(
                        $"Generated mutable memory seed enumeration for table '{table.DbName}' failed during cleanup.");
                }
            }
        }
    }

    private static IEnumerator<Mutable<TModel>> GetModelEnumerator<TModel>(
        TableDefinition table,
        IEnumerable<Mutable<TModel>> models)
        where TModel : class, IImmutableInstance
    {
        try
        {
            return models.GetEnumerator();
        }
        catch (Exception exception) when (ShouldWrapSeedFailure(exception))
        {
            throw new MemorySeedException(
                $"Generated mutable memory seed for table '{table.DbName}' could not be enumerated.");
        }
    }

    private static MemoryTableState CreateCore(
        TableDefinition table,
        IEnumerable<object?[]> rowsToSeed,
        bool valuesAreModelValues,
        string rowsParameterName)
    {
        if (!table.IsFrozen)
        {
            throw new MemorySeedException(
                $"Memory table '{table.DbName}' must use frozen generated metadata before seed publication.");
        }

        if (table.PrimaryKeyColumns.Count == 0)
        {
            throw new MemorySeedException(
                $"Memory table '{table.DbName}' has no primary key. The read-only memory backend requires keyed table state.");
        }

        var rows = new List<CanonicalProviderValueRow>();
        var primaryKeyOrdinals = new Dictionary<DataLinqKey, int>();
        var rowOrdinal = 0;

        foreach (var values in rowsToSeed)
        {
            CanonicalProviderValueRow row;
            DataLinqKey primaryKey;
            try
            {
                if (values is null)
                {
                    throw new ArgumentException(
                        valuesAreModelValues
                            ? "A model-valued memory seed row cannot be null."
                            : "A canonical memory seed row cannot be null.",
                        rowsParameterName);
                }

                var canonicalValues = valuesAreModelValues
                    ? NormalizeModelValues(table, values)
                    : values;
                row = CanonicalProviderValueRow.Create(table, canonicalValues);
                if (!row.TryCreateCanonicalPrimaryKey(out primaryKey))
                {
                    throw new InvalidOperationException(
                        $"Canonical memory seed row for table '{table.DbName}' has no primary-key identity.");
                }
            }
            catch (Exception exception) when (ShouldWrapSeedFailure(exception))
            {
                throw new MemorySeedException(
                    $"{(valuesAreModelValues ? "Model-valued" : "Canonical")} memory seed row {rowOrdinal} for table '{table.DbName}' is invalid. {exception.Message}");
            }

            if (!primaryKeyOrdinals.TryAdd(primaryKey, rowOrdinal))
            {
                throw new MemorySeedException(
                    $"{(valuesAreModelValues ? "Model-valued" : "Canonical")} memory seed for table '{table.DbName}' contains a duplicate primary key at row {rowOrdinal}; the first row is {primaryKeyOrdinals[primaryKey]}.");
            }

            rows.Add(row);
            rowOrdinal++;
        }

        return new MemoryTableState(rows.ToArray(), primaryKeyOrdinals);
    }

    private static object?[] NormalizeModelValues(
        TableDefinition table,
        object?[] modelValues)
    {
        if (modelValues.Length != table.ColumnCount)
        {
            throw new ArgumentException(
                $"Model-valued memory row for table '{table.DbName}' requires exactly {table.ColumnCount} table-ordinal values, but received {modelValues.Length}. Missing cells must not be represented as null.",
                nameof(modelValues));
        }

        var canonicalValues = new object?[modelValues.Length];
        for (var ordinal = 0; ordinal < modelValues.Length; ordinal++)
        {
            var column = table.Columns[ordinal];
            CanonicalProviderValueRow.ValidateModelValue(
                column,
                modelValues[ordinal],
                nameof(modelValues));
        }

        for (var ordinal = 0; ordinal < modelValues.Length; ordinal++)
        {
            var column = table.Columns[ordinal];
            canonicalValues[ordinal] = ModelValueConverter.ToCanonicalProviderValue(
                column,
                modelValues[ordinal],
                ModelSeedSourceName);
        }

        return canonicalValues;
    }

    private static bool ShouldWrapSeedFailure(Exception exception) =>
        exception is not MemorySeedException and
        not OperationCanceledException and
        not OutOfMemoryException and
        not AccessViolationException;

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

/// <summary>
/// Reports an invalid or conflicting memory seed without exposing captured row values.
/// </summary>
public sealed class MemorySeedException : InvalidOperationException
{
    /// <summary>
    /// Creates a memory seed failure with a value-redacted diagnostic.
    /// </summary>
    /// <param name="message">The value-redacted failure description.</param>
    internal MemorySeedException(string message)
        : base(message)
    {
    }
}
