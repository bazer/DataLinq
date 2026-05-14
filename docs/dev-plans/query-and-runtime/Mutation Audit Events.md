> [!WARNING]
> This document is roadmap or specification material. It describes planned behavior rather than current DataLinq behavior.

# Specification: Mutation Audit Events

**Status:** Draft / Approved Direction
**Goal:** Provide a structured, ordered, commit-time audit stream for DataLinq mutations, including full audited row data and per-column before/after values, while leaving persistence, retention, formatting, and transport to the application.

## 1. Design Position

DataLinq should provide mutation audit events.

The right boundary is narrow:

> DataLinq emits structured audit batches after a transaction successfully commits. The application decides where those batches go.

This is not normal diagnostic logging. `ILogger` is useful for "what did DataLinq do?" It is the wrong abstraction for "what business data changed?" Audit data needs stable structure, row images, table and column metadata, deterministic ordering, and application context such as user id or request id. That is a domain event stream, not a text log.

The audit stream should be:

- explicit
- opt-in per table or table group
- configurable per column
- ordered
- commit-only
- structured
- provider-neutral
- aligned with mutation planner semantics

It should not be magical. If a table is not configured for audit, DataLinq should not emit entries for that table. If an update becomes a no-op, DataLinq should not touch the database and should not emit an audit entry. If a transaction rolls back, DataLinq should not emit committed audit entries.

## 2. Relationship to Other Plans

This plan depends on:

- [Mutable Instance Lifecycle](Mutable%20Instance%20Lifecycle.md)
- [Relation-Aware Mutation API](Relation-Aware%20Mutation%20API.md)
- [Batched Mutations](Batched%20mutations.md)

It also intersects with:

- [Set-based mutations](Set-based%20mutations.md)

The relationship is:

- mutable lifecycle defines when a mutable baseline is trusted enough to produce a before image
- relation-aware mutation defines graph ordering and generated-key propagation
- batched mutations define logical mutation plans and hydration boundaries
- set-based mutations define the hard cases where many affected rows must be captured around one set operation

Optimistic concurrency is related but separate. Audit can record what DataLinq observed and wrote. It should not pretend to prove that no outside writer changed the row unless a later concurrency feature gives that guarantee.

## 3. Current Limitation

The current mutation path has some useful ingredients, but not enough for audit-grade output.

Today:

- `Transaction.Changes` preserves ordered `StateChange` records
- `StateChange` knows operation type, model, table, primary key, and changed columns
- `StateChange` captures original values for changed columns when the mutable has a baseline
- cache invalidation can use those changes to invalidate rows and relation keys

That is not an audit event.

Missing pieces:

- no commit-only audit batch
- no subscriber API
- no full before row image
- no full after row image
- no column include/exclude/redaction policy
- no application context
- no set-based mutation capture strategy
- no delivery failure policy
- no public semantics around rollback, no-op updates, or subscriber ordering

The existing `StateChange` type should not become the public audit DTO. It is an internal mutation/cache invalidation artifact. Audit needs a stable public contract.

## 4. Non-Goals

### 4.1. Do not store audit logs

DataLinq should not decide:

- database schema for audit tables
- retention policy
- external log sink
- serialization format
- message queue
- encryption-at-rest
- tenant-specific access controls

The application owns those decisions.

DataLinq owns producing a correct structured event.

### 4.2. Do not emit before commit

This feature should not include a before-commit hook.

A before-commit hook is tempting because it could let an application write audit rows inside the same database transaction. That is a real feature, but it has a different failure contract:

- subscriber failure would need to abort the transaction
- subscriber code would run while DataLinq transaction state is still active
- subscribers could re-enter the same provider in dangerous ways
- ordering with cache promotion and mutable reset would be harder

This plan deliberately draws the boundary at after-commit emission only.

### 4.3. Do not emit rollback audit entries

Rollback diagnostics are useful, but they are not committed mutation audit.

The audit stream in this plan records committed data changes only. A rolled-back transaction should discard buffered audit entries.

### 4.4. Do not audit external database writes

DataLinq can audit mutations that go through DataLinq mutation APIs. It cannot audit:

- raw SQL executed outside DataLinq
- another process changing the database
- database jobs
- triggers that write to unrelated tables unless DataLinq explicitly observes those rows

The public docs must say this plainly.

### 4.5. Do not use `ILogger` for audit payloads

