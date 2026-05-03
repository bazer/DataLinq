> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Distributed Cache Coordination and CDC

**Status:** Draft

## Purpose

DataLinq's cache is intentionally aggressive. That is a strength inside one process, but it becomes a liability when several applications point at the same database and each process keeps its own row and relation cache.

The goal of this plan is to define a cache-coordination design that lets DataLinq applications invalidate local cached rows quickly when another process changes the same data.

The honest target is:

- commit-aware invalidation
- durable transports where production needs them
- explicit support for external change-data-capture feeds
- graceful degradation when precise invalidation cannot be proven

The dishonest target would be "instant distributed consistency". We should not sell that. Distributed cache invalidation is at-least-once messaging plus local eviction discipline. Pretending otherwise is how subtle production bugs get gift-wrapped as performance features.

## Current DataLinq Baseline

The current runtime already has the local pieces that make this feature plausible:

- Mutations go through `Transaction` and `StateChange`.
- Transaction-local cache changes are applied before commit, then global cache changes are applied after the database transaction commits.
- `State.ApplyChanges(...)` delegates cache maintenance to `DatabaseCache`.
- `TableCache.ApplyChanges(...)` removes rows and index entries for local mutations.
- `TableCache.SubscribeToChanges(...)` exists, but its notification contract is currently process-local and blunt: `ICacheNotification.Clear()`.
- DataLinq metrics already expose cache-notification counters and queue depth.

That is useful groundwork, but it is not a distributed invalidation contract yet. The current notification shape says "something changed, clear yourself"; it does not say which database, which table, which primary key, which columns, which transaction, or which process originated the event.

Relevant current files:

- `src/DataLinq/Mutation/Transaction.cs`
- `src/DataLinq/Mutation/State.cs`
- `src/DataLinq/Mutation/StateChange.cs`
- `src/DataLinq/Cache/DatabaseCache.cs`
- `src/DataLinq/Cache/TableCache.cs`
- `src/DataLinq/Diagnostics/DataLinqMetrics.cs`

## Design Stance

Build this library-first.

The primary product should be a set of DataLinq cache-sync abstractions plus transport implementations for established infrastructure. A small standalone server can exist as a dev fallback, but it should not be the core correctness mechanism.

Recommended package shape:

- `DataLinq.CacheSync.Abstractions`
- `DataLinq.CacheSync.Redis`
- `DataLinq.CacheSync.Kafka`
- `DataLinq.CacheSync.Kafka.MaxScale`
- `DataLinq.CacheSync.Nats` or `DataLinq.CacheSync.JetStream`
- `DataLinq.CacheSync.SqlOutbox`
- `DataLinq.CacheSync.DevServer`

The exact package names can change. The boundary should not.

Core DataLinq should expose stable hooks and local invalidation APIs. Transport packages should own Redis, Kafka, NATS, MaxScale, and any future dependency-heavy integrations.

## Non-Goals

This feature should not try to provide:

- distributed transactions across application processes
- exactly-once cache invalidation as a hard guarantee
- full row replication as the default behavior
- a replacement for Kafka, Redis Streams, NATS JetStream, or database CDC tooling
- cache updates before the database commit is durable
- automatic correctness for raw SQL writers unless a CDC or outbox path is configured

The cache is not the source of truth. The database remains the source of truth. The cache-sync layer only decides when local cached material should stop being trusted.

## Core Concepts

### Cache Invalidation Envelope

DataLinq needs a versioned envelope that can represent DataLinq-originated changes and external CDC-originated changes.

Sketch:

```csharp
public sealed record DataLinqCacheInvalidationEnvelope
{
    public required string EnvelopeVersion { get; init; }
    public required string EventId { get; init; }
    public required string OriginInstanceId { get; init; }
    public required string DatabaseId { get; init; }
    public required string DatabaseName { get; init; }
    public required string ProviderType { get; init; }
    public required string TableName { get; init; }
    public required CacheInvalidationOperation Operation { get; init; }
    public required IReadOnlyList<CacheInvalidationKeyValue> PrimaryKey { get; init; }
    public IReadOnlyList<string> ChangedColumns { get; init; } = [];
    public IReadOnlyList<CacheInvalidationIndexValue> OldIndexValues { get; init; } = [];
    public IReadOnlyList<CacheInvalidationIndexValue> NewIndexValues { get; init; } = [];
    public string? TransactionId { get; init; }
    public string? CommitSequence { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public IReadOnlyDictionary<string, string> TransportMetadata { get; init; } = new Dictionary<string, string>();
}
```

