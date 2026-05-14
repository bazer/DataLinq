> [!WARNING]
> This document is roadmap or specification material. It describes planned behavior rather than current DataLinq behavior.

# Specification: Batched Mutations

**Status:** Draft / Approved Direction
**Goal:** Reduce mutation round trips and improve write throughput without making returned immutable rows, transaction-local reads, relation caches, or mutable lifecycle state lie.

## 1. Design Position

Batched mutation is worth doing.

DataLinq has a useful advantage here: mutation is explicit. There is no ambient dirty tracker scanning arbitrary objects, so the runtime can plan writes from a known list of explicit operations:

- row `Insert`
- row `Update`
- row `Save`
- row `Delete`
- relation-aware `Insert`, `Update`, `Save`, `Delete`, and `Unlink`

That makes batching realistic.

The danger is pretending that every mutation can be deferred until `Commit()` while still returning fully trustworthy immutable rows from existing APIs. That is not true.

Today `Insert`, `Update`, and `Save` return immutable rows. A returned immutable row should reflect the database row DataLinq just created or updated, including generated primary keys, defaults, converters, trigger-side effects that are visible after a reload, and cache identity. If the method returns before the write reaches the database, DataLinq either has to return a fake immutable row or hide database I/O later behind immutable property access. Both options are bad.

So the rule is:

> Batch aggressively inside semantic barriers, but do not let public immutable rows escape before their required database write and hydration have happened.

## 2. Relationship to Other Plans

This plan depends on:

- [Mutable Instance Lifecycle](Mutable%20Instance%20Lifecycle.md)
- [Relation-Aware Mutation API](Relation-Aware%20Mutation%20API.md)

It also intersects with, but should not be merged into:

- [Set-based mutations](Set-based%20mutations.md)

Batched mutations optimize many explicit row operations. Set-based mutations translate one filtered query into one set-oriented `UPDATE` or `DELETE`. They are different features.

Optimistic concurrency also belongs in a separate plan. This document can prepare the execution pipeline for row-count verification and later concurrency checks, but it should not require ghost hashing or row-version support in the first implementation.

## 3. Current Limitation

The current transaction path executes each mutation immediately.

For a row insert/update/delete:

1. construct a `StateChange`
2. execute the SQL immediately
3. apply transaction-local cache changes
4. reload or fetch the changed row through the transaction cache
5. reset the mutable instance

`Commit()` only commits the provider transaction and merges transaction-local cache changes into global cache.

This is simple and correct, but chatty:

- every row mutation can become its own database command
- multi-row `Insert(IEnumerable<Mutable<T>>)` currently behaves like a loop
- relation-aware graph saves would be inefficient if every planned node executes independently
- generated-key handoff between parent and child rows currently forces users into manual step-by-step code

## 4. Non-Goals

### 4.1. Do not make ordinary `Save()` a fake asynchronous unit-of-work

This should not happen:

```csharp
var saved = transaction.Insert(employee);
// saved looks real, but row is not in the database yet
```

That would break the meaning of the returned immutable instance.

### 4.2. Do not hide I/O inside immutable property getters

A deferred immutable that flushes the transaction when a property is read would be clever and poisonous.

Generated immutable properties should stay cheap value accessors and relation accessors should remain visible relation-loading boundaries. They should not secretly execute a pending write batch.

### 4.3. Do not combine this with ghost-hash optimistic concurrency

The previous version of this document mixed batching and "ghost hash" concurrency. That was too much at once.

First build the mutation planner and batch executor. Then add concurrency checks as a separate layer.

### 4.4. Do not invent graph operations

Batched mutation can optimize explicit relation operations. It must not infer operations by diffing loaded relation collections.

No collection diffing. No silent delete. No magic insert.

## 5. Key Concepts

### Mutation operation

An internal command object representing one explicit operation:

- insert this mutable row
- update this mutable row
- delete this row
- unlink this row by setting FK columns to null
- no-op root row that only carries pending relation operations

### Mutation plan

An ordered plan built from one or more mutation operations.

For simple row saves, this can be a single operation. For relation-aware saves, it can be a dependency graph.

### Batch segment

A contiguous sequence of operations that can be sent to the provider together without violating hydration, generated-key, cache, or transaction-read semantics.

