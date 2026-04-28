> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: SQL Generation Optimization

**Status:** Partially implemented by Roadmap Phase 3; remaining ideas require new evidence.
**Goal:** Minimize (and eventually eliminate) heap allocations during the SQL generation phase of a query. By treating SQL generation as a "hot path," we reduce Garbage Collector pressure and improve overall throughput.

## Current Implementation State

Roadmap Phase 3 implemented the parts of this plan that had clear benchmark leverage:

- generated SQL now carries provider-neutral `SqlParameterBinding` values until SQLite/MySQL command creation
- SQL rendering removed several avoidable LINQ, string-join, and formatting allocations
- repeated equality and fixed-slot `IN` SELECT shapes can reuse a bounded SQL template cache
- `QueryExecutor` no longer uses result-operator display strings to decide common LINQ operator behavior
- the `phase3-query-hotpath` benchmark lane tracks allocation and timing changes for repeated query execution

The useful Phase 3 claim is lower allocation pressure on measured repeated query paths. Local timing remained noisy enough that wall-clock speedup should not be treated as proven.

What deliberately did not land:

- a custom `ValueStringBuilder`
- broad `readonly struct` conversion of the query tree
- a universal SQL structural hash
- dependency-tracked result-set caching

Those ideas are not dead, but they should stay behind benchmark evidence. The Phase 3 result says the boring seams paid off first: parameter boundary, rendering loops, and narrow template reuse.

---

## 1. The "ValueStringBuilder" Strategy

Standard `StringBuilder` allocates objects on the heap and resizes internal arrays, creating GC pressure. We will replace it with a stack-allocated string builder.

### 1.1. Implementation
Introduce `DataLinq.Utils.ValueStringBuilder`.
*   **Type:** `ref struct`
*   **Backing:** `Span<char>` (initially stack-allocated) falling back to `ArrayPool<char>.Shared` if the string grows too large.
*   **Behavior:** Similar API to `StringBuilder` (`Append`, `AppendFormat`), but zero-allocation for typical query sizes (< 256 chars).

### 1.2. Usage
All `ToSql()` methods in the `Query` namespace will be refactored to accept `ref ValueStringBuilder` instead of returning `string` or modifying a `StringBuilder`.

```csharp
// Before
public string ToSql() { ... }

// After
public void WriteSql(ref ValueStringBuilder builder) { ... }
```

---

## 2. Struct-ifying Query Components

Currently, `Where`, `OrderBy`, and `Join` are classes. Adding conditions to a query allocates multiple small objects, fragmenting the heap and hurting memory locality.

### 2.1. The Shift to Structs
Convert the following classes to `readonly record struct`:
*   `OrderBy`
*   `Join`
*   `Where` (and `Comparison`, `Operand`)

### 2.2. List Storage
The `SqlQuery` class currently holds `List<OrderBy>`, `List<Join>`, etc.
*   **Benefit:** `List<Struct>` is a dense array of data in memory. Iterating it is CPU-cache friendly.
*   **Constraint:** We must avoid casting these structs to interfaces (like `IQueryPart`) in hot paths to prevent Boxing. Concrete types should be preferred internally.

---

## 3. Parameter Optimization

Currently, `Sql` holds `List<IDataParameter>`. This is inefficient because:
1.  `List<T>` allocates a wrapper.
2.  `IDataParameter` implementations (`MySqlParameter`, `SqliteParameter`) are classes (heap allocation).
3.  We are creating these objects early, only for the ADO.NET provider to read them and potentially copy them into its own internal structure.

### 3.1. Raw Value Storage
Refactor the `Sql` (or `QueryBindings`) class to store **Raw Values** (`List<object>`) instead of `IDataParameter` objects.

### 3.2. Just-in-Time Parameter Creation
The `IDatabaseProvider` (e.g., `SQLiteProvider`) is responsible for iterating the `List<object>` and creating the actual `SqliteParameter` objects immediately before execution `command.Parameters.AddWithValue(...)`.

This removes the "middleman" objects that exist solely to transport data from the Query Builder to the Provider.

---

## 4. The Template & Binding Architecture

Currently, every execution of a LINQ query regenerates the SQL string from scratch, even if the query structure hasn't changed (e.g., `Get(1)` vs `Get(2)`).

We will split `SqlQuery` generation into two phases:

### 4.1. Phase 1: The Template (Cacheable)
Generate the SQL string *with placeholders* for parameters.
*   **Input:** Query Structure (`Where Id = ?`)
*   **Output:** `SqlTemplate` (immutable string: `SELECT * FROM Users WHERE Id = @p0`)
*   **Cache Key:** A structural hash of the query components (e.g., "Select-Users-Where-Eq").

### 4.2. Phase 2: The Bindings (Dynamic)
Extract only the values needed for the placeholders.
*   **Input:** The specific `Where` clause values.
*   **Output:** `QueryBindings` (`object[] { 1 }`).

### 4.3. Execution Flow
1.  **Compute Structural Hash.**
2.  **Check Template Cache.**
    *   *Hit:* Retrieve SQL string.
    *   *Miss:* Generate SQL using `ValueStringBuilder` and store in cache.
3.  **Extract Values:** Populate `List<object>`.
4.  **Execute:** Pass SQL + Values to Provider.

## 5. Implementation Steps

1.  [ ] Implement `DataLinq.Utils.ValueStringBuilder`.
2.  [ ] Refactor `SqlQuery` and `Where`/`Join`/`OrderBy` to use `ValueStringBuilder`.
3.  [ ] Convert `OrderBy`, `Join`, `Comparison` to `readonly record struct`.
4.  [x] Refactor generated SQL bindings so `Sql` no longer requires provider-specific `IDataParameter` objects for normal query generation.
5.  [x] Update Providers (`SQLite`, `MySQL`) to materialize provider parameters at command creation time.
6.  [x] Implement bounded template caching for narrow repeated equality and fixed-slot `IN` SELECT shapes.
