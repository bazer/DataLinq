using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Instances;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class ProviderKeyComponentsTests
{
    [Test]
    public async Task NeutralSourceLoading_AcceptsOnlyIntegralOrExactScalarGuidKeys()
    {
        var integral = CreateIntegralTable();
        var directGuid = CreateGuidTable(converted: false, componentCount: 1);
        var convertedGuid = CreateGuidTable(converted: true, componentCount: 1);
        var compositeGuid = CreateGuidTable(converted: false, componentCount: 2);

        await Assert.That(ProviderKeyComponents.SupportsNeutralSourceRowLoading(
            integral,
            DatabaseType.Unknown)).IsTrue();
        await Assert.That(ProviderKeyComponents.SupportsNeutralSourceRowLoading(
            directGuid,
            DatabaseType.SQLite)).IsTrue();
        await Assert.That(ProviderKeyComponents.SupportsNeutralSourceRowLoading(
            convertedGuid,
            DatabaseType.SQLite)).IsTrue();

        await Assert.That(ProviderKeyComponents.SupportsNeutralSourceRowLoading(
            directGuid,
            DatabaseType.MySQL)).IsFalse();
        await Assert.That(ProviderKeyComponents.SupportsNeutralSourceRowLoading(
            directGuid,
            DatabaseType.Unknown)).IsFalse();
        await Assert.That(ProviderKeyComponents.SupportsNeutralSourceRowLoading(
            compositeGuid,
            DatabaseType.SQLite)).IsFalse();
    }

    [Test]
    public async Task ExactCanonicalKey_RejectsModelValuesWrongTypesAndWrongShapes()
    {
        var columns = CreateIntegralTable().PrimaryKeyColumns;

        var accepted = ProviderKeyComponents.TryCreateExactCanonicalKey(
            42,
            columns,
            out var canonicalKey);
        var dynamicAccepted = ProviderKeyComponents.TryCreateExactCanonicalKey(
            DataLinqKey.FromValue(43),
            columns,
            out var dynamicCanonicalKey);
        var modelRejected = ProviderKeyComponents.TryCreateExactCanonicalKey(
            new IntegralKeyId(42),
            columns,
            out _);
        var wrongTypeRejected = ProviderKeyComponents.TryCreateExactCanonicalKey(
            42L,
            columns,
            out _);
        var wrongShapeRejected = ProviderKeyComponents.TryCreateExactCanonicalKey(
            DataLinqKey.FromValues([42, 43]),
            columns,
            out _);

        await Assert.That(ProviderKeyComponents.HasOnlyIntegralCanonicalComponents(columns)).IsTrue();
        await Assert.That(accepted).IsTrue();
        await Assert.That(canonicalKey.GetValue(0)).IsEqualTo(42);
        await Assert.That(dynamicAccepted).IsTrue();
        await Assert.That(dynamicCanonicalKey.GetValue(0)).IsEqualTo(43);
        await Assert.That(modelRejected).IsFalse();
        await Assert.That(wrongTypeRejected).IsFalse();
        await Assert.That(wrongShapeRejected).IsFalse();
    }

    private static TableDefinition CreateIntegralTable()
    {
        var draft = new MetadataDatabaseDraft(
            "IntegralKeyDb",
            new CsTypeDeclaration(typeof(ProviderKeyComponentsTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(IntegralKeyRow)))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id") { PrimaryKey = true })
                        ]
                    },
                    new MetadataTableDraft("integral_key_rows"))
            ]
        };

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels[0]
            .Table;
    }

    private static TableDefinition CreateGuidTable(bool converted, int componentCount)
    {
        var converter = converted
            ? new MetadataScalarConverterDraft(
                new CsTypeDeclaration(typeof(GuidKeyId)),
                new CsTypeDeclaration(typeof(Guid)),
                new CsTypeDeclaration(typeof(GuidKeyIdConverter)),
                static () => new GuidKeyIdConverter())
            {
                Origin = ScalarConverterOrigin.Property
            }
            : null;
        var properties = new List<MetadataValuePropertyDraft>(componentCount);
        for (var index = 0; index < componentCount; index++)
        {
            properties.Add(new MetadataValuePropertyDraft(
                $"Id{index}",
                new CsTypeDeclaration(converted ? typeof(GuidKeyId) : typeof(Guid)),
                new MetadataColumnDraft($"id_{index}")
                {
                    PrimaryKey = true,
                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "BLOB")]
                })
            {
                Attributes =
                [
                    new GuidStorageAttribute(
                        DatabaseType.SQLite,
                        GuidStorageFormat.Binary16Rfc4122)
                ],
                ScalarConverter = converter
            });
        }

        var draft = new MetadataDatabaseDraft(
            $"GuidKeyDb{converted}{componentCount}",
            new CsTypeDeclaration(typeof(ProviderKeyComponentsTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(GuidKeyRow)))
                    {
                        ValueProperties = properties
                    },
                    new MetadataTableDraft("guid_key_rows"))
            ]
        };

        return new MetadataDefinitionFactory()
            .Build(draft)
            .ValueOrException()
            .TableModels.Single()
            .Table;
    }

    private sealed class IntegralKeyRow;
    private sealed class GuidKeyRow;
    private readonly record struct IntegralKeyId(int Value);
    private readonly record struct GuidKeyId(Guid Value);

    private sealed class GuidKeyIdConverter : DataLinqScalarConverter<GuidKeyId, Guid>
    {
        public override Guid ToProvider(GuidKeyId modelValue, in ScalarConversionContext context) =>
            modelValue.Value;

        public override GuidKeyId FromProvider(Guid providerValue, in ScalarConversionContext context) =>
            new(providerValue);
    }
}
