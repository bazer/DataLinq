# Podman Test Infrastructure

These scripts are the first step toward a Podman-first test environment for DataLinq.

The version matrix lives in `test-infra/podman/matrix.json`.

## Commands

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

Available files:

* `tests.fast.runsettings`
  Runs SQLite plus the preferred local fast server target. This now prefers the MariaDB target from the active profile.
* `tests.profile.runsettings`
  Runs SQLite plus every server target in the active profile. By default that means `current-lts`.
* `tests.mariadb-10.11-lts.runsettings`
  Runs the profile lane against `mysql-8.4 + mariadb-10.11`.
* `tests.mariadb-11.4-lts.runsettings`
  Runs the profile lane against `mysql-8.4 + mariadb-11.4`.
* `tests.mariadb-11.8-lts.runsettings`
  Runs the profile lane against `mysql-8.4 + mariadb-11.8`.
* `tests.all-lts.runsettings`
  Requests the full logical LTS matrix. This only works when every LTS server target is currently provisioned, for example after `.\test-infra\podman\up.ps1 -AllLts`. If the running target set is incomplete, the suite fails immediately with a clear error instead of silently running too little.

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
  `fast` for SQLite plus one primary server target,
  `profile` for SQLite plus every server in the active Podman profile,
  `targets` for SQLite plus an explicit target list,
  `all-lts` for the full LTS matrix.
* `DATALINQ_TEST_TARGETS`
  Comma-separated server target ids used when `DATALINQ_TEST_PROVIDER_SET=targets`.
* `DATALINQ_TEST_INCLUDE_SQLITE`
  Set this to `false` when you want a batch to skip SQLite and SQLite in-memory so they only run once across a larger matrix pass.

When you run `up.ps1` or `wait.ps1`, the scripts also persist the resolved runtime settings to `artifacts/testdata/podman-settings.json`. `DataLinq.Testing` reads that file automatically, so tests can pick up the right Podman host and the currently provisioned target ports even if they are launched from a different shell.

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
  Starts every LTS server target at once so `tests.all-lts.runsettings` can run the whole matrix in a single pass.
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
