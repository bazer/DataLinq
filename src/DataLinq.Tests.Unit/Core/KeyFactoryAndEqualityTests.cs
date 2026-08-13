using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataLinq.Instances;

namespace DataLinq.Tests.Unit.Core;

public enum TestStatusEnum
{
    Inactive = 0,
    Active = 1,
    Pending = 5
}

public enum TestByteEnum : byte
{
    Small = 10,
    Medium = 20,
    Large = 30
}

public sealed record SimpleKeyCase(object Value1, object Value2, object DifferentValue);

public class KeyFactoryAndEqualityTests
{
    [Test]
    public async Task DataLinqKey_Bytes_OwnsItsContentAndRemainsAStableDictionaryKey()
    {
        var data1 = new byte[] { 1, 2, 3, 4, 5 };
        var data2 = new byte[] { 1, 2, 3, 4, 5 };
        var data3 = new byte[] { 6, 7, 8 };

        var key1 = KeyFactory.CreateKeyFromValue(data1);
        var key2 = KeyFactory.CreateKeyFromValue(data2);
        var key3 = KeyFactory.CreateKeyFromValue(data3);
        var originalHashCode = key1.GetHashCode();
        var dictionary = new Dictionary<DataLinqKey, string> { [key1] = "cached" };

        data1[0] = 99;
        var exposedValue = (byte[])key1.GetValue(0)!;
        exposedValue[1] = 99;
        var rereadValue = (byte[])key1.GetValue(0)!;

        await Assert.That(key1.ValueCount).IsEqualTo(1);
        await Assert.That(exposedValue).IsNotSameReferenceAs(data1);
        await Assert.That(rereadValue).IsNotSameReferenceAs(exposedValue);
        await Assert.That(rereadValue.SequenceEqual(data2)).IsTrue();
        await Assert.That(key1.Equals(key2)).IsTrue();
        await Assert.That(key1.GetHashCode()).IsEqualTo(originalHashCode);
        await Assert.That(key1.GetHashCode()).IsEqualTo(key2.GetHashCode());
        await Assert.That(dictionary.TryGetValue(key2, out var cached)).IsTrue();
        await Assert.That(cached).IsEqualTo("cached");
        await Assert.That(key1.Equals(key3)).IsFalse();
        await Assert.That(key1.GetHashCode()).IsNotEqualTo(key3.GetHashCode());
    }

    [Test]
    public async Task DataLinqKey_Composite_Equality_IsBasedOnContentAndOrder()
    {
        var key1 = KeyFactory.CreateKeyFromValues(new object[] { 123, "test", new Guid("f81d4fae-7dec-11d0-a765-00a0c91e6bf6") });
        var key2 = KeyFactory.CreateKeyFromValues(new object[] { 123, "test", new Guid("f81d4fae-7dec-11d0-a765-00a0c91e6bf6") });
        var key3 = KeyFactory.CreateKeyFromValues(new object[] { "test", 123, new Guid("f81d4fae-7dec-11d0-a765-00a0c91e6bf6") });
        var key4 = KeyFactory.CreateKeyFromValues(new object[] { 456, "different" });

        await Assert.That(key1.Equals(key2)).IsTrue();
        await Assert.That(key1.GetHashCode()).IsEqualTo(key2.GetHashCode());
        await Assert.That(key1.Equals(key3)).IsFalse();
        await Assert.That(key1.GetHashCode()).IsNotEqualTo(key3.GetHashCode());
        await Assert.That(key1.Equals(key4)).IsFalse();
    }

    [Test]
    public async Task DataLinqKey_Composite_WithBytes_OwnsItsContentAndRemainsAStableDictionaryKey()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var key1 = KeyFactory.CreateKeyFromValues(new object[] { 1, bytes });
        var key2 = KeyFactory.CreateKeyFromValues(new object[] { 1, new byte[] { 1, 2, 3 } });
        var key3 = KeyFactory.CreateKeyFromValues(new object[] { 1, new byte[] { 4, 5, 6 } });
        var originalHashCode = key1.GetHashCode();
        var dictionary = new Dictionary<DataLinqKey, string> { [key1] = "cached" };

        bytes[0] = 99;
        var exposedValue = (byte[])key1.GetValue(1)!;
        exposedValue[1] = 99;
        var rereadValue = (byte[])key1.GetValue(1)!;

