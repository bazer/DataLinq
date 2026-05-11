> [!WARNING]
> This document is planning material. It describes a proposed internal cache/key redesign and should not be read as shipped behavior.

# Generated Provider-Key Cache Design

**Status:** Draft design
**Created:** 2026-05-11
**Updated:** 2026-05-11
**Scope:** generated primary-key lookup, row-cache keys, relation/index-cache keys, scalar converter integration, and removal of the legacy `IKey` abstraction.

## Problem

DataLinq already generates convenient static `Get(...)` methods for primary-key lookup, but those methods currently delegate straight back into the legacy runtime key layer:

```csharp
public static Employee? Get(int empNo, Database<EmployeesDb> database)
    => IImmutable<Employee>.Get(KeyFactory.CreateKeyFromValue(empNo), database.Provider.ReadOnlyAccess);
```

That gives users the right shape but not the right hot path. `KeyFactory.CreateKeyFromValue(...)` returns `IKey`, and the row/index caches are keyed by `IKey`. For value-type keys, crossing that interface boundary boxes. For composite keys, the current path also builds `object?[]` arrays.

The same problem exists in relation traversal. Generated relation properties call through `Immutable.GetRelationKey(...)`, which creates an `IKey` from row data before asking `TableCache` for related rows.

This is exactly backwards for the generated-runtime direction. DataLinq knows the table, primary-key columns, foreign-key columns, CLR types, provider types, and generated accessors at build time. The runtime should not rebuild a generic key object just to do a cache lookup.

## Decision

The end-state should remove `IKey` completely.

Do not keep `IKey` as a public compatibility layer, advanced key API, or internal cache key. It made sense when the runtime was mostly metadata-driven. It does not fit a generated runtime where every table and relation can have exact key accessors.

The replacement is:

- generated primary-key and foreign-key accessors
- cache stores keyed by provider CLR values
- relation indexes that accept provider key components directly
- scalar converter metadata that normalizes model values to provider values before cache, query, and mutation paths see them

This is a breaking architectural cleanup, not a small allocation patch. That is fine. The current `IKey` design is the abstraction causing the problem.

## Non-Negotiable Constraints

- A warm primary-key cache hit through generated APIs must allocate 0 B for ordinary scalar keys.
- A warm relation index lookup must allocate 0 B and must not box key components.
- Relation lookup must not create a lookup-only key object. Passing raw key components is the intended path.
- Cache keys must be based on provider CLR values, not model CLR values.
- Third-party typed ID libraries must be supported without making the runtime depend on those libraries.
- Composite key support must remain correct, but the implementation can be more specialized than the scalar path.
- Legacy dynamic fallback should not preserve `IKey` in the final design. Temporary migration scaffolding is acceptable while the branch is under construction, but it should be deleted before the feature is considered done.

## Key Vocabulary

**Model value** is the value exposed by the generated model API. For example, `CustomerId`.

**Provider value** is the value stored in the database and used by cache indexes. For example, `int`.

**Scalar converter** maps between model value and provider value. Converter discovery can be flexible; hot-path conversion must be generated or statically bound.

**Key shape** is the ordered provider-value tuple for a primary key, foreign key, or index. Examples:

- `int`
- `Guid`
- `(string deptNo, int empNo)`

**Row store** is the cache for table rows by primary-key provider values.

**Relation index store** is the cache for relation lookup by foreign-key provider values.

## Generated Public API

The existing generated static `Get(...)` methods should remain, but they should call generated table accessors instead of `KeyFactory`.

Single-column primitive key:

```csharp
public static Employee? Get(int empNo, Database<EmployeesDb> database)
    => EmployeesTableAccessor.GetByPrimaryKey(empNo, database.Provider.ReadOnlyAccess);
```

Typed ID over an `int` provider key:

```csharp
public static Customer? Get(CustomerId id, Database<AppDb> database)
    => CustomersTableAccessor.GetByPrimaryKey(
        CustomerIdDataLinqConverter.ToProvider(id),
        database.Provider.ReadOnlyAccess);
```

Composite key:

```csharp
public static Dept_emp? Get(string deptNo, int empNo, Database<EmployeesDb> database)
    => DeptEmpTableAccessor.GetByPrimaryKey(deptNo, empNo, database.Provider.ReadOnlyAccess);
```

The generated database model should also be able to expose typed table readers instead of plain `DbRead<T>` where useful:

```csharp
public sealed class EmployeeRead : DbRead<Employee>
{
    public Employee? Get(int empNo) => EmployeesTableAccessor.GetByPrimaryKey(empNo, DataSource);
}
```

Then ordinary use becomes:

```csharp
var employee = db.Employees.Get(10001);
```

That is a sensible public API. `Database.Get<M>(IKey key)` is not.

## Runtime Store Shape

### Single-Column Keys

Single-column keys should use the provider CLR type directly:

```csharp
internal sealed class RowStore<TKey>
    where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, IImmutableInstance> rows;

    public bool TryGet(TKey key, out IImmutableInstance? row);
    public bool TryAdd(TKey key, IImmutableInstance row);
    public bool TryRemove(TKey key, out IImmutableInstance? row);
}
```

For `Employee.emp_no`, this means the cache is keyed by `int`. For a Vogen or StronglyTypedId-backed `CustomerId`, the cache is still keyed by the provider `int`.

This gives the runtime the important property: typed ID libraries affect the model boundary, not the cache representation.

### Composite Keys

Composite keys are where we should be strict about the user's constraint: no new key just to look up.

A `Dictionary<GeneratedKey2<T1, T2>, TValue>` avoids heap allocation, but a lookup still constructs a key value. That may be cheap, but it is not the design goal for relation lookups.

Composite stores should expose component-addressed methods:

```csharp
internal sealed class RowStore2<TKey1, TKey2>
    where TKey1 : notnull
    where TKey2 : notnull
{
    public bool TryGet(TKey1 key1, TKey2 key2, out IImmutableInstance? row);
    public bool TryAdd(TKey1 key1, TKey2 key2, IImmutableInstance row);
    public bool TryRemove(TKey1 key1, TKey2 key2, out IImmutableInstance? row);
}
```

Possible implementations:

- nested dictionaries, such as `Dictionary<TKey1, Dictionary<TKey2, TValue>>`
- generated bucket stores with entries that store key components inline
- generated arity-specific stores for 2-column and 3-column keys

Nested dictionaries are the simplest zero-lookup-key implementation and use normal .NET comparers without boxing. They may cost more memory for high-cardinality first components. Generated bucket stores are more work but can store one node per row and compare components directly. This should be benchmarked before choosing the final implementation for composite-heavy tables.

What we should not do is recreate `CompositeKey` under a different name and route every lookup through `object?[]`.

## Generated Relation Access

Relation properties should stop calling `GetRelationKey(...)`.

Current conceptual shape:

```csharp
public override IImmutableRelation<Dept_emp> dept_emp
    => _dept_emp ??= GetImmutableRelation<Dept_emp>(DataLinqRelation_dept_emp);
```

Target shape:

```csharp
public override IImmutableRelation<Dept_emp> dept_emp
    => _dept_emp ??= EmployeesRelations.GetDepartmentEmployeesByEmployeeNumber(
        emp_no,
        GetDataSource());
```

If the relation column is a typed ID, the generated accessor converts to provider values before entering the relation index:

```csharp
public override IImmutableRelation<Order> Orders
    => _orders ??= CustomerRelations.GetOrdersByCustomerId(
        CustomerIdDataLinqConverter.ToProvider(Id),
        GetDataSource());
```

The generated relation helper then calls an index store that accepts provider components directly:

```csharp
internal static IImmutableRelation<Dept_emp> GetDepartmentEmployeesByEmployeeNumber(
    int empNo,
    IDataSourceAccess source)
{
    return DeptEmpByEmployeeNumberIndex.Get(empNo, source);
}
```

No `IKey`. No boxed scalar. No lookup-only key object.

## Relation Collection API

The current `IImmutableRelation<T>` exposes `IKey`-based lookup members:

```csharp
T? this[IKey key] { get; }
T? Get(IKey key);
bool ContainsKey(IKey key);
ImmutableArray<IKey> Keys { get; }
```

Those members should be removed in the target design.

The common relation API should focus on collection behavior:

```csharp
public interface IImmutableRelation<T> : IReadOnlyList<T>
{
    ImmutableArray<T> Values { get; }
    int Count { get; }
}
```

If keyed lookup inside a relation is valuable, generate typed relation collection types:

```csharp
public sealed class DepartmentEmployeesRelation : IImmutableRelation<Dept_emp>
{
    public Dept_emp? Get(string deptNo, int empNo);
    public bool Contains(string deptNo, int empNo);
}
```

The key type belongs to the generated relation type, not to a universal `IKey` API.

## Scalar Converters and Typed IDs

This design depends on the scalar converter plan. The important rule is simple:

> The cache sees provider values. The model sees model values.

For a typed ID:

```csharp
public readonly partial record struct CustomerId(int Value);
```

The generated runtime needs an exact provider conversion:

```csharp
internal static class CustomerIdDataLinqConverter
{
    public static int ToProvider(CustomerId value) => value.Value;
    public static CustomerId FromProvider(int value) => new(value);
}
```

The converter can come from several sources:

- explicit `[ScalarConverter]` metadata
- assembly-level converter registration
- generated typed ID support from DataLinq itself
- optional adapter packages for libraries such as Vogen or StronglyTypedId
- source-generation plugins that recognize common typed-ID shapes

The runtime should not care which one produced the converter. It should only see generated direct calls between `TModel` and `TProvider`.

Reflection-based discovery is acceptable during generation or metadata construction if the target app allows it. It is not acceptable on cache lookup, relation traversal, query parameter creation, or row materialization hot paths.

## Query Translation

Query translation should normalize key constants through the same provider-value layer:

```csharp
db.Query().Customers.Where(x => x.Id == customerId);
```

The SQL parameter should be the provider value, not the typed ID object.

The current simple-primary-key extraction path returns `IKey`. That should become a generated or provider-key-aware result:

```csharp
SimplePrimaryKeyMatch<int>
```

or a table-specific accessor call:

```csharp
CustomersTableAccessor.TryGetSimplePrimaryKey(query, out int id)
```

Joined primary-key reads should also stop returning `IKey`. The source or table accessor should read provider key components directly from the data reader and pass them into the row store.

## Mutation and Invalidation

Mutation state currently stores primary keys through `IKey`. That should become table-specific generated key handling.

The generated model/table handler should be responsible for:

- reading current primary-key provider values from a mutable model
- reading original primary-key provider values from an immutable model
- detecting primary-key changes
- removing row-cache entries by provider key components
- removing relation index entries by provider key components
- updating auto-increment/default generated values through model-value conversion

This probably means `StateChange` becomes less generic. That is a good trade. Cache invalidation is table-specific behavior pretending to be generic.

If a common runtime container is still needed, it should carry a generated table handler plus the model instance, not a universal key object.

## Row Data Storage

The scalar converter plan leaves open whether `RowData` stores model values, provider values, or both.

For this design, provider values should be available without conversion when building cache keys. That does not require every property getter to expose provider values. It does mean one of these must be true:

- `RowData` stores provider values and immutable property getters convert to model values
- `RowData` stores model values but generated key accessors keep provider key fields separately
- `RowData` stores provider values plus optional lazy model-value cache for expensive converters

The first option is the cleanest for key performance. The cost is that generated property getters need converter calls or cached converted values for non-trivial model types.

## Migration Plan

### Step 1: Add Key Shape Metadata

Extend generated metadata with primary-key and foreign-key key shapes:

- model CLR types
- provider CLR types
- nullable information
- converter handles
- column order
- generated accessor names

This should build on the scalar converter metadata, not duplicate it.

### Step 2: Generate Typed Table Readers and Static Get Paths

Change generated static `Get(...)` methods to call generated table accessors directly.

Optionally generate typed `DbRead` subclasses so `db.Employees.Get(10001)` becomes the ordinary public API.

