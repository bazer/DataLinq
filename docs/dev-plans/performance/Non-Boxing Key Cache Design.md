> [!WARNING]
> This document is planning material. It describes a proposed internal cache/key redesign and should not be read as shipped behavior.

# Non-Boxing Key Cache Design

**Status:** Draft design  
**Created:** 2026-05-11  
**Scope:** key representation, row-cache dictionary keys, relation/index-cache dictionary keys, and primary-key lookup APIs.

## Problem

The current cache key abstraction is allocation-friendly in shape but not in actual runtime behavior. Simple keys such as `IntKey` are value types, but the cache APIs accept `IKey`:

```csharp
public M? Get<M>(IKey key)
```

Passing a value-type key through `IKey` boxes it. The warm primary-key probe confirmed the cost:

| Path | Allocation |
| --- | ---: |
| `Database.Get` with `new IntKey(...)` | 24 B/op |
| `Database.Get` with preboxed `IKey` | 0 B/op |
| `TableCache.GetRow` with preboxed `IKey` | 0 B/op |

The one-operation startup probe showed this as roughly 48 B because the measurement is noisier at that scale, but the cause is the same: the value-type key crosses an interface boundary.

This is not the biggest remaining allocation source, but it is conceptually wrong for DataLinq's cache-hit story. A warm primary-key lookup should not require users to prebox or cache key objects themselves.

## What We Should Not Do

The first proof-of-concept was an `int`-specific lookup path:

- `Database.Get<M>(int key)`
- `Transaction<T>.Get<M>(int key)`
- `RowCache.TryGetValue(int key, ...)`
- `TableCache.GetRow(int key, ...)`

That proved the box can be avoided, but it is the wrong long-term design.

Why it is not good enough:

- It duplicates the cache-hit and cache-miss path.
- It only helps `int` keys.
- Extending it to `long`, `Guid`, `string`, `DateOnly`, enums, and composite keys would multiply overloads and branches.
- It pushes key-type knowledge into `TableCache`, which is otherwise metadata-driven.
- It risks making cold inserts more expensive if secondary typed lookup maps are maintained eagerly.

The overload approach is a benchmark bandage. The cache needs a better key representation.

## Preferred Direction: Concrete Non-Boxing Runtime Key

Replace internal `IKey` dictionary keys with a concrete value type, tentatively named `DataLinqKey`.

The type should be a small discriminated value:

```csharp
internal readonly struct DataLinqKey : IEquatable<DataLinqKey>
{
    private readonly DataLinqKeyKind kind;
    private readonly int intValue;
    private readonly long longValue;
    private readonly Guid guidValue;
    private readonly object? referenceValue;
}
```

The exact layout needs measurement, but the idea is straightforward:

- common single-column keys are stored inline
- reference keys such as `string` store the reference without boxing
- uncommon scalar types can use a reference fallback initially
- composite keys use a compact immutable backing representation
- equality and hashing are implemented directly on `DataLinqKey`
- dictionaries use `ConcurrentDictionary<DataLinqKey, IImmutableInstance>` instead of `ConcurrentDictionary<IKey, IImmutableInstance>`

This keeps the cache metadata-driven while removing interface boxing from the normal path.

## Why This Beats `RowCache<TKey>`

A fully generic cache can also remove boxing:

```csharp
RowCache<TKey>
TableCache<TKey>
ConcurrentDictionary<TKey, IImmutableInstance>
```

That design is attractive on paper, but it is a larger architectural move because every caller must preserve `TKey` all the way down:

- generated `Database.Get` helpers
- query parser simple-key extraction
- `KeyFactory`
- relation traversal
- index caches
- transaction caches
- generated metadata handles

It probably requires generated per-table/per-index cache accessors or generated key structs. That may become the best end-state after the Remotion/query-plan rewrite, but it is too invasive for a focused cache allocation pass.

A concrete `DataLinqKey` value type is the pragmatic middle ground. It removes boxing broadly without requiring `TableCache` to become generic or forcing generated code to own the entire cache path.

## Compatibility Model

Do not remove `IKey` immediately. Treat it as a public compatibility/input shape and convert at the boundary.

Suggested staged model:

1. Add `DataLinqKey` internally.
2. Add `KeyFactory` conversion APIs:
   - `DataLinqKey FromValue(object? value)`
   - `DataLinqKey FromReader(...)`
   - `DataLinqKey FromRowData(...)`
   - `DataLinqKey FromLegacyKey(IKey key)`
3. Change `RowCache` to use `DataLinqKey`.
4. Change `IndexCache` to use `DataLinqKey`.
5. Keep public `Database.Get<M>(IKey key)` by converting once at entry.
6. Add non-boxing public convenience overloads only if they are thin boundary conversions and do not duplicate cache internals.

