using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.ErrorHandling;
using DataLinq.Metadata;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class GuidStorageMetadataTests
{
    [Test]
    public async Task Build_PreservesDefaultAndProviderScopedDeclarations()
    {
        var database = new MetadataDefinitionFactory()
            .Build(CreateDraft(
                new GuidStorageAttribute(GuidStorageFormat.Text36),
                new GuidStorageAttribute(
                    DatabaseType.MySQL,
                    GuidStorageFormat.Binary16Rfc4122)))
            .ValueOrException();

        var declarations = database.TableModels[0]
            .Model.ValueProperties["Id"]
            .Attributes
            .OfType<GuidStorageAttribute>()
            .OrderBy(x => x.DatabaseType)
            .ToArray();

        await Assert.That(declarations.Length).IsEqualTo(2);
        await Assert.That(declarations[0].DatabaseType).IsEqualTo(DatabaseType.Default);
        await Assert.That(declarations[0].Format).IsEqualTo(GuidStorageFormat.Text36);
        await Assert.That(declarations[1].DatabaseType).IsEqualTo(DatabaseType.MySQL);
        await Assert.That(declarations[1].Format)
            .IsEqualTo(GuidStorageFormat.Binary16Rfc4122);
    }

    [Test]
    public async Task Build_RejectsDuplicateProviderDeclarations()
    {
        var draft = MetadataDefinitionDraft.FromTypedMetadata(CreateDraft(
            new GuidStorageAttribute(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian),
            new GuidStorageAttribute(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)));
        var result = new MetadataDefinitionFactory().Build(draft);

        await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
        await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
        await Assert.That(failure.Message).Contains("multiple [GuidStorage] attributes");
        await Assert.That(failure.Message).Contains(nameof(DatabaseType.MySQL));
    }

    [Test]
    public async Task Build_RejectsUndefinedProviderAndFormatValues()
    {
        GuidStorageAttribute[] invalidDeclarations =
        [
            new(DatabaseType.Unknown, GuidStorageFormat.Text36),
            new((DatabaseType)int.MaxValue, GuidStorageFormat.Text36),
            new((GuidStorageFormat)int.MaxValue)
        ];

        foreach (var declaration in invalidDeclarations)
        {
            var result = new MetadataDefinitionFactory().Build(CreateDraft(declaration));

            await Assert.That(result.TryUnwrap(out _, out var failure)).IsFalse();
            await Assert.That(failure.FailureType).IsEqualTo(DLFailureType.InvalidModel);
            await Assert.That(failure.Message).Contains("Guid storage attribute");
        }
    }

    private static MetadataDatabaseDraft CreateDraft(params GuidStorageAttribute[] declarations)
    {
        var attributes = declarations.Cast<Attribute>()
            .Prepend(new PrimaryKeyAttribute())
            .Prepend(new ColumnAttribute("id"))
            .ToArray();

        return new MetadataDatabaseDraft(
            "GuidStorageMetadataDb",
            new CsTypeDeclaration(typeof(GuidStorageMetadataTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration(typeof(GuidStorageMetadataRow)))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(Guid)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes =
                                    [
                                        new DatabaseColumnType(DatabaseType.MySQL, "binary", 16),
                                        new DatabaseColumnType(DatabaseType.MariaDB, "char", 36),
                                        new DatabaseColumnType(DatabaseType.SQLite, "TEXT")
                                    ]
                                })
                            {
                                Attributes = attributes
                            }
                        ]
                    },
                    new MetadataTableDraft("guid_storage_metadata_rows"))
            ]
        };
    }

    private sealed class GuidStorageMetadataRow
    {
    }
}
