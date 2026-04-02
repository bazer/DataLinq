# Podman Test Infrastructure

These scripts are the first step toward a Podman-first test environment for DataLinq.

The version matrix lives in `test-infra/podman/matrix.json`.

## Commands

Start or resume the test databases:

```powershell
.\test-infra\podman\up.ps1
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

Start a specific LTS compatibility profile:

```powershell
.\test-infra\podman\up.ps1 -Profile mariadb-10.11-lts
```

## Defaults

The scripts and `DataLinq.Testing` use the same default values:

* Container prefix: `datalinq-tests`
* Active profile: `current-lts`
* MySQL host/port: `127.0.0.1:3307`
* MariaDB host/port: `127.0.0.1:3308`
* Admin user/password: `datalinq` / `datalinq`
* Application user/password: `datalinq` / `datalinq`
* Initial database: `datalinq_employees`

## Environment Variables

You can override the defaults with environment variables:

* `DATALINQ_TEST_PODMAN_POD`
  This is now just the naming prefix used for the MySQL and MariaDB containers. The scripts no longer run both databases inside one Podman pod, because that makes both servers fight over port `3306` inside the shared network namespace.
* `DATALINQ_TEST_PROFILE`
* `DATALINQ_TEST_DB_HOST`
* `DATALINQ_TEST_MYSQL_PORT`
* `DATALINQ_TEST_MARIADB_PORT`
* `DATALINQ_TEST_DB_ADMIN_USER`
* `DATALINQ_TEST_DB_ADMIN_PASSWORD`
* `DATALINQ_TEST_DB_APP_USER`
* `DATALINQ_TEST_DB_APP_PASSWORD`
* `DATALINQ_TEST_EMPLOYEES_DB`

When you run `up.ps1` or `wait.ps1`, the scripts also persist the resolved runtime settings to `artifacts/testdata/podman-settings.json`. `DataLinq.Testing` reads that file automatically, so tests can pick up the right Podman host even if they are launched from a different shell.

## Supported LTS Targets

Current matrix:

* MySQL: `mysql:8.4`
* MariaDB: `mariadb:10.11`
* MariaDB: `mariadb:11.4`
* MariaDB: `mariadb:11.8`

Default active profile:

* `current-lts` = `mysql:8.4` + `mariadb:11.8`

The important point is that images are pinned by series, not floating tags. That is necessary if version compatibility is part of what the test suite is claiming to validate.

## Isolation Model

The TUnit migration is moving toward per-test databases for server-backed runs instead of one shared mutable schema.

That requires two things:

* The container startup elevates the existing host-accessible `datalinq` user with global privileges for test database lifecycle work.
* The regular application user is granted access only to the bootstrap database by the official images, so `DataLinq.Testing` creates per-test databases and grants that user access before running schema creation.

That design is stricter and more scalable than reusing one shared server database across the whole suite.

There is one ugly but necessary Windows wrinkle here: with Podman machine, `127.0.0.1` goes through Windows port forwarding, and that path can break MySQL auth even when the credentials are correct. The scripts therefore resolve the Podman machine IP and persist it for the test harness instead of assuming localhost. That is test-infra behavior, not a production recommendation.

## Why Not One Pod

Running MySQL and MariaDB inside the same Podman pod was the wrong design.

Both images listen on container port `3306`, and containers inside a pod share one network namespace. That means the second server cannot start because `3306` is already taken. The scripts now run two standalone containers with separate host port mappings instead:

* MySQL: `127.0.0.1:3307 -> 3306`
* MariaDB: `127.0.0.1:3308 -> 3306`

## Readiness Checks

MariaDB 11.x container images are not a drop-in match for old `mysqladmin ping` health checks.

The scripts now use:

* `mysqladmin ping` for MySQL
* `healthcheck.sh --connect --innodb_initialized` for MariaDB
* a host-side TCP port probe for both servers

That is closer to what the official MariaDB image actually supports.
