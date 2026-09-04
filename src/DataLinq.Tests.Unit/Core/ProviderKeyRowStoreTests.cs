using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Metadata;

namespace DataLinq.Tests.Unit.Core;

public class ProviderKeyRowStoreTests
{
    [Test]
    public async Task RowCache_ProviderKeyStore_RoundTripsCommonScalarKeys()
    {
        await AssertProviderKeyRoundTrip(DataLinqKey.FromValue(42), 42);
        await AssertProviderKeyRoundTrip(DataLinqKey.FromValue(42L), 42L);
        await AssertProviderKeyRoundTrip(DataLinqKey.FromValue(new Guid("2f4a38d5-3f4e-4f40-9c79-7b4a0a2a6f11")), new Guid("2f4a38d5-3f4e-4f40-9c79-7b4a0a2a6f11"));
        await AssertProviderKeyRoundTrip(DataLinqKey.FromValue("dept-1"), "dept-1");
    }

    [Test]
    public async Task RowCache_ProviderKeyRemoval_RemovesProviderEntry()
    {
        var cache = new RowCache();
        var row = new TestImmutableInstance(DataLinqKey.FromValue(42));

        await Assert.That(cache.TryAddRow(42, 128, row)).IsTrue();
        await Assert.That(cache.TryRemoveProviderKey(42, out var rowsRemoved)).IsTrue();

        await Assert.That(rowsRemoved).IsEqualTo(1);
        await Assert.That(cache.TryGetValue(42, out _)).IsFalse();
        await Assert.That(cache.TryGetValue(DataLinqKey.FromValue(42), out _)).IsFalse();
    }