Diagnostic logs can reference that audit emission happened. They should not be the audit payload.

Text logs are too lossy and too easy to misconfigure for full row audit data.

### 4.6. Do not emit no-op updates

If an update resolves to no actual changed audited or persisted values, DataLinq should treat it as a no-op:

- no SQL
- no cache invalidation
- no audit entry

This requires a normalization step before execution. Merely assigning a property should not be enough to force an update if the assigned value equals the trusted baseline after provider conversion/equality normalization.

## 5. Key Concepts

### Audit batch

A committed transaction-level audit payload.

One transaction commit can produce zero or one audit batch. If no audited rows changed, no batch should be emitted.

### Audit entry

One audited row mutation inside a batch.

Entries are ordered and represent logical row mutations, even when the provider executed multiple entries through one batched SQL command.

### Row image

A complete snapshot of the audited columns for one row at one point in time.

For an insert, the `After` image is required.

For an update, both `Before` and `After` images are required.

For a delete, the `Before` image is required.

### Column change

A before/after value pair for one audited column whose value changed.

For updates, column changes should be computed by comparing the audited before and after row images, not merely by listing assigned mutable fields. That captures database defaults, generated columns, converter normalization, and visible trigger effects when DataLinq hydrates the final row.

### Audit context

Application-supplied metadata attached to the batch.

Examples:

- actor user id
- actor display name
- tenant id
- request id
- correlation id
- job id
- source subsystem

Audit context is not row data. It describes why or by whom the mutation happened.

### Audit policy

Configuration that decides which tables and columns are included, excluded, or redacted.

The audit policy must be evaluated before planning capture work. If a table is not audited, DataLinq should avoid extra row capture for that table.

## 6. Public Semantics

### 6.1. Insert

```csharp
using var transaction = database.Transaction();

var saved = transaction.Insert(employee);

transaction.Commit(MutationAuditContext.From(
    ("actor.user_id", currentUser.Id),
    ("request.id", requestId)));
```

Expected audit entry:

- operation is `Insert`
- `Before` is null
- `After` contains all included columns from the hydrated inserted row
- generated primary keys are included when policy allows them
- default values and provider-converted values are included in the hydrated image
- entry is emitted only after `Commit()` succeeds

The inserted row image must not be based only on values assigned to the mutable. It must represent the row DataLinq believes exists after the insert has completed and hydration has happened.

### 6.2. Update

```csharp
using var transaction = database.Transaction();

employee.first_name = "Ada";
employee.last_name = "Lovelace";

var saved = transaction.Update(employee);

transaction.Commit(MutationAuditContext.From(("actor.user_id", currentUser.Id)));
```

Expected audit entry:

- operation is `Update`
- `Before` contains all included columns before the update
- `After` contains all included columns after the update
- `Changes` contains included columns where `Before` and `After` differ
- entry order matches the logical mutation order

The before image should come from a trusted mutable baseline when available. If the baseline is not complete enough for a full audited before image, DataLinq must fetch the before row before executing the update.

The after image should come from the hydrated immutable row returned by the mutation path, or from an equivalent post-write hydration step.

### 6.3. No-op update

```csharp
using var transaction = database.Transaction();

employee.first_name = employee.GetImmutableInstance()!.first_name;

transaction.Update(employee);
transaction.Commit();
```

Expected behavior:

- no SQL update
- no audit entry
- no cache invalidation

This is important. Audit logs are worse than useless if they fill with "changed X from A to A" entries.

### 6.4. Delete

```csharp
using var transaction = database.Transaction();

transaction.Delete(employee);
transaction.Commit(MutationAuditContext.From(("actor.user_id", currentUser.Id)));
```

Expected audit entry:

- operation is `Delete`
- `Before` contains all included columns from the row being deleted
- `After` is null
- `Changes` is empty unless DataLinq later chooses to expose delete fields as removed values

If the caller deletes a dirty mutable, the delete audit `Before` image must represent the persisted row being deleted, not unsaved local assignments. Unsaved local changes did not reach the database.

### 6.5. Rollback

```csharp
using var transaction = database.Transaction();

transaction.Insert(employee);
transaction.Rollback();
```

Expected behavior:

- no committed audit batch
- buffered audit entries are discarded
- subscribers are not called

### 6.6. Multiple audited tables

If a transaction mutates rows from multiple audited tables, DataLinq emits one batch with entries interleaved by logical mutation sequence.

Example:

```csharp
using var transaction = database.Transaction();

var employee = transaction.Insert(newEmployee);
transaction.Insert(newSalary);
transaction.Update(department);

transaction.Commit(context);
```

Expected order:

1. employee insert
2. salary insert
3. department update

Provider command batching must not reorder audit entries in the public payload.

## 7. API Surface

### 7.1. Subscription

Suggested shape:

```csharp
public interface IMutationAuditSubscriber
{
    void OnMutationCommitted(MutationAuditBatch batch);
}
```

Convenience delegate:

```csharp
public delegate void MutationAuditCommittedHandler(MutationAuditBatch batch);
```

Registration:

```csharp
IDisposable subscription = database.MutationAudit.Subscribe(batch =>
{
    auditWriter.Write(batch);
});
```

or:

```csharp
database.MutationAudit.Subscribe(new ApplicationAuditSubscriber());
```

Subscriptions should be strong subscriptions and should return `IDisposable`.

Do not use weak event subscriptions for audit. Losing a subscriber because it was only weakly referenced would be unacceptable and extremely hard to diagnose.

### 7.2. Batch DTO

Sketch:

```csharp
public sealed record MutationAuditBatch(
    ulong BatchId,
    uint TransactionId,
    string DatabaseName,
    DatabaseType DatabaseType,
    DateTimeOffset StartedAt,
    DateTimeOffset CommittedAt,
    MutationAuditContext Context,
    IReadOnlyList<MutationAuditEntry> Entries);
```

Notes:

- `BatchId` is a process-local monotonic id unless a stronger provider-wide id is later introduced.
- `TransactionId` maps to DataLinq's transaction id.
- `StartedAt` is when the DataLinq transaction started.
- `CommittedAt` is captured immediately after provider commit succeeds.
- `Context` is a snapshot, not a live mutable dictionary.

### 7.3. Entry DTO

Sketch:

```csharp
public sealed record MutationAuditEntry(
    long Sequence,
    MutationAuditOperation Operation,
    string DatabaseName,
    string TableName,
    string? SchemaName,
    Type ModelType,
    MutationAuditRowIdentity? BeforeIdentity,
    MutationAuditRowIdentity? AfterIdentity,
    MutationAuditRowImage? Before,
    MutationAuditRowImage? After,
    IReadOnlyList<MutationAuditColumnChange> Changes);
```

```csharp
public enum MutationAuditOperation
{
    Insert,
    Update,
    Delete,
    Unlink
}
```

`Unlink` is included for relation-aware mutation. If unlink is implemented as an update that sets foreign keys to null, it can still be surfaced as `Update` in v1. If the relation-aware API exposes unlink as a distinct user operation, preserving that operation name in audit is useful.

### 7.4. Row image DTO

Sketch:

```csharp
public sealed record MutationAuditRowImage(
    IReadOnlyList<MutationAuditColumnValue> Columns);

public sealed record MutationAuditColumnValue(
    string PropertyName,
    string ColumnName,
    Type? ClrType,
    string? DatabaseType,
    object? Value,
    bool IsRedacted);
```

Column order should follow table metadata order, not dictionary insertion order.

Values should be provider/model CLR values after DataLinq conversion, not raw database reader objects, unless a provider cannot safely convert a value. The public docs should state the value representation.

### 7.5. Column change DTO

Sketch:

```csharp
public sealed record MutationAuditColumnChange(
    string PropertyName,
    string ColumnName,
    Type? ClrType,
    string? DatabaseType,
    object? Before,
    object? After,
    bool IsRedacted);
```

For redacted columns, the entry should preserve the fact that the column changed without exposing the values:

```text
ColumnName = "password_hash"
Before = null
After = null
IsRedacted = true
```

Excluded columns should not appear at all.

### 7.6. Row identity

Sketch:

```csharp
public sealed record MutationAuditRowIdentity(
    IReadOnlyList<MutationAuditColumnValue> PrimaryKeyColumns);
```

Primary key columns are needed for row identity, but they are still data. The policy must decide how to handle excluded or redacted primary key columns.

Recommended v1 behavior:

- primary key columns are included by default for audited tables
- excluding all primary key columns for an audited table should throw at configuration time
- redacting primary key values is allowed, but then the consumer knowingly receives a less useful identity

## 8. Audit Policy

Audit should be disabled by default.

The application should explicitly enable the tables it cares about.

### 8.1. Table selection

