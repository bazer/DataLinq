using System;
using System.Threading.Tasks;
using DataLinq.Instances;

namespace DataLinq.Tests.Unit.Core;

public sealed class ReferenceKeyTests
{
    [Test]
    public async Task ReferenceAbsence_IsSeparateFromCompositeKeyIdentity()
    {
        var partial = DataLinqKey.FromValues([17, null]);
        await Assert.That(partial.IsNull).IsFalse();
        await Assert.That(ProviderKeyComponents.IsNull(partial)).IsFalse();
        await Assert.That(ProviderKeyComponents.HasNullReferenceComponent(partial)).IsTrue();
        await Assert.That(ProviderKeyComponents.HasNullReferenceComponent(DataLinqKey.FromValues([null, 17]))).IsTrue();
        await Assert.That(ProviderKeyComponents.HasNullReferenceComponent(DataLinqKey.Null)).IsTrue();
        await Assert.That(ProviderKeyComponents.HasNullReferenceComponent(DataLinqKey.FromValues([17, 19]))).IsFalse();
    }

    [Test]
    public async Task TypedProviderAndScalarReferences_PreserveNullAndValidComponents()
    {
        await Assert.That(ProviderKeyComponents.HasNullReferenceComponent(new CompositeKey(17, null))).IsTrue();
        await Assert.That(ProviderKeyComponents.HasNullReferenceComponent(new CompositeKey(DBNull.Value, 19))).IsTrue();
        await Assert.That(ProviderKeyComponents.HasNullReferenceComponent(new CompositeKey(17, 19))).IsFalse();
        await Assert.That(ProviderKeyComponents.HasNullReferenceComponent(17)).IsFalse();
        await Assert.That(ProviderKeyComponents.HasNullReferenceComponent("key")).IsFalse();
        await Assert.That(ProviderKeyComponents.HasNullReferenceComponent(DBNull.Value)).IsTrue();
    }

    private readonly record struct CompositeKey(object? First, object? Second) : IProviderKey
    {
        public int ValueCount => 2;
        public object? GetValue(int index) => index switch
        {
            0 => First, 1 => Second, _ => throw new IndexOutOfRangeException()
        };
    }
}
