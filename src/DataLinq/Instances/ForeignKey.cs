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
        Index = index;
        Data = data;
    }

    /// <summary>
    /// Gets the column that the foreign key references.
    /// </summary>
    public ColumnIndex Index { get; }

    /// <summary>
    /// Gets the data that the foreign key references.
    /// </summary>
    public object[] Data { get; }

    public IEnumerable<(Column column, object data)> GetColumns()
    {
        for (int i = 0; i < Index.Columns.Count; i++)
            yield return (Index.Columns[i], Data[i]);
    }

    public IEnumerable<(string columnDbName, object data)> GetData()
    {
        for (int i = 0; i < Index.Columns.Count; i++)
            yield return (Index.Columns[i].DbName, Data[i]);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="other">The object to compare with the current object.</param>
    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
    public bool Equals(ForeignKey other)
    {
        if (ReferenceEquals(null, other))
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return Index == other.Index && Data.SequenceEqual(other.Data);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
            return false;
        if (ReferenceEquals(this, obj))
            return true;
        if (obj.GetType() != typeof(ForeignKey))
            return false;

        return Equals((ForeignKey)obj);
    }

    /// <summary>
    /// Serves as the default hash function.
    /// </summary>
    /// <returns>A hash code for the current object.</returns>
    //public override int GetHashCode()
    //{
    //    unchecked
    //    {
    //        int hash = 17;

    //        hash = hash * 31 + Index.GetHashCode();
    //        hash = hash * 31 + Data.GetHashCode();

    //        return hash;
    //    }
    //}

    public override int GetHashCode()
    {
        unchecked
        {
            if (Data == null)
            {
                return 0;
            }
            int hash = 17;
            for (int i = 0; i < Data.Length; i++)
            {
                // Use a fixed hash code for null items, e.g., 0.
                // If item is not null, use item's hash code.
                hash = hash * 31 + (Data[i]?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }

    public override string ToString()
    {
        return $"{Index} = {Data.ToJoinedString(", ")}";
    }
}