Suggested configuration:

```csharp
database.MutationAudit.Configure(options => options
    .AuditTable<Employee>()
    .AuditTable<Salary>()
    .IgnoreTable<Session>()
    .IgnoreTable<BackgroundJobHeartbeat>());
```

Alternative for broad systems:

```csharp
database.MutationAudit.Configure(options => options
    .AuditAllTables()
    .IgnoreTable<Session>()
    .IgnoreTable<CacheEntry>());
```

`AuditAllTables()` is convenient, but it should not be the default. It is too easy to leak sensitive data or create a write-amplification problem.

### 8.2. Column selection

Suggested configuration:

```csharp
database.MutationAudit.Configure(options => options
    .AuditTable<Employee>(table => table
        .Exclude(x => x.password_hash)
        .Exclude(x => x.reset_token)
        .Redact(x => x.ssn))
    .AuditTable<Salary>(table => table
        .Include(x => x.emp_no)
        .Include(x => x.salary)
        .Include(x => x.from_date)
        .Include(x => x.to_date)));
```

Column modes:

- include all columns except exclusions
- include only selected columns
- redact selected columns
- reject unsafe primary-key exclusion unless explicitly overridden

Redaction and exclusion are different:

- excluded means the column is absent
- redacted means the column is present but the value is not exposed

### 8.3. Value limits

Audit configuration should include value-size limits.

Sketch:

```csharp
public sealed record MutationAuditOptions
{
    public int MaxEntriesPerBatch { get; init; } = 10_000;
    public int MaxValueBytes { get; init; } = 64 * 1024;
    public MutationAuditLargeBatchBehavior LargeBatchBehavior { get; init; } =
        MutationAuditLargeBatchBehavior.ThrowBeforeExecute;
}
```

Large audited mutations should fail before modifying data unless the application explicitly chooses a large-batch strategy. Silently truncating audit rows would be dishonest.

### 8.4. Sensitive metadata

Later, generated metadata can support attributes such as:

```csharp
[AuditIgnore]
public string password_hash { get; }

[AuditRedact]
public string ssn { get; }
```

Runtime configuration should still win. Attributes are a useful default, not a substitute for application policy.

## 9. Application Context

Applications need to attach context such as the logged-in user.

This should be explicit first.

### 9.1. Commit-time context

Primary API:

```csharp
transaction.Commit(MutationAuditContext.From(
    ("actor.user_id", currentUser.Id),
    ("actor.name", currentUser.DisplayName),
    ("tenant.id", tenantId),
    ("request.id", requestId)));
```

This is the cleanest API because it makes the audit context part of the commit operation.

### 9.2. Transaction context bag

Also useful:

```csharp
using var transaction = database.Transaction();

transaction.AuditContext.Set("actor.user_id", currentUser.Id);
transaction.AuditContext.Set("request.id", requestId);

transaction.Update(employee);
transaction.Commit();
```

This helps when lower layers perform mutations but the code that knows user context is higher up. The context bag must be snapshotted at commit.

If both transaction context and commit-time context are supplied, commit-time values should override transaction context values with the same key.

### 9.3. Ambient context

An ambient context can be convenient in web applications:

```csharp
using var auditScope = database.MutationAudit.BeginContext(
    MutationAuditContext.From(
        ("actor.user_id", currentUser.Id),
        ("request.id", requestId)));

database.Commit(transaction =>
{
    transaction.Update(employee);
});
```

But ambient context must be treated as a convenience layer, not the core contract.

Rules:

- use `AsyncLocal`, not `ThreadLocal`, if ambient context is added
- explicit transaction context overrides ambient context
- explicit commit context overrides both
- ambient context should be snapshotted into the transaction, not looked up lazily by subscribers
- docs must warn that background jobs and request pipelines should prefer explicit context where possible

The reason is simple: "thread-level" state is wrong in modern async server code. Async-local state is better, but explicit commit context is still the least surprising API.

### 9.4. Context value shape

Context values should be simple immutable values:

- string
- numeric primitives
- bool
- `Guid`
- `DateTimeOffset`
- null

Do not encourage passing `ClaimsPrincipal`, HTTP context objects, ORM entities, or mutable application services. Audit context should be serializable without retaining the application object graph.

## 10. Capture Semantics

### 10.1. Insert capture

For audited inserts:

1. execute insert
2. hydrate generated keys/defaults/final row
3. build `After` row image from hydrated row
4. buffer audit entry inside transaction
5. emit after commit

