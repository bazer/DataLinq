using System;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Instances;

namespace DataLinq.Tests.Unit.Core;

public class ProviderKeyRelationCacheTests
{
    [Test]
    public async Task IndexCache_ProviderScalarForeignKeys_RoundTripThroughSingleTypedStore()
    {
        var cache = new TypedIndexCache<int>();
        var primaryKey = DataLinqKey.FromValues(["d001", 42]);

        await Assert.That(cache.KeyType).IsEqualTo(typeof(int));
        await Assert.That(cache.TryAdd(42, [primaryKey])).IsTrue();

        await Assert.That(cache.TryGet(42, out var providerKeys)).IsTrue();
        await Assert.That(providerKeys).IsNotNull();
        await Assert.That(providerKeys![0]).IsEqualTo(primaryKey);

        await Assert.That(cache.TryGet(DataLinqKey.FromValue(42), out var dynamicKeys)).IsTrue();
        await Assert.That(dynamicKeys).IsNotNull();
        await Assert.That(dynamicKeys![0]).IsEqualTo(primaryKey);

        await Assert.That(cache.TryRemovePrimaryKey(primaryKey, out var removedRows)).IsTrue();
        await Assert.That(removedRows).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task IndexCache_DynamicCompositeForeignKeys_RemainOnDynamicFallbackStore()
    {
        var cache = new IndexCache();
        var providerForeignKey = new TestCompositeProviderKey("d001", 42);
        var foreignKey = DataLinqKey.FromProviderKey(providerForeignKey);
        var primaryKey = DataLinqKey.FromValues(["d001", 42, new DateOnly(2020, 1, 1)]);

        await Assert.That(cache.KeyType).IsEqualTo(typeof(DataLinqKey));
        await Assert.That(cache.TryAdd(providerForeignKey, [primaryKey])).IsTrue();
        await Assert.That(cache.TryGet(providerForeignKey, out var providerKeys)).IsTrue();
        await Assert.That(providerKeys).IsNotNull();
        await Assert.That(providerKeys![0]).IsEqualTo(primaryKey);

        await Assert.That(cache.TryGet(foreignKey, out var keys)).IsTrue();
        await Assert.That(keys).IsNotNull();
        await Assert.That(keys![0]).IsEqualTo(primaryKey);

        await Assert.That(cache.TryRemove(foreignKey, out var removedRows)).IsTrue();
        await Assert.That(removedRows).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(0);
    }

    [Test]
    public async Task IndexCache_PrimaryKeyArrays_AreOwnedByTheCache()
    {
        var cache = new TypedIndexCache<int>();
        var firstPrimaryKey = DataLinqKey.FromValue(1);
        var secondPrimaryKey = DataLinqKey.FromValue(2);
        var replacementPrimaryKey = DataLinqKey.FromValue(999);
        var callerOwnedPrimaryKeys = new[] { firstPrimaryKey, secondPrimaryKey };

        await Assert.That(cache.TryAdd(42, callerOwnedPrimaryKeys)).IsTrue();

        callerOwnedPrimaryKeys[0] = replacementPrimaryKey;
        callerOwnedPrimaryKeys[1] = replacementPrimaryKey;

        await Assert.That(cache.TryGet(42, out var cachedPrimaryKeys)).IsTrue();
        await Assert.That(cachedPrimaryKeys).IsNotNull();
        await Assert.That(ReferenceEquals(cachedPrimaryKeys, callerOwnedPrimaryKeys)).IsFalse();
        await Assert.That(cachedPrimaryKeys!.Length).IsEqualTo(2);
        await Assert.That(cachedPrimaryKeys[0]).IsEqualTo(firstPrimaryKey);
        await Assert.That(cachedPrimaryKeys[1]).IsEqualTo(secondPrimaryKey);

        await Assert.That(cache.TryRemovePrimaryKey(replacementPrimaryKey, out var removedByReplacement)).IsTrue();
        await Assert.That(removedByReplacement).IsEqualTo(0);
        await Assert.That(cache.Count).IsEqualTo(1);

        await Assert.That(cache.TryRemovePrimaryKey(firstPrimaryKey, out var removedByOriginal)).IsTrue();
        await Assert.That(removedByOriginal).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(0);

        var unrelatedPrimaryKey = DataLinqKey.FromValue(3);
        await Assert.That(cache.TryAdd(42, [unrelatedPrimaryKey])).IsTrue();
        await Assert.That(cache.TryRemovePrimaryKey(secondPrimaryKey, out var removedByStaleMapping)).IsTrue();
        await Assert.That(removedByStaleMapping).IsEqualTo(0);
        await Assert.That(cache.Count).IsEqualTo(1);

        await Assert.That(cache.TryGet(42, out var remainingPrimaryKeys)).IsTrue();
        await Assert.That(remainingPrimaryKeys).IsNotNull();
        await Assert.That(remainingPrimaryKeys!.Length).IsEqualTo(1);
        await Assert.That(remainingPrimaryKeys[0]).IsEqualTo(unrelatedPrimaryKey);
    }

    private readonly record struct TestCompositeProviderKey(string DepartmentNumber, int EmployeeNumber) : IProviderKey
    {
        public int ValueCount => 2;

        public object? GetValue(int index) => index switch
        {
            0 => DepartmentNumber,
            1 => EmployeeNumber,
            _ => throw new IndexOutOfRangeException()
        };
    }
}
