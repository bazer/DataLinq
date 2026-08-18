using System;
using System.Collections.Generic;
using DataLinq.Metadata;

namespace DataLinq.Instances;

/// <summary>
/// Constructor-only bridge for a materialized row and its source-validated canonical key. Immutable
/// base construction unwraps the row and captures the key; this carrier is never retained by a model.
/// </summary>
internal sealed class KnownCanonicalPrimaryKeyRowData : IRowData
{
    internal KnownCanonicalPrimaryKeyRowData(
        RowData rowData,
        DataLinqKey canonicalProviderKey)
    {
        RowData = rowData ?? throw new ArgumentNullException(nameof(rowData));
        CanonicalProviderKey = canonicalProviderKey;
    }

    internal RowData RowData { get; }
    internal DataLinqKey CanonicalProviderKey { get; }

    public TableDefinition Table => RowData.Table;
    public object? this[ColumnDefinition column] => RowData[column];
    public object? this[int columnIndex] => RowData[columnIndex];
    public object? GetValue(ColumnDefinition column) => RowData.GetValue(column);
    public object? GetValue(int columnIndex) => RowData.GetValue(columnIndex);
    public IEnumerable<object?> GetValues(IEnumerable<ColumnDefinition> columns) =>
        RowData.GetValues(columns);
    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues() =>
        RowData.GetColumnAndValues();
    public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetColumnAndValues(
        IEnumerable<ColumnDefinition> columns) =>
        RowData.GetColumnAndValues(columns);
}
