using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Metadata;

namespace DataLinq.Tests.Unit.Core;

public sealed record PrimitiveIdentityCase(Type ClrType, TableKeyComponentStoreKind StoreKind);

public sealed record TypedIdFixtureCase(object First, object Equal, object Different);

public sealed record CanonicalProviderKeyCase(Type ClrType, object First, object Equal, object Different);

public readonly record struct CharacterizationIntId(int Value);
public readonly record struct CharacterizationLongId(long Value);
public readonly record struct CharacterizationGuidId(Guid Value);
public readonly record struct CharacterizationStringId(string Value);

public class ValueAndUuidCharacterizationTests
{
    private const string KnownText36 = "00112233-4455-6677-8899-aabbccddeeff";
    private const string KnownText32 = "00112233445566778899aabbccddeeff";
    private const string KnownLittleEndianHex = "33221100554477668899AABBCCDDEEFF";
    private const string KnownRfc4122Hex = "00112233445566778899AABBCCDDEEFF";
    private static readonly Guid KnownGuid = Guid.ParseExact(KnownText36, "D");

    [Test]
    [MethodDataSource(nameof(PrimitiveIdentityCases))]
    public async Task PrimitiveKeyMetadata_UsesIdentityProviderRepresentation(PrimitiveIdentityCase testCase)
    {
        var component = CreateKeyComponent(testCase.ClrType);

        await Assert.That(component.ModelClrType).IsEqualTo(testCase.ClrType);
        await Assert.That(component.ProviderClrType).IsEqualTo(testCase.ClrType);
        await Assert.That(component.ProviderCsType).IsEqualTo(component.ModelCsType);
        await Assert.That(component.ProviderStoreKind).IsEqualTo(testCase.StoreKind);
        await Assert.That(component.ScalarConverterHandle).IsNull();
        await Assert.That(component.HasScalarConverter).IsFalse();
    }

