> [!WARNING]
> This document is roadmap/specification material. It describes planned behavior, not shipped DataLinq behavior.

# Specification: UUID Storage Format Support

**Status:** Implementation in progress. Bounded `UUID-1A` declaration/codec primitives are implemented; resolved per-provider column metadata and every provider/runtime integration remain open.

**Release scope:** Required 0.9 runtime correctness slice; broader UUID policy work remains later.

**Target release:** DataLinq 0.9 for the bounded runtime slice defined below.

**Depends on:** The scalar value contract starts with `SC-1`; the complete UUID slice consumes `SC-2` through `SC-5` at the per-workstream boundaries defined below. See [Scalar Converters And Typed IDs Implementation Plan](../roadmap-implementation/v0.9/Scalar%20Converters%20and%20Typed%20IDs%20Implementation%20Plan.md).

**Last reviewed:** 2026-07-12.

**Goal:** Make DataLinq responsible for UUID/`Guid` storage semantics so reads, writes, queries, defaults, validation, and generated metadata do not depend on MySqlConnector connection-string behavior.

**Current implementation boundary:** `GuidStorageFormat` freezes the five 0.9 formats, `GuidStorageAttribute` declarations receive intrinsic provider/format/duplicate validation, and raw declarations survive source parsing, CLI metadata merge/model regeneration, and source-generated runtime metadata. The internal `GuidCodec` deterministically converts canonical `Guid` values to native-text, exact text, legacy .NET mixed-endian bytes, or RFC-order bytes and back. No provider currently resolves or consumes these declarations; `UUID-1` is not complete.

## 0.9 Decision And Ownership

UUID storage correctness belongs in 0.9. Codec metadata begins after `SC-1`; provider, query/key, and validation slices follow their granular scalar prerequisites. This document owns that work; the scalar plan owns only the general model-to-canonical-provider conversion boundary.

The distinction is deliberate:

```text
typed/model value <-> canonical Guid <-> column-specific physical UUID value
```

Scalar converters own the first arrow. The UUID codec in this plan owns the second. The read-only memory backend stores canonical `Guid` values and does not imitate SQL binary/text layouts.

The 0.9 public decisions are frozen for implementation:

- use the CLR-facing names `GuidStorageAttribute` and `GuidStorageFormat`
- absence of an attribute means “resolve DataLinq's deterministic provider default”
- do not expose `ProviderDefault` as a public enum value; resolved metadata may record internally whether a default or explicit format produced the result
- string-only legacy `SqlQuery` predicates cannot infer ambiguous binary UUID layout and are not promised automatic UUID normalization; internal paths must preserve `ColumnDefinition`, while ambiguous public string-only binding fails or requires an explicit column-aware route

The 0.9 slice is bounded to closing the current correctness hole across existing runtime paths:

- resolved per-column UUID storage metadata
- MySQL/MariaDB reads and writes for native UUID, text UUID, compatibility little-endian `BINARY(16)`, and explicit RFC-order `BINARY(16)`
- SQLite text UUID and explicitly configured binary UUID reads and writes
- column-aware equality and nullable query parameters
- local `Contains(...)` and already-supported local equality-membership shapes
- primary/composite key components, cache lookup, and relation predicates
- insert, update, delete, and generated/default value paths
- static default literals and a diagnostic for incompatible database-generated UUID-version claims
- schema validation that checks both physical type and declared/resolved UUID format
- provider evidence that relevant paths no longer depend on MySqlConnector `GuidFormat`

Explicitly outside the 0.9 slice:

- automatic data migration between UUID byte layouts
- changing the compatibility default for existing MySQL `BINARY(16)` models
- MySQL time-swap format support
- a project-wide UUID policy or new-template default change
- live-schema import CLI switches and ambiguous-import policy beyond a clear diagnostic
- a broad redesign of client-side versus server-side UUID generation
- JSON persistence encoding

## Current State

DataLinq currently has useful but incomplete UUID support.

The current MySQL/MariaDB documentation is honest about the weak spot:

- MySQL maps `Guid` to `BINARY(16)` by default.
- MariaDB maps `Guid` to native `UUID` by default where supported.
- Historically, `BINARY(16)` query values depended on MySqlConnector `GuidFormat=LittleEndianBinary16`; the 0.9 query-plan equality/membership path now hands canonical `Guid` values to the column writer instead. Other UUID paths still need the explicit metadata/codec work below.
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
- `src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs`
- `src/DataLinq/Linq/Planning/Sql/QueryPlanSqlValueRenderer.cs`
- `src/DataLinq/Linq/Planning/Sql/QueryPlanSqlPredicateBuilder.cs`
- `src/DataLinq.SharedCore/Attributes/DefaultAttribute.cs`
- `src/DataLinq.SharedCore/Metadata/PropertyDefinition.cs`
- `src/DataLinq.Tests.MySql/MariaDbGuidTypeMappingTests.cs`

