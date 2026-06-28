> [!WARNING]
> This document is planning material. It records allocation findings from a code audit after the phase 8 work and a local 2026-05-10 benchmark refresh. Each implementation change still needs its own before/after BenchmarkDotNet validation before a win is treated as proven.

# Allocation Reduction Audit

**Status:** Historical allocation audit with Phase 10 and Phase 12 closeout notes. The 2026-05-10 numbers are useful baseline evidence, but the proposed workstreams below are no longer the active execution order. Rerun the benchmark lanes before using this document to schedule new optimization work.
**Created:** 2026-05-09  
**Scope:** provider initialization, generated metadata startup, metadata access, keys, row data, cache internals, and query construction.

## Executive Summary

The blunt answer is that DataLinq is not suffering from one giant allocation leak. It is suffering from a bunch of small, defensible-looking copies that became expensive once phase 8 made provider startup and hot paths more visible.

The most important pattern is repeated array snapshotting from metadata objects. Public getters such as `DatabaseDefinition.TableModels`, `TableDefinition.Columns`, `TableDefinition.PrimaryKeyColumns`, `ColumnDefinition.DbTypes`, and several attribute/value getters return fresh arrays. That is a good immutability instinct, but it is the wrong API shape now that backward compatibility is not a constraint. The runtime now uses these getters in provider initialization, query construction, cache setup, row reads, model accessors, and LINQ execution. That means we repeatedly allocate arrays just to iterate over already-frozen metadata.

The second major pattern is key materialization. `IKey.Values` returns an `object?[]`, and the cache code reads it frequently. Simple integer and GUID keys should be close to allocation-free once they exist, but every `Values[0]` access allocates a one-element array and boxes value types. Composite keys are worse because `CompositeKey.Values` allocates nested arrays from child keys and then allocates the final array.

The third major pattern is generated metadata startup. Generated metadata avoids reflection, which is good, but it still builds a transient `MetadataDatabaseDraft` graph, converts it into runtime metadata, validates it, freezes it, and binds generated handles. Provider metadata caching hides that work for repeated provider instances, but the cold path is still object-heavy. Provider startup should be treated as a first-class hot path: cache lifetime can improve repeated construction, but it should not justify eager cache work during provider construction.

Spans can help in tight internal loops and key construction. They are not the main answer for the public API. The better answer is read-only metadata collections backed by frozen internal arrays, non-copying internal views, lookup maps built once during freeze, and span-based overloads at the small number of places where temporary sequences are currently forced into arrays.

String builders are also not the main problem. `Sql` already uses `StringBuilder`, and `DbCommand.CommandText` eventually needs a `string`. We can reduce intermediate strings and parameter-name arrays, but we cannot make command text generation truly allocation-free.

## Measurement State

The existing benchmark lane is useful again. A local default-profile run on 2026-05-10 completed successfully for `sqlite-memory` on commit `57ce5efd36875f346969d7dfb596ffe21e50e5a2`.

The fresh Phase 2 watchpoint run was:

```powershell
$env:DATALINQ_BENCHMARK_PROVIDERS = 'sqlite-memory'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile default --history-json artifacts\benchmarks\history\allocation-audit-phase2-watch-20260510.json
```

Results:

| Method | Provider | Mean | Allocated | Noise |
| --- | --- | ---: | ---: | ---: |
| Provider initialization | `sqlite-memory` | 2,428.8 us | 899.41 KB | 353.1% |
| Startup primary-key fetch | `sqlite-memory` | 839.5 us | 145.86 KB | 181.6% |
| Warm primary-key fetch | `sqlite-memory` | 264.9 us | 15.75 KB | 228.4% |

The fresh Phase 3 query hot-path run was:

```powershell
$env:DATALINQ_BENCHMARK_PROVIDERS = 'sqlite-memory'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile default --history-json artifacts\benchmarks\history\allocation-audit-phase3-query-hotpath-20260510.json
```

