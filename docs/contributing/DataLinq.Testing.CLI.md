# DataLinq.Testing.CLI

`DataLinq.Testing.CLI` is the canonical entry point for test infrastructure orchestration and provider-matrix test runs.

Use it when the run depends on target aliases, batched server targets, runtime state, or container lifecycle control.

For first-time machine setup, required tools, and Podman/WSL bootstrap steps, see [Dev and Test Environment](Dev%20and%20Test%20Environment.md).

## Why It Exists

This tool replaced the older PowerShell-driven workflow on purpose.

Maintaining both shell scripts and a .NET CLI for the same test infrastructure is pointless duplication. One source of truth is the only sane model here.

## Command Surface

The command examples assume your current directory is the repo's `src` folder.

### `list`

Lists:

- feedback run plans and their warm budgets
- suites
- provider target-set aliases
- targets
- current runtime state

```bash
dotnet run --project DataLinq.Testing.CLI -- list
dotnet run --project DataLinq.Testing.CLI -- list --plan smoke
```

### `up`

Starts the selected server targets and waits for readiness.

```bash
dotnet run --project DataLinq.Testing.CLI -- up --alias latest
dotnet run --project DataLinq.Testing.CLI -- up --targets 'mysql-9.7,mariadb-12.3'
```

Useful option:

- `--recreate`
  Removes existing containers before starting the selected targets.

### `wait`

Waits for the selected targets to become ready and refreshes runtime state from the containers that are actually running.

```bash
dotnet run --project DataLinq.Testing.CLI -- wait --alias latest
```

### `down`

Stops or removes the selected targets.

```bash
dotnet run --project DataLinq.Testing.CLI -- down
dotnet run --project DataLinq.Testing.CLI -- down --remove
```

### `reset`

Recreates the selected targets from scratch.

```bash
dotnet run --project DataLinq.Testing.CLI -- reset --targets mysql-9.7
```

### `run`

Runs a named feedback plan or an explicitly selected suite. Provider-backed suites use the selected targets; targetless suites run once.

```bash
dotnet run --project DataLinq.Testing.CLI -- run --plan smoke
dotnet run --project DataLinq.Testing.CLI -- run --plan quick
dotnet run --project DataLinq.Testing.CLI -- run --plan latest --batch-size 4
dotnet run --project DataLinq.Testing.CLI -- run --plan full --batch-size 1
dotnet run --project DataLinq.Testing.CLI -- run --plan focused --suite unit --filter "/*/*/CacheNotificationManagerTests/*"
dotnet run --project DataLinq.Testing.CLI -- run --suite compliance --targets 'mysql-9.7,mariadb-12.3'
dotnet run --project DataLinq.Testing.CLI -- run --suite memory --output failures --summary-json artifacts/test-results/memory.json
```

## Run Plans

Run plans answer **what tests should run now?** Provider aliases and `--targets` independently answer **which database implementations should provider-backed tests use?** Do not conflate those axes.

| Plan | Intent | Default prerequisites | Warm budget |
| --- | --- | --- | ---: |
| `focused` | One explicit suite and TUnit tree filter for the code under change. | Selected-suite dependent. | 30 s |
| `smoke` | Curated query, mutation, mapping, cache, generator, Memory, and SQLite representatives. | Warm build; no Podman. | 30 s |
| `quick` | All generator, unit, Memory, and provider-invariant compliance tests against `sqlite-file`. | Warm build; no Podman. | 60 s |
| `latest` | Complete logical suite coverage against SQLite and the latest target in each server family. | Podman and latest server targets. | 300 s |
| `full` | Every required suite and supported provider target. | Podman and the full server matrix. | 600 s |

`list --plan <name>` shows the exact suites, targets, purpose/resource classifications, expected case counts, estimates, and the most recent recorded measurement before execution. A plan run writes `artifacts/test-results/last-<plan>.json` automatically unless `--summary-json` chooses another artifact path. The listing separates accumulated test-host wall time—the meaningful warm comparison—from cold build and total duration.

Smoke is an explicit test-method allow-list. Adding a test—even beside an existing smoke test—does not silently make it smoke coverage; a maintainer must deliberately add its exact TUnit path to the catalog and state its purpose/resource classification. Expensive lifecycle, process, filesystem, package, SQLite, and server-backed coverage remains in quick/latest/full even when it is not appropriate for smoke. No test is deleted to make a budget green.

## Target Selection

Target selection for provider-backed suites is controlled by either `--alias` or `--targets`. Aliases are provider target sets, not run plans; they do not select suites or the DataLinq.Memory backend.

