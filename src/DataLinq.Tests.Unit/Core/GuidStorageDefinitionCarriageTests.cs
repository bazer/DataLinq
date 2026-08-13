using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.Testing;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.Core;

public sealed class GuidStorageDefinitionCarriageTests
{
    [Test]
    public async Task TypedDraftAndSnapshot_PreserveProviderKeyedDefinitions()
    {
        GuidStorageDefinition[] definitions =
        [
            new(
                DatabaseType.MySQL,
                GuidStorageFormat.Binary16LittleEndian,
                IsExplicit: false),
            new(
                DatabaseType.MariaDB,
                GuidStorageFormat.NativeUuid,
                IsExplicit: false),
            new(
                DatabaseType.SQLite,
                GuidStorageFormat.Text36,
                IsExplicit: false)
        ];
        var database = new MetadataDefinitionFactory()
            .Build(CreateDraft(definitions))
            .ValueOrException();
        var column = database.TableModels.Single().Table.Columns.Single();
        var copiedColumn = MetadataDefinitionSnapshot
            .Copy(database)
            .TableModels.Single()
            .Table.Columns.Single();

        await Assert.That(column.IsGuidColumn).IsTrue();
        await Assert.That(column.GuidStorageDefinitions.ToArray()).IsEquivalentTo(definitions);
        await Assert.That(column.GetGuidStorageFor(DatabaseType.MySQL)).IsEqualTo(definitions[0]);
        await Assert.That(column.GetGuidStorageFor(DatabaseType.MariaDB)).IsEqualTo(definitions[1]);
        await Assert.That(column.GetGuidStorageFor(DatabaseType.SQLite)).IsEqualTo(definitions[2]);
        await Assert.That(column.GetGuidStorageFor(DatabaseType.Default)).IsNull();
        await Assert.That(copiedColumn.GuidStorageDefinitions.ToArray()).IsEquivalentTo(definitions);
        await Assert.That(MetadataEquivalenceDigest.CreateText(database)).Contains(
            "guidStorage=MySQL:Binary16LittleEndian:False,MariaDB:NativeUuid:False,SQLite:Text36:False");
    }

    private static MetadataDatabaseDraft CreateDraft(
        IReadOnlyList<GuidStorageDefinition> definitions) =>
        new(
            "GuidStorageCarriageDb",
            new CsTypeDeclaration(typeof(GuidStorageDefinitionCarriageTests)))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(
                        new CsTypeDeclaration(typeof(GuidStorageDefinitionCarriageRow)))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(Guid)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    GuidStorageDefinitions = definitions
                                })
                            {
                                Attributes =
                                [
                                    new PrimaryKeyAttribute(),
                                    new ColumnAttribute("id")
                                ]
                            }
                        ]
                    },
                    new MetadataTableDraft("guid_storage_carriage_rows"))
            ]
        };

    private sealed class GuidStorageDefinitionCarriageRow;
}