Important details:

- `EventId` is required for deduplication.
- `OriginInstanceId` prevents an application from evicting its own freshly updated cache unless explicitly configured.
- `PrimaryKey` must use database column identity plus typed values, not generated C# property names.
- `ChangedColumns` is useful for targeted index invalidation.
- `OldIndexValues` matter when an update changes a foreign key or indexed value.
- `CommitSequence` can be a Kafka offset, Redis stream ID, database outbox sequence, GTID, or provider-specific equivalent.

### Local Invalidation API

The current `TableCache.ApplyChanges(IEnumerable<StateChange>, Transaction?)` is built around local mutation objects. Remote invalidation should not require constructing fake `StateChange` objects.

Add a lower-level API conceptually like:

```csharp
public sealed record CacheInvalidationRequest(
    TableDefinition Table,
    IKey PrimaryKey,
    CacheInvalidationOperation Operation,
    IReadOnlyList<ColumnDefinition> ChangedColumns,
    IReadOnlyList<IndexInvalidationValue> OldIndexValues,
    IReadOnlyList<IndexInvalidationValue> NewIndexValues);
```

The first version may be blunt:

- remove the row by primary key
- clear relevant table index caches
- clear related relation-side index entries if precise old/new values are available
- fall back to table-level index clear when precision is unavailable

That is the right tradeoff. A slightly over-eager eviction is a performance cost. A stale relation index is a correctness bug.

### Transport Contract

The transport abstraction should be intentionally boring:

```csharp
public interface IDataLinqCacheInvalidationPublisher
{
    ValueTask PublishAsync(DataLinqCacheInvalidationBatch batch, CancellationToken cancellationToken);
}

public interface IDataLinqCacheInvalidationConsumer
{
    IAsyncEnumerable<DataLinqCacheInvalidationEnvelope> ReadAsync(CancellationToken cancellationToken);
    ValueTask AcknowledgeAsync(DataLinqCacheInvalidationEnvelope envelope, CancellationToken cancellationToken);
}
```

Transport packages can adapt this to stream IDs, offsets, acks, commits, dead-letter handling, or replay. Core DataLinq should not know whether the event came from Redis, Kafka, NATS, or a SQL outbox table.

## Commit-Aware Publishing

There are two publishing modes, and they are not equivalent.

### Direct Post-Commit Publishing

Flow:

1. DataLinq executes the mutation inside the database transaction.
2. The database transaction commits.
3. DataLinq applies local global-cache changes.
4. DataLinq publishes invalidation events to the configured transport.

This is simple and useful, but it has a crash window: the database commit can succeed and the process can die before the event is published.

Direct post-commit publishing is acceptable for:

- dev
- low-risk apps
- deployments where occasional stale cache is tolerable and bounded by TTL/manual clear
- transports that are used as latency accelerators rather than correctness foundations

It is not enough for serious multi-application production correctness.

### Transactional Outbox Publishing

Flow:

1. DataLinq executes the mutation inside the database transaction.
2. DataLinq writes invalidation events to an outbox table inside the same transaction.
3. The database transaction commits.
4. A dispatcher reads the outbox and publishes events to Redis, Kafka, NATS, or another transport.
5. Published outbox rows are marked dispatched or deleted according to retention settings.

This is the production-grade pattern because the row change and the invalidation event become one durable database fact.

The cost is operational and implementation complexity:

- outbox schema
- dispatcher lifecycle
- retry policy
- poison event handling
- cleanup/retention
- provider-specific transaction integration

That cost is justified if cross-process cache correctness matters.

## External CDC Ingestion

DataLinq-originated invalidation is only half the problem. Many real systems have raw SQL scripts, legacy services, admin tools, ETL jobs, and database-side processes that change rows without going through DataLinq.

For those environments, DataLinq should be able to consume external CDC streams and translate them into cache invalidation envelopes.

### Kafka and MariaDB MaxScale KafkaCDC

Kafka is especially interesting because MariaDB MaxScale has a `kafkacdc` router that reads MariaDB changes through replication and streams JSON events to Kafka.

