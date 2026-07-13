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
                OptionalText36 = null,
                TypedBinaryRfc4122 = new SQLiteGuidStorageId(KnownGuid),
                OptionalTypedText36 = null
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
        await Assert.That(inserted.TypedBinaryRfc4122).IsEqualTo(new SQLiteGuidStorageId(KnownGuid));
        await Assert.That(inserted.OptionalTypedText36).IsNull();
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
            "optional_text36 = 'fedcba98-7654-3210-89ab-cdef01234567', " +
            "typed_binary_rfc4122 = X'FEDCBA987654321089ABCDEF01234567', " +
            "optional_typed_text36 = 'fedcba98-7654-3210-89ab-cdef01234567' " +
            $"WHERE id = {insertedId}");

        database.Provider.State.ClearCache();
        var rawReloaded = database.Query().Rows.Single(row => row.Id == insertedId);

        await Assert.That(rawUpdated).IsEqualTo(1);
        await Assert.That(rawReloaded.Text36).IsEqualTo(AlternateGuid);
        await Assert.That(rawReloaded.Text32).IsEqualTo(AlternateGuid);
        await Assert.That(rawReloaded.BinaryLittleEndian).IsEqualTo(AlternateGuid);
        await Assert.That(rawReloaded.BinaryRfc4122).IsEqualTo(AlternateGuid);
        await Assert.That(rawReloaded.OptionalText36).IsEqualTo(AlternateGuid);
        await Assert.That(rawReloaded.TypedBinaryRfc4122).IsEqualTo(new SQLiteGuidStorageId(AlternateGuid));
        await Assert.That(rawReloaded.OptionalTypedText36).IsEqualTo(new SQLiteGuidStorageId(AlternateGuid));

        var mutable = rawReloaded.Mutate();
        mutable.Text36 = KnownGuid;
        mutable.Text32 = KnownGuid;
        mutable.BinaryLittleEndian = KnownGuid;
        mutable.BinaryRfc4122 = KnownGuid;
        mutable.OptionalText36 = null;
        mutable.TypedBinaryRfc4122 = new SQLiteGuidStorageId(KnownGuid);
        mutable.OptionalTypedText36 = null;
        var updated = database.Update(mutable);

        await Assert.That(updated.Text36).IsEqualTo(KnownGuid);
        await Assert.That(updated.Text32).IsEqualTo(KnownGuid);
        await Assert.That(updated.BinaryLittleEndian).IsEqualTo(KnownGuid);
        await Assert.That(updated.BinaryRfc4122).IsEqualTo(KnownGuid);
        await Assert.That(updated.OptionalText36).IsNull();
        await Assert.That(updated.TypedBinaryRfc4122).IsEqualTo(new SQLiteGuidStorageId(KnownGuid));
        await Assert.That(updated.OptionalTypedText36).IsNull();
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
        await Assert.That(finalReload.TypedBinaryRfc4122).IsEqualTo(new SQLiteGuidStorageId(KnownGuid));
        await Assert.That(finalReload.OptionalTypedText36).IsNull();
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.SqliteProviders))]
    public async Task NonKeyGuidFormats_QueryPredicatesBindExactPhysicalValuesAcrossSQLiteProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<SQLiteGuidStorageDb>.Create(
            provider,
            nameof(NonKeyGuidFormats_QueryPredicatesBindExactPhysicalValuesAcrossSQLiteProviders));
        var database = databaseScope.Database;
        var seeded = database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO guid_storage_rows " +
            "(text36, text32, binary_little_endian, binary_rfc4122, optional_text36, typed_binary_rfc4122, optional_typed_text36) VALUES " +
            "('00112233-4455-6677-8899-aabbccddeeff', '00112233445566778899aabbccddeeff', " +
            "X'33221100554477668899AABBCCDDEEFF', X'00112233445566778899AABBCCDDEEFF', " +
            "'00112233-4455-6677-8899-aabbccddeeff', X'00112233445566778899AABBCCDDEEFF', " +
            "'00112233-4455-6677-8899-aabbccddeeff'), " +
            "('fedcba98-7654-3210-89ab-cdef01234567', 'fedcba987654321089abcdef01234567', " +
            "X'98BADCFE5476103289ABCDEF01234567', X'FEDCBA987654321089ABCDEF01234567', " +
            "NULL, X'FEDCBA987654321089ABCDEF01234567', NULL)");

        database.Provider.State.ClearCache();

        var missingGuid = Guid.Parse("01234567-89ab-cdef-1032-547698badcfe");
        var typedProbe = new SQLiteGuidStorageId(KnownGuid);
        Guid? optionalProbe = KnownGuid;
        SQLiteGuidStorageId? optionalTypedProbe = typedProbe;
        var selectedGuids = new[] { KnownGuid, missingGuid };
        var selectedTypedIds = selectedGuids.Select(static value => new SQLiteGuidStorageId(value)).ToArray();
        var selectedOptionalGuids = selectedGuids.Select(static value => (Guid?)value).ToArray();
        var selectedOptionalTypedIds = selectedGuids
            .Select(static value => (SQLiteGuidStorageId?)new SQLiteGuidStorageId(value))
            .ToArray();

        var directEquality = database.Query().Rows.Where(row => row.BinaryLittleEndian == KnownGuid);
        var reversedEquality = database.Query().Rows.Where(row => KnownGuid == row.BinaryLittleEndian);
        var directInequality = database.Query().Rows.Where(row => row.BinaryLittleEndian != KnownGuid);
        var reversedInequality = database.Query().Rows.Where(row => KnownGuid != row.BinaryLittleEndian);
        var directContains = database.Query().Rows.Where(row => selectedGuids.Contains(row.BinaryLittleEndian));
        var directAnyLocalFirst = database.Query().Rows.Where(row => selectedGuids.Any(value => value == row.BinaryLittleEndian));
        var directAnyColumnFirst = database.Query().Rows.Where(row => selectedGuids.Any(value => row.BinaryLittleEndian == value));
        var text36Equality = database.Query().Rows.Where(row => row.Text36 == KnownGuid);
        var text32Equality = database.Query().Rows.Where(row => row.Text32 == KnownGuid);
        var directRfc4122Equality = database.Query().Rows.Where(row => row.BinaryRfc4122 == KnownGuid);

        var typedEquality = database.Query().Rows.Where(row => row.TypedBinaryRfc4122 == typedProbe);
        var reversedTypedEquality = database.Query().Rows.Where(row => typedProbe == row.TypedBinaryRfc4122);
        var typedInequality = database.Query().Rows.Where(row => row.TypedBinaryRfc4122 != typedProbe);
        var reversedTypedInequality = database.Query().Rows.Where(row => typedProbe != row.TypedBinaryRfc4122);
        var typedContains = database.Query().Rows.Where(row => selectedTypedIds.Contains(row.TypedBinaryRfc4122));
        var typedAnyLocalFirst = database.Query().Rows.Where(row => selectedTypedIds.Any(value => value == row.TypedBinaryRfc4122));
        var typedAnyColumnFirst = database.Query().Rows.Where(row => selectedTypedIds.Any(value => row.TypedBinaryRfc4122 == value));

        var optionalEquality = database.Query().Rows.Where(row => row.OptionalText36 == optionalProbe);
        var reversedOptionalEquality = database.Query().Rows.Where(row => optionalProbe == row.OptionalText36);
        var optionalInequality = database.Query().Rows.Where(row => row.OptionalText36 != optionalProbe);
        var reversedOptionalInequality = database.Query().Rows.Where(row => optionalProbe != row.OptionalText36);
        var optionalNullEquality = database.Query().Rows.Where(row => row.OptionalText36 == null);
        var optionalContains = database.Query().Rows.Where(row => Enumerable.Contains(selectedOptionalGuids, row.OptionalText36));
        var optionalAnyLocalFirst = database.Query().Rows.Where(row => selectedOptionalGuids.Any(value => value == row.OptionalText36));
        var optionalAnyColumnFirst = database.Query().Rows.Where(row => selectedOptionalGuids.Any(value => row.OptionalText36 == value));

        var optionalTypedEquality = database.Query().Rows.Where(row => row.OptionalTypedText36 == optionalTypedProbe);
        var reversedOptionalTypedEquality = database.Query().Rows.Where(row => optionalTypedProbe == row.OptionalTypedText36);
        var optionalTypedInequality = database.Query().Rows.Where(row => row.OptionalTypedText36 != optionalTypedProbe);
        var reversedOptionalTypedInequality = database.Query().Rows.Where(row => optionalTypedProbe != row.OptionalTypedText36);
        var optionalTypedNullEquality = database.Query().Rows.Where(row => row.OptionalTypedText36 == null);
        var optionalTypedContains = database.Query().Rows.Where(row => Enumerable.Contains(selectedOptionalTypedIds, row.OptionalTypedText36));
        var optionalTypedAnyLocalFirst = database.Query().Rows.Where(row => selectedOptionalTypedIds.Any(value => value == row.OptionalTypedText36));
        var optionalTypedAnyColumnFirst = database.Query().Rows.Where(row => selectedOptionalTypedIds.Any(value => row.OptionalTypedText36 == value));

        await Assert.That(seeded).IsEqualTo(2);
        await Assert.That(new[]
        {
            directEquality.Count(),
            reversedEquality.Count(),
            directInequality.Count(),
            reversedInequality.Count(),
            directContains.Count(),
            directAnyLocalFirst.Count(),
            directAnyColumnFirst.Count(),
            text36Equality.Count(),
            text32Equality.Count(),
            directRfc4122Equality.Count()
        }).IsEquivalentTo(new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 });
        await Assert.That(new[]
        {
            typedEquality.Count(),
            reversedTypedEquality.Count(),
            typedInequality.Count(),
            reversedTypedInequality.Count(),
            typedContains.Count(),
            typedAnyLocalFirst.Count(),
            typedAnyColumnFirst.Count()
        }).IsEquivalentTo(new[] { 1, 1, 1, 1, 1, 1, 1 });
        await Assert.That(new[]
        {
            optionalEquality.Count(),
            reversedOptionalEquality.Count(),
            optionalInequality.Count(),
            reversedOptionalInequality.Count(),
            optionalNullEquality.Count(),
            optionalContains.Count(),
            optionalAnyLocalFirst.Count(),
            optionalAnyColumnFirst.Count()
        }).IsEquivalentTo(new[] { 1, 1, 1, 1, 1, 1, 1, 1 });
        await Assert.That(new[]
        {
            optionalTypedEquality.Count(),
            reversedOptionalTypedEquality.Count(),
            optionalTypedInequality.Count(),
            reversedOptionalTypedInequality.Count(),
            optionalTypedNullEquality.Count(),
            optionalTypedContains.Count(),
            optionalTypedAnyLocalFirst.Count(),
            optionalTypedAnyColumnFirst.Count()
        }).IsEquivalentTo(new[] { 1, 1, 1, 1, 1, 1, 1, 1 });

        var directScalarSql = new[]
        {
            CurrentQueryTranslationInspection.BuildSql(database, directEquality),
            CurrentQueryTranslationInspection.BuildSql(database, reversedEquality),
            CurrentQueryTranslationInspection.BuildSql(database, directInequality),
            CurrentQueryTranslationInspection.BuildSql(database, reversedInequality)
        };
        foreach (var sql in directScalarSql)
        {
            await Assert.That(sql.Parameters.Count).IsEqualTo(1);
            await Assert.That(((byte[])sql.Parameters[0].Value!).SequenceEqual(KnownGuid.ToByteArray())).IsTrue();
        }

        var directMembershipSql = new[]
        {
            CurrentQueryTranslationInspection.BuildSql(database, directContains),
            CurrentQueryTranslationInspection.BuildSql(database, directAnyLocalFirst),
            CurrentQueryTranslationInspection.BuildSql(database, directAnyColumnFirst)
        };
        foreach (var sql in directMembershipSql)
        {
            await Assert.That(sql.Parameters.Count).IsEqualTo(2);
            await Assert.That(((byte[])sql.Parameters[0].Value!).SequenceEqual(KnownGuid.ToByteArray())).IsTrue();
            await Assert.That(((byte[])sql.Parameters[1].Value!).SequenceEqual(missingGuid.ToByteArray())).IsTrue();
        }

        var text36Sql = CurrentQueryTranslationInspection.BuildSql(database, text36Equality);
        var text32Sql = CurrentQueryTranslationInspection.BuildSql(database, text32Equality);
        var directRfc4122Sql = CurrentQueryTranslationInspection.BuildSql(database, directRfc4122Equality);
        await Assert.That(text36Sql.Parameters.Select(static parameter => parameter.Value).ToArray())
            .IsEquivalentTo(new object?[] { KnownGuid.ToString("D") });
        await Assert.That(text32Sql.Parameters.Select(static parameter => parameter.Value).ToArray())
            .IsEquivalentTo(new object?[] { KnownGuid.ToString("N") });
        await Assert.That(directRfc4122Sql.Parameters.Count).IsEqualTo(1);
        await Assert.That(((byte[])directRfc4122Sql.Parameters[0].Value!).SequenceEqual(KnownGuid.ToByteArray(bigEndian: true))).IsTrue();

        var typedScalarSql = new[]
        {
            CurrentQueryTranslationInspection.BuildSql(database, typedEquality),
            CurrentQueryTranslationInspection.BuildSql(database, reversedTypedEquality),
            CurrentQueryTranslationInspection.BuildSql(database, typedInequality),
            CurrentQueryTranslationInspection.BuildSql(database, reversedTypedInequality)
        };
        foreach (var sql in typedScalarSql)
        {
            await Assert.That(sql.Parameters.Count).IsEqualTo(1);
            await Assert.That(((byte[])sql.Parameters[0].Value!).SequenceEqual(KnownGuid.ToByteArray(bigEndian: true))).IsTrue();
        }

        var typedMembershipSql = new[]
        {
            CurrentQueryTranslationInspection.BuildSql(database, typedContains),
            CurrentQueryTranslationInspection.BuildSql(database, typedAnyLocalFirst),
            CurrentQueryTranslationInspection.BuildSql(database, typedAnyColumnFirst)
        };
        foreach (var sql in typedMembershipSql)
        {
            await Assert.That(sql.Parameters.Count).IsEqualTo(2);
            await Assert.That(((byte[])sql.Parameters[0].Value!).SequenceEqual(KnownGuid.ToByteArray(bigEndian: true))).IsTrue();
            await Assert.That(((byte[])sql.Parameters[1].Value!).SequenceEqual(missingGuid.ToByteArray(bigEndian: true))).IsTrue();
        }

        var expectedText36 = KnownGuid.ToString("D");
        var expectedMissingText36 = missingGuid.ToString("D");
        var optionalEqualitySql = new[]
        {
            CurrentQueryTranslationInspection.BuildSql(database, optionalEquality),
            CurrentQueryTranslationInspection.BuildSql(database, reversedOptionalEquality),
            CurrentQueryTranslationInspection.BuildSql(database, optionalTypedEquality),
            CurrentQueryTranslationInspection.BuildSql(database, reversedOptionalTypedEquality)
        };
        foreach (var sql in optionalEqualitySql)
        {
            await Assert.That(sql.Parameters.Count).IsEqualTo(1);
            await Assert.That(sql.Parameters[0].Value).IsEqualTo(expectedText36);
        }

        var optionalInequalitySql = new[]
        {
            CurrentQueryTranslationInspection.BuildSql(database, optionalInequality),
            CurrentQueryTranslationInspection.BuildSql(database, reversedOptionalInequality),
            CurrentQueryTranslationInspection.BuildSql(database, optionalTypedInequality),
            CurrentQueryTranslationInspection.BuildSql(database, reversedOptionalTypedInequality)
        };
        foreach (var sql in optionalInequalitySql)
        {
            await Assert.That(sql.Parameters.Count).IsEqualTo(2);
            await Assert.That(sql.Parameters[0].Value).IsEqualTo(expectedText36);
            await Assert.That(sql.Parameters[1].Value).IsNull();
        }

        var optionalMembershipSql = new[]
        {
            CurrentQueryTranslationInspection.BuildSql(database, optionalContains),
            CurrentQueryTranslationInspection.BuildSql(database, optionalAnyLocalFirst),
            CurrentQueryTranslationInspection.BuildSql(database, optionalAnyColumnFirst),
            CurrentQueryTranslationInspection.BuildSql(database, optionalTypedContains),
            CurrentQueryTranslationInspection.BuildSql(database, optionalTypedAnyLocalFirst),
            CurrentQueryTranslationInspection.BuildSql(database, optionalTypedAnyColumnFirst)
        };
        foreach (var sql in optionalMembershipSql)
        {
            await Assert.That(sql.Parameters.Select(static parameter => (string)parameter.Value!)
                .SequenceEqual(new[] { expectedText36, expectedMissingText36 }))
                .IsTrue();
        }

        var directNullSql = CurrentQueryTranslationInspection.BuildSql(database, optionalNullEquality);
        var typedNullSql = CurrentQueryTranslationInspection.BuildSql(database, optionalTypedNullEquality);
        await Assert.That(directNullSql.Parameters).IsEmpty();
        await Assert.That(typedNullSql.Parameters).IsEmpty();
        await Assert.That(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(directNullSql.Text)).Contains(" IS NULL");
        await Assert.That(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(typedNullSql.Text)).Contains(" IS NULL");
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
            $"SELECT hex(typed_binary_rfc4122) FROM guid_storage_rows WHERE id = {id}"))
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
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT typeof(typed_binary_rfc4122) FROM guid_storage_rows WHERE id = {id}"))
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

        var optionalTypedIsNull = Convert.ToInt32(access.ExecuteScalar(
            $"SELECT optional_typed_text36 IS NULL FROM guid_storage_rows WHERE id = {id}"));
        await Assert.That(optionalTypedIsNull).IsEqualTo(optionalText36 is null ? 1 : 0);
        if (optionalText36 is not null)
        {
            await Assert.That(access.ExecuteScalar<string>(
                $"SELECT optional_typed_text36 FROM guid_storage_rows WHERE id = {id}"))
                .IsEqualTo(optionalText36);
        }
    }
}

public readonly record struct SQLiteGuidStorageId(Guid Value);

public sealed class SQLiteGuidStorageIdConverter
    : DataLinqScalarConverter<SQLiteGuidStorageId, Guid>
{
    public override Guid ToProvider(
        SQLiteGuidStorageId modelValue,
        in ScalarConversionContext context) => modelValue.Value;

    public override SQLiteGuidStorageId FromProvider(
        Guid providerValue,
        in ScalarConversionContext context) => new(providerValue);
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

    [Type(DatabaseType.SQLite, "BLOB")]
    [GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16Rfc4122)]
    [ScalarConverter(typeof(SQLiteGuidStorageIdConverter))]
    [Column("typed_binary_rfc4122")]
    public abstract SQLiteGuidStorageId TypedBinaryRfc4122 { get; }

    [Nullable]
    [Type(DatabaseType.SQLite, "TEXT")]
    [ScalarConverter(typeof(SQLiteGuidStorageIdConverter))]
    [Column("optional_typed_text36")]
    public abstract SQLiteGuidStorageId? OptionalTypedText36 { get; }
}
