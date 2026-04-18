using System;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DataLinq.Interfaces;
using DataLinq.Mutation;

namespace DataLinq.Diagnostics;

internal readonly record struct DataLinqTelemetryContext(
    string ProviderInstanceId,
    string ProviderTypeName,
    string DatabaseName,
    DatabaseType DatabaseType)
{
    public static DataLinqTelemetryContext FromProvider(IDatabaseProvider? databaseProvider)
        => databaseProvider is null
            ? new DataLinqTelemetryContext(
                ProviderInstanceId: string.Empty,
                ProviderTypeName: "Unknown",
                DatabaseName: string.Empty,
                DatabaseType: DatabaseType.Unknown)
            : new DataLinqTelemetryContext(
                ProviderInstanceId: databaseProvider.TelemetryInstanceId,
                ProviderTypeName: databaseProvider.GetType().Name,
                DatabaseName: databaseProvider.DatabaseName,
                DatabaseType: databaseProvider.DatabaseType);
}

internal static class DataLinqTelemetry
{
    private const string InstrumentationName = "DataLinq";
    private static readonly string? InstrumentationVersion = typeof(DatabaseProvider).Assembly.GetName().Version?.ToString();
    private static readonly ConcurrentDictionary<string, CacheGaugeRegistration> CacheGaugeRegistrations = new(StringComparer.Ordinal);

    private static readonly Meter Meter = new(InstrumentationName, InstrumentationVersion);
    private static readonly ActivitySource ActivitySource = new(InstrumentationName, InstrumentationVersion);

    private static readonly Counter<long> CommandCounter = Meter.CreateCounter<long>(
        "datalinq.db.commands",
        unit: "{command}",
        description: "Number of database commands executed by DataLinq.");
    private static readonly Counter<long> QueryCounter = Meter.CreateCounter<long>(
        "datalinq.queries",
        unit: "{query}",
        description: "Number of DataLinq queries executed.");
    private static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>(
        "datalinq.query.duration",
        unit: "ms",
        description: "DataLinq end-to-end query execution duration in milliseconds.");
    private static readonly Histogram<double> CommandDuration = Meter.CreateHistogram<double>(
        "datalinq.db.command.duration",
        unit: "ms",
        description: "Database command execution duration in milliseconds.");
    private static readonly Counter<long> TransactionStartCounter = Meter.CreateCounter<long>(
        "datalinq.db.transactions.started",
        unit: "{transaction}",
        description: "Number of database transactions started by DataLinq.");
    private static readonly Counter<long> TransactionCompleteCounter = Meter.CreateCounter<long>(
        "datalinq.db.transactions.completed",
        unit: "{transaction}",
        description: "Number of database transactions completed by DataLinq.");
    private static readonly Histogram<double> TransactionDuration = Meter.CreateHistogram<double>(
        "datalinq.db.transaction.duration",
        unit: "ms",
        description: "Database transaction duration in milliseconds.");
    private static readonly Counter<long> MutationCounter = Meter.CreateCounter<long>(
        "datalinq.db.mutations",
        unit: "{mutation}",
        description: "Number of DataLinq mutations executed.");
    private static readonly Counter<long> MutationAffectedRowsCounter = Meter.CreateCounter<long>(
        "datalinq.db.mutation.affected_rows",
        unit: "{row}",
        description: "Number of rows reported as affected by DataLinq mutations.");
    private static readonly Histogram<double> MutationDuration = Meter.CreateHistogram<double>(
        "datalinq.db.mutation.duration",
        unit: "ms",
        description: "DataLinq mutation execution duration in milliseconds.");
    private static readonly Counter<long> CacheRowsRemovedCounter = Meter.CreateCounter<long>(
        "datalinq.cache.rows.removed",
        unit: "{row}",
        description: "Number of rows removed from DataLinq caches.");
    private static readonly Counter<long> CacheMaintenanceCounter = Meter.CreateCounter<long>(
        "datalinq.cache.maintenance.operations",
        unit: "{operation}",
        description: "Number of DataLinq cache maintenance operations.");
    private static readonly Histogram<double> CacheMaintenanceDuration = Meter.CreateHistogram<double>(
        "datalinq.cache.maintenance.duration",
        unit: "ms",
        description: "Duration of DataLinq cache maintenance operations in milliseconds.");
    private static readonly ObservableGauge<long> CacheRowsGauge = Meter.CreateObservableGauge<long>(
        "datalinq.cache.rows",
        ObserveCacheRows,
        unit: "{row}",
        description: "Current number of rows stored in DataLinq row caches.");
    private static readonly ObservableGauge<long> CacheTransactionRowsGauge = Meter.CreateObservableGauge<long>(
        "datalinq.cache.transaction.rows",
        ObserveCacheTransactionRows,
        unit: "{row}",
        description: "Current number of rows stored in DataLinq transaction-local caches.");
    private static readonly ObservableGauge<long> CacheBytesGauge = Meter.CreateObservableGauge<long>(
        "datalinq.cache.bytes",
        ObserveCacheBytes,
        unit: "By",
        description: "Current estimated size of DataLinq row caches in bytes.");
    private static readonly ObservableGauge<long> CacheIndexEntriesGauge = Meter.CreateObservableGauge<long>(
        "datalinq.cache.index.entries",
        ObserveCacheIndexEntries,
        unit: "{entry}",
        description: "Current number of entries stored in DataLinq index caches.");

