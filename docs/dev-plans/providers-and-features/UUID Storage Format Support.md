> [!WARNING]
> This document is roadmap/specification material. It describes planned behavior, not shipped DataLinq behavior.

# Specification: UUID Storage Format Support

**Status:** Implementation in progress. Bounded `UUID-1A` declaration/codec primitives and `UUID-1B` immutable provider-keyed resolution are implemented. Bounded `UUID-2` checkpoints consume resolved formats for non-primary-key full-row canonical `Guid` reads and insert/update writes on SQLite, MySQL, and MariaDB, including direct model `Guid` values and a representative converter-backed typed-ID composition slice. SQLite covers Text36, Text32, little-endian BLOB, and RFC-order BLOB; MySQL/MariaDB cover native MariaDB UUID, Text36, Text32, little-endian `BINARY(16)`, and RFC-order `BINARY(16)`, with nullable Text36/SQL NULL evidence on every provider family. Bounded `UUID-3A` proves exact column-aware parameters for the initial non-key equality/membership matrix, and `UUID-3B` makes captured scalar nullness and null-containing nullable local sequences explicit invocation specializations with C#-correct SQL null semantics. Bounded `UUID-3C` now extends the exact provider codecs to a scalar UUID primary key: exactly one canonical `Guid` component with resolved SQLite, MySQL, or MariaDB `GuidStorage`, represented either directly or by a representative Guid-backed typed ID. That slice covers column-aware key selection, neutral single and batched cold loading, warm cache identity, transaction authoritative reload, and insert/update/delete key encoding. Bounded `UUID-4` validates UUID format only after exact physical SQL type equality, reports unobservable binary or SQLite text layouts as unresolved, treats trusted same-type representation changes as manual migrations, and formats fixed `Guid` values through the exact resolved codec before DDL literal rendering. Its fail-closed generation checkpoint preserves `DefaultNewUUID` Version4/Version7 through direct source parsing, direct metadata-to-model regeneration, semantic comparison, snapshots, and equivalence digests; rejects database DDL generation for both versions on SQLite, MySQL, and MariaDB; and preserves imported exact MySQL/MariaDB `UUID()` expressions as provider-scoped `DefaultSql` rather than inventing v4/v7 semantics. A further checkpoint adds exact-D `[DefaultGuid("...")]` as a storage-neutral source carrier that normalizes to the existing ordinary fixed model-`Guid` meaning, initializes generated mutable models through `global::System.Guid.Parse(...)`, regenerates canonical lowercase, and compares identically to an expression-free base `DefaultAttribute(Guid)`. Custom Guid-valued default subclasses remain distinct; Guid defaults carrying a `CodeExpression` fail closed during regeneration and remain distinct in roundtrip comparison, while schema, snapshot, and digest fingerprints describe the fixed value. The latest checkpoint completes direct generated-client Version4/Version7 initialization across net8/net9/net10 and makes the parameterless mutable constructor the single evaluator for all generated client defaults. Source/database metadata-merge precedence is not part of these preservation or constructor-ownership claims. Composite UUID keys, relation/index/foreign-key paths, general projections, manual string-only `SqlQuery`, joined typed-UUID key hydration, static provider-default import, converter-backed defaults, automatic server UUID generation, broader schema/canonical-source work, provider-less readers, memory, and Native AOT/browser publication remain open; aggregate `UUID-2`, `UUID-3`, and `UUID-4` are not complete.

**Release scope:** Required 0.9 runtime correctness slice; broader UUID policy work remains later.

**Target release:** DataLinq 0.9 for the bounded runtime slice defined below.

**Depends on:** The scalar value contract starts with `SC-1`; the complete UUID slice consumes `SC-2` through `SC-5` at the per-workstream boundaries defined below. See [Scalar Converters And Typed IDs Implementation Plan](../roadmap-implementation/v0.9/Scalar%20Converters%20and%20Typed%20IDs%20Implementation%20Plan.md).

**Last reviewed:** 2026-07-14.

**Goal:** Make DataLinq responsible for UUID/`Guid` storage semantics so reads, writes, queries, defaults, validation, and generated metadata do not depend on MySqlConnector connection-string behavior.

