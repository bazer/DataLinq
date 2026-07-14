using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DataLinq.Exceptions;
using DataLinq.Linq.Planning;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Memory;
using DataLinq.Metadata;
using DataLinq.SQLite;

namespace DataLinq.Tests.Memory;

public sealed class MemorySQLiteParityTests
{
    private static readonly ParitySeedRow[] SeedRows =
    [
        new(17, 3, "seventeen"),
        new(int.MinValue, 3, "minimum"),
        new(int.MaxValue, 7, "maximum"),
        new(-11, 7, "negative-eleven"),
        new(0, 7, "zero")
    ];

    private static readonly ParityObservation ExpectedObservation = new(
        RootEntities:
            "-2147483648:3:minimum|-11:7:negative-eleven|0:7:zero|" +
            "17:3:seventeen|2147483647:7:maximum",
        RepeatedEqualityEntities: "0:7:zero",
        AscendingEntityIds: "-2147483648,-11,0,17,2147483647",
        DescendingEntityIds: "2147483647,17,0,-11,-2147483648",
        TakeZeroEntityIds: string.Empty,
        OrderedTakenEntityIds: "2147483647,0",
        OverCardinalityEntityIds: "-2147483648,-11,0,17,2147483647",
        OrderedScalarIds: "-2147483648,-11,0,17,2147483647",
        OrderedScalarGroupIds: "3,7,7,3,7",
        ComposedScalarIds: "2147483647,0",
        EmptyScalarIds: string.Empty,
        RootAny: true,
        MatchingAny: true,
        MissingAny: false,
        RootCount: 5,
        MatchingCount: 3,
        MissingCount: 0,
        ProjectedRootAny: true,
        ProjectedMissingAny: false,
        ProjectedRootCount: 5,
        ProjectedMatchingCount: 3,
        ProjectedMissingCount: 0);

