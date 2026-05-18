> [!WARNING]
> This document is roadmap/specification material. It describes planned behavior, not shipped DataLinq behavior.

# Specification: UUID Storage Format Support

**Status:** Draft specification.
**Goal:** Make DataLinq responsible for UUID/`Guid` storage semantics so reads, writes, queries, defaults, validation, and generated metadata do not depend on MySqlConnector connection-string behavior.

## Current State

DataLinq currently has useful but incomplete UUID support.

The current MySQL/MariaDB documentation is honest about the weak spot:

- MySQL maps `Guid` to `BINARY(16)` by default.
- MariaDB maps `Guid` to native `UUID` by default where supported.
- `BINARY(16)` `Guid` values depend on MySqlConnector `GuidFormat=LittleEndianBinary16`.
- Native MariaDB `UUID` avoids that connection-string dependency.

Relevant current files:

- `docs/backends/MySQL-MariaDB.md`
- `docs/backends/SQLite.md`
- `src/DataLinq.MySql/Shared/SqlProvider.cs`
- `src/DataLinq.MySql/Shared/SqlDataLinqDataReader.cs`
- `src/DataLinq.MySql/Shared/SqlDataLinqDataWriter.cs`
- `src/DataLinq.MySql/Shared/SqlFromMetadataFactory.cs`
- `src/DataLinq.MySql/MySql/SqlFromMySqlFactory.cs`
- `src/DataLinq.MySql/MariaDB/SqlFromMariaDBFactory.cs`
- `src/DataLinq.MySql/Shared/MetadataFromSqlFactory.cs`
- `src/DataLinq.SQLite/SQLiteProvider.cs`
- `src/DataLinq.SQLite/SQLiteDataLinqDataReader.cs`
- `src/DataLinq.SQLite/SQLiteDataLinqDataWriter.cs`
- `src/DataLinq/Query/Where.cs`
- `src/DataLinq/Linq/Visitors/WhereVisitor.cs`
- `src/DataLinq/Linq/QueryBuilder.cs`
- `src/DataLinq.SharedCore/Attributes/DefaultAttribute.cs`
- `src/DataLinq.SharedCore/Metadata/PropertyDefinition.cs`
- `src/DataLinq.Tests.MySql/MariaDbGuidTypeMappingTests.cs`

The implementation currently splits responsibility in a way that is too fragile:

- MySQL mutations convert `Guid` to `byte[]` for `BINARY(16)` using `Guid.ToByteArray()`.
- MySQL reads call `MySqlDataReader.GetGuid(...)`, so MySqlConnector decides how to decode bytes.
- MySQL query parameters are passed as raw `Guid` values unless a higher-level path already converted them.
- LINQ `Contains(...)` loses column metadata in some paths, so the provider cannot reliably encode each list value for the target column.
- SQLite query parameters normalize `Guid` to text at provider command creation, which is a healthier pattern than the MySQL path.

The tests encode the practical failure:

- native MariaDB `UUID` `Contains(...)` works without `GuidFormat`
- `BINARY(16)` `Contains(...)` requires `GuidFormat=LittleEndianBinary16`
- the same query returns no rows when `GuidFormat` is removed

That is not a MySqlConnector bug. It is a DataLinq ownership bug. MySqlConnector exposes several legitimate `GuidFormat` values, and DataLinq currently relies on one global connection setting to recover per-column storage meaning.

## Problem Statement

A database column type of `BINARY(16)` only says "sixteen bytes." It does not say which UUID byte order those bytes use.

Common layouts include:

- .NET `Guid.ToByteArray()` layout, which reverses the first 4-byte group and the next two 2-byte groups relative to the canonical UUID string
- RFC 4122 / string-order binary layout, matching MySQL `UUID_TO_BIN(x)`
- MySQL time-swapped layout, matching `UUID_TO_BIN(x, 1)` and `BIN_TO_UUID(x, 1)`
- plain text layouts such as `CHAR(36)` or `CHAR(32)`
- provider-native UUID types such as MariaDB `UUID`

MySqlConnector's `GuidFormat` is connection-wide. DataLinq metadata is column-specific. Those two scopes do not match.

The current dependency causes several correctness risks:

