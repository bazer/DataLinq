# DataLinq + MySQL & MariaDB

DataLinq provides unified support for both MySQL and MariaDB through the `DataLinq.MySql` NuGet package. It uses the `MySqlConnector` ADO.NET driver.

## What This Provider Actually Covers

This provider does three distinct jobs:

- runtime access through `DataLinq.MySql`
- metadata introspection from `information_schema`
- SQL generation for MySQL and MariaDB

That matters because MariaDB-specific behavior only appears when the connection type and factory selection are actually MariaDB-aware.

For drift checks and conservative SQL suggestions against MySQL or MariaDB metadata, see [Schema Validation and Diff](../Schema%20Validation%20and%20Diff.md). For the exact provider metadata boundary, see [Provider Metadata Support Matrix](../support-matrices/Provider%20Metadata%20Support%20Matrix.md).

## Supported Server Versions

| Family | Supported LTS series | Default `latest` target |
| --- | --- | --- |
| MySQL | 8.4, 9.7 | 9.7 |
| MariaDB | 10.11, 11.4, 11.8, 12.3 | 12.3 |

The test matrix follows each official image's rolling minor tag within its LTS series. `latest` means the explicitly configured family defaults, not a lexical comparison of version strings. The full release lane retains every LTS series in this table.

## Schema Introspection and Type Mapping

`generate models` reads schema metadata from `information_schema` and maps backend types to C# types. The mapping is aware of signedness, length, defaults, foreign keys, indices, and enum definitions.

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
| `BINARY(16)` | `Guid` when UUID intent is resolved; physical byte order still needs trusted/explicit metadata |
| `BLOB`, `VARBINARY`, etc. | `byte[]` |
| `JSON` | `string` |
| `UUID` (MariaDB only) | `Guid` |

Additional notes:

- `SET` is treated as `string`
- `BINARY(16)` is MySQL's built-in `Guid` mapping, with the legacy little-endian layout as DataLinq's model-side compatibility default
- enums are emitted as generated C# enums with value metadata

`TIME` values mapped to `TimeOnly` must be within a single day, from `00:00:00`
through `23:59:59.999999`. Negative values and durations of 24 hours or more
throw `InvalidCastException` instead of wrapping into a different time of day.
For duration columns, declare the model property as `TimeSpan` (or `TimeSpan?`
for nullable columns) with a `time` provider type. This preserves negative and
multi-day values. Model generation retains `TimeOnly` as its default mapping,
so adjust generated model declarations for columns whose meaning is a duration.
Duration defaults preserve the sign, total hours, and microseconds in generated
MySQL/MariaDB SQL, and exact ticks in generated C# code. SQL generation rejects
sub-microsecond duration defaults that MySQL/MariaDB cannot represent.

Match time-of-day inputs to the column's fractional precision. For example,
MySQL can round `23:59:59.999999` stored in `TIME(0)` to `24:00:00`, which is
outside `TimeOnly`'s range. Use sufficient column precision or explicitly choose
the application's rounding/truncation policy before writing. DataLinq does not
silently wrap the stored duration to midnight.

## Default Value Handling

`generate models` imports MySQL and MariaDB defaults into DataLinq metadata instead of treating them as raw schema text.

That includes:

- quoted string defaults
- quoted numeric defaults such as `DEFAULT '0'`, converted to the actual C# property type instead of being treated as strings
- enum defaults, including enum labels reported from schema metadata
- `CURRENT_DATE`, `CURRENT_TIME`, `CURRENT_TIMESTAMP`
- MySQL/MariaDB temporal aliases such as `NOW()`, `LOCALTIME`, and `LOCALTIMESTAMP`
- parenthesized defaults such as `(0)` and `('abc')`

This matters because MySQL and MariaDB are loose about how defaults are represented in schema metadata. DataLinq normalizes the SQL literal first, then converts it according to the target property type.

Examples:

- `INT DEFAULT '0'` -> C# `int` default `0`
- `BIGINT DEFAULT '0'` -> C# `long` default `0L`
- `ENUM('standard','premium') DEFAULT 'premium'` -> generated enum member default
- `VARCHAR DEFAULT '""'` -> C# string default containing two double-quote characters, not leaked SQL quoting syntax

### Unsupported or Dangerous Defaults

Zero-date defaults such as `0000-00-00` and `0000-00-00 00:00:00` are not generated into typed date properties.

DataLinq warns and skips those defaults instead of emitting broken C# or pretending that invalid MySQL date garbage is fine.

## MariaDB-Specific Features

MariaDB can use a native `UUID` type. DataLinq supports that, but only when you actually use MariaDB-specific type metadata.

### Native `UUID` Type

- **Reading schema:** A MariaDB `UUID` column maps to `Guid`.
- **Generating schema:** To emit a native MariaDB `UUID`, use `[Type(DatabaseType.MariaDB, "uuid")]`.
- **Default behavior:** MariaDB SQL generation prefers native `UUID` for plain `Guid` properties.
- **Explicit storage:** `[GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.NativeUuid)]` records the physical contract and also applies to typed IDs whose converter canonical type is `Guid`.

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

## UUID Physical Storage

`Guid` and Guid-backed typed IDs have two independent mappings: model-to-canonical conversion and canonical-`Guid`-to-physical storage. Configure the latter with `[GuidStorage(...)]`.

For MySQL/MariaDB:

- `NativeUuid` is valid only for unmodified MariaDB `UUID`;
- `Text36` matches `CHAR(36)`/`VARCHAR(36)`;
- `Text32` matches `CHAR(32)`/`VARCHAR(32)`;
- `Binary16LittleEndian` and `Binary16Rfc4122` both match `BINARY(16)`.

The SQL type `BINARY(16)` cannot tell those two byte layouts apart. DataLinq's compatibility default is the legacy .NET/MySqlConnector little-endian layout, but existing data remains the real authority. DataLinq's mapped path encodes binary parameters as bytes and reads binary columns as bytes, so `[GuidStorage]`—not a vague connector default—is the physical contract. Raw ADO.NET reads/writes outside DataLinq must use the same layout. Native MariaDB `UUID` avoids binary byte-order ambiguity.

Changing the binary format without rewriting stored data is not a metadata edit. It is a data migration. Schema validation reports unresolved/mismatched formats and supported joins require the same resolved format on both keys before SQL execution.

See the authoritative [`[GuidStorage]` attribute contract](../Attributes%20and%20Model%20Definitions.md#guidstorage) and [Scalar Converters and Typed IDs](../Scalar%20Converters%20and%20Typed%20IDs.md).

## SQL Generation

When generating a schema from your DataLinq models:

- MySQL maps `Guid` to `BINARY(16)` by default
- MariaDB maps `Guid` to native `UUID` by default
- explicit `[Type(...)]` plus `[GuidStorage(...)]` overrides select compatible text or binary layouts instead
- view definitions use `CREATE OR REPLACE VIEW`

Default SQL generation is typed, not stringly:

- string and char defaults are SQL-quoted and escaped correctly
- `bit` defaults are emitted as `b'0'` and `b'1'`
- numeric defaults use invariant formatting
- `DateOnly`, `TimeOnly`, `DateTime`, and related values are emitted as provider-safe SQL literals
- MariaDB `uuid` defaults and MySQL `binary(16)` `Guid` defaults are emitted differently because they are genuinely different storage shapes

If DataLinq can parse a supported MySQL/MariaDB default and represent it in metadata, it should also be able to emit it back out correctly in generated SQL. That roundtrip is now something the test suite actually checks.

## Transaction Behavior

The current MySQL and MariaDB transaction implementation opens transactions with `IsolationLevel.ReadCommitted`.

That is relevant when comparing provider behavior to SQLite. Do not write cross-provider transaction visibility tests that assume they behave the same.