Results:

| Method | Provider | Mean | Allocated | Noise |
| --- | --- | ---: | ---: | ---: |
| Repeated non-PK equality fetch | `sqlite-memory` | 495.1 us | 33.3 KB | 63.4% |
| Repeated scalar `Any` | `sqlite-memory` | 514.0 us | 25.73 KB | 106.0% |
| Repeated `IN` predicate fetch | `sqlite-memory` | 660.5 us | 47.91 KB | 180.3% |

These timings are far too noisy for latency claims. The allocation columns are still good enough to invalidate the stale April numbers and to guide the next allocation-reduction pass. The important correction is brutal: provider initialization is not around 334 KB anymore. The local run measured 899.41 KB, and the May 10 published trend screenshot shows the same order of magnitude at roughly 829 KB.

The benchmark artifacts from this refresh are:

- `artifacts/benchmarks/results/20260510-122107538-ab7fd2d71e034318a04df80b54caca9f-summary.json`
- `artifacts/benchmarks/results/20260510-122228296-b2428406423d436b81c711cb4487a3c5-summary.json`
- `artifacts/benchmarks/history/allocation-audit-phase2-watch-20260510.json`
- `artifacts/benchmarks/history/allocation-audit-phase3-query-hotpath-20260510.json`

## Phase 10 and Phase 12 Closeout

Treat the detailed findings below as May 2026 audit evidence, not as a fresh backlog.

Phase 10 consumed the core key/allocation findings: metadata collection shape was cleaned up, frozen lookup paths were added, generated provider-key cache paths landed, generated relation lookup moved away from the lookup-only `IKey` path, scalar-converter seams were left in the cache identity model, and allocation closeout evidence was recorded.

Phase 11 and Phase 12 consumed the cache-management findings: explicit invalidation APIs, invalidation envelopes, freshness vocabulary, cache telemetry, estimated cache-footprint accounting, byte limits based on estimated footprint, bounded memory-pressure cleanup, and cleanup telemetry have landed. Phase 12 also rejected production value/key deduplication on measurement grounds rather than turning the old global-dedup idea into shipped behavior.

The remaining useful guidance is narrower:

- Use this document to understand why Phases 10 through 12 existed.
- Do not treat the May 10 allocation numbers as current baselines.
- Do not revive row hashing, global key deduplication, or broad cache policy work without new benchmark evidence and a concrete correctness story.
- If provider initialization or query construction becomes a priority again, start with a new benchmark refresh and allocation attribution pass rather than copying the historical workstream list.

## What Phase 8 Already Improved

The current code is already better than the old reflection-heavy shape:

- generated model code exposes static metadata handles through `SetDataLinqGeneratedMetadata`
- generated immutable model accessors use indexed row data reads
- `RowData` stores values densely in an `object?[]` instead of using a per-row dictionary
- generated relation metadata is explicit instead of being rediscovered through reflection
- query SQL uses a `StringBuilder` wrapper rather than repeated string concatenation
- simple select shapes have a `SelectSqlTemplateCache`
- benchmark categories exist for provider initialization, startup fetches, warm fetches, relation traversal, `IN` predicates, and scalar `Any`

Those changes were the right direction. The remaining allocations are now mostly in the supporting plumbing.

## High-Impact Findings

### 1. Metadata Getters Copy Too Much

Several metadata properties return fresh arrays every time they are accessed:

- `DatabaseDefinition.TableModels`
- `DatabaseDefinition.Attributes`
- `TableDefinition.Columns`
- `TableDefinition.PrimaryKeyColumns`
- `ColumnDefinition.DbTypes`
- `PropertyDefinition.Attributes`
- `EnumProperty.DbEnumValues`
- `EnumProperty.CsValuesOrDbValues`
- `GeneratedDatabaseModelDeclaration.TableModels`

This was defensible for a public immutability boundary when compatibility mattered. It is wasteful for runtime code and public callers that only need to read frozen metadata.

