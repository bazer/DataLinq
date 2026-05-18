# DataLinq Configuration Files

DataLinq uses JSON configuration files for the CLI and model-generation workflow.

There are two files:

- `datalinq.json`
- `datalinq.user.json`

The CLI reads the main file first, then looks for a matching `.user.json` file next to it by replacing the extension. The user file is then merged on top.

For new projects, the easiest way to create both files is:

```bash
datalinq config init
```

## What This Config Is For

This config is used by the `datalinq` CLI.

It is not required for runtime database access if you are instantiating `MySqlDatabase<T>`, `MariaDBDatabase<T>`, or `SQLiteDatabase<T>` directly in application code.

---

## Config Discovery

By default, the CLI looks for `datalinq.json` in the current working directory.

You can also pass:

- a file path with `-c` or `--config`
- a directory path with `-c` or `--config`, in which case DataLinq appends `datalinq.json`

If the main file is:

```text
C:\repo\MyApp\datalinq.json
```

then the CLI will also look for:

```text
C:\repo\MyApp\datalinq.user.json
```

---

## Comments in JSON

The config reader strips both:

- `// single-line comments`
- `/* multi-line comments */`

That means comment-bearing JSON examples work in practice even though standard JSON does not normally allow comments.

---

## CLI Validation

DataLinq reads config files strictly. Unknown properties and unsupported option values fail fast instead of being ignored, so stale fields such as `KeyPlacement` are treated as configuration errors.

Run a config-only check when editing shared or user config:

```bash
datalinq config validate
datalinq config validate --recursive
```

This command reads `datalinq.json`, merges the matching `datalinq.user.json` when present, and validates the config shape without opening a database connection.

---

## Editor Autocomplete and Validation

DataLinq publishes a JSON Schema for config files:

```text
https://datalinq.org/schemas/datalinq.schema.json
```

New `datalinq config init` shared configs include it automatically:

```json
{
  "$schema": "https://datalinq.org/schemas/datalinq.schema.json",
  "Databases": []
}
```

You can also write the embedded schema from the CLI. Without `--output`, the command writes `datalinq.schema.json` next to the selected config path; use `--stdout` when you specifically want terminal output.

```bash
datalinq config schema
datalinq config schema --update-config
datalinq config schema --output datalinq.schema.json
datalinq config schema --stdout
```

Editors do not auto-discover a local `datalinq.schema.json` file. The config file needs one top-level `$schema` value, such as `"$schema": "./datalinq.schema.json"`. JSON Schema tooling does not support multiple top-level `$schema` references in one file; if the config already points at the public URL, replace it with the local file reference when you want local schema validation. `--update-config` adds the local reference only when the file does not already have `$schema`.

DataLinq accepts comments in config files, but editor behavior depends on the editor. If your editor treats `.json` files as strict JSON, it may warn about comments even though the CLI accepts them.

---

## Minimal Example: MariaDB or MySQL

```json
{
  "$schema": "https://datalinq.org/schemas/datalinq.schema.json",
  "Databases": [
    {
      "Name": "AppDb",
      "CsType": "AppDb",
      "Namespace": "MyApp.Models",
      "ModelDirectory": "Models",
      "Connections": [
        {
          "Type": "MariaDB",
          "DataSourceName": "appdb",
          "ConnectionString": "Server=localhost;Database=appdb;User ID=app;Password=${env:DATALINQ_APPDB_PASSWORD};"
        }
      ]
    }
  ]
}
```

Generate models:

```bash
datalinq generate models -n AppDb
```

Generate SQL:

```bash
datalinq generate sql -n AppDb -o schema.sql
```

If you want MySQL instead of MariaDB, change `"Type": "MariaDB"` to `"Type": "MySQL"`. The password in this example comes from an environment variable so it does not need to be committed to the config file.

---

## Minimal Example: SQLite

