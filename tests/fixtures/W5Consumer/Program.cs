using System.Runtime.InteropServices;
using DataLinq;
using DataLinq.Exceptions;
using DataLinq.SQLite;
using DataLinq.Validation;
using SessionConsumer;
using W5Consumer;

if (args.Length > 0 && args[0] == "--server-case")
{
    await ServerCaseScenarios.Run(args);
    return;
}

if (args.Length > 0 && args[0] == "--isolated")
{
    Fixture.Check(Path.GetFullPath(Environment.CurrentDirectory) == Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),
        "Isolated execution must run inside its published directory.");
    Fixture.Check(!Directory.EnumerateFiles(".", "*.cs", SearchOption.AllDirectories).Any(), "Published directory contains model source.");
    Fixture.Check(!Directory.EnumerateFiles(".", "DataLinq.Tools*", SearchOption.AllDirectories).Any(), "Published directory contains Tools.");
}
SQLiteProvider.RegisterProvider();
using (var anonymous = new SQLiteDatabase<TestDb>("Data Source=:memory:"))
using (var independent = new SQLiteDatabase<TestDb>("Data Source=:memory:"))
{
    Fixture.CreateSchema(anonymous);
    var effectiveConnection = anonymous.Provider.ConnectionString;
    Fixture.Check(!anonymous.ValidateSchema().HasFailures && !(await anonymous.ValidateSchemaAsync()).HasFailures,
        "Anonymous memory validation lost the provider's effective identity.");
    Fixture.Check(independent.ValidateSchema().DatabaseTableCount == 0, "Independent anonymous databases were merged.");
    Fixture.Check(anonymous.Provider.ConnectionString == effectiveConnection && anonymous.Provider.TableExists("rows"),
        "Validation changed anonymous identity or disposed its keeper.");
}
using (var database = Fixture.Database())
{
    Fixture.CreateSchema(database); // Application-owned migration, before validation.
    Bindings.Direct(database, CancellationToken.None);
    Fixture.Check(!database.ValidateSchema().HasFailures, "Compiled/native schema failed.");
    Fixture.Check(!(await DataLinqSchemaValidator.ValidateAsync(database.Provider)).HasFailures, "Async compiled/native schema failed.");
    await database.EnsureSchemaValidAsync();
    var configured = 0;
    var include = new[] { "ROWS" };
    var pending = database.ValidateSchemaAsync(options => { configured++; options.Include = include; options.CommandTimeout = TimeSpan.FromMilliseconds(500); });
    Fixture.Check(configured == 1, "Configuration was not synchronous and once-only.");
    include[0] = "absent";
    var selected = await pending;
    Fixture.Check(selected.ModelTableCount == 1 && selected.DatabaseTableCount == 1 && !selected.HasFailures, "Captured Include changed.");
    Fixture.Expect<ArgumentException>(() => database.ValidateSchema(options => options.Include = ["absent"]));
    await Fixture.ExpectAsync<OperationCanceledException>(() => database.ValidateSchemaAsync(cancellationToken: new(true)));
    database.Provider.DatabaseAccess.ExecuteNonQuery("CREATE TABLE unrelated(id INTEGER PRIMARY KEY)");
    Fixture.Check(database.ValidateSchema().DatabaseTableCount == 2, "Default scope did not observe the new table.");
    Fixture.Check(database.ValidateSchema(options => options.Include = ["rows"]).DatabaseTableCount == 1, "Include did not scope both sides.");
    database.Provider.DatabaseAccess.ExecuteNonQuery("CREATE INDEX expression_index ON rows(length(value))");
    var limited = database.ValidateSchema();
    Fixture.Check(limited.HasFailures && limited.Issues.Count > 0, "Typed metadata issue did not fail default policy.");
    var allowed = database.ValidateSchema(options => options.TreatValidationIssuesAsFailures = false);
    Fixture.Check(allowed.Issues.Count == limited.Issues.Count && !allowed.HasFailures, "Issue policy discarded evidence or changed unrelated severity.");
    database.Provider.DatabaseAccess.ExecuteNonQuery("DROP INDEX expression_index; DROP TABLE rows; DROP TABLE unrelated");
    var drift = database.ValidateSchema();
    Fixture.Check(drift.HasFailures && drift.DatabaseTableCount == 0, "Readable empty schema was not a completed failure.");
    var failure = Fixture.Expect<DataLinqSchemaValidationException>(() => database.EnsureSchemaValid());
    Fixture.Check(failure.Result.HasFailures && failure.Result.Differences.Count > 0, "Result-based exception lost comparison evidence.");
    await Fixture.ExpectAsync<DataLinqSchemaValidationException>(() => database.EnsureSchemaValidAsync());
    Fixture.Check(!selected.HasFailures, "Earlier result changed after database mutation.");
}
// This old implementation's metadata, connection and I/O members all throw.
// Unsupported capability must be rejected before attempting any of them.
Fixture.Expect<NotSupportedException>(() => DataLinqSchemaValidator.Validate(new LegacyProvider()));
Fixture.Expect<NotSupportedException>(() => { _ = DataLinqSchemaValidator.ValidateAsync(new LegacyProvider()); });
var missingPath = Path.Combine(Path.GetTempPath(), $"w5_missing_{Guid.NewGuid():N}.sqlite");
using (var missing = new SQLiteDatabase<TestDb>($"Data Source={missingPath}", "missing"))
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    File.Delete(missingPath);
    Fixture.Expect<Microsoft.Data.Sqlite.SqliteException>(() => missing.ValidateSchema());
    await Fixture.ExpectAsync<Microsoft.Data.Sqlite.SqliteException>(() => missing.ValidateSchemaAsync());
    Fixture.Check(!File.Exists(missingPath), "Validation created a missing database.");
}
Bindings.Registration();
await HostScenarios.Run();
#if NET10_0
await HttpScenarios.Run();
#endif
if (args.Length > 0 && args[0] != "--isolated") ContractManifest.Write(args[0]);
Console.WriteLine("W5 runtime and host consumer passed on " + RuntimeInformation.FrameworkDescription);

namespace W5Consumer
{
    internal static class Fixture
    {
        internal static SQLiteDatabase<TestDb> Database() => new($"Data Source=w5_{Guid.NewGuid():N};Mode=Memory;Cache=Shared", "runtime_override");
        internal static void CreateSchema(SQLiteDatabase<TestDb> database) => database.Provider.DatabaseAccess.ExecuteNonQuery(database.Provider.GetCreateSql().Text);
        internal static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        internal static T Expect<T>(Action action) where T : Exception
        { try { action(); } catch (T failure) { return failure; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
        internal static async Task<T> ExpectAsync<T>(Func<Task> action) where T : Exception
        { try { await action(); } catch (T failure) { return failure; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
    }
}
