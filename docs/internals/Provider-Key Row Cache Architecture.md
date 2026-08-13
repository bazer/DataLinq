# Provider-Key Row Cache Architecture

This page documents the internal key model used by DataLinq row caches.

It is intentionally an internals page. The names here are useful when reading generated code, cache behavior, diagnostics, or performance work, but they are not meant to become a large public key abstraction surface.

## The Core Rule

A `RowCache` stores rows in exactly one `RowStore<TKey>`.

That `TKey` is normally the provider-key type for the table:

- scalar primary key with stable value semantics: the provider CLR type, such as `int`, `long`, `Guid`, or `string`
- generated composite primary key whose components have stable value semantics: a generated `DataLinqPrimaryKey` struct
- scalar or composite primary key containing `byte[]`: `DataLinqKey`, which snapshots the bytes and compares them structurally
- metadata-only fallback: `DataLinqKey`

The binary exception is necessary rather than cosmetic. A `RowStore<byte[]>` would use array reference identity, and a generated record struct containing `byte[]` would also compare that component by array reference. Both would retain mutable caller storage and miss separately allocated byte arrays with identical contents.

That rule is the important part. DataLinq should not store the same row under both a generated provider key and a second lookup-only key wrapper. Duplicating keys creates extra memory pressure and makes invalidation harder to reason about.

## Main Types

`RowCache`
: Owns cache-level behavior for one table cache: row count, byte totals, cleanup by age/size/count, and the single row-store instance.

`RowStore<TKey>`
: Owns the actual dictionary of rows. Its key type is fixed once the first row is stored. A `RowCache` that starts as `RowStore<int>` cannot later become `RowStore<DataLinqKey>`.

`IProviderKey`
: A tiny component reader for provider keys. Generated composite keys implement it so metadata-driven code can inspect key components without knowing the generated struct's fields.

`IProviderKeyRowStoreAccessor`
: Generated table-specific adapter. It knows how to create the exact provider-key value for a table from row data, model data, or a dynamic key carrier. Current accessors also publish with the canonical provider key captured before provider-to-model conversion; the default method preserves older generated accessors until regeneration.

`DataLinqKey`
: A bounded dynamic key carrier for metadata-driven paths and the structural fallback for provider keys containing `byte[]`. It is not the normal generated hot-path row-cache identity.

`TypedIndexCache<TKey>`
: Owns relation index buckets for one foreign-key index. Scalar generated relation paths use the provider foreign-key type directly, such as `int`, `long`, `Guid`, or `string`. Composite or unsupported shapes fall back to `IndexCache`, which is `TypedIndexCache<DataLinqKey>`.

## Scalar Converter Boundary

DataLinq 0.7.0 does not implement scalar converters, but the row-cache key shape now separates model metadata from provider metadata:

- `TableKeyComponentDefinition.ModelCsType` and `ModelClrType` describe the value exposed by generated model APIs.
- `TableKeyComponentDefinition.ProviderCsType` and `ProviderClrType` describe the value used by readers, query parameters, row stores, and relation index stores.
- `TableKeyComponentDefinition.ProviderStoreKind` selects scalar cache/index stores. The old ambiguous `StoreKind` surface is gone because cache identity is provider identity.
- `TableKeyComponentDefinition.ScalarConverterHandle` is currently `null`; it is the reserved metadata slot for resolved converter information.

Today, model and provider types are the same. That is an implementation state, not an architectural rule. The rule remains:

> Cache keys and relation index keys are provider values.

Future scalar converter work should plug into these exact places:

- Generated `Get(...)` methods should keep their model-shaped public signatures, then convert model key arguments to provider values before calling `GetByProviderKey(...)`.
- Generated relation traversal should convert foreign-key model properties to provider values before entering `GetImmutableRelation<..., TProviderKey>(...)`, `GetImmutableForeignKey<..., TProviderKey>(...)`, and `TypedIndexCache<TKey>`.
- Query constants should normalize equality, local `Contains(...)`, simple primary-key extraction, and join key constants to provider values before SQL parameters or cache probes are created.
- Mutation/default handling should accept model values at the mutable model boundary, convert to provider values for writes and cache invalidation, and convert database-generated provider defaults back to model values before updating mutable state.
- Schema validation should compare database column storage against provider CLR type mapping and explicit database type metadata. The model CLR type should drive generated API shape and diagnostics, not storage compatibility.

