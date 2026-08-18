using System;
using System.Collections.Generic;
using DataLinq.Core.Factories;
using DataLinq.Metadata;

namespace DataLinq.Instances;

public interface IRowData
{
    TableDefinition Table { get; }

    object? this[ColumnDefinition column] { get; }
    object? this[int columnIndex] { get; }

    object? GetValue(ColumnDefinition column);
    object? GetValue(int columnIndex);

    IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns);

    IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues();

    IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(IEnumerable<ColumnDefinition> columns);
}

public sealed class RowData : IRowData, IEquatable<RowData>
{
    private readonly object?[] data;
    private readonly bool[]? populatedColumns;

    private RowData(TableDefinition table, object?[] data, int size)
    {
        Table = table;
        this.data = data;
        populatedColumns = null;
        Size = size;
    }

    public RowData(IDataLinqDataReader reader, TableDefinition table, IReadOnlyList<ColumnDefinition> columns, bool hasIndexedColumns)
        : this(reader, table, columns, hasIndexedColumns, "reader:row-data")
    {
    }

    internal RowData(
        IDataLinqDataReader reader,
        TableDefinition table,
        IReadOnlyList<ColumnDefinition> columns,
        bool hasIndexedColumns,
        string sourceName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(columns);
        ProviderRowMaterializer.ValidateSourceName(sourceName);

        Table = table;

        // Initialize array sized to the total number of columns in the table definition
        // This allows O(1) access by Column.Index
        data = new object?[table.ColumnCount];
        if (columns.Count < table.ColumnCount)
        {
            populatedColumns = new bool[table.ColumnCount];
            for (var index = 0; index < columns.Count; index++)
                populatedColumns[columns[index].Index] = true;
        }

        // Read values based on the *requested* columns (which match the reader's ordinal order)
        // and place them into their correct slots in the dense array.
        Size = hasIndexedColumns
            ? ReadOrderedIndexReader(reader, columns, data, sourceName)
            : ReadUnorderedReader(reader, columns, data, sourceName);
    }

    /// <summary>
    /// Creates public model-valued row state from a complete canonical provider row and already-materialized
    /// model values. The provider row supplies the validated table layout and a per-column canonical
    /// payload fallback when a converter-backed model wrapper has no direct cache-size estimate.
    /// </summary>
    internal static RowData CreateTrusted(
        CanonicalProviderValueRow providerRow,
        ReadOnlySpan<object?> modelValues)
    {
        ArgumentNullException.ThrowIfNull(providerRow);

        if (modelValues.Length != providerRow.Count)
        {
            throw new ArgumentException(
                $"Model row for table '{providerRow.Table.DbName}' requires exactly {providerRow.Count} table-ordinal values, but received {modelValues.Length}.",
                nameof(modelValues));
        }

        var copiedValues = new object?[modelValues.Length];
        var size = 0;
        for (var ordinal = 0; ordinal < modelValues.Length; ordinal++)
        {
            var column = providerRow.Table.Columns[ordinal];
            var value = modelValues[ordinal];

            CanonicalProviderValueRow.ValidateModelValue(column, value, nameof(modelValues));
            copiedValues[ordinal] = CanonicalProviderValueRow.CopyMutableValue(value);
            size = checked(size + GetMaterializedValueSize(
                column,
                copiedValues[ordinal],
                providerRow,
                ordinal));
        }

        return new RowData(providerRow.Table, copiedValues, size);
    }

    /// <summary>
    /// Takes exclusive ownership of a complete, validated model-value buffer produced by the row
    /// materializer. The caller must not retain or mutate the array after this call.
    /// </summary>
    internal static RowData CreateMaterializedOwned(
        CanonicalProviderValueRow providerRow,
        object?[] modelValues,
        int size)
    {
        ArgumentNullException.ThrowIfNull(providerRow);
        ArgumentNullException.ThrowIfNull(modelValues);
        ArgumentOutOfRangeException.ThrowIfNegative(size);

        if (modelValues.Length != providerRow.Count)
        {
            throw new ArgumentException(
                $"Model row for table '{providerRow.Table.DbName}' requires exactly {providerRow.Count} table-ordinal values, but received {modelValues.Length}.",
                nameof(modelValues));
        }

        return new RowData(providerRow.Table, modelValues, size);
    }

    //protected Dictionary<ColumnDefinition, object?> Data { get; }

    public TableDefinition Table { get; }

    public int Size { get; }

    internal bool IsColumnPresent(int columnIndex) =>
        populatedColumns is null || populatedColumns[columnIndex];

    public object? this[ColumnDefinition column] => GetValue(column);
    public object? this[int columnIndex] => GetValue(columnIndex);

    public object? GetValue(ColumnDefinition column)
    {
        // Fast array access using the pre-calculated index
        return CanonicalProviderValueRow.CopyMutableValue(data[column.Index]);
    }

