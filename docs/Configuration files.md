# DataLinq Configuration Files

DataLinq uses JSON configuration files for the CLI and model-generation workflow.

There are two files:

- `datalinq.json`
- `datalinq.user.json`

The CLI reads the main file first, then looks for a matching `.user.json` file next to it by replacing the extension. The user file is then merged on top.

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

## Minimal Example: MariaDB or MySQL

```json
{
  "Databases": [
    {
      "Name": "AppDb",
      "CsType": "AppDb",
      "Namespace": "MyApp.Models",
      "SourceDirectories": [ "Models/Source" ],
      "DestinationDirectory": "Models/Generated",
      "Connections": [
        {
          "Type": "MariaDB",
          "DataSourceName": "appdb",
          "ConnectionString": "Server=localhost;Database=appdb;User ID=app;Password=secret;"
        }
      ]
    }
  ]
}
```

Generate models:

```bash
datalinq create-models -n AppDb
```

Generate SQL:

```bash
datalinq create-sql -n AppDb -o schema.sql
```

If you want MySQL instead of MariaDB, change `"Type": "MariaDB"` to `"Type": "MySQL"`.

---

## Minimal Example: SQLite

```json
{
  "Databases": [
    {
      "Name": "AppDb",
      "CsType": "AppDb",
      "Namespace": "MyApp.Models",
      "DestinationDirectory": "Models/Generated",
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

The `DataSourceName` is also used as the default target file name for SQLite operations unless you override it with `-d`.

---

## Using `datalinq.user.json`

The normal pattern is:

- keep shared structure in `datalinq.json`
- keep local connection details or secrets in `datalinq.user.json`

Example shared config:

```json
{
  "Databases": [
    {
      "Name": "AppDb",
      "CsType": "AppDb",
      "Namespace": "MyApp.Models",
      "SourceDirectories": [ "Models/Source" ],
      "DestinationDirectory": "Models/Generated"
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

## Database Object Fields

Each item in `Databases` describes one logical database definition.

- `Name`  
  Required. Used by CLI selection via `-n` / `--name`.

- `CsType`  
  Optional. Defaults to `Name`.

- `Namespace`  
  Optional. Defaults to `Models`.

- `SourceDirectories`  
  Optional. Source model paths used when `create-models` reads existing source models.

- `DestinationDirectory`  
  Optional in the raw schema, but effectively required for generation commands that write files.

- `Include`  
  Optional. Limits generation to selected tables or views.

- `UseRecord`  
  Optional. Defaults to `false`.

- `UseFileScopedNamespaces`  
  Optional. Defaults to `false`.

- `UseNullableReferenceTypes`  
  Optional. Defaults to `false`.

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
  Required in practice. The runtime connection-string parser expects a real connection string here.

---

## Selection Rules in the CLI

- If the config contains more than one database, pass `-n`.
- If the selected database contains more than one connection type, pass `-t`.
- If the config path points to a directory, the CLI resolves `datalinq.json` inside it.

---

## Practical Notes

- `create-models` writes generated files directly to `DestinationDirectory`.
- When `--skip-source` is not used, `create-models` will also read from `SourceDirectories` if they are configured.
- For SQLite, the CLI may rewrite the `Data Source` value in the connection string based on the resolved target path.

---

## Summary

- `datalinq.json` is the main CLI config file.
- `datalinq.user.json` is the local override file discovered next to it.
- Comments are allowed because the reader strips them before JSON deserialization.
- The safest pattern is to keep shared structure in `datalinq.json` and local connection details in `datalinq.user.json`.
