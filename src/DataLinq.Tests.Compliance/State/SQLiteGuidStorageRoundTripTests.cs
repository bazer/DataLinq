using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class SQLiteGuidStorageRoundTripTests
{
    private static readonly Guid KnownGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid AlternateGuid = Guid.Parse("fedcba98-7654-3210-89ab-cdef01234567");

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.SqliteProviders))]
    public async Task NonKeyGuidFormats_RoundTripKnownPhysicalValuesAcrossSQLiteProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<SQLiteGuidStorageDb>.Create(
            provider,
            nameof(NonKeyGuidFormats_RoundTripKnownPhysicalValuesAcrossSQLiteProviders));
        var database = databaseScope.Database;

        SQLiteGuidStorageRow inserted;
        using (var transaction = database.Transaction())
        {
            inserted = transaction.Insert(new MutableSQLiteGuidStorageRow
            {
                Text36 = KnownGuid,
                Text32 = KnownGuid,
                BinaryLittleEndian = KnownGuid,
                BinaryRfc4122 = KnownGuid,
                OptionalText36 = null
            });
            transaction.Commit();
        }

        var insertedId = inserted.Id
            ?? throw new InvalidOperationException("The SQLite UUID storage test row did not receive an identity value.");

        await Assert.That(inserted.Text36).IsEqualTo(KnownGuid);
        await Assert.That(inserted.Text32).IsEqualTo(KnownGuid);
        await Assert.That(inserted.BinaryLittleEndian).IsEqualTo(KnownGuid);
        await Assert.That(inserted.BinaryRfc4122).IsEqualTo(KnownGuid);
        await Assert.That(inserted.OptionalText36).IsNull();
        await AssertPhysicalStorage(
            database,
            insertedId,
            "00112233-4455-6677-8899-aabbccddeeff",
            "00112233445566778899aabbccddeeff",
            "33221100554477668899AABBCCDDEEFF",
            "00112233445566778899AABBCCDDEEFF",
            optionalText36: null);

        var rawUpdated = database.Provider.DatabaseAccess.ExecuteNonQuery(
            "UPDATE guid_storage_rows SET " +
            "text36 = 'fedcba98-7654-3210-89ab-cdef01234567', " +
            "text32 = 'fedcba987654321089abcdef01234567', " +
            "binary_little_endian = X'98BADCFE5476103289ABCDEF01234567', " +
            "binary_rfc4122 = X'FEDCBA987654321089ABCDEF01234567', " +
            "optional_text36 = 'fedcba98-7654-3210-89ab-cdef01234567' " +
            $"WHERE id = {insertedId}");

        database.Provider.State.ClearCache();
        var rawReloaded = database.Query().Rows.Single(row => row.Id == insertedId);

        await Assert.That(rawUpdated).IsEqualTo(1);
        await Assert.That(rawReloaded.Text36).IsEqualTo(AlternateGuid);
        await Assert.That(rawReloaded.Text32).IsEqualTo(AlternateGuid);
        await Assert.That(rawReloaded.BinaryLittleEndian).IsEqualTo(AlternateGuid);
        await Assert.That(rawReloaded.BinaryRfc4122).IsEqualTo(AlternateGuid);
        await Assert.That(rawReloaded.OptionalText36).IsEqualTo(AlternateGuid);

        var mutable = rawReloaded.Mutate();
        mutable.Text36 = KnownGuid;
        mutable.Text32 = KnownGuid;
        mutable.BinaryLittleEndian = KnownGuid;
        mutable.BinaryRfc4122 = KnownGuid;
        mutable.OptionalText36 = null;
        var updated = database.Update(mutable);

        await Assert.That(updated.Text36).IsEqualTo(KnownGuid);
        await Assert.That(updated.Text32).IsEqualTo(KnownGuid);
        await Assert.That(updated.BinaryLittleEndian).IsEqualTo(KnownGuid);
        await Assert.That(updated.BinaryRfc4122).IsEqualTo(KnownGuid);
        await Assert.That(updated.OptionalText36).IsNull();
        await AssertPhysicalStorage(
            database,
            insertedId,
            "00112233-4455-6677-8899-aabbccddeeff",
            "00112233445566778899aabbccddeeff",
            "33221100554477668899AABBCCDDEEFF",
            "00112233445566778899AABBCCDDEEFF",
            optionalText36: null);

        database.Provider.State.ClearCache();
        var finalReload = database.Query().Rows.Single(row => row.Id == insertedId);

        await Assert.That(finalReload.Text36).IsEqualTo(KnownGuid);
        await Assert.That(finalReload.Text32).IsEqualTo(KnownGuid);
        await Assert.That(finalReload.BinaryLittleEndian).IsEqualTo(KnownGuid);
        await Assert.That(finalReload.BinaryRfc4122).IsEqualTo(KnownGuid);
        await Assert.That(finalReload.OptionalText36).IsNull();
    }

    private static async Task AssertPhysicalStorage(
        Database<SQLiteGuidStorageDb> database,
        int id,
        string text36,
        string text32,
        string binaryLittleEndianHex,
        string binaryRfc4122Hex,
        string? optionalText36)
    {
        var access = database.Provider.DatabaseAccess;

        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT text36 FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo(text36);
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT text32 FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo(text32);
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT hex(binary_little_endian) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo(binaryLittleEndianHex);
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT hex(binary_rfc4122) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo(binaryRfc4122Hex);
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT typeof(text36) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo("text");
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT typeof(text32) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo("text");
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT typeof(binary_little_endian) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo("blob");
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT typeof(binary_rfc4122) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo("blob");

        var optionalIsNull = Convert.ToInt32(access.ExecuteScalar(
            $"SELECT optional_text36 IS NULL FROM guid_storage_rows WHERE id = {id}"));
        await Assert.That(optionalIsNull).IsEqualTo(optionalText36 is null ? 1 : 0);
        if (optionalText36 is not null)
        {
            await Assert.That(access.ExecuteScalar<string>(
                $"SELECT optional_text36 FROM guid_storage_rows WHERE id = {id}"))
                .IsEqualTo(optionalText36);
        }
    }
}

[Database("sqliteguidstorage")]
public sealed partial class SQLiteGuidStorageDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<SQLiteGuidStorageRow> Rows { get; } = new(dataSource);
}

[Table("guid_storage_rows")]
public abstract partial class SQLiteGuidStorageRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<SQLiteGuidStorageRow, SQLiteGuidStorageDb>(rowData, dataSource),
      ITableModel<SQLiteGuidStorageDb>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.SQLite, "INTEGER")]
    [Column("id")]
    public abstract int? Id { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [Column("text36")]
    public abstract Guid Text36 { get; }

    [Type(DatabaseType.SQLite, "TEXT")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Text32)]
    [Column("text32")]
    public abstract Guid Text32 { get; }

    [Type(DatabaseType.SQLite, "BLOB")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16LittleEndian)]
    [Column("binary_little_endian")]
    public abstract Guid BinaryLittleEndian { get; }

    [Type(DatabaseType.SQLite, "BLOB")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16Rfc4122)]
    [Column("binary_rfc4122")]
    public abstract Guid BinaryRfc4122 { get; }

    [Nullable]
    [Type(DatabaseType.SQLite, "TEXT")]
    [Column("optional_text36")]
    public abstract Guid? OptionalText36 { get; }
}
