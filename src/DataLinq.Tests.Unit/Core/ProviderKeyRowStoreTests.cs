using System;
using System.Collections.Generic;
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
        var cache = new RowCache();

        await AssertProviderKeyRoundTrip(cache, new IntKey(42), 42);
        await AssertProviderKeyRoundTrip(cache, new LongKey(42L), 42L);
        await AssertProviderKeyRoundTrip(cache, new GuidKey(new Guid("2f4a38d5-3f4e-4f40-9c79-7b4a0a2a6f11")), new Guid("2f4a38d5-3f4e-4f40-9c79-7b4a0a2a6f11"));
        await AssertProviderKeyRoundTrip(cache, new StringKey("dept-1"), "dept-1");
    }

    [Test]
    public async Task RowCache_ProviderKeyRemoval_RemovesLegacyAndProviderEntries()
    {
        var cache = new RowCache();
        var row = new TestImmutableInstance(new IntKey(42));

        await Assert.That(cache.TryAddRow(new IntKey(42), 128, row)).IsTrue();
        await Assert.That(cache.TryRemoveProviderKey(42, out var rowsRemoved)).IsTrue();

        await Assert.That(rowsRemoved).IsEqualTo(1);
        await Assert.That(cache.TryGetValue(42, out _)).IsFalse();
        await Assert.That(cache.TryGetValue((IKey)new IntKey(42), out _)).IsFalse();
    }

    [Test]
    public async Task RowCache_CompositeKeys_StayOnLegacyAdapterPath()
    {
        var cache = new RowCache();
        var compositeKey = KeyFactory.CreateKeyFromValues([42, "dept-1"]);
        var row = new TestImmutableInstance(compositeKey);

        await Assert.That(cache.TryAddRow(compositeKey, 128, row)).IsTrue();

        await Assert.That(cache.TryGetValue(42, out _)).IsFalse();
        await Assert.That(cache.TryGetValue(compositeKey, out var legacyRow)).IsTrue();
        await Assert.That(ReferenceEquals(row, legacyRow)).IsTrue();
    }

    private static async Task AssertProviderKeyRoundTrip<TKey>(RowCache cache, IKey key, TKey providerKey)
    {
        var row = new TestImmutableInstance(key);

        await Assert.That(cache.TryAddRow(key, 128, row)).IsTrue();
        await Assert.That(cache.TryGetValue(providerKey, out var providerRow)).IsTrue();
        await Assert.That(cache.TryGetValue(key, out var legacyRow)).IsTrue();
        await Assert.That(ReferenceEquals(row, providerRow)).IsTrue();
        await Assert.That(ReferenceEquals(row, legacyRow)).IsTrue();
    }

    private sealed class TestImmutableInstance(IKey primaryKeys) : IImmutableInstance
    {
        public object? this[string propertyName] => throw new NotSupportedException();
        public object? this[ColumnDefinition column] => throw new NotSupportedException();

        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues() => [];
        public IEnumerable<KeyValuePair<ColumnDefinition, object?>> GetValues(IEnumerable<ColumnDefinition> columns) => [];
        public bool HasPrimaryKeysSet() => true;
        public ModelDefinition Metadata() => throw new NotSupportedException();
        public IKey PrimaryKeys() => primaryKeys;
        public IRowData GetRowData() => throw new NotSupportedException();
        IRowData IModelInstance.GetRowData() => GetRowData();
        public void ClearLazy() { }
        public V? GetLazy<V>(string name, Func<V> fetchCode) => fetchCode();
        public IDataSourceAccess GetDataSource() => throw new NotSupportedException();
    }
}