Current hot call sites include:

- `DatabaseCache` construction: `Metadata.TableModels.ToDictionary(...)`
- `TableCache` construction: `Table.PrimaryKeyColumns.Length`, `Table.ColumnIndices`
- `RowData` construction: `table.Columns.Length`
- `MutableRowData.GetValue(int)`: `Table.Columns[columnIndex]`
- `SqlQuery<T>` construction: `Provider.Metadata.TableModels.Single(...)`
- `Select.GetColumnsToRead()`: `query.Table.Columns`
- `Select.Execute()`: `query.Table.PrimaryKeyColumns`
- `DbRead`, `Database`, `Transaction`, `ReadOnlyAccess`, `QueryBuilder`, and `QueryExecutor` metadata lookups

Recommended direction:

1. Replace public metadata array properties with stable read-only collection APIs. Backward compatibility is not required here.
2. Use `IReadOnlyList<T>` for public and internal surfaces that need a holdable collection object.
3. Use `ReadOnlySpan<T>` only for immediate internal iteration where the data is already contiguous and the method does not need to store it.
4. Back the read-only collections with frozen metadata-owned arrays or another genuinely immutable collection shape. Do not expose a mutable array instance behind an `IReadOnlyList<T>` property if callers can reasonably cast it back to `T[]`.
5. Add focused tests proving callers cannot mutate metadata through the public collection surface.

Possible internal API shape:

```csharp
internal ReadOnlySpan<TableDefinition> TableModelsSpan => tableModels;
internal ReadOnlySpan<ColumnDefinition> ColumnsSpan => columns;
internal ReadOnlySpan<ColumnDefinition> PrimaryKeyColumnsSpan => primaryKeyColumns;
internal int ColumnCount => columns.Length;
internal ColumnDefinition GetColumn(int index) => columns[index];
```

For APIs that need to be stored or passed through interfaces, `IReadOnlyList<T>` or a cached read-only wrapper over a frozen array is usually more practical than `ReadOnlySpan<T>`.

Expected impact: high. This is cross-cutting and removes allocations from provider initialization, cache construction, query construction, row reads, and LINQ metadata lookup.

### 2. Metadata Needs Frozen Lookup Maps

The runtime repeatedly searches metadata with LINQ:

- table by model type
- table by database name
- column by property name
- column by database column name
- primary key column set
- relation target/source columns

The repeated pattern is `TableModels.Single(...)`, `Columns.Single(...)`, `Columns.SingleOrDefault(...)`, or `Contains(...)` over freshly copied arrays.

This should be moved into lookup maps built once when metadata is frozen. The maps should live on the metadata objects, not in separate runtime caches that have to rediscover the same facts.

Candidate lookups:

- `DatabaseDefinition.GetTableModel(Type modelType)`
- `DatabaseDefinition.TryGetTableModel(Type modelType, out TableDefinition table)`
- `DatabaseDefinition.GetTableModelByDbName(string dbName)`
- `TableDefinition.GetColumnByPropertyName(string propertyName)`
- `TableDefinition.TryGetColumnByPropertyName(string propertyName, out ColumnDefinition column)`
- `TableDefinition.GetColumnByDbName(string dbName)`
- `TableDefinition.GetColumn(int ordinal)`

Expected impact: high. This reduces allocations and CPU at the same time. It also centralizes error messages for missing metadata, which will make diagnostics cleaner.

### 3. Generated Metadata Still Builds a Transient Graph

Generated metadata is not reflection-based anymore, but cold provider initialization still constructs a full draft graph through generated code:

- generated `GetDataLinqGeneratedMetadata()` returns a new `MetadataDatabaseDraft`
- the draft contains generated `MetadataTableModel`, `MetadataColumn`, relation, attribute, and database type objects
- `MetadataTypedDrafts` converts that graph into runtime metadata
- database column types are cloned into runtime `DatabaseColumnType` instances
- the runtime metadata is validated and frozen
- generated static handles are then bound through `SetDataLinqGeneratedMetadata`

