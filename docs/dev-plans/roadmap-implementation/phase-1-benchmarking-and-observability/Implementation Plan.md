> [!WARNING]
> This document is implementation planning material for Roadmap Phase 1. It is not normative product documentation, and it should not be treated as a description of shipped behavior unless a section explicitly says so.
# Phase 1 Implementation Plan: Benchmarking and Observability

**Status:** Implementation substantially complete; evidence-gathering and final review still active

## Progress Snapshot

The implementation side of Phase 1 is now mostly done.

What is completed:

- the benchmark harness is cross-platform, repeatable, and publishes history
- deterministic benchmark datasets and scenario setup are in place
- standard .NET telemetry exists through `Meter("DataLinq")` and `ActivitySource("DataLinq")`
- command, transaction, mutation, cache occupancy, cleanup, relation, row-cache, and notification telemetry are implemented
- benchmark telemetry deltas, CI history, baseline comparison, and website reporting exist
- diagnostics and telemetry integration documentation exist

What still needs observation rather than more implementation:

- let the heavier nightly benchmark lane accumulate evidence
- confirm which scenarios truly deserve to remain in the published stable subset
- decide whether any noisy scenarios should be reworked, demoted, or simply documented as noisy

## Purpose

This document turns the high-level goals in [Roadmap.md](../../Roadmap.md) into an execution plan for the next stretch of work.

The bigger-picture roadmap is correct: Phase 1 should come first.

The blunt reason is simple:

DataLinq is still too easy to misread.

The current benchmark harness is not yet credible enough for regression watching, and the current telemetry is strong on cache internals but weak on command execution, transaction behavior, and end-to-end runtime visibility. That combination is exactly how teams waste time optimizing the wrong thing.

## Current Baseline

The current repository state already gives us something useful to build on:

- `src/DataLinq.Benchmark` exists and runs real BenchmarkDotNet benchmarks.
- `DataLinq.Diagnostics.DataLinqMetrics` already exposes a hierarchical in-process snapshot model for runtime, provider, and table metrics.
- Current metrics cover query execution counts, row-cache activity, relation cache activity, and cache-notification behavior.
- SQL command logging already exists through `ILogger`.

But the current state also has obvious weaknesses:

- the benchmark scenarios are too short and noisy to trust for serious regression decisions
- the benchmark harness does not yet explain enough about why one scenario is slower than another
- the telemetry surface is mostly a custom snapshot API, not a standard .NET instrumentation surface
- the runtime lacks first-class command-duration, transaction-lifecycle, mutation, and cleanup-cost telemetry

## Phase Objective

By the end of this phase, DataLinq should be able to answer two questions honestly:

1. What is the cost of the main hot paths under controlled benchmark conditions?
2. What is the library doing inside a running application when performance or cache behavior looks wrong?

If we cannot answer both, the phase is not done.

## Benchmarking Stance

The older [Performance Benchmarking.md](../../performance/Performance%20Benchmarking.md) document is directionally useful, and several of its ideas should explicitly shape this phase.

The important ones are:

- deterministic benchmark data matters more than benchmark variety
- allocated bytes are not a side metric; they are one of the core outputs
- startup and cold-path cost need to be measured separately from warm steady-state behavior
- historical benchmark results and CI comparison matter, or "regression testing" is just a slogan

The parts that should be treated more cautiously are the more ambitious arena-comparison ideas. Cross-ORM comparisons can be valuable, but they should not block Phase 1. First we need a trustworthy DataLinq harness. Then we can decide whether EF Core and Dapper comparisons are worth the maintenance cost.

## Design Stance

This phase should not turn into a telemetry science project.

The right approach is:

- keep the current snapshot API because it is useful for in-process diagnostics and benchmark delta analysis
- add standard .NET telemetry producers so DataLinq integrates cleanly with modern tooling
- avoid coupling the core library to a specific monitoring backend

That means:

- `System.Diagnostics.Metrics.Meter` for exported metrics
- `System.Diagnostics.ActivitySource` for traces/spans
- `ILogger` for logs

OpenTelemetry should be treated as the normal collection/export path for applications, not as a required dependency of the `DataLinq` core package.

## Primary Benchmark Outputs

The benchmark harness should optimize for a small number of outputs that are actually decision-useful.