    internal static string GetCommandOperation(IDbCommand command)
    {
        var commandText = command.CommandText;
        if (string.IsNullOrWhiteSpace(commandText))
            return "unknown";

        var span = commandText.AsSpan().TrimStart();
        var tokenLength = 0;

        while (tokenLength < span.Length)
        {
            var ch = span[tokenLength];
            if (!char.IsLetter(ch) && ch != '_')
                break;

            tokenLength++;
        }

        if (tokenLength == 0)
            return "unknown";

        return span[..tokenLength].ToString().ToLowerInvariant();
    }

    internal static string GetTransactionOutcome(DatabaseTransactionStatus status)
        => status switch
        {
            DatabaseTransactionStatus.Committed => "commit",
            DatabaseTransactionStatus.RolledBack => "rollback",
            DatabaseTransactionStatus.Open => "open",
            DatabaseTransactionStatus.Closed => "closed",
            _ => "unknown"
        };

    internal static Activity? StartCommandActivity(
        DataLinqTelemetryContext context,
        string commandKind,
        string operation,
        bool transactional,
        TransactionType? transactionType)
    {
        var activity = ActivitySource.StartActivity("datalinq.db.command", ActivityKind.Client);
        if (activity is null)
            return null;

        ApplyCommonTags(activity, context);
        activity.SetTag("db.operation.name", operation);
        activity.SetTag("datalinq.command.kind", commandKind);
        activity.SetTag("datalinq.transactional", transactional);

        if (transactionType.HasValue)
            activity.SetTag("datalinq.transaction.type", GetTransactionTypeName(transactionType.Value));

        return activity;
    }

    internal static Activity? StartQueryActivity(
        DataLinqTelemetryContext context,
        string tableName,
        string queryKind,
        bool transactional)
    {
        var activity = ActivitySource.StartActivity("datalinq.query", ActivityKind.Internal);
        if (activity is null)
            return null;

        ApplyCommonTags(activity, context);
        activity.SetTag("datalinq.table", tableName);
        activity.SetTag("datalinq.query.kind", queryKind);
        activity.SetTag("datalinq.transactional", transactional);
        return activity;
    }

    internal static void RecordQueryExecution(
        DataLinqTelemetryContext context,
        string tableName,
        string queryKind,
        bool transactional,
        bool succeeded,
        TimeSpan duration)
    {
        var tags = CreateTableTags(context, tableName);
        tags.Add("datalinq.query.kind", queryKind);
        tags.Add("datalinq.transactional", transactional);
        tags.Add("datalinq.outcome", succeeded ? "success" : "failure");

        QueryCounter.Add(1, tags);
        QueryDuration.Record(duration.TotalMilliseconds, tags);
    }

