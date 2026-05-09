> [!WARNING]
> This document is planning material. It records allocation findings from a code audit after the phase 8 work. It should be validated with fresh BenchmarkDotNet numbers before any change is treated as proven.

# Allocation Reduction Audit

**Status:** Draft audit  
**Created:** 2026-05-09  
**Scope:** provider initialization, generated metadata startup, metadata access, keys, row data, cache internals, and query construction.

## Executive Summary

The blunt answer is that DataLinq is not suffering from one giant allocation leak. It is suffering from a bunch of small, defensible-looking copies that became expensive once phase 8 made provider startup and hot paths more visible.

The most important pattern is repeated array snapshotting from metadata objects. Public getters such as `DatabaseDefinition.TableModels`, `TableDefinition.Columns`, `TableDefinition.PrimaryKeyColumns`, `ColumnDefinition.DbTypes`, and several attribute/value getters return fresh arrays. That is a good immutability instinct, but it is the wrong shape for internal runtime code. The runtime now uses these getters in provider initialization, query construction, cache setup, row reads, model accessors, and LINQ execution. That means we repeatedly allocate arrays just to iterate over already-frozen metadata.

The second major pattern is key materialization. `IKey.Values` returns an `object?[]`, and the cache code reads it frequently. Simple integer and GUID keys should be close to allocation-free once they exist, but every `Values[0]` access allocates a one-element array and boxes value types. Composite keys are worse because `CompositeKey.Values` allocates nested arrays from child keys and then allocates the final array.

The third major pattern is generated metadata startup. Generated metadata avoids reflection, which is good, but it still builds a transient `MetadataDatabaseDraft` graph, converts it into runtime metadata, validates it, freezes it, and binds generated handles. Provider metadata caching hides that work for repeated provider instances, but the cold path is still object-heavy.

Spans can help in tight internal loops and key construction. They are not the main answer for the public API. The better answer is frozen internal arrays, non-copying internal views, lookup maps built once during freeze, and span-based overloads at the small number of places where temporary sequences are currently forced into arrays.

String builders are also not the main problem. `Sql` already uses `StringBuilder`, and `DbCommand.CommandText` eventually needs a `string`. We can reduce intermediate strings and parameter-name arrays, but we cannot make command text generation truly allocation-free.

## Measurement State

The existing benchmark lane is useful but needs a fresh, trustworthy baseline before implementation starts.

The last usable benchmark artifact found during this audit was:

- `artifacts/benchmarks/results/20260428-192551821-ad13addd73d244e3981990af365291bc-summary.json`

That run predates the current phase 8 state, so it should be treated as historical evidence, not the current baseline. It still points at the right areas:

- provider initialization allocated roughly 334 KB per operation for the SQLite provider variants
- startup primary-key fetch allocated roughly 119 KB per operation
- warm primary-key fetch still allocated roughly 15 KB per operation, despite the row cache avoiding database work
- warm relation traversal was in the same allocation neighborhood
- repeated `IN` predicate fetch was materially higher, around the low tens of KB per operation

A fresh run on 2026-05-09 did not produce valid current numbers. The smoke profile produced invalid BenchmarkDotNet job output for provider initialization, and the default profile failed with an `UnauthorizedAccessException` against:

```text
.dotnet\Home\AppData\Local\Microsoft\Windows\INetCache\Content.IE5
```

The benchmark CLI creates child process environments through `BenchmarkCliSettings.CreateProcessEnvironment()`, which uses `DevToolPaths.CreateEnvironment(ToolingProfile.Repo)`. That deliberately pins `APPDATA`, `LOCALAPPDATA`, `HOME`, `USERPROFILE`, `DOTNET_CLI_HOME`, and NuGet paths under the repository. That is the right general idea for reproducible tooling, but BenchmarkDotNet is currently tripping over the repo-local Windows internet cache folder.

The first implementation step should be to fix this measurement blocker, not to guess from code audit alone.

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

This is defensible for a public immutability boundary. It is wasteful for runtime code that is only reading frozen metadata.

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

1. Keep public array-returning properties for compatibility and defensive copying.
2. Add internal non-copying accessors for frozen runtime use.
3. Migrate DataLinq runtime code to those accessors.
4. Add tests that mutating a public returned array does not mutate metadata.

Possible internal API shape:

```csharp
internal ReadOnlySpan<TableDefinition> TableModelsSpan => tableModels;
internal ReadOnlySpan<ColumnDefinition> ColumnsSpan => columns;
internal ReadOnlySpan<ColumnDefinition> PrimaryKeyColumnsSpan => primaryKeyColumns;
internal int ColumnCount => columns.Length;
internal ColumnDefinition GetColumn(int index) => columns[index];
```