- one connection string option silently controls all `BINARY(16)` UUID reads and parameters
- mixed schemas cannot represent two different binary UUID formats in the same connection
- generated SQL literals use `Guid.ToByteArray()` while runtime reads may use a different connector format
- LINQ `Contains(...)`, relation predicates, cache primary-key loads, and explicit `SqlQuery` paths can diverge
- imported `BINARY(16)` columns map to `Guid` without preserving the actual byte layout
- `[DefaultNewUUID(UUIDVersion.Version7)]` maps to C# `Guid.CreateVersion7()` but MySQL/MariaDB SQL generation emits `UUID()`, which is not UUIDv7

The desired behavior is simple to state:

> A DataLinq model should declare or infer the UUID storage format for each `Guid` column, and every provider/runtime path should use that same format.

## Design Principles

- **Column metadata owns UUID semantics:** connection strings can influence transport, but they should not define model meaning.
- **Explicit beats magical:** users should be able to see and override UUID storage format in model code.
- **Backward compatibility matters:** existing MySQL `BINARY(16)` data written by DataLinq likely uses `Guid.ToByteArray()` layout. Do not silently reinterpret it.
- **Provider defaults should be sane, not invisible:** default behavior can exist, but generated/imported metadata should make ambiguous binary UUID storage visible.
- **One codec per column:** reads, writes, SQL literals, query parameters, validation, cache keys, and relation predicates must use the same codec.
- **Native UUID is preferred when real:** MariaDB native `UUID` should remain the preferred MariaDB path where supported.
- **Text UUID is allowed:** `CHAR(36)` is bigger and slower than `BINARY(16)`, but it is obvious, portable, and useful for legacy/interoperability schemas.
- **Do not abuse MySQL time-swap:** MySQL `UUID_TO_BIN(x, 1)` is designed around UUIDv1 time parts. It should not be the default for UUIDv7.

## Non-Goals

- Do not add a new UUID type separate from `System.Guid`.
- Do not require users to use UUID primary keys.
- Do not make MySQL `BINARY(16)` the only recommended representation.
- Do not make DataLinq depend on MySqlConnector-specific behavior for core semantics.
- Do not attempt full historical data migration in the first slice.
- Do not make server-generated UUIDv7 appear supported on MySQL/MariaDB unless the provider can genuinely generate UUIDv7.

## Terminology

DataLinq should continue using `Guid` for C# API surface because that is the CLR type. Documentation and metadata should use "UUID storage format" for database semantics because the database concepts are UUID-shaped even when the CLR type is `Guid`.

The API can use `Guid` naming if that reads better in C#:

- `GuidStorageAttribute`
- `GuidStorageFormat`
- `GuidCodec`

The docs should explain that this governs UUID storage.

## Public API Shape

Initial attribute:

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class GuidStorageAttribute : Attribute
{
    public GuidStorageAttribute(GuidStorageFormat format)
        : this(DatabaseType.Default, format)
    {
    }

    public GuidStorageAttribute(DatabaseType databaseType, GuidStorageFormat format)
    {
        DatabaseType = databaseType;
        Format = format;
    }

    public DatabaseType DatabaseType { get; }
    public GuidStorageFormat Format { get; }
}