Primary outputs:

- mean execution time
- allocated bytes per operation
- GC pressure where it is meaningful
- cold-start or first-query cost where startup behavior is under test
- telemetry deltas that explain why a scenario behaved the way it did

This matters because raw timing alone is too easy to overinterpret. A "faster" benchmark that quietly increased allocations or command count may still be a worse design.

## What This Phase Will Deliver

### 1. Trustworthy benchmark harness foundations

We will turn `src/DataLinq.Benchmark` from a useful experiment into a harness that can survive repeated use.

Deliverables:

- ~~benchmark scenarios with enough work per iteration to avoid meaningless sub-millisecond noise~~
- ~~deterministic setup and reset rules for cold and warm paths~~
- ~~a clear split between warm-path, cold-path, and startup-oriented scenarios~~
- ~~benchmark output that includes telemetry deltas, not only wall-clock time and allocations~~
- ~~a baseline/report storage strategy for historical comparison~~
- ~~a documented benchmark workflow for repeatable local runs~~

### 2. First-class runtime observability

We will expose the main runtime behaviors through standard .NET diagnostics primitives and keep the current snapshot model for richer drilldown.

Deliverables:

- ~~a DataLinq `Meter`~~
- ~~a DataLinq `ActivitySource`~~
- ~~a documented instrumentation model for apps that want OpenTelemetry collection~~
- ~~low-cardinality exported metrics for the main hot paths~~
- ~~trace spans around meaningful units of work~~

### 3. Better interpretation of cache and query behavior

We will close the current visibility gaps that make benchmark and production behavior ambiguous.

Deliverables:

- ~~command execution metrics and spans~~
- ~~transaction lifecycle metrics and spans~~
- ~~mutation metrics~~
- ~~cleanup and cache-maintenance telemetry~~
- ~~cache occupancy gauges where they are meaningful and affordable~~

## Workstreams

## Workstream A: Benchmark Harness Credibility

### Goals

- eliminate obviously misleading benchmark configuration
- make cold and warm scenarios explicit and repeatable
- capture enough supporting signals to explain results
- establish a small benchmark set that is worth running regularly

### Tasks

1. Review the current benchmark scenarios and raise the work per iteration until BenchmarkDotNet stops warning that the measurements are too short.
2. ~~Separate benchmark setup concerns cleanly:~~
   - dataset provisioning
   - cache reset
   - warmup priming
   - telemetry reset
3. ~~Add benchmark result columns or exported summaries derived from telemetry deltas:~~
   - DB commands executed
   - rows read
   - row-cache hits and misses
   - materializations
   - relation loads and cache hits
4. ~~Split the benchmark suite into categories:~~
   - startup/cold-start scenarios
   - hot-path micro-benchmarks
   - macro user-path scenarios
5. ~~Expand scenarios only after the existing employees benchmarks are trustworthy.~~

### Verification

- no more "minimum observed iteration time is very small" warnings for the primary scenarios
- reduced bimodality or a clearly documented reason when bimodality remains
- benchmark reports include both timing/allocation data and meaningful behavior counters

## Workstream B: Deterministic Data and Scenario Design

### Goals

- keep benchmark inputs reproducible
- avoid pretending that network noise is ORM architecture data

### Tasks

1. ~~Keep the main Phase 1 architectural benchmarks on deterministic SQLite-backed or in-memory datasets.~~
2. ~~Reuse `DataLinq.Testing` seed/provisioning logic where possible instead of inventing a parallel data pipeline.~~
3. ~~Define a small scenario set that covers the behaviors we actually care about first:~~
   - warm primary-key fetch
   - cold primary-key fetch
   - warm relation traversal
   - cold relation traversal
   - startup or first-query cost
4. ~~Treat remote-database/provider-network comparisons as separate and lower-priority because they mostly test environment variance, not hot-path runtime design.~~

## Workstream C: Standard .NET Metrics

### Goals

- expose useful operational metrics through `Meter`
- keep metric labels bounded so the signals are exportable in real systems

### Tasks

1. ~~Define a DataLinq meter name and versioning policy.~~
2. ~~Add low-cardinality instruments for:~~
   - query executions by kind
   - DB command executions by kind and outcome
   - DB command duration
   - transaction starts and outcomes
   - transaction duration
   - mutation counts by operation type
   - cache hits and misses
   - relation cache hits and loads
   - cleanup operations and cleanup duration
