using System;
using DataLinq.Core.Factories;
using DataLinq.Metadata;

namespace DataLinq.Instances;

/// <summary>
/// A dense, table-ordinal row of canonical provider CLR values.
/// </summary>
/// <remarks>
/// This is deliberately separate from <see cref="IRowData"/>, whose values are public model values.
/// SQL physical values must be decoded before entering this buffer, and scalar-converter model values
/// must not enter it. The buffer owns a copy of the supplied slots and of mutable byte-array cells.
/// </remarks>
internal sealed class CanonicalProviderValueRow
{
    private readonly object?[] values;
    private readonly int[] estimatedValueSizes;

    private CanonicalProviderValueRow(
        TableDefinition table,
        object?[] values,
        int[] estimatedValueSizes,
        int estimatedPayloadSize)
    {
        Table = table;
        this.values = values;
        this.estimatedValueSizes = estimatedValueSizes;
        EstimatedPayloadSize = estimatedPayloadSize;
    }

    public TableDefinition Table { get; }
    public int Count => values.Length;

    /// <summary>
    /// Gets the stable canonical payload estimate. Model-valued rows use it only as a per-column fallback for
    /// converter-backed wrapper types whose model payload cannot be estimated directly.
    /// </summary>
    internal int EstimatedPayloadSize { get; }

    public object? this[int columnOrdinal]
    {
        get
        {
            if ((uint)columnOrdinal >= (uint)values.Length)
                throw new ArgumentOutOfRangeException(nameof(columnOrdinal));

            return CopyMutableValue(values[columnOrdinal]);
        }
    }

    public object? this[ColumnDefinition column]
    {
        get
        {
            ValidateColumn(Table, column);
            return CopyMutableValue(values[column.Index]);
        }
    }

    internal static CanonicalProviderValueRow Create(TableDefinition table, ReadOnlySpan<object?> canonicalValues)
    {
        ArgumentNullException.ThrowIfNull(table);

        ValidateTableLayout(table);

        if (canonicalValues.Length != table.ColumnCount)
        {
            throw new ArgumentException(
                $"Canonical provider row for table '{table.DbName}' requires exactly {table.ColumnCount} table-ordinal values, but received {canonicalValues.Length}. Missing cells must not be represented as null.",
                nameof(canonicalValues));
        }

        var copiedValues = new object?[canonicalValues.Length];
        var estimatedValueSizes = new int[canonicalValues.Length];
        var estimatedPayloadSize = 0;

        for (var ordinal = 0; ordinal < canonicalValues.Length; ordinal++)
        {
            var column = table.Columns[ordinal];
            var value = canonicalValues[ordinal];

            ValidateValue(column, value, useProviderType: true, nameof(canonicalValues));

            var copiedValue = CopyMutableValue(value);
            copiedValues[ordinal] = copiedValue;
            var estimatedValueSize = EstimateCanonicalValueSize(column, copiedValue);
            estimatedValueSizes[ordinal] = estimatedValueSize;

            try
            {
                estimatedPayloadSize = checked(estimatedPayloadSize + estimatedValueSize);
            }
            catch (OverflowException exception)
            {
                throw new InvalidOperationException(
                    $"Canonical provider payload estimate overflowed while reading column '{table.DbName}.{column.DbName}'.",
                    exception);
            }
        }

        return new CanonicalProviderValueRow(table, copiedValues, estimatedValueSizes, estimatedPayloadSize);
    }

    internal int GetEstimatedValueSize(int columnOrdinal)
    {
        if ((uint)columnOrdinal >= (uint)estimatedValueSizes.Length)
            throw new ArgumentOutOfRangeException(nameof(columnOrdinal));

        return estimatedValueSizes[columnOrdinal];
    }

    internal bool TryCreateCanonicalPrimaryKey(out DataLinqKey key)
    {
        var primaryKeyColumns = Table.PrimaryKeyColumns;
        if (primaryKeyColumns.Count == 0)
        {
            key = DataLinqKey.Null;
            return false;
        }

        var keyValues = new object?[primaryKeyColumns.Count];
        for (var index = 0; index < primaryKeyColumns.Count; index++)
        {
            var column = primaryKeyColumns[index];
            keyValues[index] = this[column];
            if (keyValues[index] is null)
            {
                throw new InvalidOperationException(
                    $"Canonical provider row for table '{Table.DbName}' contains null primary-key component '{column.DbName}'.");
            }
        }

        key = DataLinqKey.FromValues(keyValues);
        return true;
    }