public enum GuidStorageFormat
{
    ProviderDefault,
    NativeUuid,
    Text36,
    Text32,
    Binary16LittleEndian,
    Binary16Rfc4122,
    Binary16MySqlTimeSwap
}
```

Example for compatibility with current DataLinq MySQL binary behavior:

```csharp
[PrimaryKey]
[Column("id")]
[Type(DatabaseType.MySQL, "binary", 16)]
[GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16LittleEndian)]
public abstract Guid Id { get; }
```

Example for MySQL binary UUID string order:

```csharp
[PrimaryKey]
[Column("id")]
[Type(DatabaseType.MySQL, "binary", 16)]
[GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)]
public abstract Guid Id { get; }
```

Example for MariaDB native UUID plus MySQL binary fallback:

```csharp
[PrimaryKey]
[Column("id")]
[Type(DatabaseType.MariaDB, "uuid")]
[GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.NativeUuid)]
[Type(DatabaseType.MySQL, "binary", 16)]
[GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)]
public abstract Guid Id { get; }
```

Example for simple interoperable text:

```csharp
[Column("external_id")]
[Type(DatabaseType.MySQL, "char", 36)]
[GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Text36)]
public abstract Guid ExternalId { get; }
```

### Attribute Rules

- `GuidStorageAttribute` is valid only on `Guid` and `Guid?` properties.
- Multiple provider-specific attributes are allowed.
- An exact provider match wins over `DatabaseType.Default`.
- `ProviderDefault` means "use DataLinq's provider default," not "ask MySqlConnector."
- `NativeUuid` requires a provider-native UUID type or a provider-specific mapping that behaves as native UUID.
- `Binary16MySqlTimeSwap` should be MySQL/MariaDB-specific unless another provider explicitly supports the same layout.
- `Text32` and `Text36` should validate against compatible declared database types where possible.

## Metadata Shape

`ColumnDefinition` should expose resolved UUID storage metadata rather than requiring every provider path to scan attributes:

```csharp
public sealed class GuidStorageDefinition
{
    public DatabaseType DatabaseType { get; }
    public GuidStorageFormat Format { get; }
    public bool IsExplicit { get; }
}
```

Useful helpers:

```csharp
public bool IsGuidColumn { get; }
public GuidStorageDefinition? GuidStorage { get; }
```

The resolved metadata should answer:

- whether the property is a `Guid`/`Guid?`
- whether the database type is native UUID, text, or binary
- which binary byte order applies
- whether the format was explicitly declared or inferred by provider default
- whether the mapping is compatible with the declared database type

This metadata is needed by:

- provider readers
- provider writers
- query parameter binding
- SQL generation
- literal/default formatting
- schema validation
- model file generation
- source generator metadata snapshots
- cache provider-key lookup paths

## Guid Codec

Introduce a small internal codec layer:

```csharp
internal static class GuidCodec
{
    public static object ToProviderValue(
        Guid value,
        GuidStorageFormat format);

    public static Guid FromProviderValue(
        object value,
        GuidStorageFormat format);