**Current implementation boundary:** `GuidStorageFormat` freezes the five 0.9 formats, `GuidStorageAttribute` declarations receive intrinsic validation immediately and canonical-type validation after semantic scalar resolution, and raw declarations survive source parsing, CLI metadata merge/model regeneration, and source-generated runtime metadata. Syntax-only metadata deliberately defers scalar-dependent UUID resolution. The internal `GuidCodec` deterministically converts canonical `Guid` values to native-text, exact text, legacy .NET mixed-endian bytes, or RFC-order bytes and back. `ColumnDefinition` owns immutable definitions resolved per applicable built-in provider after scalar conversion, including typed IDs over canonical `Guid`; generated metadata carries those definitions and runtime construction recomputes them to reject inconsistent non-empty carried definitions. SQLite's column-aware reader and mutation writer consume exact resolved definitions for mapped full-row canonical `Guid` values across Text36, Text32, little-endian BLOB, and RFC-order BLOB storage. Provider-owned MySQL/MariaDB access and transaction readers retain exact provider identity, bypass connector UUID interpretation for both binary formats, accept connector-canonical or raw text/native values, and pair that decoding with exact resolved mutation encoding. Full-row decoding, including a scalar UUID primary-key column, opts canonical `Guid` values into this column-aware path. Scalar primary-key selection does the same before `DataLinqKey` construction; general scalar/projection decoding remains metadata-free. Provider-less public constructors retain legacy connector-driven fallback behavior. The shared writers encode mapped canonical `Guid` values, including direct and converter-backed scalar primary keys, through resolved metadata wherever those writers are used. Exact-D `[DefaultGuid("...")]` is now the legal, storage-neutral source carrier for a fixed model `Guid`; parsing normalizes it to base `DefaultAttribute(Guid)` with no code expression, runtime metadata contains the real `Guid`, mutable initialization uses `global::System.Guid.Parse(...)`, and model generation emits canonical lowercase. `[Default("guid-text")]` remains a string and is invalid for `Guid`. The carrier is not a new semantic default category: comparison, migration snapshots, roundtrip checks, and equivalence digests normalize it with an expression-free exact base fixed-`Guid` default. Custom Guid-valued default subclasses remain distinct. A Guid default carrying a `CodeExpression` remains distinct in roundtrip comparison and fails closed during regeneration; schema comparison, snapshots, and digests intentionally describe the fixed default value instead. SQLite, MySQL, and MariaDB then render that canonical value through the same resolved codec used by runtime writes across the covered 13 direct formats; unresolved layouts and converter-backed model defaults reject before SQL and invoke no converter. `DefaultNewUUID` keeps Version4/Version7 distinct through source parsing, explicit model regeneration, schema default fingerprints, migration snapshots, metadata roundtrip comparison, and equivalence digests. Direct generated mutable initialization now evaluates Version4 or RFC 9562 Version7 exactly once per new instance on net8/net9/net10; immutable-copy construction evaluates neither. The Version7 helper uses UTC Unix milliseconds plus random remaining bits and does not claim same-millisecond monotonicity. SQLite, MySQL, and MariaDB DDL generation still rejects both versions with provider/column/version diagnostics instead of substituting an unverified function. MySQL/MariaDB schema import preserves an exact `UUID()` expression as provider-scoped `DefaultSql`; explicit provider-scoped `DefaultSql` still passes through when its real generation and storage semantics are intentional. The neutral source-row path accepts a UUID key only when the table has exactly one canonical `Guid` primary-key component, the source reports SQLite, MySQL, or MariaDB identity, and that provider has an exact resolved `GuidStorage` definition. The existing `SC-4` canonical-column operand route proves that expression-query direct equality, nullable equality against literal or captured null/non-null values, and supported local `Contains(...)`/equality-`Any(...)` shapes, including null-containing nullable sequences, reach that exact writer boundary without losing column metadata. Captured scalar null comparisons render literal `IS NULL`/`IS NOT NULL` with no parameter; nullable local sequences partition null from non-null membership, and only non-null members reach the target column codec. Representative typed-ID evidence covers SQLite RFC-order BLOB, MySQL little-endian binary, MariaDB RFC-order binary, and nullable Text36/SQL NULL. `SchemaComparer` now gates UUID-format comparison on exact active-provider physical type equality. Resolved metadata and raw exact/default declarations can match schema-observable MySQL/MariaDB text lengths or native MariaDB `UUID`; unhinted binary layouts and SQLite `TEXT`/`BLOB` remain unresolved, while trusted same-type changes require manual migration. Bare deferred Guid syntax is unresolved because assembly registrations are not visible, and property converter markers are not assumed canonical Guid. Manual string-only `SqlQuery` binding, composite UUID keys, relation/index/foreign-key routing, general projections, joined typed-UUID key hydration, static provider-default import, converter-backed defaults, automatic provider/server UUID generation, transformer source precedence, canonical/source-only schema completion, SQLite expression/BLOB import, provider-less MySQL/MariaDB readers, memory, Native AOT/browser publication, and other reader/query routes remain open, so aggregate `UUID-2`, `UUID-3`, and `UUID-4` support is not complete.

The `DefaultNewUUID` version-preservation boundary above is specifically the direct parser/regenerator and semantic-fingerprint surface, not the `MetadataTransformer` source/database merge. Adding UUID version to the persisted default fingerprint intentionally makes newly written schema migration snapshots format version 2; existing format-version-1 JSON remains readable because deserialization does not reject older versions. Normalizing `DefaultGuid` to the already-existing fixed-`Guid` meaning does not introduce another format bump.

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

The pre-0.9 characterization exposed a responsibility split that was too fragile:

- MySQL mutations converted `Guid` to `byte[]` for `BINARY(16)` using `Guid.ToByteArray()`.
- MySQL reads called `MySqlDataReader.GetGuid(...)`, so MySqlConnector decided how to decode bytes.
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
- before the fail-closed UUID-4 checkpoint, `[DefaultNewUUID(UUIDVersion.Version7)]` mapped to C# `Guid.CreateVersion7()` while MySQL/MariaDB SQL generation emitted `UUID()`, which is UUIDv1 rather than UUIDv7

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

- `GuidStorageAttribute` is valid only when the resolved canonical provider CLR type is `Guid`/`Guid?`; this includes typed IDs whose scalar converter produces canonical `Guid`. Resolution runs after scalar mapping, including Roslyn assembly registrations.
- Multiple provider-specific attributes are allowed, with at most one declaration per `DatabaseType`.
- An exact provider match wins over `DatabaseType.Default`.
- no attribute means use DataLinq's deterministic provider default, not MySqlConnector's connection-wide behavior
- `NativeUuid` requires a provider-native UUID type or a provider-specific mapping that behaves as native UUID.
- `Text32`, `Text36`, native, and binary formats validate against parity-aligned built-in effective-type rules. Exact provider declarations are shared directly, while translated and canonical-fallback behavior is kept aligned with the SQL factories by focused parity tests; downstream factory overrides remain outside provider-neutral metadata resolution.
- Concrete `[Type]` scopes define the applicable providers. No type or a `DatabaseType.Default` type applies to all built-in providers; an exact provider `GuidStorage` declaration also makes that provider applicable. A default storage declaration is a fallback, not a hidden provider-selection mechanism.

## Metadata Shape

`ColumnDefinition` should expose resolved UUID storage metadata rather than requiring every provider path to scan attributes:

```csharp
public sealed record GuidStorageDefinition(
    DatabaseType DatabaseType,
    GuidStorageFormat Format,
    bool IsExplicit);
```

Useful helpers:

```csharp
public bool IsGuidColumn { get; }
public MetadataCollection<GuidStorageDefinition> GuidStorageDefinitions { get; }
public GuidStorageDefinition? GetGuidStorageFor(DatabaseType databaseType);
public bool IsGuidStorageUnresolvedFor(DatabaseType databaseType);
```

Resolved definitions must be provider-keyed. Runtime metadata is cached by model CLR type, and one model can legitimately declare MariaDB native UUID plus MySQL binary UUID storage; a singular mutable `ColumnDefinition.GuidStorage` would be incorrect.

The resolved and unresolved lookups are exact-only. `DatabaseType.Default` exists only in raw declarations and is expanded to concrete provider definitions during resolution. Definitions are emitted in stable MySQL, MariaDB, SQLite order, copied through snapshots and typed drafts, and included in metadata equivalence digests. A non-empty carried generated definition is not trusted: the runtime factory recomputes the expected result from scalar mapping, raw declarations, and the effective physical type, then rejects inconsistent metadata with a regeneration diagnostic. Missing definitions from older generated payloads are recomputed for compatibility. Provider-snapshot callers can use `IsGuidStorageUnresolvedFor(...)` to distinguish ambiguous byte layout from a provider that is simply not applicable.

Effective-type sharing is defined for DataLinq's built-in provider factories. Their existing protected virtual translation hooks remain active, but UUID metadata cannot predict arbitrary downstream subclass overrides during provider-neutral metadata construction. Custom mappings should use exact provider `[Type]` plus explicit `[GuidStorage]`; a general third-party provider/type-policy seam is outside the 0.9 contract.

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
- emit resolved generated metadata so the format is visible to runtime consumers
- consider switching new-project templates to explicit `Binary16Rfc4122` or `Text36`, but do not silently change the runtime default in a minor release

### MariaDB

MariaDB should continue preferring native `UUID` where supported.

Recommended default strategy:

- native `UUID` -> `NativeUuid`
- model `BINARY(16)` without a declaration -> `Binary16LittleEndian` for compatibility; live provider snapshots do not infer byte order from the SQL type alone
- `CHAR(36)` / `VARCHAR(36)` -> `Text36`
- `CHAR(32)` / `VARCHAR(32)` -> `Text32`

The supported 0.9 MariaDB matrix starts at 10.11, where native `UUID` is available, so immutable metadata resolves the built-in no-type default to `NativeUuid`. Existing runtime version detection remains a guard for older ad hoc connections; `UUID-2` must reject unsupported native UUID use there rather than mutating globally cached column metadata.

### SQLite

SQLite currently documents `Guid` as `TEXT` by default and recommends text unless there is a specific reason not to.

Recommended behavior:

- default `Guid` -> `TEXT` / `Text36`
- `BLOB` with `Guid` -> explicit `Binary16LittleEndian` or `Binary16Rfc4122`
- imported `BLOB` named `guid`/`uuid` should probably generate an explicit warning because byte layout cannot be inferred from SQLite type affinity

