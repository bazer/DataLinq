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
        await Assert.That(cache.TryAddProviderKey(42, [primaryKey])).IsTrue();

        await Assert.That(cache.TryGetProviderKey(42, out var providerKeys)).IsTrue();
        await Assert.That(providerKeys).IsNotNull();
        await Assert.That(providerKeys![0]).IsEqualTo(primaryKey);

        await Assert.That(cache.TryGetValue(DataLinqKey.FromValue(42), out var dynamicKeys)).IsTrue();
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
        await Assert.That(cache.TryAddProviderKey(providerForeignKey, [primaryKey])).IsTrue();
        await Assert.That(cache.TryGetProviderKey(providerForeignKey, out var providerKeys)).IsTrue();
        await Assert.That(providerKeys).IsNotNull();
        await Assert.That(providerKeys![0]).IsEqualTo(primaryKey);

        await Assert.That(cache.TryGetValue(foreignKey, out var keys)).IsTrue();
        await Assert.That(keys).IsNotNull();
        await Assert.That(keys![0]).IsEqualTo(primaryKey);

        await Assert.That(cache.TryRemoveForeignKey(foreignKey, out var removedRows)).IsTrue();
        await Assert.That(removedRows).IsEqualTo(1);
        await Assert.That(cache.Count).IsEqualTo(0);
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
