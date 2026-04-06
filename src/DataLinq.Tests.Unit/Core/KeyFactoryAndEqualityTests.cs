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
    public async Task BytesKey_Equality_IsBasedOnContent()
    {
        var data1 = new byte[] { 1, 2, 3, 4, 5 };
        var data2 = new byte[] { 1, 2, 3, 4, 5 };
        var data3 = new byte[] { 6, 7, 8 };

        var key1 = KeyFactory.CreateKeyFromValue(data1);
        var key2 = KeyFactory.CreateKeyFromValue(data2);
        var key3 = KeyFactory.CreateKeyFromValue(data3);

        await Assert.That(key1).IsTypeOf<BytesKey>();
        await Assert.That(key1.Equals(key2)).IsTrue();
        await Assert.That(key1.GetHashCode()).IsEqualTo(key2.GetHashCode());
        await Assert.That(key1.Equals(key3)).IsFalse();
        await Assert.That(key1.GetHashCode()).IsNotEqualTo(key3.GetHashCode());
    }

    [Test]
    public async Task CompositeKey_Equality_IsBasedOnContentAndOrder()
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
    public async Task CompositeKey_WithBytesKey_Equality()
    {
        var key1 = KeyFactory.CreateKeyFromValues(new object[] { 1, new byte[] { 1, 2, 3 } });
        var key2 = KeyFactory.CreateKeyFromValues(new object[] { 1, new byte[] { 1, 2, 3 } });
        var key3 = KeyFactory.CreateKeyFromValues(new object[] { 1, new byte[] { 4, 5, 6 } });

        await Assert.That(key1.Equals(key2)).IsTrue();
        await Assert.That(key1.GetHashCode()).IsEqualTo(key2.GetHashCode());
        await Assert.That(key1.Equals(key3)).IsFalse();
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

        await Assert.That(activeKey).IsTypeOf<IntKey>();
        await Assert.That(((IntKey)activeKey).Value).IsEqualTo(1);
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

        await Assert.That(smallKey).IsTypeOf<ByteKey>();
        await Assert.That(((ByteKey)smallKey).Value).IsEqualTo((byte)10);
        await Assert.That(smallKey.Equals(smallAgainKey)).IsTrue();
        await Assert.That(smallKey.GetHashCode()).IsEqualTo(smallAgainKey.GetHashCode());
        await Assert.That(smallKey.Equals(largeKey)).IsFalse();
    }

    [Test]
    public async Task CompositeKey_WithEnum_Equality()
    {
        var key1 = KeyFactory.CreateKeyFromValues(new object[] { "product_a", TestStatusEnum.Active });
        var key2 = KeyFactory.CreateKeyFromValues(new object[] { "product_a", TestStatusEnum.Active });
        var key3 = KeyFactory.CreateKeyFromValues(new object[] { "product_a", TestStatusEnum.Pending });

        await Assert.That(key1.Equals(key2)).IsTrue();
        await Assert.That(key1.GetHashCode()).IsEqualTo(key2.GetHashCode());
        await Assert.That(key1.Equals(key3)).IsFalse();
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
}