3. ~~Add current-value instruments where appropriate:~~
   - cache row count
   - cache byte estimate
   - notification queue depth
   - transaction cache entry count
4. ~~Keep tags conservative:~~
   - provider type
   - database type
   - table name when stable and bounded
   - operation kind
   - outcome

### Explicit non-goal

Do not export high-cardinality metric tags such as per-provider-instance GUIDs or raw SQL text. That is how you poison metric backends.

## Workstream D: Distributed Tracing Surface

### Goals

- make expensive operations visible in a trace viewer
- correlate DataLinq work with application requests and logs

### Tasks

1. ~~Define a DataLinq `ActivitySource`.~~
2. ~~Add spans around meaningful operations:~~
   - end-to-end query execution
   - DB command execution
   - transaction commit and rollback
   - optionally relation load when it represents real lazy I/O
3. ~~Attach useful tags:~~
   - provider type
   - database type
   - table name when known
   - command kind
   - transaction type
   - cache path when relevant
4. ~~Use `ILogger` for detailed SQL text and debug-heavy payloads instead of stuffing them into spans.~~

### Explicit non-goal

Do not create a span for every tiny internal helper. Verbose traces are not observability; they are visual noise.

## Workstream E: Snapshot API and Benchmark Delta Analysis

### Goals

- keep `DataLinqMetrics.Snapshot()` useful
- make it complement standard .NET telemetry rather than compete with it

### Tasks

1. ~~Preserve the current hierarchical snapshot API.~~
2. ~~Add missing counters or gauges that matter for benchmark interpretation and runtime diagnosis.~~
3. ~~Add helper logic in benchmarks to capture before/after snapshots and compute deltas.~~
4. ~~Document which values are:~~
   - counters
   - gauges
   - maxima
   - summed child values

### Why this matters

The snapshot API is still the best place for rich, in-process drilldown. Exported metrics are for dashboards. Snapshot models are for understanding exactly which provider and table were hot.

## Workstream F: Baselines, Artifacts, and Regression Watching

### Goals

- make benchmark history visible
- turn benchmark output into something that can catch regressions instead of just generating one-off reports

### Tasks

1. ~~Define a stable benchmark history schema before wiring CI or website output.~~
   The schema should carry:
   - run metadata such as commit, branch, timestamp, runner, OS, and benchmark profile
   - per-scenario timing, allocation, noise, and telemetry-delta values
   - comparison-friendly identifiers for scenario and provider
2. ~~Define where benchmark artifacts, accepted baselines, and published history live.~~
   The storage model should separate:
   - ephemeral CI artifacts
   - accepted baseline files used for comparison
   - published history data consumed by the documentation site
2. ~~Standardize which outputs are kept:~~
   - human-readable markdown summary
   - machine-readable artifact suitable for comparison
   - machine-readable history snapshot suitable for trend graphs
4. ~~Add a repeatable local runner script or documented command surface for producing those artifacts and comparing them to a baseline.~~
5. ~~Add a CI lane for a small, stable subset of benchmarks once the scenarios are trustworthy.~~
6. ~~Persist CI benchmark history in a way that survives individual workflow runs and can be consumed by the website without scraping workflow artifacts.~~
7. ~~Start with warning/reporting thresholds before treating regressions as hard failures.~~

### Why this matters

If nobody can compare today's run to last month's accepted baseline, the benchmark project is still halfway to theater.

## Workstream G: Documentation and Example Integration

### Goals

- make the instrumentation usable without reverse-engineering the source
- show one sane way to wire DataLinq telemetry into a modern .NET app

### Tasks

1. ~~Update diagnostics documentation after the telemetry surface is real.~~
2. ~~Add an example of application-side integration with standard .NET telemetry collection.~~
3. ~~Document at least two practical workflows:~~
   - local ad hoc inspection with `dotnet-counters`
   - application integration with OpenTelemetry
4. ~~Add a benchmark results page to the website that presents the current stable benchmark subset and trend graphs from published history data.~~
5. ~~Keep roadmap and implementation docs separate from shipped docs.~~

## Proposed Execution Order

