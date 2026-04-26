# Configuration and Model Generation

For most new projects, the CLI-driven path is the right way to get started.

The job here is simple:

1. describe your database in `datalinq.json`
2. run `create-models`
3. inspect the generated output

## Create `datalinq.json`

Minimal MariaDB or MySQL example:

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

For MySQL, change `"Type": "MariaDB"` to `"Type": "MySQL"`.

Minimal SQLite example:

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

## Keep Secrets Out of the Shared File

Use `datalinq.user.json` for machine-local overrides such as real connection strings.

That is the sane setup:

- `datalinq.json` for shared structure
- `datalinq.user.json` for local connection details

If you want the exact merge behavior and full field reference, use [Configuration Files](../Configuration%20files.md).

## Generate Models

Once the config exists, generate the model surface:

```bash
datalinq create-models -n AppDb
```

If the selected database has more than one provider type configured, pass `-t` as well:

```bash
datalinq create-models -n AppDb -t MariaDB
```

## What Gets Generated

After generation, expect a generated model surface that gives you:

- a database model type such as `AppDb`
- generated immutable row types
- generated mutable row types
- generated helper extensions such as mutation helpers

You should treat the generated output as generated output. Do not hand-edit it and then be surprised when regeneration overwrites your changes.

Put custom code in source model files or partials instead.

## Optional: Generate SQL From Models

If you want schema SQL from the model metadata:

```bash
datalinq create-sql -n AppDb -o schema.sql
```

## What to Do Next

Now that the models exist, move to:

- [Your First Query and Update](Your%20First%20Query%20and%20Update.md)