If provider batching uses `RETURNING`, use returned rows where reliable.

If provider batching requires follow-up select, use that select before the mutation API returns its immutable row.

### 10.2. Update capture

For audited updates:

1. normalize assigned changes against trusted baseline
2. if no actual changes remain, no-op
3. capture full `Before` image from trusted baseline or pre-update select
4. execute update
5. hydrate full `After` image
6. compute `Changes` by comparing included before/after columns
7. buffer audit entry
8. emit after commit

The comparison should use DataLinq value semantics after provider/model conversion. `1` and `1L`, provider-specific date precision, enum converters, and nullable normalization must not create fake changes.

### 10.3. Delete capture

For audited deletes:

1. capture full `Before` image from trusted baseline or pre-delete select
2. execute delete
3. buffer audit entry
4. emit after commit

If the row does not exist, the mutation path should already report the failed delete according to normal mutation semantics. Audit should not invent a delete entry for a row that was not deleted.

### 10.4. Relation-aware graph capture

Graph saves should emit entries in logical execution order.

Example:

```csharp
var employee = new MutableEmployee(...);

employee.salaries.Insert(new MutableSalary { salary = 75000 });
employee.titles.Insert(new MutableTitle { title = "Engineer" });

employee.Save(database);
```

Expected audit sequence:

1. employee insert
2. salary insert with propagated employee key
3. title insert with propagated employee key

The audit entries must include final foreign key values after key propagation. That means graph audit capture belongs after relation-aware planning and hydration, not before.

### 10.5. Batched mutation capture

Batching changes physical execution. It must not change audit semantics.

Rules:

- assign audit sequence numbers from the mutation plan
- preserve logical order in the emitted batch
- build row images after each operation has the data it needs
- do not let provider command grouping reorder public audit entries

For a batch insert returning rows in provider order, DataLinq must map returned rows back to planned operations deterministically.

## 11. Set-Based Mutation Capture

Set-based mutations are the painful part.

If a table is audited, DataLinq must provide row-level audit data for set-based mutations on that table. It cannot emit "updated 500 rows" and call that a full audit log.

### 11.1. Set-based update

For audited `UpdateMany()`:

1. translate the source query
2. select affected primary keys and full audited before images inside the transaction
3. execute the set-based update
4. select full audited after images by captured primary keys
5. compare before/after images
6. emit one entry per actually changed row

Possible optimization:

- if assignments are constants
- and provider conversion is deterministic
- and no generated columns/triggers/database-side computed values are involved
- and all audited after values can be derived safely

then DataLinq may avoid the post-update select for some columns.

That optimization should not be v1. The honest v1 path is select before, update, select after.

### 11.2. Set-based delete

For audited `DeleteMany()`:

1. select affected primary keys and full audited before images inside the transaction
2. execute delete
3. emit one delete entry per deleted row

There is no after image.

If the provider reports fewer affected rows than captured before rows, DataLinq must report the mismatch according to mutation execution semantics. It must not silently emit delete audit entries for rows that did not delete.

### 11.3. Set-based insert

If DataLinq later supports set-based insert or insert-from-query:

1. execute insert
2. hydrate inserted rows by `RETURNING` or deterministic key capture
3. emit one insert entry per inserted row

Generated keys make this provider-sensitive. If DataLinq cannot map inserted rows back to operation results, audited set-based insert should fall back to a safer row-by-row path or throw before executing.

### 11.4. Large set operations

Audited set-based mutations can be expensive.

That is unavoidable. If the application says "audit this table", DataLinq must produce the data or refuse before changing data.

Policy options:

```csharp
public enum MutationAuditLargeBatchBehavior
{
    ThrowBeforeExecute,
    AllowInMemory,
    AllowProviderTempStorage
}
```

V1 should support `ThrowBeforeExecute` and bounded in-memory capture. Provider temp storage can wait.

## 12. Ordering

Ordering must be deterministic.

For normal row mutations:

- order follows mutation API call order

For enumerable row mutations:

- order follows enumerable materialization order after DataLinq snapshots the enumerable

For relation-aware graph mutations:

- order follows the mutation planner's logical execution order

For set-based mutations:

- order follows primary key order from DataLinq's captured affected-row set

For batched provider execution:

- order still follows DataLinq's logical sequence, not provider result order

Each audit entry should include a `Sequence` number that is unique within the batch.

