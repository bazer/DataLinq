# Podman MySQL and MariaDB Environment Investigation

This note records the local Windows/Podman test environment as observed on 2026-05-03. It exists because the same provider tests can pass, fail as a MySQL authentication problem, or fail as a sandbox networking problem depending on which host address the test process uses.

## Executive Summary

- The server-backed tests use Podman containers managed by `src/DataLinq.Testing.CLI`.
- On native Windows, the normal non-sandboxed Testing CLI resolves the Podman WSL VM address, currently `172.24.224.81`, and connects through the published host ports.
- The Codex sandbox can open TCP connections to loopback ports such as `127.0.0.1:3307`, but it cannot open TCP connections to the Podman VM address `172.24.224.81`.
- MariaDB worked through loopback because Windows loopback port `3308` was owned by Podman's `wslrelay`, so the connection reached the MariaDB container.
- MySQL 8.4 did not work through loopback on the old port `3307` because that Windows port was owned by a local Windows `mysqld` process, not Podman's `wslrelay`.
- The matrix was moved to high host ports `13307` through `13310`, then all Podman targets were recreated. After that, sandbox loopback MySqlConnector probes reached the intended containers. MariaDB 11.8 passed its provider lane; MySQL 8.4 no longer fails with authentication or connectivity errors, but the current MySQL suite still has separate provider-metadata assertion failures.
- The Testing CLI state persistence was hardened so a targeted `run --targets ...` refreshes the state to the actually running infrastructure instead of leaving `artifacts/testdata/testinfra-state.json` narrowed to the last selected target.
- Outside the sandbox, MySQL 8.4 works when the client connects to the Podman VM address, because the server sees `datalinq` from `172.24.224.1` and matches `datalinq`@`%`.
- Therefore there are two distinct issues:
  - Sandbox networking blocks the Podman VM address.
  - The old MySQL host port `3307` collided with a local Windows MySQL listener, so sandbox loopback traffic went to the wrong server.

## Repo Configuration

The target matrix lives in `test-infra/podman/matrix.json`.

| Target | Image | Host port | Container port | Default |
| --- | --- | ---: | ---: | --- |
| `mysql-8.4` | `mysql:8.4` | `13307` | `3306` | Yes |
| `mariadb-10.11` | `mariadb:10.11` | `13310` | `3306` | No |
| `mariadb-11.4` | `mariadb:11.4` | `13309` | `3306` | No |
| `mariadb-11.8` | `mariadb:11.8` | `13308` | `3306` | Yes |

The default profile is `current-lts`, which contains `mysql-8.4` and `mariadb-11.8`. The `latest` test alias expands to:

- `sqlite-file`
- `sqlite-memory`
- `mysql-8.4`
- `mariadb-11.8`

The Testing CLI builds container names as:

```text
{DATALINQ_TEST_CONTAINER_PREFIX or "datalinq-tests"}-{target-id}
```

For the default prefix, the server containers are named:

- `datalinq-tests-mysql-8.4`
- `datalinq-tests-mariadb-10.11`
- `datalinq-tests-mariadb-11.4`
- `datalinq-tests-mariadb-11.8`

## Testing CLI Lifecycle

`TestInfraOrchestrator` owns the lifecycle:

1. Resolve target selection.
2. Ensure Podman is available.
3. Create missing containers with `podman run -d --name ... -p {hostPort}:3306`.
4. Start stopped containers.
5. Wait for in-container readiness.
6. Provision test users and grants.
7. Wait for the selected host and port to accept TCP.
8. Persist runtime state to `artifacts/testdata/testinfra-state.json`.

State persistence should describe the running infrastructure, not the last test selection. `up`, `wait`, and server-backed `run` calls refresh the state from the containers that are actually running and include the local SQLite targets. A one-target verification command should therefore not make later runs believe only that one server target exists.

The current container creation command includes:

```text
--max_connections=250
--character-set-server=utf8mb4
--collation-server=utf8mb4_unicode_ci
--skip-name-resolve
```

`--skip-name-resolve` was added after some older MariaDB containers already existed. Before the high-port reset:

- `mysql-8.4` and `mariadb-11.8` were recreated with `--skip-name-resolve`.
- `mariadb-10.11` and `mariadb-11.4` were older containers without the argument in their Podman command line.
- All inspected servers still reported `@@skip_name_resolve = 1`.

After the high-port reset, all four current Podman containers were recreated from the current command line and include `--skip-name-resolve`.

The current grant code creates or alters both wildcard and localhost accounts:

```text
CREATE USER IF NOT EXISTS 'datalinq'@'%' IDENTIFIED BY 'datalinq';
ALTER USER 'datalinq'@'%' IDENTIFIED BY 'datalinq';
CREATE USER IF NOT EXISTS 'datalinq'@'localhost' IDENTIFIED BY 'datalinq';
ALTER USER 'datalinq'@'localhost' IDENTIFIED BY 'datalinq';
GRANT ALL PRIVILEGES ON *.* TO ... WITH GRANT OPTION;
```

When admin and application users are both `datalinq`, the same user receives grant option.

## Persisted Runtime State

At the start of this investigation, `artifacts/testdata/testinfra-state.json` had been narrowed to only `mariadb-11.8`. That happened because `run` persisted a single selected target. It was misleading because all four Podman containers were actually running.

Before the CLI hardening, the state was repaired with:

```powershell
dotnet run --project src\DataLinq.Testing.CLI --no-build -- wait --alias latest
```

After the CLI hardening and sandbox verification, state is refreshed from the running containers. On this machine all four server containers are currently running, so the state is:

```json
{
  "Version": 1,
  "AliasName": "all",
  "Host": "127.0.0.1",
  "AdminUser": "datalinq",
  "AdminPassword": "datalinq",
  "ApplicationUser": "datalinq",
  "ApplicationPassword": "datalinq",
  "Targets": [
    { "Id": "sqlite-file", "Runtime": "Local", "Port": null },
    { "Id": "sqlite-memory", "Runtime": "Local", "Port": null },
    { "Id": "mariadb-10.11", "Runtime": "Podman", "Port": 13310 },
    { "Id": "mariadb-11.4", "Runtime": "Podman", "Port": 13309 },
    { "Id": "mariadb-11.8", "Runtime": "Podman", "Port": 13308 },
    { "Id": "mysql-8.4", "Runtime": "Podman", "Port": 13307 }
  ]
}
```

If a normal non-sandboxed `wait --alias latest` is run afterward, the state may instead show `AliasName` as `latest` and `Host` as the Podman VM address. Both `127.0.0.1` and the VM address are valid outside the sandbox when the Windows ports are owned by Podman `wslrelay`; inside the Codex sandbox, use `127.0.0.1`.

## Podman Machine and Network

Observed Podman version:

| Component | Version | OS/Arch |
| --- | --- | --- |
| Client | `5.8.1` | `windows/amd64` |
| Server | `5.8.1` | `linux/amd64` |

Observed machine:

| Name | VM type | State | CPUs | Memory | Disk |
| --- | --- | --- | ---: | ---: | ---: |
| `podman-machine-default` | `wsl` | running | 12 | 2 GiB | 100 GiB |

Observed machine addresses:

| Interface | Address | Meaning |
| --- | --- | --- |
| `lo` | `10.255.255.254/32` | WSL loopback-related address |
| `eth0` | `172.24.224.81/20` | Podman WSL VM address used by the Testing CLI outside the sandbox |
| `podman0` | `10.88.0.1/16` | Podman bridge gateway |

Observed Podman network:

| Network | Driver |
| --- | --- |
| `podman` | `bridge` |

Observed running containers:

| Container | Image | Host port | Container IP | Command includes `--skip-name-resolve` |
| --- | --- | ---: | --- | --- |
| `datalinq-tests-mysql-8.4` | `mysql:8.4` | `13307` | `10.88.0.17` | Yes |
| `datalinq-tests-mariadb-10.11` | `mariadb:10.11` | `13310` | `10.88.0.18` | Yes |
| `datalinq-tests-mariadb-11.4` | `mariadb:11.4` | `13309` | `10.88.0.19` | Yes |
| `datalinq-tests-mariadb-11.8` | `mariadb:11.8` | `13308` | `10.88.0.20` | Yes |

All containers publish container TCP `3306` to host IP `0.0.0.0` on their assigned host ports.

## Windows Port Ownership

The decisive finding was port ownership on the Windows host before the matrix was moved:

| Port | Listener | Meaning |
| ---: | --- | --- |
| `3306` | local `mysqld` process | Local MariaDB service, not Podman |
| `3307` | local `mysqld` process | Local MySQL service/process, not Podman |
| `3308` | `wslrelay` | Podman-published `mariadb-11.8` |
| `3309` | `wslrelay` | Podman-published `mariadb-11.4` |
| `3310` | `wslrelay` | Podman-published `mariadb-10.11` |
| `3327` | `wslrelay` | Temporary MySQL probe container created during this investigation |

The service inventory showed local MariaDB and MySQL services:

| Service | State | Start mode | Path |
| --- | --- | --- | --- |
| `MariaDB` | Running | Auto | `C:\Program Files\MariaDB 10.11\bin\mysqld.exe` |
| `MySQL84` | Running | Auto | `C:\Program Files\MySQL\MySQL Server 8.4\bin\mysqld.exe` |