```json
{
  "$schema": "https://datalinq.org/schemas/datalinq.schema.json",
  "Databases": [
    {
      "Name": "AppDb",
      "CsType": "AppDb",
      "Namespace": "MyApp.Models",
      "ModelDirectory": "Models",
      "Connections": [
        {
          "Type": "SQLite",
          "DataSourceName": "app.db",
          "ConnectionString": "Data Source=app.db;Cache=Shared;"
        }
      ]
    }
  ]
}
```

The `DataSourceName` is also used as the default target file name for SQLite operations unless you override it with `--data-source`.

---

## Using `datalinq.user.json`

The normal pattern is:

- keep shared structure in `datalinq.json`
- keep local connection details or secrets in `datalinq.user.json`

Example shared config:

```json
{
  "$schema": "https://datalinq.org/schemas/datalinq.schema.json",
  "Databases": [
    {
      "Name": "AppDb",
      "CsType": "AppDb",
      "Namespace": "MyApp.Models",
      "ModelDirectory": "Models"
    }
  ]
}
```

Example local override:

```json
{
  "Databases": [
    {
      "Name": "AppDb",
      "Connections": [
        {
          "Type": "SQLite",
          "DataSourceName": "app.local.db",
          "ConnectionString": "Data Source=app.local.db;Cache=Shared;"
        }
      ]
    }
  ]
}
```

### Important merge behavior

Overrides are applied per database name.

In practice, you should treat `Connections` as a replacing value, not as a deep-merged list. If you override a database entry in `datalinq.user.json`, include the full `Connections` array you want to use.

That matters for secrets too. If your shared config does not contain safe-to-commit connection strings, put the real connection details in `datalinq.user.json` and keep that file out of source control.

---

## Secret References

Connection strings can contain secret references. DataLinq resolves them when a CLI command needs to connect to the database and redacts resolved values from CLI-controlled diagnostics.

Supported references:

- `${env:NAME}` reads an environment variable. This is the best choice for CI.
- `${secret:name}` reads a DataLinq local secret set with `datalinq secrets set <name>`.
- `${prompt:label}` prompts during the current CLI run and keeps the value only in memory.

Examples:

```json
{
  "ConnectionString": "Server=localhost;Database=appdb;User ID=app;Password=${env:DATALINQ_APPDB_PASSWORD};"
}
```

```json
{
  "ConnectionString": "Server=localhost;Database=appdb;User ID=app;Password=${secret:datalinq/AppDb/password};"
}
```

```json
{
  "ConnectionString": "${secret:datalinq/AppDb/connection-string}"
}
```

For inline connection-string values, DataLinq resolves placeholders through the connection-string builder. A password containing a semicolon is escaped as a password value, not spliced into raw connection-string text.

Use local secrets like this:

```bash
datalinq secrets set datalinq/AppDb/password
datalinq secrets list
datalinq secrets remove datalinq/AppDb/password
```

`secrets list` prints names only. `secrets set` prompts for the value and confirmation instead of accepting a plaintext command-line argument.

Local DataLinq secrets currently use Windows Credential Manager on Windows. On macOS and Linux, `${secret:...}` fails clearly instead of writing a plaintext fallback file; use `${env:...}` or `${prompt:...}` there for now.

Prompt references are useful for local one-off commands, but they are the wrong choice for unattended automation. In non-interactive runs, `${prompt:...}` fails with an error instead of hanging or storing the value.

---

## Database Object Fields

Each item in `Databases` describes one logical database definition.

- `Name`  
  Required. Used by CLI selection via `-n` / `--database`.

- `CsType`  
  Optional. Defaults to `Name`.

- `Namespace`  
  Optional. Defaults to `Models`.

- `SourceDirectories`  
  Deprecated. Parsed for compatibility, but active CLI commands read existing model files from `ModelDirectory` instead.

- `ModelDirectory`  
  Optional. The directory where DataLinq writes CLI-generated model declaration files and reads existing model declarations during regeneration.

- `DestinationDirectory`  
  Deprecated compatibility alias for `ModelDirectory`. If both names are present for the same database, they must point to the same path.

