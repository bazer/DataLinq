using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using DataLinq.Interfaces;
using DataLinq.Metadata;
using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace DataLinq.Testing;

internal static class TestDatabaseLifecycle
{
    private const int ServerAdminRetryAttempts = 30;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ServerAdminLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ServerLifecycleTelemetry> ServerTelemetry = new(StringComparer.OrdinalIgnoreCase);

    public static Database<TDatabase> CreateDatabase<TDatabase>(TestConnectionDefinition connection)
        where TDatabase : class, IDatabaseModel<TDatabase>
    {
        var creator = PluginHook.DatabaseProviders.Single(x => x.Key == connection.DatabaseType).Value;
        return creator.GetDatabaseProvider<TDatabase>(connection.ConnectionString, connection.DataSourceName);
    }

    public static void EnsureServerDatabaseReady(
        DatabaseServerTarget target,
        TestConnectionDefinition connection,
        PodmanTestEnvironmentSettings settings)
    {
        try
        {
            ExecuteServerAdminCommand(target, settings, adminConnection =>
            {
                using var createDatabase = adminConnection.CreateCommand();
                createDatabase.CommandText = $"CREATE DATABASE IF NOT EXISTS {QuoteIdentifier(connection.LogicalDatabaseName)} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
                createDatabase.ExecuteNonQuery();
            });
        }
        catch (MySqlException exception)
        {
            throw new InvalidOperationException(BuildServerSetupErrorMessage(target, settings, exception), exception);
        }
    }