### Flush

Executing all pending operations up to a required barrier.

### Hydration

Loading or returning the database row state after an insert or update so the mutable baseline and returned immutable row are truthful.

### Semantic barrier

A point where DataLinq must flush pending writes before continuing.

Common barriers:

- returning an immutable row from `Insert`, `Update`, or `Save`
- a transaction query or relation read that must see prior transaction-local writes
- a generated key needed by a dependent operation
- explicit `transaction.Flush()`
- `transaction.Commit()`
- raw database access through `transaction.DatabaseAccess`

## 6. Public Semantics

The public mutation APIs should keep their existing meaning.

### 6.1. Single row save

```csharp
var saved = employee.Save(database);
```

The returned row is hydrated before the method returns.

For one row, batching may not improve much. Correctness matters more than trying to defer a single write.

### 6.2. Enumerable save

```csharp
var saved = transaction.Insert(new[]
{
    employee1,
    employee2,
    employee3
});
```

The method can batch internally because it does not return until the whole list is available.

### 6.3. Relation-aware graph save

```csharp
var employee = new MutableEmployee(...);

employee.salaries.Insert(new MutableSalaries { salary = 75000, FromDate = from, ToDate = to });
employee.titles.Insert(new MutableTitles { title = "Engineer", FromDate = from, ToDate = to });

var savedEmployee = employee.Save(database);
```

The method can batch internally because all explicit operations are known before the root `Save()` returns.

### 6.4. Explicit transaction with multiple materializing calls

```csharp
using var transaction = database.Transaction();

var savedEmployee = transaction.Insert(employee);
var savedSalary = transaction.Insert(salary);

transaction.Commit();
```

Each call returns a hydrated immutable row. That means each call is a semantic barrier unless DataLinq can prove the returned immutable row is already truthful.

The first implementation should treat materializing calls as barriers. Later optimization can merge operations inside a call, but should not silently return provisional immutables.

### 6.5. Explicit transaction with graph-aware save

```csharp
using var transaction = database.Transaction();

var savedEmployee = transaction.Save(employeeWithPendingRelationOperations);

transaction.Commit();
```

The graph-aware `Save` can batch its internal operations before returning `savedEmployee`.

This is the main place batching and relation-aware mutation should pay off early.

## 7. Deferred Hydration of Immutables

This is the hardest part, and the honest answer is conservative:

> Public immutable instances should not be deferred-hydrated.

An immutable row returned from mutation should already represent real transaction-local database state. If DataLinq cannot hydrate it yet, it should not return it yet.

### 7.1. Why public deferred immutables are a bad fit

DataLinq immutable instances are not promises. They are row values backed by row data and a data source.

A public deferred immutable would create several problems:

- property access could force hidden writes
- an immutable might need to change after later hydration, which violates the intuitive immutable contract
- object identity in caches would become unstable
- database defaults and triggers could change values after the object was already observed
- transaction rollback would leave previously returned objects representing rows that never existed

That is too much semantic debt.

### 7.2. What can be deferred

Internal operation nodes can be deferred.

Example:

```csharp
employee.salaries.Insert(new MutableSalaries { ... });
employee.titles.Insert(new MutableTitles { ... });

employee.Save(database);
```

Before `Save()` returns, DataLinq can defer and reorder internal operation nodes. The caller has not yet received the related immutable rows, so this is safe.

### 7.3. Hydration boundary

Before a mutation API returns an immutable row, all operations needed for that returned row must be flushed and hydrated.

For root graph save:

- root immutable row must be hydrated before return
- related mutables touched by the graph should also be hydrated and reset before return
- related immutable rows can be available through `GetImmutableInstance()` on their mutables after save

### 7.4. Future non-materializing API option

If DataLinq later wants true transaction-level write-behind, it should use a non-materializing API.

Possible shape:

```csharp
transaction.Stage(employee);
transaction.Stage(salary);

transaction.Commit();
```

or:

```csharp
transaction.Defer(t =>
{
    t.Insert(employee);
    t.Insert(salary);
});
```

But this should be a deliberate API with clear semantics. It should not change the meaning of existing methods that return immutable rows.

This is not a phase-one requirement.

## 8. Flush Barriers

The transaction should maintain a pending mutation plan only until a barrier requires execution.