    internal static void ValidateModelValue(ColumnDefinition column, object? value, string parameterName)
    {
        ValidateValue(column, value, useProviderType: false, parameterName);
    }

    internal static void ValidateCanonicalValue(ColumnDefinition column, object? value, string parameterName)
    {
        ValidateValue(column, value, useProviderType: true, parameterName);
    }

    internal static object? CopyMutableValue(object? value) =>
        value is byte[] bytes ? (byte[])bytes.Clone() : value;

    private static void ValidateTableLayout(TableDefinition table)
    {
        if (!table.IsFrozen)
        {
            throw new InvalidOperationException(
                $"Canonical provider rows require frozen metadata, but table '{table.DbName}' is still mutable.");
        }

        for (var ordinal = 0; ordinal < table.ColumnCount; ordinal++)
        {
            var column = table.Columns[ordinal];
            if (!ReferenceEquals(column.Table, table) || column.Index != ordinal)
            {
                throw new InvalidOperationException(
                    $"Table '{table.DbName}' has an invalid canonical row layout at ordinal {ordinal}: column '{column.DbName}' reports index {column.Index} or belongs to another table.");
            }
        }
    }

    private static void ValidateColumn(TableDefinition table, ColumnDefinition column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (!ReferenceEquals(column.Table, table) ||
            (uint)column.Index >= (uint)table.ColumnCount ||
            !ReferenceEquals(table.Columns[column.Index], column))
        {
            throw new ArgumentException(
                $"Column '{column.DbName}' does not belong to canonical provider row table '{table.DbName}'.",
                nameof(column));
        }
    }

    private static void ValidateValue(
        ColumnDefinition column,
        object? value,
        bool useProviderType,
        string parameterName)
    {
        var representation = useProviderType ? "canonical provider" : "model";
        var declaredType = useProviderType ? column.ProviderClrType : column.ModelClrType;

        if (declaredType is null)
        {
            var declaredTypeName = useProviderType ? column.ProviderCsType.Name : column.ModelCsType.Name;
            throw new InvalidOperationException(
                $"Column '{column.Table.DbName}.{column.DbName}' declares {representation} type '{declaredTypeName}', but its runtime CLR type metadata is unresolved.");
        }

        if (ReferenceEquals(value, DBNull.Value))
        {
            throw new ArgumentException(
                $"Column '{column.Table.DbName}.{column.DbName}' received DBNull.Value. Canonical and model rows represent database NULL with null.",
                parameterName);
        }

        if (value is null)
        {
            var nullable = useProviderType ? column.Nullable : column.ValueProperty.CsNullable;
            if (!nullable)
            {
                throw new ArgumentException(
                    $"Non-nullable column '{column.Table.DbName}.{column.DbName}' cannot contain a null {representation} value.",
                    parameterName);
            }

            return;
        }

        var expectedType = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        var hasCompatibleType = useProviderType
            ? value.GetType() == expectedType
            : expectedType.IsInstanceOfType(value);
        if (!hasCompatibleType)
        {
            throw new ArgumentException(
                $"Column '{column.Table.DbName}.{column.DbName}' requires {representation} CLR type '{expectedType.FullName}', but received '{value.GetType().FullName}'. Numeric widening and model/provider substitution are not canonicalization.",
                parameterName);
        }
    }

    private static int EstimateCanonicalValueSize(ColumnDefinition column, object? value)
    {
        if (value is null)
            return 0;

        if (value is string text)
            return checked((text.Length * sizeof(char)) + sizeof(int));

        if (value is byte[] bytes)
            return bytes.Length;

        var providerType = column.ProviderClrType
            ?? throw new InvalidOperationException(
                $"Column '{column.Table.DbName}.{column.DbName}' has no runtime canonical provider CLR type metadata.");

        if (MetadataTypeConverter.CsTypeSize(providerType) is { } knownSize)
            return knownSize;

        // The cache estimator is deliberately stable rather than a CLR object walker. Known scalar,
        // string, and binary types are measured above; an otherwise valid canonical scalar gets a
        // fixed boxed-value proxy instead of making row validity depend on estimator coverage.
        return IntPtr.Size * 2;
    }
}
