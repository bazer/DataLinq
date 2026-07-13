using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Diagnostics;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.MariaDB;
using DataLinq.Mutation;
using DataLinq.MySql;
using DataLinq.Testing;
using MySqlConnector;

namespace DataLinq.Tests.Compliance;

public sealed class ServerGuidStorageRoundTripTests
{
    private static readonly Guid KnownGuid = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid AlternateGuid = Guid.Parse("fedcba98-7654-3210-89ab-cdef01234567");
    private static readonly MySqlGuidFormat?[] ConnectorGuidFormats =
    [
        null,
        MySqlGuidFormat.Char32,
        MySqlGuidFormat.Binary16,
        MySqlGuidFormat.LittleEndianBinary16
    ];

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ServerProviders))]
    public async Task NonKeyGuidFormats_IgnoreConnectorGuidFormatAcrossServerProviders(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<ServerGuidStorageDb>.Create(
            provider,
            nameof(NonKeyGuidFormats_IgnoreConnectorGuidFormatAcrossServerProviders));
        using var database = CreateDatabase(
            provider,
            WithGuidFormat(databaseScope.Connection.ConnectionString, guidFormat: null),
            databaseScope.Connection.DataSourceName);

        ServerGuidStorageRow inserted;
        using (var transaction = database.Transaction())
        {
            inserted = transaction.Insert(CreateMutable(KnownGuid, optionalText36: null));
            transaction.Commit();
        }

        var insertedId = inserted.Id
            ?? throw new InvalidOperationException("The server UUID storage test row did not receive an identity value.");

        await AssertModelValues(inserted, KnownGuid, optionalText36: null);
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
            "native_or_text36 = 'fedcba98-7654-3210-89ab-cdef01234567', " +
            "text36 = 'fedcba98-7654-3210-89ab-cdef01234567', " +
            "text32 = 'fedcba987654321089abcdef01234567', " +
            "binary_little_endian = X'98BADCFE5476103289ABCDEF01234567', " +
            "binary_rfc4122 = X'FEDCBA987654321089ABCDEF01234567', " +
            $"provider_specific_binary = X'{GetProviderSpecificHex(provider.DatabaseType, "98BADCFE5476103289ABCDEF01234567", "FEDCBA987654321089ABCDEF01234567")}', " +
            "optional_text36 = 'fedcba98-7654-3210-89ab-cdef01234567', " +
            $"typed_provider_specific_binary = X'{GetProviderSpecificHex(provider.DatabaseType, "98BADCFE5476103289ABCDEF01234567", "FEDCBA987654321089ABCDEF01234567")}', " +
            "optional_typed_text36 = 'fedcba98-7654-3210-89ab-cdef01234567' " +
            $"WHERE id = {insertedId}");
        await Assert.That(rawUpdated).IsEqualTo(1);

        database.Provider.State.ClearCache();
        var unconfiguredReload = database.Query().Rows.Single(row => row.Id == insertedId);
        await AssertModelValues(unconfiguredReload, AlternateGuid, AlternateGuid);

        using (var char32Database = CreateDatabase(
                   provider,
                   WithGuidFormat(databaseScope.Connection.ConnectionString, MySqlGuidFormat.Char32),
                   databaseScope.Connection.DataSourceName))
        {
            char32Database.Provider.State.ClearCache();
            using var transaction = char32Database.Transaction();
            var transactionReload = transaction.Query().Rows.Single(row => row.Id == insertedId);
            await AssertModelValues(transactionReload, AlternateGuid, AlternateGuid);
            transaction.Rollback();
        }

        using (var binary16Database = CreateDatabase(
                   provider,
                   WithGuidFormat(databaseScope.Connection.ConnectionString, MySqlGuidFormat.Binary16),
                   databaseScope.Connection.DataSourceName))
        {
            binary16Database.Provider.State.ClearCache();
            var binary16Reload = binary16Database.Query().Rows.Single(row => row.Id == insertedId);
            await AssertModelValues(binary16Reload, AlternateGuid, AlternateGuid);
        }

        using (var littleEndianDatabase = CreateDatabase(
                   provider,
                   WithGuidFormat(databaseScope.Connection.ConnectionString, MySqlGuidFormat.LittleEndianBinary16),
                   databaseScope.Connection.DataSourceName))
        {
            littleEndianDatabase.Provider.State.ClearCache();
            var littleEndianReload = littleEndianDatabase.Query().Rows.Single(row => row.Id == insertedId);
            await AssertModelValues(littleEndianReload, AlternateGuid, AlternateGuid);

            var updated = littleEndianDatabase.Update(CreateMutable(
                KnownGuid,
                optionalText36: null,
                source: littleEndianReload));
            await AssertModelValues(updated, KnownGuid, optionalText36: null);
            await AssertPhysicalStorage(
                littleEndianDatabase,
                insertedId,
                "00112233-4455-6677-8899-aabbccddeeff",
                "00112233445566778899aabbccddeeff",
                "33221100554477668899AABBCCDDEEFF",
                "00112233445566778899AABBCCDDEEFF",
                optionalText36: null);

            littleEndianDatabase.Provider.State.ClearCache();
            var finalReload = littleEndianDatabase.Query().Rows.Single(row => row.Id == insertedId);
            await AssertModelValues(finalReload, KnownGuid, optionalText36: null);
        }
    }

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ServerProviders))]
    public async Task NonKeyGuidPredicates_BindResolvedPhysicalValuesAcrossConnectorGuidFormats(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<ServerGuidStorageDb>.Create(
            provider,
            nameof(NonKeyGuidPredicates_BindResolvedPhysicalValuesAcrossConnectorGuidFormats));

        var knownProviderHex = GetProviderSpecificHex(
            provider.DatabaseType,
            "33221100554477668899AABBCCDDEEFF",
            "00112233445566778899AABBCCDDEEFF");
        var alternateProviderHex = GetProviderSpecificHex(
            provider.DatabaseType,
            "98BADCFE5476103289ABCDEF01234567",
            "FEDCBA987654321089ABCDEF01234567");
        var inserted = databaseScope.Database.Provider.DatabaseAccess.ExecuteNonQuery(
            "INSERT INTO guid_storage_rows (" +
            "native_or_text36, text36, text32, binary_little_endian, binary_rfc4122, " +
            "provider_specific_binary, optional_text36, typed_provider_specific_binary, optional_typed_text36" +
            ") VALUES (" +
            "'00112233-4455-6677-8899-aabbccddeeff', " +
            "'00112233-4455-6677-8899-aabbccddeeff', " +
            "'00112233445566778899aabbccddeeff', " +
            "X'33221100554477668899AABBCCDDEEFF', " +
            "X'00112233445566778899AABBCCDDEEFF', " +
            $"X'{knownProviderHex}', " +
            "'00112233-4455-6677-8899-aabbccddeeff', " +
            $"X'{knownProviderHex}', " +
            "'00112233-4455-6677-8899-aabbccddeeff'" +
            "), (" +
            "'fedcba98-7654-3210-89ab-cdef01234567', " +
            "'fedcba98-7654-3210-89ab-cdef01234567', " +
            "'fedcba987654321089abcdef01234567', " +
            "X'98BADCFE5476103289ABCDEF01234567', " +
            "X'FEDCBA987654321089ABCDEF01234567', " +
            $"X'{alternateProviderHex}', " +
            "NULL, " +
            $"X'{alternateProviderHex}', " +
            "NULL" +
            ")");

        await Assert.That(inserted).IsEqualTo(2);

        foreach (var connectorGuidFormat in ConnectorGuidFormats)
        {
            using var database = CreateDatabase(
                provider,
                WithGuidFormat(databaseScope.Connection.ConnectionString, connectorGuidFormat),
                databaseScope.Connection.DataSourceName);
            database.Provider.State.ClearCache();

            await AssertNonKeyGuidPredicateBindings(database, provider.DatabaseType);
        }
    }

    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ServerProviders))]
    public async Task ScalarGuidPrimaryKeys_PreserveCodecIdentityAcrossConnectorGuidFormats(
        TestProviderDescriptor provider)
    {
        using var databaseScope = TemporaryModelTestDatabase<ServerGuidStorageDb>.Create(
            provider,
            nameof(ScalarGuidPrimaryKeys_PreserveCodecIdentityAcrossConnectorGuidFormats));

        foreach (var connectorGuidFormat in ConnectorGuidFormats)
        {
            using var database = CreateDatabase(
                provider,
                WithGuidFormat(databaseScope.Connection.ConnectionString, connectorGuidFormat),
                databaseScope.Connection.DataSourceName);
            var access = database.Provider.DatabaseAccess;

            _ = access.ExecuteNonQuery("DELETE FROM guid_primary_key_rows");
            _ = access.ExecuteNonQuery("DELETE FROM typed_guid_primary_key_rows");
            database.Provider.State.ClearCache();

            ServerGuidPrimaryKeyRow insertedDirectKnown;
            ServerGuidPrimaryKeyRow insertedDirectAlternate;
            ServerTypedGuidPrimaryKeyRow insertedTypedKnown;
            ServerTypedGuidPrimaryKeyRow insertedTypedAlternate;
            using (var transaction = database.Transaction())
            {
                insertedDirectKnown = transaction.Insert(new MutableServerGuidPrimaryKeyRow
                {
                    Id = KnownGuid,
                    Name = "direct-known"
                });
                insertedDirectAlternate = transaction.Insert(new MutableServerGuidPrimaryKeyRow
                {
                    Id = AlternateGuid,
                    Name = "direct-alternate"
                });
                insertedTypedKnown = transaction.Insert(new MutableServerTypedGuidPrimaryKeyRow
                {
                    Id = new ServerGuidStorageId(KnownGuid),
                    Name = "typed-known"
                });
                insertedTypedAlternate = transaction.Insert(new MutableServerTypedGuidPrimaryKeyRow
                {
                    Id = new ServerGuidStorageId(AlternateGuid),
                    Name = "typed-alternate"
                });
                transaction.Commit();
            }

            await Assert.That(insertedDirectKnown.Id).IsEqualTo(KnownGuid);
            await Assert.That(insertedDirectKnown.Name).IsEqualTo("direct-known");
            await Assert.That(insertedDirectAlternate.Id).IsEqualTo(AlternateGuid);
            await Assert.That(insertedDirectAlternate.Name).IsEqualTo("direct-alternate");
            await Assert.That(insertedTypedKnown.Id).IsEqualTo(new ServerGuidStorageId(KnownGuid));
            await Assert.That(insertedTypedKnown.Name).IsEqualTo("typed-known");
            await Assert.That(insertedTypedAlternate.Id).IsEqualTo(new ServerGuidStorageId(AlternateGuid));
            await Assert.That(insertedTypedAlternate.Name).IsEqualTo("typed-alternate");
            await AssertPrimaryKeyPhysicalStorage(database, provider.DatabaseType);

            database.Provider.State.ClearCache();
            var coldDirect = ServerGuidPrimaryKeyRow.Get(KnownGuid, database)
                ?? throw new InvalidOperationException("The cold direct UUID primary-key lookup returned no row.");
            var coldTyped = ServerTypedGuidPrimaryKeyRow.Get(new ServerGuidStorageId(KnownGuid), database)
                ?? throw new InvalidOperationException("The cold typed UUID primary-key lookup returned no row.");

            await Assert.That(coldDirect.Id).IsEqualTo(KnownGuid);
            await Assert.That(coldDirect.Name).IsEqualTo("direct-known");
            await Assert.That(coldTyped.Id).IsEqualTo(new ServerGuidStorageId(KnownGuid));
            await Assert.That(coldTyped.Name).IsEqualTo("typed-known");

            DataLinqMetrics.Reset();
            var warmDirect = ServerGuidPrimaryKeyRow.Get(KnownGuid, database);
            var warmTyped = ServerTypedGuidPrimaryKeyRow.Get(new ServerGuidStorageId(KnownGuid), database);
            var warmSnapshot = DataLinqMetrics.Snapshot();

            await Assert.That(warmDirect).IsSameReferenceAs(coldDirect);
            await Assert.That(warmTyped).IsSameReferenceAs(coldTyped);
            await Assert.That(warmSnapshot.Commands.ReaderExecutions).IsEqualTo(0);

            database.Provider.State.ClearCache();
            Guid[] directKeys = [KnownGuid, AlternateGuid];
            ServerGuidStorageId[] typedKeys =
            [
                new(KnownGuid),
                new(AlternateGuid)
            ];
            var directBatch = database.Query().DirectKeyRows
                .Where(row => directKeys.Contains(row.Id))
                .OrderBy(row => row.Name)
                .ToArray();
            var typedBatch = database.Query().TypedKeyRows
                .Where(row => typedKeys.Contains(row.Id))
                .OrderBy(row => row.Name)
                .ToArray();

            await Assert.That(directBatch.Select(static row => row.Id).ToArray())
                .IsEquivalentTo(new[] { KnownGuid, AlternateGuid });
            await Assert.That(directBatch.Select(static row => row.Name).ToArray())
                .IsEquivalentTo(new[] { "direct-alternate", "direct-known" });
            await Assert.That(typedBatch.Select(static row => row.Id).ToArray())
                .IsEquivalentTo(new[]
                {
                    new ServerGuidStorageId(KnownGuid),
                    new ServerGuidStorageId(AlternateGuid)
                });
            await Assert.That(typedBatch.Select(static row => row.Name).ToArray())
                .IsEquivalentTo(new[] { "typed-alternate", "typed-known" });

            var batchedDirectKnown = directBatch.Single(static row => row.Id == KnownGuid);
            var batchedTypedKnown = typedBatch.Single(static row => row.Id == new ServerGuidStorageId(KnownGuid));
            DataLinqMetrics.Reset();
            var cachedDirectAfterBatch = ServerGuidPrimaryKeyRow.Get(KnownGuid, database);
            var cachedTypedAfterBatch = ServerTypedGuidPrimaryKeyRow.Get(new ServerGuidStorageId(KnownGuid), database);
            var batchCacheSnapshot = DataLinqMetrics.Snapshot();

            await Assert.That(cachedDirectAfterBatch).IsSameReferenceAs(batchedDirectKnown);
            await Assert.That(cachedTypedAfterBatch).IsSameReferenceAs(batchedTypedKnown);
            await Assert.That(batchCacheSnapshot.Commands.ReaderExecutions).IsEqualTo(0);

            var directMutable = batchedDirectKnown.Mutate();
            directMutable.Name = "direct-updated";
            var updatedDirect = database.Update(directMutable);
            var typedMutable = batchedTypedKnown.Mutate();
            typedMutable.Name = "typed-updated";
            var updatedTyped = database.Update(typedMutable);

            await Assert.That(updatedDirect.Id).IsEqualTo(KnownGuid);
            await Assert.That(updatedDirect.Name).IsEqualTo("direct-updated");
            await Assert.That(updatedTyped.Id).IsEqualTo(new ServerGuidStorageId(KnownGuid));
            await Assert.That(updatedTyped.Name).IsEqualTo("typed-updated");
            await Assert.That(access.ExecuteScalar<string>(
                "SELECT name FROM guid_primary_key_rows WHERE name = 'direct-updated'"))
                .IsEqualTo("direct-updated");
            await Assert.That(access.ExecuteScalar<string>(
                "SELECT name FROM typed_guid_primary_key_rows WHERE name = 'typed-updated'"))
                .IsEqualTo("typed-updated");

            var directAlternate = directBatch.Single(static row => row.Id == AlternateGuid);
            var typedAlternate = typedBatch.Single(static row => row.Id == new ServerGuidStorageId(AlternateGuid));
            database.Delete(directAlternate);
            database.Delete(typedAlternate);

            database.Provider.State.ClearCache();
            await Assert.That(ServerGuidPrimaryKeyRow.Get(AlternateGuid, database)).IsNull();
            await Assert.That(ServerTypedGuidPrimaryKeyRow.Get(new ServerGuidStorageId(AlternateGuid), database)).IsNull();
            await Assert.That(Convert.ToInt32(access.ExecuteScalar(
                "SELECT COUNT(*) FROM guid_primary_key_rows"))).IsEqualTo(1);
            await Assert.That(Convert.ToInt32(access.ExecuteScalar(
                "SELECT COUNT(*) FROM typed_guid_primary_key_rows"))).IsEqualTo(1);
            await AssertPrimaryKeyPhysicalStorage(
                database,
                provider.DatabaseType,
                includeAlternate: false);
        }
    }

    private static async Task AssertNonKeyGuidPredicateBindings(
        Database<ServerGuidStorageDb> database,
        DatabaseType databaseType)
    {
        var guidProbe = KnownGuid;
        var missingGuid = Guid.Parse("01234567-89ab-cdef-1032-547698badcfe");
        var typedProbe = new ServerGuidStorageId(KnownGuid);
        Guid? nullableGuidProbe = KnownGuid;
        ServerGuidStorageId? nullableTypedProbe = new ServerGuidStorageId(KnownGuid);
        var guidValues = new[] { KnownGuid, missingGuid };
        var typedValues = new[]
        {
            new ServerGuidStorageId(KnownGuid),
            new ServerGuidStorageId(missingGuid)
        };
        Guid?[] nullableGuidValues = [KnownGuid, missingGuid];
        ServerGuidStorageId?[] nullableTypedValues =
        [
            new ServerGuidStorageId(KnownGuid),
            new ServerGuidStorageId(missingGuid)
        ];

        var knownBinary = GetProviderSpecificBytes(
            databaseType,
            "33221100554477668899AABBCCDDEEFF",
            "00112233445566778899AABBCCDDEEFF");
        var missingBinary = GetProviderSpecificBytes(
            databaseType,
            "67452301AB89EFCD1032547698BADCFE",
            "0123456789ABCDEF1032547698BADCFE");
        const string knownText = "00112233-4455-6677-8899-aabbccddeeff";
        const string missingText = "01234567-89ab-cdef-1032-547698badcfe";

        var rows = database.Query().Rows;

        await AssertBoundQuery(database, rows.Where(row => row.NativeOrText36 == guidProbe), 1, knownText);
        await AssertBoundQuery(database, rows.Where(row => row.Text36 == guidProbe), 1, knownText);
        await AssertBoundQuery(database, rows.Where(row => row.Text32 == guidProbe), 1, KnownGuid.ToString("N"));
        await AssertBoundQuery(database, rows.Where(row => row.BinaryLittleEndian == guidProbe), 1, KnownGuid.ToByteArray());
        await AssertBoundQuery(database, rows.Where(row => row.BinaryRfc4122 == guidProbe), 1, KnownGuid.ToByteArray(bigEndian: true));

        await AssertBoundQuery(database, rows.Where(row => row.ProviderSpecificBinary == guidProbe), 1, knownBinary);
        await AssertBoundQuery(database, rows.Where(row => guidProbe == row.ProviderSpecificBinary), 1, knownBinary);
        await AssertBoundQuery(database, rows.Where(row => row.ProviderSpecificBinary != guidProbe), 1, knownBinary);
        await AssertBoundQuery(database, rows.Where(row => guidProbe != row.ProviderSpecificBinary), 1, knownBinary);
        await AssertBoundQuery(database, rows.Where(row => guidValues.Contains(row.ProviderSpecificBinary)), 1, knownBinary, missingBinary);
        await AssertBoundQuery(database, rows.Where(row => guidValues.Any(value => value == row.ProviderSpecificBinary)), 1, knownBinary, missingBinary);
        await AssertBoundQuery(database, rows.Where(row => guidValues.Any(value => row.ProviderSpecificBinary == value)), 1, knownBinary, missingBinary);

        await AssertBoundQuery(database, rows.Where(row => row.TypedProviderSpecificBinary == typedProbe), 1, knownBinary);
        await AssertBoundQuery(database, rows.Where(row => typedProbe == row.TypedProviderSpecificBinary), 1, knownBinary);
        await AssertBoundQuery(database, rows.Where(row => row.TypedProviderSpecificBinary != typedProbe), 1, knownBinary);
        await AssertBoundQuery(database, rows.Where(row => typedProbe != row.TypedProviderSpecificBinary), 1, knownBinary);
        await AssertBoundQuery(database, rows.Where(row => typedValues.Contains(row.TypedProviderSpecificBinary)), 1, knownBinary, missingBinary);
        await AssertBoundQuery(database, rows.Where(row => typedValues.Any(value => value == row.TypedProviderSpecificBinary)), 1, knownBinary, missingBinary);
        await AssertBoundQuery(database, rows.Where(row => typedValues.Any(value => row.TypedProviderSpecificBinary == value)), 1, knownBinary, missingBinary);

        await AssertBoundQuery(database, rows.Where(row => row.OptionalText36 == nullableGuidProbe), 1, knownText);
        await AssertBoundQuery(database, rows.Where(row => nullableGuidProbe == row.OptionalText36), 1, knownText);
        await AssertBoundQuery(database, rows.Where(row => row.OptionalText36 != nullableGuidProbe), 1, knownText);
        await AssertBoundQuery(database, rows.Where(row => nullableGuidProbe != row.OptionalText36), 1, knownText);
        await AssertBoundQuery(database, rows.Where(row => row.OptionalText36 == null), 1);
        await AssertBoundQuery(database, rows.Where(row => null == row.OptionalText36), 1);
        await AssertBoundQuery(database, rows.Where(row => Enumerable.Contains(nullableGuidValues, row.OptionalText36)), 1, knownText, missingText);
        await AssertBoundQuery(database, rows.Where(row => nullableGuidValues.Any(value => value == row.OptionalText36)), 1, knownText, missingText);
        await AssertBoundQuery(database, rows.Where(row => nullableGuidValues.Any(value => row.OptionalText36 == value)), 1, knownText, missingText);

        await AssertBoundQuery(database, rows.Where(row => row.OptionalTypedText36 == nullableTypedProbe), 1, knownText);
        await AssertBoundQuery(database, rows.Where(row => nullableTypedProbe == row.OptionalTypedText36), 1, knownText);
        await AssertBoundQuery(database, rows.Where(row => row.OptionalTypedText36 != nullableTypedProbe), 1, knownText);
        await AssertBoundQuery(database, rows.Where(row => nullableTypedProbe != row.OptionalTypedText36), 1, knownText);
        await AssertBoundQuery(database, rows.Where(row => row.OptionalTypedText36 == null), 1);
        await AssertBoundQuery(database, rows.Where(row => null == row.OptionalTypedText36), 1);
        await AssertBoundQuery(database, rows.Where(row => Enumerable.Contains(nullableTypedValues, row.OptionalTypedText36)), 1, knownText, missingText);
        await AssertBoundQuery(database, rows.Where(row => nullableTypedValues.Any(value => value == row.OptionalTypedText36)), 1, knownText, missingText);
        await AssertBoundQuery(database, rows.Where(row => nullableTypedValues.Any(value => row.OptionalTypedText36 == value)), 1, knownText, missingText);

        await AssertNullableInvocationSpecialization(database, knownText);
    }

    private static async Task AssertNullableInvocationSpecialization(
        Database<ServerGuidStorageDb> database,
        string knownText)
    {
        Guid? nullGuidProbe = null;
        ServerGuidStorageId? nullTypedProbe = null;
        var rows = database.Query().Rows;

        await AssertNullableBoundQuery(
            database,
            rows.Where(row => row.OptionalText36 == nullGuidProbe),
            [null],
            " IS NULL");
        await AssertNullableBoundQuery(
            database,
            rows.Where(row => nullGuidProbe == row.OptionalText36),
            [null],
            " IS NULL");
        await AssertNullableBoundQuery(
            database,
            rows.Where(row => row.OptionalText36 != nullGuidProbe),
            [KnownGuid],
            " IS NOT NULL");
        await AssertNullableBoundQuery(
            database,
            rows.Where(row => nullGuidProbe != row.OptionalText36),
            [KnownGuid],
            " IS NOT NULL");

        await AssertNullableBoundQuery(
            database,
            rows.Where(row => row.OptionalTypedText36 == nullTypedProbe),
            [null],
            " IS NULL");
        await AssertNullableBoundQuery(
            database,
            rows.Where(row => nullTypedProbe == row.OptionalTypedText36),
            [null],
            " IS NULL");
        await AssertNullableBoundQuery(
            database,
            rows.Where(row => row.OptionalTypedText36 != nullTypedProbe),
            [KnownGuid],
            " IS NOT NULL");
        await AssertNullableBoundQuery(
            database,
            rows.Where(row => nullTypedProbe != row.OptionalTypedText36),
            [KnownGuid],
            " IS NOT NULL");

        await AssertNullableSequenceShape(database, [], [], [], [KnownGuid, null]);
        await AssertNullableSequenceShape(database, [null], [null], [null], [KnownGuid]);
        await AssertNullableSequenceShape(
            database,
            [KnownGuid],
            [new ServerGuidStorageId(KnownGuid)],
            [KnownGuid],
            [null],
            knownText);
        await AssertNullableSequenceShape(
            database,
            [KnownGuid, null],
            [new ServerGuidStorageId(KnownGuid), null],
            [KnownGuid, null],
            [],
            knownText);
    }

    private static async Task AssertNullableSequenceShape(
        Database<ServerGuidStorageDb> database,
        Guid?[] directValues,
        ServerGuidStorageId?[] typedValues,
        Guid?[] expectedPositiveRows,
        Guid?[] expectedNegativeRows,
        params object[] expectedParameterValues)
    {
        var rows = database.Query().Rows;
        IQueryable<ServerGuidStorageRow>[] positiveQueries =
        [
            rows.Where(row => Enumerable.Contains(directValues, row.OptionalText36)),
            rows.Where(row => directValues.Any(value => value == row.OptionalText36)),
            rows.Where(row => directValues.Any(value => row.OptionalText36 == value)),
            rows.Where(row => Enumerable.Contains(typedValues, row.OptionalTypedText36)),
            rows.Where(row => typedValues.Any(value => value == row.OptionalTypedText36)),
            rows.Where(row => typedValues.Any(value => row.OptionalTypedText36 == value))
        ];
        IQueryable<ServerGuidStorageRow>[] negativeQueries =
        [
            rows.Where(row => !Enumerable.Contains(directValues, row.OptionalText36)),
            rows.Where(row => !directValues.Any(value => value == row.OptionalText36)),
            rows.Where(row => !directValues.Any(value => row.OptionalText36 == value)),
            rows.Where(row => !Enumerable.Contains(typedValues, row.OptionalTypedText36)),
            rows.Where(row => !typedValues.Any(value => value == row.OptionalTypedText36)),
            rows.Where(row => !typedValues.Any(value => row.OptionalTypedText36 == value))
        ];

        foreach (var query in positiveQueries)
        {
            await AssertNullableBoundQuery(
                database,
                query,
                expectedPositiveRows,
                expectedSqlFragment: null,
                expectedParameterValues);
        }

        foreach (var query in negativeQueries)
        {
            await AssertNullableBoundQuery(
                database,
                query,
                expectedNegativeRows,
                expectedSqlFragment: null,
                expectedParameterValues);
        }
    }

    private static async Task AssertNullableBoundQuery(
        Database<ServerGuidStorageDb> database,
        IQueryable<ServerGuidStorageRow> query,
        Guid?[] expectedRows,
        string? expectedSqlFragment,
        params object[] expectedParameterValues)
    {
        var sql = CurrentQueryTranslationInspection.BuildSql(database, query);
        var actualRows = query.ToArray();

        await Assert.That(actualRows.Select(static row => row.OptionalText36).ToArray())
            .IsEquivalentTo(expectedRows);
        await Assert.That(sql.Parameters.Count).IsEqualTo(expectedParameterValues.Length);
        await Assert.That(sql.Parameters.All(static parameter =>
                parameter.Value is not null && parameter.Value != DBNull.Value))
            .IsTrue();
        await Assert.That(sql.Parameters.Select(static parameter => parameter.Value!)
                .SequenceEqual(expectedParameterValues))
            .IsTrue();

        if (expectedSqlFragment is not null)
        {
            await Assert.That(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text))
                .Contains(expectedSqlFragment);
        }
    }

    private static async Task AssertBoundQuery(
        Database<ServerGuidStorageDb> database,
        IQueryable<ServerGuidStorageRow> query,
        int expectedCount,
        params object?[] expectedParameterValues)
    {
        var sql = CurrentQueryTranslationInspection.BuildSql(database, query);
        var actualCount = query.Count();

        await Assert.That(actualCount).IsEqualTo(expectedCount);
        await Assert.That(sql.Parameters.Count).IsEqualTo(expectedParameterValues.Length);
        if (expectedParameterValues.Length == 0)
        {
            await Assert.That(CurrentQueryTranslationInspection.NormalizeSqlWhitespace(sql.Text))
                .Contains(" IS NULL");
        }

        for (var index = 0; index < expectedParameterValues.Length; index++)
        {
            var actual = sql.Parameters[index].Value;
            var expected = expectedParameterValues[index];
            if (expected is byte[] expectedBytes)
            {
                await Assert.That(actual).IsTypeOf<byte[]>();
                var actualBytes = actual as byte[]
                    ?? throw new InvalidOperationException($"UUID parameter {index} was not encoded as bytes.");
                await Assert.That(Convert.ToHexString(actualBytes))
                    .IsEqualTo(Convert.ToHexString(expectedBytes));
                continue;
            }

            await Assert.That(actual).IsEqualTo(expected);
        }
    }

    private static MutableServerGuidStorageRow CreateMutable(
        Guid value,
        Guid? optionalText36,
        ServerGuidStorageRow? source = null)
    {
        if (source is null)
        {
            return new MutableServerGuidStorageRow
            {
                NativeOrText36 = value,
                Text36 = value,
                Text32 = value,
                BinaryLittleEndian = value,
                BinaryRfc4122 = value,
                ProviderSpecificBinary = value,
                OptionalText36 = optionalText36,
                TypedProviderSpecificBinary = new ServerGuidStorageId(value),
                OptionalTypedText36 = optionalText36.HasValue
                    ? new ServerGuidStorageId(optionalText36.Value)
                    : null
            };
        }

        var mutable = source.Mutate();
        mutable.NativeOrText36 = value;
        mutable.Text36 = value;
        mutable.Text32 = value;
        mutable.BinaryLittleEndian = value;
        mutable.BinaryRfc4122 = value;
        mutable.ProviderSpecificBinary = value;
        mutable.OptionalText36 = optionalText36;
        mutable.TypedProviderSpecificBinary = new ServerGuidStorageId(value);
        mutable.OptionalTypedText36 = optionalText36.HasValue
            ? new ServerGuidStorageId(optionalText36.Value)
            : null;
        return mutable;
    }

    private static async Task AssertModelValues(
        ServerGuidStorageRow row,
        Guid expected,
        Guid? optionalText36)
    {
        await Assert.That(row.NativeOrText36).IsEqualTo(expected);
        await Assert.That(row.Text36).IsEqualTo(expected);
        await Assert.That(row.Text32).IsEqualTo(expected);
        await Assert.That(row.BinaryLittleEndian).IsEqualTo(expected);
        await Assert.That(row.BinaryRfc4122).IsEqualTo(expected);
        await Assert.That(row.ProviderSpecificBinary).IsEqualTo(expected);
        await Assert.That(row.OptionalText36).IsEqualTo(optionalText36);
        await Assert.That(row.TypedProviderSpecificBinary).IsEqualTo(new ServerGuidStorageId(expected));
        await Assert.That(row.OptionalTypedText36).IsEqualTo(
            optionalText36.HasValue
                ? new ServerGuidStorageId(optionalText36.Value)
                : null);
    }

    private static async Task AssertPhysicalStorage(
        Database<ServerGuidStorageDb> database,
        int id,
        string text36,
        string text32,
        string binaryLittleEndianHex,
        string binaryRfc4122Hex,
        string? optionalText36)
    {
        var access = database.Provider.DatabaseAccess;
        var text36Hex = Convert.ToHexString(Encoding.ASCII.GetBytes(text36));
        var text32Hex = Convert.ToHexString(Encoding.ASCII.GetBytes(text32));

        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT HEX(LOWER(CAST(native_or_text36 AS CHAR(36)))) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo(text36Hex);
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT HEX(text36) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo(text36Hex);
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT HEX(text32) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo(text32Hex);
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT HEX(binary_little_endian) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo(binaryLittleEndianHex);
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT HEX(binary_rfc4122) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo(binaryRfc4122Hex);
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT HEX(provider_specific_binary) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo(GetProviderSpecificHex(
                database.DatabaseType,
                binaryLittleEndianHex,
                binaryRfc4122Hex));
        await Assert.That(access.ExecuteScalar<string>(
            $"SELECT HEX(typed_provider_specific_binary) FROM guid_storage_rows WHERE id = {id}"))
            .IsEqualTo(GetProviderSpecificHex(
                database.DatabaseType,
                binaryLittleEndianHex,
                binaryRfc4122Hex));
        await Assert.That(Convert.ToInt32(access.ExecuteScalar(
            $"SELECT OCTET_LENGTH(binary_little_endian) FROM guid_storage_rows WHERE id = {id}")))
            .IsEqualTo(16);
        await Assert.That(Convert.ToInt32(access.ExecuteScalar(
            $"SELECT OCTET_LENGTH(binary_rfc4122) FROM guid_storage_rows WHERE id = {id}")))
            .IsEqualTo(16);
        await Assert.That(Convert.ToInt32(access.ExecuteScalar(
            $"SELECT OCTET_LENGTH(provider_specific_binary) FROM guid_storage_rows WHERE id = {id}")))
            .IsEqualTo(16);
        await Assert.That(Convert.ToInt32(access.ExecuteScalar(
            $"SELECT OCTET_LENGTH(typed_provider_specific_binary) FROM guid_storage_rows WHERE id = {id}")))
            .IsEqualTo(16);

        var optionalIsNull = Convert.ToInt32(access.ExecuteScalar(
            $"SELECT optional_text36 IS NULL FROM guid_storage_rows WHERE id = {id}"));
        await Assert.That(optionalIsNull).IsEqualTo(optionalText36 is null ? 1 : 0);
        if (optionalText36 is not null)
        {
            await Assert.That(access.ExecuteScalar<string>(
                $"SELECT HEX(optional_text36) FROM guid_storage_rows WHERE id = {id}"))
                .IsEqualTo(Convert.ToHexString(Encoding.ASCII.GetBytes(optionalText36)));
        }

        var optionalTypedIsNull = Convert.ToInt32(access.ExecuteScalar(
            $"SELECT optional_typed_text36 IS NULL FROM guid_storage_rows WHERE id = {id}"));
        await Assert.That(optionalTypedIsNull).IsEqualTo(optionalText36 is null ? 1 : 0);
        if (optionalText36 is not null)
        {
            await Assert.That(access.ExecuteScalar<string>(
                $"SELECT HEX(optional_typed_text36) FROM guid_storage_rows WHERE id = {id}"))
                .IsEqualTo(Convert.ToHexString(Encoding.ASCII.GetBytes(optionalText36)));
        }
    }

    private static async Task AssertPrimaryKeyPhysicalStorage(
        Database<ServerGuidStorageDb> database,
        DatabaseType databaseType,
        bool includeAlternate = true)
    {
        var knownHex = GetProviderSpecificHex(
            databaseType,
            "33221100554477668899AABBCCDDEEFF",
            "00112233445566778899AABBCCDDEEFF");
        var alternateHex = GetProviderSpecificHex(
            databaseType,
            "98BADCFE5476103289ABCDEF01234567",
            "FEDCBA987654321089ABCDEF01234567");
        var access = database.Provider.DatabaseAccess;

        await Assert.That(access.ExecuteScalar<string>(
            "SELECT HEX(id) FROM guid_primary_key_rows WHERE name IN ('direct-known', 'direct-updated')"))
            .IsEqualTo(knownHex);
        await Assert.That(access.ExecuteScalar<string>(
            "SELECT HEX(id) FROM typed_guid_primary_key_rows WHERE name IN ('typed-known', 'typed-updated')"))
            .IsEqualTo(knownHex);

        if (!includeAlternate)
            return;

        await Assert.That(access.ExecuteScalar<string>(
            "SELECT HEX(id) FROM guid_primary_key_rows WHERE name = 'direct-alternate'"))
            .IsEqualTo(alternateHex);
        await Assert.That(access.ExecuteScalar<string>(
            "SELECT HEX(id) FROM typed_guid_primary_key_rows WHERE name = 'typed-alternate'"))
            .IsEqualTo(alternateHex);
    }

    private static Database<ServerGuidStorageDb> CreateDatabase(
        TestProviderDescriptor provider,
        string connectionString,
        string databaseName) => provider.DatabaseType switch
    {
        DatabaseType.MySQL => new MySqlDatabase<ServerGuidStorageDb>(connectionString, databaseName),
        DatabaseType.MariaDB => new MariaDBDatabase<ServerGuidStorageDb>(connectionString, databaseName),
        _ => throw new ArgumentOutOfRangeException(
            nameof(provider),
            provider.DatabaseType,
            "The server UUID storage test only supports MySQL and MariaDB.")
    };

    private static string WithGuidFormat(string connectionString, MySqlGuidFormat? guidFormat)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        builder.Remove("GuidFormat");
        if (guidFormat.HasValue)
            builder.GuidFormat = guidFormat.Value;

        return builder.ConnectionString;
    }

    private static string GetProviderSpecificHex(
        DatabaseType databaseType,
        string littleEndianHex,
        string rfc4122Hex) => databaseType switch
    {
        DatabaseType.MySQL => littleEndianHex,
        DatabaseType.MariaDB => rfc4122Hex,
        _ => throw new ArgumentOutOfRangeException(
            nameof(databaseType),
            databaseType,
            "The server UUID storage test only supports MySQL and MariaDB.")
    };

    private static byte[] GetProviderSpecificBytes(
        DatabaseType databaseType,
        string littleEndianHex,
        string rfc4122Hex) => Convert.FromHexString(
            GetProviderSpecificHex(databaseType, littleEndianHex, rfc4122Hex));
}