### 8.1. Materialization barrier

Any API that returns a hydrated immutable row is a barrier for the operations needed to produce that row.

```csharp
var saved = transaction.Save(employee);
```

The row represented by `saved` must be real inside the transaction.

### 8.2. Generated-key barrier

If a dependent operation needs a generated key, the principal insert must execute and hydrate first unless the provider-specific batch can safely capture and reuse the generated key in the same segment.

Example:

```csharp
employee.salaries.Insert(salary);
employee.Save(database);
```

If `employee.emp_no` is auto-increment:

1. insert employee
2. hydrate `employee.emp_no`
3. assign `salary.emp_no`
4. insert salary

### 8.3. Read barrier

Before executing a query through `transaction.Query()`, DataLinq must flush pending writes that could affect the query result or relation state.

Conservative v1 rule:

> Any transaction query flushes all pending writes.

Later optimization can flush by table dependency.

### 8.4. Relation barrier

Before reading a relation that depends on pending writes, DataLinq must flush relevant writes.

Conservative v1 rule:

> Any relation read inside a transaction flushes all pending writes.

This is blunt but correct. It can be refined later using relation metadata.

### 8.5. Raw access barrier

Before exposing `transaction.DatabaseAccess` execution to user code, DataLinq should flush pending writes.

Raw commands are opaque. DataLinq cannot know whether a raw command reads or writes tables affected by pending operations.

### 8.6. Commit barrier

`Commit()` flushes all pending operations before committing the provider transaction.

If flush fails, DataLinq rolls back or leaves the transaction failed according to provider behavior and marks touched mutables according to the mutable lifecycle plan.

## 9. Execution Modes

### 9.1. Immediate mode

Execute each operation exactly as today.

Use this when:

- provider does not support a safe batch strategy for the operation
- operation count is below a threshold
- the method is a single materializing row call
- a barrier appears immediately
- diagnostics or compatibility settings disable batching

Immediate mode is the compatibility fallback.

### 9.2. Call-scoped batch mode

Batch operations discovered inside one public call before the call returns.

Primary targets:

- `Insert(IEnumerable<Mutable<T>>)`
- `Save(IEnumerable<Mutable<T>>)`
- relation-aware `Save(rootMutable)`
- relation-aware `Insert(rootMutable)`
- relation-aware `Update(rootMutable)`
- explicit graph operations inside a transaction call

This is the first implementation target.

### 9.3. Transaction-scoped write-behind mode

Queue operations across separate mutation calls until a barrier.

This mode should be deferred until the call-scoped path is correct.

It is tempting, but it raises the deferred immutable problem. The first version should not enable transaction-scoped write-behind for existing materializing APIs unless those APIs still flush before returning.

### 9.4. Provider bulk mode

Use provider-specific SQL to execute multiple row operations in fewer commands.

Examples:

- multi-row `INSERT`
- multi-statement command plus result-set hydration
- SQLite `RETURNING *`
- MariaDB `INSERT ... RETURNING`
- follow-up `SELECT ... WHERE pk IN (...)` hydration

Provider bulk mode is an executor detail. The public semantics should not vary by provider.

## 10. Planning Algorithm

### 10.1. Gather operations

From the root call:

- include the root row operation
- include pending relation operations
- include dependency operations from relation bindings
- drop no-op updates from SQL execution, but keep their mutables in the graph if they carry relation operations

### 10.2. Validate operations

Before executing SQL:

- reject mutation of views
- reject invalid or rolled-back mutables
- reject duplicate conflicting operations
- reject ordinary primary-key updates
- reject missing required FK values or bindings
- reject unsupported dependency cycles
- reject provider/database mismatch

### 10.3. Build dependencies

Edges include:

- principal insert before dependent insert when dependent FK needs generated key
- principal insert before dependent update when relation binding points to a new principal
- dependent delete or unlink before principal delete when explicitly queued and required by FK constraints

### 10.4. Segment the plan

Split operations into batch segments at:

- generated-key barriers
- hydration barriers
- provider capability boundaries
- table or column shape differences
- relation-cache precision boundaries
- explicit user flush barriers

### 10.5. Execute segments

For each segment:

1. build provider commands
2. execute command or commands
3. capture generated keys and affected rows
4. hydrate rows that require returned immutable state
5. update mutable baselines
6. apply transaction-local cache changes
7. continue to next segment

### 10.6. Commit

After all segments execute:

1. commit provider transaction
2. promote mutable baselines
3. merge transaction-local cache into global cache
4. clear pending operation state

## 11. Batch Shapes

### 11.1. Same-table inserts with explicit keys

When all inserted rows have complete explicit keys and no generated-key dependency, DataLinq can use one multi-row insert.

```sql
INSERT INTO employees (emp_no, birth_date, first_name, last_name, gender, hire_date)
VALUES
  (...),
  (...),
  (...);
```

Hydration options:

- if no server-generated values are relevant, hydrate from inserted values only if the public contract allows it
- safer v1: follow with `SELECT * WHERE pk IN (...)`

The safer v1 path is less clever but keeps immutable rows honest.

### 11.2. Same-table inserts with auto-increment keys

This is provider-specific.

Options:

- execute one insert per row and read each generated key
- use provider-specific returning support when available
- use multi-row insert only if generated key mapping is reliable for the provider and table

Default v1 should prefer correctness over fewer round trips.

### 11.3. Same-table updates with same changed columns

If several updates target the same table and same changed column set, provider executors can batch them.

Possible SQL shape:

```sql
UPDATE employees
SET last_name = CASE emp_no
    WHEN @p0 THEN @v0
    WHEN @p1 THEN @v1
END
WHERE emp_no IN (@p0, @p1);
```

This is not a required v1 optimization.

Safer v1:

- execute individual updates in one command batch where supported
- hydrate affected rows with one `SELECT ... WHERE pk IN (...)`

### 11.4. Deletes

Deletes can batch well when deleting by primary key.

```sql
DELETE FROM salaries
WHERE (emp_no, from_date) IN ((@emp0, @from0), (@emp1, @from1));
```

Provider support for tuple `IN` and parameterization differs. The provider executor should choose the shape.

### 11.5. Mixed graph segments

Relation-aware graph saves naturally split into segments.

Example:

```csharp
var employee = new MutableEmployee(...);
employee.salaries.Insert(new MutableSalaries { ... });
employee.titles.Insert(new MutableTitles { ... });

employee.Save(database);
```

Possible segments:

1. insert employee and hydrate generated key
2. assign child FKs
3. batch insert salaries and titles if provider supports multi-statement batching, otherwise execute per table
4. hydrate children

## 12. Provider Strategy

Provider-specific optimizations should be behind a common executor contract.

### 12.1. SQLite

SQLite supports `RETURNING` on top-level `INSERT`, `UPDATE`, and `DELETE` statements since 3.35.0.

Useful details from SQLite documentation:

- `RETURNING` returns one row for each directly inserted, updated, or deleted row
- it does not report additional changes caused by foreign keys or triggers
- output is accumulated in memory before rows are returned
- it is only available on top-level DML statements, not inside triggers

Implications:

- `RETURNING *` is useful for small and medium batches
- do not use `RETURNING *` blindly for very large batches because output buffering can be expensive
- trigger or FK side effects still require conservative cache invalidation

### 12.2. MySQL

MySQL 8.4 supports multi-row `INSERT` and exposes affected-row counts, `ROW_COUNT()`, and `LAST_INSERT_ID()` patterns, but DataLinq should not assume broad `RETURNING` support for `INSERT`, `UPDATE`, and `DELETE`.

Implications:

- use multi-row insert where generated-key mapping is safe
- use follow-up `SELECT` hydration by primary key
- use multi-statement command batches carefully
- prefer explicit provider tests over clever assumptions

### 12.3. MariaDB

MariaDB supports `INSERT ... RETURNING`, and single-table `DELETE ... RETURNING`. Its current `UPDATE` syntax documentation does not present an equivalent `UPDATE ... RETURNING` clause.

Implications:

- use `INSERT ... RETURNING` where it simplifies hydration
- use `DELETE ... RETURNING` carefully for single-table deletes when pre-delete row data is useful
- use follow-up `SELECT` hydration for updates
- keep MySQL and MariaDB executors separate where returning support diverges

## 13. Provider Executor Contract

Add a provider-level mutation batch executor.

Sketch:

```csharp
public interface IMutationBatchExecutor
{
    MutationBatchCapabilities Capabilities { get; }

    MutationBatchResult Execute(
        Transaction transaction,
        IReadOnlyList<PlannedMutationSegment> segments);
}
```

Capabilities:

```csharp
public sealed record MutationBatchCapabilities(
    bool SupportsMultiRowInsert,
    bool SupportsInsertReturning,
    bool SupportsUpdateReturning,
    bool SupportsDeleteReturning,
    bool SupportsMultipleResultSets,
    int MaxParameters,
    int MaxRowsPerInsertBatch,
    int MaxStatementsPerCommand);
```

Result:

```csharp
public sealed record MutationBatchResult(
    IReadOnlyList<ExecutedMutationResult> Results,
    int ExecutedCommandCount,
    int AffectedRows);
```

This lets provider logic evolve without embedding provider branching into `Transaction`.

## 14. Cache Semantics

Batching must preserve current cache behavior.

### 14.1. Transaction-local cache

After a segment executes, transaction-local cache state should reflect executed operations.

This is required so relation reads inside the same transaction can see writes that have already crossed a flush barrier.

### 14.2. Pending operations before flush

Pending, unflushed operations should not be visible through normal cache reads unless DataLinq implements a dedicated staged-row cache.

V1 should avoid staged-row public visibility. Flush before reads instead.

### 14.3. Global cache

Global cache changes are applied only after commit.

Rollback discards transaction-local cache state and invalidates touched mutable baselines as specified by the mutable lifecycle plan.

## 15. Affected Rows and Verification

The first implementation should verify basic provider results without pretending to solve full optimistic concurrency.

### 15.1. Inserts

Expected inserted row count should match operation count unless provider returning/hydration proves each row individually.

### 15.2. Updates

No-op updates are dropped before SQL execution.

For non-empty updates:

- affected-row semantics differ across providers and connection settings
- do not treat affected-row `0` as a concurrency failure until concurrency support is explicitly designed
- hydration by primary key after update can verify the row still exists

### 15.3. Deletes

Delete affected-row count can detect missing rows, but whether missing-row delete is an error should be a separate API decision.

For v1, preserve current semantics as closely as possible.

## 16. API Surface

### 16.1. Existing APIs

Keep existing row APIs:

```csharp
transaction.Insert(model);
transaction.Update(model);
transaction.Save(model);
transaction.Delete(model);
```

These can use batch internals inside the current call, but their return contracts do not change.

### 16.2. Enumerable APIs

Expand existing enumerable support:

```csharp
transaction.Insert(IEnumerable<Mutable<T>> models);
transaction.Update(IEnumerable<Mutable<T>> models);
transaction.Save(IEnumerable<Mutable<T>> models);
transaction.Delete(IEnumerable<IModelInstance> models);
```

These should be call-scoped batch candidates.

### 16.3. Explicit flush

Add:

```csharp
transaction.Flush();
```

In v1, `Flush()` may be mostly useful once transaction-scoped write-behind exists. It should still be part of the design vocabulary early.

### 16.4. Optional batch policy

Batching should be configurable.

Sketch:

```csharp
database.Provider.State.Mutations.ConfigureBatching(
    MutationBatchingPolicy.Default with
    {
        Enabled = true,
        MaxRowsPerBatch = 128,
        MaxParametersPerCommand = 1000,
        UseReturningWhenAvailable = true
    });
```

Policy belongs at provider/runtime level, not as attributes on individual model properties.

## 17. Observability

Add mutation batching metrics:

- planned operation count
- executed command count
- batch segment count
- rows per segment
- flush reason
- provider batch strategy
- hydration row count
- fallback-to-immediate count
- batch execution duration

Flush reasons should be explicit:

- `materialization`
- `generated_key`
- `transaction_query`
- `relation_read`
- `raw_access`
- `explicit_flush`
- `commit`
- `provider_fallback`

## 18. Implementation Phases

### Phase 1: Planner Foundation

1. Introduce mutation operation nodes.
2. Preserve immediate execution through the new plan abstraction.
3. Add validation and no-op update dropping.
4. Add metrics for planned versus executed mutations.

### Phase 2: Call-Scoped Enumerable Batching

