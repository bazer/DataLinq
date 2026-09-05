using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DataLinq.Metadata;

namespace DataLinq.Instances;

/// <summary>
/// Read-only index/count view over stable collection storage. The view never exposes its backing
/// collection and its pattern enumerator avoids the interface-enumerator allocation in
/// performance-sensitive synchronous loops.
/// </summary>
internal readonly struct ReadOnlyListSlice<T> : IReadOnlyList<T>
{
    private readonly IReadOnlyList<T> source;
    private readonly int offset;

    internal ReadOnlyListSlice(IReadOnlyList<T> source, int offset, int count)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > source.Count - count)
            throw new ArgumentException("The requested slice exceeds the source collection bounds.");

        this.offset = offset;
        Count = count;
    }

    internal ReadOnlyListSlice(IReadOnlyList<T> source)
        : this(source, 0, source?.Count ?? throw new ArgumentNullException(nameof(source)))
    {
    }

    public int Count { get; }
    internal int Length => Count;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return source[offset + index];
        }
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal struct Enumerator(ReadOnlyListSlice<T> slice) : IEnumerator<T>
    {
        private int index = -1;

        public T Current => slice[index];
        object? IEnumerator.Current => Current;

        public bool MoveNext() => ++index < slice.Count;
        public void Reset() => index = -1;
        public void Dispose() { }
    }
}

/// <summary>
/// Immutable request for a finite batch of full canonical-provider rows selected by primary key.
/// The owning read service carries provider and transaction scope; the request carries no backend
/// command, connection, reader, or mutation responsibility.
/// </summary>
internal sealed class SourcePrimaryKeyRowRequest
{
    internal SourcePrimaryKeyRowRequest(
        TableDefinition table,
        IEnumerable<DataLinqKey> canonicalProviderKeys,
        CancellationToken cancellationToken = default)
        : this(
            table,
            SnapshotKeys(canonicalProviderKeys),
            cancellationToken,
            nameof(canonicalProviderKeys))
    {
    }

    private SourcePrimaryKeyRowRequest(
        TableDefinition table,
        ReadOnlyListSlice<DataLinqKey> canonicalProviderKeys,
        CancellationToken cancellationToken,
        string parameterName)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));

        SourceRowLoadingValidation.ValidatePrimaryKeyTable(table);

        if (canonicalProviderKeys.Length == 0)
        {
            throw new ArgumentException(
                "A primary-key row request requires at least one canonical provider key.",
                parameterName);
        }

        for (var keyIndex = 0; keyIndex < canonicalProviderKeys.Length; keyIndex++)
            SourceRowLoadingValidation.ValidateCanonicalKey(
                table,
                canonicalProviderKeys[keyIndex],
                keyIndex,
                parameterName);

        CanonicalProviderKeys = canonicalProviderKeys;
        CancellationToken = cancellationToken;
    }

    /// <summary>
    /// Borrows one slice of caller-owned stable storage for a synchronous load. The caller must not
    /// mutate the collection until the returned result has been consumed.
    /// </summary>
    internal static SourcePrimaryKeyRowRequest Borrow(
        TableDefinition table,
        IReadOnlyList<DataLinqKey> canonicalProviderKeys,
        int offset,
        int count,
        CancellationToken cancellationToken = default) =>
        new(
            table,
            new ReadOnlyListSlice<DataLinqKey>(canonicalProviderKeys, offset, count),
            cancellationToken,
            nameof(canonicalProviderKeys));

    internal TableDefinition Table { get; }
    internal ReadOnlyListSlice<DataLinqKey> CanonicalProviderKeys { get; }
    internal CancellationToken CancellationToken { get; }

    internal void ThrowIfCancellationRequested() =>
        CancellationToken.ThrowIfCancellationRequested();

    private static ReadOnlyListSlice<DataLinqKey> SnapshotKeys(
        IEnumerable<DataLinqKey> canonicalProviderKeys)
    {
        ArgumentNullException.ThrowIfNull(canonicalProviderKeys);
        return new ReadOnlyListSlice<DataLinqKey>(canonicalProviderKeys.ToArray());
    }
}

