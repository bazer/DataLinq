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
        await AssertProviderKeyRoundTrip(new IntKey(42), 42);
        await AssertProviderKeyRoundTrip(new LongKey(42L), 42L);
        await AssertProviderKeyRoundTrip(new GuidKey(new Guid("2f4a38d5-3f4e-4f40-9c79-7b4a0a2a6f11")), new Guid("2f4a38d5-3f4e-4f40-9c79-7b4a0a2a6f11"));
        await AssertProviderKeyRoundTrip(new StringKey("dept-1"), "dept-1");
    }

    [Test]
    public async Task RowCache_ProviderKeyRemoval_RemovesLegacyAndProviderEntries()
    {
        var cache = new RowCache();
        var row = new TestImmutableInstance(new IntKey(42));

        await Assert.That(cache.TryAddRow(42, 128, row)).IsTrue();
        await Assert.That(cache.TryRemoveProviderKey(42, out var rowsRemoved)).IsTrue();

        await Assert.That(rowsRemoved).IsEqualTo(1);
        await Assert.That(cache.TryGetValue(42, out _)).IsFalse();
        await Assert.That(cache.TryGetValue((IKey)new IntKey(42), out _)).IsFalse();
    }

    [Test]
    public async Task RowCache_LegacyScalarRemoval_AdaptsIntoSingleProviderKeyStore()
    {
        var cache = new RowCache();
        var row = new TestImmutableInstance(new IntKey(42));

        await Assert.That(cache.TryAddRow(42, 128, row)).IsTrue();
        await Assert.That(cache.TryRemoveRow(new IntKey(42), out var rowsRemoved)).IsTrue();

        await Assert.That(rowsRemoved).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RowCache_GeneratedCompositeKeys_UseSingleProviderKeyStore()
    {
        var cache = new RowCache();
        var compositeKey = KeyFactory.CreateKeyFromValues([42, "dept-1"]);
        var providerKey = new TestCompositeProviderKey(42, "dept-1");
        var row = new TestImmutableInstance(compositeKey);

        await Assert.That(cache.TryAddRow(providerKey, 128, row, TestCompositeProviderKey.TryCreate)).IsTrue();

        await Assert.That(cache.TryGetValue(42, out _)).IsFalse();
        await Assert.That(cache.TryGetValue(providerKey, out var providerRow)).IsTrue();
        await Assert.That(cache.TryGetValue(compositeKey, out var adaptedRow)).IsTrue();
        await Assert.That(ReferenceEquals(row, providerRow)).IsTrue();
        await Assert.That(ReferenceEquals(row, adaptedRow)).IsTrue();
    }

    [Test]
    public async Task RowCache_LegacyCompositeKey_DoesNotCreateIKeyBackedStore()
    {
        var cache = new RowCache();
        var compositeKey = KeyFactory.CreateKeyFromValues([42, "dept-1"]);
        var row = new TestImmutableInstance(compositeKey);

        await Assert.That(cache.TryAddRow(compositeKey, 128, row)).IsFalse();
        await Assert.That(cache.Count).IsEqualTo(0);
        await Assert.That(cache.TryGetValue(compositeKey, out _)).IsFalse();
    }

    private static async Task AssertProviderKeyRoundTrip<TKey>(IKey key, TKey providerKey)
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

        public static bool TryCreate(IKey key, out TestCompositeProviderKey providerKey)
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
