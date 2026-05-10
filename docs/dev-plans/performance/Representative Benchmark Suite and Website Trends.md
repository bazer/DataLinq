> [!WARNING]
> This document is planning material. It describes benchmark and website changes that should be implemented deliberately, then promoted only after the data proves they are stable enough to trust.
# Representative Benchmark Suite and Website Trends

**Status:** Proposed
**Goal:** Add representative library-usage benchmarks, reduce avoidable benchmark noise, and make the website show performance direction over time instead of only the latest run.

## Current State

The benchmark system is already more advanced than a simple BenchmarkDotNet project:

- `src/DataLinq.Benchmark` defines the actual BenchmarkDotNet scenarios.
- `src/DataLinq.Benchmark.CLI` is the right entry point for benchmark runs, summary artifacts, history JSON, baseline comparison, and compact terminal output.
- `.github/workflows/benchmark-history.yml` publishes `latest.json`, `latest-comparison.json`, per-run JSON, and a rolling `history.json` on the `benchmark-data` branch.
- `docs/Benchmark Results.md` renders the published history through `docfx/datalinq/public/benchmark-results.js`.
- Current benchmark rows already carry useful behavior deltas: query counts, transaction counts, mutation counts, row-cache activity, materializations, and relation-cache activity.

That foundation is good. The weak parts are now narrower and easier to name:

- the published suite is still too hot-path oriented to describe ordinary library use
- the website table mostly answers "what changed since the previous latest run?", not "what direction is this going over the last N runs?"
- the trend charts are static SVGs with no hover inspection
- the published history currently mixes `default` and `heavy` benchmark profiles without making profile, cadence, and last-run dates visible enough
- the current `NoisePercent` is based on `Error / Mean`; that is useful, but it is not the same thing as a full variance story

The blunt version: we have enough plumbing to avoid benchmark theater, but only if we stop promoting broad scenarios before they earn it.

## Objectives

1. Add representative macro benchmarks that resemble ordinary DataLinq usage.
2. Keep enough published history to show multi-year direction without letting the main page become heavy.
3. Make benchmark profile, provider, cadence, and last-run date visible so mixed histories are interpretable.
4. Improve noise reporting with better statistics and policy, not wishful thresholds.
5. Upgrade the website table and charts so trends, dates, commits, values, and confidence are visible.

## Representative Benchmark Scenarios

### Keep the Existing Hot-Path Suite

The existing scenarios should stay because they answer specific architectural questions:

- provider initialization
- startup primary-key fetch
- cold and warm primary-key fetch
- cold and warm relation traversal
- repeated non-PK equality fetch
- repeated `IN` predicate fetch
- repeated scalar `Any`

These are not "normal app workflows", but they are still valuable. They tell us whether metadata startup, query translation, row-cache behavior, and relation traversal are moving in the right direction.

### Promote or Refine the Existing CRUD Work

There is already a `CrudWorkflow` scenario, but it is currently categorized as `experimental` and `macro`. It performs a real unit-of-work loop:

1. start a transaction
2. read an employee
3. traverse a department relation
4. update the employee
5. insert a new employee
6. reload the inserted employee
7. delete the inserted employee
8. commit

That is exactly the kind of scenario we want, but it should not be blindly moved into the published stable lane. First it needs a promotion pass:

- run it locally with `default` and `heavy`
- inspect BenchmarkDotNet warnings
- inspect `NoisePercent`
- inspect telemetry deltas per operation
- confirm cleanup restores deterministic state
- confirm it does not dominate CI runtime

If it stays below the noise bar, promote it into the relevant published macro category and include it in the website. If it remains noisy, keep it visible locally and do not publish it as a regression signal.

### Add a Small CRUD Workflow

Add `CrudWorkflowSmall` or `CrudWorkflowUnitOfWork` under a `macro-readwrite` category.

Purpose:

- represent a normal request-sized write workflow
- keep per-operation semantics understandable
- make the result useful for "did normal DataLinq usage get worse?"

Shape:

- one BenchmarkDotNet invocation executes enough workflows to avoid tiny-iteration warnings
- `OperationsPerInvoke` is the number of logical CRUD workflows, not the number of SQL statements
- each workflow should use a transaction and include read, relation traversal, update, insert, reload, delete, commit
- cleanup must be deterministic and outside the measured section where possible

Initial recommendation:

- start with 25 to 100 workflows per invocation
- keep the published metric normalized per workflow
- only publish after several runs prove it is stable

Do not make this a single CRUD operation benchmark unless BenchmarkDotNet shows it is long enough. A "single request" label is fine, but a measurement that is too short is garbage with a polite table around it.

### Add a Larger CRUD Batch

Add `CrudWorkflowBatch` or refine the existing one into a `macro-bulk` category after evidence.

Purpose:

