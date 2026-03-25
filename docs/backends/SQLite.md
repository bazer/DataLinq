# DataLinq + SQLite

DataLinq provides SQLite support through the `DataLinq.SQLite` NuGet package. It uses the `Microsoft.Data.Sqlite` ADO.NET driver.

## What This Provider Actually Covers

SQLite support is not just runtime connectivity. The provider also includes:

- schema introspection from `sqlite_master` and PRAGMA queries
- SQLite-specific SQL generation
- SQLite transaction handling

That is important because SQLite is structurally different from the MySQL/MariaDB path. It does not have `information_schema`, and pretending otherwise would be lazy documentation.

## Type Mapping and Affinities

SQLite uses a flexible affinity system rather than strict static types. DataLinq's `create-models` behavior for SQLite is therefore partly type-based and partly convention-based.

### Standard Affinity Mapping

| SQLite Affinity | Maps to C# Type |
| :--- | :--- |
| `INTEGER` | `int` |
| `REAL` | `double` |
| `TEXT` | `string` |
| `BLOB` | `byte[]` |

### Convention-Based Smart Mapping

SQLite "smart" typing in DataLinq is based on column naming conventions, not magic. If your existing schema uses generic names, the generator will fall back to plain `string`, `int`, or `byte[]`.

| SQLite Affinity | If Column Name... | Generated C# Type |
| :--- | :--- | :--- |
| `TEXT` | Ends with `_date` | `DateOnly` |
| `TEXT` | Ends with `_time` | `TimeOnly` |
| `TEXT` | Ends with `_at` or contains `datetime` or `timestamp` | `DateTime` |
| `TEXT` | Is `guid` or `uuid`, or ends with `_guid` or `_uuid` | `Guid` |
| `INTEGER` | Starts with `is_` or `has_` | `bool` |
| `BLOB` | Is `guid` or `uuid`, or ends with `_guid` or `_uuid` | `Guid` |

### Enum Handling

SQLite does not have a native `ENUM` type. Define enums in your C# models if you want enum semantics. `create-models` will not infer them from an `INTEGER` column.

## SQL Generation

When generating a schema for SQLite from DataLinq models:

- `Guid` maps to `TEXT`
- `bool` maps to `INTEGER`
- `DateOnly`, `DateTime`, `TimeOnly`, and similar temporal values map to `TEXT`
- integer primary keys can use `AUTOINCREMENT`

The SQL generation layer also maps the default DataLinq type system onto SQLite's smaller affinity set. For example, default `json`, `xml`, `datetime`, `timestamp`, `date`, `time`, and `uuid` all end up as `TEXT`.

## Transaction Behavior

The SQLite transaction implementation currently opens transactions with `IsolationLevel.ReadUncommitted`.

That matters because visibility of uncommitted writes can differ from MySQL and MariaDB in the tests. If you are comparing provider behavior directly, account for that instead of calling it flaky.

## Best Practices

- Use naming conventions if you want richer generated types from an existing SQLite schema.
- Prefer `Guid` as `TEXT` unless you have a very specific reason not to.
- Define enums in your source models rather than expecting SQLite introspection to infer them.