The valuable part for DataLinq:

- it can observe changes not made through DataLinq
- DML events include event type, schema/table identity, GTID fields, event sequence, timestamp, and row fields
- updates are represented with `update_before` and `update_after` events
- MaxScale can filter included and excluded tables
- Kafka becomes the durable replay and fanout layer

The sharp edges:

- MaxScale KafkaCDC requires row-based binary logging and full row image configuration.
- KafkaCDC is at-least-once; duplicates must be expected.
- `update_before` and `update_after` must be paired or interpreted carefully.
- DDL/schema events should not be treated like row invalidation. They should trigger schema drift handling or broad cache clearing.
- A CDC event may refer to a table unknown to the current DataLinq model.
- Type conversion must use DataLinq provider metadata, not naive JSON assumptions.

The likely package should be separate from the generic Kafka transport:

- `DataLinq.CacheSync.Kafka` handles DataLinq's native invalidation envelope over Kafka.
- `DataLinq.CacheSync.Kafka.MaxScale` consumes MaxScale KafkaCDC JSON and maps it into DataLinq invalidation envelopes.

This separation matters. A DataLinq-native Kafka topic and a MaxScale CDC topic are not the same protocol.

### Other CDC Formats

Keep the mapping layer pluggable:

```csharp
public interface IDataLinqCdcEventMapper<TExternalEvent>
{
    bool TryMap(TExternalEvent externalEvent, CdcMappingContext context, out DataLinqCacheInvalidationEnvelope envelope);
}
```

That leaves room for:

- Debezium-style envelopes
- provider-native notification APIs
- SQL trigger/outbox tables
- cloud database change streams
- custom application events

Do not bake MaxScale JSON into the core envelope. Treat it as one adapter.

## Transport Semantics

### Kafka

Kafka is a strong fit for production CDC and replay, but consumer group semantics are a trap for process-local caches.

Every DataLinq process with its own memory cache must see every invalidation event. If several app instances share the same Kafka consumer group, Kafka will distribute partitions across those instances and each process will see only part of the stream. That is correct Kafka behavior and wrong DataLinq cache behavior.

Therefore:

- use a unique consumer group per cache instance when every process has its own cache
- use a shared group only when a downstream service fans the event out to every cache instance
- partition native DataLinq events by stable row key if per-row ordering matters
- store the last processed offset/bookmark per cache instance if replay across restart is required
- treat duplicate events as normal

### Redis

Redis has two possible roles:

- Redis Pub/Sub for low-latency best-effort broadcast
- Redis Streams for durable replay

Redis Pub/Sub is attractive for dev and simple deployments, but it has no durable replay for disconnected consumers. That makes it a latency optimization, not a correctness foundation.

Redis Streams are more serious, but the same broadcast issue exists. Redis consumer groups divide work among consumers. That is not what process-local cache invalidation needs unless each process has its own group or some other fanout layer exists.

For DataLinq cache sync:

- Redis Pub/Sub can be a simple mode.
- Redis Streams should be the default Redis production mode.
- Consumer-group naming must be explicit and documented.
- Stream trimming must be configured carefully, because aggressive trimming destroys replay after downtime.

### NATS JetStream

NATS Core is a good simple notification bus, but it is at-most-once. JetStream is the serious option because it supports persistence, replay, acknowledgments, and redelivery.

The same rule applies: do not accidentally load-balance invalidations away from processes that each own separate local caches.

### DataLinq Dev Server

A small standalone server is still useful:

- local development
- integration tests
- demos
- teams that do not want Redis/Kafka running just to verify the feature

But the default dev server should be described as simple and non-production unless it grows:

- persistence
- replay
- authentication
- TLS
- clustering or leader election
- backpressure
- health checks
- observability

The dev server should probably use WebSockets or gRPC streaming and an in-memory ring buffer. If it becomes more ambitious than that, we should ask whether we are rebuilding a weaker message broker.

## Delivery and Failure Semantics

The contract should be explicit:

- delivery is at-least-once for durable transports
- delivery may be best-effort for simple transports
- consumers must deduplicate by `EventId` and/or transport position
- consumers must tolerate out-of-order events across partitions or streams
- per-row ordering requires stable partitioning by database/table/primary key
- missed events require replay or cache clearing
- unrecognized events should be counted and surfaced, not silently ignored