- track whole-library direction when many pieces are exercised together
- catch regressions that microbenchmarks miss
- represent back-office or service-job workloads where thousands of operations happen in one process

Shape:

- use deterministic employee samples
- execute a few thousand CRUD-relevant actions per invocation
- report normalized cost per logical workflow
- include telemetry deltas so we can tell whether a regression came from more queries, more materialization, more transactions, or more allocation

Important nuance:

"A few thousand CRUD operations" should not necessarily mean a few thousand transactions. If one workflow performs five meaningful database/library actions, 400 workflows already gives roughly 2,000 actions. CI runtime and noise should decide the exact count.

### Add Delete as a First-Class Mutation Scenario

The suite has insert and update coverage. Delete is currently present inside cleanup and the CRUD workflow, but not as its own stable mutation scenario.

Add `DeleteEmployeesBatch`.

Purpose:

- track deletion cost directly
- complete the mutation trio: insert, update, delete
- make mutation telemetry easier to validate

Shape:

- setup inserts deterministic rows before the measured iteration
- benchmark deletes those rows in one transaction
- cleanup handles partial failure safely
- telemetry should show delete count and affected rows per operation

### Defer Allround Model Macro Benchmarks

`src/DataLinq.Tests.Models/Allround` looks tempting because it is closer to a real application domain. Do not start there.

Use the employees model first because it already has deterministic testing infrastructure and benchmark context. After the CRUD lane is stable, add one Allround read/write scenario if it gives genuinely different coverage:

- user plus profile/contact read
- order plus details/products traversal
- inventory or payment update workflow

This should be a second wave. Pulling it in too early expands setup complexity before the current macro lane has proved itself.

## Noise Reduction Plan

### Make Profiles Visible Before Doing Anything Else

The current benchmark-history workflow uses:

- `default` profile on push
- `heavy` profile on scheduled runs

Both are appended to the same `history.json`, and the website does not make profile differences prominent enough. The history row contains `Metadata.Profile`, so the data exists, but the presentation and baseline selection do not respect it.

This should be fixed before new macro results are treated as meaningful.

Recommended fix:

- keep one published history surface that can show all runs together
- preserve `Metadata.Profile` on every run and expose it in tables, chart hover details, point styling, and filters
- compare candidates against the latest compatible artifact for the same profile when producing automated warning/improved status
- compute long-term visual trends across all comparable runs, but mark each point as `default` or `heavy`
- show when each scenario was last run, because some scenarios may only update on scheduled heavy runs
- make profile filtering available, but do not hide push/default runs by default

`default` and `heavy` are directionally comparable because they run the same benchmark methods, providers, and normalized operation counts. They are not statistically identical evidence: `default` maps to BenchmarkDotNet `ShortRun`, while `heavy` maps to `MediumRun`. That means the website can plot them in one long-term timeline, but regression verdicts should prefer same-profile comparisons and rolling medians. If `default` and `heavy` diverge persistently for a row, that is a signal to inspect the benchmark rather than to average the disagreement away.

### Improve the Statistical Artifact

The current history rows include mean, error, allocated bytes, and `NoisePercent`. That is useful but thin.

Add or preserve these machine-readable fields:

- median
- standard deviation
- standard deviation percent
- min and max
- sample count or iteration count when available
- BenchmarkDotNet job/profile name
- BenchmarkDotNet warnings captured for the row or run

The website does not need to show all of these by default. It does need them to decide whether a trend should be trusted.

Also rename or supplement `NoisePercent`. `Error / Mean` is a confidence-width proxy, not the whole noise picture. Keep it for continuity, but add `StdDevPercent` and display a clearer label such as `Uncertainty`.

### Gate Stable Promotion With Evidence

A benchmark should move into published history only after it passes a boring evidence gate.

Suggested promotion rule:

- run the scenario at least 5 times locally with the `heavy` profile
- collect at least 7 scheduled CI runs before treating it as a trend signal
- require low or explainable BenchmarkDotNet warnings
- require uncertainty below 10% for normal rows and below 20% for rows explicitly marked noisy
- require stable telemetry shape between runs

This is conservative, but that is the point. A noisy benchmark is worse than no benchmark because it teaches contributors to ignore performance data.

### Use Rolling Baselines, Not Only Previous Run Comparison

The current comparison is against the previous `latest.json`. That is useful for smoke detection, but it is too jumpy for hosted CI.

Add derived trend comparisons:

- latest vs previous run
- latest vs 7-run median
- latest vs 30-run median when enough data exists
- rolling slope over recent runs
- best and worst in the visible window

Regression status should prefer rolling medians over single-run deltas. Single-run deltas are a canary, not a verdict.

### Retain By Age, Not Run Count

The current workflow keeps the last 120 runs. That is the wrong unit. A scheduled-only row and a push-updated row do not have the same meaning when retention is counted by raw run count.

