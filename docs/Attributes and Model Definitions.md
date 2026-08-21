# Attributes and Model Definitions

If you generate models from an existing database, DataLinq will emit most of this for you.

This page matters when you are:

- authoring source models by hand
- customizing generated models
- trying to understand what the generator and metadata layer are actually looking at

All of the attributes below live in the `DataLinq.Attributes` namespace.

## Minimal Shape

At a high level, a DataLinq model definition consists of:

1. a database class marked with `[Database(...)]`
2. table or view models marked with `[Table(...)]` or `[View(...)]`
3. properties marked with column, key, relation, type, or cache metadata

Example:

```csharp
using DataLinq;
using DataLinq.Attributes;
using DataLinq.Instances;
using DataLinq.Interfaces;

[Database("appdb")]
public partial class AppDb(IDataLinqReadSource readSource) : IDatabaseModel
{
    public DbRead<User> Users { get; } = new(readSource);
}

[Table("users")]
public abstract partial class User(IRowData rowData, IDataLinqReadSource readSource)
    : Immutable<User, AppDb>(rowData, readSource), ITableModel<AppDb>
{
    [PrimaryKey]
    [Column("UserId")]
    public abstract Guid UserId { get; }

    [Nullable]
    [Column("Email")]
    public abstract string Email { get; }
}
```

## Core Structure Attributes

### `[Database("name")]`

Marks the database model class.

Used on classes implementing `IDatabaseModel`.

### `[Table("name")]`

Marks a table-backed model.

Used on models implementing `ITableModel<TDatabase>`.

### `[View("name")]`

Marks a view-backed model.

Used on models implementing `IViewModel<TDatabase>`.

### `[Definition("sql")]`

Supplies the SQL definition for a view or other definition-backed model.

This matters when generating SQL from source models. If a view has no definition, SQL generation has nothing honest to emit.

### `[Interface<T>]`

Requests an additional generated interface for the model.

The interface attributes also support:

- `new InterfaceAttribute()`
- `new InterfaceAttribute("IMyInterface")`
- `new InterfaceAttribute(generateInterface: false)`

This is advanced, but it is used in the test models and metadata tests and is worth knowing exists.

## Column and Key Attributes

### `[Column("db_name")]`

Maps a property to a database column name.

### `[PrimaryKey]`

Marks the property as part of the primary key.

### `[AutoIncrement]`

Marks the property as auto-incrementing.

### `[Nullable]`

Marks the property as nullable in the metadata model.

## Relation Attributes

### `[Relation("table", "column", "name")]`

Defines a relation from the current model to another table.

Example:

```csharp
[Relation("orders", "UserId", "orders_ibfk_1")]
public abstract IImmutableRelation<Order> orders { get; }
```

That relation is what allows access patterns like:

```csharp
var orders = user.orders.ToList();
```

### `[ForeignKey(...)]`

There is also a `ForeignKeyAttribute` in the public attribute set.

In practice, generated metadata often carries both relation information and foreign-key information. `[ForeignKey(...)]` is about the foreign-key column itself. `[Relation(...)]` is about navigable relation shape.

## Type and Value Attributes

### `[Type(...)]`

Overrides or specifies the database column type.

This can be:

- a default type name such as `text` or `uuid`
- a provider-specific type such as `binary(16)` for MySQL

Example:

```csharp
[Type(DatabaseType.MySQL, "binary", 16)]
[Column("UserId")]
public abstract Guid UserId { get; }
```

### `[ScalarConverter(typeof(...))]`

Maps a model-facing property type to one canonical provider CLR scalar. This is the normal way to expose typed IDs without teaching SQL providers, row caches, or query bindings about the wrapper type.

```csharp
[PrimaryKey]
[Column("employee_id")]
[ScalarConverter(typeof(EmployeeIdConverter))]
public abstract EmployeeId Id { get; }
```

The converter must derive from `DataLinqScalarConverter<TModel,TProvider>`. A property attribute overrides an assembly-level `[ScalarConverterRegistration(typeof(TModel), typeof(TConverter))]`; registrations otherwise apply to every mapped value property of the exact model type.