The important line is this: public overloads are acceptable after the internals are unified. They are not acceptable as separate cache implementations.

## Composite Keys

Composite keys are the hardest part.

The current `CompositeKey` can allocate nested arrays through `IKey.Values`. A replacement needs to avoid rebuilding arrays on normal lookup paths.

Pragmatic first version:

- `DataLinqKey` stores a reference to an immutable `object?[]` for composite keys.
- the array is normalized once at key creation
- equality compares array contents by column order
- hash is computed once if measurement proves repeated hashing is expensive

Better future version:

- generated table/index-specific key structs for common composite keys
- or a small inline representation for 2-value and 3-value composites
- or a pooled/frozen composite backing object

Do the simple version first unless benchmarks show composite key allocation is on a critical path.

## API Shape

The first internal API should look something like this:

```csharp
internal readonly struct DataLinqKey : IEquatable<DataLinqKey>
{
    public static DataLinqKey Null { get; }
    public static DataLinqKey FromInt32(int value);
    public static DataLinqKey FromInt64(long value);
    public static DataLinqKey FromGuid(Guid value);
    public static DataLinqKey FromString(string value);
    public static DataLinqKey FromComposite(object?[] values);
}
```

`RowCache` should then become:

```csharp
private readonly ConcurrentDictionary<DataLinqKey, IImmutableInstance> rows;
private readonly ConcurrentDictionary<DataLinqKey, RowMetadata> rowMetadata;
```

`IndexCache` should similarly use `DataLinqKey` for both foreign keys and primary keys.

## Migration Plan

### Step 1: Add `DataLinqKey` and Tests

- Add equality/hash tests for each supported scalar kind.
- Add null-key tests.
- Add composite-key equality and hash tests.
- Add tests for enum normalization if enum keys are supported at this layer.
- Add explicit tests proving `int`, `long`, and `Guid` key creation does not allocate.

### Step 2: Convert `RowCache`

- Change `RowCache` dictionaries from `IKey` to `DataLinqKey`.
- Convert legacy `IKey` callers at `TableCache` boundaries.
- Keep old public method overloads temporarily where useful.
- Verify warm primary-key cache hits stay at 0 B/op without preboxed keys.

### Step 3: Convert Query and Row Key Creation

- Make `KeyFactory.GetKey(reader, columns)` return `DataLinqKey` internally.
- Make `KeyFactory.GetKey(rowData, columns)` return `DataLinqKey` internally.
- Keep legacy `IKey` factory methods only where public callers still need them.

### Step 4: Convert `IndexCache`

- Replace `IKey` dictionaries with `DataLinqKey`.
- Verify relation traversal and foreign-key cache hits.
- Watch for accidental composite-key array churn.

### Step 5: Public API Cleanup

- Decide whether to keep `IKey` as public API, obsolete it, or keep it as an advanced compatibility layer.
- Add public scalar overloads only as thin wrappers:
  - `Get<M>(int key)`
  - `Get<M>(long key)`
  - `Get<M>(Guid key)`
  - possibly `Get<M>(string key)`
- Do not duplicate cache internals for each overload.

## Benchmark Plan

Use the scratch allocation probes during development, then validate with BenchmarkDotNet before treating the work as done.

Required before/after measurements:

- warm primary-key fetch
- cold primary-key fetch
- warm relation traversal
- cold relation traversal
- startup primary-key fetch
- provider initialization
- CRUD workflow small
- CRUD workflow batch

Micro probes to keep:

- `Database.Get` with `new IntKey(...)`
- `Database.Get` with generated/scalar key helper if added
- `TableCache.GetRow` cache hit
- `RowCache.TryAddRow`
- `IndexCache.TryAdd`
- relation index cache hit

Success criteria:

- warm cache hit through normal public API is 0 B/op for common single scalar keys
- row-cache inserts do not regress materially
- cold primary-key fetch does not grow from extra conversion layers
- relation traversal remains correct and does not introduce duplicate key backing allocations
- cache telemetry still records hits, misses, stores, occupancy, and cleanup correctly

## Risks

- Bad `GetHashCode` behavior could silently damage cache performance.
- Composite key equality bugs would be correctness bugs, not just performance bugs.
- Mixing legacy `IKey` and `DataLinqKey` during migration can create double conversion and allocation churn.
- `DataLinqKey` could become too large if too many scalar fields are stored inline.
- Public API cleanup can become noisy if done before the internal representation is stable.

## Opinionated Recommendation

Do not make `TableCache` generic yet. The current runtime is metadata-driven, and a generic cache would pull generated code into more of the runtime surface than is justified right now.

Implement a concrete non-boxing `DataLinqKey` first. It is the smallest design that fixes the real problem broadly. If the later query-plan/source-generator rewrite wants generated per-table key structs, it can build on the same conceptual boundary rather than fighting a pile of scalar overloads.
