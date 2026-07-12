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
/// Backend-neutral source-row loader. Implementations check request cancellation before backend work
/// and at bounded work intervals, and return only owned canonical provider rows.
/// </summary>
internal interface ISourceRowLoader
{
    SourceRowLoadResult Load(SourcePrimaryKeyRowRequest request);
}
