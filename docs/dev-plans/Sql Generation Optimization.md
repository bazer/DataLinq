# Specification: SQL Generation Optimization

**Status:** Draft
**Goal:** Minimize (and eventually eliminate) heap allocations during the SQL generation phase of a query. By treating SQL generation as a "hot path," we reduce Garbage Collector pressure and improve overall throughput.

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
4.  [ ] Refactor `Sql` class to store `List<object>` (values) instead of `IDataParameter`.
5.  [ ] Update Providers (`SQLite`, `MySQL`) to consume raw values.
6.  [ ] (Long Term) Implement Structural Hashing and Template Caching.