using System;
using System.Linq;
using System.Threading;
using DataLinq.Exceptions;
using DataLinq.Instances;

namespace DataLinq.Memory.PlatformCompatibility.Smoke;

public sealed record MemoryPlatformSmokeResult(
    int PrimitiveStoredRowCount,
    int GuidStoredRowCount,
    bool PrimitivePrimaryKeyHit,
    bool PrimitivePrimaryKeyMiss,
    bool CanonicalGuidPrimaryKeyHit,
    bool CanonicalGuidCellsStoredAsGuid,
    bool DirectGuidRoundTrip,
    bool EntityMaterializedFromMemory,
    int[] FilteredIds,
    int[] OrderedIds,
    int[] ProjectedGroupIds,
    bool HasRows,
    int RowCount,
    string UnsupportedDiagnostic,
    bool UnsupportedRejectedBeforeWork,
    bool PreCancellationPreserved,
    bool PreCancellationRejectedBeforeWork,
    int SupportedCapabilityTokenCount)
{
    public bool Passed =>
        PrimitiveStoredRowCount == 3 &&
        GuidStoredRowCount == 1 &&
        PrimitivePrimaryKeyHit &&
        PrimitivePrimaryKeyMiss &&
        CanonicalGuidPrimaryKeyHit &&
        CanonicalGuidCellsStoredAsGuid &&
        DirectGuidRoundTrip &&
        EntityMaterializedFromMemory &&
        FilteredIds.SequenceEqual([-5, 17]) &&
        OrderedIds.SequenceEqual([-5, 17]) &&
        ProjectedGroupIds.SequenceEqual([7, 7, 3]) &&
        HasRows &&
        RowCount == 3 &&
        UnsupportedDiagnostic.Contains(
            "Backend 'memory' cannot execute query plan feature 'SourceCount:Multiple'",
            StringComparison.Ordinal) &&
        UnsupportedRejectedBeforeWork &&
        PreCancellationPreserved &&
        PreCancellationRejectedBeforeWork &&
        SupportedCapabilityTokenCount == 31;

    public string ToDisplayString()
    {
        var status = Passed ? "passed" : "failed";
        return string.Join(Environment.NewLine, [
            $"DataLinq memory platform smoke {status}",
            $"primitive-rows={PrimitiveStoredRowCount}, guid-rows={GuidStoredRowCount}",
            $"pk-hit={PrimitivePrimaryKeyHit}, pk-miss={PrimitivePrimaryKeyMiss}, canonical-guid-cells={CanonicalGuidCellsStoredAsGuid}",
            $"filtered=[{string.Join(',', FilteredIds)}], ordered=[{string.Join(',', OrderedIds)}], projected=[{string.Join(',', ProjectedGroupIds)}]",
            $"any={HasRows}, count={RowCount}, capabilities={SupportedCapabilityTokenCount}",
            $"unsupported-before-work={UnsupportedRejectedBeforeWork}, pre-cancelled-before-work={PreCancellationRejectedBeforeWork}",
            $"unsupported-diagnostic=\"{UnsupportedDiagnostic}\""
        ]);
    }
}