This contract is detailed enough to deserve its own page. See [Scalar Converters and Typed IDs](Scalar%20Converters%20and%20Typed%20IDs.md) for a compilable converter, construction and visibility rules, null handling, supported runtime boundaries, query/default limitations, and UUID-backed typed IDs.

### `[GuidStorage(...)]`

Selects the physical representation for a direct `Guid` property or a converter-backed property whose canonical provider CLR type is `Guid`.

The provider-neutral form applies wherever the declared SQL type is compatible:

```csharp
[GuidStorage(GuidStorageFormat.Text36)]
```

Use the provider-scoped overload when one model targets different physical types:

```csharp
[Type(DatabaseType.MySQL, "binary", 16)]
[GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)]
[Type(DatabaseType.MariaDB, "uuid")]
[GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.NativeUuid)]
[Type(DatabaseType.SQLite, "text")]
[GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Text36)]
[Column("external_id")]
public abstract Guid ExternalId { get; }
```

The formats are deliberately explicit:

| Format | Physical value | Compatible built-in SQL shapes |
| --- | --- | --- |
| `NativeUuid` | Provider-native UUID value/string | Unmodified MariaDB `UUID` |
| `Text36` | Lowercase dashed `D` format | SQLite `TEXT`; MySQL/MariaDB `CHAR(36)` or `VARCHAR(36)` |
| `Text32` | Lowercase compact `N` format | SQLite `TEXT`; MySQL/MariaDB `CHAR(32)` or `VARCHAR(32)` |
| `Binary16LittleEndian` | Legacy .NET mixed-endian bytes from `Guid.ToByteArray()` | SQLite `BLOB`; MySQL/MariaDB `BINARY(16)` |
| `Binary16Rfc4122` | RFC/string-order bytes | SQLite `BLOB`; MySQL/MariaDB `BINARY(16)` |

Compatibility defaults preserve existing DataLinq behavior where the model type is unambiguous: MariaDB `UUID` uses `NativeUuid`; MySQL/MariaDB `BINARY(16)` uses the legacy little-endian layout; 36- and 32-character server text types select their matching text format; and DataLinq's built-in SQLite `TEXT` mapping uses `Text36`. SQLite `BLOB` has no safe byte-order default and requires an explicit binary format.

Those defaults do not make a live schema self-describing. Bare `BINARY(16)` and `BLOB` do not reveal byte order, and SQLite `TEXT` does not reveal dashed versus compact text. `validate` reports unresolved or mismatched storage unless it has trusted resolved metadata. Supported joins involving canonical `Guid` keys also require the same resolved format on both sides before SQL is rendered.

Changing `Binary16LittleEndian` to `Binary16Rfc4122`, or `Text36` to `Text32`, changes stored bytes/text. It is a data migration even when the SQL column type stays the same. `datalinq diff` will not invent that rewrite for you.

`[DefaultGuid(...)]` is a fixed direct-`Guid` model default and is encoded through the resolved format. `[DefaultNewUUID]` is also currently direct-`Guid` only, and provider DDL generation rejects it until the provider/version/storage mapping is proven. Converter-backed typed-ID defaults are not implied by either attribute.

### `[Enum(...)]`

Associates enum values with a database enum representation.

The attribute is metadata for how stored values map to a C# enum type. It does not, by itself, mean the model generator owns the enum declaration.

When regenerating models:

- database enum columns can produce generated C# enum declarations
- enum declarations already present in the model file are preserved
- enum types declared elsewhere are treated as external custom types; DataLinq preserves the property type and `[Enum(...)]` metadata, but does not generate another enum declaration with the same name

Use this when several model files share the same enum type:

```csharp
[Enum("Active", "Inactive")]
[Column("status")]
public abstract AccountStatus Status { get; }
```

`AccountStatus` can live in a separate source file. The source generator still uses the enum metadata for runtime conversion, but `generate models` will not duplicate the enum declaration in every generated model file.

### `[Default(...)]`