public readonly record struct ServerGuidStorageId(Guid Value);

public sealed class ServerGuidStorageIdConverter
    : DataLinqScalarConverter<ServerGuidStorageId, Guid>
{
    public override Guid ToProvider(
        ServerGuidStorageId modelValue,
        in ScalarConversionContext context) => modelValue.Value;

    public override ServerGuidStorageId FromProvider(
        Guid providerValue,
        in ScalarConversionContext context) => new(providerValue);
}

[Database("serverguidstorage")]
public sealed partial class ServerGuidStorageDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<ServerGuidStorageRow> Rows { get; } = new(dataSource);
    public DbRead<ServerGuidPrimaryKeyRow> DirectKeyRows { get; } = new(dataSource);
    public DbRead<ServerTypedGuidPrimaryKeyRow> TypedKeyRows { get; } = new(dataSource);
}

[UseCache]
[Table("guid_primary_key_rows")]
public abstract partial class ServerGuidPrimaryKeyRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<ServerGuidPrimaryKeyRow, ServerGuidStorageDb>(rowData, dataSource),
      ITableModel<ServerGuidStorageDb>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16Rfc4122)]
    [Column("id")]
    public abstract Guid Id { get; }

    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.MariaDB, "varchar", 40)]
    [Column("name")]
    public abstract string Name { get; }
}

