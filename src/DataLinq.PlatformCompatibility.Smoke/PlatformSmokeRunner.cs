using System.Diagnostics;
using System.Linq.Expressions;
using System.Threading;
using DataLinq.Core.Factories;
using DataLinq.Linq;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Metadata;
using DataLinq.SQLite;

namespace DataLinq.PlatformCompatibility.Smoke;

public sealed record PlatformSmokeProjection(string NormalizedTitle, int PriorityScore);

public sealed record PlatformSmokeResult(
    int OwnerCount,
    int TaskCount,
    int OpenTaskCount,
    string FirstTaskTitle,
    string RelatedOwnerName,
    PlatformSmokeProjection[] Projections,
    string StrictParserProjectionTitle,
    TimeSpan SchemaDuration,
    TimeSpan SeedDuration,
    TimeSpan FirstQueryDuration,
    TimeSpan RepeatedQueryDuration)
{
    public bool Passed =>
        OwnerCount == 2 &&
        TaskCount == 3 &&
        OpenTaskCount == 2 &&
        FirstTaskTitle == "Compile generated hooks" &&
        RelatedOwnerName == "Ada" &&
        Projections.Length == 3 &&
        Projections[0].NormalizedTitle == "COMPILE GENERATED HOOKS" &&
        Projections[0].PriorityScore == 3 &&
        StrictParserProjectionTitle == "COMPILE GENERATED HOOKS";

    public string ToDisplayString()
    {
        var status = Passed ? "passed" : "failed";
        return string.Join(Environment.NewLine, [
            $"DataLinq platform smoke {status}",
            $"owners={OwnerCount}, tasks={TaskCount}, open={OpenTaskCount}",
            $"first-task=\"{FirstTaskTitle}\", related-owner=\"{RelatedOwnerName}\"",
            $"projection=\"{Projections[0].NormalizedTitle}\"/{Projections[0].PriorityScore}",
            $"strict-parser-projection=\"{StrictParserProjectionTitle}\"",
            $"schema-ms={SchemaDuration.TotalMilliseconds:0.###}",
            $"seed-ms={SeedDuration.TotalMilliseconds:0.###}",
            $"first-query-ms={FirstQueryDuration.TotalMilliseconds:0.###}",
            $"repeated-query-ms={RepeatedQueryDuration.TotalMilliseconds:0.###}"
        ]);
    }
}

public static class PlatformSmokeRunner
{
    private static int nextDatabaseId;

    public static PlatformSmokeResult Run(Action<string>? reportStage = null)
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

    public static async ValueTask<PlatformSmokeResult> RunAsync(Func<string, ValueTask>? reportStage = null)
    {
        await ReportStage(reportStage, "creating-connection-string");

        var databaseName = $"datalinq_phase8_{Interlocked.Increment(ref nextDatabaseId)}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";

        return await RunAsync(connectionString, databaseName, reportStage);
    }

    public static PlatformSmokeResult Run(string connectionString, string databaseName, Action<string>? reportStage = null)
    {
        Func<string, ValueTask>? asyncReportStage = reportStage is null
            ? null
            : stage =>
            {
                reportStage(stage);
                return ValueTask.CompletedTask;
            };

        return RunAsync(connectionString, databaseName, asyncReportStage).AsTask().GetAwaiter().GetResult();
    }