Those conversions must be generated, statically bound, or cached outside hot loops. Reflection-heavy converter lookup belongs in metadata construction or source generation, not in cache lookup, relation traversal, materialization, or query parameter creation.

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

Binary provider keys are the deliberate exception. The central `RowCache` add path detects a scalar `byte[]` or a composite `IProviderKey` containing `byte[]`, snapshots it into `DataLinqKey`, and fixes that cache to `RowStore<DataLinqKey>`. Lookup and removal normalize equivalent provider-key values through the same structural representation. This keeps ordinary generated keys typed while preventing array reference equality or later caller mutation from corrupting binary-key cache identity.

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

## Query Materialization Flow

Entity queries use the same provider-key boundary.

For a simple scalar primary-key predicate, the query optimizer keeps the predicate value as the provider CLR value:

```text
Where(emp_no == 1001)
  -> TryGetSimpleScalarPrimaryKey()
  -> TableCache.GetRow<int>(1001, dataSource)
  -> RowStore<int>
```

For broader scalar primary-key materialization, DataLinq still runs the key-first query shape, but the key reader now collects provider values instead of `DataLinqKey` wrappers:

```text
SELECT emp_no FROM employees WHERE ...
  -> reader.GetValue<int>(emp_no, ordinal: 0)
  -> TableCache.GetRows<int>(keys, dataSource)
  -> RowStore<int>
```

Joined materialization reads each selected source primary key by reader ordinal and lets the generated table accessor construct the table's provider key:

```text
SELECT t0.emp_no AS dl_0_pk_0, t1.dept_no AS dl_1_pk_0 ...
  -> DataLinqProviderKeyRowStoreAccessor.TryGetRow(reader, ordinals, dataSource)
  -> TableCache.GetRow<TKey>(providerKey, dataSource)
```

Composite generated joins therefore use the generated `DataLinqPrimaryKey` struct when the joined source has a composite primary key. Dynamic metadata fallback still uses `DataLinqKey`.

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

The generated accessor is what prevents `DataLinqKey` from becoming a second universal row-store key. If a table has generated provider-key metadata, the accessor converts the dynamic components into the table's real row-store key before cache add, get, or remove. Shared materialization calls `TryAddCanonicalRow(...)`, so scalar conversion cannot replace provider identity with a model wrapper before publication. `RowCache` may then select the structural `DataLinqKey` store for a binary provider-key shape; it never stores both representations.

If there is no generated accessor, DataLinq can fall back to `RowStore<DataLinqKey>`. That is the dynamic compatibility path, not the normal generated model path.

## How `DataLinqKey` Differs From The Old `IKey`

The old `IKey` design was a universal identity abstraction. It had many concrete key wrappers such as integer, string, byte-array, null, object, and composite key types. Cache, relation, query, and mutation code all tended to accept or return `IKey`.

That was too broad. It made DataLinq's cache identity a DataLinq-owned wrapper instead of the provider key itself. It also made value-type keys cross interface boundaries, which can box, and it encouraged extra lookup structures when generated code already knew the exact key shape.

`DataLinqKey` has a narrower job:

- it is one concrete readonly struct, not an interface hierarchy
- it stores either one normalized value or multiple normalized components
- it preserves key semantics needed by metadata-driven code, including enum normalization, byte-array content equality, and all-null key handling
- it owns mutable byte-array components and returns defensive copies, so caller mutation cannot invalidate its cached hash or dictionary identity
- it implements `IProviderKey` so components can be read uniformly
- it is used as a bridge into generated provider-key accessors
- it is not the desired storage key for ordinary generated row caches, but it is the required structural storage key when a generated provider key contains `byte[]`

That last point is the design boundary. `DataLinqKey` may allocate or box for some dynamic composite paths, but ordinary generated cache hits should use the exact provider key type and avoid DataLinq-owned key wrappers. Binary generated keys use the explicit structural exception because exact provider-key storage would be incorrect.

## Index And Relation Keys

Relation collections still expose `DataLinqKey` primary keys because their public indexer and dictionary view are metadata-shaped APIs. That is acceptable: those keys describe the rows already loaded into the collection, not the storage identity of the target table.

