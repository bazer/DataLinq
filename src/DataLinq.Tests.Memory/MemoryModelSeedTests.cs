using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Memory;
using TUnit.Assertions.Enums;

namespace DataLinq.Tests.Memory;

public sealed class MemoryModelSeedTests
{
    private static readonly Guid KnownId = new("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid KnownDirectGuid = new("f1e2d3c4-b5a6-4789-90ab-cdef12345678");
    private static readonly Guid KnownRelatedId = new("10213243-5465-7687-98a9-bacbdcedfe0f");
    private static readonly Guid KnownOptionalRelatedId = new("89abcdef-0123-4567-89ab-cdef01234567");

    [Test]
    [NotInParallel]
    public async Task ModelValuedSeed_NormalizesTypedGuidAndKeepsUuidWireFormatsOutsideMemory()
    {
        MemoryGuidIdConverter.Reset();
        var database = new MemoryDatabase<MemoryConvertedDatabase>();

        database.SeedModelValues<MemoryConvertedRow>(CreateModelRow(
            database,
            new MemoryGuidId(KnownId),
            KnownDirectGuid,
            new MemoryGuidId(KnownRelatedId),
            optionalRelatedId: null));

        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(["id", "related_id"], CollectionOrdering.Matching);
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
        await Assert.That(database.GetStoredRowCount<MemoryConvertedRow>()).IsEqualTo(1);
        await Assert.That(database.GetMaterializedRowCount<MemoryConvertedRow>()).IsEqualTo(0);

        var stored = database.GetCanonicalRowsForTest<MemoryConvertedRow>().Single();
        var table = database.Metadata.GetTableModel(typeof(MemoryConvertedRow)).Table;
        var idColumn = table.GetColumnByDbName("id");
        var directGuidColumn = table.GetColumnByDbName("direct_guid");
        var relatedIdColumn = table.GetColumnByDbName("related_id");
        var optionalRelatedIdColumn = table.GetColumnByDbName("optional_related_id");

        await Assert.That(idColumn.GetGuidStorageFor(DatabaseType.SQLite)!.Format)
            .IsEqualTo(GuidStorageFormat.Binary16LittleEndian);
        await Assert.That(idColumn.GetGuidStorageFor(DatabaseType.MySQL)!.Format)
            .IsEqualTo(GuidStorageFormat.Text32);
        await Assert.That(idColumn.GetGuidStorageFor(DatabaseType.MariaDB)!.Format)
            .IsEqualTo(GuidStorageFormat.NativeUuid);
        await Assert.That(directGuidColumn.GetGuidStorageFor(DatabaseType.SQLite)!.Format)
            .IsEqualTo(GuidStorageFormat.Text36);
        await Assert.That(directGuidColumn.GetGuidStorageFor(DatabaseType.MySQL)!.Format)
            .IsEqualTo(GuidStorageFormat.Binary16Rfc4122);
        await Assert.That(directGuidColumn.GetGuidStorageFor(DatabaseType.MariaDB)!.Format)
            .IsEqualTo(GuidStorageFormat.Text32);

        await Assert.That(stored[idColumn]).IsTypeOf<Guid>();
        await Assert.That(stored[idColumn]).IsEqualTo(KnownId);
        await Assert.That(stored[directGuidColumn]).IsTypeOf<Guid>();
        await Assert.That(stored[directGuidColumn]).IsEqualTo(KnownDirectGuid);
        await Assert.That(stored[relatedIdColumn]).IsTypeOf<Guid>();
        await Assert.That(stored[relatedIdColumn]).IsEqualTo(KnownRelatedId);
        await Assert.That(stored[optionalRelatedIdColumn]).IsNull();

        var cold = database.Find<MemoryConvertedRow>(DataLinqKey.FromValue(KnownId));
        var warm = database.Find<MemoryConvertedRow>(DataLinqKey.FromValue(KnownId));
        var scanned = database.Model.Rows.ToArray().Single();

        await Assert.That(cold).IsNotNull();
        await Assert.That(cold!.Id).IsEqualTo(new MemoryGuidId(KnownId));
        await Assert.That(cold.DirectGuid).IsEqualTo(KnownDirectGuid);
        await Assert.That(cold.RelatedId).IsEqualTo(new MemoryGuidId(KnownRelatedId));
        await Assert.That(cold.OptionalRelatedId).IsNull();
        await Assert.That(warm).IsSameReferenceAs(cold);
        await Assert.That(scanned).IsSameReferenceAs(cold);
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns)
            .IsEquivalentTo(["id", "related_id"], CollectionOrdering.Matching);
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(["id", "related_id", "id"], CollectionOrdering.Matching);

        database.ClearMaterializedRowsForTest<MemoryConvertedRow>();
        var rematerialized = database.Find<MemoryConvertedRow>(DataLinqKey.FromValue(KnownId));

        await Assert.That(rematerialized).IsNotNull();
        await Assert.That(rematerialized).IsNotSameReferenceAs(cold);
        await Assert.That(rematerialized!.Id).IsEqualTo(new MemoryGuidId(KnownId));
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns)
            .IsEquivalentTo(
                ["id", "related_id", "id", "related_id"],
                CollectionOrdering.Matching);
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(
                ["id", "related_id", "id", "id"],
                CollectionOrdering.Matching);
    }

    [Test]
    [NotInParallel]
    public async Task CanonicalSeed_KeepsConvertedColumnsInProviderDomainUntilMaterialization()
    {
        MemoryGuidIdConverter.Reset();
        var database = new MemoryDatabase<MemoryConvertedDatabase>();

        database.SeedCanonical<MemoryConvertedRow>(CreateCanonicalRow(
            database,
            KnownId,
            KnownDirectGuid,
            KnownRelatedId,
            KnownOptionalRelatedId));

        await Assert.That(MemoryGuidIdConverter.ToProviderColumns).IsEmpty();
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();

        var stored = database.GetCanonicalRowsForTest<MemoryConvertedRow>().Single();
        var table = database.Metadata.GetTableModel(typeof(MemoryConvertedRow)).Table;
        await Assert.That(stored[table.GetColumnByDbName("id")]).IsEqualTo(KnownId);
        await Assert.That(stored[table.GetColumnByDbName("direct_guid")]).IsEqualTo(KnownDirectGuid);
        await Assert.That(stored[table.GetColumnByDbName("related_id")]).IsEqualTo(KnownRelatedId);
        await Assert.That(stored[table.GetColumnByDbName("optional_related_id")])
            .IsEqualTo(KnownOptionalRelatedId);

        var cold = database.Find<MemoryConvertedRow>(DataLinqKey.FromValue(KnownId));
        var warm = database.Find<MemoryConvertedRow>(DataLinqKey.FromValue(KnownId));

        await Assert.That(cold).IsNotNull();
        await Assert.That(cold!.Id).IsEqualTo(new MemoryGuidId(KnownId));
        await Assert.That(cold.DirectGuid).IsEqualTo(KnownDirectGuid);
        await Assert.That(cold.RelatedId).IsEqualTo(new MemoryGuidId(KnownRelatedId));
        await Assert.That(cold.OptionalRelatedId)
            .IsEqualTo(new MemoryGuidId(KnownOptionalRelatedId));
        await Assert.That(warm).IsSameReferenceAs(cold);
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns)
            .IsEquivalentTo(
                ["id", "related_id", "optional_related_id"],
                CollectionOrdering.Matching);
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(["id"], CollectionOrdering.Matching);
    }

    [Test]
    [NotInParallel]
    public async Task ModelValuedSeed_RejectsReseedBeforeRunningConverters()
    {
        MemoryGuidIdConverter.Reset();
        var database = new MemoryDatabase<MemoryConvertedDatabase>();
        database.SeedModelValues<MemoryConvertedRow>(CreateModelRow(
            database,
            new MemoryGuidId(KnownId),
            KnownDirectGuid,
            new MemoryGuidId(KnownRelatedId),
            optionalRelatedId: null));
        MemoryGuidIdConverter.Reset();

        var exception = Capture<MemorySeedException>(() =>
            database.SeedModelValues<MemoryConvertedRow>(CreateModelRow(
                database,
                new MemoryGuidId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                new MemoryGuidId(Guid.Parse("99999999-8888-7777-6666-555555555555")),
                optionalRelatedId: null)));

        await Assert.That(exception.Message).Contains("has already been seeded");
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns).IsEmpty();
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
        await Assert.That(database.GetStoredRowCount<MemoryConvertedRow>()).IsEqualTo(1);
        await Assert.That(database.GetCanonicalRowsForTest<MemoryConvertedRow>().Single()[0])
            .IsEqualTo(KnownId);
    }

    [Test]
    [NotInParallel]
    public async Task ModelValuedSeed_RejectsConcurrentSameTableAttemptWithoutHoldingSeedGateDuringConversion()
    {
        MemoryGuidIdConverter.Reset();
        using var converterEntered = new ManualResetEventSlim();
        using var releaseConverter = new ManualResetEventSlim();
        using var secondAttemptStarted = new ManualResetEventSlim();
        var database = new MemoryDatabase<MemoryConvertedDatabase>();
        var first = CreateModelRow(
            database,
            new MemoryGuidId(KnownId),
            KnownDirectGuid,
            new MemoryGuidId(KnownRelatedId),
            optionalRelatedId: null);
        var second = CreateModelRow(
            database,
            new MemoryGuidId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            new MemoryGuidId(Guid.Parse("99999999-8888-7777-6666-555555555555")),
            optionalRelatedId: null);

        MemoryGuidIdConverter.SetToProviderProbe(columnName =>
        {
            if (columnName != "id")
                return;

            converterEntered.Set();
            if (!releaseConverter.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The concurrent memory-seed test did not release the converter.");
        });

        var firstSeed = Task.Run(() => database.SeedModelValues<MemoryConvertedRow>(first));
        Task<MemorySeedException>? secondSeed = null;
        var entered = await Task.Run(() => converterEntered.Wait(TimeSpan.FromSeconds(5)));
        var secondStarted = false;
        var secondCompletedBeforeRelease = false;

        try
        {
            if (entered)
            {
                secondSeed = Task.Run(() =>
                {
                    secondAttemptStarted.Set();
                    return Capture<MemorySeedException>(() =>
                        database.SeedModelValues<MemoryConvertedRow>(second));
                });
                secondStarted = await Task.Run(() =>
                    secondAttemptStarted.Wait(TimeSpan.FromSeconds(5)));
                if (secondStarted)
                {
                    secondCompletedBeforeRelease = ReferenceEquals(
                        await Task.WhenAny(secondSeed, Task.Delay(TimeSpan.FromSeconds(2))),
                        secondSeed);
                }
            }
        }
        finally
        {
            releaseConverter.Set();
        }

        await firstSeed;
        var secondException = secondSeed is null ? null : await secondSeed;
        MemoryGuidIdConverter.SetToProviderProbe(null);

        await Assert.That(entered).IsTrue();
        await Assert.That(secondStarted).IsTrue();
        await Assert.That(secondCompletedBeforeRelease).IsTrue();
        await Assert.That(secondException).IsNotNull();
        await Assert.That(secondException!.Message).Contains("already being seeded");
        await Assert.That(database.GetStoredRowCount<MemoryConvertedRow>()).IsEqualTo(1);
        await Assert.That(database.GetCanonicalRowsForTest<MemoryConvertedRow>().Single()[0])
            .IsEqualTo(KnownId);
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(["id", "related_id"], CollectionOrdering.Matching);
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
    }

    [Test]
    [NotInParallel]
    public async Task ModelValuedSeed_ValidatesModelNullabilityBeforeRunningConverters()
    {
        MemoryGuidIdConverter.Reset();
        var database = new MemoryDatabase<MemoryConvertedDatabase>();
        var invalid = CreateModelRow(
            database,
            id: null,
            KnownDirectGuid,
            new MemoryGuidId(KnownRelatedId),
            optionalRelatedId: null);

        var exception = Capture<MemorySeedException>(() =>
            database.SeedModelValues<MemoryConvertedRow>(invalid));

        await Assert.That(exception.Message).Contains("Model-valued memory seed row 0");
        await Assert.That(exception.Message).Contains("memory_converted_rows.id");
        await Assert.That(exception.Message).Contains("cannot contain a null model value");
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns).IsEmpty();
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
        await Assert.That(database.GetStoredRowCount<MemoryConvertedRow>()).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task ModelValuedSeed_RejectsCanonicalSubstitutionWithRedactedRowAndColumnContext()
    {
        MemoryGuidIdConverter.Reset();
        var database = new MemoryDatabase<MemoryConvertedDatabase>();
        var invalid = CreateModelRow(
            database,
            new MemoryGuidId(KnownId),
            KnownDirectGuid,
            KnownRelatedId,
            optionalRelatedId: null);

        var exception = Capture<MemorySeedException>(() =>
            database.SeedModelValues<MemoryConvertedRow>(invalid));

        await Assert.That(exception.Message).Contains("Model-valued memory seed row 0");
        await Assert.That(exception.Message).Contains("memory_converted_rows.related_id");
        await Assert.That(exception.Message).Contains(typeof(MemoryGuidId).FullName!);
        await Assert.That(exception.Message).Contains(typeof(Guid).FullName!);
        await Assert.That(exception.Message).DoesNotContain(KnownId.ToString());
        await Assert.That(exception.Message).DoesNotContain(KnownDirectGuid.ToString());
        await Assert.That(exception.Message).DoesNotContain(KnownRelatedId.ToString());
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns).IsEmpty();
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
        await Assert.That(database.GetStoredRowCount<MemoryConvertedRow>()).IsEqualTo(0);

        database.SeedModelValues<MemoryConvertedRow>(CreateModelRow(
            database,
            new MemoryGuidId(KnownId),
            KnownDirectGuid,
            new MemoryGuidId(KnownRelatedId),
            optionalRelatedId: null));

        var recovered = database.Find<MemoryConvertedRow>(DataLinqKey.FromValue(KnownId));
        await Assert.That(recovered).IsNotNull();
        await Assert.That(recovered!.Id).IsEqualTo(new MemoryGuidId(KnownId));
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(["id", "related_id", "id"], CollectionOrdering.Matching);
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns)
            .IsEquivalentTo(["id", "related_id"], CollectionOrdering.Matching);
    }

    [Test]
    [NotInParallel]
    public async Task ModelValuedSeed_DetectsDuplicateCanonicalGuidKeysBeforePublication()
    {
        MemoryGuidIdConverter.Reset();
        var database = new MemoryDatabase<MemoryConvertedDatabase>();
        var first = CreateModelRow(
            database,
            new MemoryGuidId(KnownId),
            KnownDirectGuid,
            new MemoryGuidId(KnownRelatedId),
            optionalRelatedId: null);
        var second = CreateModelRow(
            database,
            new MemoryGuidId(KnownId),
            Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789"),
            new MemoryGuidId(Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210")),
            optionalRelatedId: null);

        var exception = Capture<MemorySeedException>(() =>
            database.SeedModelValues<MemoryConvertedRow>(first, second));

        await Assert.That(exception.Message).Contains("Model-valued memory seed");
        await Assert.That(exception.Message).Contains("duplicate primary key at row 1");
        await Assert.That(exception.Message).Contains("first row is 0");
        await Assert.That(exception.Message).DoesNotContain(KnownId.ToString());
        await Assert.That(database.GetStoredRowCount<MemoryConvertedRow>()).IsEqualTo(0);
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(
                ["id", "related_id", "id", "related_id"],
                CollectionOrdering.Matching);
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
    }

    private static object?[] CreateModelRow(
        MemoryDatabase<MemoryConvertedDatabase> database,
        object? id,
        Guid directGuid,
        object relatedId,
        MemoryGuidId? optionalRelatedId)
    {
        var table = database.Metadata.GetTableModel(typeof(MemoryConvertedRow)).Table;
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = id;
        values[table.GetColumnByDbName("direct_guid").Index] = directGuid;
        values[table.GetColumnByDbName("related_id").Index] = relatedId;
        values[table.GetColumnByDbName("optional_related_id").Index] = optionalRelatedId;
        return values;
    }

    private static object?[] CreateCanonicalRow(
        MemoryDatabase<MemoryConvertedDatabase> database,
        Guid id,
        Guid directGuid,
        Guid relatedId,
        Guid? optionalRelatedId)
    {
        var table = database.Metadata.GetTableModel(typeof(MemoryConvertedRow)).Table;
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = id;
        values[table.GetColumnByDbName("direct_guid").Index] = directGuid;
        values[table.GetColumnByDbName("related_id").Index] = relatedId;
        values[table.GetColumnByDbName("optional_related_id").Index] = optionalRelatedId;
        return values;
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
}