    public static async ValueTask<PlatformSmokeResult> RunAsync(
        string connectionString,
        string databaseName,
        Func<string, ValueTask>? reportStage = null)
    {
        await ReportStage(reportStage, "registering-sqlite-provider");
        SQLiteProvider.RegisterProvider();

        await ReportStage(reportStage, "opening-generated-database");
        using var database = new SQLiteDatabase<PlatformSmokeDb>(connectionString, databaseName);

        await ReportStage(reportStage, "creating-schema-from-generated-metadata");
        var schemaWatch = Stopwatch.StartNew();
        var createResult = PluginHook.CreateDatabaseFromMetadata(
            DatabaseType.SQLite,
            database.Provider.Metadata,
            databaseName,
            database.Provider.ConnectionString,
            true);
        schemaWatch.Stop();

        if (createResult.HasFailed)
            throw new InvalidOperationException(createResult.Failure.ToString());

        await ReportStage(reportStage, "enabling-foreign-keys");
        database.Provider.DatabaseAccess.ExecuteNonQuery("PRAGMA foreign_keys = ON");

        await ReportStage(reportStage, "seeding-generated-models");
        var seedWatch = Stopwatch.StartNew();
        database.Insert(new MutablePlatformSmokeOwner
        {
            Id = 1,
            Name = "Ada"
        });
        database.Insert(new MutablePlatformSmokeOwner
        {
            Id = 2,
            Name = "Grace"
        });
        database.Insert(new MutablePlatformSmokeTask
        {
            Id = 10,
            OwnerId = 1,
            Title = "Compile generated hooks",
            Priority = 2,
            Completed = false
        });
        database.Insert(new MutablePlatformSmokeTask
        {
            Id = 11,
            OwnerId = 1,
            Title = "Publish AOT smoke",
            Priority = 3,
            Completed = false
        });
        database.Insert(new MutablePlatformSmokeTask
        {
            Id = 12,
            OwnerId = 2,
            Title = "Document WASM proof",
            Priority = 1,
            Completed = true
        });
        seedWatch.Stop();

        await ReportStage(reportStage, "querying-generated-relation");
        var firstQueryWatch = Stopwatch.StartNew();
        var firstTask = database.Query().Tasks
            .Where(x => x.Id == 10)
            .ToArray()
            .Single();
        var relatedOwnerName = firstTask.Owner.Name;
        firstQueryWatch.Stop();

        await ReportStage(reportStage, "querying-generated-projection");
        var repeatedQueryWatch = Stopwatch.StartNew();
        var ownerCount = database.Query().Owners.ToArray().Length;
        var taskCount = database.Query().Tasks.ToArray().Length;
        var openTaskCount = database.Query().Tasks
            .Where(x => x.Priority >= 2)
            .ToArray()
            .Length;
        var projections = database.Query().Tasks
            .OrderBy(x => x.Id)
            .Select(x => new PlatformSmokeProjection(x.Title.Trim().ToUpper(), x.Priority + 1))
            .ToArray();
        repeatedQueryWatch.Stop();

        await ReportStage(reportStage, "verifying-strict-parser-projection");
        var strictParserProjectionTitle = VerifyStrictParserProjection(database, firstTask);

        return new PlatformSmokeResult(
            ownerCount,
            taskCount,
            openTaskCount,
            firstTask.Title,
            relatedOwnerName,
            projections,
            strictParserProjectionTitle,
            schemaWatch.Elapsed,
            seedWatch.Elapsed,
            firstQueryWatch.Elapsed,
            repeatedQueryWatch.Elapsed);
    }

    private static string VerifyStrictParserProjection(SQLiteDatabase<PlatformSmokeDb> database, PlatformSmokeTask row)
    {
        var query = database.Query().Tasks
            .Where(x => x.Priority >= 2)
            .OrderBy(x => x.Id)
            .Take(2)
            .Select(x => x.Title.Trim().ToUpper());

        var plan = ExpressionQueryPlanParser.Convert(
            database.Provider.Metadata,
            query.Expression,
            typeof(string),
            ExpressionQueryPlanParserOptions.AotStrict);

        var select = new QueryPlanSqlBuilder(plan, database.Provider.ReadOnlyAccess)
            .BuildSelect<PlatformSmokeTask>();
        var sql = select.ToSql();
        if (string.IsNullOrWhiteSpace(sql.Text))
            throw new InvalidOperationException("Strict parser smoke did not render SQL.");

        Expression<Func<PlatformSmokeTask, string>> projection = x => x.Title.Trim().ToUpper();
        return (string?)ProjectionExpressionEvaluator.Evaluate(
            projection.Body,
            projection.Parameters[0],
            row,
            ProjectionEvaluationOptions.AotStrict)
            ?? throw new InvalidOperationException("Strict parser smoke projection returned null.");
    }

    private static ValueTask ReportStage(Func<string, ValueTask>? reportStage, string stage)
    {
        return reportStage is null ? ValueTask.CompletedTask : reportStage(stage);
    }
}