Index caches are more subtle. A relation index cache has one foreign-key store, selected from the relation index shape:

- scalar `int`, `long`, `Guid`, and `string` foreign keys use `TypedIndexCache<TKey>`
- composite or unsupported foreign keys use `IndexCache`, the `DataLinqKey` fallback

The index cache values are still `DataLinqKey[]` primary-key carriers because a relation index maps one foreign key to many target primary keys. The cache clones that array once before publishing a new entry, then builds its reverse mapping from the owned snapshot. Mutating the caller's original array therefore cannot desynchronize the forward and reverse indexes. Internal cache reads intentionally remain zero-copy; those internal consumers must treat returned arrays as read-only.

The important part is that scalar generated relation traversal does not allocate a lookup-only foreign-key carrier just to ask the index cache a question.

That does not contradict the row-cache rule. The index cache can return dynamic primary-key carriers, while final row lookup still goes through the generated provider-key accessor when the target table has one.

## Invariants

The cache key architecture depends on these invariants:

- one `RowCache` has one `RowStore<TKey>`
- one relation index cache has one foreign-key store, typed for scalar generated foreign keys when supported
- generated primary-key row stores use provider key values directly unless a `byte[]` component requires the owned structural fallback
- shared materialization publishes the canonical provider key captured before model conversion
- generated scalar relation traversal passes provider foreign-key values directly
- scalar entity query materialization reads provider primary-key values directly from readers
- joined materialization lets generated accessors build provider keys from reader ordinals
- generated composite primary keys use generated structs, not object arrays as row-store keys; composites containing `byte[]` normalize to `DataLinqKey`
- index caches own the `DataLinqKey[]` arrays they publish and use for reverse mappings
- `DataLinqKey` is allowed in metadata-driven plumbing and the binary structural fallback, not as a general replacement for generated provider keys
- broad cache machinery is internal except where generated code needs a public bridge into `RowCache` or `TableCache.GetRow<TKey>(...)`
- cache invalidation should remove rows by provider-key components through the same table-specific accessor path

If a future change stores the same row under two key representations, it is almost certainly the wrong design.

## Invalidation Integration

Current cache clearing and external invalidation use these provider-key artifacts:

- `TableDefinition.PrimaryKeyShape` is the table/key descriptor. Its components expose provider/model CLR types, provider store kind, nullability, column ordinals, and the scalar-converter placeholder.
- Generated table models install `IProviderKeyRowStoreAccessor` or `IProviderKeyDataReaderRowStoreAccessor` when a table has primary-key metadata. That accessor is the bridge from bounded dynamic components into the table's exact provider-key type.
- Generated model `Get(...)` methods are the exact public primary-key lookup path. `Database<T>.Get<M>(DataLinqKey)` and `Transaction<T>.Get<M>(DataLinqKey)` remain the explicit dynamic escape hatch.
- `RowCache.TryRemoveProviderKey<TKey>(...)` removes a row from the single typed row store without constructing `IKey` or a duplicate side key.
- Internal `TableCache.TryRemoveProviderKey<TKey>(...)` applies provider-key row removal to the active read/transaction cache.
- Internal `TableCache.TryRemoveForeignKeyIndex<TKey>(...)` removes scalar relation/index buckets by provider foreign-key values. Internal `TableCache.TryRemovePrimaryKeyIndex(...)` removes relation index entries that point at a primary key carrier.
- `TableCache.ClearCache()`, `ClearRows()`, and `ClearIndex()` are the conservative table-level fallback when an external invalidation signal cannot provide precise provider-key components.
- Cache maintenance telemetry uses the `datalinq.cache.operation` tag. The current operation names are centralized in `CacheMaintenanceOperations`: `state_change_precise`, `state_change_table`, `transaction_state_change`, `transaction_state_change_table`, `transaction_remove`, `clear`, `row_limit`, `size_limit`, `age_limit`, and `limit`.
- Production source has no remaining `IKey` dependency. `DataLinqKey` remains as the bounded dynamic provider-key carrier, not as a universal row-store identity.

The practical invalidation rule is simple: public invalidation can expose convenient model/table APIs, but the point where it touches row or relation caches must be provider-key values or a conservative table clear. Reintroducing a universal key interface would undo the main provider-key cache cleanup.
