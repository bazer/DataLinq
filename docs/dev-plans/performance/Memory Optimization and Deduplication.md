> [!WARNING]
> This document is roadmap or specification material. It may describe planned, experimental, or partially implemented behavior rather than current DataLinq behavior.
# Specification: Memory Optimization and Deduplication

**Status:** Historical design note, updated by Phase 12 measurement. `RowData` is already dense-array backed, and Phase 12 did not adopt production value interning.
**Goal:** Reduce the memory footprint of the DataLinq cache only where measurement proves the change is worth the runtime cost and retention risk.

## Phase 12 Measurement Result

Phase 12 added a dedicated `phase12-cache-memory` benchmark lane and a benchmark-only bounded string-pool probe. The 2026-05-13 `sqlite-memory` default-profile run was noisy, but it was decisive enough for the adoption question:

| Probe | Baseline | Candidate | Decision |
| --- | ---: | ---: | --- |
| Low-cardinality strings | 0.0145 us/op, 51.2 B/op | 0.0462 us/op, 51.2 B/op | Reject bounded string pool. Same allocation, slower. |
| High-cardinality strings | 0.0236 us/op, 51.2 B/op | 0.0532 us/op, 51.2 B/op | Reject bounded string pool. Same allocation, slower. |
| Composite dynamic key creation | 0.0896 us/op, 245.76 B/op | No candidate adopted | Keep as a baseline; do not add component-array pooling without a stronger immutable-key design. |
| Large relation index preload | 14,720.5333 us/op, 1,475,225.6 B/op | No candidate adopted | Keep measuring; do not reuse caller-owned key arrays without a clearer ownership contract. |

The important conclusion is not "dedup is impossible." It is that DataLinq should not ship an interner just because repeated values look tempting. The first bounded string-pool probe failed the allocation gate, and relation/key-array reuse has correctness hazards without a stronger ownership model.

---

## 1. Problem Statement

Historically, this plan assumed `RowData` operated conceptually like a `Dictionary<ColumnDefinition, object>`. That is no longer current: `RowData` now stores values in a dense `object?[]` indexed by `ColumnDefinition.Index`.

The remaining memory problem is retained data duplication: business data can be repetitive (for example, `Status="Active"` or `Country="US"`). Storing many equivalent string instances can waste heap space, but pooling them is only worthwhile if it beats the lookup cost and has a bounded retention story.

## 2. Structural Refactoring: The Dense Array

This part is already implemented. `RowData` uses dense array storage rather than sparse/dictionary storage.

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

### 2.2. Dense `RowData`
`RowData` is a lightweight wrapper around an `object?[]`.

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

The first scoped string-pool prototype was benchmark-only and rejected for production. Future interning work should start from the Phase 12 benchmark lane and beat the documented baseline before it touches runtime cache code.

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

If a future scoped pool is adopted, it will need a cleanup/compaction story. Per-value `WeakReference` remains too expensive for the cache hot path, so the likely strategy is still mark-and-sweep integrated with coordinated cache cleanup.

### 4.1. The Compaction Cycle
If a future pool lands, the compaction cycle should run from the coordinated cache cleanup scheduler:

1.  **Prune Rows:** Remove old `RowData` entries from the `TableCache`.
2.  **Check Threshold:** If `ValueStore.Count` > `LiveRowCount * Factor`, trigger compaction.
3.  **Mark:** Iterate over all *live* `RowData` in the cache. Collect all distinct values currently in use into a `HashSet`.
4.  **Sweep:** Rebuild the `ValueStore` dictionary containing *only* the values found in the Mark phase. Drop the old dictionary.
5.  **Result:** The .NET GC collects the dropped dictionary and all the orphaned string/object instances that are no longer referenced by the live cache.

## 5. Historical Implementation Steps

1.  [x] Add `Index` property to `ColumnDefinition` and populate during metadata parsing.
2.  [x] Refactor `RowData` to use `object?[]`. Update `IDataLinqDataReader` consumers.
3.  [x] Add benchmark-only scoped string-pool probes.
4.  [x] Reject the first bounded string-pool candidate for production based on Phase 12 benchmark evidence.
5.  [ ] Revisit runtime value pooling only if a future candidate beats the Phase 12 allocation and throughput baselines and has a bounded cleanup story.
