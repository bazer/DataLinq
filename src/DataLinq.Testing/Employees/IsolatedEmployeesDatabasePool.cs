using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using DataLinq.Metadata;
using DataLinq.Tests.Models.Employees;
using MySqlConnector;

namespace DataLinq.Testing;

internal static class IsolatedEmployeesDatabasePool
{
    private const int MaximumLeasesPerProfile = 4;
    private static readonly ConcurrentDictionary<string, LeasePool> Pools = new(StringComparer.Ordinal);

    static IsolatedEmployeesDatabasePool()
    {
        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            var metrics = Pools.Values.Select(static pool => pool.Cleanup()).ToArray();
            MySqlConnection.ClearAllPools();
            var serverLifecycle = TestDatabaseLifecycle.CaptureServerLifecycleMetrics();
            var reportPath = Environment.GetEnvironmentVariable(
                PodmanTestEnvironmentSettings.FixtureTelemetryReportPathEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(reportPath))
                return;

            try
            {
                var directory = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(
                    reportPath,
                    JsonSerializer.Serialize(
                        new IsolatedEmployeesFixtureTelemetryReport(
                            "v0.9.fixture-telemetry.v1",
                            DateTimeOffset.UtcNow,
                            metrics,
                            serverLifecycle),
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not write fixture telemetry report '{reportPath}': {exception.Message}");
            }
        };
    }

    public static IsolatedEmployeesDatabaseLease Rent(
        TestProviderDescriptor provider,
        string scenarioName,
        EmployeesFixtureProfile profile,
        PodmanTestEnvironmentSettings settings)
    {
        if (provider.ServerTarget is null)
            throw new ArgumentException("Only server-backed employee databases use the lease pool.", nameof(provider));

        var key = string.Join(
            '|',
            provider.Name,
            settings.Host,
            settings.GetPort(provider),
            settings.ApplicationUser,
            profile);
        var pool = Pools.GetOrAdd(
            key,
            _ => new LeasePool(provider, profile, settings, MaximumLeasesPerProfile));
        return pool.Rent(scenarioName);
    }

    private sealed class LeasePool
    {
        private readonly TestProviderDescriptor provider;
        private readonly EmployeesFixtureProfile profile;
        private readonly PodmanTestEnvironmentSettings settings;
        private readonly SemaphoreSlim gate;
        private readonly ConcurrentStack<LeaseSlot> available = new();
        private readonly ConcurrentBag<LeaseSlot> allSlots = [];
        private int nextSlot;
        private long createTicks;
        private long setupTicks;
        private long schemaTicks;
        private long seedTicks;
        private long leaseWaitTicks;
        private long resetTicks;
        private long cleanupTicks;
        private int createCount;
        private int reuseCount;
        private int resetCount;
        private int resetFailureCount;
        private int cleanupStarted;

        public LeasePool(
            TestProviderDescriptor provider,
            EmployeesFixtureProfile profile,
            PodmanTestEnvironmentSettings settings,
            int capacity)
        {
            this.provider = provider;
            this.profile = profile;
            this.settings = settings;
            gate = new SemaphoreSlim(capacity, capacity);
        }

        public IsolatedEmployeesDatabaseLease Rent(string scenarioName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scenarioName);
            var waitStarted = Stopwatch.GetTimestamp();
            gate.Wait();
            Interlocked.Add(ref leaseWaitTicks, Stopwatch.GetTimestamp() - waitStarted);

            LeaseSlot? slot = null;
            try
            {
                if (available.TryPop(out slot))
                {
                    Interlocked.Increment(ref reuseCount);
                }
                else
                {
                    slot = CreateSlot();
                    allSlots.Add(slot);
                }

                slot.Owner = scenarioName;
                var capturedSlot = slot;
                return new IsolatedEmployeesDatabaseLease(
                    capturedSlot.Connection,
                    () => Return(capturedSlot, scenarioName));
            }
            catch
            {
                if (slot is not null)
                    slot.Owner = null;
                gate.Release();
                throw;
            }
        }

        public IsolatedEmployeesFixtureMetrics Cleanup()
        {
            if (Interlocked.Exchange(ref cleanupStarted, 1) != 0)
                return CreateMetrics();

            foreach (var slot in allSlots)
            {
                var cleanupStartedAt = Stopwatch.GetTimestamp();
                try
                {
                    TestDatabaseLifecycle.DropServerDatabase(
                        provider.ServerTarget!,
                        slot.Connection,
                        settings);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"Could not clean leased test database '{slot.Connection.LogicalDatabaseName}' " +
                        $"(last owner: '{slot.Owner ?? "none"}'): {exception.Message}");
                }
                finally
                {
                    Interlocked.Add(ref cleanupTicks, Stopwatch.GetTimestamp() - cleanupStartedAt);
                }
            }

            return CreateMetrics();
        }