    [Test]
    [MethodDataSource(nameof(TypedIdFixtureCases))]
    public async Task TypedIdFixtures_HaveValueEqualityAndHashSemantics(TypedIdFixtureCase testCase)
    {
        var values = new HashSet<object> { testCase.First };

        await Assert.That(testCase.First).IsEqualTo(testCase.Equal);
        await Assert.That(testCase.First.GetHashCode()).IsEqualTo(testCase.Equal.GetHashCode());
        await Assert.That(testCase.First).IsNotEqualTo(testCase.Different);
        await Assert.That(values.Add(testCase.Equal)).IsFalse();
        await Assert.That(values.Add(testCase.Different)).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(CanonicalProviderKeyCases))]
    public async Task CanonicalProviderKeys_UseClrValueEqualityAndHashing(CanonicalProviderKeyCase testCase)
    {
        var first = DataLinqKey.FromValue(testCase.First);
        var equal = DataLinqKey.FromValue(testCase.Equal);
        var different = DataLinqKey.FromValue(testCase.Different);
        var firstComponents = DataLinqKeyComponents.FromValue(testCase.First);
        var equalComponents = DataLinqKeyComponents.FromValue(testCase.Equal);

        await Assert.That(first.GetValue(0)!.GetType()).IsEqualTo(testCase.ClrType);
        await Assert.That(first).IsEqualTo(equal);
        await Assert.That(first.GetHashCode()).IsEqualTo(equal.GetHashCode());
        await Assert.That(first).IsNotEqualTo(different);
        await Assert.That(firstComponents).IsEqualTo(equalComponents);
        await Assert.That(firstComponents.GetHashCode()).IsEqualTo(equalComponents.GetHashCode());
    }

    [Test]
    public async Task CanonicalProviderKeys_PreserveTypeAndCompositeBoundaries()
    {
        var intKey = DataLinqKey.FromValue(42);
        var longKey = DataLinqKey.FromValue(42L);
        var firstComposite = DataLinqKey.FromValues([42, 42L, KnownGuid, "tenant-01"]);
        var equalComposite = DataLinqKey.FromValues([42, 42L, new Guid(KnownGuid.ToByteArray()), new string("tenant-01".ToCharArray())]);

        await Assert.That(intKey).IsNotEqualTo(longKey);
        await Assert.That(firstComposite).IsEqualTo(equalComposite);
        await Assert.That(firstComposite.GetHashCode()).IsEqualTo(equalComposite.GetHashCode());
    }

    [Test]
    public async Task CanonicalGuidKey_IsDistinctFromPhysicalRepresentations()
    {
        var canonical = DataLinqKey.FromValue(KnownGuid);
        var text = DataLinqKey.FromValue(KnownText36);
        var littleEndianBytes = DataLinqKey.FromValue(Convert.FromHexString(KnownLittleEndianHex));
        var rfc4122Bytes = DataLinqKey.FromValue(Convert.FromHexString(KnownRfc4122Hex));

        await Assert.That(canonical).IsNotEqualTo(text);
        await Assert.That(canonical).IsNotEqualTo(littleEndianBytes);
        await Assert.That(canonical).IsNotEqualTo(rfc4122Bytes);
    }

    [Test]
    public async Task GuidKnownVector_NativeAndTextForms_AreStable()
    {
        var nativeProviderValue = (object)KnownGuid;

        await Assert.That(nativeProviderValue.GetType()).IsEqualTo(typeof(Guid));
        await Assert.That((Guid)nativeProviderValue).IsEqualTo(KnownGuid);
        await Assert.That(KnownGuid.ToString("D")).IsEqualTo(KnownText36);
        await Assert.That(KnownGuid.ToString("N")).IsEqualTo(KnownText32);
        await Assert.That(Guid.ParseExact(KnownText36, "D")).IsEqualTo(KnownGuid);
        await Assert.That(Guid.ParseExact(KnownText32, "N")).IsEqualTo(KnownGuid);
    }

    [Test]
    public async Task GuidKnownVector_LittleEndianBinary_MatchesCompatibilityLayout()
    {
        var expected = Convert.FromHexString(KnownLittleEndianHex);
        var actual = KnownGuid.ToByteArray();

        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        await Assert.That(new Guid(expected)).IsEqualTo(KnownGuid);
    }

    [Test]
    public async Task GuidKnownVector_Rfc4122Binary_MatchesCanonicalStringOrder()
    {
        var expected = Convert.FromHexString(KnownRfc4122Hex);
        var actual = KnownGuid.ToByteArray(bigEndian: true);

        await Assert.That(actual.SequenceEqual(expected)).IsTrue();
        await Assert.That(new Guid(expected, bigEndian: true)).IsEqualTo(KnownGuid);
    }

    [Test]
    public async Task GuidKnownVector_BinaryLayouts_AreNotInterchangeable()
    {
        var littleEndian = Convert.FromHexString(KnownLittleEndianHex);
        var rfc4122 = Convert.FromHexString(KnownRfc4122Hex);

        await Assert.That(littleEndian.SequenceEqual(rfc4122)).IsFalse();
        await Assert.That(new Guid(littleEndian)).IsEqualTo(KnownGuid);
        await Assert.That(new Guid(rfc4122, bigEndian: true)).IsEqualTo(KnownGuid);
    }

    public static IEnumerable<Func<PrimitiveIdentityCase>> PrimitiveIdentityCases()
    {
        yield return () => new PrimitiveIdentityCase(typeof(int), TableKeyComponentStoreKind.Int32);
        yield return () => new PrimitiveIdentityCase(typeof(long), TableKeyComponentStoreKind.Int64);
        yield return () => new PrimitiveIdentityCase(typeof(Guid), TableKeyComponentStoreKind.Guid);
        yield return () => new PrimitiveIdentityCase(typeof(string), TableKeyComponentStoreKind.String);
    }

    public static IEnumerable<Func<TypedIdFixtureCase>> TypedIdFixtureCases()
    {
        yield return () => new TypedIdFixtureCase(new CharacterizationIntId(42), new CharacterizationIntId(42), new CharacterizationIntId(43));
        yield return () => new TypedIdFixtureCase(new CharacterizationLongId(4_294_967_296L), new CharacterizationLongId(4_294_967_296L), new CharacterizationLongId(4_294_967_297L));
        yield return () => new TypedIdFixtureCase(new CharacterizationGuidId(KnownGuid), new CharacterizationGuidId(new Guid(KnownGuid.ToByteArray())), new CharacterizationGuidId(Guid.Empty));
        yield return () => new TypedIdFixtureCase(new CharacterizationStringId("tenant-01"), new CharacterizationStringId(new string("tenant-01".ToCharArray())), new CharacterizationStringId("TENANT-01"));
    }

    public static IEnumerable<Func<CanonicalProviderKeyCase>> CanonicalProviderKeyCases()
    {
        yield return () => new CanonicalProviderKeyCase(typeof(int), 42, 42, 43);
        yield return () => new CanonicalProviderKeyCase(typeof(long), 4_294_967_296L, 4_294_967_296L, 4_294_967_297L);
        yield return () => new CanonicalProviderKeyCase(typeof(Guid), KnownGuid, new Guid(KnownGuid.ToByteArray()), Guid.Empty);
        yield return () => new CanonicalProviderKeyCase(typeof(string), "tenant-01", new string("tenant-01".ToCharArray()), "TENANT-01");
    }

    private static TableKeyComponentDefinition CreateKeyComponent(Type clrType)
    {
        var model = new ModelDefinition(new CsTypeDeclaration("CharacterizationModel", "DataLinq.Tests.Unit.Core", ModelCsType.Class));
        var table = new TableDefinition("characterization_values");
        var property = new ValueProperty("Id", new CsTypeDeclaration(clrType), model, []);
        var column = new ColumnDefinition("id", table);
        column.SetValuePropertyCore(property);

        return TableKeyComponentDefinition.Create(column, keyOrdinal: 0);
    }
}