Use age-based retention with thinning:

- keep all published runs for the most recent 6 months
- after 6 months, keep at most one representative run per week per scenario/provider/profile
- after 2 years, either keep one representative run per month in the main `history.json` or keep weekly points if the JSON remains comfortably small
- keep immutable per-run files longer than the compact history index if storage stays reasonable

My opinion: this should bias toward keeping years of data. Performance trends are most valuable when they catch slow drift, and slow drift is invisible if the website only remembers a few months. The only caveat is page weight. The fix is not deleting old evidence; it is decimating the main history file while preserving enough older points to show long-term direction.

### Keep Provider Scope Deliberate

Continue using `sqlite-memory` for architecture-sensitive hot-path trends. It is the cleanest signal for library overhead.

Keep the published benchmark-history suite on `sqlite-memory` for now, including macro CRUD. `sqlite-file` is more realistic for persistence cost, but it also brings filesystem variance that will make hosted trend interpretation worse. Keep it local-only until `sqlite-memory` macro coverage has proved useful and boring.

Do not add Podman-backed MySQL/MariaDB to the published benchmark-history lane yet. Those runs are valuable for provider behavior checks, but hosted CI network/container variance will swamp small library changes.

## Website Presentation Plan

### Table: Show Direction, Not Just Latest Snapshot

Replace the current latest-snapshot table with a trend table.

Recommended columns:

- scenario
- provider
- profile
- last run
- latest mean
- latest allocated
- uncertainty
- vs previous run
- vs 7-run median
- vs 30-run median
- recent slope
- latest commit
- status

Rows should be grouped by scenario category:

- startup
- read hot paths
- relation traversal
- mutation
- macro read/write
- macro bulk

The old "latest snapshot" table can remain available, but it should not be the main interpretation surface.

### Charts: Add Hover Inspection

The current charts are hand-rendered SVG polylines. That is fine. We do not need Chart.js just to get hover values.

Add an SVG interaction layer:

- transparent pointer-capture rectangle over the plot area
- nearest-point lookup on `pointermove`
- vertical crosshair
- highlighted point marker
- tooltip with:
  - date/time
  - commit short SHA
  - profile
  - scenario
  - provider
  - mean
  - allocated bytes
  - uncertainty/noise
  - telemetry summary if compact enough
- keyboard focus support for points or chart summary where practical

This keeps the site static, lightweight, and dependency-free.

### Charts: Plot More Than One Metric Intelligently

Each scenario should expose at least:

- mean time
- allocated bytes
- uncertainty/noise

The default view should show mean time. Allocations deserve a first-class toggle because DataLinq performance wins often show up as lower allocation pressure before they show up as lower mean time on CI.

Avoid showing every telemetry counter as a chart by default. Put telemetry in hover details and expandable table rows. Too much chart surface will make the page look more precise while becoming less readable.

### Table: Add Expandable Telemetry Rows

Expose telemetry deltas as expandable detail rows under each benchmark row. Keep the collapsed row focused on the numbers people scan most: time, allocation, uncertainty, trend, profile, and last run.

The expanded row should show compact per-operation telemetry groups:

- queries: entity and scalar
- transactions: starts, commits, rollbacks
- mutations: inserts, updates, deletes, affected rows
- row cache: hits, misses, stores
- relation cache: hits and loads
- database/materialization counts

This is better than tooltip-only telemetry because it makes regression diagnosis possible on touch devices and in screenshots. The guardrail is that telemetry should be hidden by default and grouped, not sprayed into new columns.

### Visual Design

The benchmark page should feel like an engineering dashboard, not a marketing page.

Use:

- compact grouped tables
- restrained colors
- status chips for `stable`, `improved`, `warning`, `noisy`, and `missing`
- inline sparklines in the table
- larger charts only for selected rows or grouped trend cards
- clear labels for profile and provider scope

Avoid:

- giant decorative cards
- over-saturated gradients
- trend lines without uncertainty context
- pretending a noisy row is actionable because the line is visually dramatic

## Data Model Changes

Keep schema compatibility where possible, but plan a schema version bump if the artifact changes materially.

Recommended `history` additions:

```json
{
  "SchemaVersion": 2,
  "Runs": [
    {
      "RunId": "...",
      "GeneratedAtUtc": "...",
      "Metadata": {
        "Profile": "heavy",
        "Commit": "...",
        "RunnerOs": "Linux"
      },
      "Rows": [
        {
          "Method": "CRUD workflow",
          "ProviderName": "sqlite-memory",
          "Category": "macro-readwrite",
          "MeanMicroseconds": 123.4,
          "MedianMicroseconds": 121.9,
          "ErrorMicroseconds": 4.2,
          "StdDevMicroseconds": 7.1,
          "UncertaintyPercent": 3.4,
          "StdDevPercent": 5.8,
          "AllocatedBytes": 4567,
          "OperationsPerInvoke": 100,
          "TelemetryDelta": {}
        }
      ]
    }
  ]
}
```

