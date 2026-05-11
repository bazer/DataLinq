> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as shipped migration behavior.
# Snapshot Migration Design

**Status:** Draft design with a first snapshot DTO implemented.

## Purpose

Stateless validation and diff scripts answer "what is different right now?"

Versioned migrations need a different input: "what did the model look like when the last migration was created?" Without that historical snapshot, a rename looks exactly like a drop plus an add, which is the worst possible default because it makes data loss look routine.

This design defines the minimum snapshot and migration history contract for a later `add-migration` / `update-database` workflow. It deliberately stops short of migration execution.

## Implemented Snapshot DTO

`SchemaMigrationSnapshot` is a versioned JSON snapshot of the schema metadata subset DataLinq can already validate credibly:

- database name, provider name, model type, model namespace, and generated UTC timestamp
- table names
- column names, C# type names, provider-specific database types, nullability, primary-key flag, auto-increment flag, semantic default values, and comments
- simple and unique indexes by name, characteristic, type, and ordered columns
- foreign keys by constraint name, dependent table/columns, and principal table/columns
- provider-supported check constraints and comments

The snapshot intentionally excludes runtime-only details, source locations, and unsupported provider metadata.

The first proposed file name is:

```text
datalinq.snapshot.json
```

If multi-database projects need multiple snapshots, prefer a deterministic provider/database suffix rather than packing unrelated databases into one file:

```text
datalinq.<database-name>.snapshot.json
```

## Snapshot Shape

Example:

```json
{
  "formatVersion": 1,
  "databaseName": "AppDb",
  "databaseType": "MariaDB",
  "modelType": "AppDb",
  "modelNamespace": "Example.App",
  "generatedAtUtc": "2026-05-01T12:00:00.0000000Z",
  "tables": [
    {
      "name": "account",
      "columns": [
        {
          "name": "id",
          "csType": "int",
          "dbType": {
            "databaseType": "MariaDB",
            "name": "int",
            "signed": true
          },
          "nullable": false,
          "primaryKey": true,
          "autoIncrement": true
        },
        {
          "name": "display_name",
          "csType": "string",
          "dbType": {
            "databaseType": "MariaDB",
            "name": "varchar",
            "length": 40
          },
          "nullable": false,
          "primaryKey": false,
          "autoIncrement": false,
          "default": "DefaultAttribute|anonymous",
          "comment": "Visible account name"
        }
      ],
      "indexes": [
        {
          "name": "idx_account_display_name",
          "characteristic": "Simple",
          "type": "BTREE",
          "columns": [ "display_name" ]
        }
      ],
      "foreignKeys": [],
      "checks": [
        {
          "name": "CK_account_id",
          "expression": "`id` > 0"
        }
      ],
      "comment": "Account table"
    }
  ]
}
```

The snapshot should stay deterministic:

- tables sorted by database name
- columns sorted by metadata index, then database name
- indexes, checks, and foreign keys sorted by stable names
- no source file paths, line numbers, reflection handles, or provider connection details

## Migration Identity

Future migration IDs should be immutable, sortable, and readable:

```text
yyyyMMddHHmmss_<slug>
```

Example:

```text
20260501120000_add_account_display_name
```

Rules:

- timestamp is UTC
- slug is generated from the user-supplied migration name
- migration ID is never changed after creation
- migration artifact includes the ID, product version, snapshot format version, provider, and checksum
- migration content is reviewable SQL or a C# operation model; the first production slice should prefer SQL because the diff generator already emits SQL

Proposed artifact path:

```text
Migrations/<migration-id>.sql
```

## Applied-Migration Table

Future execution needs a provider-owned history table:

```sql
__datalinq_migrations
```

Logical columns:

```text
id               varchar(150) primary key
product_version  varchar(50) not null
snapshot_format  int not null
checksum         varchar(64) not null
applied_at_utc   provider timestamp/text not null
description      text null
```

Provider notes:

- SQLite should store timestamps as UTC text unless a better existing convention is already used elsewhere in DataLinq.
- MySQL and MariaDB should use `datetime(6)` or `timestamp(6)` consistently with generated SQL style.
- The checksum should cover the migration artifact content that is actually applied, not the mutable current snapshot file.
- The table is not a model table and must be ignored by validation unless explicitly requested.

## Workflow

Future `add-migration` should:

1. Load current source model metadata.
2. Load the latest snapshot.
3. Compare snapshot to current model metadata.
4. Build a migration intent model from the differences.
5. Generate a reviewable migration artifact.
6. Write the next snapshot beside the artifact only after the artifact is created successfully.

Future `update-database` should:

1. Read the applied-migration table.
2. Find pending migration artifacts by ID.
3. Verify checksums before applying.
4. Apply each migration inside the safest transaction boundary the provider supports.
5. Insert the migration history row only after the migration succeeds.

## Rename Detection

Renames must not be inferred silently.

The comparer can report a rename candidate when it sees a missing object and an added object with compatible shape, but a candidate is only a hint. It is not permission to emit `RENAME TABLE` or `RENAME COLUMN`.

Future CLI behavior should require an explicit mapping:

```text
datalinq add-migration RenameAccountName --rename column:account.name=account.display_name
datalinq add-migration RenameUserTable --rename table:user=account
```

If the user does not confirm a rename, generated output must remain a drop/add warning or commented destructive operation. That is noisier, but it is honest. Guessing wrong on a rename is data loss dressed up as convenience.

The future operation model should represent renames directly:

```text
RenameTable(oldName, newName)
RenameColumn(tableName, oldName, newName)
```

Those operations can then produce provider-specific SQL:

- SQLite: `ALTER TABLE ... RENAME TO ...` and supported `ALTER TABLE ... RENAME COLUMN ...`
- MySQL/MariaDB: `RENAME TABLE ... TO ...` or `ALTER TABLE ... RENAME COLUMN ...`

## Non-Goals For This Slice

- no `add-migration` command
- no `update-database` command
- no runtime auto-migration
- no applied-migration table creation
- no destructive migration execution
- no automatic rename inference

Those features belong after validation and conservative diff generation have enough real use to prove the metadata contract is stable.