        private IsolatedEmployeesFixtureMetrics CreateMetrics() =>
            new(
                provider.Name,
                profile.ToString(),
                Volatile.Read(ref createCount),
                Volatile.Read(ref reuseCount),
                Volatile.Read(ref resetCount),
                Volatile.Read(ref resetFailureCount),
                Math.Round(ToMilliseconds(Volatile.Read(ref createTicks)), 3),
                Math.Round(ToMilliseconds(Volatile.Read(ref setupTicks)), 3),
                Math.Round(ToMilliseconds(Volatile.Read(ref schemaTicks)), 3),
                Math.Round(ToMilliseconds(Volatile.Read(ref seedTicks)), 3),
                Math.Round(ToMilliseconds(Volatile.Read(ref leaseWaitTicks)), 3),
                Math.Round(ToMilliseconds(Volatile.Read(ref resetTicks)), 3),
                Math.Round(ToMilliseconds(Volatile.Read(ref cleanupTicks)), 3));

        private LeaseSlot CreateSlot()
        {
            var started = Stopwatch.GetTimestamp();
            var slotNumber = Interlocked.Increment(ref nextSlot);
            var logicalName = $"leased_employees_{provider.Name}_{profile}_{Environment.ProcessId}_{slotNumber}";
            var connection = settings.CreateConnection(provider, logicalName);

            try
            {
                var phaseStarted = Stopwatch.GetTimestamp();
                TestDatabaseLifecycle.EnsureServerDatabaseReady(provider.ServerTarget!, connection, settings);
                Interlocked.Add(ref setupTicks, Stopwatch.GetTimestamp() - phaseStarted);
                using var database = TestDatabaseLifecycle.CreateDatabase<EmployeesDb>(connection);
                phaseStarted = Stopwatch.GetTimestamp();
                EmployeesTestDatabase.EnsureSchema(database, connection);
                Interlocked.Add(ref schemaTicks, Stopwatch.GetTimestamp() - phaseStarted);
                phaseStarted = Stopwatch.GetTimestamp();
                EmployeesTestDatabase.EnsureSeedData(database, profile);
                Interlocked.Add(ref seedTicks, Stopwatch.GetTimestamp() - phaseStarted);
                var tableNames = database.Provider.Metadata.TableModels
                    .Where(static model => model.Table.Type == TableType.Table)
                    .Select(static model => model.Table.DbName)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .ToArray();
                var fingerprint = CaptureSchemaFingerprint(connection);
                Interlocked.Increment(ref createCount);
                return new LeaseSlot(connection, tableNames, fingerprint);
            }
            catch
            {
                try
                {
                    TestDatabaseLifecycle.DropServerDatabase(provider.ServerTarget!, connection, settings);
                }
                catch
                {
                    // The original setup failure is more useful; the deterministic database name is recoverable.
                }

                throw;
            }
            finally
            {
                Interlocked.Add(ref createTicks, Stopwatch.GetTimestamp() - started);
            }
        }