The implementation currently splits responsibility in a way that is too fragile:

- MySQL mutations convert `Guid` to `byte[]` for `BINARY(16)` using `Guid.ToByteArray()`.
- MySQL reads call `MySqlDataReader.GetGuid(...)`, so MySqlConnector decides how to decode bytes.
- Legacy/string-only MySQL query parameters remain raw or caller-encoded because they do not carry column metadata.
- The expression-query direct comparison and local-membership path now retains `ColumnDefinition`, keeps canonical values for cache identity, and lazily binds column-writer physical values.
- SQLite query parameters normalize `Guid` to text at provider command creation, which is a healthier pattern than the MySQL path.

The original characterization tests encoded the practical failure:

- native MariaDB `UUID` `Contains(...)` works without `GuidFormat`
- `BINARY(16)` `Contains(...)` required `GuidFormat=LittleEndianBinary16`
- the same query returned no rows when `GuidFormat` was removed

The 2026-07-11 SC-4 query-operand slice flips that binary-membership test into a positive regression: configured and unconfigured connections now return the same rows because DataLinq uses the mapped column writer. This is one repaired query path, not completion of the UUID workstream. The original failure was not a MySqlConnector bug; it was a DataLinq ownership bug. MySqlConnector exposes several legitimate `GuidFormat` values, while DataLinq storage meaning must remain column-specific.

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
    NativeUuid,
    Text36,
    Text32,
    Binary16LittleEndian,
    Binary16Rfc4122
}
```

`Binary16MySqlTimeSwap` is a possible post-0.9 extension. It should not enlarge the first correctness slice merely because MySQL exposes it.

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

- `GuidStorageAttribute` is valid only when the resolved canonical provider CLR type is `Guid`/`Guid?`; this includes typed IDs whose scalar converter produces canonical `Guid`. That eligibility check belongs to `UUID-1B`, after scalar mapping is resolved.
- Multiple provider-specific attributes are allowed, with at most one declaration per `DatabaseType`.
- An exact provider match wins over `DatabaseType.Default`.
- no attribute means use DataLinq's deterministic provider default, not MySqlConnector's connection-wide behavior
- `NativeUuid` requires a provider-native UUID type or a provider-specific mapping that behaves as native UUID.
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
public IReadOnlyList<GuidStorageDefinition> GuidStorageDefinitions { get; }
public GuidStorageDefinition? GetGuidStorageFor(DatabaseType databaseType);
```