Mapped SQLite full-row reads and mutation writes normalize direct non-key `Guid` values through resolved column metadata in the bounded provider-consumption slice above. Bounded `UUID-3A` expression queries preserve the mapped `ColumnDefinition` through canonical operand normalization and encode non-key text and binary UUID parameters with that same writer codec. `SQLiteProvider.CreateParameter` remains the legacy metadata-free text fallback for routes without column metadata; manual string-only binding is not covered by the checkpoint.

Live-schema metadata never infers byte order from bare MySQL/MariaDB `BINARY(16)` or SQLite `BLOB`. The unresolved provider provenance survives metadata snapshots. If model-file generation cannot merge an existing source declaration that supplies the policy, it emits a blocking `DATALINQ_UUID_STORAGE_UNRESOLVED` source diagnostic instructing the user to choose little-endian compatibility or RFC/string order; the import pipeline therefore cannot silently turn an unknown schema layout into a model default.

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

Nullable membership must also preserve CLR semantics instead of delegating three-valued logic to raw SQL `IN`. The implemented invocation specialization records both total count and null count. Positive non-null-only membership adds `AND IS NOT NULL` so it remains false, rather than unknown, for a null column even under an outer negation; positive mixed membership renders `IN` over only non-null values plus `OR IS NULL`. Their negations render `NOT IN` plus `OR IS NULL` and `NOT IN` plus `AND IS NOT NULL`, respectively. Null-only membership becomes `IS NULL` or `IS NOT NULL`, and empty membership remains a fixed false or true predicate. A captured scalar null likewise renders `IS NULL`/`IS NOT NULL` without a parameter. Explicit rebinding that changes either specialized count is rejected, and only non-null values are encoded by the column codec.

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

`DefaultNewUUIDAttribute` still needs a complete client/server semantics pass, but the current UUID-4 checkpoint removes the incorrect fallback and fails closed.

Implemented safety boundary:

- direct source parsing accepts bare and qualified Version4/Version7 enum syntax, and direct metadata-to-model regeneration always emits the explicit version
- schema comparison, migration snapshots, `MetadataRoundtripComparison`, and equivalence-digest fingerprints treat Version4 and Version7 as different defaults
- SQLite, MySQL, and MariaDB DDL generation rejects both versions with an actionable provider, table, column, and requested-version diagnostic
- an exact MySQL/MariaDB `UUID()` imported from a schema remains provider-scoped `DefaultSql`; it is not converted to `DefaultNewUUIDAttribute` with an invented version
- explicit provider-scoped `DefaultSql` remains available when the caller deliberately accepts the provider expression and resulting physical storage

That preservation claim is deliberately limited to the direct parser/regenerator and semantic-fingerprint paths above. `MetadataTransformer` does not yet preserve a source `DefaultNewUUID` on a known `Guid` property through source/database metadata merge; transformer precedence remains open.

The refusal is intentional. MySQL and MariaDB document `UUID()` as UUIDv1, so it cannot satisfy either declared Version4 or Version7 semantics. MariaDB adds real `UUID_v4()` and `UUID_v7()` functions only from 11.7, while DataLinq's supported target matrix includes MariaDB 10.11 and 11.4. The SQL factory also has no server-version/capability input, and choosing a generation function alone would not prove that its result is transformed into the column's exact text, native, or binary storage format. SQLite has no verified built-in mapping in this contract. Automatic mapping is therefore deferred rather than guessed.

The public API may still need to split client semantics from provider SQL explicitly:

```csharp
[DefaultNewUUID(UUIDVersion.Version7)]          // model/client semantic default
[DefaultSql(DatabaseType.MySQL, "UUID()")]     // explicit provider SQL
```

or adding an option:

```csharp
[DefaultNewUUID(UUIDVersion.Version7, Generation = UUIDGeneration.Client)]
```

Direct generated-client Version4/Version7 initialization is now green on net8/net9/net10. Version7 uses a runtime RFC 9562 helper with the same UTC-millisecond plus random-bit contract as `Guid.CreateVersion7()`, and the parameterless generated mutable constructor is the sole evaluator for all client defaults; required constructors delegate to it without repeating assignments. This closes generated-constructor ownership, not metadata ownership. Transformer precedence when source and imported metadata disagree, SQLite expression import, and verified provider-version plus physical-storage mappings for automatic server generation remain open.

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

The bounded UUID-4 comparer now checks UUID storage format only after exact active-provider database-type equality. Physical type drift remains the primary difference and suppresses secondary format noise.

Important cases:

- model says `Text36`, database column is MySQL/MariaDB `CHAR(36)` or `VARCHAR(36)`: type and schema-observable format match
- model says `NativeUuid`, database column is MariaDB `UUID`: match
- model says either binary format and the database column is unhinted `BINARY(16)` or SQLite `BLOB`: type matches but the representation is unresolved
- model says `Text32` or `Text36` and the database column is unhinted SQLite `TEXT`: type matches but SQLite affinity does not reveal dashed versus undashed text, so the representation is unresolved
- trusted model/database metadata names different formats over the same compatible SQL type: report a semantic mismatch requiring manual data conversion