The order matters.

### ~~Step 1: Stabilize benchmark methodology~~

Before expanding telemetry breadth, fix the benchmark harness so it measures enough work per iteration and has clean cold/warm semantics.

### ~~Step 2: Lock deterministic data and the initial scenario set~~

Keep the first benchmark slice narrow and reproducible before adding breadth.

### ~~Step 3: Instrument the command layer~~

Add metrics and spans around `ExecuteReader`, `ExecuteScalar`, and `ExecuteNonQuery` in the provider access layers.

This is the most important observability gap today.

### ~~Step 4: Instrument transaction and mutation lifecycle~~

Add telemetry around transaction start, commit, rollback, duration, and mutation counts.

### ~~Step 5: Add cache occupancy and cleanup signals~~

Extend telemetry to describe what the cache is doing now, not just what it did cumulatively.

### ~~Step 6: Integrate telemetry into benchmarks~~

Capture scenario deltas and expose them in benchmark output.

### ~~Step 7: Add baseline storage and a small regression lane~~

Do not wait until the very end to think about artifact shape and CI viability.

The practical order inside this step should be:

1. lock the machine-readable history schema
2. add baseline comparison in the benchmark CLI
3. add a small CI benchmark lane that publishes long-lived history data
4. add website reporting on top of that published history

### ~~Step 8: Document and harden~~

Write the docs only after the surface is coherent enough to describe honestly.

## Detailed Telemetry Inventory

## Shipped by the End of Implementation

- provider-level entity query execution counts
- provider-level scalar query execution counts
- provider-level DB command counts, failures, and duration
- provider-level transaction starts, outcomes, and duration
- provider/table mutation counts, affected rows, and duration
- table-level row-cache hits, misses, stores, database rows loaded, and materializations
- table-level relation reference and collection cache hits and loads
- table-level cache occupancy gauges for rows, transaction rows, bytes, and index entries
- table-level cache cleanup and maintenance metrics
- table-level cache-notification subscription, sweep, and queue-depth metrics
- end-to-end query activities and query duration metrics
- startup and first-query benchmark scenarios

## Still Weak in Practice

- confidence in the noisiest published stable scenarios still depends on collecting more nightly evidence
- the final stable benchmark subset may still need one more pruning pass after additional history accumulates

## Exit Criteria

Phase 1 is done when all of the following are true:

- the main benchmark scenarios are reproducible enough to compare across changes
- the benchmark suite reports the core outputs we care about: time, allocations, and behavior deltas
- benchmark runs expose both performance and behavior deltas
- baseline artifacts exist for comparison
- a small stable benchmark subset is ready for local repetition and CI use
- DataLinq emits standard .NET metrics and traces for the main hot paths
- a running application can observe command, transaction, cache, and relation behavior without bespoke instrumentation hacks
- the docs explain how to collect the new telemetry with standard .NET tooling

## Non-Goals

This phase is not allowed to quietly expand into later roadmap work.

Not part of this phase:

- major SQL generation rewrites
- metadata architecture redesign
- generator hardening beyond what is needed for telemetry support
- async API design
- new providers
- speculative cache redesign
- vendor-specific telemetry coupling in the core package
- mandatory competitor benchmarking against EF Core and Dapper before the DataLinq harness is stable

## Risks

### Risk: Metric-cardinality explosion

If we export unstable labels such as provider instance ids or SQL text, the metric design will be bad on day one.

Mitigation:

- keep exported tags bounded
- push high-cardinality detail into logs, traces, and snapshot drilldown

### Risk: Benchmark theater

If benchmark scenarios remain too short or too synthetic, the harness will produce pretty numbers with low decision value.

Mitigation:

- fix benchmark granularity before adding many new scenarios
- require behavior deltas alongside timing

### Risk: Trace spam

If we create spans for every internal helper, trace output becomes useless.

Mitigation:

- instrument meaningful units of work only
- keep helper-level detail in logs

## Review Trigger

This plan should be updated when any of the following happens:

- benchmark evidence points to a different highest-priority hotspot
- a telemetry design decision conflicts with actual exporter behavior or tool usability
- the scope of Phase 1 becomes obviously too large to land coherently
- enough of the phase is implemented that this document should split into progress tracking and follow-up docs