## 13. Delivery Semantics

### 13.1. Emit after commit

Commit flow should become:

1. flush pending mutation work
2. collect complete audit row images for audited operations
3. commit provider transaction
4. promote transaction-local cache state
5. reset/promote mutable baselines
6. snapshot audit context
7. emit audit batch to subscribers
8. dispose or close transaction resources normally

Steps 2 and 3 may need to share captured data, but subscribers must not run until step 7.

### 13.2. Empty batches

If a transaction commits but contains no audited entries, emit nothing.

Examples:

- no mutations
- only non-audited tables changed
- only no-op updates were requested

### 13.3. Subscriber failures

Subscriber failure happens after the data transaction has committed. DataLinq cannot roll it back.

Therefore subscriber failure must never be reported as "the transaction failed".

Recommended behavior:

- catch subscriber exceptions
- report them through a configured audit delivery error handler
- record diagnostics/telemetry
- optionally support a `ThrowAfterCommit` delivery mode whose exception type clearly states that the database commit succeeded

Sketch:

```csharp
public sealed record MutationAuditDeliveryOptions
{
    public MutationAuditSubscriberFailureBehavior SubscriberFailureBehavior { get; init; } =
        MutationAuditSubscriberFailureBehavior.ReportAndContinue;

    public Action<MutationAuditDeliveryFailure>? OnDeliveryFailure { get; init; }
}
```

```csharp
public enum MutationAuditSubscriberFailureBehavior
{
    ReportAndContinue,
    ThrowAfterCommit
}
```

`ThrowAfterCommit` is useful for tests and strict applications, but it is dangerous if callers interpret any `Commit()` exception as rollback. The exception message and type must make the committed state unambiguous.

### 13.4. Subscriber reentrancy

Subscribers should be allowed to use other services, but DataLinq should document that re-entering the same provider from an audit subscriber can create confusing ordering and recursion.

If a subscriber writes audit rows through the same DataLinq provider, those audit-table mutations should not recursively generate audit events unless explicitly configured. The safer default is to suppress audit emission while delivering an audit batch.

## 14. Row Image Truthfulness

Audit row images must be honest about what they represent.

### 14.1. DataLinq-observed truth

Without optimistic concurrency, DataLinq can say:

> This is the before image DataLinq observed from the mutable baseline or from a pre-write read, and this is the after image DataLinq hydrated after the write.

It cannot always say:

> No outside writer changed this row between the original read and the update.

That stronger claim belongs to optimistic concurrency.

### 14.2. Trusted baseline

A mutable baseline is trusted when it satisfies the mutable lifecycle plan:

- committed baseline
- transaction-local baseline from the same active transaction
- not rolled back
- not failed
- not from a different active transaction

If the baseline is trusted and complete, it can supply the update/delete before image.

If it is not trusted or not complete, audited mutation should fetch a before image or throw before executing.

### 14.3. Trigger and default effects

Audit should include database-visible effects that DataLinq hydrates:

- generated primary keys
- default values
- generated columns
- provider conversion normalization
- trigger-updated values visible after reload

If a trigger writes to a different table and DataLinq does not observe that table as part of the mutation, DataLinq should not invent audit entries for that table.

## 15. Cache and Mutable Lifecycle Interaction

Audit capture must not corrupt mutation lifecycle state.

Rules:

- capture row images before mutable reset loses old values
- reset mutables only after mutation success
- rollback discards buffered audit entries
- commit promotion preserves row images already captured
- cache invalidation and audit should share mutation planning, but audit should not rely on cache invalidation DTOs as its public contract

The most likely implementation mistake is resetting a mutable before the audit entry has captured its trusted before image.

## 16. Provider Strategy

Provider support should be capability-driven.

Capabilities:

```csharp
public sealed record MutationAuditProviderCapabilities(
    bool CanReturnInsertedRows,
    bool CanReturnUpdatedRows,
    bool CanReturnDeletedRows,
    bool CanSelectRowsForAudit,
    bool SupportsStableBulkKeyCapture);
```

General strategy:

- use `RETURNING` where reliable
- use transaction-local follow-up selects where needed
- use pre-write selects for audited updates/deletes when before images cannot be supplied from trusted baselines
- fall back to row-by-row execution or throw before executing if a provider cannot produce correct audited data

Provider differences must change performance, not public audit semantics.

## 17. Public Documentation Requirements