Diff generation cannot safely rewrite UUID representation without a data migration plan. Known same-type format changes are emitted as `Error/Ambiguous` review comments with no automatic `ALTER` or data rewrite. Bare deferred `Guid`, `System.Guid`, and `global::System.Guid` source is itself unresolved unless raw `[GuidStorage]` declares UUID intent, because syntax-only metadata cannot rule out an assembly scalar registration. Property converter markers and source-only typed IDs wait for semantic converter resolution.

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

Implemented on 2026-07-12:

- add provider-keyed immutable `GuidStorageDefinition` metadata and exact lookup
- validate eligibility against resolved canonical provider type so typed IDs over `Guid` remain supported and `Guid` models converted to another canonical type are rejected
- resolve against parity-aligned built-in MySQL, MariaDB, and SQLite physical-type rules, including canonical-provider fallback for typed IDs, while retaining the SQL factories' existing extension hooks outside this provider-neutral contract
- resolve exact-provider-over-default declarations and deterministic no-attribute compatibility defaults without consulting MySqlConnector `GuidFormat`
- validate the bounded native/text/binary matrix; keep model MySQL/MariaDB `BINARY(16)` on legacy little-endian compatibility, require explicit byte order for SQLite `BLOB`, and leave bare binary provider snapshots unresolved rather than inventing schema meaning
- carry resolved definitions through snapshots, typed drafts, source-generated runtime metadata, and equivalence digests; recompute definitions at runtime and reject inconsistent non-empty carried metadata
- resolve source-generator metadata only after explicit and assembly-registered scalar converters establish canonical provider types
- preserve unresolved binary provider-snapshot provenance through model generation and emit a blocking source diagnostic unless merged source metadata supplies an explicit or compatibility policy

Exit signal:

- every model-mapped canonical-`Guid` column has one resolved physical format per applicable provider or an actionable ambiguity diagnostic
- one model resolves distinct MySQL, MariaDB, and SQLite formats without mutable global provider state
- live provider metadata does not claim a binary byte order that the schema cannot reveal

The exit signal is green. This is resolved metadata, not provider UUID behavior; `UUID-2` remains the first consumer slice.

### UUID-2: Provider Reads, Writes, And Mutation Values

SQLite-first progress on 2026-07-13: mapped canonical `Guid` values on non-primary-key full-row paths require an exact resolved SQLite definition. Mutation writes encode through `GuidCodec`, while the column-aware reader decodes raw TEXT/BLOB values through the same format before model materialization. Active file-backed and in-memory evidence covers direct non-nullable `Guid` across inferred Text36, explicit Text32, little-endian BLOB, and RFC-order BLOB, plus nullable direct Text36/SQL NULL. The same raw write/raw-seed/update lifecycle proves a converter-backed typed ID over RFC-order BLOB and a nullable typed ID over Text36/SQL NULL. That original `UUID-2` checkpoint excluded UUID primary keys; bounded `UUID-3C` below now covers the exact scalar primary-key route. Other typed formats, projections, remaining predicates/membership, relations, defaults, ambiguous BLOB snapshots/imports, other SQLite reader routes, and aggregate `UUID-2` completion remain open.

MySQL/MariaDB progress on 2026-07-13: provider-owned access and transaction readers carry exact MySQL versus MariaDB identity into column-aware decoding, while the shared writer takes identity from the selected provider factory. Direct non-primary-key full-row `Guid` values consume native MariaDB UUID, Text36, Text32, little-endian `BINARY(16)`, and RFC-order `BINARY(16)` definitions. Binary reads always use raw bytes; text/native reads accept either raw text or the connector's canonical `Guid`, so byte order never comes from connector-wide configuration. Frozen non-symmetric vectors prove DataLinq writes as exact text/bytes, independently raw-seeded reads, normal and transaction access, update re-encoding, nullable Text36/SQL NULL, and a provider-differentiated binary column under no `GuidFormat`, `Char32`, `Binary16`, and `LittleEndianBinary16`. The same lifecycle proves a converter-backed typed ID whose binary definition is little-endian on MySQL and RFC-order on MariaDB, plus nullable typed Text36/SQL NULL. Public provider-less readers/accessors and transactions retain legacy behavior. That original `UUID-2` checkpoint excluded UUID primary keys; bounded `UUID-3C` below now covers the exact scalar primary-key route. Other independently instantiated typed formats, projections, remaining predicates/membership, relations, defaults, and aggregate `UUID-2` completion remain open.

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