Supported aliases:

- `quick`
  `sqlite-file`, `sqlite-memory`
- `latest`
  `sqlite-file`, `sqlite-memory`, `mysql-9.7`, `mariadb-12.3`
- `all`
  every supported target

If you do not specify a target selection for `up`, `wait`, `reset`, or a legacy suite-level `run`, the default alias is `latest`. Named plans declare their own defaults. An explicit `--alias` or `--targets` overrides that provider set independently; smoke and quick reject Podman targets because no-server execution is part of their contract.

The `generators`, `unit`, and `memory` suites are targetless. They run once even when an alias contains several SQL targets. In summary JSON the legacy `Targets` field remains `-` for backward compatibility, while `TargetIds` is the authoritative structured field and is empty for targetless runs.

## Suites

Supported suites:

- `generators`
- `unit`
- `memory`
- `compliance`
- `mysql`
- `all`

`all` is the default and means:

- run `generators` once
- run `unit` once
- run `memory` once
- run `compliance` against target batches
- run `mysql` against the selected server-backed target batches

`memory` maps to `src/DataLinq.Tests.Memory/DataLinq.Tests.Memory.csproj`; it is a suite, not a target alias. Do not call it `sqlite-memory`: that existing target means the compliance project running against an in-memory SQLite connection. The Memory test project intentionally references `DataLinq.SQLite` for bounded differential-parity fixtures, so a green CLI `memory` run is project-based test evidence, not provider-free constrained-runtime or package-consumer evidence.

## Important `run` Options

- `--plan`
  Chooses `focused`, `smoke`, `quick`, `latest`, or `full`. Non-focused plans own their suite/filter selection. Focused requires both `--suite` and `--filter`.
- `--suite`
  Defaults to `all`.
- `--project`
  Optional project override for a single-suite run.
- `--filter`
  Optional TUnit tree-node filter expression. The CLI forwards this to the test host as `--treenode-filter`.
- `--configuration`
  Defaults to `Debug`.
- `--build`
  Explicitly builds each distinct test project once before running it. This is the default unless `--no-build` is used; the option remains useful when scripts want to state the contract visibly.
- `--no-build`
  Resolves and executes existing test host DLLs directly. Missing, ambiguous, or source-stale outputs fail with an actionable error.
- `--batch-size`
  Defaults to `2`. Must be between `1` and `32`.
- `--maximum-parallel-tests`
  Sets `TUNIT_MAX_PARALLEL_TESTS` for each child test host. Values must be between `1` and `256`. An explicit value overrides the per-suite limit recorded by a named plan.
- `--provider-affinity-role anchor|target-specific`
  Creates one auditable provider evidence shard. It requires a single compliance or MySQL-suite target, `--batch-size 1`, and no named plan or caller-supplied filter. `anchor` includes provider-invariant tests; `target-specific` applies the appropriate affinity filter internally. CI uses this option; ordinary local runs should normally use a named plan.
- `--parallel`
  Runs the selected suites in parallel instead of serially.
- `--tear-down`
  Stops provisioned server targets after the run completes.
- `--summary-json`
  Writes a machine-readable run summary using schema `v0.9.testing-run-summary.v2`.
- `--output quiet|summary|failures|raw`
  Controls run output shape.
- `--profile repo|sandbox|ci`
  Controls the repo-local execution profile used when invoking `dotnet`.

`--project` cannot be combined with `--suite all` or a named plan. Non-focused plans cannot be combined with `--suite`/`--filter`; use focused for an ad hoc selection. `--interactive` cannot be combined with `--summary-json` or `--plan`.

### Build-once execution model

The runner resolves the complete suite plan before starting test hosts. By default it builds every distinct test project exactly once, resolves the resulting executable TUnit/Microsoft.Testing.Platform DLL, and invokes each suite/target row with `dotnet exec`. Provider rows therefore do not re-enter MSBuild and do not rebuild or reevaluate the same project.

Use `--no-build` after an explicit solution/project build, including in CI. The resolver requires exactly one executable target framework/runtime, the DLL, its `.runtimeconfig.json`, and its `.deps.json`. It also walks project references and rejects an output older than relevant project sources or build props. `--build` and `--no-build` are mutually exclusive.

The summary's `BuildProject` value records whether this invocation performed the once-per-project build. Each result's command arguments record the exact resolved host DLL used by `dotnet exec`.

### Resource-aware scheduling

