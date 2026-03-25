# DataLinq + MySQL & MariaDB

DataLinq provides unified support for both MySQL and MariaDB through the `DataLinq.MySql` NuGet package. It uses the `MySqlConnector` ADO.NET driver.

## What This Provider Actually Covers

This provider does three distinct jobs:

- runtime access through `DataLinq.MySql`
- metadata introspection from `information_schema`
- SQL generation for MySQL and MariaDB

That matters because MariaDB-specific behavior only appears when the connection type and factory selection are actually MariaDB-aware.

## Schema Introspection and Type Mapping

`create-models` reads schema metadata from `information_schema` and maps backend types to C# types. The mapping is aware of signedness, length, defaults, foreign keys, indices, and enum definitions.

| MySQL/MariaDB Type | Maps to C# Type |
| :--- | :--- |
| `INT UNSIGNED` | `uint` |
| `INT` | `int` |
| `BIGINT UNSIGNED` | `ulong` |
| `BIGINT` | `long` |
| `SMALLINT` | `short` |
| `TINYINT` | `sbyte` |
| `TINYINT UNSIGNED` | `byte` |
| `BIT(1)` | `bool` |
| `DECIMAL` | `decimal` |
| `DOUBLE`, `FLOAT` | `double`, `float` |
| `VARCHAR`, `TEXT`, `CHAR`, etc. | `string` |
| `DATE` | `DateOnly` |
| `DATETIME`, `TIMESTAMP` | `DateTime` |
| `TIME` | `TimeOnly` |
| `ENUM` | generated C# `enum` |
| `BINARY(16)` | `Guid` |
| `BLOB`, `VARBINARY`, etc. | `byte[]` |
| `JSON` | `string` |
| `UUID` (MariaDB only) | `Guid` |

Additional notes:

- `SET` is treated as `string`
- `BINARY(16)` is the normal `Guid` representation
- enums are emitted as generated C# enums with value metadata

## MariaDB-Specific Features

MariaDB can use a native `UUID` type. DataLinq supports that, but only when you actually use MariaDB-specific type metadata.

### Native `UUID` Type

- **Reading schema:** A MariaDB `UUID` column maps to `Guid`.
- **Generating schema:** To emit a native MariaDB `UUID`, use `[Type(DatabaseType.MariaDB, "uuid")]`.
- **Default behavior:** A plain `Guid` still defaults to `BINARY(16)` unless you override it.

### Provider Configuration

To leverage MariaDB-specific behavior, make sure your `datalinq.json` connection is marked as `MariaDB`:

```json
"Connections": [
  {
    "Type": "MariaDB",
    "DataSourceName": "my_mariadb_database",
    "ConnectionString": "..."
  }
]
```

If you mark the connection as `MySQL`, you are asking DataLinq to behave like MySQL even if the server happens to be MariaDB.

## Important Guid Caveat

`Guid` values stored as `BINARY(16)` depend on `MySqlConnector` `GuidFormat=LittleEndianBinary16` behaving the way the tests expect.

Native MariaDB `UUID` avoids that pain. `BINARY(16)` does not. This is a real caveat, not a theoretical one.

## SQL Generation

When generating a schema from your DataLinq models:

- MySQL maps `Guid` to `BINARY(16)` by default
- MariaDB also maps `Guid` to `BINARY(16)` by default unless you explicitly request native `UUID`
- view definitions use `CREATE OR REPLACE VIEW`

## Transaction Behavior

The current MySQL and MariaDB transaction implementation opens transactions with `IsolationLevel.ReadCommitted`.

That is relevant when comparing provider behavior to SQLite. Do not write cross-provider transaction visibility tests that assume they behave the same.