Assigns a default value in the DataLinq metadata model.

This is not just decorative schema metadata. DataLinq uses these defaults in generated mutable models so default behavior stays consistent across providers instead of depending on whether the backend happens to support SQL defaults well.

Practical implications:

- the default expression must be compatible with the property type
- source-defined defaults are validated by the source generator
- invalid defaults are reported on the model source itself rather than surfacing later as broken generated code
- when `generate models` preserves an overridden C# property type from an existing model declaration, it also preserves the existing source default expression for that property instead of blindly replacing it with a database primitive

Examples:

```csharp
[Default("active")]
public abstract string Status { get; }
```

```csharp
[Default(AccountStatus.Active)]
public abstract AccountStatus Status { get; }
```

### `[DefaultGuid(...)]`

Declares one fixed `Guid` model value using a legal C# attribute argument:

```csharp
[DefaultGuid("00112233-4455-6677-8899-aabbccddeeff")]
public abstract Guid ExternalId { get; }
```

The argument must be an actual string literal in the exact 36-character Guid `D` format (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`). Uppercase hexadecimal is accepted, but `generate models` emits the canonical lowercase form. Compact, braced, parenthesized, constant, and computed-expression forms are rejected.

`DefaultGuid` is a storage-neutral source carrier, not a new semantic default category. Source parsing normalizes it to the ordinary base `DefaultAttribute` meaning with a real `Guid` value and no retained C# code expression. Runtime metadata therefore contains a `Guid`, generated mutable initialization uses `global::System.Guid.Parse(...)`, and metadata-to-model generation writes the carrier or an exact base `DefaultAttribute(Guid)` back as `[DefaultGuid("...")]` only when `CodeExpression` is null. Schema comparison, migration snapshots, and equivalence digests treat the carrier and base representation as the same fixed default value; metadata roundtrip comparison also requires the same code expression. Custom Guid-valued `DefaultAttribute` subclasses remain distinct. A Guid default carrying a `CodeExpression` keeps that distinction in roundtrip comparison and fails closed during model regeneration; schema comparison, snapshots, and digests intentionally describe the fixed default value rather than its client initialization expression. This normalization does not require another migration-snapshot format bump; new snapshots remain at format version 2.

Once provider metadata resolves the column's `[GuidStorage]`, the existing SQLite, MySQL, and MariaDB codecs encode the canonical `Guid` into the same Text36, Text32, native UUID, little-endian binary, or RFC-order binary representation used by runtime writes. The bounded provider matrix covers all 13 direct-`Guid` format combinations.

`[Default("00112233-4455-6677-8899-aabbccddeeff")]` remains a string default and is invalid for a `Guid` property; DataLinq does not silently coerce it. `DefaultGuid` also does not define converter-backed defaults, source/database merge precedence, static provider-default import, or SQLite expression/BLOB import. Dynamic client generation belongs to `[DefaultNewUUID]`, described below.

### `[DefaultCurrentTimestamp]`

Marks a property as using the provider's current date/time default.

DataLinq maps this to the right provider expression based on the property type:

- `DateOnly` -> provider current date
- `TimeOnly` -> provider current time
- `DateTime` -> provider current timestamp

### `[DefaultNewUUID]`

Marks a property as using a generated UUID default.

This attribute supports a `UUIDVersion` argument, including `Version4` and `Version7`.

Use it on `Guid` properties. Anything else is a model error, not a meaningful configuration.

Generated mutable models initialize Version4 with `Guid.NewGuid()` and Version7 with DataLinq's RFC 9562 runtime helper, which works on `net8.0`, `net9.0`, and `net10.0`. The parameterless generated constructor owns client-default evaluation. A generated constructor with required arguments delegates to it and does not generate another UUID, so each new mutable instance evaluates each dynamic default once. Constructing a mutable copy from an immutable instance preserves the existing values instead of generating replacements.

Version7 embeds the current UTC Unix timestamp in milliseconds and fills the remaining 74 usable bits with random data. This is the same stateless contract as .NET's `Guid.CreateVersion7()`: it does not promise invocation order within one millisecond, monotonic behavior after clock rollback, or distributed sequencing. RFC-order text/binary storage retains the timestamp prefix's lexical ordering; legacy little-endian binary storage does not.

> [!WARNING]
> UUID version selection is preserved by direct source parsing, direct metadata-to-model regeneration, schema comparison, migration snapshots, and metadata-equivalence checks. This does not yet guarantee preservation when source and database metadata are merged; source-precedence behavior remains open. SQLite, MySQL, and MariaDB DDL generation deliberately rejects both Version4 and Version7 `[DefaultNewUUID]` declarations until DataLinq has a verified provider-version and storage-format mapping. An exact MySQL/MariaDB `UUID()` default imported from an existing schema remains provider-scoped `[DefaultSql]`; DataLinq does not relabel that expression as UUIDv4 or UUIDv7. Use provider-scoped `[DefaultSql]` only when you intentionally accept the expression's real UUID version and physical storage. The client contract applies only to direct `Guid` properties; it does not define converter-backed typed-ID defaults.

## Index Attributes

### `[Index(...)]`

Defines an index on a property.

The public API exposes:

- `IndexCharacteristic`
- `IndexType`

Example:

```csharp
[Index("idx_username", IndexCharacteristic.Simple, IndexType.BTREE)]
[Column("UserName")]
public abstract string UserName { get; }
```

## Cache Tuning Attributes

These are advanced attributes. They matter when you want to shape cache behavior instead of accepting defaults.

### `[UseCache]`

Enables caching on a database, model, or property.

### `[CacheLimit(...)]`

Adds cache limits such as rows, bytes, seconds, minutes, or megabytes.

### `[CacheCleanup(...)]`

Defines cleanup intervals for cached data.

### Memory-Pressure Cleanup

Memory-pressure cleanup is runtime policy, not a model attribute. That is intentional: process memory pressure depends on the host process and deployment, while model metadata should describe the database shape and ordinary cache policy.

Configure it through the provider state cache:

```csharp
using DataLinq.Cache;

provider.State.Cache.ConfigureMemoryPressureCleanup(
    CacheMemoryPressureCleanupPolicy.Conservative with
    {
        HighMemoryLoadThresholdPercent = 90,
        MinimumCacheBytes = 16 * 1024 * 1024,
        TargetReductionPercent = 25,
        MaxRowsPerPass = 1024
    });
```

Use `CacheMemoryPressureCleanupPolicy.Disabled` to turn pressure-triggered cleanup off. Browser/WASM runtimes do not start background cache cleanup, and memory-pressure cleanup reports unsupported there.

### `[IndexCache(...)]`

Controls index-cache behavior.

These attributes are powerful, but they are not "set everything to eleven" knobs. If you are tuning them, do it with a performance reason and a test to prove the reason exists.

## Real Example from the Test Models

The `EmployeesDb` test model uses several of the advanced cache attributes together:

```csharp
[UseCache]
[CacheLimit(CacheLimitType.Megabytes, 200)]
[CacheLimit(CacheLimitType.Minutes, 60)]
[CacheCleanup(CacheCleanupType.Minutes, 30)]
[Database("employees")]
public partial class EmployeesDb(DataSourceAccess dataSource) : IDatabaseModel
{
    // ...
}
```

That is a good example of where the cache-related attributes belong: on the database model, not scattered randomly across unrelated places.

## Practical Advice

- Use the generator and generated models as your baseline truth. Do not freestyle the model shape unless you understand what the metadata pipeline expects.
- Prefer provider-specific `[Type(...)]` overrides only when you actually need provider-specific behavior.
- Keep scalar converters deterministic and equality-preserving; configure UUID physical storage independently from model conversion.
- Keep relation definitions simple and explicit.
- Treat cache attributes as advanced tuning, not as required setup.
- Use `[Definition(...)]` for views you expect `generate sql` to emit correctly.
- If you are unsure how an attribute should look, copy a real example from the generated test models and adjust it rather than inventing syntax from memory.
