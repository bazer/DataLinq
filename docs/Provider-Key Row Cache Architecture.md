# Provider-Key Row Cache Architecture

This page documents the internal key model used by DataLinq row caches.

It is intentionally an internals page. The names here are useful when reading generated code, cache behavior, diagnostics, or performance work, but they are not meant to become a large public key abstraction surface.

## The Core Rule

A `RowCache` stores rows in exactly one `RowStore<TKey>`.

That `TKey` is the provider-key type for the table:

- scalar primary key: the provider CLR type, such as `int`, `long`, `Guid`, or `string`
- generated composite primary key: a generated `DataLinqPrimaryKey` struct
- metadata-only fallback: `DataLinqKey`

That rule is the important part. DataLinq should not store the same row under both a generated provider key and a second lookup-only key wrapper. Duplicating keys creates extra memory pressure and makes invalidation harder to reason about.

## Main Types

`RowCache`
: Owns cache-level behavior for one table cache: row count, byte totals, cleanup by age/size/count, and the single row-store instance.

`RowStore<TKey>`
: Owns the actual dictionary of rows. Its key type is fixed once the first row is stored. A `RowCache` that starts as `RowStore<int>` cannot later become `RowStore<DataLinqKey>`.

`IProviderKey`
: A tiny component reader for provider keys. Generated composite keys implement it so metadata-driven code can inspect key components without knowing the generated struct's fields.

`IProviderKeyRowStoreAccessor`
: Generated table-specific adapter. It knows how to create the exact provider-key value for a table from row data, model data, or a dynamic key carrier.

`DataLinqKey`
: A bounded dynamic key carrier for metadata-driven paths. It is not the generated hot-path row-cache identity.

`TypedIndexCache<TKey>`
: Owns relation index buckets for one foreign-key index. Scalar generated relation paths use the provider foreign-key type directly, such as `int`, `long`, `Guid`, or `string`. Composite or unsupported shapes fall back to `IndexCache`, which is `TypedIndexCache<DataLinqKey>`.

## Generated Lookup Flow

For a generated scalar primary-key lookup, the fast path is direct:

```text
Generated Get(...)
  -> IImmutable<T>.GetByProviderKey(providerValue, dataSource)
  -> TableCache.GetRow<TKey>(providerValue, dataSource)
  -> RowCache.TryGetValue<TKey>(providerValue)
  -> RowStore<TKey>
```

For a generated composite primary-key lookup, generated code creates a table-specific key struct:

```text
Generated Get(deptNo, empNo)
  -> new DataLinqPrimaryKey(deptNo, empNo)
  -> IImmutable<T>.GetByProviderKey(providerKey, dataSource)
  -> RowStore<DataLinqPrimaryKey>
```

No lookup-only wrapper is created for either path.

## Generated Relation Flow

Generated scalar relation properties also pass provider foreign-key values directly:

```text
Generated Employee.dept_emp
  -> GetImmutableRelation<Dept_emp, int>(emp_no.Value, relationHandle)
  -> TableCache.GetRows<int>(empNo, relationHandle, dataSource)
  -> TypedIndexCache<int>
  -> RowCache target lookup through provider-key row-store access
```

Generated scalar reference properties follow the same rule:

```text
Generated Dept_emp.departments
  -> GetImmutableForeignKey<Department, string>(dept_no, relationHandle)
  -> TableCache.GetRow<string>(deptNo, dataSource)
  -> RowStore<string>
```

Nullable scalar foreign keys still have a dynamic null branch. That branch uses `DataLinqKey.Null` as the compact null carrier so relation traversal can return an empty collection or `null` reference without inventing a nullable provider-key store.

## Metadata-Driven Flow

Not every runtime path starts inside generated table-specific code. Query materialization, relation traversal, index maintenance, mutation state, and dynamic direct lookup may only have metadata and raw key components.

Those paths use `DataLinqKey` as a compact carrier:

```text
metadata-driven code
  -> DataLinqKey
  -> generated IProviderKeyRowStoreAccessor
  -> exact provider key
  -> RowStore<TKey>
```

The generated accessor is what prevents `DataLinqKey` from becoming a second universal row-store key. If a table has generated provider-key metadata, the accessor converts the dynamic components into the table's real row-store key before cache add, get, or remove.

If there is no generated accessor, DataLinq can fall back to `RowStore<DataLinqKey>`. That is the dynamic compatibility path, not the normal generated model path.

## How `DataLinqKey` Differs From The Old `IKey`

The old `IKey` design was a universal identity abstraction. It had many concrete key wrappers such as integer, string, byte-array, null, object, and composite key types. Cache, relation, query, and mutation code all tended to accept or return `IKey`.

That was too broad. It made DataLinq's cache identity a DataLinq-owned wrapper instead of the provider key itself. It also made value-type keys cross interface boundaries, which can box, and it encouraged extra lookup structures when generated code already knew the exact key shape.

`DataLinqKey` has a narrower job:

- it is one concrete readonly struct, not an interface hierarchy
- it stores either one normalized value or multiple normalized components
- it preserves key semantics needed by metadata-driven code, including enum normalization, byte-array content equality, and all-null key handling
- it implements `IProviderKey` so components can be read uniformly
- it is used as a bridge into generated provider-key accessors
- it is not the desired storage key for generated row caches

That last point is the design boundary. `DataLinqKey` may allocate or box for some dynamic composite paths, but generated cache hits should use the exact provider key type and avoid DataLinq-owned key wrappers.

## Index And Relation Keys

Relation collections still expose `DataLinqKey` primary keys because their public indexer and dictionary view are metadata-shaped APIs. That is acceptable: those keys describe the rows already loaded into the collection, not the storage identity of the target table.

Index caches are more subtle. A relation index cache has one foreign-key store, selected from the relation index shape:

- scalar `int`, `long`, `Guid`, and `string` foreign keys use `TypedIndexCache<TKey>`
- composite or unsupported foreign keys use `IndexCache`, the `DataLinqKey` fallback

The index cache values are still `DataLinqKey[]` primary-key carriers because a relation index maps one foreign key to many target primary keys. The important part is that scalar generated relation traversal does not allocate a lookup-only foreign-key carrier just to ask the index cache a question.

That does not contradict the row-cache rule. The index cache can return dynamic primary-key carriers, while final row lookup still goes through the generated provider-key accessor when the target table has one.

## Invariants

The cache key architecture depends on these invariants:

- one `RowCache` has one `RowStore<TKey>`
- one relation index cache has one foreign-key store, typed for scalar generated foreign keys when supported
- generated primary-key row stores use provider key values directly
- generated scalar relation traversal passes provider foreign-key values directly
- generated composite primary keys use generated structs, not object arrays as row-store keys
- `DataLinqKey` is allowed in metadata-driven plumbing, not as a replacement for generated provider keys
- cache invalidation should remove rows by provider-key components through the same table-specific accessor path

If a future change stores the same row under two key representations, it is almost certainly the wrong design.
