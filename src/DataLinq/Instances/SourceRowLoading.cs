using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using DataLinq.Metadata;

namespace DataLinq.Instances;

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
    {
        Table = table ?? throw new ArgumentNullException(nameof(table));
        ArgumentNullException.ThrowIfNull(canonicalProviderKeys);

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

        var keys = ImmutableArray.CreateRange(canonicalProviderKeys);
        if (keys.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A primary-key row request requires at least one canonical provider key.",
                nameof(canonicalProviderKeys));
        }

        for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            ValidateCanonicalKey(table, keys[keyIndex], keyIndex, nameof(canonicalProviderKeys));

        CanonicalProviderKeys = keys;
        CancellationToken = cancellationToken;
    }

    internal TableDefinition Table { get; }
    internal ImmutableArray<DataLinqKey> CanonicalProviderKeys { get; }
    internal CancellationToken CancellationToken { get; }

    internal void ThrowIfCancellationRequested() =>
        CancellationToken.ThrowIfCancellationRequested();

    private static void ValidateCanonicalKey(
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
/// Owned finite result from a source-row loader. Implementations must finish and dispose any backend
/// cursor before constructing this result; no reader lifetime escapes through the neutral contract.
/// </summary>
internal sealed class SourceRowLoadResult
{
    internal SourceRowLoadResult(
        SourcePrimaryKeyRowRequest request,
        IEnumerable<CanonicalProviderValueRow> rows)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        ArgumentNullException.ThrowIfNull(rows);

        var snapshot = ImmutableArray.CreateRange(rows);
        var requestedKeys = new HashSet<DataLinqKey>(request.CanonicalProviderKeys);
        var returnedKeys = new HashSet<DataLinqKey>();
        for (var index = 0; index < snapshot.Length; index++)
        {
            var row = snapshot[index]
                ?? throw new ArgumentException(
                    $"Source-row result for table '{request.Table.DbName}' contains a null row at index {index}.",
                    nameof(rows));

            if (!ReferenceEquals(row.Table, request.Table))
            {
                throw new ArgumentException(
                    $"Source-row result for table '{request.Table.DbName}' contains a row from table '{row.Table.DbName}' at index {index}.",
                    nameof(rows));
            }

            if (!row.TryCreateCanonicalPrimaryKey(out var rowKey))
            {
                throw new ArgumentException(
                    $"Source-row result for table '{request.Table.DbName}' contains a row without a canonical primary key at index {index}.",
                    nameof(rows));
            }

            if (!requestedKeys.Contains(rowKey))
            {
                throw new ArgumentException(
                    $"Source-row result for table '{request.Table.DbName}' contains an unrequested primary key at index {index}.",
                    nameof(rows));
            }

            if (!returnedKeys.Add(rowKey))
            {
                throw new ArgumentException(
                    $"Source-row result for table '{request.Table.DbName}' contains duplicate primary key '{rowKey}' at index {index}.",
                    nameof(rows));
            }
        }

        Rows = snapshot;
    }

    internal SourcePrimaryKeyRowRequest Request { get; }
    internal TableDefinition Table => Request.Table;
    internal ImmutableArray<CanonicalProviderValueRow> Rows { get; }
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
        Request = request ?? throw new ArgumentNullException(nameof(request));
        ArgumentNullException.ThrowIfNull(rows);

        var snapshot = ImmutableArray.CreateRange(rows);
        var returnedKeys = new HashSet<DataLinqKey>();
        for (var index = 0; index < snapshot.Length; index++)
        {
            var row = snapshot[index]
                ?? throw new ArgumentException(
                    $"Source-index row result for table '{request.Table.DbName}' contains a null row at index {index}.",
                    nameof(rows));

            if (!ReferenceEquals(row.Table, request.Table))
            {
                throw new ArgumentException(
                    $"Source-index row result for table '{request.Table.DbName}' contains a row from table '{row.Table.DbName}' at index {index}.",
                    nameof(rows));
            }

            if (!row.TryCreateCanonicalPrimaryKey(out var rowKey))
            {
                throw new ArgumentException(
                    $"Source-index row result for table '{request.Table.DbName}' contains a row without a canonical primary key at index {index}.",
                    nameof(rows));
            }

            if (!returnedKeys.Add(rowKey))
            {
                throw new ArgumentException(
                    $"Source-index row result for table '{request.Table.DbName}' contains duplicate primary key '{rowKey}' at index {index}.",
                    nameof(rows));
            }
        }

        Rows = snapshot;
    }

    internal SourceIndexRowRequest Request { get; }
    internal TableDefinition Table => Request.Table;
    internal ColumnIndex Index => Request.Index;
    internal ImmutableArray<CanonicalProviderValueRow> Rows { get; }
}

/// <summary>
/// Backend-neutral source-row loader. Implementations check request cancellation before backend work
/// and at bounded work intervals, and return only owned canonical provider rows.
/// </summary>
internal interface ISourceRowLoader
{
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
