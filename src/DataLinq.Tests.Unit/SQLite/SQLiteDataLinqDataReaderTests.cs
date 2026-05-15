using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Metadata;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;
using ThrowAway.Extensions;

namespace DataLinq.Tests.Unit.SQLite;

public class SQLiteDataLinqDataReaderTests
{
    [Test]
    public async Task GetValue_NumericEnumWithoutEnumMetadata_ConvertsValue()
    {
        var database = CreateDatabase();
        var statusColumn = database.TableModels
            .Single()
            .Model
            .ValueProperties["Status"]
            .Column;

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 2";

        using var sqliteReader = command.ExecuteReader();
        sqliteReader.Read();

        using var reader = new SQLiteDataLinqDataReader(sqliteReader);
        var value = reader.GetValue<ReaderNumericStatus>(statusColumn, 0);

        await Assert.That(value).IsEqualTo(ReaderNumericStatus.Active);
    }

    private static DatabaseDefinition CreateDatabase()
    {
        var draft = new MetadataDatabaseDraft(
            "ReaderDb",
            new CsTypeDeclaration("ReaderDb", "DataLinq.Tests.Unit.SQLite", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration("ReaderRow", "DataLinq.Tests.Unit.SQLite", ModelCsType.Class))
                    {
                        ValueProperties =
                        [
                            new MetadataValuePropertyDraft(
                                "Id",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("id")
                                {
                                    PrimaryKey = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "INTEGER")]
                                }),
                            new MetadataValuePropertyDraft(
                                "Status",
                                new CsTypeDeclaration(typeof(ReaderNumericStatus)),
                                new MetadataColumnDraft("status")
                                {
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "INTEGER")]
                                })
                        ]
                    },
                    new MetadataTableDraft("rows"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private enum ReaderNumericStatus : short
    {
        Unknown = 0,
        Active = 2
    }
}
