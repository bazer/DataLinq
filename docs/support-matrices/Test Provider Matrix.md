# Test Provider Matrix

This page summarizes the targets used by DataLinq's local and CI-oriented test infrastructure.

The machine-readable source is:

```text
test-infra/podman/matrix.json
```

Local SQLite targets are provided by the test catalog, while MySQL and MariaDB server targets come from the Podman matrix.

## Target Aliases

| Alias | Targets | Intended use |
| --- | --- | --- |
| `quick` | `sqlite-file`, `sqlite-memory` | Fast local feedback without Podman. |
| `latest` | `sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-11.8` | Normal contributor lane with the newest default server-backed providers. |
| `all` | `sqlite-file`, `sqlite-memory`, `mysql-8.4`, `mariadb-10.11`, `mariadb-11.4`, `mariadb-11.8` | Broad provider verification before provider-sensitive changes close. |

## Server Targets

| Target id | Display name | Family | Version | Image | Host port | Default server target |
| --- | --- | --- | --- | --- | ---: | --- |
| `mysql-8.4` | MySQL 8.4 LTS | MySql | 8.4 | `mysql:8.4` | 13307 | Yes |
| `mariadb-10.11` | MariaDB 10.11 LTS | MariaDb | 10.11 | `mariadb:10.11` | 13310 | No |
| `mariadb-11.4` | MariaDB 11.4 LTS | MariaDb | 11.4 | `mariadb:11.4` | 13309 | No |
| `mariadb-11.8` | MariaDB 11.8 LTS | MariaDb | 11.8 | `mariadb:11.8` | 13308 | Yes |

The high host ports are intentional. They avoid the common local MySQL/MariaDB range around `3306` through `3310`, where a developer machine may already have unrelated database services.

## Server Profiles

| Profile id | Display name | Server targets | Default profile |
| --- | --- | --- | --- |
| `current-lts` | Current LTS | `mysql-8.4`, `mariadb-11.8` | Yes |
| `mariadb-10.11-lts` | MySQL 8.4 + MariaDB 10.11 | `mysql-8.4`, `mariadb-10.11` | No |
| `mariadb-11.4-lts` | MySQL 8.4 + MariaDB 11.4 | `mysql-8.4`, `mariadb-11.4` | No |
| `mariadb-11.8-lts` | MySQL 8.4 + MariaDB 11.8 | `mysql-8.4`, `mariadb-11.8` | No |
| `mysql-8.4-only` | MySQL 8.4 only | `mysql-8.4` | No |
| `mariadb-10.11-only` | MariaDB 10.11 only | `mariadb-10.11` | No |
| `mariadb-11.4-only` | MariaDB 11.4 only | `mariadb-11.4` | No |
| `mariadb-11.8-only` | MariaDB 11.8 only | `mariadb-11.8` | No |

## Implementation References

- `test-infra/podman/matrix.json`
- `src/DataLinq.Testing/Matrix/DatabaseServerMatrix.cs`
- `src/DataLinq.Testing/Matrix/TestTargetCatalog.cs`
- `src/DataLinq.Testing/Matrix/TestProviderMatrix.cs`
- `src/DataLinq.Testing.CLI/Selection/TargetSelectionResolver.cs`
- [Dev and Test Environment](../contributing/Dev%20and%20Test%20Environment.md)
- [DataLinq.Testing.CLI](../contributing/DataLinq.Testing.CLI.md)

Blunt maintenance rule: do not update this page from memory. Read `matrix.json` and the target catalog first.
