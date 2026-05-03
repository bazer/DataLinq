# DataLinq.Testing.CLI

`DataLinq.Testing.CLI` is the canonical entry point for test infrastructure orchestration and provider-matrix test runs.

Use it when the run depends on target aliases, batched server targets, runtime state, or container lifecycle control.

For first-time machine setup, required tools, and Podman/WSL bootstrap steps, see [Dev and Test Environment](Dev%20and%20Test%20Environment.md).

## Why It Exists

This tool replaced the older PowerShell-driven workflow on purpose.

Maintaining both shell scripts and a .NET CLI for the same test infrastructure is pointless duplication. One source of truth is the only sane model here.

## Command Surface

### `list`

Lists:

- suites
- aliases
- targets
- current runtime state

```bash
dotnet run --project src/DataLinq.Testing.CLI -- list
```

### `up`

Starts the selected server targets and waits for readiness.

```bash
dotnet run --project src/DataLinq.Testing.CLI -- up --alias latest
dotnet run --project src/DataLinq.Testing.CLI -- up --targets mysql-8.4,mariadb-11.8
```

Useful option:

- `--recreate`
  Removes existing containers before starting the selected targets.

### `wait`

Waits for the selected targets to become ready and refreshes runtime state from the containers that are actually running.

```bash
dotnet run --project src/DataLinq.Testing.CLI -- wait --alias latest
```

### `down`

Stops or removes the selected targets.

```bash
dotnet run --project src/DataLinq.Testing.CLI -- down
dotnet run --project src/DataLinq.Testing.CLI -- down --remove
```

### `reset`

Recreates the selected targets from scratch.

```bash
dotnet run --project src/DataLinq.Testing.CLI -- reset --targets mysql-8.4
```

### `run`

Runs the selected suite or suites against the selected targets.

```bash
dotnet run --project src/DataLinq.Testing.CLI -- run --suite all --alias quick
dotnet run --project src/DataLinq.Testing.CLI -- run --suite all --alias latest --batch-size 4
dotnet run --project src/DataLinq.Testing.CLI -- run --suite compliance --targets mysql-8.4,mariadb-11.8
```

## Target Selection

Target selection is controlled by either `--alias` or `--targets`.

Supported aliases:

- `quick`
  `sqlite-file`, `sqlite-memory`
- `latest`
  `sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`
- `all`
  every supported target

If you do not specify a target selection for `up`, `wait`, `reset`, or `run`, the default alias is `latest`.

## Suites

Supported suites:

- `generators`
- `unit`
- `compliance`
- `mysql`
- `all`

`all` is the default and means:

- run `generators` once
- run `unit` once
- run `compliance` against target batches
- run `mysql` against the selected server-backed target batches

## Important `run` Options

- `--suite`
  Defaults to `all`.
- `--project`
  Optional project override for a single-suite run.
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
  Writes a machine-readable run summary JSON file.
- `--output quiet|summary|failures|raw`
  Controls run output shape.
- `--profile repo|sandbox|ci`
  Controls the repo-local execution profile used when invoking `dotnet`.

`--project` cannot be combined with `--suite all`. That combination is nonsense, and the CLI rejects it.

## Interactive Mode

If you run the CLI with no arguments, it starts the interactive workflow.

You can also request interactive prompts for a command explicitly:

```bash
dotnet run --project src/DataLinq.Testing.CLI -- wait --interactive
```

## Runtime State and Logs

The CLI writes runtime state to:

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
