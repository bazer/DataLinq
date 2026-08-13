# Schema Validation and Diff

DataLinq has two schema trust commands:

- `datalinq validate` compares generated model metadata with live provider metadata.
- `datalinq diff` runs the same validation and writes a conservative SQL suggestion script.

They are drift tools, not a migration framework. That distinction matters. Validation tells you whether the generated model and the database still agree. Diff can generate a few safe additive SQL shapes, but it deliberately comments anything that needs human judgement.

## When To Use It

Run validation when:

- a database was changed outside the DataLinq model workflow
- generated models were edited by hand
- provider metadata behavior changed after an upgrade
- CI needs to prove that a deployed schema still matches the configured model

Run diff when validation reports drift and you want a reviewable starting point for additive database changes.

## Validate One Target

```bash
datalinq validate -n AppDb -p SQLite
```

Use `-n` when the config contains more than one database. Use `-p` when the selected database has more than one connection type.

Exit codes are intentionally automation-friendly:

| Exit code | Meaning |
| --- | --- |
| `0` | Validation completed and no drift was detected. |
| `1` | Validation completed and schema drift was detected. |
| `2` | Command, config, connection, model parsing, or metadata loading failed. |

For automation, use JSON output:

```bash
datalinq validate -n AppDb -p SQLite --format json
```

The JSON payload includes the target identity, table counts, `hasDifferences`, structured diagnostic `issues`, and `differences` with `kind`, `severity`, `safety`, `path`, and `message`.

## Validate Many Targets

Use `--all` to validate every database/connection target in the selected config:

```bash
datalinq validate --all --format json
```

Use `--recursive` to discover `datalinq.json` files under the selected location:

```bash
datalinq validate --recursive --format json
```

Batch JSON reports `hasFailures`, `hasDifferences`, a summary, per-target results, and failures. A batch exits with `2` if any target fails before validation can complete, even if other targets succeeded.

## What Validation Compares

Validation compares model metadata produced from the configured model files with metadata read from the live provider.

The current comparison covers:

| Area | Notes |
| --- | --- |
| Tables and views | Missing, extra, and table/view type mismatches are reported. |
| Columns | Missing/extra columns, type, nullability, primary-key flag, auto-increment flag, and default values are compared where the provider exposes them. |
| Indexes | Simple and unique index shape is compared. Unsupported provider-specific index details are intentionally outside the contract. |
| Foreign keys | Ordered foreign-key shape and supported referential actions are compared. |
| Checks and comments | MySQL and MariaDB compare supported checks and comments. SQLite currently does not compare checks or comments. |

UUID storage format is checked only after the physical SQL type matches. MySQL/MariaDB `CHAR(36)`/`VARCHAR(36)`, `CHAR(32)`/`VARCHAR(32)`, and native MariaDB `UUID` describe their representation in the schema. Bare MySQL/MariaDB `BINARY(16)` and SQLite `BLOB` or `TEXT` do not describe UUID byte order or text shape, so validation reports an `Error/Ambiguous` unresolved-format difference unless trusted metadata supplies the format. A known format change over the same SQL type requires manual data migration and is emitted only as a review comment; `diff` never invents a UUID data rewrite.

Configured validation parses model source without loading its compiled assembly. Raw provider/default `[GuidStorage]` declarations state direct UUID intent, but bare `Guid`, `System.Guid`, or `global::System.Guid` remains unresolved because an assembly-level scalar converter registration could change the canonical provider type. Property converter markers and typed IDs likewise need authoritative resolved metadata before UUID-format validation can make a compatibility claim.

Provider-specific metadata support is summarized in [Provider Metadata Support Matrix](support-matrices/Provider%20Metadata%20Support%20Matrix.md).

## Severity And Safety

Every difference has two useful classifications:

| Field | Values | Meaning |
| --- | --- | --- |
| `severity` | `Info`, `Warning`, `Error` | How much attention the mismatch deserves. |
| `safety` | `Informational`, `Additive`, `Destructive`, `Ambiguous` | Whether a tool can safely suggest SQL without pretending to understand intent. |

Missing model-defined objects are usually additive. Extra live-database objects are usually destructive because removing them could delete data or break another application. Shape mismatches are usually ambiguous because a type, nullability, default, primary-key, or foreign-key action change may need a provider-specific migration.

## Generate A Diff Script

```bash
datalinq diff -n AppDb -p MariaDB -o update_schema.sql
```

Without `-o`, SQL is written to stdout:

```bash
datalinq diff -n AppDb -p SQLite
```

`diff` is read-only. It never applies the script.

The generated script starts with a warning header. It emits SQL for supported additive shapes:

- missing tables
- missing non-primary-key columns
- missing simple indexes
- missing unique indexes

Everything else is emitted as a comment:

- destructive drift
- ambiguous drift
- informational drift
- additive drift that the script generator does not yet know how to render, such as missing foreign keys and checks
- provider-specific index shapes outside the supported script boundary

Adding a `NOT NULL` column without a default is emitted with a warning because it can fail on non-empty tables.

## What To Do With The Script

Treat the diff script as a review artifact:

1. Read the comments and decide whether each difference is intentional.
2. Run the SQL in a disposable copy of the database first.
3. Regenerate models or update the database until `datalinq validate` exits with `0`.
4. Commit the model/config changes and the reviewed migration script if your project stores migrations.

If you need ordering, data backfills, table rebuilds, online DDL, down migrations, or deployment orchestration, use your normal migration system. DataLinq's diff output is deliberately smaller than that.