Recommended fallback rule:

If DataLinq detects that it missed part of the stream and cannot replay it, it should clear affected table caches or the entire provider cache. That is blunt but correct.

## Relation and Index Cache Handling

Relation caches are the hard part.

For local writes, `StateChange` has the model and changed values, so `TableCache.ApplyChanges(...)` can remove rows and related index entries. For external CDC, precision depends on the event format.

Initial behavior should be conservative:

- row primary-key invalidation when primary key is known
- full table index clear when changed indexed values are unknown
- relation-side index clear when old/new FK values are known
- provider-cache clear when a DDL event changes table shape

Later behavior can become more precise:

- preserve old/new indexed values in native DataLinq envelopes
- pair MaxScale `update_before` and `update_after` events
- use metadata to identify affected relation paths
- batch index invalidation by table and column

Again: over-eviction is acceptable. Under-eviction is not.

## Configuration Shape

Sketch:

```csharp
services.AddDataLinq<MyDatabase>(options =>
{
    options.UseMariaDb(connectionString);

    options.UseCacheSync(sync =>
    {
        sync.InstanceId("orders-api-01");
        sync.DatabaseId("production-orders");
        sync.IgnoreOwnEvents();
        sync.OnGap(CacheSyncGapBehavior.ClearAffectedTables);

        sync.UseKafka(kafka =>
        {
            kafka.BootstrapServers = "kafka-01:9092,kafka-02:9092";
            kafka.Topic = "datalinq.cache-invalidation";
            kafka.ConsumerGroup = CacheSyncConsumerGroup.UniquePerInstance;
            kafka.PartitionKey = CacheSyncPartitionKey.DatabaseTablePrimaryKey;
        });
    });
});
```

MaxScale CDC sketch:

```csharp
services.AddDataLinq<MyDatabase>(options =>
{
    options.UseMariaDb(connectionString);

    options.UseCacheSync(sync =>
    {
        sync.UseKafkaMaxScaleCdc(cdc =>
        {
            cdc.BootstrapServers = "kafka-01:9092";
            cdc.Topic = "mariadb-cdc";
            cdc.ConsumerGroup = CacheSyncConsumerGroup.UniquePerInstance;
            cdc.UnknownTableBehavior = CdcUnknownTableBehavior.IgnoreAndCount;
            cdc.SchemaChangeBehavior = CdcSchemaChangeBehavior.ClearProviderCacheAndWarn;
        });
    });
});
```

The API should make the dangerous configuration obvious. A shared load-balancing consumer group should not be the silent default.

## Observability

This feature must ship with metrics from day one. Otherwise, users will have no idea whether they are protected or just feeling protected.

Add metrics for:

- events published
- publish failures
- events received
- events deduplicated
- events ignored as own-origin
- events mapped from CDC
- events rejected as unknown schema/table
- invalidated rows
- table clears
- provider clears
- replay starts/completions/failures
- stream gaps
- consumer lag/bookmark age
- outbox pending rows
- outbox dispatch failures

OpenTelemetry should be application-owned as it is today. DataLinq should expose meters and activities; applications decide where to export them.

## Testing Strategy

Minimum tests before calling this real:

- two DataLinq provider instances pointed at the same database
- instance A updates a row; instance B evicts stale cache and reloads the fresh row
- duplicate event delivery does not break anything
- own-origin events can be ignored
- missed-event behavior clears affected cache
- update that changes a foreign key clears relation caches correctly
- delete invalidates row cache and relation indexes
- table-level clear fallback works
- direct publisher publishes only after commit
- rolled-back transactions do not publish
- outbox event survives process restart after commit
- Kafka transport uses broadcast-safe consumer group configuration
- Redis Streams mode does not accidentally shard invalidations across local caches
- MaxScale CDC mapper handles insert, delete, update-before/update-after, unknown table, and schema events

Provider-backed integration tests should start narrow. The first serious test matrix should be:

- SQLite/in-memory test transport for core semantics
- MariaDB with two DataLinq instances for provider behavior
- Kafka test container or local harness for native envelope transport
- MaxScale KafkaCDC harness only after core invalidation is proven

## Implementation Phases