Provider metadata caching in `DatabaseDefinition.loadedDatabases` prevents repeated cold builds for the same database type. It does not change the allocation shape of the first provider initialization, and benchmarks intentionally clear the cache to measure that path.

The design target should be extremely lean provider startup. Cache lifetime can stay useful for repeated provider construction, but provider construction should not eagerly build cache state that is only needed after a query or mutation. Generated metadata construction itself should be lean enough that cold startup is respectable without leaning on cache warmup as an excuse.

Recommended direction:

1. Short term: reduce copies inside typed draft conversion and avoid public copying getters while binding generated handles.
2. Medium term: generate a lower-allocation metadata builder path that fills runtime metadata with known capacities and avoids the extra typed draft object layer.
3. Long term: consider generated direct frozen metadata construction, but only if diagnostics remain good.

The direct-frozen approach is tempting, but it is risky if it bypasses useful validation. A builder API that preserves validation while avoiding the transient draft graph is probably the better first design.

Expected impact: high for cold provider initialization. Lower impact for repeated providers once metadata is cached.

### 4. `IKey.Values` Allocates in Hot Cache Paths

`IKey.Values` returns `object?[]`. That design makes the interface easy to consume, but it makes cache internals allocate for basic operations.

Examples:

- `IntKey.Values => [Value]`
- `GuidKey.Values => [Value]`
- `StringKey.Values => [Value]`
- `CompositeKey.Values => keys.Select(k => k.Values.FirstOrDefault()).ToArray()`

Hot call sites include:

- `TableCache.GetKeys(...)`
- `TableCache.GetRowDataFromPrimaryKeys(...)`
- relation foreign key matching
- index cache maintenance
- state change primary key handling

The simple-key fast path in `KeyFactory.GetKey(IDataLinqDataReader, ColumnDefinition[])` is already good. The problem is after keys exist: reading values back out of a key is allocation-heavy.

Recommended direction:

```csharp
public interface IKey
{
    IReadOnlyList<object?> Values { get; }
    int ValueCount { get; }
    object? GetValue(int index);
    bool TryGetSingleValue(out object? value);
}
```

Then migrate runtime code to `GetValue` or `TryGetSingleValue`.

`CompositeKey` should store raw values directly if that keeps the implementation simple. That is the cleaner representation for cache lookups because the cache wants values, not a tree of child key objects. If raw storage makes the code notably more complex, keep child keys but make `CompositeKey.GetValue(int)` use the child key's non-allocating accessor. What should not survive is the current nested `Values` allocation pattern.

Expected impact: high for warm primary-key fetches, relation traversal, and index maintenance. This is one of the clearest examples where the current design allocates for no real semantic reason.

### 5. Row Data Still Reaches Through Copying Metadata APIs

`RowData` itself is a good phase 8 improvement. It stores values in a dense `object?[]` and supports indexed reads. The remaining problem is that it still asks metadata for copied arrays in places where it only needs a count or an indexed column.

Examples:

- `RowData` constructor uses `table.Columns.Length`
- `MutableRowData.GetValue(int)` uses `Table.Columns[columnIndex]`
- `RowData.GetColumnAndValues()` uses `Table.Columns.Select(...)`

Recommended direction:

- add `TableDefinition.ColumnCount`
- add `TableDefinition.GetColumn(int index)`
- make row construction use `ColumnCount`
- make mutable row indexed reads use `GetColumn(index)`
- keep `GetColumnAndValues()` as a diagnostic/convenience API, not something hot paths use

Expected impact: medium to high. The actual row array allocation remains necessary, but metadata array snapshots around it are not.

## Medium-Impact Findings

### 6. Query Construction Allocates Through LINQ and Temporary Arrays

The query layer has several allocation sources:

