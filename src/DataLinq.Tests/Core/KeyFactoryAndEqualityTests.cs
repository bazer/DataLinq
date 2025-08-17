using System;
using System.Collections.Generic;
using DataLinq.Instances;
using Xunit;

namespace DataLinq.Tests.Core;

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

public class KeyFactoryAndEqualityTests
{
    // --- Tests for Tricky Types ---

    [Fact]
    public void BytesKey_Equality_IsBasedOnContent()
    {
        // Arrange
        var data1 = new byte[] { 1, 2, 3, 4, 5 };
        var data2 = new byte[] { 1, 2, 3, 4, 5 }; // Same content, different instance
        var data3 = new byte[] { 6, 7, 8 };       // Different content

        Assert.False(ReferenceEquals(data1, data2)); // Ensure they are different objects in memory

        var key1 = KeyFactory.CreateKeyFromValue(data1);
        var key2 = KeyFactory.CreateKeyFromValue(data2);
        var key3 = KeyFactory.CreateKeyFromValue(data3);

        // Assert
        Assert.IsType<BytesKey>(key1);
        Assert.True(key1.Equals(key2), "BytesKeys with the same content should be equal.");
        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());

        Assert.False(key1.Equals(key3), "BytesKeys with different content should not be equal.");
        // Hash codes *could* collide, but are extremely unlikely to with this data.
        Assert.NotEqual(key1.GetHashCode(), key3.GetHashCode());
    }

    [Fact]
    public void CompositeKey_Equality_IsBasedOnContentAndOrder()
    {
        // Arrange
        var key1 = KeyFactory.CreateKeyFromValues(new object[] { 123, "test", new Guid("f81d4fae-7dec-11d0-a765-00a0c91e6bf6") });
        var key2 = KeyFactory.CreateKeyFromValues(new object[] { 123, "test", new Guid("f81d4fae-7dec-11d0-a765-00a0c91e6bf6") }); // Same
        var key3 = KeyFactory.CreateKeyFromValues(new object[] { "test", 123, new Guid("f81d4fae-7dec-11d0-a765-00a0c91e6bf6") }); // Different order
        var key4 = KeyFactory.CreateKeyFromValues(new object[] { 456, "different" }); // Different values

        // Assert
        Assert.True(key1.Equals(key2));
        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());

        Assert.False(key1.Equals(key3), "CompositeKey equality should be order-sensitive.");
        Assert.NotEqual(key1.GetHashCode(), key3.GetHashCode());

        Assert.False(key1.Equals(key4));
    }

    [Fact]
    public void CompositeKey_WithBytesKey_Equality()
    {
        // Arrange
        var data1 = new byte[] { 1, 2, 3 };
        var data2 = new byte[] { 1, 2, 3 };

        var key1 = KeyFactory.CreateKeyFromValues(new object[] { 1, data1 });
        var key2 = KeyFactory.CreateKeyFromValues(new object[] { 1, data2 });
        var key3 = KeyFactory.CreateKeyFromValues(new object[] { 1, new byte[] { 4, 5, 6 } });

        // Assert
        Assert.True(key1.Equals(key2));
        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
        Assert.False(key1.Equals(key3));
    }


    // --- Theory for Simple Value Types ---

    public static IEnumerable<object[]> SimpleKeyTypeData()
    {
        var guid = Guid.NewGuid();
        var date = DateTime.Now;
        var dateOnly = DateOnly.FromDateTime(date);
        var timeOnly = TimeOnly.FromDateTime(date);

        yield return new object[] { (sbyte)-10, (sbyte)-10, (sbyte)20 };
        yield return new object[] { (byte)10, (byte)10, (byte)20 };
        yield return new object[] { (short)1000, (short)1000, (short)2000 };
        yield return new object[] { (ushort)1000, (ushort)1000, (ushort)2000 };
        yield return new object[] { 100000, 100000, 200000 };
        yield return new object[] { (uint)100000, (uint)100000, (uint)200000 };
        yield return new object[] { 10000000000L, 10000000000L, 20000000000L };
        yield return new object[] { 10000000000UL, 10000000000UL, 20000000000UL };
        yield return new object[] { guid, new Guid(guid.ToString()), Guid.NewGuid() };
        yield return new object[] { "hello", new string("hello".ToCharArray()), "world" };
        yield return new object[] { date, new DateTime(date.Ticks), date.AddDays(1) };
        yield return new object[] { dateOnly, new DateOnly(dateOnly.Year, dateOnly.Month, dateOnly.Day), dateOnly.AddDays(1) };
        yield return new object[] { timeOnly, new TimeOnly(timeOnly.Ticks), timeOnly.AddHours(1) };
        yield return new object[] { true, true, false };
        yield return new object[] { 123.45m, 123.45m, 678.90m };
    }

    [Theory]
    [MemberData(nameof(SimpleKeyTypeData))]
    public void SimpleKeyTypes_Equality_IsBasedOnValue<T>(T value1, T value2, T differentValue)
    {
        // Arrange
        var key1 = KeyFactory.CreateKeyFromValue(value1);
        var key2 = KeyFactory.CreateKeyFromValue(value2);
        var key3 = KeyFactory.CreateKeyFromValue(differentValue);

        // Assert
        Assert.Equal(key1, key2);
        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
        Assert.NotEqual(key1, key3);
        // Note: Hash code collisions are theoretically possible but highly unlikely with this data.
        Assert.NotEqual(key1.GetHashCode(), key3.GetHashCode());
    }


    // --- Tests for Enums ---

    [Fact]
    public void KeyFactory_Handles_IntBasedEnum()
    {
        // Arrange
        var active = TestStatusEnum.Active;
        var pending = TestStatusEnum.Pending;
        var activeAgain = TestStatusEnum.Active;

        var activeKey = KeyFactory.CreateKeyFromValue(active);
        var pendingKey = KeyFactory.CreateKeyFromValue(pending);
        var activeAgainKey = KeyFactory.CreateKeyFromValue(activeAgain);

        // Assert
        // The factory should create an IntKey because the enum's underlying type is int.
        Assert.IsType<IntKey>(activeKey);
        Assert.Equal(1, ((IntKey)activeKey).Value);

        // Check equality
        Assert.Equal(activeKey, activeAgainKey);
        Assert.Equal(activeKey.GetHashCode(), activeAgainKey.GetHashCode());
        Assert.NotEqual(activeKey, pendingKey);
    }

    [Fact]
    public void KeyFactory_Handles_ByteBasedEnum()
    {
        // Arrange
        var small = TestByteEnum.Small;
        var large = TestByteEnum.Large;
        var smallAgain = TestByteEnum.Small;

        var smallKey = KeyFactory.CreateKeyFromValue(small);
        var largeKey = KeyFactory.CreateKeyFromValue(large);
        var smallAgainKey = KeyFactory.CreateKeyFromValue(smallAgain);

        // Assert
        // The factory should create a ByteKey because the enum's underlying type is byte.
        Assert.IsType<ByteKey>(smallKey);
        Assert.Equal((byte)10, ((ByteKey)smallKey).Value);

        // Check equality
        Assert.Equal(smallKey, smallAgainKey);
        Assert.Equal(smallKey.GetHashCode(), smallAgainKey.GetHashCode());
        Assert.NotEqual(smallKey, largeKey);
    }

    [Fact]
    public void CompositeKey_WithEnum_Equality()
    {
        // Arrange
        var key1 = KeyFactory.CreateKeyFromValues(new object[] { "product_a", TestStatusEnum.Active });
        var key2 = KeyFactory.CreateKeyFromValues(new object[] { "product_a", TestStatusEnum.Active });
        var key3 = KeyFactory.CreateKeyFromValues(new object[] { "product_a", TestStatusEnum.Pending });

        // Assert
        Assert.Equal(key1, key2);
        Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
        Assert.NotEqual(key1, key3);
    }
}