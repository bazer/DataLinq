using System;
using System.Linq;

namespace DataLinq.Instances;

public interface IKey
{
    public object?[] Values { get; }
}

public readonly record struct NullKey() : IKey, IEquatable<NullKey>
{
    public object?[] Values => [null];

    public bool Equals(NullKey other) =>
        true;

    public override int GetHashCode() =>
        0571049712;
}

public readonly record struct ObjectKey(object Value) : IKey, IEquatable<ObjectKey>
{
    public object?[] Values => [Value];

    public bool Equals(ObjectKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct IntKey(int Value) : IKey, IEquatable<IntKey>
{
    public object?[] Values => [Value];

    public bool Equals(IntKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct UInt64Key(ulong Value) : IKey, IEquatable<UInt64Key>
{
    public object?[] Values => [Value];

    public bool Equals(UInt64Key other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct Int64Key(long Value) : IKey, IEquatable<Int64Key>
{
    public object?[] Values => [Value];

    public bool Equals(Int64Key other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct GuidKey(Guid Value) : IKey, IEquatable<GuidKey>
{
    public object?[] Values => [Value];

    public bool Equals(GuidKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct StringKey(string Value) : IKey, IEquatable<StringKey>
{
    public object?[] Values => [Value];

    public bool Equals(StringKey other) =>
        Value == other.Value;

    public override int GetHashCode() =>
        Value.GetHashCode();
}

public readonly record struct CompositeKey : IKey, IEquatable<CompositeKey>
{
    public readonly int cachedHashCode;
    public object?[] Values => values;
    private readonly object?[] values;

    public CompositeKey(object?[] values)
    {
        this.values = values;
        this.cachedHashCode = ComputeHashCode(values);
    }

    public bool Equals(CompositeKey other) =>
        values.SequenceEqual(other.values);

    public override int GetHashCode() =>
        cachedHashCode;

    public static int ComputeHashCode(object?[] values)
    {
        var hash = new HashCode();

        foreach (var val in values.Where(x => x != null))
            hash.Add(val);
        
        return hash.ToHashCode();
    }
}


//public readonly record struct CompositeKey : IKey, IEquatable<CompositeKey>
//{
//    public IEnumerable<object?> Values
//    {
//        get
//        {
//            for (int i=0; i<Lengths.Length; i++)
//            {
//                yield return DataReader.ConvertBytesToType(Data.Slice(i > 0 ? Lengths.Span[i-1] : 0, Lengths.Span[i]));
//            }


//        }
//    }

//    public Memory<byte> Data { get; }
//    public Memory<int> Lengths { get; }

//    public readonly int cachedHashCode;
//    public CompositeKey(Memory<byte> data, Memory<int> lengths)
//    {
//        this.cachedHashCode = ComputeHashCode(data.Span, lengths.Span);
//        Data = data;
//        Lengths = lengths;
//    }

//    public bool Equals(CompositeKey other)
//    {
//        return Data.Span.SequenceEqual(other.Data.Span) && Lengths.Span.SequenceEqual(other.Lengths.Span);
//    }

//    public override int GetHashCode()
//    {
//        return cachedHashCode;
//    }

//    public static int ComputeHashCode(ReadOnlySpan<byte> buffer, ReadOnlySpan<int> lengths)
//    {
//        var hash = new HashCode();
//        hash.AddBytes(buffer);
//        hash.Add(lengths);

//        return hash.ToHashCode();
//    }
//}

//public abstract class Keys
//{

//}

///// <summary>
///// Represents the primary keys of a row.
///// </summary>
//public class PrimaryKeys : Keys, IEquatable<PrimaryKeys>
//{
//    /// <summary>
//    /// Initializes a new instance of the <see cref="PrimaryKeys"/> class.
//    /// </summary>
//    /// <param name="reader">The data reader.</param>
//    /// <param name="table">The table metadata.</param>
//    public PrimaryKeys(IDataLinqDataReader reader, TableMetadata table)
//    {
//        //(Data, cachedHashCode) = ReadAndParseReader(reader, table);

//        var (data, length) = ReadReader(reader, table);
//        Values = GetValues(data.Span, length.Span, table);

//        CheckData(Values);
//        cachedHashCode = ComputeHashCode(data.Span, length.Span);
//    }

//    public PrimaryKeys(ReadOnlySpan<byte> data, ReadOnlySpan<int> length, TableMetadata table)
//    {
//        Values = GetValues(data, length, table);

//        CheckData(Values);
//        cachedHashCode = ComputeHashCode(data, length);
//    }

//    /// <summary>
//    /// Initializes a new instance of the <see cref="PrimaryKeys"/> class.
//    /// </summary>
//    /// <param name="row">The row data.</param>
//    public PrimaryKeys(RowData row)
//    {
//        Values = ReadRow(row).ToArray();
//        CheckData(Values);

//        var (data, length) = ReadObjects(row.Table, Values);
//        cachedHashCode = ComputeHashCode(data.Span, length.Span);
//    }

//    /// <summary>
//    /// Initializes a new instance of the <see cref="PrimaryKeys"/> class.
//    /// </summary>
//    /// <param name="data">The primary key data.</param>
//    public PrimaryKeys(IEnumerable<object?> values, TableMetadata table)
//    {
//        Values = values.ToArray();
//        CheckData(Values);

//        var (data, length) = ReadObjects(table, Values);
//        cachedHashCode = ComputeHashCode(data.Span, length.Span);
//    }

//    ///// <summary>
//    ///// Initializes a new instance of the <see cref="PrimaryKeys"/> class.
//    ///// </summary>
//    ///// <param name="data">The primary key data.</param>
//    //public PrimaryKeys(object?[] data)
//    //{
//    //    CheckData(data);
//    //    cachedHashCode = ComputeHashCode(data);

//    //    Values = data;
//    //}



//    public object? this[int index]
//    {
//        get => Values[index];
//    }

//    private static void CheckData(ReadOnlySpan<object?> data, TableMetadata? table = null)
//    {
//        if (data == null)
//            throw new ArgumentNullException(nameof(data));

//        //for (int i = 0; i < data.Length; i++)
//        //{
//        //    if (data[i] is null)
//        //        throw new ArgumentNullException(nameof(data), "Data contains null values.");
//        //}

//        if (table != null && data.Length != table.PrimaryKeyColumns.Length)
//            throw new ArgumentException($"The number of primary key values ({data.Length}) does not match the number of primary key columns ({table.PrimaryKeyColumns.Length}).");

//        //return data;
//    }

//    public static object?[] GetValues(ReadOnlySpan<byte> buffer, ReadOnlySpan<int> length, TableMetadata table)
//    {
//        var result = new object?[table.PrimaryKeyColumns.Length];
//        for (int i = 0; i < table.PrimaryKeyColumns.Length; i++)
//            result[i] = DataReader.ConvertBytesToType(buffer.Slice(i > 0 ? length[i - 1] : 0, i > 0 ? length[i] - length[i - 1] : length[i]), table.PrimaryKeyColumns[i].ValueProperty); //reader.ReadColumn(table.PrimaryKeyColumns[i]);

//        return result;
//    }

//    /// <summary>
//    /// Gets the primary key data.
//    /// </summary>
//    //private object?[] values;
//    public object?[] Values { get; }


//    //public byte[] Data { get; }
//    //public int[] Length { get; }
//    private readonly int cachedHashCode;

//    /// <summary>
//    /// Determines whether the specified object is equal to the current object.
//    /// </summary>
//    /// <param name="other">The object to compare with the current object.</param>
//    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
//    public bool Equals(PrimaryKeys? other)
//    {
//        if (other is null)
//            return false;
//        if (ReferenceEquals(this, other))
//            return true;

//        return ArraysEqual(Values, other.Values);
//    }

//    /// <summary>
//    /// Determines whether the specified object is equal to the current object.
//    /// </summary>
//    /// <param name="obj">The object to compare with the current object.</param>
//    /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
//    public override bool Equals(object? obj)
//    {
//        return Equals(obj as PrimaryKeys);
//    }

//    /// <summary>
//    /// Serves as the default hash function.
//    /// </summary>
//    /// <returns>A hash code for the current object.</returns>
//    public override int GetHashCode()
//    {
//        return cachedHashCode;
//        //unchecked
//        //{
//        //    if (Data == null)
//        //    {
//        //        return 0;
//        //    }
//        //    int hash = 17;
//        //    for (int i = 0; i < Data.Length; i++)
//        //    {
//        //        // Use a fixed hash code for null items, e.g., 0.
//        //        // If item is not null, use item's hash code.
//        //        hash = hash * 31 + (Data[i]?.GetHashCode() ?? 0);
//        //    }
//        //    return hash;
//        //}
//    }

//    public static int ComputeHashCode(ReadOnlySpan<byte> buffer, ReadOnlySpan<int> lengths)
//    {
//        unchecked
//        {
//            int hash = 17;
//            int offset = 0;

//            foreach (int length in lengths)
//            {
//                for (int i = offset; i < length; i++)
//                {
//                    hash = hash * 31 + buffer[i];
//                }

//                // Add delimiter to the hash
//                hash = hash * 31 + 0xFF;
//                offset = length;
//            }

//            return hash;
//        }
//    }

//    //public static int ComputeHashCode(Span<object?> data)
//    //{
//    //    unchecked
//    //    {
//    //        int hash = 17;
//    //        foreach (var obj in data)
//    //        {
//    //            hash = hash * 31 + (obj?.GetHashCode() ?? 0);
//    //        }
//    //        return hash;
//    //    }
//    //}

//    //public static int ComputeHashCodeEnumerable(IEnumerable<object?> data)
//    //{
//    //    unchecked
//    //    {
//    //        int hash = 17;
//    //        foreach (var obj in data)
//    //        {
//    //            hash = hash * 31 + (obj?.GetHashCode() ?? 0);
//    //        }
//    //        return hash;
//    //    }
//    //}

//    static bool ArraysEqual<T>(T[] a1, T[] a2)
//    {
//        // If either array is null or lengths are different, return false.
//        if (a1 == null || a2 == null || a1.Length != a2.Length)
//            return false;

//        for (int i = 0; i < a1.Length; i++)
//        {
//            // Use the static Object.Equals method to compare the elements
//            // which safely handles nulls.
//            if (!Object.Equals(a1[i], a2[i]))
//                return false;
//        }

//        // All checks passed, arrays are equal.
//        return true;
//    }

//    public static (Memory<byte> data, Memory<int> length) ReadObjects(TableMetadata table, IEnumerable<object?> objects)
//    {
//        var bytes = objects
//            .Select((x, i) => DataReader.ConvertTypeToBytes(x, table.PrimaryKeyColumns[i].ValueProperty).ToArray())
//            .ToArray();

//        var data = new Memory<byte>(new byte[bytes.Sum(x => x.Length)]);
//        var length = new Memory<int>(new int[bytes.Length]);

//        for (int i = 0; i < length.Length; i++)
//        {
//            bytes[i].CopyTo(data.Slice(i > 0 ? length.Span[i - 1] : 0));
//            length.Span[i] = bytes[i].Length;
//        }

//        return (data, length);
//    }

//    public static (Memory<byte> data, Memory<int> length) ReadReader(IDataLinqDataReader reader, TableMetadata table)
//    {
//        //Memory<object?> buffer = new object?[table.PrimaryKeyColumns.Length];
//        var buffer = new Memory<byte>(new byte[PrimaryKeys.KeyLength(table)]);
//        var length = new Memory<int>(new int[table.PrimaryKeyColumns.Length]);
//        ReadReader(reader, table, buffer.Span, length.Span);

//        return (buffer, length);
//    }

//    public static void ReadReader(IDataLinqDataReader reader, TableMetadata table, Span<byte> buffer, Span<int> length)
//    {
//        if (table.PrimaryKeyColumns.Length > length.Length)
//            throw new ArgumentException("Destination span is not large enough to hold all the primary key columns.", nameof(length));

//        //var buffer = new Memory<byte>(new byte[PrimaryKeyLength(table)]);
//        //var length = new Memory<int>(new int[table.PrimaryKeyColumns.Length]);
//        reader.ReadPrimaryKeys(table.PrimaryKeyColumns, buffer, length);

//        //for (int i = 0; i < table.PrimaryKeyColumns.Length; i++)
//        //    destination[i] = DataReader.ConvertBytesToType(buffer.Slice(i > 0 ? length.Span[i - 1] : 0, i > 0 ? length.Span[i] - length.Span[i - 1] : length.Span[i]).Span, table.PrimaryKeyColumns[i].ValueProperty); //reader.ReadColumn(table.PrimaryKeyColumns[i]);
//    }

//    public static int KeyLength(Column[] columns)
//    {
//        return columns.Sum(x => x.ValueProperty.CsSize ?? 255);
//    }

//    //public static Memory<byte[]?> GetBuffer(TableMetadata table)
//    //{
//    //    Memory<byte[]?> buffer = table.PrimaryKeyColumns
//    //        .Select(x => new byte[x.ValueProperty.CsSize ?? 255])
//    //        .ToArray();

//    //    return buffer;
//    //}

//    //public static IEnumerable<object?> ReadReader(IDataLinqDataReader reader, TableMetadata table)
//    //{
//    //    var result = new object?[table.PrimaryKeyColumns.Length];
//    //    for (int i = 0; i < table.PrimaryKeyColumns.Length; i++)
//    //    {
//    //        result[i] = reader.ReadColumn(table.PrimaryKeyColumns[i]);
//    //    }
//    //    return result;

//    //    //foreach (var column in table.PrimaryKeyColumns)
//    //    //    yield return reader.ReadColumn(column);
//    //}

//    public static IEnumerable<object?> ReadRow(RowData row)
//    {
//        foreach (var column in row.Table.PrimaryKeyColumns)
//            yield return row.GetValue(column);
//    }

//    //public static (ReadOnlyMemory<object?> data, int hashCode) ReadAndParseReader(IDataLinqDataReader reader, TableMetadata table)
//    //{
//    //    var data = ReadReader(reader, table).ToArray();
//    //    CheckData(data, table);
//    //    //CheckData(ReadRow(row).ToArray(), row.Table).ToArray();
//    //    return (data, ComputeHashCode(data));
//    //}
//}