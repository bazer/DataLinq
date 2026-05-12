> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 10 Implementation Plan: Key and Allocation Foundation

**Status:** Next implementation priority.

## Purpose

Phase 10 removes allocation and key-identity debt before DataLinq widens cache semantics or join behavior.

The core problem is blunt: generated code knows the table, primary-key columns, foreign-key columns, provider CLR types, relation shape, and row ordinals, but too many runtime paths still route through generic metadata scans, defensive array snapshots, `IKey`, and `object?[]` key bags. That is the wrong foundation for external invalidation, joins, scalar converters, and result-set caching.

Phase 10 should not try to finish every future cache idea. It should create the lower-allocation, provider-key-oriented substrate the next phases can safely build on.

## Phase-Start Baseline

Start from the Phase 9A closeout state:

- cache invalidation behavior has targeted tests
- cache maintenance emits telemetry
- benchmark history can show profile, last-run date, trends, and allocation deltas
- generated metadata startup and indexed row access exist
- `IKey` still appears in cache, relation, query, and mutation paths
- metadata getters still need an audit for defensive snapshots and repeated lookup scans

Before changing code, refresh the allocation baselines that this phase will claim against.

Required baseline lanes:

```powershell
$env:DATALINQ_BENCHMARK_PROVIDERS = 'sqlite-memory'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile default --history-json artifacts\benchmarks\history\phase10-baseline-phase2-watch.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile default --history-json artifacts\benchmarks\history\phase10-baseline-phase3-query-hotpath.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase10-key-foundation --profile default --history-json artifacts\benchmarks\history\phase10-baseline-key-foundation.json
```

If benchmark naming changes, preserve the intent: provider initialization, startup primary-key fetch, warm primary-key fetch, repeated non-PK equality, scalar `Any`, `IN` predicates, and relation traversal.

## Goals

- remove defensive metadata array snapshots from runtime provider initialization and hot query/cache paths
- add frozen metadata lookup maps for common table and column resolution
- introduce provider-key cache access paths for generated scalar primary keys
- remove lookup-only `IKey` construction from generated relation traversal for common scalar-FK paths
- provide a transitional non-allocating key value API for any `IKey` paths that cannot be deleted safely in one pass
- make joined/query materialization able to read provider key components directly
- leave Phase 11 with provider-key invalidation hooks instead of `IKey`-first invalidation hooks
- measure before/after allocation changes and document remaining identity debt honestly

## Non-Goals

- public cache clearing APIs
- memory-pressure cleanup and adaptive scheduling
- global value/key interning
- full `IKey` deletion if mutation/query compatibility makes that too risky for one phase
- full scalar converter/typed-ID ergonomics
- relation-aware join syntax
- Remotion replacement

## Design Constraints

- Provider key values are the cache identity boundary.
- Generated scalar primary-key cache hits should avoid DataLinq-owned heap allocation.
- Generated relation traversal should not allocate a lookup-only key object.
- Composite keys must remain correct even if their first implementation is less optimized than scalar keys.
- Transitional compatibility is allowed, but it must be explicit and have a removal owner.
- Public invalidation work in Phase 11 must not be forced to expose `IKey`.
- Scalar converter support should have a seam in the design, but full converter resolution can wait for Phase 15.

## Workstream A: Measurement And Allocation Attribution

Goals:

- avoid optimizing against stale numbers
- make Phase 10 closeout falsifiable

Tasks:

1. Refresh Phase 2 watch and Phase 3 query hot-path benchmark baselines.
2. Add or confirm benchmark probes for:
   - warm generated static `Get(...)`
   - warm relation traversal
   - cache row add/get/remove for scalar primary keys
   - generated join materialization if a probe already exists
3. Capture commit SHA, date, profile, provider, and result artifact paths.
4. Treat latency means as noisy unless the benchmark report proves otherwise; use allocation columns as the main signal.
5. Add a short Phase 10 closeout note when the phase finishes.

Exit criteria:

- baseline artifacts exist before implementation claims are made
- closeout can compare provider initialization, startup primary-key fetch, warm primary-key fetch, relation traversal, and query hot paths against pre-phase numbers

## Workstream B: Stable Metadata Collections

Goals:

- stop paying for defensive snapshots in frozen metadata
- keep public metadata immutable without returning fresh arrays on every access

Tasks:

1. Audit metadata properties that return arrays or build temporary collections.
2. Replace hot/public snapshot properties with stable read-only collection surfaces where compatibility allows.
3. Add internal non-copying accessors for immediate iteration and indexed access.
4. Add `ColumnCount` and `GetColumn(int)` style APIs where row/cache code only needs indexed metadata.
5. Migrate runtime call sites away from metadata array snapshots.
6. Add tests proving public callers cannot mutate metadata through the new collection surface.

Likely files:

- `src/DataLinq.SharedCore/Metadata/DatabaseDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/TableDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/ColumnDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/PropertyDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/GeneratedTableModelDeclaration.cs`
- `src/DataLinq/Instances/RowData.cs`
- `src/DataLinq/Query/SqlQuery.cs`
- `src/DataLinq/Query/Select.cs`
- `src/DataLinq/Linq/QueryBuilder.cs`
- `src/DataLinq/Linq/QueryExecutor.cs`

Exit criteria:

- runtime provider initialization no longer depends on metadata array snapshots for table/model iteration
- row construction and indexed row reads use stable metadata count/index APIs
- tests cover immutability of the public metadata collection surface

## Workstream C: Frozen Metadata Lookup Maps

Goals:

- replace repeated scans with metadata-owned lookup maps
- centralize lookup diagnostics

Tasks:

1. Build lookup maps when database/table metadata is frozen.
2. Add table lookups by model type and database table name.
3. Add column lookups by property name, database column name, and ordinal.
4. Migrate provider, query, cache, read, transaction, and LINQ code off repeated `Single(...)` and `SingleOrDefault(...)` scans.
5. Keep error messages specific enough to identify database, table, and column.

Candidate APIs:

```csharp
DatabaseDefinition.GetTableModel(Type modelType)
DatabaseDefinition.TryGetTableModel(Type modelType, out TableDefinition table)
DatabaseDefinition.GetTableModelByDbName(string dbName)
TableDefinition.GetColumnByPropertyName(string propertyName)
TableDefinition.TryGetColumnByDbName(string dbName, out ColumnDefinition column)
TableDefinition.GetColumn(int ordinal)
```

Exit criteria:

- hot code does not use copied `TableModels` or `Columns` collections for simple lookup
- lookup errors are more centralized, not less helpful
- allocation benchmarks show reduced metadata lookup churn

## Workstream D: Transitional Non-Allocating `IKey` Access

Status: corrected implementation complete as of 2026-05-12.

Goals:

- reduce key allocation immediately
- make remaining legacy key use visible
- avoid blocking Phase 10 on a risky big-bang `IKey` deletion

Tasks:

1. Add non-allocating value access to `IKey` if any legacy path must remain:

```csharp
int ValueCount { get; }
object? GetValue(int index);
bool TryGetSingleValue(out object? value);
```

2. Update simple key implementations to return values without allocating `object?[]`.
3. Update composite key access to avoid nested `Values` array construction.
4. Migrate `TableCache`, relation matching, index maintenance, and mutation invalidation off `IKey.Values`.
5. Mark `Values` as a compatibility surface if it cannot be removed yet.
6. Add tests that simple and composite key reads do not allocate just to inspect key components.

Implementation notes:

- Simple and composite `IKey` implementations expose `ValueCount`, `GetValue(int)`, and `TryGetSingleValue(out object?)` without routing through `Values`.
- Mutable primary-key recomputation, relation foreign-key grouping, and index invalidation now use indexed `KeyFactory.GetKey(...)` helpers instead of enumerable key-value bridges.
- Cache SQL key predicates use indexed `IKey` component reads directly; remaining `IKey.Values` coverage is compatibility/defensive-copy behavior, not ordinary cache inspection.
- Full `IKey` dependency removal is owned by Workstream E provider-key row stores and Phase 11 provider-key invalidation APIs.