For APIs that need to be stored or passed through interfaces, `IReadOnlyList<T>` or internal arrays are usually more practical than `ReadOnlySpan<T>`.

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
    object?[] Values { get; }
    int ValueCount { get; }
    object? GetValue(int index);
    bool TryGetSingleValue(out object? value);
}
```

Then keep `Values` as a compatibility snapshot and migrate runtime code to `GetValue`.

`CompositeKey` should probably store `object?[]` values directly, or at least expose values without forcing nested child-key arrays. If we keep the child-key representation, `CompositeKey.GetValue(int)` must call the child key's non-allocating accessor.

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

Expected impact: medium. The query layer likely still has Remotion/LINQ expression costs outside DataLinq's direct control.

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
- keep an initial snapshot only if a feature actually needs it immediately
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

Do not try to make spans the public long-lived metadata API. `ReadOnlySpan<T>` cannot be stored on classes, cannot be used in async state machines, and is awkward across many interface boundaries. For stable metadata, frozen arrays plus internal span-returning properties are the sweet spot.

Use `IReadOnlyList<T>` when callers need a stable object they can hold. Use `ReadOnlySpan<T>` when callers need fast immediate iteration. Use public array snapshots only at compatibility boundaries.

Use `StringBuilder` for command construction, but accept that the final SQL string has to allocate. The better target is fewer intermediate strings, fewer arrays of parameter names, and more reuse of known SQL shapes.

Do not pool long-lived metadata arrays. Pooling is wrong for frozen metadata because the arrays need to stay alive for the lifetime of the metadata. Pooling helps temporary buffers, not persistent object graphs.

## Proposed Workstreams

### P0: Fix Measurement First

Goal: get valid current numbers before optimization work starts.

Tasks:

- fix the BenchmarkDotNet repo-local `INetCache\Content.IE5` failure
- rerun provider initialization and phase 2 watch benchmarks with the default profile
- store the baseline artifact and note commit SHA/date/profile
- add a benchmark note if smoke profile cannot validly measure provider initialization

Candidate command once the blocker is fixed:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile default --history-json artifacts\benchmarks\history\phase8-allocation-baseline.json
```

Acceptance criteria:

- current provider initialization allocations are known
- warm primary-key fetch allocations are known
- startup primary-key fetch allocations are known
- the benchmark output has no configuration failures

### P1: Add Non-Copying Metadata Runtime Access

Goal: stop internal runtime code from paying public defensive-copy costs.

Tasks:

- add internal span/list accessors to database, table, column, property, enum, and generated declaration metadata types
- add `ColumnCount` and `GetColumn(int)` to `TableDefinition`
- migrate runtime call sites away from public array-copy properties
- preserve public snapshot behavior
- add tests for public array immutability boundaries

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

- public array getters still return defensive snapshots
- runtime provider initialization does not use `TableModels` snapshots
- row construction does not use `Table.Columns.Length`
- unit tests prove snapshot mutation does not mutate metadata

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
- keep `Values` as a compatibility snapshot

Acceptance criteria:

- warm primary-key cache reads do not allocate one-element key value arrays
- composite key access avoids nested child `Values` arrays
- public `Values` behavior remains unchanged

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
- replace or lock mutable reverse-index lists in `IndexCache`
- maintain a running byte count in `RowCache`
- inspect cache policy copies after metadata API changes

Acceptance criteria:

- provider initialization does not eagerly allocate cache history unless needed
- index reverse mappings are thread-safe
- telemetry still reports the same logical values

## Suggested Priority Order

1. Fix benchmark measurement.
2. Add internal non-copying metadata accessors.
3. Add frozen metadata lookup maps.
4. Add non-allocating key value access.
5. Re-measure provider initialization, startup fetch, warm fetch, relation traversal, and `IN` predicates.
6. Optimize generated metadata startup based on measured remaining cost.
7. Clean up query/SQL temporary arrays.
8. Clean up cache snapshot/index/byte-counter issues.

This order matters. If we start with generated metadata builder work before removing metadata getter copies, we risk making a large generator change while leaving the most obvious runtime allocation pattern intact.

## Target Outcomes

The first serious optimization pass should aim for measurable, conservative wins:

- provider initialization allocation reduced by at least 25 percent from the new current baseline
- startup primary-key fetch allocation reduced by at least 20 percent
- warm primary-key fetch allocation reduced enough to prove metadata/key snapshots were removed from the hot path
- no public compatibility break from array-returning metadata getters
- tests covering public snapshot immutability and non-allocating key access
- benchmark history artifact saved with before/after numbers

The warm fetch target should be treated carefully. Some allocation comes from LINQ expression/query infrastructure rather than the cache itself. The goal is not a fantasy zero-allocation ORM call. The goal is to remove DataLinq-owned allocations that are clearly unnecessary.

## Things Not To Do First

- Do not rewrite public metadata APIs to spans. That would be awkward, breaking, and not worth it.
- Do not pool frozen metadata arrays. They are long-lived by design.
- Do not chase the final SQL command string allocation. ADO.NET needs command text as a string.
- Do not remove validation from generated metadata startup just to save allocations.
- Do not optimize provider schema import factories before runtime provider initialization and query hot paths are measured.
- Do not replace all LINQ everywhere. Replace the LINQ that sits on hot paths or forces metadata snapshots.

## Open Questions

- Should public metadata array properties eventually be marked as snapshot APIs and supplemented with public `IReadOnlyList<T>` properties?
- Is metadata cache lifetime correct for all provider scenarios, or do tests intentionally force cold startup often enough that generated metadata construction must be extremely lean?
- Does telemetry/history require an initial cache snapshot at provider construction, or can it be lazy without changing user-observable behavior?
- How much of warm query allocation is DataLinq-owned versus Remotion/LINQ infrastructure?
- Should composite keys continue to be represented as child `IKey` values, or should they store raw values directly?

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
