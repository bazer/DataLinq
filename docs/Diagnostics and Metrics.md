# Diagnostics and Metrics

DataLinq now exposes runtime metrics through a hierarchical snapshot API:

- runtime totals at the top
- one metrics node per loaded provider instance
- one metrics node per table within each provider

That shape is deliberate. A flat runtime snapshot would be easy to read but too easy to misread. It would blur together values that are really owned by different providers and tables, and it would make it harder to reason about production behavior when several providers were loaded at once.

## Entry Points

Use the static `DataLinqMetrics` API:

```csharp
using DataLinq.Diagnostics;

var snapshot = DataLinqMetrics.Snapshot();
```

The snapshot type is `DataLinqMetricsSnapshot`.

For benchmarks or controlled test runs, you can also reset the collected metrics:

```csharp
DataLinqMetrics.Reset();
```

Do not blindly call `Reset()` in a live multi-consumer production process. That is fine for tests and benchmarks, but it is a blunt tool for shared diagnostics.

## Hierarchy

```mermaid
---
config:
  theme: neo
  look: classic
---
flowchart TD
    A["DataLinqMetricsSnapshot<br/>Runtime totals"] --> B["Provider 1<br/>DataLinqProviderMetricsSnapshot"]
    A --> C["Provider 2<br/>DataLinqProviderMetricsSnapshot"]
    B --> D["Table: employees<br/>DataLinqTableMetricsSnapshot"]
    B --> E["Table: dept-emp<br/>DataLinqTableMetricsSnapshot"]
    C --> F["Table: users<br/>DataLinqTableMetricsSnapshot"]
```

Each provider node is keyed by a stable provider-instance id for the current process lifetime. That matters because several loaded providers may share the same logical database name or the same metadata model and still need to be tracked independently.

## Ownership Rules

The ownership model is what keeps the sums honest.

- `QueryMetricsSnapshot` is provider-owned.
- `RelationMetricsSnapshot` is table-owned.
- `RowCacheMetricsSnapshot` is table-owned.
- `CacheNotificationMetricsSnapshot` is table-owned.

So:

- runtime `Queries` is the sum of provider `Queries`
- provider `Relations`, `RowCache`, and `CacheNotifications` are sums of that provider's tables
- runtime `Relations`, `RowCache`, and `CacheNotifications` are sums of all providers

Query metrics are intentionally not forced into tables. A single query can touch several tables, and fake table attribution would make the totals look cleaner while making them less true.

## Snapshot Shapes

### Runtime

```csharp
DataLinqMetricsSnapshot
{
    QueryMetricsSnapshot Queries;
    RelationMetricsSnapshot Relations;
    RowCacheMetricsSnapshot RowCache;
    CacheNotificationMetricsSnapshot CacheNotifications;
    DataLinqProviderMetricsSnapshot[] Providers;
}
```

### Provider

```csharp
DataLinqProviderMetricsSnapshot
{
    string ProviderInstanceId;
    string ProviderTypeName;
    string DatabaseName;
    DatabaseType DatabaseType;
    QueryMetricsSnapshot Queries;
    RelationMetricsSnapshot Relations;
    RowCacheMetricsSnapshot RowCache;
    CacheNotificationMetricsSnapshot CacheNotifications;
    DataLinqTableMetricsSnapshot[] Tables;
}
```

### Table

```csharp
DataLinqTableMetricsSnapshot
{
    string TableName;
    RelationMetricsSnapshot Relations;
    RowCacheMetricsSnapshot RowCache;
    CacheNotificationMetricsSnapshot CacheNotifications;
}
```

## Reading the Metrics Correctly

This is where people most often fool themselves.

### Query metrics

- `EntityExecutions` and `ScalarExecutions` are counters.
- They are provider-owned and summed upward.

### Row cache metrics

- `Hits`, `Misses`, `DatabaseRowsLoaded`, `Materializations`, and `Stores` are counters.
- They are table-owned and summed upward.

### Relation metrics

- `ReferenceCacheHits`, `ReferenceLoads`, `CollectionCacheHits`, and `CollectionLoads` are counters.
- They are table-owned and summed upward.

### Cache notification metrics

Some values are counters. Some are gauges. Some are “last seen per child, then summed”.