One local `mysqld` process was listening on `::3307` and `::33060`. Because the listener is on IPv6 any-address, connecting to `127.0.0.1:3307` or `localhost:3307` can hit the local Windows MySQL process rather than the Podman container. That is why the error text says `datalinq`@`localhost`: it is the local MySQL server rejecting the test credentials.

After moving the matrix and recreating all Podman targets, the active test ports were owned by `wslrelay`:

| Port | Listener | Meaning |
| ---: | --- | --- |
| `13307` | `wslrelay` | Podman-published `mysql-8.4` |
| `13308` | `wslrelay` | Podman-published `mariadb-11.8` |
| `13309` | `wslrelay` | Podman-published `mariadb-11.4` |
| `13310` | `wslrelay` | Podman-published `mariadb-10.11` |

## Users and Authentication

All inspected servers reported `@@skip_name_resolve = 1`.

### MySQL 8.4

Observed users:

| User | Host | Plugin | Notes |
| --- | --- | --- | --- |
| `datalinq` | `%` | `caching_sha2_password` | Has broad grants plus `datalinq_employees` grants |
| `datalinq` | `localhost` | `caching_sha2_password` | Has broad grants |
| `root` | `%` | `caching_sha2_password` | Created by MySQL image env |
| `root` | `localhost` | `caching_sha2_password` | Created by MySQL image |

In-container `mysql` client checks all authenticated with password `datalinq`:

| Command shape | `CURRENT_USER()` | `USER()` |
| --- | --- | --- |
| `mysql -h 127.0.0.1 -udatalinq -pdatalinq` | `datalinq@127.0.0.1` | `datalinq@127.0.0.1` |
| `mysql -h localhost -udatalinq -pdatalinq` | `datalinq@localhost` | `datalinq@localhost` |
| `mysql --protocol=TCP -h localhost -P 3306 -udatalinq -pdatalinq` | `datalinq@%` | `datalinq@::1` |

MySqlConnector probes from Windows against the old configured port:

| Endpoint | Result |
| --- | --- |
| `127.0.0.1:3307` | Fails against the local Windows MySQL listener: `Access denied for user 'datalinq'@'localhost'` |
| `localhost:3307` | Fails against the local Windows MySQL listener: `Access denied for user 'datalinq'@'localhost'` |
| `172.24.224.81:3307` | Succeeds: `CURRENT_USER() = datalinq@%`, `USER() = datalinq@172.24.224.1` |

After the matrix was moved and containers were recreated, the high-port loopback probe succeeded:

| Endpoint | Result |
| --- | --- |
| `127.0.0.1:13307` | Succeeds: `CURRENT_USER() = datalinq@%`, `USER() = datalinq@10.88.0.1` |

A temporary MySQL 8.4 container published on `127.0.0.1:3327` was also created during diagnosis to isolate MySQL authentication from the port collision. MySqlConnector succeeded against that temporary container from both inside and outside the sandbox.

### MariaDB

Observed users are consistent across the inspected MariaDB containers:

| User | Host | Plugin |
| --- | --- | --- |
| `datalinq` | `%` | `mysql_native_password` |
| `datalinq` | `localhost` | `mysql_native_password` |
| `root` | `%` | `mysql_native_password` |
| `root` | `localhost` | `mysql_native_password` |

In-container `mariadb` client checks authenticated with password `datalinq`:

| Command shape | `CURRENT_USER()` | `USER()` |
| --- | --- | --- |
| `mariadb -h 127.0.0.1 -udatalinq -pdatalinq` | `datalinq@%` | `datalinq@127.0.0.1` |
| `mariadb -h localhost -udatalinq -pdatalinq` | `datalinq@localhost` | `datalinq@localhost` |
| `mariadb --protocol=TCP -h localhost -P 3306 -udatalinq -pdatalinq` | `datalinq@%` | `datalinq@::1` |

MySqlConnector probes from Windows:

| Endpoint | Result |
| --- | --- |
| `127.0.0.1:13308` | Succeeds: `CURRENT_USER() = datalinq@%`, `USER() = datalinq@10.88.0.1` |
| `172.24.224.81:13308` | Succeeds outside sandbox: `CURRENT_USER() = datalinq@%`, `USER() = datalinq@172.24.224.1` |

## Sandbox Versus Non-Sandbox Connectivity

The Codex sandbox cannot execute the Podman binary directly:

```text
Program 'podman.exe' failed to run ... Access denied.
```

The `dotnet-sandbox.ps1` wrapper preserves the real Podman executable and socket paths through:

- `DATALINQ_TEST_PODMAN_PATH`
- `DATALINQ_TEST_PODMAN_SOCKET`

That allows the Testing CLI to use the Podman socket for some container operations. It does not make every Podman operation available. In particular, `podman machine ssh ...` is unsupported by the socket transport and falls back to the Podman executable, which the sandbox cannot run.

Observed sandbox TCP behavior:

| Endpoint | Sandbox result |
| --- | --- |
| `127.0.0.1:13307` | TCP connect succeeds and reaches Podman MySQL |
| `127.0.0.1:13308` | TCP connect succeeds and reaches Podman MariaDB 11.8 |
| `127.0.0.1:13309` | TCP connect succeeds and reaches Podman MariaDB 11.4 |
| `127.0.0.1:13310` | TCP connect succeeds and reaches Podman MariaDB 10.11 |
| `172.24.224.81:13307` | Socket permission error |
| `172.24.224.81:13308` | Socket permission error |
| `172.24.224.81:13309` | Socket permission error |
| `172.24.224.81:13310` | Socket permission error |

That means:

- If state points to `172.24.224.81`, server-backed tests cannot connect inside the sandbox.
- If `DATALINQ_TEST_DB_HOST=127.0.0.1` is set for the sandboxed command, server-backed tests use the high loopback ports and can reach Podman.
- Outside the sandbox, `172.24.224.81` works for both MySQL and MariaDB.

## Why MariaDB Works and MySQL Does Not

MariaDB works through loopback because MySqlConnector reaches the Podman-published port. Windows shows `127.0.0.1:13308` as owned by `wslrelay`, and the server matches the client as `datalinq`@`%`. The observed `USER()` value is `datalinq@10.88.0.1`, which is the Podman bridge gateway, not `localhost`.

MySQL 8.4 failed through loopback on the old matrix because the same style of MySqlConnector connection reached the local Windows MySQL listener on `3307`, not the Podman container. That was true both inside and outside the sandbox. The sandbox was not causing the MySQL authentication failure; it merely prevented the normal workaround, which was connecting to the Podman VM address.

Outside the sandbox, the Testing CLI uses `podman machine ssh` to discover `172.24.224.81`, then MySqlConnector connects to the Podman VM host and configured port. MySQL sees the client as `datalinq@172.24.224.1`, matches `datalinq`@`%`, and succeeds.

Inside the sandbox, the Podman VM host is blocked by socket permissions. Setting `DATALINQ_TEST_DB_HOST=127.0.0.1` makes the test process use the high loopback ports, which are now owned by `wslrelay`.

## Resolution

The MySQL problem was not the Podman MySQL user table. It was host-port ownership. The repo assigned MySQL 8.4 to host port `3307`, but this machine already had a local Windows MySQL listener on `3307`.

The fix applied in this repo is to move the Podman database targets to high, less collision-prone host ports:

| Target | New host port |
| --- | ---: |
| `mysql-8.4` | `13307` |
| `mariadb-11.8` | `13308` |
| `mariadb-11.4` | `13309` |
| `mariadb-10.11` | `13310` |

After changing the matrix, all Podman containers were recreated with:

```powershell
dotnet run --project src\DataLinq.Testing.CLI --no-build -- reset --alias all
dotnet run --project src\DataLinq.Testing.CLI --no-build -- wait --alias latest
```

Sandboxed verification uses loopback explicitly:

```powershell
$env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI --no-build -- run --suite mysql --targets mysql-8.4 --batch-size 1 --output failures
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI --no-build -- run --suite mysql --targets mariadb-11.8 --batch-size 1 --output failures
Remove-Item Env:DATALINQ_TEST_DB_HOST
```

Results:

| Command | Result |
| --- | --- |
| MySQL 8.4 direct connection/probe via sandbox loopback | Reaches Podman MySQL 8.4 as `datalinq`@`%` |
| MySQL 8.4 provider lane via sandbox loopback | Reaches the container and runs; current result is `75/78 passed` with three provider-metadata assertion failures unrelated to auth or networking |
| MariaDB 11.8 provider lane via sandbox loopback | `80/80 passed` |

Changing grants alone was not sufficient. Live experiments changed `datalinq`@`localhost` to `caching_sha2_password`, added `datalinq`@`10.88.0.1`, added `datalinq`@`::1`, and eventually reduced the live MySQL user table to `datalinq`@`%`; `127.0.0.1:3307` still failed because it was still connecting to the local Windows MySQL listener.

The remaining hardening opportunity is to add a preflight check in the Testing CLI that detects when a configured host port is already owned by a non-Podman listener and fails with a direct diagnostic.