Verification:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq\DataLinq.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug` (`538/538` passed)
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build` (`538/538` passed)
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build` (`405/405` passed for `sqlite-file` and `sqlite-memory`)

Exit criteria:

- no hot cache path reads `IKey.Values` for ordinary key inspection
- simple key inspection does not allocate a one-element array
- remaining `IKey` dependencies are documented with owners for later removal

## Workstream E: Provider-Key Row Stores

Status: complete as of 2026-05-12.

Goals:

- let generated runtime paths address row caches with provider key components directly
- give Phase 11 a provider-key invalidation substrate

Tasks:

1. Define table key-shape metadata or generated key descriptors:
   - key arity
   - provider CLR types
   - model CLR types
   - nullability
   - column ordinals
   - scalar-converter handle placeholder
2. Add a single provider-key row store API that is generic over the table's actual provider-key type.
3. Add a composite-key design that avoids `object?[]` and `IKey` storage on generated lookup paths.
4. Route generated static `Get(...)` through provider-key store access for scalar and composite primary keys.
5. Keep legacy dynamic lookup working through a clearly isolated adapter if needed.
6. Add tests for `int`, `long`, `Guid`, `string`, generated composite provider keys, and legacy composite rejection.

Implementation notes:

- `TableDefinition.PrimaryKeyShape` describes primary-key arity, component ordinals, CLR types, nullability, scalar store kind, and the placeholder converter handle.
- The initial side-dictionary implementation was rejected because it stored every key twice. The corrected design gives each `RowCache` exactly one `RowStore<TKey>` selected by the table's provider-key shape.
- `RowCache` no longer has an `IKey` dictionary, scalar side stores, or a duplicate key-age queue. Rows, byte totals, and age cleanup are owned by the single typed store.
- `RowCache.TryRemoveProviderKey(...)` and `TableCache.TryRemoveProviderKey(...)` provide the provider-key removal hook Phase 11 needs without coordinating with legacy key storage.
- Generated scalar primary-key `Get(...)` methods call `IImmutable<T>.GetByProviderKey(...)` with the scalar provider value directly.
- Generated composite primary-key `Get(...)` methods use an internal generated `DataLinqPrimaryKey` record struct that implements the provider-key component reader and acts as the cache key. The struct is stored directly by `Dictionary<DataLinqPrimaryKey, ...>`, so the cache does not box scalar components or keep a parallel `IKey`.
- Generated metadata also carries a provider-key row-store accessor per primary-key table. Query and relation materialization use that accessor to populate the same typed store as generated static `Get(...)`, so composite tables do not lose cache population when rows arrive outside the direct lookup path.
- Legacy `IKey` lookup/add/remove paths are compatibility adapters only. Scalar legacy keys can create the typed store for existing dynamic call sites; composite legacy keys can only adapt into an already-created generated composite store via the generated converter. A bare legacy composite key is not allowed to create an `IKey`-backed row store.

Verification:

- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq\DataLinq.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Tests.Unit\DataLinq.Tests.Unit.csproj -c Debug` (`544/544` passed)
- `.\scripts\dotnet-sandbox.ps1 test --project src\DataLinq.Generators.Tests\DataLinq.Generators.Tests.csproj -c Debug` (`32/32` passed)
- `.\scripts\dotnet-sandbox.ps1 build src\DataLinq.Tests.Models\DataLinq.Tests.Models.csproj -c Debug -v minimal --no-incremental`
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build` (`544/544` passed)
- `.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build` (`405/405` passed for `sqlite-file` and `sqlite-memory`)

Initial internal shape:

```csharp
public sealed class TableKeyShape
{
    int Arity { get; }
    MetadataCollection<TableKeyComponentDefinition> Components { get; }
    bool SupportsScalarProviderKeyStore { get; }
}

internal sealed class RowStore<TKey>
    where TKey : notnull
{
    public bool TryGet(TKey key, out IImmutableInstance? row);
    public bool TryAdd(TKey key, int size, IImmutableInstance row);
    public bool TryRemove(TKey key, out int numRowsRemoved);
}

