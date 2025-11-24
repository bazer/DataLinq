# Specification: Memory Optimization and Deduplication

**Status:** Draft
**Goal:** Drastically reduce the memory footprint of the DataLinq cache by altering the internal structure of `RowData` and implementing intelligent value deduplication (interning).

---

## 1. Problem Statement

Currently, `RowData` operates conceptually like a `Dictionary<ColumnDefinition, object>`. While flexible, this incurs significant overhead:
1.  **Structural Overhead:** A dictionary has internal arrays (buckets, entries) and object headers. For a table with 1 million rows, we are creating 1 million dictionaries.
2.  **Data Duplication:** Business data is highly repetitive (e.g., Status="Active", Country="US", CreatedBy="User123"). Storing 10,000 copies of the string "Active" wastes heap space.

## 2. Structural Refactoring: The Dense Array

We will move from a sparse/dictionary storage to a dense array storage for `RowData`.

### 2.1. Table Metadata Indexing
The `TableDefinition` must maintain a fixed mapping of columns to array indices. This effectively "compiles" the schema structure.

```csharp
public class TableDefinition
{
    // Existing properties...
    public ColumnDefinition[] ColumnsByIndex { get; private set; }
    public int GetColumnIndex(ColumnDefinition col); // O(1) lookup
}
```

### 2.2. The New `RowData`
`RowData` becomes a lightweight wrapper around an `object[]`.

```csharp
public class RowData : IRowData
{
    private readonly object[] _values;
    private readonly TableDefinition _table;

    public object this[ColumnDefinition column] 
    {
        get => _values[column.Index];
    }
    
    // Used for compaction
    public object[] GetRawValues() => _values;
}
```

*   **Memory Savings:** Reduces per-row overhead from ~64+ bytes (Dictionary) to ~16 bytes (Array).

---

## 3. Value Deduplication (The Interner)

We will implement a "scoped interning" mechanism to deduplicate repetitive values on the heap.

### 3.1. `ValueStore`
A class responsible for holding unique references to values. It effectively acts as a local pool for strings and boxed value types.

```csharp
public class ValueStore
{
    // High-performance dictionary for lookups
    private Dictionary<object, object> _store; 
    
    public object Intern(object value)
    {
        // If value exists, return the stored reference.
        // If not, add and return the incoming value.
    }
}
```

### 3.2. Heuristics (When NOT to Intern)
To prevent "interning fatigue" (wasting CPU on values that will never repeat), we apply rules before attempting to intern:
*   **Type Exclusion:** Never intern `byte[]`, `Guid` (unless stored as string), `float`, `double`.
*   **Length Exclusion:** Do not intern strings > 256 characters (configurable).
*   **Cardinality Check (Advanced):** If the store grows too quickly relative to the row count, temporarily disable interning for that column.

---

## 4. Garbage Collection: Compaction Strategy

Since we cannot use `WeakReference` for every value (too much overhead), we will use a **Mark-and-Sweep** strategy integrated with the existing `CleanCacheWorker`.

### 4.1. The Compaction Cycle
When `CleanCacheWorker` wakes up to prune old rows:

1.  **Prune Rows:** Remove old `RowData` entries from the `TableCache`.
2.  **Check Threshold:** If `ValueStore.Count` > `LiveRowCount * Factor`, trigger compaction.
3.  **Mark:** Iterate over all *live* `RowData` in the cache. Collect all distinct values currently in use into a `HashSet`.
4.  **Sweep:** Rebuild the `ValueStore` dictionary containing *only* the values found in the Mark phase. Drop the old dictionary.
5.  **Result:** The .NET GC collects the dropped dictionary and all the orphaned string/object instances that are no longer referenced by the live cache.

## 5. Implementation Steps

1.  [ ] Add `Index` property to `ColumnDefinition` and populate during metadata parsing.
2.  [ ] Refactor `RowData` to use `object[]`. Update `IDataLinqDataReader` consumers.
3.  [ ] Implement `ValueStore` class with Heuristics.
4.  [ ] Integrate `ValueStore` into `TableCache`.
5.  [ ] Implement `Compact()` method in `TableCache` and hook into `CleanCacheWorker`.