internal static class SourceRowLoadingValidation
{
    internal static void ValidatePrimaryKeyTable(TableDefinition table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (!table.IsFrozen)
        {
            throw new InvalidOperationException(
                $"Source-row requests require frozen metadata, but table '{table.DbName}' is still mutable.");
        }

        if (table.PrimaryKeyColumns.Count == 0)
        {
            throw new ArgumentException(
                $"Table '{table.DbName}' has no primary key and cannot create a primary-key row request.",
                nameof(table));
        }
    }

    internal static void ValidateCanonicalKey(
        TableDefinition table,
        DataLinqKey key,
        int keyIndex,
        string parameterName)
    {
        var primaryKeyColumns = table.PrimaryKeyColumns;
        if (key.ValueCount != primaryKeyColumns.Count)
        {
            throw new ArgumentException(
                $"Canonical provider key at index {keyIndex} for table '{table.DbName}' has {key.ValueCount} components, expected {primaryKeyColumns.Count}.",
                parameterName);
        }

        for (var componentIndex = 0; componentIndex < primaryKeyColumns.Count; componentIndex++)
        {
            var column = primaryKeyColumns[componentIndex];
            var value = key.GetValue(componentIndex);
            if (value is null || ReferenceEquals(value, DBNull.Value))
            {
                throw new ArgumentException(
                    $"Canonical provider key at index {keyIndex} for table '{table.DbName}' contains a null component for column '{column.DbName}'.",
                    parameterName);
            }

            var providerType = column.ProviderClrType
                ?? throw new InvalidOperationException(
                    $"Primary-key column '{table.DbName}.{column.DbName}' has no runtime canonical provider CLR type metadata.");
            var expectedType = Nullable.GetUnderlyingType(providerType) ?? providerType;
            if (expectedType.IsEnum)
                expectedType = Enum.GetUnderlyingType(expectedType);

            if (value.GetType() != expectedType)
            {
                throw new ArgumentException(
                    $"Canonical provider key at index {keyIndex} for column '{table.DbName}.{column.DbName}' requires CLR type '{expectedType.FullName}', but received '{value.GetType().FullName}'.",
                    parameterName);
            }
        }
    }

    internal static void ValidateSingleResult(
        TableDefinition table,
        in DataLinqKey requestedKey,
        CanonicalProviderValueRow row,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!ReferenceEquals(row.Table, table))
        {
            throw new InvalidOperationException(
                $"{operation} returned a row from table '{row.Table.DbName}' for table '{table.DbName}'.");
        }

        if (!row.TryCreateCanonicalPrimaryKey(out var returnedKey))
        {
            throw new InvalidOperationException(
                $"{operation} for table '{table.DbName}' returned a row without a canonical primary key.");
        }

        if (!returnedKey.Equals(requestedKey))
        {
            throw new InvalidOperationException(
                $"{operation} for table '{table.DbName}' returned an unrequested primary key.");
        }
    }
}

/// <summary>
/// Immutable request for full canonical-provider rows selected by one table index value.
/// Matching semantics belong to the backend: SQL collation and provider equality are not
/// reinterpreted by this neutral request.
/// </summary>
internal sealed class SourceIndexRowRequest
{
    internal SourceIndexRowRequest(
        TableDefinition table,
        ColumnIndex index,
        DataLinqKey canonicalProviderIndexKey,
        CancellationToken cancellationToken = default)
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        Index = index ?? throw new ArgumentNullException(nameof(index));

        if (!table.IsFrozen)
        {
            throw new InvalidOperationException(
                $"Source-index row requests require frozen metadata, but table '{table.DbName}' is still mutable.");
        }

        if (!index.IsFrozen)
        {
            throw new ArgumentException(
                $"Source-index row requests require a frozen index, but index '{index.Name}' is still mutable.",
                nameof(index));
        }

        if (!ReferenceEquals(index.Table, table) || !table.ColumnIndices.Contains(index))
        {
            throw new ArgumentException(
                $"Index '{index.Name}' does not belong to table '{table.DbName}'.",
                nameof(index));
        }

        if (table.PrimaryKeyColumns.Count == 0)
        {
            throw new ArgumentException(
                $"Table '{table.DbName}' has no primary key and cannot create an index-row request whose rows participate in cache identity.",
                nameof(table));
        }