public interface IProviderKeyRowStoreAccessor
{
    bool TryAddRow(RowCache cache, RowData rowData, IImmutableInstance row);
}
```

Exit criteria:

- generated scalar primary-key `Get(...)` can hit cache without constructing `IKey`
- generated provider-key removal exists for Phase 11 row invalidation
- generated composite primary-key `Get(...)` can hit cache through a generated provider-key struct without constructing or storing `IKey`

## Workstream F: Generated Relation Provider-Key Access

Goals:

- stop generated relation traversal from creating lookup-only relation keys
- align relation cache identity with provider-key row identity

Tasks:

1. Identify generated relation property shapes that currently call `GetRelationKey(...)` or equivalent key factories.
2. Generate relation helper calls that pass provider FK components directly.
3. Add relation index store APIs that accept provider components.
4. Preserve lazy relation collection behavior and cache policy behavior.
5. Add tests for one-column FK relation traversal.
6. Add at least one composite FK regression test, even if the first composite path is less optimized.

Exit criteria:

- generated scalar-FK relation traversal avoids lookup-only `IKey`
- relation index add/get/remove can operate from provider key components
- relation traversal allocation improves or the remaining allocation is documented

## Workstream G: Query And Materialization Key Reads

Goals:

- stop query/materialization paths from creating generic key objects where provider components are already available
- prepare joined materialization for Phase 13

Tasks:

1. Replace simple-primary-key query extraction that returns `IKey` with provider-key-aware results where practical.
2. Read primary-key components from data readers by ordinal into table-specific store accessors.
3. Update joined materialization to pass provider key components for each joined source slot where the current architecture allows it.
4. Keep unsupported or dynamic query shapes on the transitional path with diagnostics or comments.
5. Add tests that materialization still uses the cache identity consistently after update/delete/reload.

Exit criteria:

- ordinary materialization can populate/read cache entries through provider-key components
- joined materialization has a clear provider-key path or a documented blocker for Phase 13
- behavior remains identical for supported query shapes

## Workstream H: Scalar Converter Seam

Goals:

- avoid designing provider-key stores that assume model type equals provider type forever
- keep Phase 15 from requiring another cache identity rewrite

Tasks:

1. Add key-shape fields or placeholders for provider CLR type and model CLR type.
2. Ensure provider-key caches are named and typed around provider values.
3. Avoid public APIs that imply model values are always cache keys.
4. Document the exact places Phase 15 must plug converter calls into:
   - generated `Get(...)`
   - relation traversal
   - query constants
   - mutation/default value handling
   - schema validation

Exit criteria:

- Phase 10 does not ship full scalar converters
- Phase 15 can add converter resolution without changing the cache identity model again

## Workstream I: Handoff To Phase 11

Goals:

- make explicit cache invalidation straightforward
- prevent Phase 11 from guessing at Phase 10 internals

Phase 10 should leave these handoff artifacts:

- provider-key table/key descriptor or generated accessor shape
- row-cache remove path by provider key components
- relation/index invalidation hook by provider key components or documented conservative table-level fallback
- telemetry names or extension points for key/cache operations
- list of remaining `IKey` dependencies with whether Phase 11 may touch them
- before/after benchmark artifact paths

Exit criteria:

- Phase 11 can design public invalidation around provider key components
- any remaining legacy key bridge is explicit and not accidentally promoted to public API

## Verification

Routine checks:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Provider checks after cache/materialization changes:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --targets sqlite-file,sqlite-memory --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite mysql --alias latest --output failures --build
```

Benchmark checks:

```powershell
$env:DATALINQ_BENCHMARK_PROVIDERS = 'sqlite-memory'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile default --history-json artifacts\benchmarks\history\phase10-closeout-phase2-watch.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile default --history-json artifacts\benchmarks\history\phase10-closeout-phase3-query-hotpath.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase10-key-foundation --profile default --history-json artifacts\benchmarks\history\phase10-closeout-key-foundation.json
```

Add focused tests for:

- metadata collection immutability
- metadata lookup maps
- scalar primary-key cache hit without `IKey` construction
- generated relation traversal without lookup-only key construction
- update/delete invalidation after provider-key cache changes
- composite key correctness

## Risk Register

| Risk | Severity | Mitigation |
| --- | --- | --- |
| Metadata API change leaks mutable internals | High | Use stable read-only surfaces, tests, and avoid exposing raw mutable arrays. |
| Provider-key stores duplicate legacy cache state | High | Keep one authoritative row identity path per table; adapters should bridge, not mirror. |
| Composite keys become incorrect while scalar keys improve | High | Add composite correctness tests even if scalar optimization lands first. |
| Phase 10 grows into full scalar converter work | Medium | Add converter seams only; keep converter resolution and typed-key ergonomics in Phase 15. |
| `IKey` survives by inertia | Medium | Require a closeout list of remaining `IKey` dependencies with owners. |
| Benchmarks show no visible allocation win | Medium | Attribute remaining allocations honestly and avoid claiming latency wins from noisy means. |

## Release Acceptance Criteria

Phase 10 can close when:

- metadata snapshot allocation has been removed from the targeted runtime hot paths
- metadata lookup maps replace repeated scans in query/cache/provider paths
- generated scalar primary-key cache hits avoid lookup-only `IKey` construction
- generated scalar relation traversal avoids lookup-only relation key construction
- materialization has a provider-key component path suitable for Phase 13 joins
- Phase 11 has provider-key invalidation hooks or a documented temporary bridge
- before/after benchmark artifacts exist and the closeout states what improved, what did not, and why
