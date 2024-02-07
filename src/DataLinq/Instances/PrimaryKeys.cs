using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Metadata;

namespace DataLinq.Instances;

/// <summary>
/// Represents the primary keys of a row.
/// </summary>
public class PrimaryKeys : IEquatable<PrimaryKeys>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PrimaryKeys"/> class.
    /// </summary>
    /// <param name="reader">The data reader.</param>
    /// <param name="table">The table metadata.</param>
    public PrimaryKeys(IDataLinqDataReader reader, TableMetadata table)
    {
        Data = CheckData(ReadReader(reader, table).ToArray(), table);
        cachedHashCode = ComputeHashCode();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrimaryKeys"/> class.
    /// </summary>
    /// <param name="row">The row data.</param>
    public PrimaryKeys(RowData row)
    {
        Data = CheckData(ReadRow(row).ToArray(), row.Table);
        cachedHashCode = ComputeHashCode();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrimaryKeys"/> class.
    /// </summary>
    /// <param name="data">The primary key data.</param>
    public PrimaryKeys(params object[] data)
    {
        Data = CheckData(data);
        cachedHashCode = ComputeHashCode();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PrimaryKeys"/> class.
    /// </summary>
    /// <param name="data">The primary key data.</param>
    public PrimaryKeys(IEnumerable<object> data)
    {
        Data = CheckData(data.ToArray());
        cachedHashCode = ComputeHashCode();
    }

    private object[] CheckData(object?[] data, TableMetadata? table = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        //for (int i = 0; i < data.Length; i++)
        //{
        //    if (data[i] is null)
        //        throw new ArgumentNullException(nameof(data), "Data contains null values.");
        //}

        if (table != null && data.Length != table.PrimaryKeyColumns.Length)
            throw new ArgumentException($"The number of primary key values ({data.Length}) does not match the number of primary key columns ({table.PrimaryKeyColumns.Length}).");

        return data!;
    }

    /// <summary>
    /// Gets the primary key data.
    /// </summary>
    public object?[] Data { get; }
    private readonly int cachedHashCode;

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="other">The object to compare with the current object.</param>
    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
    public bool Equals(PrimaryKeys? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return ArraysEqual(Data, other.Data);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as PrimaryKeys);
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode()
    {
        return cachedHashCode;
        //unchecked
        //{
        //    if (Data == null)
        //    {
        //        return 0;
        //    }
        //    int hash = 17;
        //    for (int i = 0; i < Data.Length; i++)
        //    {
        //        // Use a fixed hash code for null items, e.g., 0.
        //        // If item is not null, use item's hash code.
        //        hash = hash * 31 + (Data[i]?.GetHashCode() ?? 0);
        //    }
        //    return hash;
        //}
    }

    private int ComputeHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (var obj in Data)
            {
                hash = hash * 31 + (obj?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }

    static bool ArraysEqual<T>(T[] a1, T[] a2)
    {
        // If either array is null or lengths are different, return false.
        if (a1 == null || a2 == null || a1.Length != a2.Length)
            return false;

        for (int i = 0; i < a1.Length; i++)
        {
            // Use the static Object.Equals method to compare the elements
            // which safely handles nulls.
            if (!Object.Equals(a1[i], a2[i]))
                return false;
        }

        // All checks passed, arrays are equal.
        return true;
    }

    private static IEnumerable<object?> ReadReader(IDataLinqDataReader reader, TableMetadata table)
    {
        foreach (var column in table.PrimaryKeyColumns)
            yield return reader.ReadColumn(column);
    }

    private static IEnumerable<object> ReadRow(RowData row)
    {
        foreach (var column in row.Table.PrimaryKeyColumns)
            yield return row.GetValue(column);
    }
}