### Step 3: Add Provider-Key Row Stores

Add row stores for single-column provider keys first:

- `int`
- `long`
- `Guid`
- `string`
- enum provider types after normalization

Then add arity-specific composite stores.

### Step 4: Generate Relation Index Accessors

Replace `Immutable.GetRelationKey(...)` calls in generated relation properties with generated relation helper calls.

Relation index stores should accept provider components directly and return cached relation collections or row references without building key objects.

### Step 5: Convert Query and Materialization Paths

Replace `KeyFactory.GetKey(...)` and simple-primary-key extraction with provider-key readers.

Joined queries should read key provider components directly from the data reader and pass them to table stores.

### Step 6: Convert Mutation State and Cache Invalidation

Replace `StateChange.PrimaryKeys` and related `IKey` invalidation calls with generated table handlers.

This is the step that proves `IKey` can be deleted rather than merely bypassed.

### Step 7: Delete `IKey`

Delete:

- `IKey`
- `KeyValues`
- `KeyFactory`
- scalar key record structs such as `IntKey`, `GuidKey`, and `StringKey`
- `CompositeKey`
- public APIs that accept or expose `IKey`
- relation APIs that expose `IKey`

Any remaining dynamic lookup should be expressed as queries or generated table accessors, not as a universal key bag.

## Benchmark Plan

Required before/after measurements:

- warm primary-key fetch
- cold primary-key fetch
- warm relation traversal
- cold relation traversal
- startup primary-key fetch
- CRUD workflow small
- CRUD workflow batch

Micro probes:

- generated static `Get(int, database)` cache hit
- generated typed-ID `Get(CustomerId, database)` cache hit
- generated relation lookup by one scalar FK
- generated relation lookup by composite FK
- row-cache add for scalar PK
- row-cache add for composite PK
- relation index add/remove
- mutation invalidation for scalar and composite keys

Success criteria:

- generated scalar primary-key cache hit is 0 B/op
- generated scalar relation lookup is 0 B/op
- generated typed-ID primary-key lookup is 0 B/op when converter is allocation-free
- relation traversal does not allocate merely to identify the FK
- composite lookup does not allocate heap memory and does not use `object?[]`
- startup allocations do not regress from eager construction of generated stores

## Test Matrix

Minimum TUnit coverage:

- `int`, `long`, `Guid`, and `string` primary keys
- nullable auto-increment primary keys
- enum-backed keys normalized through provider values
- typed IDs with explicit scalar converters
- typed IDs through a fake adapter/convention plugin
- FK relation loading where PK and FK use the same typed ID
- direct `Get(...)` on generated static methods
- direct `Get(...)` on generated table readers if added
- relation traversal through scalar FK
- relation traversal through composite FK
- cache invalidation after update/delete
- primary-key mutation handling
- query `Where(x => x.Id == id)` using provider parameter values
- local `ids.Contains(x.Id)` using provider parameter values
- joined query materialization without `IKey`

## Risks

- This is a larger change than replacing `IKey` with a concrete `DataLinqKey`.
- Removing `IKey` means every hidden identity dependency must be found and replaced, especially mutation state, equality, relation collections, query materialization, and diagnostics.
- Composite stores are easy to get subtly wrong. Equality, null handling, and hash behavior need direct tests.
- Nested dictionary composite stores avoid lookup-key construction but may cost too much memory for some key distributions.
- Custom bucket stores can be faster and smaller, but they need careful thread-safety work.
- Converter ambiguity can produce correctness bugs if two typed ID libraries expose similar shapes.

## Opinionated Recommendation

Do not implement a universal `DataLinqKey`.

Do not preserve `IKey` as an advanced escape hatch.

Use generated provider-key stores. Start with single-column primary keys and one-column relation indexes because those are the benchmark-sensitive paths and the simplest correctness surface. Then add composite component-addressed stores and delete the legacy key layer once mutation and query materialization no longer depend on it.

The guiding rule is blunt but useful:

> If generated code knows the key columns and provider types, the runtime should never allocate an object to rediscover them.
