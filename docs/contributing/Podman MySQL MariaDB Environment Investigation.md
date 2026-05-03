# Podman MySQL and MariaDB Environment Notes

This page records the current server-backed test environment guidance for local Windows/Podman development. Treat `docs/support-matrices/Test Provider Matrix.md`, `docs/contributing/Dev and Test Environment.md`, and `docs/contributing/DataLinq.Testing.CLI.md` as the primary references when this page drifts.

## Current Test Targets

The Podman-backed matrix uses high host ports to avoid common local MySQL and MariaDB services:

| Target | Host port |
| --- | ---: |
| `mysql-8.4` | `13307` |
| `mariadb-11.8` | `13308` |
| `mariadb-11.4` | `13309` |
| `mariadb-10.11` | `13310` |

Each target publishes container TCP `3306` through its assigned host port. If one of these ports is already occupied by a non-Podman process, fix the listener conflict or deliberately change the matrix before trusting server-backed test results.

## Codex Sandbox Guidance

On native Windows inside the Codex sandbox, set `DATALINQ_TEST_DB_HOST=127.0.0.1` for server-backed Testing CLI commands. The sandbox blocks direct TCP connections to the Podman VM address, while loopback reaches Podman's published listeners when the matrix ports are available.

Example:

```powershell
$env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --targets 'mysql-8.4,mariadb-11.8' --output failures --build
Remove-Item Env:DATALINQ_TEST_DB_HOST
```

Server-backed `run` commands refresh runtime state from the actually running containers. Targeted verification should not leave `artifacts/testdata/testinfra-state.json` narrowed to one server target.