    public static string ToSqlLiteral(
        Guid value,
        GuidStorageFormat format);
}
```

The concrete API may be provider-specific rather than static. The important part is that the conversion logic lives in one place and is called everywhere.

Expected mappings:

| Format | Provider value | Notes |
| --- | --- | --- |
| `NativeUuid` | `Guid` or string, provider-specific | Prefer provider-native handling only when it is proven stable. |
| `Text36` | lowercase dashed string | Matches `Guid.ToString("D")` and MySQL/MariaDB `UUID()` text. |
| `Text32` | lowercase undashed string | Useful for legacy `CHAR(32)`. |
| `Binary16LittleEndian` | `byte[16]` from `Guid.ToByteArray()` | Current DataLinq/MySqlConnector compatibility format. |
| `Binary16Rfc4122` | `byte[16]` matching UUID string order | Matches MySQL `UUID_TO_BIN(x)` without swap. |
| `Binary16MySqlTimeSwap` | `byte[16]` matching `UUID_TO_BIN(x, 1)` | Useful only when the UUID version/layout benefits from MySQL time-part swapping. |

For .NET 8+ and newer, `Guid.ToByteArray(bigEndian: true)` and `new Guid(bytes, bigEndian: true)` may be useful for RFC 4122 order. Multi-targeting may require a local implementation for older target frameworks.

## Provider Behavior

### MySQL

MySQL has no native UUID column type. DataLinq should support:

- `BINARY(16)` with explicit or inferred binary storage format
- `CHAR(36)` / `VARCHAR(36)` as `Text36`
- `CHAR(32)` / `VARCHAR(32)` as `Text32`

Recommended default strategy:

- keep current default `Guid` -> `BINARY(16)` for compatibility
- resolve that default as `Binary16LittleEndian` for existing behavior
- emit a diagnostic or generated attribute when importing/generating so the format is visible
- consider switching new-project templates to explicit `Binary16Rfc4122` or `Text36`, but do not silently change the runtime default in a minor release

### MariaDB

MariaDB should continue preferring native `UUID` where supported.

Recommended default strategy:

- native `UUID` -> `NativeUuid`
- explicit `BINARY(16)` -> require or infer a binary storage format
- `CHAR(36)` / `VARCHAR(36)` -> `Text36`
- `CHAR(32)` / `VARCHAR(32)` -> `Text32`

MariaDB provider version detection already exists for native UUID support. That detection should feed storage-format resolution instead of only type generation.

### SQLite

SQLite currently documents `Guid` as `TEXT` by default and recommends text unless there is a specific reason not to.

Recommended behavior:

- default `Guid` -> `TEXT` / `Text36`
- `BLOB` with `Guid` -> explicit `Binary16LittleEndian` or `Binary16Rfc4122`
- imported `BLOB` named `guid`/`uuid` should probably generate an explicit warning because byte layout cannot be inferred from SQLite type affinity

SQLite query parameter normalization already converts `Guid` to text in `SQLiteProvider.CreateParameter`. That pattern should become column-aware if binary UUID support is formalized.

## Query and Parameter Binding

This is the critical implementation area.

Every query predicate with a `Guid` value must be normalized using the target column's `GuidStorageDefinition` before provider parameter creation.

This includes:

- direct LINQ equality: `x.Id == id`
- nullable comparisons: `x.ParentId == maybeId`
- local `Contains(...)`: `ids.Contains(x.Id)`
- local object-list `Any(...)` expansion
- relation predicates
- explicit `SqlQuery.Where(...)` when column metadata is available
- cache primary-key loads
- update/delete primary-key predicates

The current LINQ path often creates a `ColumnOperandWithDefinition` for direct comparisons, then normalizes only special cases such as `char`. That is the right place to add column-aware provider-value normalization.

The current `Contains(...)` path often does this:

```csharp
var fieldName = builder.GetColumnMaybe(member)?.DbName ?? member.Member.Name;
var whereClause = builder.CurrentParentGroup.AddWhere(fieldName, null, connectionType);
whereClause.In(listToProcess);
```

That loses `ColumnDefinition`. It should instead preserve the column:

```csharp
var column = builder.GetColumn(member);
var whereClause = builder.CurrentParentGroup.AddWhere(column, null, connectionType);
whereClause.In(NormalizeValuesForColumn(column, listToProcess));
```

The exact public `WhereGroup` API can remain string-based for user ergonomics, but internal LINQ translation should use column-aware operands wherever it knows the column.

Provider-level `CreateParameter` should not be the only normalization point because it does not know which column the parameter targets. It can remain a final safety net for provider-wide defaults, but column-aware normalization must happen earlier.

## Reader Behavior

Provider readers should decode `Guid` based on column metadata.

For MySQL/MariaDB:

- `uuid`, `char(36)`, `varchar(36)` -> parse text or provider-native `Guid`
- `char(32)`, `varchar(32)` -> parse undashed text
- `binary(16)` -> read bytes and decode with the resolved `GuidStorageFormat`

Do not call `MySqlDataReader.GetGuid(...)` for DataLinq model `Guid` columns unless the provider-native path is explicitly chosen and proven stable.

For SQLite:

- `TEXT` -> parse text
- `BLOB` -> read bytes and decode with the resolved `GuidStorageFormat`

The `IDataLinqDataReader.GetGuid(int ordinal)` method has no column metadata. It can remain a low-level convenience, but `GetValue<T>(ColumnDefinition column, int ordinal)` should be the canonical model-reading path for `Guid` columns.

## Writer Behavior

Provider writers should encode `Guid` based on column metadata.

The current MySQL writer already does the right kind of thing structurally:

- string-like UUID -> string
- `binary(16)` -> bytes

The change is to stop hard-coding `Guid.ToByteArray()` and use the resolved codec.

Mutation paths that already call `writer.ConvertColumnValue(column, value)` should then keep working:

- insert values
- update values
- delete/update primary-key predicates
- cache primary-key lookup values

The bigger task is ensuring LINQ and explicit query paths get the same conversion.

## SQL Generation and Defaults

Static `Guid` defaults must use the same codec as runtime writes.

Today, MySQL binary defaults are emitted as:

```sql
X'...'
```

using `Guid.ToByteArray()`. That should become:

```text
Guid -> GuidCodec.ToSqlLiteral(value, resolvedFormat)
```

Examples:

```sql
-- Text36
DEFAULT '00112233-4455-6677-8899-aabbccddeeff'

-- Binary16Rfc4122
DEFAULT X'00112233445566778899AABBCCDDEEFF'