- metadata copies from `Table.Columns` and `Table.PrimaryKeyColumns`
- LINQ scans over copied metadata arrays
- `Where.In(IEnumerable<V>)` forces `values.ToArray()`
- `ValueOperand.Value(IEnumerable<object>)` copies values with collection expressions
- `WhereGroup.AddCommandParameters(...)` returns `string[]` parameter names
- `ReadPrimaryAndForeignKeys(...)` uses `Concat`, `Distinct`, `ToArray`, `GroupBy`, and more `ToArray`

Some of this is unavoidable because a query object needs stable values after construction. But we can avoid array creation where the input is already an array/span and where parameter names can be appended directly to SQL.

Recommended direction:

- add overloads for array/span inputs where call sites already have stable arrays
- avoid returning parameter-name arrays when the callee can append parameter placeholders directly
- use metadata lookup maps instead of repeated `Single` over columns
- extend SQL template caching only after metadata/key fixes have landed and new numbers show query construction remains a dominant allocation source

Expected impact: medium. The query layer now has DataLinq-owned expression parsing, plan building, SQL rendering, and projection evaluation costs that should be measured directly before making fresh allocation claims.

### 7. SQL Text Is Not Zero-Allocation, and That Is Fine

`Sql` already wraps `StringBuilder`, which is the correct basic primitive for command text construction. The final command text must become a `string` for `DbCommand.CommandText`.

The realistic target is not "zero allocation SQL." The realistic target is:

- avoid repeated `Sql.Text` calls before the final command is needed
- avoid intermediate formatted strings
- avoid parameter-name arrays when direct append works
- cache common command shapes where stable

Expected impact: medium to low. It is worth cleanup, but this should not be first.

### 8. Cache Snapshots Are Created During Provider Initialization

`DatabaseCache` creates a snapshot in its constructor:

```csharp
CacheHistory = [MakeSnapshot()];
```

That means provider initialization allocates snapshot state even before the user has queried or mutated anything.

Recommended direction:

- make the first snapshot lazy
- do not perform cache-history work during provider construction unless a feature proves it needs that exact eager behavior
- ensure telemetry/history behavior remains unchanged from a public point of view

Expected impact: medium for provider initialization. The exact size needs measurement.

### 9. Cache Policy Copies Metadata Policy Arrays

`DatabaseCachePolicy.FromMetadata(...)` copies metadata cache policy settings into runtime policy records. That is not automatically wrong. Cache policy is configuration, and detaching it from mutable metadata used to be reasonable.

After metadata is frozen, we should decide whether those copies still buy anything.

Recommended direction:

- after non-copying metadata APIs exist, inspect policy copies again
- only remove them if the runtime policy can safely reference frozen metadata-owned arrays

Expected impact: low to medium.

### 10. Provider Schema Factories Allocate Heavily, but They Are Not the Main Runtime Path

`MetadataFromSQLiteFactory` and the MySQL/MariaDB metadata factories use a lot of LINQ, `ToList`, `ToArray`, and temporary provider draft structures.

That is real allocation work, but it mostly affects schema import/generation/tooling paths rather than normal generated runtime provider initialization.

Recommended direction:

- do not optimize provider schema factories first
- revisit if CLI schema import or dynamic metadata loading becomes a measured allocation problem

Expected impact: low for generated runtime startup, potentially higher for tooling.

## Correctness-Adjacent Findings

### 11. `IndexCache` Uses Mutable Lists Inside a Concurrent Dictionary

`IndexCache` stores reverse mappings in a `ConcurrentDictionary<IKey, List<IKey>>`. `AddOrUpdate` mutates the list, and `TryRemovePrimaryKey` calls `ToList()` to avoid mutating while enumerating.

That is both an allocation smell and a concurrency smell.

Recommended direction:

- either protect each list mutation with a lock
- or replace list values with immutable/frozen arrays on update
- or use a small purpose-built bucket type with internal locking and non-allocating snapshots for common cases

Expected impact: correctness first, allocation second. This is not the biggest startup issue, but it is the kind of internal structure that can become painful under concurrent cache updates.