Named plans carry an explicit worker limit per suite: generators, Memory, compliance, and MySQL/MariaDB use eight workers; unit uses sixteen. These are resource budgets, not CPU-count guesses. Override them only for a recorded concurrency sweep with `--maximum-parallel-tests`; the invocation and each TRX performance result record the requested limit, observed effective concurrency, and test-host duration.

The August 2026 MariaDB 11.8 compliance sweep used the same warm build, 489 cases, and 250-connection container for every row:

| Workers | Host | Effective concurrency | p95 | Connections opened | Threads before/after host exit | Admin-lock wait |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 4 | 31.5 s | 2.08 | 0.51 s | 2,643 | 248 / 1 | 682 ms |
| 8 | 29.8 s | 3.32 | 0.98 s | 2,629 | 230 / 1 | 1,627 ms |
| 12 | 29.8 s | 5.41 | 1.68 s | 2,640 | 249 / 1 | 1,081 ms |
| 16 | 30.2 s | 6.98 | 2.02 s | 2,629 | 245 / 1 | 1,214 ms |

Eight is the plan default because it had the best host time without the 12-worker run's one-thread connection headroom. Higher test-body concurrency merely increased contention and p95; it did not shorten the run. Every sweep row had zero max-connection retries and returned to the single post-process telemetry connection.

Five subsequent eight-worker MariaDB 11.8 runs passed all 489 cases in 29.8, 27.9, 29.6, 31.0, and 29.8 seconds (29.8-second median). All five recorded zero connection retries and one connected telemetry probe after the test host exited.

The `latest` and `full` compliance and MySQL/MariaDB manifests assign each batch an auditable `ProviderAffinityRole`:

- `AnchorWithInvariant` is the first batch. It runs the provider-invariant tests once, any catalog/SQLite-special cases once, and the provider cases for its selected targets.
- `TargetSpecific` batches use TUnit properties to select `EveryProvider` cases and, for server batches, `ServerFamily` cases. They do not rediscover invariant or SQLite/catalog cases.

Compliance methods using `ActiveProviders`, `ServerProviders`, `SqliteProviders`, or `AllLtsServerProviders`, and MySQL-suite methods using a server provider source, must declare `ProviderAffinity` beside their data source. Tests without a provider data source are invariant by convention. This makes the full-plan logical pairing explicit: an invariant method appears once, while each required provider-backed method appears once for every applicable target. `--batch-size 1` still produces one explicit result row per target; the anchor role explains why the first row contains the one-time cases.

The source-controlled eight-target full-plan floors currently total 5,436 tests: 3,103 compliance cases and 445 MySQL-suite cases alongside 61 generator, 1,686 unit, and 141 Memory cases. Its 17 rows are explicit: compliance has a floor of 498 on the SQLite-file anchor, 367 on SQLite memory, and 373 on each of the six servers; the MySQL suite floors are 127 on the MySQL 9.7 anchor, 62 on MySQL 8.4, and 64 on each MariaDB target. Plan listings use these values as approximate workload estimates. The authoritative nightly gate also loads the previous successful per-shard counts, so new tests pass and ratchet automatically while a later per-shard decrease fails closed.

Unkeyed `[NotInParallel]` stops the entire test process and is restricted to a source-enforced allowlist. It remains justified only where unconstrained tests necessarily observe or modify the same process-global resource:

- telemetry/metrics tests reset global counters or install listeners that receive ordinary database activity from every concurrent test;
- CLI/configuration tests mutate process environment variables or the current directory, and console tests redirect the process streams;
- provider-registry tests replace global plugin/provider registrations read by otherwise unconstrained tests;
- Memory characterization tests inspect static converter/materialization histories that ordinary Memory tests also update;
- compliance translation, relation-cache, GUID, and capability characterizations assert process-global converter or telemetry call counts that ordinary compliance tests also update;
- the Employees lease isolation proof requires sole ownership to prove deterministic LIFO reuse, while its metrics/cache characterization peers reset process-global counters.

Database-local and fixture-local exclusions use these stable key families instead:

| Key family | Protected resource |
| --- | --- |
| `process:database-cache` | Static database/cache notification state where every mutating peer participates in the same key |

Tests sharing a key serialize with each other but continue alongside tests that do not touch that resource. A source-policy test rejects any new process-global file outside the reviewed allowlist and verifies that provider data sources carry the matching affinity property.

