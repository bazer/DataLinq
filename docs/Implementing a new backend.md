# Implementing a New SQL Provider

DataLinq has two different seams that are easy to confuse:

1. a public SQL provider/plugin surface for a database that fits the existing ADO.NET and metadata model;
2. an internal query-plan backend seam used by the built-in SQL and Memory executors.

0.9 does **not** ship a public SDK for arbitrary non-SQL execution backends. `IQueryPlanBackend`, `QueryExecutionRequest`, capability profiles, cursors, and the Memory executor are internal runtime architecture. Copying `DataLinq.Memory` and registering it through `PluginHook` is not a supported extension path.

The practical public extension is a SQL provider whose connection, command, reader, transaction, metadata, and DDL behavior can satisfy DataLinq's existing contracts.

## Public SQL Provider Surface

A complete provider normally owns four connected pieces:

| Concern | Public hook | Responsibility |
| --- | --- | --- |
| Runtime database creation | `IDatabaseProviderCreator` | Create the provider-specific `Database<T>` and configure logging/type recognition. |
| Schema metadata | `IMetadataFromDatabaseFactoryCreator` + `IMetadataFromSqlFactory` | Read tables, views, columns, keys, indexes, relations, defaults, and supported provider details into `DatabaseDefinition`. |
| DDL generation/application | `ISqlFromMetadataFactory` | Render supported metadata to provider SQL and create the database/file when requested. |
| Registration | `PluginHook.RegisterProvider` | Atomically bind one `DatabaseType` to all three creator/factory roles. |

The public interfaces are real, but this remains a low-level provider contract, not a polished third-party-provider SDK. You own consistency across runtime codecs, SQL rendering, metadata import, schema comparison, transactions, and tests.

## 1. Runtime and ADO.NET Integration

Implement `IDatabaseProviderCreator` and a provider-specific `Database<T>`/provider stack that can:

- create and open compatible `IDbConnection` instances;
- create parameterized commands and wrap data readers;
- bind canonical provider CLR values to the provider's physical/wire representation;
- decode provider values into canonical rows before model conversion;
- create and attach provider transactions;
- report commit/rollback status accurately enough for DataLinq's managed lifecycle;
- preserve command and transaction telemetry without leaking SQL or parameter values.

Transaction integration is not merely “call `Commit()`”. The DataLinq wrapper publishes committed cache state only after the database commit is known, distinguishes an unknown provider outcome from a known commit followed by local finalization failure, and invalidates transaction-derived mutable baselines conservatively. A provider adapter that lies about status will corrupt the higher-level lifecycle. Read [Transactions](Transactions.md) before implementing this layer.

## 2. Schema Metadata Reading

Implement `IMetadataFromDatabaseFactoryCreator` and return an `IMetadataFromSqlFactory` for schema introspection.

The reader must map the provider's supported surface into DataLinq metadata without silently flattening unsupported details. Existing providers use:

- MySQL/MariaDB `information_schema`;
- SQLite `sqlite_master` plus PRAGMA queries.

At minimum, decide and test:

- tables versus views;
- ordered columns and exact database names;
- provider SQL type, length, signedness, precision, and scale;
- nullability, primary keys, auto-increment/generated values, and defaults;
- simple/unique/composite indexes;
- ordered foreign keys and supported referential actions;
- comments/checks or an explicit unsupported disposition;
- canonical provider CLR types and UUID-format ambiguity.

When provider metadata cannot represent a DataLinq distinction, warn, reject, or leave it unresolved. Importing lossy metadata as if it were exact makes `validate` worse than useless.

## 3. The Default Type Contract

When a model has no provider-specific `[Type(...)]`, `ISqlFromMetadataFactory` translates DataLinq's default type names:

| Default type | Canonical CLR values | Typical native mapping |
| --- | --- | --- |
| `integer` | `int`, `uint`, `short`, `ushort` | `INT`, `INTEGER` |
| `big-integer` | `long`, `ulong` | `BIGINT` |
| `decimal` | `decimal` | `DECIMAL`, `NUMERIC` |
| `float`, `double` | `float`, `double` | `REAL`, `FLOAT`, `DOUBLE` |
| `text` | `string`, `char` | `TEXT`, `VARCHAR`, `CLOB` |
| `boolean` | `bool` | `BOOLEAN`, `BIT`, integer affinity |
| `datetime`, `timestamp` | `DateTime` | Provider temporal type |
| `date`, `time` | `DateOnly`, `TimeOnly` | Provider temporal type |
| `uuid` | `Guid` | Native UUID, text, or 16-byte binary |
| `blob` | `byte[]` | `BLOB`, `VARBINARY` |
| `json`, `xml` | usually `string` | Native type or documented text fallback |

These names are a portability input, not a promise of identical semantics. Provider-specific type attributes, scalar converters, and physical UUID storage still have to resolve to a compatible mapping.

For canonical `Guid`, implement every claimed `GuidStorageFormat` consistently across parameters, readers, DDL, fixed defaults, schema validation, and joins. Bare binary types do not reveal byte order. See the [`[GuidStorage]` contract](Attributes%20and%20Model%20Definitions.md#guidstorage).

## 4. DDL From Metadata

Implement `ISqlFromMetadataFactory` so the metadata reader and SQL generator round-trip the same supported subset.

Cover:

- identifier quoting and database names;
- provider-specific/default type translation;
- primary keys, unique/simple indexes, and ordered foreign keys;
- nullability, auto-increment, defaults, and supported referential actions;
- view definitions inside the provider's documented boundary;
- UUID physical formats and fail-closed unsupported/default-generation cases.

DataLinq's `diff` command is intentionally more conservative than the raw DDL generator. Do not broaden migration claims merely because a provider can emit `CREATE TABLE`.

## 5. Register the Provider

Register all required roles for the same `DatabaseType`:

```csharp
bool installed = PluginHook.RegisterProvider(
    type, databaseProviderCreator, sqlFactory, metadataFactoryCreator);
```

Registration publishes the three services together. Concurrent first use is safe;
the first registration wins and later calls return `false`. Built-in providers'
`HasBeenRegistered` properties read this central state. Use `replaceExisting: true`
only when intentionally replacing a provider's complete registration.

**Extension API migration:** the former mutable dictionary fields are now read-only
snapshot properties. Code that assigned those fields or updated individual entries
must migrate to `RegisterProvider` and be recompiled. Existing lookup/enumeration
source code still works. Captured snapshots remain unchanged after registration or
replacement; the service objects themselves are not made immutable. Use
`PluginHook.Registrations` or `TryGetRegistration` when several services must come
from the same registration during concurrent replacement.

## 6. Query Execution Boundary

SQL providers do not implement `IQueryPlanBackend` themselves. The core runtime's internal `SqlQueryPlanBackend` owns normalized-plan capability validation and SQL query execution, then calls the selected DataLinq read source/provider services.

That separation is intentional:

```text
ExpressionQueryPlanParser
  -> QueryPlanTemplate + invocation values
  -> QueryExecutionRequest
  -> source-owned SqlQueryPlanBackend
  -> capability validation
  -> QueryPlanSqlBuilder
  -> provider command/reader/transaction services
```

If a future release exposes a public non-SQL backend API, it will need a versioned capability, row, materialization, cancellation, and lifecycle contract. The internal 0.9 types are not that promise.

## 7. Verification Expectations

Do not ship a provider with happy-path CRUD tests. At minimum verify:

- provider metadata read -> DDL -> fresh schema -> metadata roundtrip;
- `validate` and conservative `diff` behavior for the supported subset;
- scalar converter and direct value reads/writes;
- every claimed UUID physical format, including legacy data and mismatches;
- generated primary-key lookup, cache hits, relations, and supported joins;
- query parameters, local membership, scalar results, and projection readers;
- insert/update/delete plus database-generated value hydration;
- commit, rollback, mutation failure, unknown outcome, attached transaction, disposal, and mutable invalidation;
- telemetry and redacted diagnostics;
- the shared TUnit compliance suite plus provider-specific edge cases.

The hard part is not SQL syntax. It is making metadata, canonical values, physical codecs, transactions, and cache identity tell the same story.