### 12. `RowCache.TotalBytes` Recomputes Queue Size

`RowCache.TotalBytes` sums the rows queue on every call:

```csharp
rows.Values.Sum(x => x.SizeBytes)
```

That is not primarily an allocation bug, but it is needless repeated work.

Recommended direction:

- maintain a running byte counter when rows are added and removed

Expected impact: low unless telemetry reads this frequently.

## Spans, Arrays, and Builders: Practical Guidance

Use spans where the data is already contiguous and the method does not need to store it:

- row construction from data reader columns
- key construction from temporary values
- generated metadata builder loops
- SQL parameter append loops
- internal metadata iteration

Do not try to make spans the public long-lived metadata API. `ReadOnlySpan<T>` cannot be stored on classes, cannot be used in async state machines, and is awkward across many interface boundaries. For stable metadata, read-only collections backed by frozen arrays plus internal span-returning helpers are the sweet spot.

Use `IReadOnlyList<T>` when callers need a stable object they can hold. Use `ReadOnlySpan<T>` when callers need fast immediate iteration. Do not keep public array snapshots for metadata just for compatibility; compatibility is not a constraint for this cleanup.

Use `StringBuilder` for command construction, but accept that the final SQL string has to allocate. The better target is fewer intermediate strings, fewer arrays of parameter names, and more reuse of known SQL shapes.

Do not pool long-lived metadata arrays. Pooling is wrong for frozen metadata because the arrays need to stay alive for the lifetime of the metadata. Pooling helps temporary buffers, not persistent object graphs.

## Historical Proposed Workstreams

The workstreams below are retained for traceability. They explain the execution thinking that fed Phases 10 through 12, but they are not the current roadmap queue.

### P0: Keep Measurement Honest

Goal: keep using valid current numbers before and after optimization work.

Tasks:

- rerun provider initialization and phase 2 watch benchmarks before claiming startup wins
- rerun the Phase 3 query hot-path lane before claiming query-construction wins
- store the baseline artifact and note commit SHA/date/profile
- treat the current default-profile timing means as noisy, but the allocation columns as useful

Current baseline commands:

```powershell
$env:DATALINQ_BENCHMARK_PROVIDERS = 'sqlite-memory'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile default --history-json artifacts\benchmarks\history\allocation-audit-phase2-watch-20260510.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile default --history-json artifacts\benchmarks\history\allocation-audit-phase3-query-hotpath-20260510.json
```

Acceptance criteria:

- provider initialization allocation is compared against the 899.41 KB local baseline
- startup primary-key fetch allocation is compared against the 145.86 KB local baseline
- warm primary-key fetch allocation is compared against the 15.75 KB local baseline
- repeated non-PK equality, scalar `Any`, and `IN` predicate allocations are compared against the May 10 Phase 3 lane

### P1: Replace Metadata Array APIs With Read-Only Collections

Goal: stop runtime and public callers from paying defensive-copy costs for frozen metadata.

Tasks:

- replace public metadata array properties with `IReadOnlyList<T>` or the most appropriate stable read-only collection type
- add internal span/list accessors to database, table, column, property, enum, and generated declaration metadata types where they still add value
- add `ColumnCount` and `GetColumn(int)` to `TableDefinition`
- migrate runtime call sites away from array-copy properties
- remove public snapshot behavior unless a specific API still needs it
- add tests for public metadata immutability boundaries

Likely files:

- `src/DataLinq.SharedCore/Metadata/DatabaseDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/TableDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/ColumnDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/PropertyDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/GeneratedTableModelDeclaration.cs`
- `src/DataLinq/Instances/RowData.cs`
- `src/DataLinq/Cache/DatabaseCache.cs`
- `src/DataLinq/Cache/TableCache.cs`
- `src/DataLinq/Query/SqlQuery.cs`
- `src/DataLinq/Query/Select.cs`
- `src/DataLinq/Linq/QueryBuilder.cs`
- `src/DataLinq/Linq/QueryExecutor.cs`

