> [!WARNING]
> This document is roadmap or specification material. It describes planned behavior rather than current DataLinq behavior.

# Specification: Set-Based Mutations

**Status:** Draft
**Goal:** Add cache-aware `UPDATE` and `DELETE` operations for many rows while keeping the API close to DataLinq's current LINQ and mutation style.

Set-based mutations are different from batched row mutations. Batched mutations optimize many existing `Insert`, `Update`, and `Delete` calls over individual mutable instances. Set-based mutations translate one filtered query into one set-oriented database operation:

```sql
UPDATE agents
SET reachable = true
WHERE tenant_id = 42;
```

The hard part is not generating this SQL. The hard part is preserving DataLinq's cache guarantees after rows are changed without materializing and mutating every row as a normal object.

---

## 1. Current Limitation

Today, callers can update or delete many rows only by dropping down to:

- the fluent SQL API
- raw strings through `DatabaseAccess`
- provider-specific commands

Those paths bypass the mutation pipeline, so DataLinq cannot reliably invalidate row caches, relation caches, and index caches for the affected rows.

The new API should let callers stay inside the DataLinq query and mutation model:

```csharp
transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .MutateMany(x =>
    {
        x.SenasteKommunikation = DateTime.Now;
        x.Antraffbar = true;
    })
    .UpdateMany();
```

---

## 2. API Principles

1. **LINQ selects the rows.**
   The target rows come from a normal DataLinq query.

2. **Mutation syntax should feel familiar.**
   Simple value assignment should look like the existing `Mutate(...)` API.

3. **Bulk mutation objects are not normal mutable models.**
   They must not be accepted by `database.Update(...)`, `transaction.Update(...)`, `Save(...)`, or ordinary row-mutation APIs.

4. **Move mistakes to compile time where practical.**
   If a call is conceptually wrong, prefer a missing overload or incompatible type over a runtime exception.

5. **Getters on bulk mutation recorders throw.**
   Reading from the recorder has no well-defined value because the operation targets many rows.

6. **SQL expression updates use a separate `Set(...)` API.**
   `MutateMany(...)` records concrete values. `Set(...)` translates row-relative SQL expressions.

7. **Global cache invalidation is published after commit.**
   Explicit transactions should see their own changes through transaction-local cache handling, but global cache state should not be invalidated into uncommitted data.

---

## 3. Proposed Public API

### 3.1. Value assignment with `MutateMany`

```csharp
var updateRows = transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .MutateMany(x =>
    {
        x.SenasteKommunikation = DateTime.Now;
    });

updateRows.Antraffbar = true;
var result = updateRows.UpdateMany();
```

The same operation can be written without the initial lambda:

```csharp
var updateRows = transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .MutateMany();

updateRows.SenasteKommunikation = DateTime.Now;
updateRows.Antraffbar = true;

var result = updateRows.UpdateMany();
```

Inline usage remains compact:

```csharp
var result = transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .MutateMany(x =>
    {
        x.SenasteKommunikation = DateTime.Now;
        x.Antraffbar = true;
    })
    .UpdateMany();
```

### 3.2. `SaveMany`

`SaveMany()` can be offered as a symmetry alias for `UpdateMany()`:

```csharp
transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .MutateMany(x => x.Antraffbar = true)
    .SaveMany();
```

For the first version, `SaveMany()` should not imply insert or upsert behavior. The source is a filtered query over existing rows, so the only sensible operation is update.

If DataLinq later adds `MERGE`, upsert, or insert-from-query behavior, that should be a separate API.

### 3.3. Direct terminal update

For callers who do not need to keep setting properties after the initial mutation:

```csharp
transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .UpdateMany(x => x.Antraffbar = true);
```

This is equivalent to:

```csharp
transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .MutateMany(x => x.Antraffbar = true)
    .UpdateMany();
```

### 3.4. Delete directly from the query

`DeleteMany()` belongs directly on the filtered query. There is no useful mutable object for deletes.

```csharp
var result = transaction.Query().Agenter
    .Where(x => x.Antraffbar == false)
    .DeleteMany();
```

Whole-table deletes should require an explicit opt-in:

```csharp
transaction.Query().Agenter
    .DeleteMany(BulkMutationOptions.AllowWholeTable());
```

or a deliberately named API:

```csharp
transaction.Query().Agenter.DeleteAll();
```

The default should reject accidental unfiltered `DeleteMany()`.

---

## 4. Bulk Mutable Recorder Types