public static class MemoryPlatformSmokeRunner
{
    private static readonly Guid KnownGuidId = new("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid KnownDirectGuid = new("f1e2d3c4-b5a6-4789-90ab-cdef12345678");

    public static MemoryPlatformSmokeResult Run(Action<string>? reportStage = null)
    {
        Func<string, ValueTask>? asyncReportStage = reportStage is null
            ? null
            : stage =>
            {
                reportStage(stage);
                return ValueTask.CompletedTask;
            };

        return RunAsync(asyncReportStage).AsTask().GetAwaiter().GetResult();
    }

    public static async ValueTask<MemoryPlatformSmokeResult> RunAsync(
        Func<string, ValueTask>? reportStage = null)
    {
        await ReportStage(reportStage, "constructing-memory-database");
        var database = new MemoryDatabase<MemoryPlatformSmokeDatabase>();

        await ReportStage(reportStage, "seeding-generated-primitive-rows");
        database.Seed<MemoryPlatformPrimitiveRow>(
        [
            new MutableMemoryPlatformPrimitiveRow
            {
                Id = 17,
                GroupId = 7,
                Name = "seventeen"
            },
            new MutableMemoryPlatformPrimitiveRow
            {
                Id = -5,
                GroupId = 7,
                Name = "minus-five"
            },
            new MutableMemoryPlatformPrimitiveRow
            {
                Id = 42,
                GroupId = 3,
                Name = "forty-two"
            }
        ]);

        await ReportStage(reportStage, "seeding-generated-guid-row");
        database.Seed<MemoryPlatformGuidRow>(
        [
            new MutableMemoryPlatformGuidRow
            {
                Id = new MemoryPlatformGuidId(KnownGuidId),
                DirectGuid = KnownDirectGuid,
                Name = "canonical-guid"
            }
        ]);

        var query = database.Query();

        await ReportStage(reportStage, "probing-primary-keys");
        var primitiveHit = database.Find<MemoryPlatformPrimitiveRow>(DataLinqKey.FromValue(17));
        var primitiveMiss = database.Find<MemoryPlatformPrimitiveRow>(DataLinqKey.FromValue(999));
        var guidHit = database.Find<MemoryPlatformGuidRow>(DataLinqKey.FromValue(KnownGuidId));

        var guidTable = database.Metadata.GetTableModel(typeof(MemoryPlatformGuidRow)).Table;
        var storedGuidRow = database
            .GetCanonicalRowsForTest<MemoryPlatformGuidRow>()
            .Single();
        var storedGuidId = storedGuidRow[guidTable.GetColumnByDbName("id")];
        var storedDirectGuid = storedGuidRow[guidTable.GetColumnByDbName("direct_guid")];

        await ReportStage(reportStage, "querying-captured-equality");
        var selectedGroupId = 7;
        var filteredIds = query.PrimitiveRows
            .Where(row => row.GroupId == selectedGroupId)
            .ToArray()
            .Select(static row => row.Id)
            .Order()
            .ToArray();

        await ReportStage(reportStage, "querying-order-and-take");
        var orderedIds = query.PrimitiveRows
            .OrderBy(static row => row.Id)
            .Take(2)
            .ToArray()
            .Select(static row => row.Id)
            .ToArray();

        await ReportStage(reportStage, "querying-direct-scalar-projection");
        var projectedGroupIds = query.PrimitiveRows
            .OrderBy(static row => row.Id)
            .Select(static row => row.GroupId)
            .ToArray();

        await ReportStage(reportStage, "querying-any-and-count");
        var hasRows = query.PrimitiveRows.Any();
        var rowCount = query.PrimitiveRows.Count();

        await ReportStage(reportStage, "verifying-unsupported-self-join");
        var beforeUnsupported = database.Diagnostics;
        var unsupported = Capture<QueryTranslationException>(() =>
            query.PrimitiveRows
                .Join(
                    query.PrimitiveRows,
                    static left => left.Id,
                    static right => right.Id,
                    static (left, _) => left)
                .ToArray());
        var unsupportedRejectedBeforeWork = database.Diagnostics == beforeUnsupported;

        await ReportStage(reportStage, "verifying-pre-cancellation");
        var beforeCancellation = database.Diagnostics;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = Capture<OperationCanceledException>(() =>
            database.Execute(query.PrimitiveRows, cancellation.Token).ToArray());
        var preCancellationRejectedBeforeWork = database.Diagnostics == beforeCancellation;

        return new MemoryPlatformSmokeResult(
            PrimitiveStoredRowCount: database.GetStoredRowCount<MemoryPlatformPrimitiveRow>(),
            GuidStoredRowCount: database.GetStoredRowCount<MemoryPlatformGuidRow>(),
            PrimitivePrimaryKeyHit: primitiveHit is { Id: 17, GroupId: 7, Name: "seventeen" },
            PrimitivePrimaryKeyMiss: primitiveMiss is null,
            CanonicalGuidPrimaryKeyHit: guidHit is
            {
                Id.Value: var guidId,
                DirectGuid: var directGuid,
                Name: "canonical-guid"
            } && guidId == KnownGuidId && directGuid == KnownDirectGuid,
            CanonicalGuidCellsStoredAsGuid:
                storedGuidId is Guid canonicalId && canonicalId == KnownGuidId &&
                storedDirectGuid is Guid canonicalDirectGuid && canonicalDirectGuid == KnownDirectGuid,
            DirectGuidRoundTrip: guidHit?.DirectGuid == KnownDirectGuid,
            EntityMaterializedFromMemory:
                primitiveHit is not null && ReferenceEquals(primitiveHit.GetReadSource(), database.ReadSource),
            FilteredIds: filteredIds,
            OrderedIds: orderedIds,
            ProjectedGroupIds: projectedGroupIds,
            HasRows: hasRows,
            RowCount: rowCount,
            UnsupportedDiagnostic: unsupported.Message,
            UnsupportedRejectedBeforeWork: unsupportedRejectedBeforeWork,
            PreCancellationPreserved: cancelled.CancellationToken == cancellation.Token,
            PreCancellationRejectedBeforeWork: preCancellationRejectedBeforeWork,
            SupportedCapabilityTokenCount: database.SupportedCapabilityTokens.Count);
    }

    private static async ValueTask ReportStage(
        Func<string, ValueTask>? reportStage,
        string stage)
    {
        if (reportStage is not null)
            await reportStage(stage);
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