[UseCache]
[Table("typed_guid_primary_key_rows")]
public abstract partial class ServerTypedGuidPrimaryKeyRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<ServerTypedGuidPrimaryKeyRow, ServerGuidStorageDb>(rowData, dataSource),
      ITableModel<ServerGuidStorageDb>
{
    [PrimaryKey]
    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16Rfc4122)]
    [ScalarConverter(typeof(ServerGuidStorageIdConverter))]
    [Column("id")]
    public abstract ServerGuidStorageId Id { get; }

    [Type(DatabaseType.MySQL, "varchar", 40)]
    [Type(DatabaseType.MariaDB, "varchar", 40)]
    [Column("name")]
    public abstract string Name { get; }
}

[Table("guid_storage_rows")]
public abstract partial class ServerGuidStorageRow(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<ServerGuidStorageRow, ServerGuidStorageDb>(rowData, dataSource),
      ITableModel<ServerGuidStorageDb>
{
    [PrimaryKey]
    [AutoIncrement]
    [Type(DatabaseType.MySQL, "int", 11)]
    [Type(DatabaseType.MariaDB, "int", 11)]
    [Column("id")]
    public abstract int? Id { get; }

    [Type(DatabaseType.MySQL, "char", 36)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Text36)]
    [Type(DatabaseType.MariaDB, "uuid")]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.NativeUuid)]
    [Column("native_or_text36")]
    public abstract Guid NativeOrText36 { get; }

    [Type(DatabaseType.MySQL, "char", 36)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Text36)]
    [Type(DatabaseType.MariaDB, "char", 36)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Text36)]
    [Column("text36")]
    public abstract Guid Text36 { get; }

    [Type(DatabaseType.MySQL, "char", 32)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Text32)]
    [Type(DatabaseType.MariaDB, "char", 32)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Text32)]
    [Column("text32")]
    public abstract Guid Text32 { get; }

    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16LittleEndian)]
    [Column("binary_little_endian")]
    public abstract Guid BinaryLittleEndian { get; }

    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16Rfc4122)]
    [Column("binary_rfc4122")]
    public abstract Guid BinaryRfc4122 { get; }

    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16Rfc4122)]
    [Column("provider_specific_binary")]
    public abstract Guid ProviderSpecificBinary { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "char", 36)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Text36)]
    [Type(DatabaseType.MariaDB, "char", 36)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Text36)]
    [Column("optional_text36")]
    public abstract Guid? OptionalText36 { get; }

    [Type(DatabaseType.MySQL, "binary", 16)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian)]
    [Type(DatabaseType.MariaDB, "binary", 16)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Binary16Rfc4122)]
    [ScalarConverter(typeof(ServerGuidStorageIdConverter))]
    [Column("typed_provider_specific_binary")]
    public abstract ServerGuidStorageId TypedProviderSpecificBinary { get; }

    [Nullable]
    [Type(DatabaseType.MySQL, "char", 36)]
    [GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Text36)]
    [Type(DatabaseType.MariaDB, "char", 36)]
    [GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.Text36)]
    [ScalarConverter(typeof(ServerGuidStorageIdConverter))]
    [Column("optional_typed_text36")]
    public abstract ServerGuidStorageId? OptionalTypedText36 { get; }
}
