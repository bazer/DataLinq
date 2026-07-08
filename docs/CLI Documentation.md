# DataLinq CLI Documentation

The `datalinq` CLI reads `datalinq.json`, optionally merges `datalinq.user.json`, resolves configured secret references when a command needs a database connection, and runs configuration-driven model, SQL, database, validation, diff, and config inspection commands.

## Global Options

All commands accept:

- `-c`, `--config`: path to `datalinq.json` or a directory containing it
- `-v`, `--verbose`: print verbose messages

## Target Options

Commands that operate on a database target accept:

- `-n`, `--database`: database entry name in `datalinq.json`
- `-p`, `--provider`: provider on the selected database entry, such as `MySQL`, `MariaDB`, or `SQLite`
- `--data-source`: database name or file path override for the selected run

If the config contains more than one database, pass `--database`. If the selected database has more than one connection type, pass `--provider`.

## Commands

### `generate models`

Generates C# model declaration files from database metadata.

```bash
datalinq generate models -n AppDb -p SQLite
```

Options:

- `--fresh`: ignore existing files in `ModelDirectory` and regenerate from database metadata plus config
- `--overwrite-types`: replace preserved C# property types with database-inferred types
- `--stamp-generated-header`: add the CLI version and one UTC timestamp to generated file headers
- `--all`: generate models for all databases and connections in the selected config
- `--recursive`: discover `datalinq.json` files recursively and generate matching targets

By default, `generate models` reads existing files from `ModelDirectory` before writing. That is how supported class names, property names, relation names, and C# property types are preserved. See [Model Generation](model-generation.md).

For batch generation with `--all` or `--recursive`, DataLinq renders every target first. If any target fails before writing starts, no generated files are written.

`create-models` remains as a deprecated compatibility alias for `generate models`.

### `generate sql`

Generates a schema SQL script from the configured model files.

```bash
datalinq generate sql -n AppDb -p SQLite -o schema.sql
```

Options:

- `-o`, `--output`: required output path

### `database create`

Creates the selected database from configured model metadata.

```bash
datalinq database create -n AppDb -p SQLite
```

### `validate`

Compares configured model metadata with live database metadata. For workflow guidance and the exact comparison/safety model, see [Schema Validation and Diff](Schema%20Validation%20and%20Diff.md).

```bash
datalinq validate -n AppDb -p SQLite
```

Options:

- `--format`: `text` or `json`, default `text`
- `--all`: validate all databases and connections in the selected config
- `--recursive`: discover `datalinq.json` files recursively and validate matching targets

Exit codes:

- `0`: validation completed and no drift was detected
- `1`: validation completed and drift was detected
- `2`: command, configuration, connection, model parsing, or metadata loading failed

### `diff`

Runs validation and emits a conservative SQL suggestion script for supported additive drift. For review guidance and the current SQL generation boundary, see [Schema Validation and Diff](Schema%20Validation%20and%20Diff.md).

```bash
datalinq diff -n AppDb -p MariaDB -o update_schema.sql
```

Options:

- `-o`, `--output`: optional output path; if omitted, SQL is written to stdout

If validation issues are found, `diff` reports them and writes no SQL output file.

### `config list`

Lists databases and connections in the selected config.

```bash
datalinq config list -c ./datalinq.json
```

Options:

- `--recursive`: discover and list every readable `datalinq.json` under the selected directory or config location

### `config validate`

Validates `datalinq.json` and the matching `datalinq.user.json` without connecting to any database. This catches misspelled config properties, stale options such as `KeyPlacement`, unsupported enum values, and invalid merge state before generation or schema validation starts.

```bash
datalinq config validate -c ./datalinq.json
datalinq config validate --recursive
```

Options:

- `--recursive`: discover and validate every `datalinq.json` under the selected directory or config location

### `config init`

Interactively creates or completes DataLinq config files.

```bash
datalinq config init
```

Behavior depends on the files it finds:

- if neither file exists, it creates `datalinq.json` and `datalinq.user.json`
- if `datalinq.json` exists but `datalinq.user.json` is missing, it creates only the missing local user file
- if both files exist, it summarizes the current setup and changes nothing
- if only `datalinq.user.json` exists, it can create the missing shared config without rewriting the user file

New shared configs include the public JSON Schema URL and use `ModelDirectory`. Local connection details are written to `datalinq.user.json`.

### `config schema`

Writes the JSON Schema used for DataLinq config autocomplete and validation. Without `--output`, it writes `datalinq.schema.json` next to the selected config path.

```bash
datalinq config schema
datalinq config schema --update-config
datalinq config schema --output datalinq.schema.json
datalinq config schema --stdout
```

Editors use the schema only when `datalinq.json` or `datalinq.user.json` has a top-level `$schema` value, for example `"$schema": "./datalinq.schema.json"`. JSON supports one top-level `$schema` reference per file; use the public URL or the local file, not both. `--update-config` adds the local reference only to existing config files that do not already have a `$schema` value.

The published schema URL is:

```text
https://datalinq.org/schemas/datalinq.schema.json
```

### `secrets`

Manages DataLinq local secret values used by `${secret:name}` config references.

```bash
datalinq secrets list
datalinq secrets set datalinq/AppDb/password
datalinq secrets remove datalinq/AppDb/password
```

`list` prints names only. `set` prompts for the value and confirmation without putting the secret on the command line. `remove` asks for confirmation before deleting the entry.

Local secrets currently use Windows Credential Manager on Windows. On macOS and Linux, use `${env:NAME}` or `${prompt:label}` until a secure platform backend is available.

## Generated File Headers

CLI-generated model declaration files start with:

```csharp
// <auto-generated />
// Generated by DataLinq. Supported model class names, property names, relation names, and C# property types may be edited.
// See https://datalinq.org/docs/model-generation.html before changing mapping attributes or using --fresh.
#nullable enable
```

If the selected database config has `"UseNullableReferenceTypes": false`, DataLinq emits `#nullable disable` instead.

Source-generator implementation files use a stricter banner:

```csharp
// <auto-generated />
// Generated by DataLinq. Do not edit this compiler-generated implementation file.
```

## Common Failure Cases

- More than one database in config and no `--database`: the command fails until you select one.
- More than one provider on the selected database and no `--provider`: the command fails until you select one.
- `generate models --fresh` ignores existing model declarations, so supported C# surface edits are not preserved.
- Validation exit code `1` means real drift, not a CLI crash. Exit code `2` means validation could not produce a trustworthy comparison.
