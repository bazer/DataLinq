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

Stop the pod:

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

* Pod name: `datalinq-tests`
* Active profile: `current-lts`
* MySQL host/port: `127.0.0.1:3307`
* MariaDB host/port: `127.0.0.1:3308`
* Admin user/password: `root` / `datalinq-root`
* Application user/password: `datalinq` / `datalinq`
* Initial database: `datalinq_employees`

## Environment Variables

You can override the defaults with environment variables:

* `DATALINQ_TEST_PODMAN_POD`
* `DATALINQ_TEST_PROFILE`
* `DATALINQ_TEST_DB_HOST`
* `DATALINQ_TEST_MYSQL_PORT`
* `DATALINQ_TEST_MARIADB_PORT`
* `DATALINQ_TEST_DB_ADMIN_USER`
* `DATALINQ_TEST_DB_ADMIN_PASSWORD`
* `DATALINQ_TEST_DB_APP_USER`
* `DATALINQ_TEST_DB_APP_PASSWORD`
* `DATALINQ_TEST_EMPLOYEES_DB`

## Supported LTS Targets

Current matrix:

* MySQL: `mysql:8.4`
* MariaDB: `mariadb:10.11`
* MariaDB: `mariadb:11.4`
* MariaDB: `mariadb:11.8`

Default active profile:

* `current-lts` = `mysql:8.4` + `mariadb:11.8`

The important point is that images are pinned by series, not floating tags. That is necessary if version compatibility is part of what the test suite is claiming to validate.
