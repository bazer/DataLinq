using System.Diagnostics;
using System.Linq.Expressions;
using System.Threading;
using DataLinq.Core.Factories;
using DataLinq.Exceptions;
using DataLinq.Linq;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Linq.Planning.Sql;
using DataLinq.Metadata;
using DataLinq.SQLite;
using Microsoft.Data.Sqlite;

namespace DataLinq.PlatformCompatibility.Smoke;

public sealed record PlatformSmokeProjection(string NormalizedTitle, int PriorityScore);

public sealed record PlatformSmokeExpressionRoute(int OpenTaskCount, string FirstTaskTitle);

public sealed record PlatformSmokeJoinedTaskOwner(int Id, string Title, string OwnerName);

public sealed record PlatformSmokeQueryCoverage(
    int PrioritySum,
    int PriorityMin,
    int PriorityMax,
    double PriorityAverage,
    bool HasOpenTasks,
    string PagedTaskTitle,
    int OwnersWithOpenTasks,
    string FirstJoinedTaskOwner,
    int LocalMembershipCount,
    int NullableEstimateCount,
    string UnsupportedDiagnostic);

public sealed record PlatformSmokeResult(
    int OwnerCount,
    int TaskCount,
    int OpenTaskCount,
    string FirstTaskTitle,
    string RelatedOwnerName,
    PlatformSmokeProjection[] Projections,
    PlatformSmokeExpressionRoute ExpressionRoute,
    PlatformSmokeQueryCoverage QueryCoverage,
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
        ExpressionRoute.OpenTaskCount == 2 &&
        ExpressionRoute.FirstTaskTitle == "Compile generated hooks" &&
        QueryCoverage.PrioritySum == 6 &&
        QueryCoverage.PriorityMin == 1 &&
        QueryCoverage.PriorityMax == 3 &&
        Math.Abs(QueryCoverage.PriorityAverage - 2d) < 0.001d &&
        QueryCoverage.HasOpenTasks &&
        QueryCoverage.PagedTaskTitle == "Publish AOT smoke" &&
        QueryCoverage.OwnersWithOpenTasks == 1 &&
        QueryCoverage.FirstJoinedTaskOwner == "Ada:Compile generated hooks" &&
        QueryCoverage.LocalMembershipCount == 2 &&
        QueryCoverage.NullableEstimateCount == 1 &&
        QueryCoverage.UnsupportedDiagnostic.Contains("Method 'IsKnownTaskTitle'", StringComparison.Ordinal) &&
        StrictParserProjectionTitle == "COMPILE GENERATED HOOKS";

    public string ToDisplayString()
    {
        var status = Passed ? "passed" : "failed";
        return string.Join(Environment.NewLine, [
            $"DataLinq platform smoke {status}",
            $"owners={OwnerCount}, tasks={TaskCount}, open={OpenTaskCount}",
            $"first-task=\"{FirstTaskTitle}\", related-owner=\"{RelatedOwnerName}\"",
            $"projection=\"{Projections[0].NormalizedTitle}\"/{Projections[0].PriorityScore}",
            $"expression-route=\"{ExpressionRoute.FirstTaskTitle}\"/open={ExpressionRoute.OpenTaskCount}",
            $"coverage=sum:{QueryCoverage.PrioritySum}, avg:{QueryCoverage.PriorityAverage:0.###}, page:\"{QueryCoverage.PagedTaskTitle}\", relation-owners:{QueryCoverage.OwnersWithOpenTasks}, join:\"{QueryCoverage.FirstJoinedTaskOwner}\", local:{QueryCoverage.LocalMembershipCount}, nullable:{QueryCoverage.NullableEstimateCount}",
            $"unsupported-diagnostic=\"{QueryCoverage.UnsupportedDiagnostic}\"",
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

        await VerifyGeneratedMetadata(reportStage);
        await VerifyRawSqliteConnection(connectionString, reportStage);
        await VerifyRawSqliteKeepAlivePattern(connectionString, reportStage);

        await ReportStage(reportStage, "constructing-generated-database");
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
            EstimateHours = null,
            Completed = false
        });
        database.Insert(new MutablePlatformSmokeTask
        {
            Id = 11,
            OwnerId = 1,
            Title = "Publish AOT smoke",
            Priority = 3,
            EstimateHours = 4,
            Completed = false
        });
        database.Insert(new MutablePlatformSmokeTask
        {
            Id = 12,
            OwnerId = 2,
            Title = "Document WASM proof",
            Priority = 1,
            EstimateHours = 1,
            Completed = true
        });
        seedWatch.Stop();

        var expressionProvider = ExpressionQueryPlanProvider.ForExecution(database.Provider.ReadOnlyAccess);
        var owners = expressionProvider.CreateRoot<PlatformSmokeOwner>();
        var tasks = expressionProvider.CreateRoot<PlatformSmokeTask>();

        await ReportStage(reportStage, "querying-generated-relation");
        var firstQueryWatch = Stopwatch.StartNew();
        var firstTask = tasks
            .Where(x => x.Id == 10)
            .ToArray()
            .Single();
        var relatedOwnerName = firstTask.Owner.Name;
        firstQueryWatch.Stop();

        await ReportStage(reportStage, "querying-generated-projection");
        var repeatedQueryWatch = Stopwatch.StartNew();
        var ownerCount = owners.Count();
        var taskCount = tasks.Count();
        var openTaskCount = tasks.Count(x => x.Priority >= 2);
        var projections = tasks
            .OrderBy(x => x.Id)
            .ToArray()
            .Select(x => new PlatformSmokeProjection(x.Title.Trim().ToUpper(), x.Priority + 1))
            .ToArray();
        repeatedQueryWatch.Stop();

        await ReportStage(reportStage, "querying-expression-parser-route");
        var expressionRoute = VerifyExpressionParserRoute(tasks);

        await ReportStage(reportStage, "querying-documented-subset-coverage");
        var queryCoverage = VerifyDocumentedSubsetCoverage(owners, tasks);

        await ReportStage(reportStage, "verifying-strict-parser-projection");
        var strictParserProjectionTitle = VerifyStrictParserProjection(database, tasks, firstTask);

        return new PlatformSmokeResult(
            ownerCount,
            taskCount,
            openTaskCount,
            firstTask.Title,
            relatedOwnerName,
            projections,
            expressionRoute,
            queryCoverage,
            strictParserProjectionTitle,
            schemaWatch.Elapsed,
            seedWatch.Elapsed,
            firstQueryWatch.Elapsed,
            repeatedQueryWatch.Elapsed);
    }

    private static async ValueTask VerifyGeneratedMetadata(Func<string, ValueTask>? reportStage)
    {
        await ReportStage(reportStage, "building-generated-metadata-draft");
        var draft = PlatformSmokeDb.GetDataLinqGeneratedMetadata();
        if (draft is null)
            throw new InvalidOperationException("Generated metadata draft probe returned null.");

        await ReportStage(reportStage, "building-generated-metadata-definition");
        var metadata = MetadataFromTypeFactory.ParseDatabaseFromDatabaseModel<PlatformSmokeDb>();
        if (metadata.HasFailed)
            throw new InvalidOperationException(metadata.Failure.ToString());
    }

    private static async ValueTask VerifyRawSqliteConnection(
        string connectionString,
        Func<string, ValueTask>? reportStage)
    {
        await ReportStage(reportStage, "opening-raw-sqlite-connection");
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        await ReportStage(reportStage, "querying-raw-sqlite-version");
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT sqlite_version();";
            var version = command.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException("Raw SQLite version probe returned no version text.");
        }

        await ReportStage(reportStage, "executing-raw-read-uncommitted-pragma");
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA read_uncommitted = true;";
            command.ExecuteNonQuery();
        }

        await ReportStage(reportStage, "executing-raw-journal-mode-pragma");
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode = WAL;";
            command.ExecuteNonQuery();
        }
    }

    private static async ValueTask VerifyRawSqliteKeepAlivePattern(
        string connectionString,
        Func<string, ValueTask>? reportStage)
    {
        await ReportStage(reportStage, "opening-raw-keepalive-sqlite-connection");
        using var keepAliveConnection = new SqliteConnection(connectionString);
        keepAliveConnection.Open();

        await ReportStage(reportStage, "opening-second-raw-sqlite-connection");
        using var secondConnection = new SqliteConnection(connectionString);
        secondConnection.Open();

        await ReportStage(reportStage, "executing-second-raw-read-uncommitted-pragma");
        using (var command = secondConnection.CreateCommand())
        {
            command.CommandText = "PRAGMA read_uncommitted = true;";
            command.ExecuteNonQuery();
        }

        await ReportStage(reportStage, "executing-second-raw-journal-mode-pragma");
        using (var command = secondConnection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode = WAL;";
            command.ExecuteNonQuery();
        }
    }

    private static PlatformSmokeExpressionRoute VerifyExpressionParserRoute(IQueryable<PlatformSmokeTask> tasks)
    {
        var openTaskCount = tasks.Count(x => x.Priority >= 2);
        var firstTaskTitle = tasks
            .Where(x => x.Priority >= 2)
            .OrderBy(x => x.Id)
            .First()
            .Title;

        return new PlatformSmokeExpressionRoute(openTaskCount, firstTaskTitle);
    }

    private static PlatformSmokeQueryCoverage VerifyDocumentedSubsetCoverage(
        IQueryable<PlatformSmokeOwner> owners,
        IQueryable<PlatformSmokeTask> tasks)
    {
        var prioritySum = tasks.Sum(x => x.Priority);
        var priorityMin = tasks.Min(x => x.Priority);
        var priorityMax = tasks.Max(x => x.Priority);
        var priorityAverage = tasks.Average(x => x.Priority);
        var hasOpenTasks = tasks.Any(x => !x.Completed);
        var pagedTasks = tasks
            .OrderBy(x => x.Id)
            .Skip(1)
            .Take(1)
            .ToArray();
        var pagedTaskTitle = pagedTasks.Single().Title;
        var ownersWithOpenTasks = owners.Count(owner => owner.Tasks.Any(task => task.Completed == false));
        var firstJoinedTaskOwner = tasks
            .Join(
                owners,
                task => task.OwnerId,
                owner => owner.Id,
                (task, owner) => new PlatformSmokeJoinedTaskOwner(task.Id, task.Title, owner.Name))
            .ToList()
            .OrderBy(static row => row.Id)
            .Select(static row => $"{row.OwnerName}:{row.Title}")
            .First();
        var selectedTaskIds = new[] { 10, 12, 99 };
        var localMembershipCount = tasks.Count(task => selectedTaskIds.Contains(task.Id));
        var nullableEstimateCount = tasks.Count(task =>
            task.EstimateHours != null &&
            task.EstimateHours.Value >= 4);
        var unsupportedDiagnostic = CaptureUnsupportedDiagnostic(tasks);

        return new PlatformSmokeQueryCoverage(
            prioritySum,
            priorityMin,
            priorityMax,
            priorityAverage,
            hasOpenTasks,
            pagedTaskTitle,
            ownersWithOpenTasks,
            firstJoinedTaskOwner,
            localMembershipCount,
            nullableEstimateCount,
            unsupportedDiagnostic);
    }

    private static string CaptureUnsupportedDiagnostic(IQueryable<PlatformSmokeTask> tasks)
    {
        try
        {
            _ = tasks
                .Where(task => IsKnownTaskTitle(task.Title))
                .ToList();
        }
        catch (QueryTranslationException exception)
        {
            return exception.Message;
        }

        throw new InvalidOperationException("Unsupported query diagnostic smoke unexpectedly succeeded.");
    }

    private static bool IsKnownTaskTitle(string value) =>
        value.StartsWith("Compile", StringComparison.Ordinal);

    private static string VerifyStrictParserProjection(
        SQLiteDatabase<PlatformSmokeDb> database,
        IQueryable<PlatformSmokeTask> tasks,
        PlatformSmokeTask row)
    {
        var query = tasks
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