-- Binary16LittleEndian
DEFAULT X'33221100554477668899AABBCCDDEEFF'
```

### Dynamic UUID Defaults

`DefaultNewUUIDAttribute` needs a separate provider semantics pass.

Current metadata says:

- `DefaultNewUUIDAttribute` defaults to `UUIDVersion.Version7`
- generated C# default value for version 7 is `Guid.CreateVersion7()`
- MySQL/MariaDB SQL generation emits `UUID()`

That is inconsistent. MySQL documents `UUID()` as UUID version 1, not UUIDv7. DataLinq should not claim v7 semantics while generating v1 SQL.

Recommended behavior:

- C# client-side default:
  - `Version4` -> `Guid.NewGuid()`
  - `Version7` -> `Guid.CreateVersion7()`
- database-side MySQL/MariaDB default:
  - `UUID()` is allowed only when the requested version is compatible with what the provider actually generates, or when the attribute/API explicitly asks for provider default UUID generation
  - `DefaultNewUUID(UUIDVersion.Version7)` should warn or fail for MySQL/MariaDB database-side SQL generation unless a real v7 expression is configured
- binary MySQL defaults should use `UUID_TO_BIN(UUID())` only when the storage format is RFC 4122 or time-swap and the user has accepted provider UUID generation semantics

This may require splitting the API:

```csharp
[DefaultNewUUID(UUIDVersion.Version7)]          // model/client semantic default
[DefaultSql(DatabaseType.MySQL, "UUID()")]     // explicit provider SQL
```

or adding an option:

```csharp
[DefaultNewUUID(UUIDVersion.Version7, Generation = UUIDGeneration.Client)]
```

The first slice can simply document and diagnose the mismatch. It should not pretend MySQL has native UUIDv7 generation if it does not.

## Metadata Import and Model Generation

When importing a live schema:

- MariaDB `UUID` should generate `[GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.NativeUuid)]` or resolve equivalent metadata.
- MySQL/MariaDB `CHAR(36)` named as UUID should generate `Text36`.
- MySQL/MariaDB `CHAR(32)` named as UUID should generate `Text32`.
- MySQL/MariaDB `BINARY(16)` mapped to `Guid` should generate an explicit storage format only when DataLinq can infer it from configuration or user option.
- SQLite `TEXT` UUID columns should generate `Text36` when a text UUID convention was used.
- SQLite `BLOB` UUID columns should warn unless the import command was given a storage-format hint.

The CLI should probably grow an option for ambiguous imports:

```bash
datalinq generate --guid-binary-format Binary16Rfc4122
```

or provider-scoped config:

```json
{
  "providers": {
    "mysql": {
      "guidBinaryFormat": "Binary16Rfc4122"
    }
  }
}
```

Default import behavior should be conservative:

- existing compatibility mode can infer `Binary16LittleEndian`
- a future template or major-version mode can default to `Binary16Rfc4122`
- warnings should explain that `BINARY(16)` does not self-describe UUID byte order

## Schema Validation and Diff

Schema validation should compare UUID storage format in addition to database type.

Important cases:

- model says `Binary16LittleEndian`, database column is `BINARY(16)`: type matches, format is not directly visible; validation can only trust explicit model metadata or configured database hints
- model says `Text36`, database column is `CHAR(36)`: type and format match
- model says `NativeUuid`, database column is MariaDB `UUID`: match
- model says `Binary16Rfc4122`, database column is `BINARY(16)` but imported metadata lacks a storage hint: validation should report an unresolved/unknown format issue, not a false match

Diff generation cannot safely rewrite UUID byte order without a data migration plan. A type-only diff from `BINARY(16)` to `BINARY(16)` with a format change should be reported as a semantic migration requiring manual data conversion.

## Backward Compatibility

This is the sharpest edge.

Existing DataLinq MySQL `BINARY(16)` data is likely stored using `Guid.ToByteArray()` because that is what the current writer and static default generation do. Changing the default binary format to RFC 4122 would make existing rows unreadable by key unless the model opts into compatibility.

Recommended compatibility stance:

1. For existing runtime default behavior, resolve MySQL `Guid` -> `BINARY(16)` as `Binary16LittleEndian`.
2. Add explicit metadata so generated code and imported models show that choice.
3. Add diagnostics that explain this is the compatibility format, not the only good UUID format.
4. Introduce an opt-in project/provider setting for new projects to use `Binary16Rfc4122`.
5. Consider changing new-template defaults before changing core runtime defaults.

A future major version can revisit the default, but only with a clear migration story.

## Implementation Slices

### Slice 1: Metadata and Codec Foundation

- Add `GuidStorageFormat`.
- Add `GuidStorageAttribute`.
- Add resolved `GuidStorageDefinition` metadata.
- Add validation for invalid attribute usage.
- Add codec tests for all supported formats.
- Keep runtime behavior unchanged except for explicit codec helpers.

### Slice 2: MySQL/MariaDB Runtime Conversion

- Update MySQL/MariaDB writer to use the codec.
- Update MySQL/MariaDB reader to decode via column metadata.
- Stop calling `MySqlDataReader.GetGuid(...)` for model `Guid` columns where storage format is known.
- Add tests for `BINARY(16)` roundtrip without `GuidFormat`.
- Add tests for native MariaDB `UUID` behavior without `GuidFormat`.

### Slice 3: Query Parameter Normalization

- Preserve `ColumnDefinition` in LINQ `Contains(...)` translation.
- Normalize `Guid` values in direct comparisons and `IN` lists using column metadata.
- Normalize relation predicate values.
- Verify explicit `SqlQuery.Where(columns, key)` and cache primary-key paths keep using writer conversion.
- Add tests for `Contains`, equality, nullable equality, relation predicates, and cache reload by `Guid` key without MySqlConnector `GuidFormat`.

### Slice 4: SQL Generation and Defaults

- Format static `Guid` defaults through the codec.
- Add provider diagnostics for unsupported `DefaultNewUUID` version semantics.
- Decide whether MySQL/MariaDB `DefaultNewUUID` should emit provider UUID generation only through explicit provider SQL or a new generation mode.
- Update SQL generation tests for text, native, little-endian binary, RFC 4122 binary, and time-swap binary.

### Slice 5: Import, Validation, and Docs

- Generate explicit `GuidStorage` metadata for imported UUID columns where possible.
- Add CLI/config hints for ambiguous binary UUID imports.
- Update schema validation to include UUID format compatibility.
- Update MySQL/MariaDB and SQLite docs.
- Add migration notes for users moving away from connection-string-dependent behavior.

## Test Plan

Unit tests:

- codec roundtrips known UUID values for each format
- known byte sequences produce expected `Guid` values
- static SQL literals match each format
- invalid `[GuidStorage]` usage reports model diagnostics
- provider default resolution returns expected format per provider/type

MySQL/MariaDB server tests:

- MySQL `BINARY(16)` insert/read without `GuidFormat`
- MySQL `BINARY(16)` equality predicate without `GuidFormat`
- MySQL `BINARY(16)` `Contains(...)` without `GuidFormat`
- MariaDB native `UUID` insert/read/query without `GuidFormat`
- mixed native/text/binary UUID columns in one database
- static default UUID literal roundtrip for binary and text
- relation loading through `Guid` foreign keys without `GuidFormat`

SQLite tests:

- `TEXT` Guid predicate matches raw text UUID
- `BLOB` Guid predicate works with explicit binary format
- ambiguous `BLOB` UUID import warns or requires a format hint

Regression tests:

- existing `Binary16LittleEndian` data remains readable
- `Binary16Rfc4122` and `Binary16LittleEndian` do not accidentally match the same byte sequence except for coincidental symmetric values
- `DefaultNewUUID(UUIDVersion.Version7)` does not silently emit MySQL `UUID()` as if it were v7

## Open Questions

- Should the public attribute be named `GuidStorageAttribute`, `UuidStorageAttribute`, or `UuidFormatAttribute`?
- Should `ProviderDefault` be allowed in model source, or should it be internal-only after resolution?
- Should DataLinq expose a project-level default UUID storage policy for new models?
- Should model generation always emit explicit `GuidStorage` attributes, or only for ambiguous/non-default mappings?
- Should MySQL `BINARY(16)` imported without hints default to compatibility mode or require user confirmation?
- Should database-side UUID generation be split from C# default generation in the public API?
- Should `GuidStorageFormat.Binary16MySqlTimeSwap` be hidden behind a provider-specific enum to avoid implying portability?

## References

- MySqlConnector `GuidFormat` connection option: <https://mysqlconnector.net/connection-options/>
- MySqlConnector `MySqlGuidFormat` enum: <https://mysqlconnector.net/api/mysqlconnector/mysqlguidformattype/>
- MySQL `UUID()`, `UUID_TO_BIN(...)`, and `BIN_TO_UUID(...)`: <https://dev.mysql.com/doc/refman/en/miscellaneous-functions.html>
- .NET `Guid.ToByteArray()` byte-order remarks: <https://learn.microsoft.com/en-us/dotnet/api/system.guid.tobytearray>
