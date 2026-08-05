using System.Text.Json;
using DataLinq;
using DataLinq.Core.Factories;
using DataLinq.Memory;
using DataLinq.Metadata;
using DataLinq.MySql;
using DataLinq.PackageConsumer;
using DataLinq.SQLite;

var memory = CaptureMemoryExecution();
var sqlite = CaptureSQLiteExecution();
var mySqlCompilationProbe = CaptureMySqlCompilationProbe();
var passed = memory.Passed && sqlite.Passed && mySqlCompilationProbe;

var result = new PackageConsumerExecutionResult(
    SchemaVersion: "v0.9.package-consumer-execution.v1",
    TargetFramework: GetTargetFramework(),
    Memory: memory,
    Sqlite: sqlite,
    MySqlCompilationProbe: mySqlCompilationProbe,
    Passed: passed);

Console.WriteLine(JsonSerializer.Serialize(
    result,
    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

Environment.ExitCode = passed ? 0 : 1;

static MemoryExecutionResult CaptureMemoryExecution()
{
    try
    {
        var database = new MemoryDatabase<PackageConsumerDatabase>();
        database.Seed<PackageConsumerRow>(CreateRows());

        var found = database.Find<PackageConsumerRow>(17);
        var missing = database.Find<PackageConsumerRow>(999) is null;
        var queryIds = database.Query().Rows
            .Where(static row => row.GroupId == 7)
            .OrderBy(static row => row.Id)
            .Select(static row => row.Id)
            .ToArray();

        var passed =
            found?.Id == 17 &&
            missing &&
            queryIds.SequenceEqual([-5, 17]);

        return new MemoryExecutionResult(passed, found?.Id, missing, queryIds);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"Memory package-consumer execution failed: {exception.GetType().FullName}: {exception.Message}");
        return new MemoryExecutionResult(false, null, false, []);
    }
}

static SQLiteExecutionResult CaptureSQLiteExecution()
{
    try
    {
        var databaseName = $"package_consumer_{Guid.NewGuid():N}";
        var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";

        SQLiteProvider.RegisterProvider();

        using var database = new SQLiteDatabase<PackageConsumerDatabase>(
            connectionString,
            databaseName);

        var createResult = PluginHook.CreateDatabaseFromMetadata(
            DatabaseType.SQLite,
            database.Provider.Metadata,
            databaseName,
            database.Provider.ConnectionString,
            foreignKeyRestrict: true);

        if (createResult.HasFailed)
            throw new InvalidOperationException(createResult.Failure.ToString());

        foreach (var row in CreateRows())
            database.Insert(row);

        var rowIds = database.Query().Rows
            .OrderBy(static row => row.Id)
            .Select(static row => row.Id)
            .ToArray();

        return new SQLiteExecutionResult(
            rowIds.SequenceEqual([-5, 17, 42]),
            rowIds);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"SQLite package-consumer execution failed: {exception.GetType().FullName}: {exception.Message}");
        return new SQLiteExecutionResult(false, []);
    }
}

static bool CaptureMySqlCompilationProbe()
{
    try
    {
        return typeof(MySqlDatabase<PackageConsumerDatabase>)
            .GetConstructor([typeof(string)]) is not null;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(
            $"MySQL package-consumer compilation probe failed: {exception.GetType().FullName}: {exception.Message}");
        return false;
    }
}

static MutablePackageConsumerRow[] CreateRows() =>
[
    new MutablePackageConsumerRow
    {
        Id = 17,
        GroupId = 7,
        Name = "seventeen"
    },
    new MutablePackageConsumerRow
    {
        Id = -5,
        GroupId = 7,
        Name = "minus-five"
    },
    new MutablePackageConsumerRow
    {
        Id = 42,
        GroupId = 3,
        Name = "forty-two"
    }
];

static string GetTargetFramework()
{
#if NET8_0
    return "net8.0";
#elif NET9_0
    return "net9.0";
#elif NET10_0
    return "net10.0";
#else
#error The package-consumer fixture must target a recognized supported framework.
#endif
}

internal sealed record PackageConsumerExecutionResult(
    string SchemaVersion,
    string TargetFramework,
    MemoryExecutionResult Memory,
    SQLiteExecutionResult Sqlite,
    bool MySqlCompilationProbe,
    bool Passed);

internal sealed record MemoryExecutionResult(
    bool Passed,
    int? FoundId,
    bool Missing,
    int[] QueryIds);

internal sealed record SQLiteExecutionResult(
    bool Passed,
    int[] RowIds);