        private void Return(LeaseSlot slot, string scenarioName)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                Reset(slot);
                slot.Owner = null;
                available.Push(slot);
                Interlocked.Increment(ref resetCount);
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref resetFailureCount);
                try
                {
                    TestDatabaseLifecycle.DropServerDatabase(
                        provider.ServerTarget!,
                        slot.Connection,
                        settings);
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(
                        $"Failed to reset leased database '{slot.Connection.LogicalDatabaseName}' owned by test " +
                        $"'{scenarioName}', and emergency cleanup also failed. The database name is safe to drop manually.",
                        exception,
                        cleanupException);
                }

                throw new InvalidOperationException(
                    $"Failed to reset leased database '{slot.Connection.LogicalDatabaseName}' owned by test " +
                    $"'{scenarioName}'. The poisoned lease was dropped and will be replaced.",
                    exception);
            }
            finally
            {
                Interlocked.Add(ref resetTicks, Stopwatch.GetTimestamp() - started);
                gate.Release();
            }
        }

        private void Reset(LeaseSlot slot)
        {
            var currentFingerprint = CaptureSchemaFingerprint(slot.Connection);
            if (!string.Equals(currentFingerprint, slot.SchemaFingerprint, StringComparison.Ordinal))
            {
                Rebuild(slot);
                return;
            }

            using (var connection = new MySqlConnection(slot.Connection.ConnectionString))
            {
                connection.Open();
                var resetSql = new StringBuilder("SET FOREIGN_KEY_CHECKS = 0;\n");
                foreach (var tableName in slot.TableNames)
                {
                    resetSql
                        .Append("DELETE FROM ")
                        .Append(QuoteIdentifier(slot.Connection.LogicalDatabaseName))
                        .Append('.')
                        .Append(QuoteIdentifier(tableName))
                        .Append(";\n");
                }

                resetSql
                    .Append("ALTER TABLE ")
                    .Append(QuoteIdentifier(slot.Connection.LogicalDatabaseName))
                    .Append(".`employees` AUTO_INCREMENT = 1;\n")
                    .Append("SET FOREIGN_KEY_CHECKS = 1;");
                ExecuteNonQuery(connection, resetSql.ToString());
            }

            using var database = TestDatabaseLifecycle.CreateDatabase<EmployeesDb>(slot.Connection);
            var seedStarted = Stopwatch.GetTimestamp();
            EmployeesTestDatabase.EnsureSeedData(database, profile);
            Interlocked.Add(ref seedTicks, Stopwatch.GetTimestamp() - seedStarted);
        }

        private void Rebuild(LeaseSlot slot)
        {
            var phaseStarted = Stopwatch.GetTimestamp();
            TestDatabaseLifecycle.DropServerDatabase(provider.ServerTarget!, slot.Connection, settings);
            TestDatabaseLifecycle.EnsureServerDatabaseReady(provider.ServerTarget!, slot.Connection, settings);
            Interlocked.Add(ref setupTicks, Stopwatch.GetTimestamp() - phaseStarted);
            using var database = TestDatabaseLifecycle.CreateDatabase<EmployeesDb>(slot.Connection);
            phaseStarted = Stopwatch.GetTimestamp();
            EmployeesTestDatabase.EnsureSchema(database, slot.Connection);
            Interlocked.Add(ref schemaTicks, Stopwatch.GetTimestamp() - phaseStarted);
            phaseStarted = Stopwatch.GetTimestamp();
            EmployeesTestDatabase.EnsureSeedData(database, profile);
            Interlocked.Add(ref seedTicks, Stopwatch.GetTimestamp() - phaseStarted);
            slot.SchemaFingerprint = CaptureSchemaFingerprint(slot.Connection);
        }

        private static string CaptureSchemaFingerprint(TestConnectionDefinition definition)
        {
            var schema = new StringBuilder();
            using var connection = new MySqlConnection(definition.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT 'table', TABLE_NAME, TABLE_TYPE, COALESCE(ENGINE, ''),
                       COALESCE(TABLE_COLLATION, ''), COALESCE(CREATE_OPTIONS, ''), '', ''
                  FROM information_schema.TABLES
                 WHERE TABLE_SCHEMA = @schema
                UNION ALL
                SELECT 'column', TABLE_NAME, COLUMN_NAME, CAST(ORDINAL_POSITION AS CHAR),
                       COLUMN_TYPE, IS_NULLABLE, COALESCE(COLUMN_DEFAULT, ''), EXTRA
                  FROM information_schema.COLUMNS
                 WHERE TABLE_SCHEMA = @schema
                UNION ALL
                SELECT 'index', TABLE_NAME, INDEX_NAME, CAST(SEQ_IN_INDEX AS CHAR),
                       COLUMN_NAME, CAST(NON_UNIQUE AS CHAR), INDEX_TYPE, COALESCE(CAST(SUB_PART AS CHAR), '')
                  FROM information_schema.STATISTICS
                 WHERE TABLE_SCHEMA = @schema
                UNION ALL
                SELECT 'foreign-key', TABLE_NAME, CONSTRAINT_NAME, REFERENCED_TABLE_NAME,
                       UPDATE_RULE, DELETE_RULE, UNIQUE_CONSTRAINT_NAME, ''
                  FROM information_schema.REFERENTIAL_CONSTRAINTS
                 WHERE CONSTRAINT_SCHEMA = @schema
                UNION ALL
                SELECT 'trigger', TRIGGER_NAME, EVENT_OBJECT_TABLE, EVENT_MANIPULATION,
                       ACTION_TIMING, ACTION_ORIENTATION, COALESCE(ACTION_CONDITION, ''), ACTION_STATEMENT
                  FROM information_schema.TRIGGERS
                 WHERE TRIGGER_SCHEMA = @schema
                UNION ALL
                SELECT 'view', TABLE_NAME, COALESCE(VIEW_DEFINITION, ''), CHECK_OPTION,
                       IS_UPDATABLE, SECURITY_TYPE, CHARACTER_SET_CLIENT, COLLATION_CONNECTION
                  FROM information_schema.VIEWS
                 WHERE TABLE_SCHEMA = @schema
                 ORDER BY 1, 2, 3, 4, 5, 6, 7, 8;
                """;
            command.Parameters.AddWithValue("@schema", definition.LogicalDatabaseName);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                for (var index = 0; index < reader.FieldCount; index++)
                    schema.Append(reader.IsDBNull(index) ? "<null>" : reader.GetValue(index)).Append('|');
                schema.Append('\n');
            }

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema.ToString())));
        }

        private static void ExecuteNonQuery(MySqlConnection connection, string commandText)
        {
            using var command = connection.CreateCommand();
            command.CommandTimeout = 30;
            command.CommandText = commandText;
            command.ExecuteNonQuery();
        }

        private static string QuoteIdentifier(string value) =>
            $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";

        private static double ToMilliseconds(long stopwatchTicks) =>
            stopwatchTicks * 1000d / Stopwatch.Frequency;
    }

    private sealed class LeaseSlot(
        TestConnectionDefinition connection,
        IReadOnlyList<string> tableNames,
        string schemaFingerprint)
    {
        public TestConnectionDefinition Connection { get; } = connection;
        public IReadOnlyList<string> TableNames { get; } = tableNames;
        public string SchemaFingerprint { get; set; } = schemaFingerprint;
        public string? Owner { get; set; }
    }
}

internal sealed record IsolatedEmployeesDatabaseLease(
    TestConnectionDefinition Connection,
    Action Release);

internal sealed record IsolatedEmployeesFixtureTelemetryReport(
    string SchemaVersion,
    DateTimeOffset CompletedAtUtc,
    IReadOnlyList<IsolatedEmployeesFixtureMetrics> Profiles,
    IReadOnlyList<ServerLifecycleMetrics> ServerLifecycle);

internal sealed record IsolatedEmployeesFixtureMetrics(
    string Target,
    string Profile,
    int Creates,
    int Reuses,
    int Resets,
    int ResetFailures,
    double CreateMilliseconds,
    double SetupMilliseconds,
    double SchemaMilliseconds,
    double SeedMilliseconds,
    double LeaseWaitMilliseconds,
    double ResetMilliseconds,
    double CleanupMilliseconds);
