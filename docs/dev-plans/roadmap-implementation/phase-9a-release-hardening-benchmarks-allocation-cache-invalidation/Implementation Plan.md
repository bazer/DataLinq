> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 9A Implementation Plan: Release Hardening, Benchmarks, Allocation, and Cache Invalidation

**Status:** In progress. Workstream A warning cleanup is complete as of 2026-05-10.

## Purpose

Phase 9A turns the current cleanup, benchmark, allocation, and cache-invalidation plans into one release boundary.

This is the phase where DataLinq should become easier to trust before it becomes more ambitious. The right outcome is a quieter compiler, a better benchmark story, lower measured allocation, and cache invalidation behavior that is characterized and conservative. The wrong outcome would be adding row hashing, external invalidation hooks, adaptive heuristics, and result-set caching before the existing cache can prove what it is doing.

## Phase-Start Baseline

Known starting points:

- `Warning Cleanup Plan.md` recorded 318 outside-sandbox solution warnings after a clean Debug build, with 84 generated `CS0108` locations and 61 nullable `.Value` locations as the largest buckets.
- `Allocation Reduction Audit.md` recorded fresh 2026-05-10 `sqlite-memory` baseline allocations on commit `57ce5efd36875f346969d7dfb596ffe21e50e5a2`:
  - provider initialization: 899.41 KB
  - startup primary-key fetch: 145.86 KB
  - warm primary-key fetch: 15.75 KB
  - repeated non-PK equality fetch: 33.3 KB
  - repeated scalar `Any`: 25.73 KB
  - repeated `IN` predicate fetch: 47.91 KB
- `Representative Benchmark Suite and Website Trends.md` defines the benchmark-history and website direction:
  - show all comparable published runs together
  - surface profile and last-run date
  - keep published provider scope to `sqlite-memory`
  - retain history by age with thinning
  - split macro categories into `macro-readwrite` and `macro-bulk`
  - expose telemetry deltas through expandable rows
- Current cache code still has concrete issues:
  - `DatabaseCache` eagerly calls `MakeSnapshot()` during construction
  - `IndexCache` mutates `List<IKey>` values inside a `ConcurrentDictionary`
  - `RowCache.TotalBytes` sums queued row sizes on every read
  - invalidation behavior is not documented by enough targeted tests

## Goals

- clean compiler warnings honestly, with runtime guards where the compiler found real ambiguity
- improve benchmark history artifacts and website trend interpretation before claiming performance wins
- replace public metadata array APIs with stable read-only collection APIs
- add frozen metadata lookup maps for table and column resolution
- add non-allocating key value access and remove hot cache usage of key value snapshots
- reduce generated metadata startup allocation without bypassing validation
- reduce query and SQL temporary arrays where measurements say it still matters
- characterize cache invalidation with tests before changing behavior
- harden invalidation for update/delete, indexed/relation column changes, commit/rollback boundaries, and relation notifications
- clean low-risk cache internals and make their behavior observable

## Non-Goals

- row hashing or row-version freshness primitives
- external invalidation APIs
- adaptive cache heuristics, database profiling, or LFU policy
- dependency-tracked result-set caching
- broad provider expansion
- async provider pipeline work
- Remotion replacement, query-plan migration, or supported-subset parser work
- hiding warnings through blanket suppression

## Workstream A: Warning Cleanup

Status: Complete as of 2026-05-10.

The Debug solution build is clean for DataLinq-owned C# compiler warnings, with compiler warnings now treated as errors through `src/Directory.Build.props`. The only remaining build warnings are the two documented `WASM0001` events from `DataLinq.BlazorWasm` linking SQLitePCLRaw's `e_sqlite3` WebAssembly native asset. That warning is intentionally not suppressed; Phase 13 owns the call-path proof, provider/bundle investigation, or public support caveat.

Goals:

- make the compiler agree with the invariants the code already relies on
- add runtime guards where warnings expose a real nullable or lifecycle ambiguity
- remove warning noise before cache-semantics work starts

Tasks:

1. Fix generator and information-schema contracts:
   - align `ICOLUMNS` nullability with provider reality
   - emit `new` only for intentional generated interface hiding
   - add generator coverage for inherited manual interfaces
2. Fix metadata object graph initialization:
   - use `= null!` only for required back-references wired before public use
   - initialize collection properties such as mutation history changes
   - remove or normalize custom exception state that duplicates `Exception.Message`
3. Fix query state and nullable parameters:
   - make lazy state explicitly nullable or eagerly initialized
   - allow null SQL parameter values where runtime behavior already permits them
   - normalize nullable aliases and parameter prefixes
4. Fix provider nullability and runtime guards:
   - check provider metadata type availability at reader boundaries
   - guard nullable provider schema fields before constructing keys or attributes
   - replace primary-constructor captures that produce `CS9107`
5. Clean CLI, Blazor sample, and tool initialization warnings.
6. Fix compliance test nullable primary-key usage with explicit persisted-row preconditions.
7. Decide the WebAssembly SQLite warning disposition without blanket suppression.
8. Add warning enforcement only after cleanup.

Exit criteria:

- Debug solution build is warning-clean outside known environment/third-party exceptions
- any remaining warnings are documented with owner, reason, and future disposition
- generator, unit, compliance, and provider tests still pass for affected areas
- warning enforcement can be enabled without blocking legitimate build environments

## Workstream B: Benchmark History And Website Trends

Goals:

- make performance evidence useful over months and years
- avoid comparing incompatible evidence as if it were identical
- expose enough telemetry to diagnose regressions without making the page unreadable

Tasks:

1. Preserve profile, provider, commit, runner, category, and last-run information in the published data model.
2. Keep one website surface that can show all comparable published runs together.
3. Make automated warning/improved status prefer same-profile baselines.
4. Add rolling comparisons:
   - latest vs previous compatible run
   - latest vs 7-run median
   - latest vs 30-run median when available
   - recent slope
5. Replace raw run-count retention with age-based thinning:
   - keep all recent runs
   - thin older runs to weekly or monthly representative points
   - preserve immutable per-run files longer when storage permits
6. Keep published provider scope to `sqlite-memory`.
7. Add `macro-readwrite` and `macro-bulk` category policy.
8. Add expandable telemetry rows to the benchmark results page.
9. Add hoverable SVG chart inspection with date, commit, profile, value, allocation, and uncertainty.

Exit criteria:

- website shows profile and last-run date per scenario/provider
- mixed `default` and `heavy` runs are visually interpretable, not silently blended
- benchmark comparison does not use a different profile as the primary automated verdict
- historical retention is age-based or the implementation plan has an explicit migration path
- telemetry deltas are accessible through collapsed details without crowding the default table
- DocFX output is verified through an HTTP-served `_site`

## Workstream C: Metadata Collection And Lookup Allocation

Goals:

- remove repeated metadata array snapshot allocation
- keep public metadata immutable without defensive copies on every read
- replace repeated LINQ scans with frozen lookup maps

Tasks:

1. Replace public metadata array properties with stable read-only collection APIs.
2. Back read-only collections with frozen metadata-owned arrays or genuinely immutable collection shapes.
3. Add internal span helpers only where immediate iteration benefits from them.
4. Add `ColumnCount` and `GetColumn(int)` to `TableDefinition`.
5. Add database table lookup maps by model type and database name.
6. Add table column lookup maps by property name, database name, and ordinal.
7. Migrate runtime call sites away from copied metadata arrays:
   - `DatabaseCache`
   - `TableCache`
   - `RowData`
   - `SqlQuery<T>`
   - `Select`
   - read/transaction/query executor paths
8. Add tests proving public callers cannot mutate metadata collections.

Exit criteria:

- provider initialization does not use public `TableModels` array snapshots
- row construction does not use `Table.Columns.Length`
- hot metadata lookup does not require `Single(...)` over copied arrays
- public metadata collections are stable and read-only
- allocation benchmarks are rerun after the change

## Workstream D: Key And Cache Hot-Path Allocation

Goals:

- stop cache code from allocating arrays just to read values from existing keys
- make composite keys expose values without nested child snapshot allocation
- reduce warm fetch and relation traversal allocation where DataLinq owns the cost

Tasks:

1. Add `ValueCount`, `GetValue(int)`, and `TryGetSingleValue(...)` to `IKey`.
2. Change `IKey.Values` to a read-only collection surface rather than an array snapshot.
3. Update simple key implementations.
4. Update `CompositeKey` to store raw values directly if that stays clean.
5. Migrate cache, relation, and index code away from `Values` indexing.
6. Add tests that prove key value reads are non-allocating in representative simple-key paths.
7. Re-measure warm primary-key fetch and relation traversal.

Exit criteria:

- simple key value access does not allocate a one-element array
- composite key value access avoids nested child `Values` allocation
- cache code uses `GetValue` or `TryGetSingleValue` for hot access
- public key values remain read-only and do not expose mutable key internals

## Workstream E: Generated Metadata And Query Temporary Allocation

Goals:

- reduce cold provider initialization allocation after metadata collection fixes land
- remove query temporary arrays where the query layer still owns the cost
- preserve diagnostics and behavior

Tasks:

1. Profile typed draft conversion after Workstreams C and D.
2. Reduce `DatabaseColumnType` clone churn where safe.
3. Generate capacity-aware metadata builder code.
4. Keep validation and diagnostic location information intact.
5. Add array/span overloads for query paths where inputs are already materialized.
6. Avoid returning parameter-name arrays when SQL can be appended directly.
7. Audit `ReadPrimaryAndForeignKeys` for avoidable `Distinct`, `GroupBy`, and `ToArray`.
8. Extend SQL template caching only where current benchmarks prove repeated-shape cost remains high.

Exit criteria:

- provider initialization allocation drops materially from the Phase 9A baseline
- generated metadata validation failures remain descriptive
- repeated `IN` predicate allocation improves or has a documented non-DataLinq attribution
- query behavior remains identical under existing compliance tests

## Workstream F: Cache Invalidation Characterization

Goals:

- lock down expected cache invalidation behavior before changing implementation
- make transaction boundaries explicit
- expose current invalidation gaps as failing or pending tests instead of folklore