        await Assert.That(exposedValue).IsNotSameReferenceAs(bytes);
        await Assert.That(rereadValue).IsNotSameReferenceAs(exposedValue);
        await Assert.That(rereadValue.SequenceEqual(new byte[] { 1, 2, 3 })).IsTrue();
        await Assert.That(key1.Equals(key2)).IsTrue();
        await Assert.That(key1.GetHashCode()).IsEqualTo(originalHashCode);
        await Assert.That(key1.GetHashCode()).IsEqualTo(key2.GetHashCode());
        await Assert.That(dictionary.TryGetValue(key2, out var cached)).IsTrue();
        await Assert.That(cached).IsEqualTo("cached");
        await Assert.That(key1.Equals(key3)).IsFalse();
    }

    [Test]
    public async Task DataLinqKey_FromProviderKey_DeepCopiesByteArrayComponents()
    {
        var bytes = new byte[] { 10, 20, 30 };
        var providerKey = new MutableProviderKey("tenant-1", bytes);
        var key = DataLinqKey.FromProviderKey(providerKey);
        var equivalent = DataLinqKey.FromValues(["tenant-1", new byte[] { 10, 20, 30 }]);
        var originalHashCode = key.GetHashCode();
        var dictionary = new Dictionary<DataLinqKey, string> { [key] = "cached" };

        bytes[0] = 99;
        var exposedValue = (byte[])key.GetValue(1)!;
        exposedValue[1] = 99;
        var rereadValue = (byte[])key.GetValue(1)!;

        await Assert.That(exposedValue).IsNotSameReferenceAs(bytes);
        await Assert.That(rereadValue).IsNotSameReferenceAs(exposedValue);
        await Assert.That(rereadValue.SequenceEqual(new byte[] { 10, 20, 30 })).IsTrue();
        await Assert.That(key).IsEqualTo(equivalent);
        await Assert.That(key.GetHashCode()).IsEqualTo(originalHashCode);
        await Assert.That(dictionary.TryGetValue(equivalent, out var cached)).IsTrue();
        await Assert.That(cached).IsEqualTo("cached");
    }

    [Test]
    public async Task DataLinqKeyComponents_BinaryIndexerReturnsDefensiveCopies()
    {
        var bytes = new byte[] { 7, 8, 9 };
        var components = DataLinqKeyComponents.FromValues("tenant-1", bytes);
        var equivalent = DataLinqKeyComponents.FromValues("tenant-1", new byte[] { 7, 8, 9 });
        var dictionary = new Dictionary<DataLinqKeyComponents, string> { [components] = "cached" };

        bytes[0] = 99;
        var exposedValue = (byte[])components[1]!;
        exposedValue[1] = 99;

        await Assert.That((byte[])components[1]!).IsEquivalentTo(new byte[] { 7, 8, 9 });
        await Assert.That(components).IsEqualTo(equivalent);
        await Assert.That(dictionary.TryGetValue(equivalent, out var cached)).IsTrue();
        await Assert.That(cached).IsEqualTo("cached");
    }

    [Test]
    [MethodDataSource(nameof(SimpleKeyTypeData))]
    public async Task SimpleKeyTypes_Equality_IsBasedOnValue(SimpleKeyCase testCase)
    {
        var key1 = KeyFactory.CreateKeyFromValue(testCase.Value1);
        var key2 = KeyFactory.CreateKeyFromValue(testCase.Value2);
        var key3 = KeyFactory.CreateKeyFromValue(testCase.DifferentValue);

        await Assert.That(key1.Equals(key2)).IsTrue();
        await Assert.That(key1.GetHashCode()).IsEqualTo(key2.GetHashCode());
        await Assert.That(key1.Equals(key3)).IsFalse();
        await Assert.That(key1.GetHashCode()).IsNotEqualTo(key3.GetHashCode());
    }

    [Test]
    public async Task KeyFactory_Handles_IntBasedEnum()
    {
        var activeKey = KeyFactory.CreateKeyFromValue(TestStatusEnum.Active);
        var pendingKey = KeyFactory.CreateKeyFromValue(TestStatusEnum.Pending);
        var activeAgainKey = KeyFactory.CreateKeyFromValue(TestStatusEnum.Active);

        await Assert.That(activeKey.ValueCount).IsEqualTo(1);
        await Assert.That(activeKey.GetValue(0)).IsEqualTo(1);
        await Assert.That(activeKey.Equals(activeAgainKey)).IsTrue();
        await Assert.That(activeKey.GetHashCode()).IsEqualTo(activeAgainKey.GetHashCode());
        await Assert.That(activeKey.Equals(pendingKey)).IsFalse();
    }

    [Test]
    public async Task KeyFactory_Handles_ByteBasedEnum()
    {
        var smallKey = KeyFactory.CreateKeyFromValue(TestByteEnum.Small);
        var largeKey = KeyFactory.CreateKeyFromValue(TestByteEnum.Large);
        var smallAgainKey = KeyFactory.CreateKeyFromValue(TestByteEnum.Small);

        await Assert.That(smallKey.ValueCount).IsEqualTo(1);
        await Assert.That(smallKey.GetValue(0)).IsEqualTo((byte)10);
        await Assert.That(smallKey.Equals(smallAgainKey)).IsTrue();
        await Assert.That(smallKey.GetHashCode()).IsEqualTo(smallAgainKey.GetHashCode());
        await Assert.That(smallKey.Equals(largeKey)).IsFalse();
    }

    [Test]
    public async Task DataLinqKey_Composite_WithEnum_Equality()
    {
        var key1 = KeyFactory.CreateKeyFromValues(new object[] { "product_a", TestStatusEnum.Active });
        var key2 = KeyFactory.CreateKeyFromValues(new object[] { "product_a", TestStatusEnum.Active });
        var key3 = KeyFactory.CreateKeyFromValues(new object[] { "product_a", TestStatusEnum.Pending });

        await Assert.That(key1.Equals(key2)).IsTrue();
        await Assert.That(key1.GetHashCode()).IsEqualTo(key2.GetHashCode());
        await Assert.That(key1.Equals(key3)).IsFalse();
    }

    [Test]
    public async Task DataLinqKey_ComponentReads_DoNotExposeMutableCompositeStorage()
    {
        var simpleKey = KeyFactory.CreateKeyFromValue("employee-1");
        var compositeKey = KeyFactory.CreateKeyFromValues(new object?[] { "employee-1", "dept-1" });

        await Assert.That(simpleKey.ValueCount).IsEqualTo(1);
        await Assert.That(compositeKey.ValueCount).IsEqualTo(2);
        await Assert.That(simpleKey.GetValue(0)).IsEqualTo("employee-1");
        await Assert.That(compositeKey.GetValue(0)).IsEqualTo("employee-1");
        await Assert.That(compositeKey.GetValue(1)).IsEqualTo("dept-1");
    }

    [Test]
    public async Task DataLinqKey_AllNullCompositeValues_CollapseToNull()
    {
        var key = KeyFactory.CreateKeyFromValues(new object?[] { null, null });

        await Assert.That(key.IsNull).IsTrue();
        await Assert.That(key).IsEqualTo(DataLinqKey.Null);
    }

    [Test]
    public async Task SimpleKeyValueReads_DoNotAllocateSnapshotArrays()
    {
        var key = KeyFactory.CreateKeyFromValue("employee-1");

        var allocatedBytes = MeasureKeyReads(key);

        await Assert.That(allocatedBytes).IsEqualTo(0);
    }

    [Test]
    public async Task DataLinqKeyCompositeValueReads_DoNotAllocateSnapshotArrays()
    {
        var key = KeyFactory.CreateKeyFromValues(new object?[] { "employee-1", "dept-1" });

        var allocatedBytes = MeasureKeyReads(key);

        await Assert.That(allocatedBytes).IsEqualTo(0);
    }

    public static IEnumerable<Func<SimpleKeyCase>> SimpleKeyTypeData()
    {
        var guid = Guid.NewGuid();
        var date = DateTime.Now;
        var dateOnly = DateOnly.FromDateTime(date);
        var timeOnly = TimeOnly.FromDateTime(date);

        yield return () => new SimpleKeyCase((sbyte)-10, (sbyte)-10, (sbyte)20);
        yield return () => new SimpleKeyCase((byte)10, (byte)10, (byte)20);
        yield return () => new SimpleKeyCase((short)1000, (short)1000, (short)2000);
        yield return () => new SimpleKeyCase((ushort)1000, (ushort)1000, (ushort)2000);
        yield return () => new SimpleKeyCase(100000, 100000, 200000);
        yield return () => new SimpleKeyCase((uint)100000, (uint)100000, (uint)200000);
        yield return () => new SimpleKeyCase(10000000000L, 10000000000L, 20000000000L);
        yield return () => new SimpleKeyCase(10000000000UL, 10000000000UL, 20000000000UL);
        yield return () => new SimpleKeyCase(guid, new Guid(guid.ToString()), Guid.NewGuid());
        yield return () => new SimpleKeyCase("hello", new string("hello".ToCharArray()), "world");
        yield return () => new SimpleKeyCase(date, new DateTime(date.Ticks), date.AddDays(1));
        yield return () => new SimpleKeyCase(dateOnly, new DateOnly(dateOnly.Year, dateOnly.Month, dateOnly.Day), dateOnly.AddDays(1));
        yield return () => new SimpleKeyCase(timeOnly, new TimeOnly(timeOnly.Ticks), timeOnly.AddHours(1));
        yield return () => new SimpleKeyCase(true, true, false);
        yield return () => new SimpleKeyCase(123.45m, 123.45m, 678.90m);
    }

    private static long MeasureKeyReads(DataLinqKey key)
    {
        _ = key.ValueCount;
        _ = key.GetValue(0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            _ = key.ValueCount;
            _ = key.GetValue(0);
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class MutableProviderKey(params object?[] values) : IProviderKey
    {
        public int ValueCount => values.Length;

        public object? GetValue(int index) => values[index];
    }
}