- `Subscriptions` is a cumulative counter of `Subscribe()` calls. It is not the current number of live subscribers.
- `ApproximateCurrentQueueDepth` is a gauge. At runtime level it is the sum of the current per-table queue depths.
- `NotifySweeps`, `NotifySnapshotEntries`, `NotifyLiveSubscribers`, `CleanSweeps`, `CleanSnapshotEntries`, `CleanRequeuedSubscribers`, `CleanDroppedSubscribers`, and `CleanBusySkips` are cumulative counters.
- `LastNotifySnapshotEntries`, `LastNotifyLiveSubscribers`, `LastCleanSnapshotEntries`, `LastCleanRequeuedSubscribers`, and `LastCleanDroppedSubscribers` are the latest values recorded on each child, then summed upward.
- `ApproximatePeakQueueDepth` is a max, not a sum.

That last point is important. A runtime peak queue depth of `5000` means some underlying table peaked around `5000`. It does not mean the system once had a global atomic queue depth of exactly `5000`.

## Example

```csharp
var snapshot = DataLinqMetrics.Snapshot();

// Runtime totals
var totalEntityQueries = snapshot.Queries.EntityExecutions;
var totalRowCacheHits = snapshot.RowCache.Hits;
var totalNotificationDepth = snapshot.CacheNotifications.ApproximateCurrentQueueDepth;

// Provider-level drilldown
foreach (var provider in snapshot.Providers)
{
    Console.WriteLine($"{provider.ProviderTypeName} ({provider.DatabaseName})");
    Console.WriteLine($"  Entity queries: {provider.Queries.EntityExecutions}");
    Console.WriteLine($"  Row cache hits: {provider.RowCache.Hits}");
    Console.WriteLine($"  Notification depth: {provider.CacheNotifications.ApproximateCurrentQueueDepth}");

    foreach (var table in provider.Tables)
    {
        Console.WriteLine($"    {table.TableName}:");
        Console.WriteLine($"      Row cache hits: {table.RowCache.Hits}");
        Console.WriteLine($"      Notification depth: {table.CacheNotifications.ApproximateCurrentQueueDepth}");
    }
}
```

## Practical Recommendation for Application Integrations

If you are integrating this into an admin page or periodic telemetry log:

- keep a flat adapter DTO if your UI already expects one
- expose the provider/table tree as a second, richer view when you need drilldown
- log both runtime totals and the hottest provider/table contributors

If you only log the runtime totals, you will eventually end up asking “which table actually caused this?” and have no answer.

## Standard .NET Telemetry

`DataLinqMetrics` is the in-process snapshot view. It is not the whole telemetry story.

DataLinq also emits standard .NET telemetry with:

- `Meter`: `DataLinq`
- `ActivitySource`: `DataLinq`

That is the right library boundary. DataLinq produces telemetry; your application decides whether to inspect it locally, export it with OpenTelemetry, or ignore it.

### What DataLinq emits

The exported surface now covers the main runtime paths:

- query count and end-to-end query duration
- DB command count and duration
- transaction start/completion count and duration
- mutation count, affected rows, and duration
- cache occupancy gauges for rows, transaction rows, bytes, and index entries
- cache maintenance counters and duration

SQL text is still a logging concern, not a metric tag. That is deliberate. Putting SQL text into metric tags would be a cardinality bug.

## Local Inspection with `dotnet-counters`

For quick local inspection, `dotnet-counters` is the simplest path.

1. Start your application.
2. Find the process id.
3. Monitor the DataLinq meter:

```bash
dotnet-counters monitor --process-id <pid> --counters DataLinq
```

That is useful for questions like:

- are commands actually being issued?
- are mutations increasing?
- is the cache growing or being cleaned up?
- are transaction rates changing under load?

If you need table-by-table drilldown, use `DataLinqMetrics.Snapshot()` in-process. `dotnet-counters` is for live aggregate observation, not rich per-table analysis.

## OpenTelemetry Integration

DataLinq does not require an OpenTelemetry dependency in the core package. The application should opt into collection and exporting.

A normal application-side setup looks like this:

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("DataLinq")
            .AddRuntimeInstrumentation();
    })
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("DataLinq");
    });
```

Then add whatever exporter your app actually uses. That might be OTLP, Azure Monitor, or just console/exporter wiring during development.

The important part is not the exporter. The important part is that the app listens to:

- `Meter("DataLinq")`
- `ActivitySource("DataLinq")`

## Choosing Between Snapshot and Exported Telemetry

Use `DataLinqMetrics.Snapshot()` when you need:

- provider/table drilldown
- deterministic before/after deltas in tests or benchmarks
- a local admin/debug endpoint

Use `Meter` and `ActivitySource` when you need:

- live process observation
- app-wide telemetry collection
- traces correlated with the rest of your service
- backend/export integration through standard .NET tooling

These are complementary. If you force one to do the other's job, you will get worse results.
