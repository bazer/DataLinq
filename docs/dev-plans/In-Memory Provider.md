# Specification: In-Memory Database Provider

**Status:** Draft
**Goal:** Implement a fully functional, ACID-compliant In-Memory database provider. This allows for ultra-fast unit testing, transient session storage, and enables the DataLinq Cache to effectively act as a database engine itself.

---

## 1. Architecture

The In-Memory provider treats the database state as a **Persistent Immutable Data Structure**.

### 1.1. Storage Structure
The data is not stored in `List<T>`. It is stored in immutable trees to support snapshot isolation and lock-free reads.

```csharp
internal class InMemoryTableState
{
    // Primary Storage: PK -> Row
    public ImmutableDictionary<IKey, IImmutableInstance> Rows { get; init; }
    
    // Secondary Indices: IndexName -> (Value -> Set of PKs)
    public ImmutableDictionary<string, ImmutableDictionary<object, ImmutableHashSet<IKey>>> Indices { get; init; }
    
    // Metadata
    public long AutoIncrementCounter { get; init; }
}
```

### 1.2. The Global State
The `InMemoryDatabase` holds a `ConcurrentDictionary<string, InMemoryTableState>` (one per table).
Alternatively, for full snapshotting support, the entire Database can be one `ImmutableDictionary<TableName, TableState>`, allowing you to snapshot the *entire* DB in one pointer swap.

---

## 2. Transaction Model

We implement **Snapshot Isolation** with **Pessimistic Write Locking**.

### 2.1. Read Path (Lock-Free)
1.  Reader grabs the current `InMemoryTableState` reference.
2.  All lookups happen against this immutable reference.
3.  Zero locking required.

### 2.2. Write Path (Transactions)
1.  **Begin:** Transaction acquires a `ReaderWriterLockSlim` (Write mode) on the Table (or DB).
2.  **Snapshot:** Transaction reads the `CurrentState`.
3.  **Mutate:**
    *   Insert/Update/Delete operations return a *new* `InMemoryTableState` (Copy-on-Write).
    *   Indices are updated synchronously with the rows in the new state.
4.  **Commit:** The global reference `_currentState` is replaced with `_newState`.
5.  **Release:** Lock released.

*Note:* While Optimistic Concurrency (CAS loop) is possible, a Write Lock is simpler to implement correctly for complex multi-table transactions and prevents livelock under high contention.

---

## 3. Indexing Strategy

Without indices, every query is a full table scan (O(N)).

*   **Primary Key:** O(1) lookup via `Rows` dictionary.
*   **Secondary Indices:** Defined by `[Index]` attributes. Maintained in the `Indices` dictionary.
*   **Lookup:** The `InMemoryQueryTranslator` checks if the `WHERE` clause targets an indexed column. If so, it fetches the `IKey`s from the index and then looks up the rows.

---

## 4. Data Lifecycle

### 4.1. Deduplication
This provider will heavily leverage the **Memory Optimization** spec. All data entering `InMemoryTableState` will pass through the `ValueStore` interner.

### 4.2. Disposal
When the `InMemoryDatabase` instance is disposed, all references to the Root Immutable Dictionaries are dropped. The GC reclaims the entire database. No special cleanup file I/O is needed.

## 5. Implementation Steps

1.  [ ] Create `DataLinq.Memory` project.
2.  [ ] Implement `InMemoryTableState` using `System.Collections.Immutable`.
3.  [ ] Implement `InMemoryTransaction` handling the Write Lock and State Swap.
4.  [ ] Implement `InMemoryQueryTranslator` (The Interpreter) to filter rows.
5.  [ ] Implement Index maintenance on Insert/Update/Delete.
6.  [ ] Hook up `KeyFactory` for deduplicated keys.
```