Bounded `UUID-3A` progress on 2026-07-13 proves the existing `SC-4` canonical-column operand path and exact non-primary-key provider writers compose correctly without a production-route change. SQLite file and memory evidence covers equality for Text36, Text32, little-endian BLOB, and RFC-order BLOB. MySQL 8.4 and MariaDB 10.11/11.4/11.8 cover native MariaDB UUID, Text36, Text32, little-endian `BINARY(16)`, RFC-order `BINARY(16)`, and provider-differentiated binary storage under no `GuidFormat`, `Char32`, `Binary16`, and `LittleEndianBinary16`. Deep cases prove direct and representative converter-backed binary equality, inequality, local `Contains(...)`, and supported equality-`Any(...)`, plus nullable direct and typed Text36 comparison to both a non-null value and literal `null`. Exact captured parameters and selective queries over non-symmetric values prove column-specific physical text/bytes rather than a symmetric application round trip. Focused cases pass `2/2` on SQLite and `1/1` on the latest MySQL and MariaDB targets; full gates pass `1055/1055` unit, `762/762` SQLite compliance, `432/432` on each server target (`1728/1728` sequential), and `414/414` provider-specific executions. Captured-null variables, null-containing sequences, manual string-only `SqlQuery`, UUID primary/composite keys, cache/relation/update-delete key paths, projections, defaults, other typed formats, provider-less readers, memory, and Native AOT/browser publication remain outside this checkpoint; aggregate `UUID-3` is not complete.

Bounded `UUID-3B` progress on 2026-07-13 closes the captured-null and null-containing-sequence exclusions for the non-key expression-query route. Query-plan local-sequence specialization now records total count plus null count, and explicit rebinding rejects either mismatch while same-shape rebinding refreshes values and null position. Captured scalar null equality/inequality emits literal `IS NULL`/`IS NOT NULL` with zero parameters. Nullable `Contains(...)` and equality-`Any(...)` partition null members from codec-bound non-null members: positive non-null-only membership is `IN (...) AND IS NOT NULL`; positive mixed membership is `IN (...) OR IS NULL`; negative non-null-only membership is `NOT IN (...) OR IS NULL`; negative mixed membership is `NOT IN (...) AND IS NOT NULL`; null-only and empty sequences collapse to `IS NULL`/`IS NOT NULL` and fixed false/true respectively. The positive non-null-only guard keeps the predicate two-valued under outer negation and compound Boolean composition. Focused evidence covers direct `Guid?` and Guid-backed typed nullable Text36 on SQLite file/memory and the existing MySQL/MariaDB `GuidFormat` loops. Focused results are `21/21` `QueryPlanInvocation`, `10/10` `QueryPlanNode`, `25/25` parser/snapshot, `38/38` SQL parity, `2/2` generic nullable SQLite, `4/4` UUID SQLite, `2/2` MySQL 8.4, and `2/2` MariaDB 11.8. Full gates pass `1057/1057` unit, `766/766` SQLite compliance, `435/435` on each server target (`1740/1740` sequential), and `414/414` provider-specific executions. Manual string-only `SqlQuery`, UUID primary/composite keys, cache/relation/update-delete key paths, projections, defaults, other typed formats, provider-less readers, memory, and Native AOT/browser publication remain outside this checkpoint; aggregate `UUID-3` is not complete.

Bounded `UUID-3C` progress on 2026-07-13 closes the scalar UUID primary-key exclusion only for a table with exactly one canonical `Guid` key component and an exact resolved `GuidStorage` definition for the concrete SQLite, MySQL, or MariaDB source. The provider readers and writers now use that column definition for full-row decoding, scalar key selection, insert/update/delete values, and update/delete predicates; converter-backed key selection remains canonical and does not invoke model conversion before `DataLinqKey` construction. The neutral source-row route admits direct `Guid` and a representative Guid-backed typed ID on that exact shape, preserving single and batched cold loads, one logical cache probe, warm reference identity, and transaction authoritative reload. SQLite evidence uses little-endian BLOB for the direct key and RFC-order BLOB for the typed key. MySQL/MariaDB evidence differentiates MySQL little-endian binary from MariaDB RFC-order binary under absent, `Char32`, `Binary16`, and `LittleEndianBinary16` connector settings; raw physical bytes, independently cold reads, batched loads, warm hits, update predicates, and delete predicates are all asserted. This is not aggregate UUID key support: composite UUID keys, relation/index/foreign-key operands, general scalar/projection decoding, manual string-only `SqlQuery`, joined typed-UUID row-local hydration, provider-less readers, memory, Native AOT/browser publication, defaults, schema work beyond the separate bounded UUID-4 format checkpoint, and other typed formats remain open; aggregate `UUID-2` and `UUID-3` are not complete.

Focused `UUID-3C` evidence is `2/2` on SQLite file/memory, `1/1` for the new scalar-key case on MySQL 8.4 and MariaDB 11.8, and `3/3` for the full UUID fixture on those two server targets. The integrated gates pass `1059/1059` unit, `57/57` generator, `768/768` SQLite compliance, `438/438` on each of MySQL 8.4 and MariaDB 10.11/11.4/11.8 (`1752/1752` sequential server executions), and `414/414` provider-specific executions. This is evidence for the bounded scalar-key checkpoint, not aggregate UUID completion or the final frozen-candidate rerun.

- preserve `ColumnDefinition` through direct equality, nullable equality, and LINQ membership translation
- normalize every local `Contains(...)` element and already-supported equality-membership `Any(...)` element with the target column codec
- normalize explicit `SqlQuery.Where(...)` values whenever column metadata is available
- complete composite UUID key components, relation/index/foreign-key predicates, and the remaining cache and mutation key routes beyond bounded scalar `UUID-3C`
- reject ambiguous binary UUID binding before execution instead of passing a raw `Guid` to the connector