Resolved definitions must be provider-keyed. Runtime metadata is cached by model CLR type, and one model can legitimately declare MariaDB native UUID plus MySQL binary UUID storage; a singular mutable `ColumnDefinition.GuidStorage` would be incorrect.

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
    internal static object ToPhysicalValue(
        Guid canonicalValue,
        GuidStorageFormat format);

    internal static Guid FromPhysicalValue(
        object physicalValue,
        GuidStorageFormat format);
}
```

The codec deliberately says *physical*, not *provider*, because scalar conversion already defines the canonical provider value as `Guid`. SQL literal quoting and binary-literal syntax remain provider-owned rather than entering the generic codec.

Expected mappings:

| Format | Provider value | Notes |
| --- | --- | --- |
| `NativeUuid` | lowercase dashed string on write; exact dashed string or `Guid` on read | Preserves current MariaDB text binding and avoids connector-wide `GuidFormat` reinterpretation. |
| `Text36` | lowercase dashed string | Matches `Guid.ToString("D")` and MySQL/MariaDB `UUID()` text. |
| `Text32` | lowercase undashed string | Useful for legacy `CHAR(32)`. |
| `Binary16LittleEndian` | `byte[16]` from `Guid.ToByteArray()` | Current compatibility format; this is .NET's legacy mixed-endian layout, not a uniformly little-endian 128-bit integer. |
| `Binary16Rfc4122` | `byte[16]` matching UUID string order | Matches MySQL `UUID_TO_BIN(x)` without swap. |

The runtime targets net8.0, net9.0, and net10.0, so `Guid.ToByteArray(bigEndian: true)` and `new Guid(bytes, bigEndian: true)` provide one implementation across every supported target. The codec source is linked into the SQL provider assemblies while remaining internal.

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

The current plan path represents mapped members as `QueryPlanColumnValue` nodes and captured/local values as plan bindings. `QueryPlanSqlValueRenderer` and `QueryPlanSqlPredicateBuilder` already retain `ColumnDefinition` for direct comparisons and membership rendering. UUID normalization belongs at that column-aware boundary, after model-to-canonical conversion and before provider parameter creation.

For local `Contains(...)` and supported equality-membership `Any(...)`, the plan must retain the target column for the complete sequence. Each canonical `Guid` value should be encoded with that column's resolved UUID format; no list path may degrade into an untyped raw `Guid` parameter collection.

The legacy public `SqlQuery`/`WhereGroup` surface can remain string-based where callers provide only names, but internal DataLinq paths must use column-aware operands whenever metadata is available. A string-only explicit query cannot safely infer an ambiguous binary UUID layout and must require an explicit typed/column-aware route or fail clearly; it is not part of the automatic UUID-normalization claim.

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
Guid -> GuidCodec.ToPhysicalValue(value, resolvedFormat) -> provider-owned SQL literal formatting
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
- binary MySQL provider expressions may use `UUID_TO_BIN(UUID())` only for RFC-order storage and only when the user has explicitly accepted the provider's UUID-generation semantics

This may require splitting the API:

```csharp
[DefaultNewUUID(UUIDVersion.Version7)]          // model/client semantic default
[DefaultSql(DatabaseType.MySQL, "UUID()")]     // explicit provider SQL
```

or adding an option:

```csharp
[DefaultNewUUID(UUIDVersion.Version7, Generation = UUIDGeneration.Client)]
```

The 0.9 slice should document and diagnose the mismatch. A broader generation API redesign is later work; 0.9 must not pretend MySQL has native UUIDv7 generation if it does not.

## Post-0.9: Schema Import And Policy Tooling

The following is useful follow-up work, but it is not required to fix the 0.9 runtime paths. Resolved source-generated and runtime column metadata needed by providers remain part of `UUID-1B`; this section concerns importing ambiguous external schemas and choosing wider project policy.

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

## 0.9 Workstreams

These IDs are local to this plan and deliberately do not reuse roadmap-wide phase numbers.

| UUID workstream | Scalar/foundation prerequisites |
| --- | --- |
| `UUID-1A` declaration vocabulary, preservation, and codec primitives | `SC-1` |
| `UUID-1B` resolved provider-keyed metadata and compatibility defaults | `UUID-1A`; coordinate physical compatibility with `SC-5` |
| `UUID-2` provider reads/writes | `UUID-1B`, `SC-2` |
| `UUID-3` queries, keys, relations | `UUID-1B`, `UUID-2`, `SC-3`, `SC-4` |
| `UUID-4` defaults and validation | `UUID-1B`, `UUID-2`, `SC-5` |
| `UUID-5` evidence and docs | `UUID-1A` through `UUID-4` |

Known-value byte/string vectors and current-behavior characterization were recorded before `UUID-1A`; the implemented codec tests now consume those frozen vectors without changing provider behavior or defaults.

### UUID-1A: Declaration And Physical Codec Primitives

Implemented on 2026-07-12:

- freeze the bounded 0.9 `GuidStorageFormat` values without `ProviderDefault` or MySQL time-swap
- add `GuidStorageAttribute` with default and provider-scoped declarations
- reject undefined provider/format values and duplicate declarations for one provider
- preserve declarations through syntax parsing, metadata transformation, model-file generation, source-generated metadata, and equivalence digests
- implement strict known-value codecs for native text, exact text, legacy .NET mixed-endian binary, and RFC-order binary formats with owned arrays

Bounded exit signal is green: known strings and bytes round-trip deterministically, canonical `Guid` remains distinct from physical values, and declaration metadata is lossless. This is not provider UUID support.

### UUID-1B: Resolved Provider-Keyed Metadata

- add provider-keyed immutable `GuidStorageDefinition` metadata and lookup
- validate eligibility against resolved canonical provider type so typed IDs over `Guid` remain supported
- resolve exact-provider-over-default declarations and deterministic no-attribute compatibility defaults without consulting MySqlConnector `GuidFormat`
- validate native/text/binary physical type compatibility and diagnose ambiguous `BINARY(16)`/`BLOB` mappings
- carry the resolved format, not merely raw attributes, through runtime and source-generated metadata

Exit signal:

- every mapped canonical-`Guid` column has one resolved physical format per applicable provider or an actionable ambiguity diagnostic
- one model can resolve distinct MySQL, MariaDB, and SQLite formats without mutable global provider state

### UUID-2: Provider Reads, Writes, And Mutation Values

- update MySQL/MariaDB readers and writers to use column metadata and the codec
- stop relying on `MySqlDataReader.GetGuid(...)` for known binary layouts
- cover MariaDB native UUID plus MySQL/MariaDB text and binary columns
- make SQLite text behavior explicit and support binary UUID only with an explicit format
- route insert, update, delete-key, and generated/default hydration paths through the same conversion boundary

Exit signal:

- relevant provider round-trips work without a MySqlConnector `GuidFormat` connection option
- existing MySQL little-endian `BINARY(16)` data remains compatible
- physical values returned by database defaults materialize as canonical `Guid` and then as any converted model value

### UUID-3: Queries, Membership, Keys, And Relations

- preserve `ColumnDefinition` through direct equality, nullable equality, and LINQ membership translation
- normalize every local `Contains(...)` element and already-supported equality-membership `Any(...)` element with the target column codec
- normalize explicit `SqlQuery.Where(...)` values whenever column metadata is available
- cover primary-key and composite-key components, cache reloads, relation predicates, and update/delete key predicates
- reject ambiguous binary UUID binding before execution instead of passing a raw `Guid` to the connector

Exit signal:

- equality, local membership, keys, cache lookup, and relations all bind the same physical bytes/text as writes
- a connection-wide `GuidFormat` setting is no longer required for the supported paths
- mixed UUID formats in one connection remain column-correct

### UUID-4: Defaults And Validation

- format static `Guid` SQL defaults through the resolved codec
- diagnose `DefaultNewUUID(UUIDVersion.Version7)` when provider SQL would silently use a different UUID version
- validate native, text, and binary storage compatibility against declared database types
- report an unresolved format for ambiguous `BINARY(16)`/`BLOB` metadata rather than claiming a false match
- report byte-layout changes as semantic/manual data migrations even when the SQL type remains `BINARY(16)`

Exit signal:

- runtime writes and static defaults produce identical physical layouts
- schema validation distinguishes canonical `Guid` compatibility from physical UUID-format compatibility
- 0.9 does not claim database-generated UUIDv7 semantics that the provider does not supply

### UUID-5: Provider Evidence And Documentation

- add focused unit, SQL-shape, and active-provider behavior tests
- test MySQL, MariaDB, and SQLite representations included in the bounded slice
- run tests without MySqlConnector `GuidFormat`, plus an explicit regression proving connector configuration cannot silently redefine column meaning
- update provider docs and compatibility notes
- document that automatic byte-layout migration, import policy, and time-swap support remain later work

Exit signal:

- every in-scope path has provider evidence
- documentation describes only the formats and behaviors actually shipped in 0.9

## Work After 0.9

The following items remain useful, but should not delay the correctness slice:

- CLI/config hints for ambiguous live-schema imports
- richer generation policy for when explicit `GuidStorage` attributes are emitted
- `Binary16MySqlTimeSwap`, if real user demand justifies the provider-specific surface
- project-wide/new-template UUID defaults
- automated or assisted data migration between binary layouts
- a broader client-versus-server UUID-generation API

## Test Plan

Unit tests:

- codec roundtrips known UUID values for each format
- known byte sequences produce expected `Guid` values
- static SQL literals match each format
- invalid `[GuidStorage]` usage reports model diagnostics
- provider default resolution returns expected format per provider/type
- canonical `Guid` values remain separate from encoded text/byte values
- ambiguous physical formats fail before a raw connector parameter is created

MySQL/MariaDB server tests:

- MySQL `BINARY(16)` insert/read without `GuidFormat`
- MySQL `BINARY(16)` equality predicate without `GuidFormat`
- MySQL `BINARY(16)` `Contains(...)` without `GuidFormat`
- nullable equality and local equality-membership without `GuidFormat`
- MariaDB native `UUID` insert/read/query without `GuidFormat`
- mixed native/text/binary UUID columns in one database
- static default UUID literal roundtrip for binary and text
- relation loading through `Guid` foreign keys without `GuidFormat`
- primary and composite key lookup, update, and delete paths without `GuidFormat`

SQLite tests:

- `TEXT` Guid predicate matches raw text UUID
- `BLOB` Guid predicate works with explicit binary format
- ambiguous `BLOB` runtime metadata fails with an actionable format diagnostic

Regression tests:

- existing `Binary16LittleEndian` data remains readable
- `Binary16Rfc4122` and `Binary16LittleEndian` do not accidentally match the same byte sequence except for coincidental symmetric values
- `DefaultNewUUID(UUIDVersion.Version7)` does not silently emit MySQL `UUID()` as if it were v7
- changing only UUID byte layout is reported as a semantic migration even when the SQL type is unchanged

## Open Questions

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