The exact names can change during implementation. The important rule is that the website should not have to reverse-engineer statistical meaning from formatted strings.

## Implementation Order

### 1. Fix profile-aware history interpretation

- keep the website capable of showing all published runs together
- make the website and comparison logic profile-aware
- ensure baseline comparison uses the same profile
- update the benchmark-results page to show profile and last-run date per scenario

This should happen before adding broad macro charts.

### 2. Improve benchmark artifact statistics

- preserve median/stddev/min/max/sample metadata in summary and history JSON
- add row-level warning/noise metadata
- keep old fields for compatibility while the website migrates

### 3. Add or promote representative scenarios locally

- add `CrudWorkflowSmall`
- promote or refine existing `CrudWorkflow`
- add `DeleteEmployeesBatch`
- consider increasing macro action count only after measuring runtime

At this point, keep new scenarios out of the published stable lane unless evidence says otherwise.

### 4. Add macro category policy

- introduce `macro-readwrite` for request-sized read/write workflows that pass the noise gate
- introduce `macro-bulk` for larger batch workflows that pass the noise gate
- keep `experimental` for broader local probes
- document promotion criteria in `docs/contributing/DataLinq.Benchmark.CLI.md`

### 5. Add age-based history thinning

- retain all runs for the recent window
- thin older history by age, not raw run count
- preserve representative weekly or monthly points for multi-year trends
- keep per-run archive files longer than the compact website history if storage permits

### 6. Upgrade website data calculations

- compute previous-run delta
- compute 7-run median delta
- compute 30-run median delta
- compute recent slope
- compute status from rolling trend plus uncertainty
- compute last-run date per scenario/provider/profile

### 7. Upgrade website presentation

- replace the table with the trend table
- add inline sparklines
- add hoverable SVG charts
- add metric toggles for mean time and allocated bytes
- expose tooltip details with date, value, commit, profile, and noise
- expose telemetry deltas in expandable rows

### 8. Promote scenarios after evidence

- let the new scenarios accumulate local and scheduled history
- promote only the stable ones to the published website surface
- explicitly mark noisy scenarios instead of hiding their flaws

## Verification

For benchmark changes:

- `dotnet run --project src/DataLinq.Benchmark.CLI -- list`
- `dotnet run --project src/DataLinq.Benchmark.CLI -- run --profile smoke --filter "*Crud*"`
- `dotnet run --project src/DataLinq.Benchmark.CLI -- run --profile heavy --filter "*Crud*"`
- inspect generated summary/history JSON
- confirm telemetry deltas match expected query/mutation/transaction counts

For website changes:

- build DocFX
- verify generated `_site/docs/Benchmark Results.html`
- serve through HTTP, not `file://`
- test hover tooltips on desktop and narrow mobile widths
- test expandable telemetry rows on desktop and narrow mobile widths
- test missing `latest-comparison.json`
- test histories with fewer than 2, 7, and 30 runs
- test mixed-profile history input because the website should show all comparable published runs together

## Non-Goals

- no EF Core or Dapper comparison in this slice
- no MySQL/MariaDB published history lane yet
- no benchmark hard-fail gate on ordinary PRs
- no large charting dependency unless the custom SVG approach proves painful
- no claim that hosted CI timings are absolute truth

## Resolved Questions And Remaining Gaps

1. The website should show all comparable published runs together by default, not hide push/default runs behind a heavy-only view. Each row and point must show profile and last-run date. Automated comparison status should still prefer same-profile baselines.
2. Keep the published suite on `sqlite-memory` for now. `sqlite-file` remains local-only until there is a strong reason to accept filesystem variance on the public trend page.
3. Retain history by age, not raw run count. Keep all recent runs, then thin older data to representative weekly or monthly points so multi-year drift remains visible without bloating the page.
4. Split macro CRUD into `macro-readwrite` and `macro-bulk`.
5. Try expandable telemetry rows. Tooltips can keep compact telemetry summaries, but expandable rows are the better diagnostic surface if they stay collapsed by default.

## Suggested First Slice

The best first slice is not adding the benchmark methods. It is making history interpretation profile-aware, adding last-run visibility, and avoiding raw run-count retention.

After that:

1. add `DeleteEmployeesBatch`
2. split the current CRUD workflow into small and batch variants
3. run them locally under `heavy`
4. promote only the rows that stay boring
5. upgrade the website table, expandable telemetry rows, and hover charts against the improved history shape

That sequence is less glamorous than dumping new scenarios into CI, but it is the path that keeps the benchmark page useful instead of decorative.
