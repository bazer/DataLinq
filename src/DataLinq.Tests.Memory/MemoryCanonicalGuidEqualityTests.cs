using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Memory;

namespace DataLinq.Tests.Memory;

public sealed class MemoryCanonicalGuidEqualityTests
{
    private static readonly MemoryGuidId FirstId = new(
        Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
    private static readonly MemoryGuidId SecondId = new(
        Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"));
    private static readonly MemoryGuidId ThirdId = new(
        Guid.Parse("20314253-6475-8697-a8b9-cadbecfd0e1f"));
    private static readonly MemoryGuidId MissingId = new(
        Guid.Parse("ffffffff-eeee-dddd-cccc-bbbbbbbbbbbb"));
    private static readonly Guid SharedDirectGuid =
        Guid.Parse("f1e2d3c4-b5a6-4789-90ab-cdef12345678");
    private static readonly Guid OtherDirectGuid =
        Guid.Parse("e2d3c4b5-a697-4809-a1bc-def234567890");
    private static readonly MemoryGuidId SharedRelatedId = new(
        Guid.Parse("31425364-7586-97a8-b9ca-dbecfd0e1f20"));
    private static readonly MemoryGuidId OtherRelatedId = new(
        Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567"));

    [Test]
    [NotInParallel]
    public async Task PublicQuery_DirectAndTypedGuidEqualitySupportsHitMissReverseAndRebinding()
    {
        var database = CreateDatabase();
        var rows = database.Query().Rows;
        var idProbe = FirstId;
        var reboundQuery = rows.Where(row => row.Id == idProbe);

        var first = reboundQuery.ToArray();
        idProbe = SecondId;
        var second = reboundQuery.ToArray();
        var reversed = rows.Where(row => idProbe == row.Id).ToArray();
        var directMatches = rows
            .Where(row => row.DirectGuid == SharedDirectGuid)
            .ToArray();
        var relatedMatches = rows
            .Where(row => row.RelatedId == SharedRelatedId)
            .ToArray();
        var missing = rows.Where(row => row.Id == MissingId).ToArray();

        await Assert.That(first.Select(static row => row.Id).ToArray())
            .IsEquivalentTo([FirstId]);
        await Assert.That(second.Select(static row => row.Id).ToArray())
            .IsEquivalentTo([SecondId]);
        await Assert.That(reversed.Single()).IsSameReferenceAs(second.Single());
        await Assert.That(directMatches.Select(static row => row.Id).ToArray())
            .IsEquivalentTo([FirstId, ThirdId]);
        await Assert.That(relatedMatches.Select(static row => row.Id).ToArray())
            .IsEquivalentTo([FirstId, SecondId]);
        await Assert.That(missing).IsEmpty();
    }

    [Test]
    [NotInParallel]
    public async Task AnyAndCount_NormalizeOnlyTypedGuidBindingsAndDoNotMaterializeEntities()
    {
        var database = CreateDatabase();
        MemoryGuidIdConverter.Reset();
        var rows = database.Query().Rows;

        var directHit = rows.Any(row => row.DirectGuid == SharedDirectGuid);
        await Assert.That(directHit).IsTrue();
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns).IsEmpty();
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();

        var typedHit = rows.Any(row => row.Id == FirstId);
        var typedCount = rows.Count(row => row.RelatedId == SharedRelatedId);
        var typedMiss = rows.Any(row => MissingId == row.Id);

        await Assert.That(typedHit).IsTrue();
        await Assert.That(typedCount).IsEqualTo(2);
        await Assert.That(typedMiss).IsFalse();
        await Assert.That(MemoryGuidIdConverter.ToProviderColumns)
            .IsEquivalentTo(["id", "related_id", "id"]);
        await Assert.That(MemoryGuidIdConverter.FromProviderColumns).IsEmpty();
        await Assert.That(database.GetMaterializedRowCount<MemoryConvertedRow>()).IsEqualTo(0);
    }

    [Test]
    [NotInParallel]
    public async Task NullableTypedGuidEquality_RemainsUnsupportedBeforeStoreWork()
    {
        var database = CreateDatabase();
        MemoryGuidId? probe = SharedRelatedId;
        var before = database.Diagnostics;

        var exception = Capture<QueryBackendCapabilityException>(() =>
            database.Query().Rows
                .Where(row => row.OptionalRelatedId == probe)
                .ToArray());

        await Assert.That(exception.Feature)
            .IsEqualTo("ComparisonShape:DefaultNullSemantics");
        await Assert.That(exception.Location).IsEqualTo("operations[0].predicate.shape");
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    [NotInParallel]
    public async Task NearbyTypedGuidShapes_RemainUnsupportedBeforeStoreWork()
    {
        var database = CreateDatabase();
        var rows = database.Query().Rows;
        var before = database.Diagnostics;
        var guidProbe = FirstId.Value;
        MemoryGuidId[] selectedIds = [FirstId, SecondId];

        var unwrappedMember = Capture<QueryTranslationException>(() =>
            rows.Where(row => row.Id.Value == guidProbe).ToArray());
        var membership = Capture<QueryBackendCapabilityException>(() =>
            rows.Where(row => selectedIds.Contains(row.Id)).ToArray());
        var ordering = Capture<QueryBackendCapabilityException>(() =>
            rows.OrderBy(static row => row.Id).ToArray());
        var projection = Capture<QueryBackendCapabilityException>(() =>
            rows.Select(static row => row.Id).ToArray());

        await Assert.That(unwrappedMember.ToString()).DoesNotContain(guidProbe.ToString());
        await Assert.That(membership.Feature).IsEqualTo("Predicate:In");
        await Assert.That(ordering.Feature).IsEqualTo("OrderingShape:Other");
        await Assert.That(projection.Feature).IsEqualTo("ScalarProjectionShape:Other");
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    [NotInParallel]
    public async Task TypedGuidBindingFailure_RedactsTheCompletePublicExceptionGraph()
    {
        const string outerSecret = "guid-converter-outer-secret-4821";
        const string innerSecret = "guid-converter-inner-secret-7359";
        var database = CreateDatabase();
        MemoryGuidIdConverter.Reset();
        MemoryGuidIdConverter.SetToProviderProbe(_ =>
            throw new InvalidOperationException(
                outerSecret,
                new Exception(innerSecret)));
        var before = database.Diagnostics;

        QueryTranslationException exception;
        try
        {
            exception = Capture<QueryTranslationException>(() =>
                database.Query().Rows.Any(row => row.Id == FirstId));
        }
        finally
        {
            MemoryGuidIdConverter.Reset();
        }

        await Assert.That(exception.Message).Contains("scalar binding 'p0'");
        await Assert.That(exception.Message).Contains("memory_converted_rows.id");
        await Assert.That(exception.InnerException).IsNull();
        await Assert.That(exception.GetBaseException()).IsSameReferenceAs(exception);
        await Assert.That(exception.ToString()).DoesNotContain(outerSecret);
        await Assert.That(exception.ToString()).DoesNotContain(innerSecret);
        await Assert.That(exception.ToString()).DoesNotContain(FirstId.Value.ToString());
        await Assert.That(database.Diagnostics).IsEqualTo(before);
    }

    [Test]
    [NotInParallel]
    public async Task TypedGuidBindingFailure_PreservesCancellationAndFatalExceptionIdentity()
    {
        var database = CreateDatabase();
        var before = database.Diagnostics;
        Exception[] sentinels =
        [
            new OperationCanceledException("query conversion cancelled"),
            new OutOfMemoryException("query conversion exhausted memory"),
            new AccessViolationException("query conversion accessed invalid memory")
        ];

        foreach (var sentinel in sentinels)
        {
            MemoryGuidIdConverter.Reset();
            MemoryGuidIdConverter.SetToProviderProbe(_ => throw sentinel);

            Exception actual;
            try
            {
                actual = Capture<Exception>(() =>
                    database.Query().Rows.Any(row => row.Id == FirstId));
            }
            finally
            {
                MemoryGuidIdConverter.Reset();
            }

            await Assert.That(actual).IsSameReferenceAs(sentinel);
            await Assert.That(database.Diagnostics).IsEqualTo(before);
        }
    }

    private static MemoryDatabase<MemoryConvertedDatabase> CreateDatabase()
    {
        MemoryGuidIdConverter.Reset();
        var database = new MemoryDatabase<MemoryConvertedDatabase>();
        return database.Seed<MemoryConvertedRow>(
        [
            new MutableMemoryConvertedRow
            {
                Id = FirstId,
                DirectGuid = SharedDirectGuid,
                RelatedId = SharedRelatedId,
                OptionalRelatedId = null
            },
            new MutableMemoryConvertedRow
            {
                Id = SecondId,
                DirectGuid = OtherDirectGuid,
                RelatedId = SharedRelatedId,
                OptionalRelatedId = SharedRelatedId
            },
            new MutableMemoryConvertedRow
            {
                Id = ThirdId,
                DirectGuid = SharedDirectGuid,
                RelatedId = OtherRelatedId,
                OptionalRelatedId = OtherRelatedId
            }
        ]);
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

        throw new InvalidOperationException(
            $"Expected exception of type '{typeof(TException).Name}'.");
    }
}
