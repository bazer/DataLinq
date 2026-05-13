using System;
using DataLinq.Instances;

namespace DataLinq.Cache;

/// <summary>
/// Centralized approximations for cache-owned memory. These values are deliberately stable estimates,
/// not CLR heap measurements, so cleanup and diagnostics can be explainable without object walking.
/// </summary>
internal static class CacheMemoryEstimator
{
    /// <summary>Estimated object-reference slot size for the current process architecture.</summary>
    public static int ReferenceSize => IntPtr.Size;

    /// <summary>Estimated object header plus method-table pointer.</summary>
    public static int ObjectHeaderBytes => ReferenceSize * 2;

    /// <summary>Estimated array object overhead, including the array length field and pointer alignment.</summary>
    public static int ArrayHeaderBytes => AlignToPointer(ObjectHeaderBytes + sizeof(int));

    public static long ObjectArrayBytes(int length) =>
        ArrayBytes(length, ReferenceSize);

    public static long ByteArrayBytes(int length) =>
        ArrayBytes(length, sizeof(byte));

    public static long StringBytes(string? value) =>
        value is null ? 0 : StringBytes(value.Length);

    public static long StringBytes(int characterCount)
    {
        var length = Math.Max(characterCount, 0);
        var instanceBytes = SaturatingAdd(ObjectHeaderBytes, sizeof(int));
        instanceBytes = SaturatingAdd(instanceBytes, sizeof(char));
        instanceBytes = AlignToPointer(instanceBytes);
        return SaturatingAdd(instanceBytes, SaturatingMultiply(length, sizeof(char)));
    }

    public static long DataLinqKeyComponentArrayBytes(DataLinqKey key) =>
        DataLinqKeyComponentArrayBytes(key.ValueCount);

    public static long DataLinqKeyComponentArrayBytes(int componentCount) =>
        componentCount <= 1 ? 0 : ObjectArrayBytes(componentCount);

    public static long DictionaryBucketArrayBytes(int bucketCount) =>
        ArrayBytes(bucketCount, sizeof(int));

    public static long DictionaryEntryArrayBytes(int entryCount)
    {
        // Dictionary entries carry hash code, next index, key, and value. Treat key/value as reference-sized
        // slots here; precise value-type entry widths are a later component-accounting concern.
        var entryBytes = AlignToPointer(sizeof(int) + sizeof(int) + ReferenceSize + ReferenceSize);
        return ArrayBytes(entryCount, entryBytes);
    }

    public static long DictionaryOverheadBytes(int entryCount)
    {
        var count = Math.Max(entryCount, 0);
        var dictionaryObjectBytes = AlignToPointer(ObjectHeaderBytes + (ReferenceSize * 4) + (sizeof(int) * 4));
        var bucketBytes = DictionaryBucketArrayBytes(count);
        var entryBytes = DictionaryEntryArrayBytes(count);
        return SaturatingAdd(SaturatingAdd(dictionaryObjectBytes, bucketBytes), entryBytes);
    }

    public static long SaturatingAdd(long left, long right)
    {
        left = Math.Max(left, 0);
        right = Math.Max(right, 0);

        if (left > long.MaxValue - right)
            return long.MaxValue;

        return left + right;
    }

    public static long SaturatingMultiply(long count, long itemBytes)
    {
        count = Math.Max(count, 0);
        itemBytes = Math.Max(itemBytes, 0);

        if (count == 0 || itemBytes == 0)
            return 0;

        if (count > long.MaxValue / itemBytes)
            return long.MaxValue;

        return count * itemBytes;
    }

    public static int AlignToPointer(int bytes)
    {
        var pointerSize = ReferenceSize;
        var remainder = bytes % pointerSize;
        return remainder == 0 ? bytes : bytes + pointerSize - remainder;
    }

    public static long AlignToPointer(long bytes)
    {
        bytes = Math.Max(bytes, 0);
        var pointerSize = ReferenceSize;
        var remainder = bytes % pointerSize;
        return remainder == 0 ? bytes : SaturatingAdd(bytes, pointerSize - remainder);
    }

    private static long ArrayBytes(int length, long elementBytes)
    {
        var count = Math.Max(length, 0);
        return SaturatingAdd(ArrayHeaderBytes, SaturatingMultiply(count, elementBytes));
    }
}