Acceptance criteria:

- public metadata collections are stable and non-mutating
- runtime provider initialization does not use `TableModels` snapshots
- row construction does not use `Table.Columns.Length`
- unit tests prove public callers cannot mutate metadata

### P1: Add Frozen Metadata Lookup Maps

Goal: replace repeated scans and copied arrays with O(1) metadata lookups.

Tasks:

- build table lookup maps when `DatabaseDefinition` freezes
- build column lookup maps when `TableDefinition` freezes
- migrate table/column resolution in query, read, transaction, and LINQ code
- keep lookup error messages specific and helpful

Acceptance criteria:

- table lookup by model type is non-allocating
- column lookup by property name is non-allocating
- hot code no longer does `Provider.Metadata.TableModels.Single(...)`
- hot code no longer does `Table.Columns.Single(...)` for simple metadata lookup

### P1: Add Non-Allocating Key Value Access

Goal: stop cache code from allocating arrays just to read key values.

Tasks:

- add `ValueCount`, `GetValue(int)`, and `TryGetSingleValue(...)` to `IKey`
- update simple key implementations
- update `CompositeKey`
- add span-based key factory overloads where useful
- migrate `TableCache`, relation handling, and index handling away from `Values`
- make `Values` a read-only collection surface, not an array snapshot

Acceptance criteria:

- warm primary-key cache reads do not allocate one-element key value arrays
- composite key access avoids nested child `Values` arrays
- public `Values` remains read-only and does not expose mutable key internals

### P2: Lower Generated Metadata Startup Allocation

Goal: reduce cold provider initialization allocations after the broad internal-copy problem is fixed.

Tasks:

- profile typed draft conversion after P1 changes
- reduce `DatabaseColumnType` clone churn where safe
- generate capacity-aware metadata builder code
- preserve validation and diagnostics
- consider direct runtime metadata construction only after the builder path proves insufficient

Acceptance criteria:

- provider initialization allocation drops materially from the measured baseline
- validation failures still point at useful metadata locations
- generated code remains readable enough to debug

### P2: Reduce Query and SQL Temporary Arrays

Goal: remove unnecessary temporary values in query construction without overfitting.

Tasks:

- add array/span overloads for `In` and key query paths where inputs are already materialized
- avoid returning `string[]` parameter names from predicate helpers when direct SQL append is possible
- audit `ReadPrimaryAndForeignKeys` for unnecessary `Distinct`, `GroupBy`, and `ToArray`
- extend template caching only where measurements show repeated shapes

Acceptance criteria:

- repeated `IN` predicate allocation drops from the current baseline
- warm primary-key fetch does less query-construction allocation
- query behavior remains identical

### P3: Cache Internals Cleanup

Goal: clean up smaller allocation and correctness issues.

Tasks:

- make `DatabaseCache` initial snapshot lazy if behavior allows
- avoid other provider-construction cache work unless it is required for observable behavior
- replace or lock mutable reverse-index lists in `IndexCache`
- maintain a running byte count in `RowCache`
- inspect cache policy copies after metadata API changes

Acceptance criteria:

- provider initialization does not eagerly allocate cache history unless needed
- index reverse mappings are thread-safe
- telemetry still reports the same logical values

## Historical Suggested Priority Order

1. Keep benchmark measurement current.
2. Replace metadata array APIs with read-only collections and add internal non-copying accessors where useful.
3. Add frozen metadata lookup maps.
4. Add non-allocating key value access.
5. Re-measure provider initialization, startup fetch, warm fetch, relation traversal, and `IN` predicates.
6. Optimize generated metadata startup based on measured remaining cost.
7. Clean up query/SQL temporary arrays.
8. Clean up cache snapshot/index/byte-counter issues.