    internal static void RecordCommand(
        DataLinqTelemetryContext context,
        string commandKind,
        string operation,
        bool transactional,
        TransactionType? transactionType,
        bool succeeded,
        TimeSpan duration)
    {
        var tags = CreateCommonTags(context);
        tags.Add("db.operation.name", operation);
        tags.Add("datalinq.command.kind", commandKind);
        tags.Add("datalinq.transactional", transactional);
        tags.Add("datalinq.outcome", succeeded ? "success" : "failure");

        if (transactionType.HasValue)
            tags.Add("datalinq.transaction.type", GetTransactionTypeName(transactionType.Value));

        CommandCounter.Add(1, tags);
        CommandDuration.Record(duration.TotalMilliseconds, tags);
        DataLinqMetrics.RecordCommandExecution(context, commandKind, succeeded, duration);
    }

    internal static Activity? StartTransactionActivity(DataLinqTelemetryContext context, TransactionType transactionType)
    {
        var activity = ActivitySource.StartActivity("datalinq.db.transaction", ActivityKind.Client);
        if (activity is null)
            return null;

        ApplyCommonTags(activity, context);
        activity.SetTag("datalinq.transaction.type", GetTransactionTypeName(transactionType));
        return activity;
    }

    internal static void RecordTransactionStarted(DataLinqTelemetryContext context)
    {
        var tags = CreateCommonTags(context);
        TransactionStartCounter.Add(1, tags);
        DataLinqMetrics.RecordTransactionStarted(context);
    }

    internal static void RecordTransactionCompleted(
        DataLinqTelemetryContext context,
        TransactionType transactionType,
        DatabaseTransactionStatus outcome,
        bool succeeded,
        TimeSpan duration)
    {
        var tags = CreateCommonTags(context);
        tags.Add("datalinq.transaction.type", GetTransactionTypeName(transactionType));
        tags.Add("datalinq.outcome", succeeded ? GetTransactionOutcome(outcome) : "failure");

        TransactionCompleteCounter.Add(1, tags);
        TransactionDuration.Record(duration.TotalMilliseconds, tags);
        DataLinqMetrics.RecordTransactionCompleted(context, outcome, succeeded, duration);
    }

    internal static Activity? StartMutationActivity(
        DataLinqTelemetryContext context,
        string tableName,
        TransactionChangeType mutationType,
        TransactionType transactionType)
    {
        var activity = ActivitySource.StartActivity("datalinq.db.mutation", ActivityKind.Client);
        if (activity is null)
            return null;

        ApplyCommonTags(activity, context);
        activity.SetTag("datalinq.table", tableName);
        activity.SetTag("datalinq.mutation.type", GetMutationTypeName(mutationType));
        activity.SetTag("datalinq.transaction.type", GetTransactionTypeName(transactionType));
        return activity;
    }

    internal static void RecordMutationExecution(
        DataLinqTelemetryContext context,
        string tableName,
        TransactionChangeType mutationType,
        TransactionType transactionType,
        bool succeeded,
        int affectedRows,
        TimeSpan duration)
    {
        var tags = CreateTableTags(context, tableName);
        tags.Add("datalinq.mutation.type", GetMutationTypeName(mutationType));
        tags.Add("datalinq.transaction.type", GetTransactionTypeName(transactionType));
        tags.Add("datalinq.outcome", succeeded ? "success" : "failure");

        MutationCounter.Add(1, tags);

        if (affectedRows > 0)
            MutationAffectedRowsCounter.Add(affectedRows, tags);

        MutationDuration.Record(duration.TotalMilliseconds, tags);
        DataLinqMetrics.RecordMutationExecution(context, tableName, mutationType, succeeded, affectedRows, duration);
    }

    internal static void RecordException(Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error.type", exception.GetType().FullName);
    }

    internal static void RegisterTableCache(
        DataLinqTelemetryContext context,
        string tableName,
        Func<CacheOccupancyMetricsSnapshot> getSnapshot)
    {
        CacheGaugeRegistrations[GetTableRegistrationKey(context, tableName)] = new CacheGaugeRegistration(context, tableName, getSnapshot);
    }

