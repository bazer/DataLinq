using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Core.Factories;
using DataLinq.Exceptions;
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

    [Test]
    public async Task GetValue_SqlNullForRequiredColumnThrowsFocusedException()
    {
        var statusColumn = CreateDatabase().TableModels.Single().Model.ValueProperties["Status"].Column;
        using var reader = CreateReader("SELECT NULL");

        var exception = Capture<DataLinqNullabilityMismatchException>(() =>
            reader.GetValue<ReaderNumericStatus>(statusColumn, 0));

        await Assert.That(exception.MismatchKind)
            .IsEqualTo(DataLinqNullabilityMismatchKind.DatabaseColumn);
        await Assert.That(exception.TableName).IsEqualTo("rows");
        await Assert.That(exception.ColumnName).IsEqualTo("status");
        await Assert.That(exception.PropertyName).IsEqualTo("Status");
        await Assert.That(exception.SourceName).IsEqualTo("reader:SQLite");
        await Assert.That(exception.Message).DoesNotContain("connection");
    }

    [Test]
    public async Task GetValue_SqlNullRequiresNullableModelAndRequestedClrType()
    {
        var optionalColumn = CreateNullableDatabase().TableModels.Single().Model.ValueProperties["OptionalNumber"].Column;

        using var nullableReader = CreateReader("SELECT NULL");
        var nullableValue = nullableReader.GetValue<int?>(optionalColumn, 0);

        using var requiredReader = CreateReader("SELECT NULL");
        var exception = Capture<DataLinqNullabilityMismatchException>(() =>
            requiredReader.GetValue<int>(optionalColumn, 0));

        await Assert.That(nullableValue).IsNull();
        await Assert.That(exception.MismatchKind)
            .IsEqualTo(DataLinqNullabilityMismatchKind.RequestedClrType);
        await Assert.That(exception.ExpectedClrType).IsEqualTo(typeof(int));
        await Assert.That(exception.Message).Contains("Request a nullable CLR type");
        await Assert.That(exception.Message).DoesNotContain("mark the model property nullable");
    }

    [Test]
    public async Task GetBytes_DistinguishesEmptyBlobFromSqlNull()
    {
        using var reader = CreateReader("SELECT X'', NULL");

        var empty = reader.GetBytes(0);
        var sqlNull = reader.GetBytes(1);

        await Assert.That(empty).IsNotNull();
        await Assert.That(empty!).IsEmpty();
        await Assert.That(sqlNull).IsNull();
    }

    [Test]
    public async Task GetBytes_ReturnsExactIndependentBuffers()
    {
        using var reader = CreateReader("SELECT X'010203'");

        var first = reader.GetBytes(0)!;
        var second = reader.GetBytes(0)!;
        first[0] = 9;

        await Assert.That(first.Length).IsEqualTo(3);
        await Assert.That(second.Length).IsEqualTo(3);
        await Assert.That(first).IsNotSameReferenceAs(second);
        await Assert.That(second).IsEquivalentTo(new byte[] { 1, 2, 3 });
    }

    private static SQLiteDataLinqDataReader CreateReader(string sql)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        var sqliteReader = command.ExecuteReader(CommandBehavior.CloseConnection);
        sqliteReader.Read();
        return new SQLiteDataLinqDataReader(sqliteReader);
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

    private static DatabaseDefinition CreateNullableDatabase()
    {
        var draft = new MetadataDatabaseDraft(
            "NullableReaderDb",
            new CsTypeDeclaration("NullableReaderDb", "DataLinq.Tests.Unit.SQLite", ModelCsType.Class))
        {
            TableModels =
            [
                new MetadataTableModelDraft(
                    "Rows",
                    new MetadataModelDraft(new CsTypeDeclaration("NullableReaderRow", "DataLinq.Tests.Unit.SQLite", ModelCsType.Class))
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
                                "OptionalNumber",
                                new CsTypeDeclaration(typeof(int)),
                                new MetadataColumnDraft("optional_number")
                                {
                                    Nullable = true,
                                    DbTypes = [new DatabaseColumnType(DatabaseType.SQLite, "INTEGER")]
                                })
                            {
                                CsNullable = true
                            }
                        ]
                    },
                    new MetadataTableDraft("nullable_rows"))
            ]
        };

        return new MetadataDefinitionFactory().Build(draft).ValueOrException();
    }

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }

    private enum ReaderNumericStatus : short
    {
        Unknown = 0,
        Active = 2
    }
}