    public object? GetValue(int columnIndex)
    {
        return CanonicalProviderValueRow.CopyMutableValue(data[columnIndex]);
    }

    /// <summary>
    /// Borrows one model cell for immediate trusted runtime processing. The returned value must not
    /// be retained or mutated; public accessors detach mutable binary values from cached row state.
    /// </summary>
    internal object? GetBorrowedValue(int columnIndex)
    {
        if ((uint)columnIndex >= (uint)data.Length)
            throw new ArgumentOutOfRangeException(nameof(columnIndex));

        return data[columnIndex];
    }

    internal object? GetBorrowedValue(ColumnDefinition column)
    {
        ArgumentNullException.ThrowIfNull(column);
        return GetBorrowedValue(column.Index);
    }

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues()
    {
        for (var i = 0; i < Table.Columns.Count; i++)
        {
            var column = Table.Columns[i];
            yield return new KeyValuePair<ColumnDefinition, object?>(
                column,
                CanonicalProviderValueRow.CopyMutableValue(data[column.Index]));
        }
    }

    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(IEnumerable<ColumnDefinition> columns)
    {
        foreach (var column in columns)
            yield return new KeyValuePair<ColumnDefinition, object?>(column, GetValue(column));
    }

    public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns)
    {
        foreach (var column in columns)
            yield return GetValue(column);
    }

    private static int ReadOrderedIndexReader(
        IDataLinqDataReader reader,
        IReadOnlyList<ColumnDefinition> columns,
        object?[] data,
        string sourceName)
    {
        var size = 0;

        // Iterate using the span length. The reader ordinals 0..N match this span's order.
        for (int i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var value = ReadModelValue(reader, column, i, sourceName);
            size += GetSize(column, value); // Keep existing size calc logic
            data[column.Index] = value;
        }
        return size;
    }

    private static int ReadUnorderedReader(
        IDataLinqDataReader reader,
        IReadOnlyList<ColumnDefinition> columns,
        object?[] data,
        string sourceName)
    {
        var size = 0;

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var ordinal = reader.GetOrdinal(column.DbName);
            var value = ReadModelValue(reader, column, ordinal, sourceName);
            size += GetSize(column, value);

            data[column.Index] = value;
        }
        return size;
    }

    private static object? ReadModelValue(
        IDataLinqDataReader reader,
        ColumnDefinition column,
        int ordinal,
        string sourceName)
    {
        if (!reader.IsDbNull(ordinal))
            return reader.GetValue<object>(column, ordinal);

        // RowData is model-valued state, so validate both the database and model contracts here.
        // This protects the boundary even when a custom IDataLinqDataReader implementation does not.
        DataLinqNullabilityContract.EnsureModelAllowsSqlNull(column, sourceName);
        return null;
    }

    internal static int GetMaterializedValueSize(
        ColumnDefinition column,
        object? value,
        CanonicalProviderValueRow providerRow,
        int columnOrdinal)
    {
        ArgumentNullException.ThrowIfNull(providerRow);

        return TryGetSize(column, value)
            ?? providerRow.GetEstimatedValueSize(columnOrdinal);
    }

    private static int GetSize(ColumnDefinition column, object? value)
        => TryGetSize(column, value)
            ?? throw new NotImplementedException($"Size for type '{column.ValueProperty.CsType}' not implemented");

    private static int? TryGetSize(ColumnDefinition column, object? value)
    {
        if (value == null)
            return 0;

        if (column.ValueProperty.CsSize.HasValue)
            return column.ValueProperty.CsSize.Value;

        if (column.ValueProperty.CsType.Type is { } runtimeType &&
            MetadataTypeConverter.CsTypeSize(runtimeType) is { } runtimeSize)
            return runtimeSize;

        if (column.ValueProperty.CsType.ModelCsType == ModelCsType.Enum || value is Enum)
            return MetadataTypeConverter.CsTypeSize("enum")!.Value;

        if (column.ValueProperty.CsType.Type == typeof(string) && value is string s)
            return s.Length * sizeof(char) + sizeof(int);

        if (column.ValueProperty.CsType.Type == typeof(byte[]) && value is byte[] b)
            return b.Length;

        return null;
    }

    public bool Equals(RowData? other)
    {
        if (other == null) return false;
        if (data.Length != other.data.Length) return false;

        for (int i = 0; i < data.Length; i++)
        {
            if (IsColumnPresent(i) != other.IsColumnPresent(i)) return false;
            if (!object.Equals(data[i], other.data[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj)
    {
        return obj is RowData other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();

        // Missing partial-row cells and selected SQL NULL values are observably different state.
        for (var index = 0; index < data.Length; index++)
        {
            hash.Add(IsColumnPresent(index));
            hash.Add(data[index]);
        }

        return hash.ToHashCode();
    }
}