    internal static void UnregisterTableCache(DataLinqTelemetryContext context, string tableName)
    {
        CacheGaugeRegistrations.TryRemove(GetTableRegistrationKey(context, tableName), out _);
    }

    internal static void RecordCacheMaintenance(
        DataLinqTelemetryContext context,
        string tableName,
        string operation,
        int rowsRemoved,
        TimeSpan duration)
    {
        var tags = CreateTableTags(context, tableName);
        tags.Add("datalinq.cache.operation", operation);

        CacheMaintenanceCounter.Add(1, tags);
        CacheMaintenanceDuration.Record(duration.TotalMilliseconds, tags);

        if (rowsRemoved > 0)
            CacheRowsRemovedCounter.Add(rowsRemoved, tags);
    }

    private static TagList CreateCommonTags(DataLinqTelemetryContext context)
    {
        var tags = new TagList
        {
            { "db.system", GetDatabaseSystemName(context.DatabaseType) },
            { "datalinq.provider.type", context.ProviderTypeName }
        };

        if (!string.IsNullOrWhiteSpace(context.DatabaseName))
            tags.Add("db.namespace", context.DatabaseName);

        return tags;
    }

    private static TagList CreateTableTags(DataLinqTelemetryContext context, string tableName)
    {
        var tags = CreateCommonTags(context);
        tags.Add("datalinq.table", tableName);
        return tags;
    }

    private static void ApplyCommonTags(Activity activity, DataLinqTelemetryContext context)
    {
        activity.SetTag("db.system", GetDatabaseSystemName(context.DatabaseType));
        activity.SetTag("datalinq.provider.type", context.ProviderTypeName);

        if (!string.IsNullOrWhiteSpace(context.DatabaseName))
            activity.SetTag("db.namespace", context.DatabaseName);
    }

    private static string GetDatabaseSystemName(DatabaseType databaseType)
        => databaseType switch
        {
            DatabaseType.MySQL => "mysql",
            DatabaseType.MariaDB => "mariadb",
            DatabaseType.SQLite => "sqlite",
            DatabaseType.Default => "default",
            _ => "unknown"
        };

    private static string GetTransactionTypeName(TransactionType transactionType)
        => transactionType switch
        {
            TransactionType.ReadAndWrite => "read_write",
            TransactionType.ReadOnly => "read_only",
            TransactionType.WriteOnly => "write_only",
            _ => "unknown"
        };

    private static string GetMutationTypeName(TransactionChangeType mutationType)
        => mutationType switch
        {
            TransactionChangeType.Insert => "insert",
            TransactionChangeType.Update => "update",
            TransactionChangeType.Delete => "delete",
            _ => "unknown"
        };

    private static IEnumerable<Measurement<long>> ObserveCacheRows()
        => ObserveCacheGauge(snapshot => snapshot.Rows);

    private static IEnumerable<Measurement<long>> ObserveCacheTransactionRows()
        => ObserveCacheGauge(snapshot => snapshot.TransactionRows);

    private static IEnumerable<Measurement<long>> ObserveCacheBytes()
        => ObserveCacheGauge(snapshot => snapshot.Bytes);

    private static IEnumerable<Measurement<long>> ObserveCacheIndexEntries()
        => ObserveCacheGauge(snapshot => snapshot.IndexEntries);

    private static IEnumerable<Measurement<long>> ObserveCacheGauge(Func<CacheOccupancyMetricsSnapshot, long> selector)
    {
        foreach (var registration in CacheGaugeRegistrations.Values)
        {
            CacheOccupancyMetricsSnapshot snapshot;
            try
            {
                snapshot = registration.GetSnapshot();
            }
            catch
            {
                continue;
            }

            yield return new Measurement<long>(selector(snapshot), CreateTableTags(registration.Context, registration.TableName));
        }
    }

    private static string GetTableRegistrationKey(DataLinqTelemetryContext context, string tableName)
        => $"{context.ProviderInstanceId}:{tableName}";

    private readonly record struct CacheGaugeRegistration(
        DataLinqTelemetryContext Context,
        string TableName,
        Func<CacheOccupancyMetricsSnapshot> GetSnapshot);
}