1. Batch `Insert(IEnumerable<Mutable<T>>)` by table.
2. Add `Update(IEnumerable<Mutable<T>>)` and `Save(IEnumerable<Mutable<T>>)` if missing.
3. Hydrate rows before returning lists.
4. Preserve provider parity.

### Phase 3: Relation-Aware Graph Batching

1. Integrate with relation-aware mutation graph planning.
2. Segment by generated-key and dependency barriers.
3. Batch independent child operations.
4. Hydrate all touched mutables before root save returns.

### Phase 4: Provider-Specific Bulk Executors

1. SQLite `RETURNING` executor for bounded batches.
2. MariaDB `INSERT ... RETURNING` executor.
3. MySQL/MariaDB follow-up hydration executor.
4. Provider limits and fallback behavior.

### Phase 5: Transaction-Scoped Write-Behind

Only after previous phases are correct:

1. add pending operation queues across public calls
2. add conservative flush-before-read behavior
3. keep materializing row APIs honest
4. consider separate non-materializing staging APIs if needed

### Phase 6: Concurrency Layer

Separate follow-up:

1. row-version attribute
2. concurrency exception type
3. provider row-count handling
4. optional hash-based strategy if still justified

## 19. Testing Requirements

Add compliance tests for SQLite, MySQL, and MariaDB covering:

- enumerable insert returns hydrated rows
- enumerable insert preserves generated keys
- enumerable update drops no-op updates
- enumerable update returns hydrated rows
- enumerable save mixes new and existing rows
- relation-aware parent plus children save hydrates parent before children
- graph save batches independent children after generated-key barrier
- transaction query after pending writes sees flushed writes
- relation read after pending writes sees flushed writes
- raw transaction access flushes pending writes first
- rollback invalidates all touched mutables after batched execution
- commit merges transaction-local cache after batched execution
- provider fallback preserves behavior when batch executor declines a segment
- SQLite returning path hydrates rows correctly
- MariaDB insert returning path hydrates rows correctly where available
- MySQL follow-up select hydration path hydrates rows correctly
- trigger/default smoke tests prove returned immutable rows are hydrated, not provisional
- metrics report planned operations, executed commands, segments, and flush reasons

## 20. Public Documentation Requirements

When implemented, update:

- `docs/Caching and Mutation.md`
- `docs/Transactions.md`
- `docs/Diagnostics and Metrics.md`
- `docs/Troubleshooting.md`
- generated XML docs for enumerable mutation APIs and `Flush()`

The public docs should state:

- batching preserves normal `Save`, `Insert`, `Update`, and `Delete` semantics
- returned immutable rows are hydrated before they are returned
- graph-aware saves may batch internal operations
- transaction reads can flush pending writes
- no-op updates are dropped
- batching is provider-aware and may fall back to immediate execution
- batching is not optimistic concurrency by itself

## 21. References

- SQLite `RETURNING` documentation: <https://www.sqlite.org/lang_returning.html>
- MariaDB `INSERT` documentation: <https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/inserting-loading-data/insert>
- MariaDB `DELETE` documentation: <https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/changing-deleting-data/delete>
- MariaDB `UPDATE` documentation: <https://mariadb.com/docs/server/reference/sql-statements/data-manipulation/changing-deleting-data/update>
- MySQL 8.4 `INSERT` documentation: <https://dev.mysql.com/doc/en/insert.html>
- MySQL 8.4 `DELETE` documentation: <https://dev.mysql.com/doc/refman/8.4/en/delete.html>

## 22. Implementation Checklist

- [ ] Replace direct `StateChange.ExecuteQuery(...)` calls with mutation planner execution.
- [ ] Add mutation operation and segment types.
- [ ] Add provider batch capabilities.
- [ ] Add provider batch executor abstraction.
- [ ] Preserve immediate mode as fallback.
- [ ] Add enumerable update/save/delete APIs if missing.
- [ ] Implement call-scoped same-table insert batching.
- [ ] Implement call-scoped hydration before return.
- [ ] Integrate graph-aware mutation plans.
- [ ] Add flush barriers for queries, relation reads, raw access, explicit flush, and commit.
- [ ] Add batching diagnostics and metrics.
- [ ] Add provider compliance tests.
- [ ] Update public docs after behavior lands.