        ValidateCanonicalIndexKey(
            table,
            index,
            canonicalProviderIndexKey,
            nameof(canonicalProviderIndexKey));

        CanonicalProviderIndexKey = CopyKey(canonicalProviderIndexKey);
        CancellationToken = cancellationToken;
    }

    internal TableDefinition Table { get; }
    internal ColumnIndex Index { get; }
    internal DataLinqKey CanonicalProviderIndexKey { get; }
    internal CancellationToken CancellationToken { get; }

    internal void ThrowIfCancellationRequested() =>
        CancellationToken.ThrowIfCancellationRequested();

    private static void ValidateCanonicalIndexKey(
        TableDefinition table,
        ColumnIndex index,
        DataLinqKey key,
        string parameterName)
    {
        if (key.ValueCount != index.Columns.Count)
        {
            throw new ArgumentException(
                $"Canonical provider index key for index '{index.Name}' on table '{table.DbName}' has {key.ValueCount} components, expected {index.Columns.Count}.",
                parameterName);
        }

        for (var componentIndex = 0; componentIndex < index.Columns.Count; componentIndex++)
        {
            var column = index.Columns[componentIndex];
            if (!ReferenceEquals(column.Table, table) ||
                (uint)column.Index >= (uint)table.ColumnCount ||
                !ReferenceEquals(table.Columns[column.Index], column))
            {
                throw new ArgumentException(
                    $"Index '{index.Name}' contains column '{column.DbName}' that is not owned by table '{table.DbName}'.",
                    nameof(index));
            }

            var value = key.GetValue(componentIndex);
            if (value is null || ReferenceEquals(value, DBNull.Value))
            {
                throw new ArgumentException(
                    $"Canonical provider index key for index '{index.Name}' on table '{table.DbName}' contains a null component for column '{column.DbName}'.",
                    parameterName);
            }

            var providerType = column.ProviderClrType
                ?? throw new InvalidOperationException(
                    $"Index column '{table.DbName}.{column.DbName}' has no runtime canonical provider CLR type metadata.");
            var expectedType = Nullable.GetUnderlyingType(providerType) ?? providerType;
            if (expectedType.IsEnum)
                expectedType = Enum.GetUnderlyingType(expectedType);

            if (value.GetType() != expectedType)
            {
                throw new ArgumentException(
                    $"Canonical provider index key for column '{table.DbName}.{column.DbName}' requires CLR type '{expectedType.FullName}', but received '{value.GetType().FullName}'.",
                    parameterName);
            }
        }
    }

    private static DataLinqKey CopyKey(DataLinqKey key)
    {
        var values = new object?[key.ValueCount];
        for (var index = 0; index < values.Length; index++)
            values[index] = key.GetValue(index);

        return DataLinqKey.FromValues(values);
    }
}

/// <summary>
/// Short-lived source-boundary row carrier. The canonical key owns any mutable components and is
/// reused by cache lookup, materialization, immutable construction, and cache publication.
/// </summary>
internal readonly record struct LoadedCanonicalRow
{
    internal LoadedCanonicalRow(
        CanonicalProviderValueRow providerRow,
        DataLinqKey canonicalProviderKey)
    {
        ProviderRow = providerRow ?? throw new ArgumentNullException(nameof(providerRow));
        CanonicalProviderKey = canonicalProviderKey;
    }

    internal CanonicalProviderValueRow ProviderRow { get; }
    internal DataLinqKey CanonicalProviderKey { get; }
    internal RowReadGeneration? ReadGeneration { get; init; }
}

/// <summary>
/// Owned finite result from a source-row loader. Implementations must finish and dispose any backend
/// cursor before constructing this result; no reader lifetime escapes through the neutral contract.
/// </summary>
internal sealed class SourceRowLoadResult
{
    // The dedicated crossover benchmark keeps linear scans decisively ahead through 16 rows;
    // hash sets reach parity around 32 while adding 1.7-4 KB per validation at those sizes.
    internal const int LinearValidationThreshold = 16;

    internal SourceRowLoadResult(
        SourcePrimaryKeyRowRequest request,
        IEnumerable<CanonicalProviderValueRow> rows)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new Builder(
            request,
            rows is ICollection<CanonicalProviderValueRow> collection ? collection.Count : 0);
        foreach (var candidate in rows)
            builder.Add(candidate, nameof(rows));

