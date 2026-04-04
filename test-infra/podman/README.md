# Podman Test Infrastructure

The preferred entry point is now `DataLinq.Testing.CLI`.

The old PowerShell scripts still exist for the moment, but they are transitional. The CLI is the real direction because it is cross-platform, target-based, and owns the runtime state that `DataLinq.Testing` consumes.

The version matrix lives in `test-infra/podman/matrix.json`.

## Preferred CLI Commands

List known aliases, targets, and the current runtime state:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- list
```

Start the default local lane:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- up --alias latest
```

Run the SQLite-only quick lane:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- run --alias quick
```

Run the latest local lane:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- run --alias latest
```

Run the full supported matrix in batches:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- run --alias all --batch-size 2
```

Start every LTS server target at once:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- up --alias all
```

Stop all running test containers:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- down
```

Remove all test containers:

```powershell
dotnet run --project src/DataLinq.Testing.CLI -- down --remove
```

## Transitional PowerShell Commands

Start or resume the test databases:

```powershell
.\test-infra\podman\up.ps1
```

Start every LTS server target at once:

```powershell
.\test-infra\podman\up.ps1 -AllLts
```

Wait for both databases to become ready:

```powershell
.\test-infra\podman\wait.ps1
```

Stop the test containers:

```powershell
.\test-infra\podman\down.ps1
```

Stop and remove the pod and containers:

```powershell
.\test-infra\podman\down.ps1 -Remove
```

Recreate everything from scratch:

```powershell
.\test-infra\podman\reset.ps1
```

Recreate every LTS server target:

```powershell
.\test-infra\podman\reset.ps1 -AllLts
```

Run the compliance suite across the full LTS matrix in target batches:

```powershell
.\test-infra\podman\run-all-lts.ps1
```

Run the same matrix one target at a time:

```powershell
.\test-infra\podman\run-all-lts.ps1 -BatchSize 1
```

Start a specific LTS compatibility profile:

```powershell
.\test-infra\podman\up.ps1 -Profile mariadb-10.11-lts
```

## Visual Studio Runsettings

Use `Test -> Configure Run Settings -> Select Solution Wide runsettings File` in Visual Studio.

Available files in the solution root (`src`):

* `src/tests.quick.runsettings`
  Runs only `sqlite-file` and `sqlite-memory`.
* `src/tests.latest.runsettings`
  Runs `sqlite-file`, `sqlite-memory`, `mysql-8.4`, and `mariadb-11.8`.
* `src/tests.all.runsettings`
  Requests the full supported target set. This only works when every supported LTS server target is currently provisioned, for example after `dotnet run --project src/DataLinq.Testing.CLI -- up --alias all`. If the running target set is incomplete, the suite fails immediately with a clear error instead of silently running too little.

## Defaults

The scripts and `DataLinq.Testing` use the same default values:

* Container prefix: `datalinq-tests`
* Active profile: `current-lts`
* Host: resolved Podman machine IP on Windows, otherwise `127.0.0.1`
* MySQL 8.4 host port: `3307`
* MariaDB 11.8 host port: `3308`
* MariaDB 11.4 host port: `3309`
* MariaDB 10.11 host port: `3310`
* Admin user/password: `datalinq` / `datalinq`
* Application user/password: `datalinq` / `datalinq`
* Initial database: `datalinq_employees`

## Environment Variables

You can override the defaults with environment variables:

* `DATALINQ_TEST_PODMAN_POD`
  This is now just the naming prefix used for the MySQL and MariaDB containers. The scripts no longer run both databases inside one Podman pod, because that makes both servers fight over port `3306` inside the shared network namespace.
* `DATALINQ_TEST_PROFILE`
* `DATALINQ_TEST_DB_HOST`
* `DATALINQ_TEST_DB_ADMIN_USER`
* `DATALINQ_TEST_DB_ADMIN_PASSWORD`
* `DATALINQ_TEST_DB_APP_USER`
* `DATALINQ_TEST_DB_APP_PASSWORD`
* `DATALINQ_TEST_EMPLOYEES_DB`
* `DATALINQ_TEST_PROVIDER_SET`
  Controls which provider lane the compliance suite uses by default. Supported values are:
  `quick` for `sqlite-file` and `sqlite-memory`,
  `latest` for SQLite plus the newest supported MySQL and MariaDB LTS targets,
  `targets` for an explicit target list,
  `all` for the full supported matrix.
* `DATALINQ_TEST_TARGETS`
  Comma-separated target ids used when `DATALINQ_TEST_PROVIDER_SET=targets`.
* `DATALINQ_TEST_TARGET_ALIAS`
  Explicit alias expansion used by the new CLI when it wants the harness to resolve `quick`, `latest`, or `all` through the shared target model.

When you run the new CLI, it persists the resolved runtime settings to `artifacts/testdata/testinfra-state.json`. `DataLinq.Testing` reads that file automatically, so tests can pick up the right Podman host and the currently provisioned target ports even if they are launched from a different shell. The legacy PowerShell scripts still write `podman-settings.json`, and the harness can read that as a fallback during the transition.

## Supported LTS Targets

Current matrix:

* MySQL: `mysql:8.4`
* MariaDB: `mariadb:10.11`
* MariaDB: `mariadb:11.4`
* MariaDB: `mariadb:11.8`

Default active profile:

* `current-lts` = `mysql:8.4` + `mariadb:11.8`

The important point is that images are pinned by series, not floating tags. That is necessary if version compatibility is part of what the test suite is claiming to validate.

The local matrix flow now has two modes on purpose:

* `.\test-infra\podman\up.ps1 -AllLts`
  Starts every LTS server target at once so `src/tests.all.runsettings` can run the whole matrix in a single pass.
* `.\test-infra\podman\run-all-lts.ps1 -BatchSize <n>`
  Fans out across the same targets in batches when you do not want every container running at once.

That batching mode avoids obviously wasteful duplication:

* SQLite runs once
* SQLite in-memory runs once
* each server target runs once

So if you choose `-BatchSize 2`, the first batch can run `mysql-8.4` plus the current MariaDB LTS, and later batches only run the remaining MariaDB versions instead of retesting MySQL and SQLite over and over.

## Isolation Model

The test harness now uses two isolation modes on purpose:

* Shared seeded databases for read-only compliance tests
* Per-test isolated databases for mutating tests and temporary-schema tests

That is the right tradeoff. Full per-test provisioning for every single read-only query test was technically clean and operationally stupid because it turned the suite into a 4-5 minute grind.

That requires the container startup to elevate the host-accessible `datalinq` user with global privileges, so isolated test databases can be created without per-test user and grant churn.

There is one ugly but necessary Windows wrinkle here: with Podman machine, `127.0.0.1` goes through Windows port forwarding, and that path can break MySQL auth even when the credentials are correct. The scripts therefore resolve the Podman machine IP and persist it for the test harness instead of assuming localhost. That is test-infra behavior, not a production recommendation.

## Why Not One Pod

Running MySQL and MariaDB inside the same Podman pod was the wrong design.

Both images listen on container port `3306`, and containers inside a pod share one network namespace. That means the second server cannot start because `3306` is already taken. The scripts now run standalone containers with separate host port mappings instead, one container per target version.

## Readiness Checks

MariaDB 11.x container images are not a drop-in match for old `mysqladmin ping` health checks.

The scripts now use:

* `mysqladmin ping` for MySQL
* `healthcheck.sh --connect --innodb_initialized` for MariaDB
* a host-side TCP port probe for both servers

That is closer to what the official MariaDB image actually supports.