    [Test]
    public async Task RowCache_DataLinqScalarRemoval_AdaptsIntoSingleProviderKeyStore()
    {
        var cache = new RowCache();
        var row = new TestImmutableInstance(DataLinqKey.FromValue(42));

        await Assert.That(cache.TryAddRow(42, 128, row)).IsTrue();
        await Assert.That(cache.TryRemoveRow(DataLinqKey.FromValue(42), out var rowsRemoved)).IsTrue();

        await Assert.That(rowsRemoved).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RowCache_GeneratedCompositeAccessors_UseSingleProviderKeyStore()
    {
        var cache = new RowCache();
        var compositeKey = KeyFactory.CreateKeyFromValues([42, "dept-1"]);
        var providerKey = new TestCompositeProviderKey(42, "dept-1");
        var row = new TestImmutableInstance(compositeKey);
        var accessor = new TestCompositeProviderKeyRowStoreAccessor();

        await Assert.That(cache.TryAddRow(providerKey, 128, row)).IsTrue();

        await Assert.That(cache.TryGetValue(42, out _)).IsFalse();
        await Assert.That(cache.TryGetValue(providerKey, out var providerRow)).IsTrue();
        await Assert.That(accessor.TryGetRow(cache, compositeKey, out var adaptedRow)).IsTrue();
        await Assert.That(cache.Count).IsEqualTo(1);
        await Assert.That(ReferenceEquals(row, providerRow)).IsTrue();
        await Assert.That(ReferenceEquals(row, adaptedRow)).IsTrue();

        await Assert.That(accessor.TryRemoveRow(cache, compositeKey, out var rowsRemoved)).IsTrue();
        await Assert.That(rowsRemoved).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RowCache_DataLinqCompositeValue_CanUseSingleDynamicStoreWhenNoGeneratedAccessorExists()
    {
        var cache = new RowCache();
        var compositeKey = KeyFactory.CreateKeyFromValues([42, "dept-1"]);
        var row = new TestImmutableInstance(compositeKey);

        await Assert.That(cache.TryAddRow(compositeKey, 128, row)).IsTrue();
        await Assert.That(cache.Count).IsEqualTo(1);
        await Assert.That(cache.TryGetValue(compositeKey, out var cachedRow)).IsTrue();
        await Assert.That(ReferenceEquals(row, cachedRow)).IsTrue();
    }

    [Test]
    public async Task RowCache_BinaryProviderKey_UsesStructuralSnapshotForLookupAndRemoval()
    {
        var cache = new RowCache();
        var providerKey = new byte[] { 0x01, 0x02, 0x03 };
        var row = new TestImmutableInstance(DataLinqKey.FromValue(providerKey));

        await Assert.That(cache.TryAddRow(providerKey, 128, row)).IsTrue();

        providerKey[0] = 0xff;

        await Assert.That(cache.TryGetValue(new byte[] { 0x01, 0x02, 0x03 }, out var cachedRow)).IsTrue();
        await Assert.That(ReferenceEquals(row, cachedRow)).IsTrue();
        await Assert.That(cache.TryRemoveProviderKey(new byte[] { 0x01, 0x02, 0x03 }, out var rowsRemoved)).IsTrue();
        await Assert.That(rowsRemoved).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RowCache_CompositeBinaryProviderKey_UsesStructuralSnapshotForLookupAndRemoval()
    {
        var cache = new RowCache();
        var binaryComponent = new byte[] { 0x01, 0x02, 0x03 };
        var providerKey = new TestBinaryCompositeProviderKey(42, binaryComponent);
        var row = new TestImmutableInstance(KeyFactory.CreateKeyFromValues([42, binaryComponent]));

        await Assert.That(cache.TryAddRow(providerKey, 128, row)).IsTrue();

        binaryComponent[0] = 0xff;

        var lookupKey = new TestBinaryCompositeProviderKey(42, new byte[] { 0x01, 0x02, 0x03 });
        await Assert.That(cache.TryGetValue(lookupKey, out var cachedRow)).IsTrue();
        await Assert.That(ReferenceEquals(row, cachedRow)).IsTrue();
        var removalKey = new TestBinaryCompositeProviderKey(42, new byte[] { 0x01, 0x02, 0x03 });
        await Assert.That(cache.TryRemoveProviderKey(removalKey, out var rowsRemoved)).IsTrue();
        await Assert.That(rowsRemoved).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RowStore_EvictionPreservesAgeOrderAcrossRemovalAndReplacement()
    {
        long tick = 10;
        var store = new RowStore<int>(() => tick);
        var row = new TestImmutableInstance(DataLinqKey.FromValue(1));
        store.TryAdd(3, 10, 0, row);
        store.TryAdd(1, 20, 0, row);
        store.TryAdd(2, 30, 0, row);
        store.TryRemove(1, out _);
        tick = 20;
        store.TryAdd(1, 40, 0, row);

        await Assert.That(store.RemoveRowsOverRowLimitAndReturnKeys(2).Single()).IsEqualTo(DataLinqKey.FromValue(3));
        await Assert.That(store.RemoveRowsOverSizeLimitAndReturnKeys(40).Single()).IsEqualTo(DataLinqKey.FromValue(2));
        await Assert.That(store.RowPayloadBytes).IsEqualTo(40L);
        await Assert.That(store.OldestTick).IsEqualTo(20L);
        await Assert.That(store.NewestTick).IsEqualTo(20L);
        await Assert.That(store.TryGet(1, out var retained)).IsTrue();
        await Assert.That(retained).IsSameReferenceAs(row);
        await Assert.That(store.RemoveOldestRows(0).Count).IsEqualTo(0);
        await Assert.That(store.RemoveOldestRows(10).Single()).IsEqualTo(DataLinqKey.FromValue(1));
        await Assert.That(store.RowPayloadBytes).IsEqualTo(0L);
        await Assert.That(store.OldestTick).IsNull();
        await Assert.That(store.NewestTick).IsNull();
        await Assert.That(store.GetMemoryEstimate()).IsEqualTo(new RowStore<int>().GetMemoryEstimate());
    }

    [Test]
    public async Task RowStore_AgeCutoffHandlesClockRollbackAndEqualTimestamps()
    {
        long tick = 30;
        var store = new RowStore<int>(() => tick);
        var row = new TestImmutableInstance(DataLinqKey.FromValue(1));
        store.TryAdd(1, 8, 0, row);
        tick = 10;
        store.TryAdd(2, 8, 0, row);
        tick = 20;
        store.TryAdd(3, 8, 0, row);
        store.TryAdd(4, 8, 0, row);
        await Assert.That(store.OldestTick).IsEqualTo(10L);
        await Assert.That(store.NewestTick).IsEqualTo(30L);
        await Assert.That(store.RemoveRowsInsertedBeforeTickAndReturnKeys(20).Single()).IsEqualTo(DataLinqKey.FromValue(2));
        await Assert.That(store.RemoveOldestRows(3).SequenceEqual(new[] { 3, 4, 1 }.Select(DataLinqKey.FromValue))).IsTrue();
        store.TryAdd(5, 8, 0, row);
        store.Clear();
        await Assert.That(store.RemoveOldestRows(1).Count).IsEqualTo(0);
        await Assert.That(store.GetMemoryEstimate()).IsEqualTo(new RowStore<int>().GetMemoryEstimate());
    }

    [Test]
    public async Task RowStore_ConcurrentLookupChurnAndEvictionKeepAccountingConsistent()
    {
        var store = new RowStore<int>();
        var row = new TestImmutableInstance(DataLinqKey.FromValue(1));
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 4).Select(worker => Task.Run(() =>
        {
            start.Wait();
            for (var i = 0; i < 10_000; i++)
            {
                var key = worker * 10_000 + i;
                store.TryAdd(key, 8, 0, row);
                store.TryGet(key, out _);
                if ((i & 1) == 0)
                    store.TryRemove(key, out _);
                store.RemoveRowsOverRowLimit(128);
            }
        })).ToArray();
        start.Set();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That(store.Count <= 128).IsTrue();
        await Assert.That(store.RowPayloadBytes).IsEqualTo(store.Count * 8L);
        await Assert.That(store.Rows.Count()).IsEqualTo(store.Count);
        store.Clear();
        await Assert.That(store.GetMemoryEstimate()).IsEqualTo(new RowStore<int>().GetMemoryEstimate());
    }

    private static async Task AssertProviderKeyRoundTrip<TKey>(DataLinqKey key, TKey providerKey)
        where TKey : notnull
    {
        var cache = new RowCache();
        var row = new TestImmutableInstance(key);

        await Assert.That(cache.TryAddRow(providerKey, 128, row)).IsTrue();
        await Assert.That(cache.TryGetValue(providerKey, out var providerRow)).IsTrue();
        await Assert.That(cache.TryGetValue(key, out var legacyRow)).IsTrue();
        await Assert.That(ReferenceEquals(row, providerRow)).IsTrue();
        await Assert.That(ReferenceEquals(row, legacyRow)).IsTrue();
    }

    private readonly record struct TestCompositeProviderKey(int EmployeeNumber, string DepartmentNumber) : IProviderKey
    {
        public int ValueCount => 2;

        public object? GetValue(int index) => index switch
        {
            0 => EmployeeNumber,
            1 => DepartmentNumber,
            _ => throw new IndexOutOfRangeException()
        };

        public static bool TryCreate(DataLinqKey key, out TestCompositeProviderKey providerKey)
        {
            if (key.ValueCount == 2 &&
                key.GetValue(0) is int employeeNumber &&
                key.GetValue(1) is string departmentNumber)
            {
                providerKey = new TestCompositeProviderKey(employeeNumber, departmentNumber);
                return true;
            }

            providerKey = default;
            return false;
        }
    }

    private readonly record struct TestBinaryCompositeProviderKey(int EmployeeNumber, byte[] BinaryComponent) : IProviderKey
    {
        public int ValueCount => 2;

        public object? GetValue(int index) => index switch
        {
            0 => EmployeeNumber,
            1 => BinaryComponent,
            _ => throw new IndexOutOfRangeException()
        };
    }

    private sealed class TestCompositeProviderKeyRowStoreAccessor : IProviderKeyRowStoreAccessor
    {
        public bool TryAddRow(RowCache cache, RowData rowData, IImmutableInstance row) => throw new NotSupportedException();

        public bool TryGetRow(RowCache cache, DataLinqKey key, out IImmutableInstance? row)
        {
            if (!TestCompositeProviderKey.TryCreate(key, out var providerKey))
            {
                row = null;
                return false;
            }

            return cache.TryGetValue(providerKey, out row);
        }

        public bool TryRemoveRow(RowCache cache, DataLinqKey key, out int numRowsRemoved)
        {
            if (!TestCompositeProviderKey.TryCreate(key, out var providerKey))
            {
                numRowsRemoved = 0;
                return false;
            }

            return cache.TryRemoveProviderKey(providerKey, out numRowsRemoved);
        }

        public bool TryCreateKey(IRowData rowData, out DataLinqKey key) => throw new NotSupportedException();
        public bool TryCreateKey(IModelInstance model, out DataLinqKey key) => throw new NotSupportedException();
    }

    private sealed class TestImmutableInstance(DataLinqKey primaryKeys) : IImmutableInstance
    {
        public object? this[string propertyName] => throw new NotSupportedException();
        public object? this[ColumnDefinition column] => throw new NotSupportedException();

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => [];
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) => [];
        public bool HasPrimaryKeysSet() => true;
        public ModelDefinition Metadata() => throw new NotSupportedException();
        public DataLinqKey PrimaryKeys() => primaryKeys;
        public IRowData GetRowData() => throw new NotSupportedException();
        IRowData IModelInstance.GetRowData() => GetRowData();
        public void ClearLazy() { }
        public V? GetLazy<V>(string name, Func<V> fetchCode) => fetchCode();
        public IDataSourceAccess GetDataSource() => throw new NotSupportedException();
    }
}