Exit signal:

- equality, local membership, keys, cache lookup, and relations all bind the same physical bytes/text as writes
- a connection-wide `GuidFormat` setting is no longer required for the supported paths
- mixed UUID formats in one connection remain column-correct

### UUID-4: Defaults And Validation

Implemented bounded validation half on 2026-07-14:

- compare UUID format only after exact physical SQL type equality
- validate resolved or raw-declared model format against compatible provider types without converter calls
- infer database format only where the SQL declaration is self-describing: MySQL/MariaDB text length or native MariaDB `UUID`
- report unhinted `BINARY(16)`, SQLite `BLOB`, and SQLite `TEXT` as unresolved rather than claiming a false match
- report trusted same-type representation changes as semantic/manual data migrations and generate review-only comments
- keep bare deferred Guid syntax unresolved and property converter markers non-UUID until authoritative scalar metadata exists

Focused evidence is `13/13` comparer, `6/6` configured SQLite validator, `2/2` live MySQL/MariaDB binary-import, and `1/1` live MariaDB native-UUID tests. Integrated generator, unit, and SQLite file/memory gates pass `58/58`, `1170/1170`, and `803/803`; the full provider-specific lane passes `326/326` across MySQL 8.4 and MariaDB 10.11/11.4/11.8.

Implemented a second bounded defaults checkpoint on 2026-07-14:

- format a fixed `Guid` already present in finalized direct-canonical metadata through the column's exact resolved provider codec
- emit Text36, Text32, little-endian binary, RFC-order binary, and native MariaDB UUID literals from the same physical values used by runtime writes
- reject unresolved `BINARY(16)`/BLOB layouts before SQL rather than reviving a type-based byte-order guess
- reject converter-backed model defaults without invoking either conversion direction
- preserve provider-scoped `DefaultSql` as the explicit raw-expression escape hatch

Focused defaults evidence is `9/9` for the MySQL/MariaDB literal matrix, `3/3` for SQLite literal/engine/guard coverage, and `4/4` for omitted-column inserts across MySQL 8.4 and MariaDB 10.11/11.4/11.8. The integrated gates pass `58/58` generator, `1173/1173` unit, `803/803` SQLite file/memory compliance, and `354/354` provider-specific tests across all server targets. This evidence covers all 13 provider-format combinations, unresolved-layout rejection, and zero-call converter rejection, but it starts from finalized metadata containing a real `Guid`; it does not prove a public source declaration or regeneration roundtrip.

Implemented a third bounded defaults checkpoint on 2026-07-14:

- reject `DefaultNewUUID` Version4 and Version7 during SQLite, MySQL, and MariaDB DDL generation instead of mapping either declaration to unverified provider UUID semantics
- preserve the requested UUID version through direct source parsing, direct metadata-to-model regeneration, schema default comparison, migration snapshots, `MetadataRoundtripComparison`, and equivalence-digest fingerprints
- advance newly written schema migration snapshots to format version 2 because the serialized default fingerprint now includes UUID version; existing version-1 JSON remains readable because deserialization does not enforce a version gate
- import an exact MySQL/MariaDB `UUID()` expression as provider-scoped `DefaultSql` rather than falsely labeling the provider's UUIDv1 function as UUIDv4 or UUIDv7
- retain explicit provider-scoped `DefaultSql` as the intentional raw-expression escape hatch

That checkpoint's integrated gates passed `58/58` generator, `1180/1180` unit, and `803/803` SQLite file/memory compliance tests. Its four-target compliance batch passed `1511/1511`; the full provider-specific MySQL/MariaDB lane passed `372/372`, split into `185/185` for MySQL 8.4 plus MariaDB 10.11 and `187/187` for MariaDB 11.4 plus 11.8. Live evidence on all four server targets created an exact `UUID()` default, imported it from information schema as provider-scoped `DefaultSql`, regenerated DDL, recreated the schema, and preserved the raw expression. This proves the fail-closed provider boundary, raw `UUID()` import/DDL roundtrip, and UUID-version semantic fingerprints. It does not prove automatic server generation or a complete client default contract.

Implemented a fourth bounded fixed-value source checkpoint on 2026-07-14:

- add public `[DefaultGuid("xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx")]` as a storage-neutral, legal attribute encoding for one fixed model `Guid`; this is a source carrier for the existing ordinary default meaning, not a new semantic default category
- require an actual string literal in exact 36-character Guid `D` form; accept uppercase hexadecimal on input while regenerating canonical lowercase, and leave `[Default("guid-text")]` invalid rather than silently coercing a string
- normalize parsing to base `DefaultAttribute(Guid)` with no retained `CodeExpression`, build runtime metadata with a real `Guid`, and initialize generated mutable models through `global::System.Guid.Parse(...)`
- regenerate an expression-free ordinary `DefaultAttribute(Guid)` as `[DefaultGuid("canonical-lowercase-D")]`
- normalize the carrier and expression-free base representation in schema comparison, migration snapshots, `MetadataRoundtripComparison`, and equivalence digests
- reuse the already-covered provider codecs to render that direct canonical value across all 13 SQLite/MySQL/MariaDB text, native, and binary formats

