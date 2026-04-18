using System;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
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

    private static readonly Meter Meter = new(InstrumentationName, InstrumentationVersion);
    private static readonly ActivitySource ActivitySource = new(InstrumentationName, InstrumentationVersion);

    private static readonly Counter<long> CommandCounter = Meter.CreateCounter<long>(
        "datalinq.db.commands",
        unit: "{command}",
        description: "Number of database commands executed by DataLinq.");
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

    internal static void RecordException(Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error.type", exception.GetType().FullName);
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
}
