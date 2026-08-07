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

- suites
- aliases
- targets
- current runtime state

```bash
dotnet run --project DataLinq.Testing.CLI -- list
```

### `up`

Starts the selected server targets and waits for readiness.

```bash
dotnet run --project DataLinq.Testing.CLI -- up --alias latest
dotnet run --project DataLinq.Testing.CLI -- up --targets mysql-8.4,mariadb-11.8
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
dotnet run --project DataLinq.Testing.CLI -- reset --targets mysql-8.4
```

### `run`

Runs the selected suite or suites. Provider-backed suites use the selected targets; targetless suites run once.

```bash
dotnet run --project DataLinq.Testing.CLI -- run --suite all --alias quick
dotnet run --project DataLinq.Testing.CLI -- run --suite all --alias latest --batch-size 4
dotnet run --project DataLinq.Testing.CLI -- run --suite compliance --targets mysql-8.4,mariadb-11.8
dotnet run --project DataLinq.Testing.CLI -- run --suite memory --output failures --summary-json artifacts/test-results/memory.json
dotnet run --project DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/CacheNotificationManagerTests/*"
```

## Target Selection

Target selection for provider-backed suites is controlled by either `--alias` or `--targets`. Aliases select SQL test targets; they do not select the DataLinq.Memory backend.

Supported aliases:

- `quick`
  `sqlite-file`, `sqlite-memory`
- `latest`
  `sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`
- `all`
  every supported target

If you do not specify a target selection for `up`, `wait`, `reset`, or `run`, the default alias is `latest`.

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

- `--suite`
  Defaults to `all`.
- `--project`
  Optional project override for a single-suite run.
- `--filter`
  Optional TUnit tree-node filter expression. The CLI forwards this to the test host as `--treenode-filter`.
- `--configuration`
  Defaults to `Debug`.
- `--build`
  Builds the test project before running it.
- `--batch-size`
  Defaults to `2`. Must be between `1` and `32`.
- `--parallel`
  Runs the selected suites in parallel instead of serially.
- `--tear-down`
  Stops provisioned server targets after the run completes.
- `--summary-json`
  Writes a machine-readable run summary using schema `v0.9.testing-run-summary.v1`.
- `--output quiet|summary|failures|raw`
  Controls run output shape.
- `--profile repo|sandbox|ci`
  Controls the repo-local execution profile used when invoking `dotnet`.

`--project` cannot be combined with `--suite all`. That combination is nonsense, and the CLI rejects it. `--interactive` cannot be combined with `--summary-json`.

### Summary JSON evidence contract

The versioned summary records the resolved invocation, safe non-secret environment inputs, structured selected targets and resolved suites, expected-versus-observed suite/batch rows, build and test command arguments with UTC timestamps, totals and outcomes, report and raw-log artifact paths, and start/end checkout plus Testing CLI/DevTools runner attestations. Each server-backed command row records the normalized effective database host resolved from the child environment or current runtime state; missing capture, disagreement with an explicit override, or inconsistent effective hosts makes the invocation incomplete. The report writer and stale-file invalidation accept destinations only beneath `<repo>/artifacts`. `ArtifactsComplete` likewise accepts referenced build/test raw logs only when they exist there as regular files; reparse-point escapes fail closed. Failure details are bounded and credential-redacted. Once parsing has invoked the run action, semantic run-action validation invalidates an older file at the requested path before new output is written, so an interrupted or rejected rerun cannot leave a stale green report behind. `System.CommandLine` syntax and parser failures occur before that action and therefore neither invalidate the old file nor synthesize JSON; evidence consumers must require a successful command exit together with the expected schema and validity gates, never mere file existence.

`Outcome` and `IsCompleteForInvocation` describe the selected invocation. A focused or filtered run can therefore pass and be complete for what it was asked to execute while still having `ValidForEvidence` set to `false`. `ValidForEvidence` is deliberately stricter: it requires a passed, complete, artifact-complete, unfiltered `all`-suite/`all`-target run over the exact five-suite (`generators`, `unit`, `memory`, `compliance`, `mysql`) and six-target (`sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-10.11`, `mariadb-11.4`, `mariadb-11.8`) release catalog. The reporter reconstructs the expected suite/batch rows from that resolved invocation and requires an exact expected-versus-observed match, with one target per provider-backed result row; it does not trust the aggregate coverage flags alone. Valid evidence also requires a clean checkout whose commit and status remain stable and matching Testing CLI and DevTools assemblies built from that clean commit. Missing counts, expected rows, build records, or referenced logs make the requested summary incomplete or invalid rather than silently producing release evidence.

Provider totals are aggregate within a target batch. Use `--batch-size 1` for the authoritative release matrix so each provider-backed result row has exactly one `TargetIds` entry and `HasPerTargetProviderTotals` is true. Warnings and skipped tests still require the separate dispositions defined by the release plan; `ValidForEvidence` does not waive that review.

The active suites run on TUnit and Microsoft.Testing.Platform, so this is not the old VSTest `FullyQualifiedName~Foo` filter grammar. Use the TUnit tree-node shape:

```text
/<Assembly>/<Namespace>/<Class name>/<Test name>
```

Useful examples:

```bash
dotnet run --project DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/CacheNotificationManagerTests/*"
dotnet run --project DataLinq.Testing.CLI -- run --suite unit --filter "/*/*/*/HandleEvent_NoSubscribers_DoesNotThrow"
dotnet run --project DataLinq.Testing.CLI -- run --suite compliance --alias quick --filter "/*/DataLinq.Tests.Compliance.Query/*/*"
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

Raw CLI logs are written under:

```text
artifacts/testdata/cli-logs/
```

That runtime state is how the test harness discovers:

- the resolved host
- the running server target ids, plus local SQLite targets
- published ports
- configured test credentials

Server-backed `up`, `wait`, and `run` commands refresh this file from the containers that are actually running. A targeted `run --targets mysql-8.4` selects MySQL for that run, but it should not permanently narrow runtime state if other Podman targets are still running.

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