The generator should emit a bulk mutable recorder type for each table model.

Example shape:

```csharp
public sealed class BulkMutableAgent
{
    private readonly BulkMutationContext context;

    public DateTime? SenasteKommunikation
    {
        get => throw BulkMutationErrors.GetterNotSupported(nameof(SenasteKommunikation));
        set => context.RecordSet(nameof(SenasteKommunikation), value);
    }

    public bool Antraffbar
    {
        get => throw BulkMutationErrors.GetterNotSupported(nameof(Antraffbar));
        set => context.RecordSet(nameof(Antraffbar), value);
    }

    public BulkMutationResult UpdateMany() => context.UpdateMany();
    public BulkMutationResult SaveMany() => context.SaveMany();
}
```

The recorder intentionally should not inherit from:

- `Mutable<T>`
- generated `MutableAgent`
- `IModelInstance`
- `IMutableInstance`

That makes these compile-time errors:

```csharp
var updateRows = transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .MutateMany();

database.Update(updateRows);      // compile error
transaction.Update(updateRows);   // compile error
```

This matters because `Database.Update(...)` and `Transaction.Update(...)` are row-mutation APIs. Letting a bulk recorder flow into them would make a serious conceptual mistake look valid.

### 4.1. Getter Behavior

This must throw:

```csharp
var updateRows = transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .MutateMany();

updateRows.Antraffbar = !updateRows.Antraffbar;
```

There is no single `Antraffbar` value. The query may match zero, one, or many rows. Silent default reads would corrupt data.

The exception should be explicit:

```text
Bulk mutation properties can only be assigned. Reading 'Antraffbar' is not supported because the mutation targets a set of rows. Use Set(...) for row-relative SQL expressions.
```

---

## 5. `Set(...)` API for Row-Relative SQL Updates

`MutateMany(...)` handles concrete values:

```csharp
query.MutateMany(x => x.Antraffbar = true).UpdateMany();
```

It cannot represent "use the current row value":

```csharp
// This must not be supported by MutateMany.
query.MutateMany(x => x.RetryCount = x.RetryCount + 1).UpdateMany();
```

That is the job of `Set(...)`.

### 5.1. Increment a numeric column

```csharp
transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .Set(x => x.RetryCount, x => x.RetryCount + 1)
    .UpdateMany();
```

MySQL or MariaDB:

```sql
UPDATE `database`.`agents`
SET `retry_count` = `retry_count` + ?v0
WHERE `id` = ?p0;
```

SQLite:

```sql
UPDATE "agents"
SET "retry_count" = "retry_count" + @v0
WHERE "id" = @p0;
```

### 5.2. Assign a concrete value with `Set(...)`

```csharp
transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .Set(x => x.Antraffbar, true)
    .Set(x => x.SenasteKommunikation, DateTime.Now)
    .UpdateMany();
```

SQL:

```sql
UPDATE `database`.`agents`
SET
  `antraffbar` = ?v0,
  `senaste_kommunikation` = ?v1
WHERE `id` = ?p0;
```

This is more verbose than `MutateMany(...)`, but useful when building dynamic updates.

### 5.3. Normalize text

```csharp
transaction.Query().Agenter
    .Where(x => x.Namn != null)
    .Set(x => x.Namn, x => x.Namn.Trim())
    .UpdateMany();
```

MySQL or MariaDB:

```sql
UPDATE `database`.`agents`
SET `namn` = TRIM(`namn`)
WHERE `namn` IS NOT NULL;
```

SQLite:

```sql
UPDATE "agents"
SET "namn" = TRIM("namn")
WHERE "namn" IS NOT NULL;
```

### 5.4. Preserve existing values with `COALESCE`

```csharp
transaction.Query().Agenter
    .Where(x => x.SenasteKommunikation == null)
    .Set(x => x.SenasteKommunikation, x => x.SenasteKommunikation ?? DateTime.Now)
    .UpdateMany();
```

SQL:

```sql
UPDATE `database`.`agents`
SET `senaste_kommunikation` = COALESCE(`senaste_kommunikation`, ?v0)
WHERE `senaste_kommunikation` IS NULL;
```

### 5.5. Combining `MutateMany(...)` and `Set(...)`

The API can allow both styles in one bulk mutation builder:

```csharp
transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .MutateMany(x => x.Antraffbar = true)
    .Set(x => x.RetryCount, x => x.RetryCount + 1)
    .UpdateMany();
```