### Phase A: Local Invalidation Surface

- Add internal/public local invalidation request shape.
- Add table/provider cache invalidation APIs that do not require fake `StateChange`.
- Preserve current in-process notification behavior.
- Add tests for precise and blunt invalidation.

### Phase B: Native DataLinq Envelope

- Add versioned envelope and batch types.
- Add origin instance id and event id generation.
- Add serialization tests.
- Add compatibility tests for unknown future fields.

### Phase C: Direct Post-Commit Transport

- Add publisher hook that runs only after successful commit.
- Add in-memory transport for tests.
- Add Redis Pub/Sub or dev-server transport as a first low-friction integration.
- Document the crash window clearly.

### Phase D: Durable Transport

- Add Kafka native envelope transport or Redis Streams transport.
- Implement replay/bookmark behavior.
- Add gap detection and cache-clear fallback.
- Add transport metrics.

### Phase E: Transactional Outbox

- Add SQL outbox schema and dispatcher.
- Ensure outbox writes occur inside the same database transaction as the mutation.
- Add retry, retention, and poison-row handling.
- Add provider tests for MariaDB and SQLite where feasible.

### Phase F: External CDC

- Add CDC mapping abstraction.
- Add MaxScale KafkaCDC mapper.
- Handle `insert`, `delete`, `update_before`, `update_after`, and schema events.
- Add table filtering and unknown-table behavior.
- Add documentation for required MariaDB binary-log configuration.

### Phase G: Dev Server

- Add simple standalone host for local development and demos.
- Keep it explicitly non-production unless durability and security are implemented.
- Add integration tests that exercise reconnection and replay behavior if the server supports replay.

## Open Questions

- Should the core envelope live in `DataLinq` or a separate `DataLinq.CacheSync.Abstractions` package?
- Should outbox support be provider-specific from the beginning, or should it start with SQL text hooks?
- How much of the local invalidation API should be public?
- Should DataLinq support eager remote refresh, or only eviction?
- Do we need a model/schema version stamp in every envelope before CDC adapters are safe?
- Should cache-sync be opt-in per database, per table, or both?
- What is the safest default for DDL events: provider clear, process warning, or forced sync pause?

## References

- MariaDB MaxScale overview says MaxScale can export database changes to Kafka through the KafkaCDC router: [MariaDB MaxScale Overview](https://mariadb.com/kb/en/mariadb-maxscale-overview/)
- MariaDB MaxScale KafkaCDC reads MariaDB changes through replication and streams JSON to Kafka, with DML event types including `insert`, `delete`, `update_before`, and `update_after`: [MaxScale 23.08 KafkaCDC](https://mariadb.com/docs/maxscale/maxscale-archive/archive/mariadb-maxscale-23.08/mariadb-maxscale-23-08-routers/mariadb-maxscale-2308-kafkacdc)
- MariaDB documents `binlog_format=ROW` and `binlog_row_image=FULL` as KafkaCDC requirements, and states that KafkaCDC provides at-least-once semantics with possible duplicates: [MariaDB MaxScale KafkaCDC](https://mariadb.com/kb/en/mariadb-maxscale-2308-kafkacdc/)
- MariaDB's legacy MaxScale CDC protocol is deprecated in favor of KafkaCDC: [MaxScale Change Data Capture Protocol](https://mariadb.com/docs/maxscale/reference/maxscale-protocols/maxscale-change-data-capture-cdc-protocol)
- Kafka consumer groups broadcast records to distinct groups but distribute partitions within a group: [Apache Kafka Introduction](https://kafka.apache.org/20/getting-started/introduction/)
- Kafka consumers track offsets and can resume from committed positions: [Apache Kafka Distribution](https://kafka.apache.org/42/implementation/distribution/)
- Redis `XREADGROUP` consumer groups partition stream entries among consumers and require acknowledgment: [Redis XREADGROUP](https://redis.io/docs/latest/commands/xreadgroup/)
- Redis Streams are append-only logs that can be used as a buffer between processes: [Redis Streams](https://redis.io/docs/latest/develop/data-types/streams/)
- NATS JetStream adds persistence, replay, acknowledgments, and at-least-once delivery over Core NATS: [NATS JetStream Consumers](https://docs.nats.io/nats-concepts/jetstream/consumers)