        var built = builder.Build();
        Request = built.Request;
        Rows = built.Rows;
    }

    private SourceRowLoadResult(
        SourcePrimaryKeyRowRequest request,
        List<LoadedCanonicalRow> ownedRows)
    {
        Request = request;
        Rows = new ReadOnlyListSlice<LoadedCanonicalRow>(ownedRows);
    }

    internal SourcePrimaryKeyRowRequest Request { get; }
    internal TableDefinition Table => Request.Table;
    internal ReadOnlyListSlice<LoadedCanonicalRow> Rows { get; }

    internal struct Builder
    {
        private readonly SourcePrimaryKeyRowRequest request;
        private readonly List<LoadedCanonicalRow> rows;
        private HashSet<DataLinqKey>? requestedKeys;
        private HashSet<DataLinqKey>? returnedKeys;
        private bool built;

        internal Builder(SourcePrimaryKeyRowRequest request, int capacity = 0)
        {
            this.request = request ?? throw new ArgumentNullException(nameof(request));
            rows = capacity > 0
                ? new List<LoadedCanonicalRow>(capacity)
                : [];
        }

        internal void Add(CanonicalProviderValueRow? candidate, string parameterName = "rows")
        {
            if (built)
                throw new InvalidOperationException("A source-row result builder cannot be reused after Build.");

            var index = rows.Count;
            var row = candidate
                ?? throw new ArgumentException(
                    $"Source-row result for table '{request.Table.DbName}' contains a null row at index {index}.",
                    parameterName);

            if (!ReferenceEquals(row.Table, request.Table))
            {
                throw new ArgumentException(
                    $"Source-row result for table '{request.Table.DbName}' contains a row from table '{row.Table.DbName}' at index {index}.",
                    parameterName);
            }

            if (!row.TryCreateCanonicalPrimaryKey(out var rowKey))
            {
                throw new ArgumentException(
                    $"Source-row result for table '{request.Table.DbName}' contains a row without a canonical primary key at index {index}.",
                    parameterName);
            }

            if (!ContainsRequestedKey(rowKey))
            {
                throw new ArgumentException(
                    $"Source-row result for table '{request.Table.DbName}' contains an unrequested primary key at index {index}.",
                    parameterName);
            }

            if (ContainsReturnedKey(rowKey))
            {
                throw new ArgumentException(
                    $"Source-row result for table '{request.Table.DbName}' contains duplicate primary key '{rowKey}' at index {index}.",
                    parameterName);
            }

            rows.Add(new LoadedCanonicalRow(row, rowKey));
        }

        internal SourceRowLoadResult Build()
        {
            if (built)
                throw new InvalidOperationException("A source-row result builder can build only once.");

            built = true;
            return new SourceRowLoadResult(request, rows);
        }

        private bool ContainsRequestedKey(DataLinqKey key)
        {
            if (request.CanonicalProviderKeys.Length <= LinearValidationThreshold)
            {
                for (var index = 0; index < request.CanonicalProviderKeys.Length; index++)
                {
                    if (request.CanonicalProviderKeys[index].Equals(key))
                        return true;
                }

                return false;
            }

            requestedKeys ??= new HashSet<DataLinqKey>(request.CanonicalProviderKeys);
            return requestedKeys.Contains(key);
        }

        private bool ContainsReturnedKey(DataLinqKey key)
        {
            if (rows.Count < LinearValidationThreshold)
            {
                for (var index = 0; index < rows.Count; index++)
                {
                    if (rows[index].CanonicalProviderKey.Equals(key))
                        return true;
                }

                return false;
            }

            if (returnedKeys is null)
            {
                returnedKeys = new HashSet<DataLinqKey>();
                for (var index = 0; index < rows.Count; index++)
                    returnedKeys.Add(rows[index].CanonicalProviderKey);
            }

            return !returnedKeys.Add(key);
        }
    }
}

/// <summary>
/// Owned finite result from an index-row loader. The result validates row ownership and canonical
/// primary-key identity, but deliberately does not compare returned index cells with the request:
/// matching equality belongs to the selected backend.
/// </summary>
internal sealed class SourceIndexRowLoadResult
{
    internal SourceIndexRowLoadResult(
        SourceIndexRowRequest request,
        IEnumerable<CanonicalProviderValueRow> rows)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new Builder(
            request,
            rows is ICollection<CanonicalProviderValueRow> collection ? collection.Count : 0);
        foreach (var candidate in rows)
            builder.Add(candidate, nameof(rows));