SQL:

```sql
UPDATE `database`.`agents`
SET
  `antraffbar` = ?v0,
  `retry_count` = `retry_count` + ?v1
WHERE `id` = ?p0;
```

If the same column is assigned more than once, the builder should throw before executing SQL:

```csharp
query
    .MutateMany(x => x.Antraffbar = true)
    .Set(x => x.Antraffbar, false)
    .UpdateMany();
```

Suggested exception:

```text
Column 'antraffbar' was assigned more than once in the same bulk mutation.
```

---

## 6. Result Type

Bulk mutation methods should return a result object, not just an integer.

```csharp
public sealed record BulkMutationResult(
    int MatchedRows,
    int AffectedRows,
    int InvalidatedRows,
    BulkInvalidationMode InvalidationMode);
```

`MatchedRows` and `AffectedRows` are not the same:

- `MatchedRows` is the number of rows DataLinq identified for cache invalidation.
- `AffectedRows` is the database provider's affected-row count.
- `InvalidatedRows` is the number of cached rows or cache entries actually removed.
- `InvalidationMode` states whether invalidation was precise, table-level, or broader.

For updates that set a column to its existing value, a provider may report `AffectedRows = 0` even when rows matched the predicate. DataLinq should still invalidate matched cached rows because cached data may be stale relative to the database.

---

## 7. Cache Invalidation Strategy

The cache invalidation algorithm should be deliberately conservative.

### 7.1. Default execution flow

1. Ensure the operation runs inside a DataLinq transaction.
2. Translate the LINQ source query into a reusable query plan.
3. Select affected primary keys and any relation/index columns needed for invalidation.
4. Execute the set-based `UPDATE` or `DELETE`.
5. Apply transaction-local cache invalidation.
6. On commit, apply global cache invalidation.
7. On rollback, discard transaction-local invalidation state.

This keeps the behavior aligned with the current transaction mutation model: writes inside a transaction can be visible inside that transaction, but global cache state should not be published before commit.

### 7.2. Precise invalidation

For small and medium affected sets, DataLinq can invalidate by primary key.

For update:

- remove affected row-cache entries
- if changed columns intersect relation or index columns, remove affected index entries
- notify relation subscribers for the changed table once

For delete:

- remove affected row-cache entries
- remove affected index entries
- notify relation subscribers for the changed table once

### 7.3. Table-level invalidation

For very large affected sets, tracking every key can be more expensive than clearing the relevant table cache.

Default mode should be `Auto`:

```csharp
public enum BulkInvalidationMode
{
    Auto,
    Precise,
    Table,
    Database
}
```

Suggested default:

- capture keys up to a configurable threshold
- switch to table-level invalidation above that threshold

Table-level invalidation is not a correctness failure. It is often the correct engineering choice when most of a table is changing.

### 7.4. Triggers, cascades, and external effects

DataLinq cannot precisely invalidate changes it did not perform or observe.

If a trigger updates another table, the original bulk mutation predicate does not tell DataLinq which rows changed there. The safe options are:

- explicit `InvalidateAlso<T>()`
- table-level invalidation for known affected tables
- database-level invalidation
- provider-specific `RETURNING` or change-capture support where available

The API should make this explicit:

```csharp
transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .MutateMany(x => x.Antraffbar = false)
    .InvalidateAlso<AgentAudit>()
    .UpdateMany();
```

---

## 8. SQL Generation

### 8.1. Value assignment update

C#:

```csharp
transaction.Query().Agenter
    .Where(x => x.Id == agentId)
    .MutateMany(x =>
    {
        x.SenasteKommunikation = DateTime.Now;
        x.Antraffbar = true;
    })
    .UpdateMany();
```

SQL:

```sql
UPDATE `database`.`agents`
SET
  `senaste_kommunikation` = ?v0,
  `antraffbar` = ?v1
WHERE `id` = ?p0;
```

### 8.2. Delete

C#:

```csharp
transaction.Query().Agenter
    .Where(x => x.Antraffbar == false)
    .DeleteMany();
```

SQL:

```sql
DELETE FROM `database`.`agents`
WHERE `antraffbar` = ?p0;
```

### 8.3. Identity capture for cache invalidation

Provider-neutral implementation can capture keys before mutation:

```sql
SELECT
  `id`,
  `tenant_id`
FROM `database`.`agents`
WHERE `id` = ?p0;
```

Then execute:

```sql
UPDATE `database`.`agents`
SET `antraffbar` = ?v0
WHERE `id` = ?p0;
```

Provider-specific optimizations can improve this later:

- SQLite can use `RETURNING` for `UPDATE` and `DELETE`.
- MySQL or MariaDB can use temporary tables, locked preselects, or provider-specific row capture strategies.

The first implementation should prefer correctness and provider parity over clever SQL.

---

## 9. Constraints for Version 1

The first version should intentionally reject these cases:

- primary-key updates
- relation property mutation
- property reads inside `MutateMany(...)`
- duplicate assignment to the same column
- unfiltered `DeleteMany()` without explicit opt-in
- unfiltered `UpdateMany()` without explicit opt-in
- provider-unsupported `Set(...)` expression shapes

These can be added later with precise tests, but shipping them casually would be reckless.

---

## 10. Implementation Notes

### 10.1. Query translation reuse

The bulk mutation executor needs access to the same query translation used by normal LINQ reads. Today, the LINQ-to-`SqlQuery` path is embedded in query execution. The implementation should extract a reusable query-plan step so both normal reads and set-based mutations translate predicates consistently.

### 10.2. Generated API surface

The source generator should emit model-specific bulk mutation helpers:

```csharp
public static BulkMutableAgent MutateMany(this IQueryable<Agent> source);
public static BulkMutableAgent MutateMany(this IQueryable<Agent> source, Action<BulkMutableAgent> changes);
public static BulkMutationResult UpdateMany(this IQueryable<Agent> source, Action<BulkMutableAgent> changes);
public static BulkMutationBuilder<Agent> Set<TValue>(
    this IQueryable<Agent> source,
    Expression<Func<Agent, TValue>> property,
    TValue value);
public static BulkMutationBuilder<Agent> Set<TValue>(
    this IQueryable<Agent> source,
    Expression<Func<Agent, TValue>> property,
    Expression<Func<Agent, TValue>> expression);
public static BulkMutationResult DeleteMany(this IQueryable<Agent> source);
```

`BulkMutableAgent` should not be assignable to normal mutable APIs.

### 10.3. Runtime API surface

Runtime types should own the provider-neutral behavior:

```csharp
public sealed class BulkMutationContext
{
    public void RecordSet(string propertyName, object? value);
    public BulkMutationResult UpdateMany();
    public BulkMutationResult SaveMany();
}

public sealed class BulkMutationBuilder<T>
{
    public BulkMutationBuilder<T> Set<TValue>(
        Expression<Func<T, TValue>> property,
        TValue value);

    public BulkMutationBuilder<T> Set<TValue>(
        Expression<Func<T, TValue>> property,
        Expression<Func<T, TValue>> expression);

    public BulkMutationResult UpdateMany();
}
```

### 10.4. Testing requirements

Tests should cover:

- `MutateMany(...).UpdateMany()` updates matching rows
- `MutateMany()` followed by property assignments updates matching rows
- `SaveMany()` behaves as update-only alias
- `DeleteMany()` deletes matching rows
- `database.Update(updateRows)` does not compile in generator approval tests
- `transaction.Update(updateRows)` does not compile in generator approval tests
- property getter in a bulk mutation recorder throws
- duplicate assignment throws before SQL execution
- unfiltered update/delete requires explicit opt-in
- row cache invalidation after update
- relation cache invalidation after update
- relation cache invalidation after delete
- table-level invalidation threshold behavior
- provider parity across SQLite, MySQL, and MariaDB

---

## 11. Implementation Checklist

- [ ] Add runtime `BulkMutationResult`.
- [ ] Add runtime `BulkMutationContext`.
- [ ] Add runtime `BulkMutationBuilder<T>`.
- [ ] Extract reusable LINQ query-plan translation for mutation predicates.
- [ ] Generate per-model `BulkMutable{Model}` recorder types.
- [ ] Generate per-model `MutateMany(...)`, `UpdateMany(...)`, `SaveMany(...)`, and `DeleteMany(...)` extension methods.
- [ ] Implement value-assignment `UPDATE`.
- [ ] Implement query-direct `DELETE`.
- [ ] Implement initial `Set(property, value)` support.
- [ ] Implement limited `Set(property, expression)` support.
- [ ] Implement precise cache invalidation by primary key.
- [ ] Implement table-level invalidation fallback.
- [ ] Add transaction-local/global cache lifecycle tests.
- [ ] Add provider compliance tests.
- [ ] Document the shipped subset only after implementation lands.
