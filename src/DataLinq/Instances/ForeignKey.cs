using System;
using System.Collections.Generic;
using System.Linq;
using DataLinq.Extensions.Helpers;
using DataLinq.Metadata;

namespace DataLinq.Instances;

/// <summary>
/// Represents a foreign key in a database table.
/// </summary>
public class ForeignKey
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ForeignKey"/> class.
    /// </summary>
    /// <param name="index">The column that the foreign key references.</param>
    /// <param name="data">The data that the foreign key references.</param>
    public ForeignKey(ColumnIndex index, object[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Index = index;
        Data = data;
        cachedHashCode = ComputeHashCode();
    }

    /// <summary>
    /// Gets the column that the foreign key references.
    /// </summary>
    public ColumnIndex Index { get; }

    /// <summary>
    /// Gets the data that the foreign key references.
    /// </summary>
    public object[] Data { get; }
    private readonly int cachedHashCode;

    public IEnumerable<(Column column, object data)> GetColumns()
    {
        for (int i = 0; i < Index.Columns.Count; i++)
            yield return (Index.Columns[i], Data[i]);
    }

    public IEnumerable<(string columnDbName, object? data)> GetData()
    {
        for (int i = 0; i < Index.Columns.Count; i++)
            yield return (Index.Columns[i].DbName, Data[i]);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="other">The object to compare with the current object.</param>
    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
    //public bool Equals(ForeignKey other)
    //{
    //    if (ReferenceEquals(null, other))
    //        return false;
    //    if (ReferenceEquals(this, other))
    //        return true;

    //    return Index == other.Index && Data.SequenceEqual(other.Data);
    //}

    public bool Equals(ForeignKey? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return Index == other.Index && ArraysEqual(Data, other.Data);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as ForeignKey);
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    public override int GetHashCode()
    {
        return cachedHashCode;
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

    public override string ToString()
    {
        return $"{Index} = {Data.ToJoinedString(", ")}";
    }
}