    [Test]
    public async Task AdmittedPrimitiveQueryIsland_MatchesSQLiteForTheSameQueryShapes()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);
        await Assert.That(ReferenceEquals(memory.Metadata, sqlite.Provider.Metadata)).IsTrue();

        const int matchingGroupId = 7;
        const int missingGroupId = 99;
        const int matchingId = 0;
        var observations = Observe(
            memory,
            sqlite,
            matchingGroupId,
            missingGroupId,
            matchingId);

        await Assert.That(observations.Memory).IsEqualTo(ExpectedObservation);
        await Assert.That(observations.SQLite).IsEqualTo(ExpectedObservation);
        await Assert.That(memory.Diagnostics.ScanRowsVisited).IsGreaterThan(0);
        await Assert.That(memory.Diagnostics.PredicateEvaluations).IsGreaterThan(0);
    }

    [Test]
    public async Task PagedTerminal_RemainsADeliberateSQLiteOnlyDifference()
    {
        var memory = CreateMemoryDatabase();
        using var sqlite = new SQLiteDatabase<MemoryPrimitiveDatabase>("Data Source=:memory:");
        await InitializeSQLite(sqlite);

        var rows = memory.Model.Rows;
        Expression<Func<bool>> query = () => rows
            .OrderBy(static row => row.Id)
            .Take(2)
            .Any();
        var invocation = ExpressionQueryPlanParser.Convert(
            memory.Metadata,
            query.Body,
            typeof(bool));
        var before = memory.Diagnostics;

        var sqliteResult = ExpressionQueryPlanExecutor.Execute<bool>(
            sqlite.Provider.ReadOnlyAccess,
            invocation);
        var memoryFailure = Capture<QueryTranslationException>(() =>
            ExpressionQueryPlanExecutor.Execute<bool>(memory.ReadSource, invocation));

        await Assert.That(sqliteResult).IsTrue();
        await Assert.That(memoryFailure.Message).Contains("Operation:Pushdown");
        await Assert.That(memory.Diagnostics).IsEqualTo(before);
    }

    private static ProviderPair<ParityObservation> Observe(
        MemoryDatabase<MemoryPrimitiveDatabase> memory,
        SQLiteDatabase<MemoryPrimitiveDatabase> sqlite,
        int matchingGroupId,
        int missingGroupId,
        int matchingId)
    {
        var rows = memory.Model.Rows;
        var rootEntities = ExecuteSequence(
            memory,
            sqlite,
            rows);
        var repeatedEqualityEntities = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => row.GroupId == matchingGroupId)
                .Where(row => row.Id == matchingId));
        var ascendingEntities = ExecuteSequence(
            memory,
            sqlite,
            rows.OrderBy(static row => row.Id));
        var descendingEntities = ExecuteSequence(
            memory,
            sqlite,
            rows.OrderByDescending(static row => row.Id));
        var takeZeroEntities = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Take(0));
        var orderedTakenEntities = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => row.GroupId == matchingGroupId)
                .OrderByDescending(static row => row.Id)
                .Take(2));
        var overCardinalityEntities = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Take(99));
        var orderedScalarIds = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Select(static row => row.Id));
        var orderedScalarGroupIds = ExecuteSequence(
            memory,
            sqlite,
            rows
                .OrderBy(static row => row.Id)
                .Select(static row => row.GroupId));
        var composedScalarIds = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => row.GroupId == matchingGroupId)
                .OrderByDescending(static row => row.Id)
                .Take(2)
                .Select(static row => row.Id));
        var emptyScalarIds = ExecuteSequence(
            memory,
            sqlite,
            rows
                .Where(row => row.GroupId == missingGroupId)
                .OrderBy(static row => row.Id)
                .Select(static row => row.Id));
        var rootAny = ExecuteScalar(memory, sqlite, () => rows.Any());
        var matchingAny = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Any(row => row.GroupId == matchingGroupId));
        var missingAny = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Any(row => row.GroupId == missingGroupId));
        var rootCount = ExecuteScalar(memory, sqlite, () => rows.Count());
        var matchingCount = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Count(row => row.GroupId == matchingGroupId));
        var missingCount = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Count(row => row.GroupId == missingGroupId));
        var projectedRootAny = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Select(static row => row.Id).Any());
        var projectedMissingAny = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .Where(row => row.GroupId == missingGroupId)
                .Select(static row => row.Id)
                .Any());
        var projectedRootCount = ExecuteScalar(
            memory,
            sqlite,
            () => rows.Select(static row => row.GroupId).Count());
        var projectedMatchingCount = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .Where(row => row.GroupId == matchingGroupId)
                .Select(static row => row.GroupId)
                .Count());
        var projectedMissingCount = ExecuteScalar(
            memory,
            sqlite,
            () => rows
                .Where(row => row.GroupId == missingGroupId)
                .Select(static row => row.GroupId)
                .Count());

        return new ProviderPair<ParityObservation>(
            CreateObservation(
                rootEntities.Memory,
                repeatedEqualityEntities.Memory,
                ascendingEntities.Memory,
                descendingEntities.Memory,
                takeZeroEntities.Memory,
                orderedTakenEntities.Memory,
                overCardinalityEntities.Memory,
                orderedScalarIds.Memory,
                orderedScalarGroupIds.Memory,
                composedScalarIds.Memory,
                emptyScalarIds.Memory,
                rootAny.Memory,
                matchingAny.Memory,
                missingAny.Memory,
                rootCount.Memory,
                matchingCount.Memory,
                missingCount.Memory,
                projectedRootAny.Memory,
                projectedMissingAny.Memory,
                projectedRootCount.Memory,
                projectedMatchingCount.Memory,
                projectedMissingCount.Memory),
            CreateObservation(
                rootEntities.SQLite,
                repeatedEqualityEntities.SQLite,
                ascendingEntities.SQLite,
                descendingEntities.SQLite,
                takeZeroEntities.SQLite,
                orderedTakenEntities.SQLite,
                overCardinalityEntities.SQLite,
                orderedScalarIds.SQLite,
                orderedScalarGroupIds.SQLite,
                composedScalarIds.SQLite,
                emptyScalarIds.SQLite,
                rootAny.SQLite,
                matchingAny.SQLite,
                missingAny.SQLite,
                rootCount.SQLite,
                matchingCount.SQLite,
                missingCount.SQLite,
                projectedRootAny.SQLite,
                projectedMissingAny.SQLite,
                projectedRootCount.SQLite,
                projectedMatchingCount.SQLite,
                projectedMissingCount.SQLite));
    }

    private static ParityObservation CreateObservation(
        MemoryPrimitiveRow[] rootEntities,
        MemoryPrimitiveRow[] repeatedEqualityEntities,
        MemoryPrimitiveRow[] ascendingEntities,
        MemoryPrimitiveRow[] descendingEntities,
        MemoryPrimitiveRow[] takeZeroEntities,
        MemoryPrimitiveRow[] orderedTakenEntities,
        MemoryPrimitiveRow[] overCardinalityEntities,
        int[] orderedScalarIds,
        int[] orderedScalarGroupIds,
        int[] composedScalarIds,
        int[] emptyScalarIds,
        bool rootAny,
        bool matchingAny,
        bool missingAny,
        int rootCount,
        int matchingCount,
        int missingCount,
        bool projectedRootAny,
        bool projectedMissingAny,
        int projectedRootCount,
        int projectedMatchingCount,
        int projectedMissingCount) =>
        new(
            RootEntities: SnapshotUnordered(rootEntities),
            RepeatedEqualityEntities: SnapshotUnordered(repeatedEqualityEntities),
            AscendingEntityIds: SnapshotIds(ascendingEntities),
            DescendingEntityIds: SnapshotIds(descendingEntities),
            TakeZeroEntityIds: SnapshotIds(takeZeroEntities),
            OrderedTakenEntityIds: SnapshotIds(orderedTakenEntities),
            OverCardinalityEntityIds: SnapshotIds(overCardinalityEntities),
            OrderedScalarIds: string.Join(",", orderedScalarIds),
            OrderedScalarGroupIds: string.Join(",", orderedScalarGroupIds),
            ComposedScalarIds: string.Join(",", composedScalarIds),
            EmptyScalarIds: string.Join(",", emptyScalarIds),
            RootAny: rootAny,
            MatchingAny: matchingAny,
            MissingAny: missingAny,
            RootCount: rootCount,
            MatchingCount: matchingCount,
            MissingCount: missingCount,
            ProjectedRootAny: projectedRootAny,
            ProjectedMissingAny: projectedMissingAny,
            ProjectedRootCount: projectedRootCount,
            ProjectedMatchingCount: projectedMatchingCount,
            ProjectedMissingCount: projectedMissingCount);

    private static string SnapshotUnordered(MemoryPrimitiveRow[] rows) =>
        string.Join(
            "|",
            rows
                .OrderBy(static row => row.Id)
                .Select(static row => $"{row.Id}:{row.GroupId}:{row.Name}"));

    private static string SnapshotIds(MemoryPrimitiveRow[] rows) =>
        string.Join(",", rows.Select(static row => row.Id));

    private static ProviderPair<T[]> ExecuteSequence<T>(
        MemoryDatabase<MemoryPrimitiveDatabase> memory,
        SQLiteDatabase<MemoryPrimitiveDatabase> sqlite,
        IQueryable<T> query)
    {
        var invocation = ExpressionQueryPlanParser.Convert(
            memory.Metadata,
            query.Expression,
            typeof(T));

        return new ProviderPair<T[]>(
            ExpressionQueryPlanExecutor
                .ExecuteEnumerable<T>(memory.ReadSource, invocation)
                .ToArray(),
            ExpressionQueryPlanExecutor
                .ExecuteEnumerable<T>(sqlite.Provider.ReadOnlyAccess, invocation)
                .ToArray());
    }

    private static ProviderPair<T> ExecuteScalar<T>(
        MemoryDatabase<MemoryPrimitiveDatabase> memory,
        SQLiteDatabase<MemoryPrimitiveDatabase> sqlite,
        Expression<Func<T>> query)
    {
        var invocation = ExpressionQueryPlanParser.Convert(
            memory.Metadata,
            query.Body,
            typeof(T));

        return new ProviderPair<T>(
            ExpressionQueryPlanExecutor.Execute<T>(memory.ReadSource, invocation),
            ExpressionQueryPlanExecutor.Execute<T>(sqlite.Provider.ReadOnlyAccess, invocation));
    }

    private static MemoryDatabase<MemoryPrimitiveDatabase> CreateMemoryDatabase()
    {
        var database = new MemoryDatabase<MemoryPrimitiveDatabase>();
        var rows = SeedRows
            .Select(row => CreateCanonicalRow(database, row))
            .ToArray();
        return database.SeedCanonical<MemoryPrimitiveRow>(rows);
    }

    private static object?[] CreateCanonicalRow(
        MemoryDatabase<MemoryPrimitiveDatabase> database,
        ParitySeedRow row)
    {
        var table = database.Metadata.GetTableModel(typeof(MemoryPrimitiveRow)).Table;
        var values = new object?[table.ColumnCount];
        values[table.GetColumnByDbName("id").Index] = row.Id;
        values[table.GetColumnByDbName("group_id").Index] = row.GroupId;
        values[table.GetColumnByDbName("name").Index] = row.Name;
        return values;
    }

    private static async Task SeedSQLite(
        SQLiteDatabase<MemoryPrimitiveDatabase> database)
    {
        foreach (var row in SeedRows)
        {
            var escapedName = row.Name.Replace("'", "''", StringComparison.Ordinal);
            var affected = database.Provider.DatabaseAccess.ExecuteNonQuery(
                $"INSERT INTO memory_primitive_rows (id, group_id, name) " +
                $"VALUES ({row.Id}, {row.GroupId}, '{escapedName}')");

            await Assert.That(affected).IsEqualTo(1);
        }
    }

    private static async Task InitializeSQLite(
        SQLiteDatabase<MemoryPrimitiveDatabase> database)
    {
        var creation = DatabaseType.SQLite.CreateDatabaseFromMetadata(
            database.Provider.Metadata,
            database.Provider.DatabaseName,
            database.Provider.ConnectionString,
            foreignKeyRestrict: true);

        await Assert.That(creation.HasFailed).IsFalse();
        await SeedSQLite(database);
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

    private sealed record ParitySeedRow(int Id, int GroupId, string Name);

    private sealed record ProviderPair<T>(T Memory, T SQLite);

    private sealed record ParityObservation(
        string RootEntities,
        string RepeatedEqualityEntities,
        string AscendingEntityIds,
        string DescendingEntityIds,
        string TakeZeroEntityIds,
        string OrderedTakenEntityIds,
        string OverCardinalityEntityIds,
        string OrderedScalarIds,
        string OrderedScalarGroupIds,
        string ComposedScalarIds,
        string EmptyScalarIds,
        bool RootAny,
        bool MatchingAny,
        bool MissingAny,
        int RootCount,
        int MatchingCount,
        int MissingCount,
        bool ProjectedRootAny,
        bool ProjectedMissingAny,
        int ProjectedRootCount,
        int ProjectedMatchingCount,
        int ProjectedMissingCount);
}