Focused evidence passes `61/61` `SyntaxParserTests`, `190/190` `MetadataDefinitionFactoryTests`, `22/22` `ModelFileFactoryTests`, `15/15` `SchemaComparerGuidStorageTests`, and `1/1` focused generator coverage. Integrated gates pass `59/59` generator, `1187/1187` unit, `803/803` SQLite file/memory, `1614/1614` latest four-target compliance, and `372/372` full MySQL/MariaDB provider-specific tests (`185/185` plus `187/187`). Because the carrier normalizes to the existing fixed-`Guid` default meaning, schema migration snapshots remain at format version 2; this checkpoint does not introduce another format bump.

This closes the bounded source declaration, mutable initialization, and direct metadata-to-model regeneration path for fixed direct-`Guid` defaults. At that checkpoint it did not prove source/database `MetadataTransformer` precedence, import of static provider Guid defaults, SQLite expression/BLOB import, converter-backed defaults, dynamic `DefaultNewUUID` completion, or one authoritative constructor/default owner.

A following bounded client-generation checkpoint makes direct `DefaultNewUUID` Version4 and Version7 initialization portable across net8/net9/net10. Version4 emits fully qualified `Guid.NewGuid()`; Version7 calls a runtime-owned RFC 9562 helper because the source generator itself targets netstandard2.0 and consumer code cannot call `Guid.CreateVersion7()` on net8. The helper embeds the current UTC Unix-millisecond timestamp and preserves 74 random bits. It is stateless and thread-safe, but deliberately does not promise same-millisecond invocation order, clock-regression monotonicity, distributed sequencing, or ordering under legacy little-endian binary storage. Generated parameterless mutable constructors now own all client-default evaluation. Required constructors retain `: this()` and assign only their required arguments, while immutable-copy construction generates nothing. This changes no provider DDL: SQLite, MySQL, and MariaDB still reject automatic Version4/Version7 server generation.

Focused runtime evidence passes `5/5`, constructor-generation coverage passes `16/16`, the focused source-generator case passes `1/1`, and the existing fail-closed SQLite default guard passes `6/6`. Integrated gates pass `60/60` generator, `1203/1203` unit, and `803/803` SQLite file/memory tests. The generated consumer fixture builds cleanly with zero warnings or errors for net8.0, net9.0, and net10.0.

Still open:

- define converter-backed model-default conversion instead of treating model values as canonical values
- define authoritative metadata ownership and transformer precedence when source declarations and imported metadata disagree
- add verified provider-version capability and exact physical-storage mappings before enabling automatic server generation
- import static provider Guid defaults into the fixed-value carrier where the provider representation is unambiguous
- preserve or diagnose SQLite UUID default expressions and BLOB literals during schema import
- extend canonical compatibility beyond finalized converter-backed `Int32` and add authoritative source-only typed-ID converter resolution
- complete the aggregate UUID-4 provider/evidence matrix

Exit signal:

- runtime writes and fixed direct-`Guid` defaults in finalized metadata produce identical physical layouts
- schema validation distinguishes canonical `Guid` compatibility from physical UUID-format compatibility
- 0.9 does not claim database-generated UUIDv7 semantics that the provider does not supply

The format comparison, fixed direct-`Guid` source-carrier and DDL-literal, fail-closed UUID-version truthfulness, and net8-safe single-evaluation generated-client portions of the exit signal are green. The finalized converter-backed canonical `Int32` checkpoint narrows the general schema prerequisite but does not complete Guid-specific or source-only canonical resolution. Converter-backed defaults, automatic server capability/storage mapping, source-transform ownership, static provider-default import, SQLite expression/BLOB import, broader canonical/source-only validation, and aggregate evidence keep UUID-4 incomplete.

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
- MySQL UUIDv1 `UUID()`, `UUID_TO_BIN(...)`, and `BIN_TO_UUID(...)`: <https://dev.mysql.com/doc/refman/en/miscellaneous-functions.html>
- MariaDB UUIDv1 `UUID()`: <https://mariadb.com/docs/server/reference/sql-functions/secondary-functions/miscellaneous-functions/uuid>
- MariaDB 11.7+ `UUID_v4()`: <https://mariadb.com/docs/server/reference/sql-functions/secondary-functions/miscellaneous-functions/uuid_v4>
- MariaDB 11.7+ `UUID_v7()`: <https://mariadb.com/docs/server/reference/sql-functions/secondary-functions/miscellaneous-functions/uuid_v7>
- .NET `Guid.ToByteArray()` byte-order remarks: <https://learn.microsoft.com/en-us/dotnet/api/system.guid.tobytearray>