This order matters. If we start with generated metadata builder work before removing metadata getter copies, we risk making a large generator change while leaving the most obvious runtime allocation pattern intact.

## Historical Target Outcomes

The first serious optimization pass should aim for measurable, conservative wins:

- provider initialization allocation reduced by at least 25 percent from the new current baseline
- startup primary-key fetch allocation reduced by at least 20 percent
- warm primary-key fetch allocation reduced enough to prove metadata/key snapshots were removed from the hot path
- public metadata arrays replaced with stable read-only collection APIs
- tests covering public collection immutability and non-allocating key access
- benchmark history artifact saved with before/after numbers

The warm fetch target should be treated carefully. Some allocation comes from LINQ expression/query infrastructure rather than the cache itself. The goal is not a fantasy zero-allocation ORM call. The goal is to remove DataLinq-owned allocations that are clearly unnecessary.

## Things Not To Do First

- Do not rewrite public metadata APIs to spans. That would be awkward and not worth it.
- Do not pool frozen metadata arrays. They are long-lived by design.
- Do not chase the final SQL command string allocation. ADO.NET needs command text as a string.
- Do not remove validation from generated metadata startup just to save allocations.
- Do not optimize provider schema import factories before runtime provider initialization and query hot paths are measured.
- Do not replace all LINQ everywhere. Replace the LINQ that sits on hot paths or forces metadata snapshots.

## Resolved Questions And Remaining Gaps

- Public metadata array properties should be replaced with stable read-only collection APIs. Do not keep array snapshots for backward compatibility.
- Provider startup should be optimized as an extremely lean path. Cache lifetime may still help repeated construction, but provider construction should not eagerly do cache work that can be lazy.
- Telemetry/history should be lazy unless a concrete public behavior requires an initial cache snapshot at provider construction.
- The current benchmark lane does not split warm query allocation between parser/planning, SQL rendering, materialization, and cache access. Current `sqlite-memory` totals are 15.75 KB for warm primary-key fetch, 33.3 KB for repeated non-PK equality, 25.73 KB for repeated scalar `Any`, and 47.91 KB for repeated `IN` predicate fetch. A fresh allocation attribution pass is needed before claiming which part belongs to the 0.8 parser and plan renderer.
- Composite keys should store raw values directly if that keeps the code clean. If direct raw storage makes the implementation ugly, keep child `IKey` values but expose composite values without nested array allocation.

## Reference Files

- `src/DataLinq/Database/DatabaseProvider.cs`
- `src/DataLinq.SharedCore/Factories/MetadataDefinitionFactory.cs`
- `src/DataLinq.SharedCore/Factories/MetadataTypedDrafts.cs`
- `src/DataLinq.SharedCore/Metadata/DatabaseDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/TableDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/ColumnDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/PropertyDefinition.cs`
- `src/DataLinq.SharedCore/Metadata/GeneratedTableModelDeclaration.cs`
- `src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs`
- `src/DataLinq/Instances/RowData.cs`
- `src/DataLinq/Instances/IKey.cs`
- `src/DataLinq/Instances/KeyFactory.cs`
- `src/DataLinq/Cache/DatabaseCache.cs`
- `src/DataLinq/Cache/TableCache.cs`
- `src/DataLinq/Cache/IndexCache.cs`
- `src/DataLinq/Cache/RowCache.cs`
- `src/DataLinq/Query/Sql.cs`
- `src/DataLinq/Query/SqlQuery.cs`
- `src/DataLinq/Query/Select.cs`
- `src/DataLinq/Query/Where.cs`
- `src/DataLinq/Linq/QueryBuilder.cs`
- `src/DataLinq/Linq/QueryExecutor.cs`
- `src/DataLinq.Benchmark/EmployeesBenchmarks.cs`
- `src/DataLinq.Benchmark/BenchmarkContext.cs`
- `src/DataLinq.Benchmark.CLI/BenchmarkCliSettings.cs`
- `src/DataLinq.DevTools/DevToolPaths.cs`