When implemented, update:

- `docs/Caching and Mutation.md`
- `docs/Transactions.md`
- `docs/Diagnostics and Metrics.md`
- `docs/Troubleshooting.md`
- generated XML docs for audit configuration, subscription, context, and commit overloads

Public docs must state:

- audit events are emitted only after successful commit
- rollback emits no committed audit events
- no-op updates emit no audit entries
- audit covers DataLinq mutation APIs, not arbitrary external database writes
- tables and columns must be configured explicitly
- excluded columns are absent
- redacted columns reveal that a value changed but not the value itself
- set-based mutations on audited tables may require extra reads and may be expensive
- subscriber failures happen after commit and cannot roll back committed data
- application context should be supplied explicitly when possible

## 18. Testing Requirements

Add compliance tests covering:

- insert emits one committed batch after commit
- insert emits no batch before commit
- rollback emits no batch
- update includes full before and after row images
- update includes changed column before/after values
- assignment to same value is normalized to no-op and emits no event
- delete includes full before row image
- dirty mutable delete logs persisted before image, not unsaved local changes
- table not configured for audit emits no entries
- excluded column is absent from row images and changes
- redacted column appears without values
- primary-key exclusion is rejected unless explicitly allowed
- commit-time context appears in batch
- transaction context appears in batch
- commit-time context overrides transaction context
- ambient context is snapshotted when used
- multiple row mutations preserve order
- enumerable mutations preserve materialized enumerable order
- relation-aware graph save logs propagated foreign keys
- batched provider execution preserves logical audit order
- set-based update on audited table captures before and after rows
- set-based delete on audited table captures before rows
- large audited set mutation throws before executing when over configured limit
- subscriber exception is reported as post-commit delivery failure
- audit delivery does not recursively audit itself by default

## 19. Implementation Phases

### Phase 1: DTOs and Configuration

1. Add audit DTOs.
2. Add audit policy builder.
3. Add table and column include/exclude/redaction configuration.
4. Add audit context type.
5. Add subscription manager with strong subscriptions.

### Phase 2: Row Mutation Capture

1. Capture insert after images.
2. Capture update before and after images.
3. Capture delete before images.
4. Normalize no-op updates before SQL.
5. Buffer entries on the transaction.

### Phase 3: Commit Emission

1. Add commit overload accepting `MutationAuditContext`.
2. Add transaction audit context bag.
3. Emit after successful commit.
4. Discard on rollback/dispose.
5. Add subscriber failure handling.

### Phase 4: Relation-Aware and Batched Integration

1. Assign sequence numbers from mutation plans.
2. Preserve logical order across provider batching.
3. Capture propagated foreign keys after graph planning.
4. Map provider-returned rows back to planned operations.

### Phase 5: Set-Based Mutation Audit

1. Capture affected primary keys and before images.
2. Capture after images for audited set updates.
3. Emit one row entry per changed row.
4. Add large-batch guardrails.

### Phase 6: Documentation and Diagnostics

1. Document public semantics.
2. Add telemetry for batches, entries, skipped entries, capture reads, and delivery failures.
3. Add troubleshooting docs for missing audit entries and subscriber failures.

## 20. Implementation Checklist

- [ ] Add `MutationAuditBatch`.
- [ ] Add `MutationAuditEntry`.
- [ ] Add `MutationAuditRowImage`.
- [ ] Add `MutationAuditColumnValue`.
- [ ] Add `MutationAuditColumnChange`.
- [ ] Add `MutationAuditContext`.
- [ ] Add audit policy configuration.
- [ ] Add table include/exclude support.
- [ ] Add column include/exclude/redaction support.
- [ ] Add strong subscription manager.
- [ ] Add transaction audit buffer.
- [ ] Add commit-time context overload.
- [ ] Add transaction context bag.
- [ ] Add optional ambient context scope.
- [ ] Normalize no-op updates before SQL.
- [ ] Capture insert after images.
- [ ] Capture update before/after images.
- [ ] Capture delete before images.
- [ ] Emit only after successful commit.
- [ ] Discard on rollback.
- [ ] Add subscriber failure handling.
- [ ] Integrate relation-aware graph sequence ordering.
- [ ] Integrate batched mutation row mapping.
- [ ] Add audited set-based update/delete capture.
- [ ] Add large audited mutation guardrails.
- [ ] Add compliance tests.
- [ ] Update public docs after behavior lands.