        var built = builder.Build();
        Request = built.Request;
        Rows = built.Rows;
    }

    private SourceIndexRowLoadResult(
        SourceIndexRowRequest request,
        List<LoadedCanonicalRow> ownedRows)
    {
        Request = request;
        Rows = new ReadOnlyListSlice<LoadedCanonicalRow>(ownedRows);
    }

    internal SourceIndexRowRequest Request { get; }
    internal TableDefinition Table => Request.Table;
    internal ColumnIndex Index => Request.Index;
    internal ReadOnlyListSlice<LoadedCanonicalRow> Rows { get; }

    internal struct Builder
    {
        private readonly SourceIndexRowRequest request;
        private readonly List<LoadedCanonicalRow> rows;
        private HashSet<DataLinqKey>? returnedKeys;
        private bool built;

        internal Builder(SourceIndexRowRequest request, int capacity = 0)
        {
            this.request = request ?? throw new ArgumentNullException(nameof(request));
            rows = capacity > 0
                ? new List<LoadedCanonicalRow>(capacity)
                : [];
        }

        internal void Add(CanonicalProviderValueRow? candidate, string parameterName = "rows")
        {
            if (built)
                throw new InvalidOperationException("A source-index result builder cannot be reused after Build.");

            var index = rows.Count;
            var row = candidate
                ?? throw new ArgumentException(
                    $"Source-index row result for table '{request.Table.DbName}' contains a null row at index {index}.",
                    parameterName);

            if (!ReferenceEquals(row.Table, request.Table))
            {
                throw new ArgumentException(
                    $"Source-index row result for table '{request.Table.DbName}' contains a row from table '{row.Table.DbName}' at index {index}.",
                    parameterName);
            }

            if (!row.TryCreateCanonicalPrimaryKey(out var rowKey))
            {
                throw new ArgumentException(
                    $"Source-index row result for table '{request.Table.DbName}' contains a row without a canonical primary key at index {index}.",
                    parameterName);
            }

            if (ContainsReturnedKey(rowKey))
            {
                throw new ArgumentException(
                    $"Source-index row result for table '{request.Table.DbName}' contains duplicate primary key '{rowKey}' at index {index}.",
                    parameterName);
            }

            rows.Add(new LoadedCanonicalRow(row, rowKey));
        }

        internal SourceIndexRowLoadResult Build()
        {
            if (built)
                throw new InvalidOperationException("A source-index result builder can build only once.");

            built = true;
            return new SourceIndexRowLoadResult(request, rows);
        }

        private bool ContainsReturnedKey(DataLinqKey key)
        {
            if (rows.Count < SourceRowLoadResult.LinearValidationThreshold)
            {
                for (var index = 0; index < rows.Count; index++)
                {
                    if (rows[index].CanonicalProviderKey.Equals(key))
                        return true;
                }

                return false;
            }

            if (returnedKeys is null)
            {
                returnedKeys = new HashSet<DataLinqKey>();
                for (var index = 0; index < rows.Count; index++)
                    returnedKeys.Add(rows[index].CanonicalProviderKey);
            }

            return !returnedKeys.Add(key);
        }
    }
}

/// <summary>
/// Backend-neutral source-row loader. Implementations check request cancellation before backend work
/// and at bounded work intervals, and return only owned canonical provider rows.
/// </summary>
internal interface ISourceRowLoader
{
    CanonicalProviderValueRow? LoadSingle(
        TableDefinition table,
        in DataLinqKey canonicalProviderKey,
        CancellationToken cancellationToken = default);

    SourceRowLoadResult Load(SourcePrimaryKeyRowRequest request);
}

/// <summary>
/// Optional backend-neutral loader for full rows selected by an index value. Implementations own
/// backend cursor lifetime and matching semantics and return only buffered canonical provider rows.
/// </summary>
internal interface ISourceIndexRowLoader
{
    SourceIndexRowLoadResult Load(SourceIndexRowRequest request);
}