The `aggregate` command validates downloaded nightly shard artifacts against the canonical 17-row full-matrix manifest. It requires an exact commit SHA and configuration, accepts an optional previous successful `--baseline`, and writes schema `v0.9.testing-shard-aggregate.v2`. Missing, duplicate, below-floor, count-regressed, wrong-role, failed, dirty, schema-incompatible, runtime-incompatible, or artifact-incomplete shards are hard failures. Count growth is valid and is emitted as a compact `CaseCountBaseline` for the next run. An intentional reduction requires a reviewed source-floor change plus a baseline-epoch increment; a newer epoch ignores older ratchet history exactly once and then publishes the new floor. See [CI Test Lanes](CI%20Test%20Lanes.md) for the blocking policy and workflow shape.

### Compliance fixture profiles and reuse

Employees compliance fixtures must choose the smallest explicit `EmployeesFixtureProfile` that proves the behavior:

- `SchemaOnly` creates the schema without stock rows. Use it for schema, custom-seed, and empty-database cases.
- `TinySeeded` creates 32 deterministic employees. It is the default choice for isolated mutation, transaction, cache, and relationship behavior.
- `FullSeeded` creates the 300-row corpus. Reserve it for tests whose expected result or query distribution depends on that corpus.

There is intentionally no implicit profile. A new test that does not state its data requirement is underspecified.

Server-backed isolated fixtures rent one of four databases per target and profile. A returned lease is reset before reuse: the harness fingerprints the schema, rebuilds it when a test changed database objects, otherwise deletes the tiny fixture rows in one foreign-key-controlled batch, restores the employee auto-increment sequence, and then reapplies only the selected seed profile. Lease failures identify both the owning test scenario and logical database; a failed reset poisons and replaces that lease instead of returning suspect state to another test. SQLite fixtures retain per-test lifetime because their cheap local setup does not benefit from the server lease pool.

Shared server fixtures use connector pooling with connection reset enabled and a maximum pool size of eight, matching normal test-host concurrency without exhausting the server across multiple logical pools. Isolated and administrative connections remain unpooled. Do not enable pooling for isolated fixtures: it obscures ownership, delays cleanup, and was a major source of unnecessary server connections.

Each compliance result directory can include `fixture-metrics.json`. Its versioned report records per-target/profile create, reuse, reset, failure, wait, seed, and cleanup measurements; serialized administrative-command and lock counts; global server connection counters; and a final server-status sample taken by the CLI after the test host has exited. `ServerThreadsConnectedAfterTestHostExit` should therefore return to the telemetry probe itself rather than retain test-host sockets. The CLI treats telemetry as diagnostic evidence: inability to sample it is reported without turning otherwise valid tests into failures.

### Summary JSON evidence contract

The versioned summary records a collision-free run id, the named plan when present, the resolved invocation, runtime/OS identity, safe non-secret environment inputs, structured selected targets and resolved suites (including plan filters), expected-versus-observed suite/batch rows, build and test command arguments with UTC timestamps, totals and outcomes, report and raw-log artifact paths, and start/end checkout plus Testing CLI/DevTools runner attestations. Each result row includes accumulated infrastructure setup and test-host time plus TRX-derived test-body totals, nearest-rank p50/p95/p99/max durations, effective concurrency, configured TUnit parallelism when present, and the 20 slowest tests and classes. The aggregate `Timings` object reports accumulated build-process, infrastructure, test-host, test-body, and teardown seconds; these are deliberately labelled as accumulated work because parallel suite execution can overlap them.

Each server-backed command row records the normalized effective database host resolved from the child environment or current runtime state; missing capture, disagreement with an explicit override, or inconsistent effective hosts makes the invocation incomplete. The report writer and stale-file invalidation accept destinations only beneath `<repo>/artifacts`. `ArtifactsComplete` requires every result's raw log, HTML report, and TRX report to exist as regular files beneath that root; malformed or count-mismatched TRX performance data also makes an otherwise passing row incomplete. Reparse-point escapes fail closed. Failure details are bounded and credential-redacted. Once parsing has invoked the run action, semantic run-action validation invalidates an older file at the requested path before new output is written, so an interrupted or rejected rerun cannot leave a stale green report behind. `System.CommandLine` syntax and parser failures occur before that action and therefore neither invalidate the old file nor synthesize JSON; evidence consumers must require a successful command exit together with the expected schema and validity gates, never mere file existence.