    public static void DropServerDatabase(
        DatabaseServerTarget target,
        TestConnectionDefinition connection,
        PodmanTestEnvironmentSettings settings)
    {
        ExecuteServerAdminCommand(target, settings, adminConnection =>
        {
            using var dropDatabase = adminConnection.CreateCommand();
            dropDatabase.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(connection.LogicalDatabaseName)};";
            dropDatabase.ExecuteNonQuery();
        });
    }

    public static void DeleteSqliteFile(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
            return;

        const int attempts = 60;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                SqliteConnection.ClearAllPools();

                if (attempt % 5 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                if (!File.Exists(builder.DataSource))
                    return;

                File.Delete(builder.DataSource);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(250);
            }
            catch (UnauthorizedAccessException) when (attempt < attempts)
            {
                Thread.Sleep(250);
            }
        }
    }

    internal static IReadOnlyList<ServerLifecycleMetrics> CaptureServerLifecycleMetrics() =>
        ServerTelemetry.Values
            .OrderBy(static telemetry => telemetry.TargetId, StringComparer.OrdinalIgnoreCase)
            .Select(static telemetry => telemetry.Capture())
            .ToArray();

    private static string QuoteIdentifier(string value) => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";

    private static void ExecuteServerAdminCommand(
        DatabaseServerTarget target,
        PodmanTestEnvironmentSettings settings,
        Action<MySqlConnection> action)
    {
        var telemetry = ServerTelemetry.GetOrAdd(
            target.Id,
            _ => new ServerLifecycleTelemetry(target, settings));
        var serverLock = ServerAdminLocks.GetOrAdd(target.Id, _ => new SemaphoreSlim(1, 1));
        var waitStarted = Stopwatch.GetTimestamp();
        serverLock.Wait();
        telemetry.RecordLockWait(Stopwatch.GetTimestamp() - waitStarted);

        try
        {
            MySqlException? lastException = null;

            for (var attempt = 1; attempt <= ServerAdminRetryAttempts; attempt++)
            {
                try
                {
                    using var adminConnection = new MySqlConnection(settings.CreateAdminConnectionString(target));
                    adminConnection.Open();
                    telemetry.RecordConnectionOpen(adminConnection);
                    var commandStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        action(adminConnection);
                    }
                    finally
                    {
                        telemetry.RecordCommand(Stopwatch.GetTimestamp() - commandStarted);
                    }
                    return;
                }
                catch (MySqlException exception) when (IsTooManyConnections(exception) && attempt < ServerAdminRetryAttempts)
                {
                    lastException = exception;
                    telemetry.RecordRetry();
                    Thread.Sleep(GetRetryDelay(attempt));
                }
            }

            if (lastException is not null)
                throw lastException;

            throw new InvalidOperationException("Server admin command failed without a captured MySqlException.");
        }
        finally
        {
            serverLock.Release();
        }
    }

    private sealed class ServerLifecycleTelemetry(
        DatabaseServerTarget target,
        PodmanTestEnvironmentSettings settings)
    {
        private readonly object statusSync = new();
        private long adminLockWaitTicks;
        private long adminCommandTicks;
        private int adminCommands;
        private int adminConnectionOpens;
        private int retries;
        private bool startCaptured;
        private long? startConnections;
        private long? startThreadsConnected;

        public string TargetId => target.Id;

        public void RecordLockWait(long ticks) => Interlocked.Add(ref adminLockWaitTicks, ticks);

        public void RecordCommand(long ticks)
        {
            Interlocked.Add(ref adminCommandTicks, ticks);
            Interlocked.Increment(ref adminCommands);
        }

        public void RecordRetry() => Interlocked.Increment(ref retries);

        public void RecordConnectionOpen(MySqlConnection connection)
        {
            Interlocked.Increment(ref adminConnectionOpens);
            lock (statusSync)
            {
                if (startCaptured)
                    return;

                try
                {
                    (startConnections, startThreadsConnected) = ReadServerStatus(connection);
                    startCaptured = true;
                }
                catch
                {
                    // Telemetry must never turn a healthy database setup into a test failure.
                }
            }
        }

        public ServerLifecycleMetrics Capture()
        {
            long? endConnections = null;
            long? endThreadsConnected = null;
            try
            {
                using var connection = new MySqlConnection(settings.CreateAdminConnectionString(target));
                connection.Open();
                Interlocked.Increment(ref adminConnectionOpens);
                (endConnections, endThreadsConnected) = ReadServerStatus(connection);
            }
            catch
            {
                // The already-recorded lifecycle metrics remain useful when final status is unavailable.
            }

            return new ServerLifecycleMetrics(
                target.Id,
                Volatile.Read(ref adminCommands),
                Volatile.Read(ref adminConnectionOpens),
                Volatile.Read(ref retries),
                Math.Round(Volatile.Read(ref adminLockWaitTicks) * 1000d / Stopwatch.Frequency, 3),
                Math.Round(Volatile.Read(ref adminCommandTicks) * 1000d / Stopwatch.Frequency, 3),
                startConnections,
                endConnections,
                startConnections.HasValue && endConnections.HasValue
                    ? endConnections.Value - startConnections.Value
                    : null,
                startThreadsConnected,
                endThreadsConnected);
        }

        private static (long? Connections, long? ThreadsConnected) ReadServerStatus(MySqlConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SHOW GLOBAL STATUS WHERE Variable_name IN ('Connections', 'Threads_connected');";
            using var reader = command.ExecuteReader();
            long? connections = null;
            long? threadsConnected = null;
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (!long.TryParse(reader.GetString(1), out var value))
                    continue;

                if (string.Equals(name, "Connections", StringComparison.OrdinalIgnoreCase))
                    connections = value;
                else if (string.Equals(name, "Threads_connected", StringComparison.OrdinalIgnoreCase))
                    threadsConnected = value;
            }

            return (connections, threadsConnected);
        }
    }

    private static bool IsTooManyConnections(MySqlException exception)
        => exception.Number == 1040
        || exception.Message.Contains("Too many connections", StringComparison.OrdinalIgnoreCase);

    private static int GetRetryDelay(int attempt)
        => Math.Min(1000, 100 + (attempt * 100));

    private static string BuildServerSetupErrorMessage(
        DatabaseServerTarget target,
        PodmanTestEnvironmentSettings settings,
        MySqlException exception)
    {
        if (exception.Message.Contains("Access denied for user 'root'", StringComparison.OrdinalIgnoreCase))
        {
            return
                $"Could not connect to '{target.Id}' with the configured admin account '{settings.AdminUser}'. " +
                $"Recreate the target with 'dotnet run --project src\\DataLinq.Testing.CLI -- reset --targets {target.Id}' so the test infrastructure CLI can provision the host admin privileges correctly. " +
                $"Current admin endpoint: {settings.Host}:{settings.GetPort(target)}.";
        }

        if (exception.Message.Contains($"Access denied for user '{settings.AdminUser}'", StringComparison.OrdinalIgnoreCase))
        {
            return
                $"Could not connect to '{target.Id}' with the configured admin account '{settings.AdminUser}'. " +
                $"Recreate the target with 'dotnet run --project src\\DataLinq.Testing.CLI -- reset --targets {target.Id}' so the test infrastructure CLI can reapply the elevated grants for that user. " +
                $"Current admin endpoint: {settings.Host}:{settings.GetPort(target)}.";
        }

        if (exception.Message.Contains("Incorrect database name", StringComparison.OrdinalIgnoreCase))
        {
            return
                $"The generated test database name was rejected by '{target.Id}'. " +
                $"This usually means the provider-specific test identifier exceeded the server's identifier rules.";
        }

        return
            $"Could not reach the '{target.Id}' test server at {settings.Host}:{settings.GetPort(target)}. " +
            $"Start the required target with 'dotnet run --project src\\DataLinq.Testing.CLI -- up --targets {target.Id}' and wait for it to become ready before running server-backed TUnit tests.";
    }
}

internal sealed record ServerLifecycleMetrics(
    string Target,
    int AdminCommands,
    int AdminConnectionOpens,
    int TooManyConnectionRetries,
    double AdminLockWaitMilliseconds,
    double AdminCommandMilliseconds,
    long? ServerConnectionsAtStart,
    long? ServerConnectionsAtEnd,
    long? ServerConnectionOpenDelta,
    long? ServerThreadsConnectedAtStart,
    long? ServerThreadsConnectedAtEnd);