Tasks:

1. Add tests for row-cache invalidation after update and delete.
2. Add tests for relation/index invalidation when indexed columns change.
3. Add tests that unchanged indexed columns do not cause broader invalidation than necessary, where the current design can support it.
4. Add tests for transaction-local cache behavior before commit.
5. Add tests proving rollback does not publish global invalidation.
6. Add tests proving commit publishes global cache changes after database commit succeeds.
7. Add tests for relation notification subscribers:
   - subscriber clears on relevant table changes
   - dead subscribers are compacted
   - notification cleanup does not miss invalidation
8. Add telemetry assertions for invalidation operation names and counts where practical.

Exit criteria:

- current behavior is documented by tests before implementation changes
- known incorrect behavior has explicit failing tests or a documented deferral
- commit/rollback boundaries are unambiguous
- relation notification behavior has targeted coverage

## Workstream G: Cache Invalidation And Cache Internals Hardening

Goals:

- make invalidation precise where precision is cheap and correct
- keep conservative broad invalidation where precision would be risky
- remove known concurrency and allocation risks from cache internals

Tasks:

1. Make `DatabaseCache` initial history snapshot lazy unless a public behavior requires eagerness.
2. Replace `IndexCache` mutable list values with a thread-safe bucket, immutable arrays on update, or a lock-protected structure.
3. Make `GetForeignKeysByPrimaryKey` return a stable snapshot or protected enumeration.
4. Maintain `RowCache.TotalBytes` as a running counter during add/remove/clear.
5. Ensure update invalidation only removes relation/index entries affected by changed relation/index columns where possible.
6. Keep delete invalidation conservative and complete.
7. Ensure transaction-local changes do not leak global invalidation before commit.
8. Add table-level fallback for invalidation cases that cannot be represented precisely without dangerous complexity.
9. Add telemetry for precise invalidation, table-level invalidation, cleanup, and notification sweep outcomes.

Exit criteria:

- `IndexCache` reverse mappings are safe under concurrent cache updates
- `RowCache.TotalBytes` is O(1) for reads
- provider construction avoids eager cache-history allocation
- rollback does not clear global relation caches for uncommitted changes
- commit invalidation happens after successful database commit
- telemetry can distinguish precise, table-level, cleanup, and notification invalidation work

## Workstream H: Benchmark Closeout And Release Evidence

Goals:

- prove the phase with the same evidence style used to plan it
- avoid claiming wins from one noisy timing run
- record remaining gaps honestly

Tasks:

1. Rerun Phase 2 watch benchmarks for `sqlite-memory`.
2. Rerun Phase 3 query hot-path benchmarks for `sqlite-memory`.
3. Run or add macro read/write benchmarks covering invalidation-relevant workflows.
4. Capture before/after allocation history JSON artifacts.
5. Compare provider initialization, startup PK, warm PK, repeated non-PK, scalar `Any`, and repeated `IN`.
6. Record whether timing noise prevents latency claims.
7. Update planning docs with final measured outcomes and any deferred work.

Exit criteria:

- benchmark artifacts exist for before/after allocation comparison
- release notes can state allocation and invalidation changes without guessing
- any benchmark regression has an owner and explanation
- Phase 9B starts from documented cache telemetry and invalidation behavior

## Suggested Implementation Order

1. Run baseline warning/build/benchmark commands.
2. Complete Workstream A warning cleanup.
3. Implement Workstream B benchmark-history and website changes.
4. Implement Workstream C metadata collection and lookup allocation changes.
5. Implement Workstream D key/cache hot-path allocation changes.
6. Add Workstream F invalidation characterization tests.
7. Implement Workstream G cache invalidation and internals hardening.
8. Implement Workstream E generated metadata and query temporary-array reductions.
9. Complete Workstream H benchmark closeout.

Workstreams C, D, and F can overlap if write sets are kept separate. Workstream G should not begin until F has enough coverage to catch semantic mistakes.

## Verification

Warning/build verification:

```powershell
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.sln -c Debug -v minimal --no-incremental
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Dev.CLI -- build src\DataLinq.sln --profile ci --output raw -- --no-incremental
```

Test verification:

```powershell
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite unit --alias quick --output failures --build
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite generators --alias quick --output failures
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias quick --output failures --build
```

Benchmark verification:

```powershell
$env:DATALINQ_BENCHMARK_PROVIDERS = 'sqlite-memory'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile default --history-json artifacts\benchmarks\history\phase9a-phase2-watch.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile default --history-json artifacts\benchmarks\history\phase9a-phase3-query-hotpath.json
```

Website verification:

```powershell
docfx docfx.json
```

Serve the generated site over HTTP before judging browser behavior. Do not inspect DocFX output through `file://`.

## Release Acceptance Criteria

Phase 9A can ship when:

- warning cleanup has a clean or explicitly documented baseline
- benchmark website changes are implemented or consciously deferred with a smaller release note
- allocation work has before/after measurement
- cache invalidation tests cover the committed behavior
- cache internals cleanup has no known concurrency regression
- release notes clearly separate shipped Phase 9A behavior from deferred Phase 9B cache semantics
