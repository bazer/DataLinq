# Test Infrastructure

`DataLinq.Testing.CLI` is now the only supported entry point for local test infrastructure orchestration.

The old PowerShell scripts are gone on purpose. Keeping both a .NET CLI and a shell-script layer would be two sources of truth for the same workflow, which is sloppy and eventually breaks.

The server target matrix still lives in `test-infra/podman/matrix.json`.

## Core Commands

List aliases, targets, and current runtime state:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- list
```

Launch the interactive workflow:

```powershell
dotnet run --project src/DataLinq.Testing.CLI
```

Launch a specific command interactively:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- wait --interactive
```

Start the latest server-backed lane:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- up --alias latest
```

Run the quick lane:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- run --alias quick
```

Run the unit lane only:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- run --suite unit
```

Run the latest lane:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- run --alias latest
```

Run both suites with the default behavior:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- run
```

Run every selected suite in parallel:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- run --parallel
```

Run the full supported matrix in batches of two targets:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- run --alias all --batch-size 2
```

Start every supported server target at once:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- up --alias all
```

Stop every running server target:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- down
```

Remove every test container:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- down --remove
```

If you are invoking the CLI repeatedly from the same build output, prefer `--no-build` on `dotnet run`. In practice, use it together with `-c Debug --framework net10.0` so `dotnet run` picks the expected output deterministically. The CLI itself does not need to rebuild every time; that extra build check is just default `dotnet run` behavior.

## Aliases

The supported aliases are:

* `quick`
  `sqlite-file`, `sqlite-memory`
* `latest`
  `sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8`
* `all`
  every supported target

These aliases are the same model used by the CLI, the test harness, and the Visual Studio runsettings files.

## Suites

The CLI supports three suite modes:

* `unit`
  Runs `src/DataLinq.Tests.Unit/DataLinq.Tests.Unit.csproj` once.
* `compliance`
  Runs `src/DataLinq.Tests.Compliance/DataLinq.Tests.Compliance.csproj` against the selected target batches.
* `mysql`
  Runs `src/DataLinq.Tests.MySql/DataLinq.Tests.MySql.csproj` against the selected server target batches.
* `all`
  Runs the unit suite once, then the compliance and MySQL/MariaDB suites against the selected target batches.

`run` now defaults to `--suite all`. That is the right default now that the new unit lane is real.

By default, suites run serially. Use `--parallel` if you explicitly want to overlap them. That can be faster, but it also increases contention against shared MySQL and MariaDB targets, so it should be an opt-in knob instead of a clever default.

## Visual Studio Runsettings

Use `Test -> Configure Run Settings -> Select Solution Wide runsettings File`.

Available files under `src`:

* `src/tests.quick.runsettings`
* `src/tests.latest.runsettings`
* `src/tests.all.runsettings`

Those are intentionally the only runsettings files left. Anything more granular belongs on the CLI command line, not in a pile of near-duplicate IDE config files.

## Runtime State

The CLI persists the active target set to:

* `artifacts/testdata/testinfra-state.json`

`DataLinq.Testing` reads that file automatically so the compliance suite can find:

* the resolved host
* the active target ids
* the per-target published ports
* the configured test credentials

That matters especially on Windows, where Podman machine host resolution is not reliably the same as blindly assuming `127.0.0.1`.

## Current Defaults

* Container prefix: `datalinq-tests`
* Host: resolved Podman machine IP on Windows, otherwise `127.0.0.1`
* MySQL 8.4 host port: `3307`
* MariaDB 11.8 host port: `3308`
* MariaDB 11.4 host port: `3309`
* MariaDB 10.11 host port: `3310`
* Admin user/password: `datalinq` / `datalinq`
* Application user/password: `datalinq` / `datalinq`
* Initial database: `datalinq_employees`
* Server `max_connections`: `250`

## Environment Variables

Supported overrides:

* `DATALINQ_TEST_CONTAINER_PREFIX`
  Naming prefix for the test containers. The infrastructure no longer uses Podman pods for MySQL and MariaDB because that was a bad design and caused `3306` conflicts inside a shared network namespace.
* `DATALINQ_TEST_DB_HOST`
* `DATALINQ_TEST_DB_ADMIN_USER`
* `DATALINQ_TEST_DB_ADMIN_PASSWORD`
* `DATALINQ_TEST_DB_APP_USER`
* `DATALINQ_TEST_DB_APP_PASSWORD`
* `DATALINQ_TEST_EMPLOYEES_DB`
* `DATALINQ_TEST_DB_MAX_CONNECTIONS`
  Overrides the server-side `max_connections` setting applied when MySQL and MariaDB test containers are created.
* `DATALINQ_TEST_PODMAN_PATH`
  Explicit path to the Podman executable if auto-discovery is not good enough on the current machine.
* `DATALINQ_TEST_PROVIDER_SET`
  Supported values are `quick`, `latest`, `all`, `targets`, and `alias`.
* `DATALINQ_TEST_TARGETS`
  Comma-separated target ids used when `DATALINQ_TEST_PROVIDER_SET=targets`.
* `DATALINQ_TEST_TARGET_ALIAS`
  Explicit alias used when `DATALINQ_TEST_PROVIDER_SET=alias`.

The old `DATALINQ_TEST_PODMAN_POD` name is still accepted as a compatibility fallback, but it is obsolete and should not be used in new setup.

## Supported Server Targets

Current LTS matrix:

* MySQL: `mysql:8.4`
* MariaDB: `mariadb:10.11`
* MariaDB: `mariadb:11.4`
* MariaDB: `mariadb:11.8`

The images are pinned by supported series, not floating tags. Anything else would make “compatibility” claims sloppy and non-reproducible.

## Isolation Model

The compliance suite now uses two lanes on purpose:

* shared seeded databases for read-only compliance tests
* per-test isolated databases for mutating and temporary-schema tests

That is the right tradeoff. Full per-test provisioning for every single read-only query test was technically clean and operationally stupid because it turned the suite into a multi-minute grind.

## Readiness Checks

The CLI uses:

* `mysqladmin ping` for MySQL
* `healthcheck.sh --connect --innodb_initialized` for MariaDB
* a host-side TCP probe for both

That is much closer to the actual container behavior than pretending every image is health-checked the same way.
