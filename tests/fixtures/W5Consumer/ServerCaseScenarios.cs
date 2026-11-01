using System.Text.Json;
using DataLinq;
using DataLinq.MariaDB;
using DataLinq.MySql;
using DataLinq.Validation;
using MySqlConnector;

namespace W5Consumer;

internal static class ServerCaseScenarios
{
    internal static async Task Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Supply --server-case MySQL|MariaDB expected-mode report-path.");
        var family = Enum.Parse<DatabaseType>(args[1]);
        if (family is not DatabaseType.MySQL and not DatabaseType.MariaDB) throw new ArgumentException("A server family is required.");
        var requested = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
        var connectionString = Environment.GetEnvironmentVariable("DATALINQ_W5_SERVER_CONNECTION")
            ?? throw new InvalidOperationException("Supply the dedicated fixture connection through DATALINQ_W5_SERVER_CONNECTION.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var connection = new MySqlConnection(connectionString);
        while (true)
        {
            try { await connection.OpenAsync(timeout.Token); break; }
            catch (MySqlException) when (!timeout.IsCancellationRequested) { await Task.Delay(500, timeout.Token); }
        }
        async Task<object?> Scalar(string sql)
        { await using var command = new MySqlCommand(sql, connection); return await command.ExecuteScalarAsync(timeout.Token); }
        async Task Execute(string sql)
        { await using var command = new MySqlCommand(sql, connection); await command.ExecuteNonQueryAsync(timeout.Token); }
        var observed = Convert.ToInt32(await Scalar("SELECT @@lower_case_table_names"));
        var version = Convert.ToString(await Scalar("SELECT VERSION()"));
        if (requested != observed)
        {
            if (requested != 2) throw new InvalidOperationException($"Requested mode {requested}, observed {observed}.");
            File.WriteAllText(args[3], JsonSerializer.Serialize(new { Family = family.ToString(), Requested = requested, Observed = observed,
                ServerVersion = version, NativeModeAvailable = false, Passed = false,
                Disposition = "The dedicated server did not accept mode 2 on this filesystem. This is not native mode-2 evidence." }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Mode 2 unavailable: {family} {version} reports {observed}; no mode-2 pass claimed.");
            return;
        }
        var schema = "W5_Runtime_" + Guid.NewGuid().ToString("N");
        await Execute($"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
        try
        {
            await connection.ChangeDatabaseAsync(schema, timeout.Token);
            // The connection's default database and effective requested schema
            // intentionally differ. Both identities remain application-owned.
            using Database<CaseDb> database = family == DatabaseType.MySQL
                ? new MySqlDatabase<CaseDb>(connectionString, schema)
                : new MariaDBDatabase<CaseDb>(connectionString, schema);
            await Execute(database.Provider.GetCreateSql().Text);
            var sync = database.ValidateSchema();
            var asynchronous = await database.ValidateSchemaAsync(cancellationToken: timeout.Token);
            Fixture.Check(!sync.HasFailures && !asynchronous.HasFailures && sync.Differences.Count == 0 && sync.Issues.Count == 0,
                "Compiled case/default/check/index/relation roundtrip failed: " + string.Join("; ", sync.Differences.Select(item => item.Message)));
            Fixture.Check(sync.ModelTableCount == 3 && sync.DatabaseTableCount == 3 && sync.DatabaseName == schema, "Effective schema/counts changed.");
            Fixture.Expect<ArgumentException>(() => database.ValidateSchema(options => options.Include = ["orders"]));
            var scoped = await database.ValidateSchemaAsync(options => options.Include = ["Child"], timeout.Token);
            Fixture.Check(!scoped.HasFailures && scoped.ModelTableCount == 1 && scoped.DatabaseTableCount == 1, "Case-insensitive endpoint scope failed.");
            var count = Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'orders'"));
            Fixture.Check(count == 1, "The native server did not expose the case-insensitive table identity.");
            await Execute("ALTER TABLE `ÅParent` MODIFY COLUMN `Value` varchar(40) NOT NULL DEFAULT 'a' COMMENT 'a'");
            var changed = DataLinqSchemaValidator.Validate(database.Provider);
            Fixture.Check(changed.Differences.Any(item => item.Kind == SchemaDifferenceKind.ColumnDefaultMismatch) &&
                changed.Differences.Any(item => item.Kind == SchemaDifferenceKind.ColumnCommentMismatch), "Case rules erased literal default/comment drift.");
            File.WriteAllText(args[3], JsonSerializer.Serialize(new { Family = family.ToString(), Requested = requested, Observed = observed,
                ServerVersion = version, NativeModeAvailable = true, Passed = true, ModelObjects = sync.ModelTableCount,
                ScopedObjects = scoped.ModelTableCount, DriftKinds = changed.Differences.Select(item => item.Kind.ToString()).ToArray(),
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Native {family} mode {observed}: compiled mixed-case/Unicode identities, exact Include, scoped relation and literal drift passed.");
        }
        finally { await Execute($"DROP DATABASE `{schema}`"); }
    }
}