`Outcome` and `IsCompleteForInvocation` describe the selected invocation. A focused or filtered run can therefore pass and be complete for what it was asked to execute while still having `ValidForEvidence` set to `false`. `ValidForEvidence` is deliberately stricter: it requires a passed, complete, artifact-complete, unfiltered `all`-suite/`all`-target run over the exact five-suite (`generators`, `unit`, `memory`, `compliance`, `mysql`) and eight-target (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mysql-9.7`, `mariadb-10.11`, `mariadb-11.4`, `mariadb-11.8`, `mariadb-12.3`) release catalog. The reporter reconstructs the expected suite/batch rows from that resolved invocation and requires an exact expected-versus-observed match, with one target per provider-backed result row; it does not trust the aggregate coverage flags alone. Valid evidence also requires a clean checkout whose commit and status remain stable and matching Testing CLI and DevTools assemblies built from that clean commit. Missing counts, expected rows, build records, or referenced logs make the requested summary incomplete or invalid rather than silently producing release evidence.

Provider totals are aggregate within a target batch. Use `--batch-size 1` for the authoritative release matrix so each provider-backed result row has exactly one `TargetIds` entry and `HasPerTargetProviderTotals` is true. Warnings and skipped tests still require the separate dispositions defined by the release plan; `ValidForEvidence` does not waive that review.

The active suites run on TUnit and Microsoft.Testing.Platform, so this is not the old VSTest `FullyQualifiedName~Foo` filter grammar. Use the TUnit tree-node shape:

```text
/<Assembly>/<Namespace>/<Class name>/<Test name>
```

Useful examples:

```bash
dotnet run --project DataLinq.Testing.CLI -- run --plan focused --suite unit --filter "/*/*/CacheNotificationManagerTests/*"
dotnet run --project DataLinq.Testing.CLI -- run --plan focused --suite unit --filter "/*/*/*/HandleEvent_NoSubscribers_DoesNotThrow"
dotnet run --project DataLinq.Testing.CLI -- run --plan focused --suite compliance --alias quick --filter "/*/DataLinq.Tests.Compliance.Query/*/*"
```

Wildcards are supported. For the underlying syntax, see the [TUnit test filter documentation](https://tunit.dev/docs/execution/test-filters/).

## Interactive Mode

If you run the CLI with no arguments, it starts the interactive workflow.

You can also request interactive prompts for a command explicitly:

```bash
dotnet run --project DataLinq.Testing.CLI -- wait --interactive
```

## Runtime State and Logs

The CLI writes runtime state to this repo-root path:

```text
artifacts/testdata/testinfra-state.json
```

Every non-interactive `run` gets a unique artifact tree:

```text
artifacts/test-results/<run-id>/<suite>/<target-row>/
  raw.log
  report.html
  report.trx
  fixture-metrics.json  # compliance rows that rent server fixtures
```

Explicit build logs for that invocation are written under `artifacts/test-results/<run-id>/build/`. The summary's `RunId`, result paths, and aggregate `ArtifactPaths` connect each suite/target row to these files. GitHub Actions uploads this tree with `if: always()` so failed rows retain the reports the test host managed to produce.

That runtime state is how the test harness discovers:

- the resolved host
- the running server target ids, plus local SQLite targets
- published ports
- configured test credentials

Server-backed `up`, `wait`, and `run` commands refresh this file from the containers that are actually running. A targeted `run --targets mysql-9.7` selects MySQL for that run, but it should not permanently narrow runtime state if other Podman targets are still running.

If you bypass the CLI and expect the suites to “just know” the active provider matrix, you are making the repo harder than it needs to be.

## Environment and Matrix Inputs

The active target matrix still lives in:

```text
test-infra/podman/matrix.json
```

The public summary of aliases, server targets, and profiles is the [Test Provider Matrix](../support-matrices/Test%20Provider%20Matrix.md).

Important environment-variable overrides include:

- `DATALINQ_TEST_CONTAINER_PREFIX`
- `DATALINQ_TEST_DB_HOST`
- `DATALINQ_TEST_DB_ADMIN_USER`
- `DATALINQ_TEST_DB_ADMIN_PASSWORD`
- `DATALINQ_TEST_DB_APP_USER`
- `DATALINQ_TEST_DB_APP_PASSWORD`
- `DATALINQ_TEST_EMPLOYEES_DB`
- `DATALINQ_TEST_DB_MAX_CONNECTIONS`
- `DATALINQ_TEST_PODMAN_PATH`
- `DATALINQ_TEST_PROVIDER_SET`
- `DATALINQ_TEST_TARGETS`
- `DATALINQ_TEST_TARGET_ALIAS`

Use overrides deliberately. The defaults are there so normal local runs stay simple.
