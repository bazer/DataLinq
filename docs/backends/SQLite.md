# DataLinq + SQLite

DataLinq provides SQLite support through the `DataLinq.SQLite` NuGet package. It uses the `Microsoft.Data.Sqlite` ADO.NET driver.

## What This Provider Actually Covers

SQLite support is not just runtime connectivity. The provider also includes:

- schema introspection from `sqlite_master` and PRAGMA queries
- SQLite-specific SQL generation
- SQLite transaction handling

That is important because SQLite is structurally different from the MySQL/MariaDB path. It does not have `information_schema`, and pretending otherwise would be lazy documentation.

For drift checks and conservative SQL suggestions against SQLite metadata, see [Schema Validation and Diff](../Schema%20Validation%20and%20Diff.md). For the exact provider metadata boundary, see [Provider Metadata Support Matrix](../support-matrices/Provider%20Metadata%20Support%20Matrix.md).

## Type Mapping and Affinities

SQLite uses a flexible affinity system rather than strict static types. DataLinq's `generate models` behavior for SQLite is therefore partly type-based and partly convention-based.

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

SQLite does not have a native `ENUM` type. Define enums in your C# models if you want enum semantics. `generate models` will not infer them from an `INTEGER` column.

### UUID Physical Storage

SQLite has no native UUID type and its affinity does not encode UUID shape. For a direct `Guid` or a typed ID whose converter canonical type is `Guid`, use:

- `Text36` or `Text32` with SQLite `TEXT`;
- `Binary16LittleEndian` or `Binary16Rfc4122` with SQLite `BLOB`.

DataLinq's built-in model mapping from canonical `Guid` to SQLite `TEXT` preserves the dashed `Text36` compatibility form. A live `TEXT` schema alone still cannot prove dashed versus compact values, and a `BLOB` schema cannot prove byte order. SQLite `BLOB` therefore has no model-side byte-order default and must be explicit.

```csharp
[Type(DatabaseType.SQLite, "blob")]
[GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Binary16Rfc4122)]
[Column("external_id")]
public abstract Guid ExternalId { get; }
```

Changing the format requires rewriting the stored values. `datalinq diff` intentionally emits only a review comment for a known format change and never fabricates a UUID data migration. Supported Guid-key joins also require both columns to resolve the same active SQLite format.

See the authoritative [`[GuidStorage]` attribute contract](../Attributes%20and%20Model%20Definitions.md#guidstorage) and [Scalar Converters and Typed IDs](../Scalar%20Converters%20and%20Typed%20IDs.md).

## Default Value Handling

SQLite defaults are now imported from `PRAGMA table_info(...).dflt_value` and converted into typed DataLinq metadata where that can be done honestly.

Supported cases include:

- numeric literals
- quoted numeric literals such as `DEFAULT '0'`, converted according to the target property type
- string literals
- parseable temporal literals for `DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset`, `TimeSpan`, and `Guid`
- `CURRENT_DATE`, `CURRENT_TIME`, and `CURRENT_TIMESTAMP`
- parenthesized scalar defaults such as `(1.5)` or `('abc')`

Examples:

- `INTEGER DEFAULT '0'` can become `int`, `long`, or `bool` depending on the target model property
- `TEXT DEFAULT '2024-01-02'` can become a `DateOnly` default if the property type is `DateOnly`
- `TEXT DEFAULT CURRENT_TIMESTAMP` maps to DataLinq's dynamic current-timestamp default metadata

### Unsupported SQLite Default Expressions

SQLite will happily store arbitrary expression text as a default. DataLinq does not pretend all of those expressions are portable or safe to regenerate.

For unsupported expression-shaped defaults, DataLinq warns and skips the default rather than emitting questionable model code or fake provider parity.

## SQL Generation

When generating a schema for SQLite from DataLinq models:

- `Guid` maps to `TEXT`
- `bool` maps to `INTEGER`
- `DateOnly`, `DateTime`, `TimeOnly`, and similar temporal values map to `TEXT`
- integer primary keys can use `AUTOINCREMENT`

The SQL generation layer also maps the default DataLinq type system onto SQLite's smaller affinity set. For example, default `json`, `xml`, `datetime`, `timestamp`, `date`, `time`, and `uuid` all end up as `TEXT`.

Supported imported defaults are also emitted back out as `DEFAULT` clauses during SQLite SQL generation. Unsupported SQLite expressions are intentionally not round-tripped as if DataLinq understood them when it does not.

## Transaction Behavior

DataLinq-owned SQLite connections explicitly set `PRAGMA read_uncommitted = false` whenever they open. This includes non-query, scalar, and reader paths, so a pooled connection cannot leak a previous caller's dirty-read setting into normal DataLinq access.

Owned transactions begin with `IsolationLevel.Serializable` in deferred mode. The deferred start lets independent read transactions coexist; SQLite acquires the real write lock when a transaction writes. The owning transaction sees its own changes, while an ordinary outside reader cannot receive those pending values.

Call this **committed visibility**, not SQLite `ReadCommitted`. SQLite still has snapshot/serializable behavior and a single-writer model:

- file-backed WAL with private/default cache can keep serving the last committed snapshot during a pending write
- explicit shared-cache connections can return `SQLITE_LOCKED` instead of that snapshot
- writer contention can return `SQLITE_BUSY` within the configured timeout
- DataLinq does not automatically retry failed statements or transactions

DataLinq preserves the connection string's `Default Timeout` and an explicit command's `CommandTimeout`. When contention fails, the original `SqliteException` remains the thrown exception, including its SQLite error codes and message. The `datalinq.db.command` activity records the operation, command kind, transactional flag, error type, and error status without recording SQL text.

Transactions attached from caller-owned connections are deliberately not reconfigured. Their pragmas, isolation, timeout, and cache mode remain the caller's policy; complete them through the DataLinq wrapper as described in [Transactions](../Transactions.md#attaching-an-existing-adonet-transaction).

## Best Practices

- Use naming conventions if you want richer generated types from an existing SQLite schema.
- Prefer an explicit `Text36` `Guid` mapping unless you have a real storage/interoperability reason to use another format.
- Define enums in your source models rather than expecting SQLite introspection to infer them.
- Prefer WAL with private/default cache for file-backed concurrent readers and writers. Reserve shared cache for cases such as named in-memory databases that actually require it.
- DataLinq's CLI and test defaults omit `Cache` for file-backed databases. Explicit caller-supplied cache modes are preserved; named in-memory databases are normalized to shared cache so DataLinq's multiple connections see the same database.
