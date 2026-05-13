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

    public static long ArrayBytes(int length, long elementBytes)
    {
        var count = Math.Max(length, 0);
        return SaturatingAdd(ArrayHeaderBytes, SaturatingMultiply(count, elementBytes));
    }

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

    public static long DataLinqKeyArrayBytes(int length) =>
        ArrayBytes(length, DataLinqKeyStructBytes);

    public static int DataLinqKeyStructBytes =>
        AlignToPointer((ReferenceSize * 2) + (sizeof(int) * 2));

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

    public static long ConcurrentDictionaryOverheadBytes(int entryCount)
    {
        var count = Math.Max(entryCount, 0);
        var dictionaryObjectBytes = AlignToPointer(ObjectHeaderBytes + (ReferenceSize * 6) + (sizeof(int) * 2));
        var tableBytes = AlignToPointer(ObjectHeaderBytes + (ReferenceSize * 3));
        var bucketBytes = ObjectArrayBytes(count);
        var nodeBytes = SaturatingMultiply(count, AlignToPointer(ObjectHeaderBytes + (ReferenceSize * 3) + sizeof(int)));
        return SaturatingAdd(SaturatingAdd(dictionaryObjectBytes, tableBytes), SaturatingAdd(bucketBytes, nodeBytes));
    }

    public static long QueueOverheadBytes(int entryCount, long entryBytes)
    {
        var count = Math.Max(entryCount, 0);
        var queueObjectBytes = AlignToPointer(ObjectHeaderBytes + (ReferenceSize * 2) + (sizeof(int) * 4));
        return SaturatingAdd(queueObjectBytes, ArrayBytes(count, entryBytes));
    }

    public static long IndexCacheContainerBytes =>
        AlignToPointer(ObjectHeaderBytes + (ReferenceSize * 5) + sizeof(long));

    public static long TickQueueEntryBytes(Type keyType) =>
        AlignToPointer(EstimateArrayElementBytes(keyType) + sizeof(long));

    public static long ImmutableArrayBackingBytes(Type elementType, int length) =>
        length <= 0 ? 0 : ArrayBytes(length, EstimateArrayElementBytes(elementType));

    public static long EstimateArrayElementBytes(Type elementType)
    {
        if (!elementType.IsValueType)
            return ReferenceSize;

        if (elementType == typeof(byte) || elementType == typeof(bool))
            return 1;

        if (elementType == typeof(short) || elementType == typeof(ushort) || elementType == typeof(char))
            return 2;

        if (elementType == typeof(int) || elementType == typeof(uint) || elementType.IsEnum)
            return 4;

        if (elementType == typeof(long) ||
            elementType == typeof(ulong) ||
            elementType == typeof(double) ||
            elementType == typeof(DateTime) ||
            elementType == typeof(DateOnly))
        {
            return 8;
        }

        if (elementType == typeof(float))
            return 4;

        if (elementType == typeof(Guid))
            return 16;

        if (elementType == typeof(DataLinqKey))
            return DataLinqKeyStructBytes;

        return ReferenceSize * 2;
    }

    public static long RowStoreContainerBytes =>
        AlignToPointer(ObjectHeaderBytes + (ReferenceSize * 2) + sizeof(long));

    public static long RowEntryBytes =>
        AlignToPointer(ObjectHeaderBytes + ReferenceSize + sizeof(int) + sizeof(long));

    public static long RowDataContainerBytes(int columnCount)
    {
        var rowDataObjectBytes = AlignToPointer(ObjectHeaderBytes + ReferenceSize + sizeof(int));
        return SaturatingAdd(rowDataObjectBytes, ObjectArrayBytes(columnCount));
    }

    public static long ImmutableRowInstanceBytes =>
        AlignToPointer(ObjectHeaderBytes + (ReferenceSize * 5));

    public static long EstimateKeyPayloadBytes(object? key)
    {
        if (key is null)
            return 0;

        if (key is string text)
            return StringBytes(text);

        if (key is byte[] bytes)
            return ByteArrayBytes(bytes.Length);

        if (key is DataLinqKey dataLinqKey)
            return EstimateDataLinqKeyPayloadBytes(dataLinqKey);

        if (key is IProviderKey providerKey)
        {
            var total = providerKey.ValueCount <= 1 ? 0 : ObjectArrayBytes(providerKey.ValueCount);
            for (var i = 0; i < providerKey.ValueCount; i++)
                total = SaturatingAdd(total, EstimateKeyPayloadBytes(providerKey.GetValue(i)));

            return total;
        }

        return key.GetType().IsValueType ? 0 : ObjectHeaderBytes;
    }

    public static long EstimateDataLinqKeyPayloadBytes(DataLinqKey key)
    {
        var total = DataLinqKeyComponentArrayBytes(key);

        for (var i = 0; i < key.ValueCount; i++)
            total = SaturatingAdd(total, EstimateKeyPayloadBytes(key.GetValue(i)));

        return total;
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

}