- `ModelLayout`  
  Optional. Controls generated model member order. Defaults to `PropertyOrder: "Column"`, `PrimaryKeyPlacement: "Top"`, `ForeignKeyPlacement: "Inline"`, and `RelationPlacement: "Bottom"`.

- `Include`  
  Optional. Limits generation to selected tables or views.

- `UseFileScopedNamespaces`  
  Optional. Defaults to `false`.

- `UseNullableReferenceTypes`  
  Optional. Defaults to `true`. When enabled, generated C# uses nullable reference annotations for nullable reference-type columns and starts with `#nullable enable`. Set this to `false` to keep reference-type annotations out of generated files; DataLinq then emits `#nullable disable` in those files.

- `CapitalizeNames`  
  Optional. Defaults to `false`.

- `RemoveInterfacePrefix`  
  Optional. Defaults to `true`.

- `SeparateTablesAndViews`  
  Optional. Defaults to `false`.

- `Connections`  
  Required by actual CLI usage. If no usable connections exist, commands that need a provider will fail.

- `FileEncoding`  
  Optional. Defaults to UTF-8 without BOM. Supported examples include `UTF8` and `UTF8BOM`.

---

## Connection Fields

Each entry in `Connections` describes one provider-specific connection.

- `Type`  
  Required in practice. For the built-in CLI providers, use `MySQL`, `MariaDB`, or `SQLite`.

- `DatabaseName`  
  Optional alias. If `DataSourceName` is missing, this value is used instead.

- `DataSourceName`  
  Required in practice unless `DatabaseName` is present. This is the logical database name, server-side database name, or file name depending on provider.

- `ConnectionString`  
  Required in practice. May contain `${env:...}`, `${secret:...}`, or `${prompt:...}` references when used through the CLI.

---

## Generated C# File Headers and Nullable Context

Generated model and database files written by the CLI start with:

```csharp
// <auto-generated />
// Generated by DataLinq. Supported model class names, property names, relation names, and C# property types may be edited.
// See https://datalinq.org/docs/model-generation.html#editing-generated-models before changing mapping attributes or using --fresh.
#nullable enable
```

If `UseNullableReferenceTypes` is explicitly set to `false`, the nullable directive is `#nullable disable` instead.

The nullable directive is intentional. It makes generated files compile with the same nullable interpretation regardless of the consuming project's `<Nullable>` setting. In practical terms:

- omitted `UseNullableReferenceTypes` means nullable reference generation is enabled
- `UseNullableReferenceTypes: true` emits nullable reference annotations and `#nullable enable`
- `UseNullableReferenceTypes: false` opts out and emits `#nullable disable`

The source generator follows file-level nullable context too. If a model source file contains `#nullable enable` or `#nullable disable`, generated source for that model follows the file directive, falling back to the database declaration and then the project setting when no source-level context is available.

The CLI can also add a non-deterministic stamp to generated file headers:

```bash
datalinq generate models -n AppDb --stamp-generated-header
```

That adds the DataLinq CLI version and one UTC generation timestamp shared by the files from that generation run. Leave it off if you want regeneration to avoid timestamp-only source diffs.

---

## Selection Rules in the CLI

- If the config contains more than one database, pass `-n`.
- If the selected database contains more than one connection type, pass `-p`.
- If the config path points to a directory, the CLI resolves `datalinq.json` inside it.

---

## Practical Notes

- `generate models` writes generated files to `ModelDirectory`.
- When validation or rendering fails before file replacement, `generate models` reports the issues and does not replace generated output.
- Unless `--fresh` is used, `generate models` reads existing model declarations from `ModelDirectory` so supported C# surface edits can be preserved.
- For SQLite, the CLI may rewrite the `Data Source` value in the connection string based on the resolved target path.

---

## Summary

- `datalinq.json` is the main CLI config file.
- `datalinq.user.json` is the local override file discovered next to it.
- Comments are allowed because the reader strips them before JSON deserialization.
- The safest pattern is to keep shared structure in `datalinq.json` and local connection details in `datalinq.user.json`.
