> [!WARNING]
> This document is roadmap or specification material. It describes planned behavior, not current DataLinq behavior.

# Specification: Scalar Converter Support

**Status:** Accepted.

**Release scope:** The bounded first implementation targets 0.9; broader converter authoring remains later work.

**Last reviewed:** 2026-07-10.

**0.9 execution plan:** [Scalar Converters and Typed IDs Implementation Plan](../roadmap-implementation/v0.9/Scalar%20Converters%20and%20Typed%20IDs%20Implementation%20Plan.md).

**Goal:** Add a first-class scalar conversion layer so a model property can use a domain CLR type while DataLinq stores, queries, caches, validates, and mutates the underlying provider CLR value consistently.

The core idea is simple:

```text
Model CLR type    <->    Canonical provider CLR type    <->    Provider physical/wire representation
CustomerId        <->    int                            <->    INT / INTEGER
EmailAddress      <->    string                         <->    VARCHAR(320) / TEXT
OrderPayload      <->    string                         <->    JSON / TEXT
Guid              <->    Guid                           <->    native UUID / text / column-specific binary layout
```

This should be a general DataLinq feature, not a strongly typed ID feature with a generic name. Strongly typed IDs are the obvious first use case, but the same mechanism should support JSON parsing, encrypted strings, compressed payloads, custom date/string formats, URL/email/country-code value objects, and plugin-provided conversions for legacy schemas.

The two conversion boundaries are deliberately distinct:

- scalar converters own model value to canonical provider CLR value conversion
- provider codecs own canonical provider CLR value to column-specific physical/wire representation

UUID byte order and text/native UUID choices therefore belong to [UUID Storage Format Support](../providers-and-features/UUID%20Storage%20Format%20Support.md), not inside a typed-ID converter.

## Why This Exists

DataLinq currently treats a column's model CLR type as the value that provider readers, provider writers, query translation, mutation SQL, cache keys, and generated model properties all share. That works for primitives, enums, `Guid`, `DateOnly`, `TimeOnly`, and similar direct storage types.

It breaks down when the public model should be stricter than storage:

```csharp
public readonly partial record struct CustomerId(int Value);
public readonly partial record struct OrderId(Guid Value);

[Table("orders")]
public abstract partial class Order(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<Order, ShopDb>(rowData, dataSource), ITableModel<ShopDb>
{
    [PrimaryKey]
    [AutoIncrement]
    [Column("id")]
    [ScalarConverter(typeof(OrderIdConverter))]
    public abstract OrderId? Id { get; }

    [ForeignKey("customers", "id", "orders_customer_id_fk")]
    [Column("customer_id")]
    [ScalarConverter(typeof(CustomerIdConverter))]
    public abstract CustomerId CustomerId { get; }
}
```

The database still stores `orders.customer_id` as an integer. The C# API no longer lets a caller accidentally pass `ProductId` where `CustomerId` belongs. That is not cosmetic; it removes an entire class of same-shaped-primitive bugs.

## Design Principles

- The converter belongs to column metadata. Every runtime path must ask the same metadata question instead of each layer inventing its own conversion check.
- Null handling should be DataLinq-owned by default. A non-null converter should not need to understand `Nullable<T>` unless it explicitly opts into null-aware behavior.
- The provider CLR type, not the model CLR type, decides database type inference unless an explicit `[Type(...)]` attribute overrides it.
- Conversions must be usable without reflection on hot paths. Reflection is acceptable during metadata construction; runtime should use cached delegates or generated calls.
- Explicit configuration wins over convention. Plugins may discover converters, but no plugin should silently change a column mapping when an explicit converter is present.
- The 0.9 converter form is pure and stateless, has a public parameterless constructor, and is resolved once during metadata construction. Service-dependent converter lifetimes are post-0.9 work.
- An explicit property converter wins over an assembly registration. Duplicate assembly registrations for the same model type are an error rather than an order-dependent choice.
- Single-column scalar conversion is the first feature. Composite value-object mapping is a separate feature and should not be smuggled into v1.

## Non-Goals

- Do not implement object-to-multiple-column mapping in this feature.
- Do not require a dependency on Vogen, StronglyTypedId, Meziantou.Framework.StronglyTypedId, or any other typed-ID library in `DataLinq`.
- Do not make JSON path querying part of the first scalar converter implementation. Scalar conversion can deserialize JSON values; provider-native JSON path predicates are a later query feature.
- Do not make arbitrary converters run inside SQL translation. Query translation should translate supported value extraction and provider values, not execute user code on database rows.

## Proposed API Surface

The runtime API should be small. The generic base type gives authors a clear implementation target while DataLinq consumes a non-generic interface from metadata.

```csharp
public readonly record struct ScalarConversionContext(ColumnDefinition Column);

public interface IDataLinqScalarConverter
{
    Type ModelType { get; }
    Type ProviderType { get; }

    object? ToProviderObject(object? modelValue, in ScalarConversionContext context);
    object? FromProviderObject(object? providerValue, in ScalarConversionContext context);
}

public abstract class DataLinqScalarConverter<TModel, TProvider> : IDataLinqScalarConverter
{
    public Type ModelType => typeof(TModel);
    public Type ProviderType => typeof(TProvider);

    public abstract TProvider ToProvider(TModel modelValue, in ScalarConversionContext context);
    public abstract TModel FromProvider(TProvider providerValue, in ScalarConversionContext context);

    public object? ToProviderObject(object? modelValue, in ScalarConversionContext context)
    {
        if (modelValue is null)
            return null;

        return ToProvider((TModel)modelValue, context);
    }

    public object? FromProviderObject(object? providerValue, in ScalarConversionContext context)
    {
        if (providerValue is null)
            return null;

        return FromProvider((TProvider)providerValue, context);
    }
}
```

The runtime conversion context is intentionally column-only. Active SQL provider identity does not belong in model-to-canonical conversion: SQLite, MySQL, MariaDB, and memory must materialize the same model value from the same canonical CLR value. Provider-aware converter discovery may use separate resolution metadata, while physical/wire codecs own database-specific representation.

Attribute-level configuration:

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class ScalarConverterAttribute(Type converterType) : Attribute
{
    public Type ConverterType { get; } = converterType;
}
```

Assembly-level default configuration:

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ScalarConverterRegistrationAttribute(
    Type modelType,
    Type converterType) : Attribute
{
    public Type ModelType { get; } = modelType;
    public Type ConverterType { get; } = converterType;
}
```

The assembly-level form avoids copy-pasting the converter on every `CustomerId` column:

```csharp
[assembly: ScalarConverterRegistration(typeof(CustomerId), typeof(CustomerIdConverter))]
[assembly: ScalarConverterRegistration(typeof(OrderId), typeof(OrderIdConverter))]
```

An explicit property attribute still wins:

```csharp
[ScalarConverter(typeof(TrimmedUppercaseCodeConverter))]
[Column("legacy_code")]
public abstract LegacyCode Code { get; }
```

## Typed ID Example

Built-in or user-authored typed IDs should be boring to map:

```csharp
public readonly partial record struct CustomerId(int Value);

public sealed class CustomerIdConverter
    : DataLinqScalarConverter<CustomerId, int>
{
    public override int ToProvider(CustomerId modelValue, in ScalarConversionContext context) =>
        modelValue.Value;

    public override CustomerId FromProvider(int providerValue, in ScalarConversionContext context) =>
        new(providerValue);
}
```

Generated models then use the typed ID directly:

```csharp
[PrimaryKey]
[AutoIncrement]
[Column("id")]
public abstract CustomerId? Id { get; }

[ForeignKey("customers", "id", "orders_customer_id_fk")]
[Column("customer_id")]
public abstract CustomerId CustomerId { get; }
```

Important behavior:

- `db.Get<Customer>(new CustomerId(42))` should be supported eventually, but internally it should normalize to `IntKey(42)`.
- `db.Query().Orders.Where(x => x.CustomerId == customerId)` should parameterize `customerId.Value`.
- `localCustomerIds.Contains(x.CustomerId)` should become an `IN (...)` predicate over provider values.
- FK relation matching should compare provider values, not boxed typed-ID instances.
- Auto-increment inserts should read the provider value and construct the typed ID before mutating the inserted model state.

## Existing Library Adapters

DataLinq should not care which library produced the type. It should only need a converter.

Vogen-shaped example:

```csharp
[ValueObject<int>]
public readonly partial struct CustomerId;

public sealed class CustomerIdConverter
    : DataLinqScalarConverter<CustomerId, int>
{
    public override int ToProvider(CustomerId modelValue, in ScalarConversionContext context) =>
        modelValue.Value;

    public override CustomerId FromProvider(int providerValue, in ScalarConversionContext context) =>
        CustomerId.From(providerValue);
}
```

StronglyTypedId-shaped example:

```csharp
[StronglyTypedId(Template.Int)]
public readonly partial struct CustomerId;

public sealed class CustomerIdConverter
    : DataLinqScalarConverter<CustomerId, int>
{
    public override int ToProvider(CustomerId modelValue, in ScalarConversionContext context) =>
        modelValue.Value;

    public override CustomerId FromProvider(int providerValue, in ScalarConversionContext context) =>
        new(providerValue);
}
```

Optional future adapter packages could provide generator helpers, for example:

- `DataLinq.Vogen`
- `DataLinq.StronglyTypedId`
- `DataLinq.MeziantouStronglyTypedId`

Those packages should generate or register DataLinq converters. They should not alter the core DataLinq model.

## String Parsing and Legacy Columns

Yes, plugin support for arbitrary parsing of string values should sit naturally on top of this.

Examples:

```csharp
public readonly partial record struct CountryCode(string Value);

public sealed class CountryCodeConverter
    : DataLinqScalarConverter<CountryCode, string>
{
    public override string ToProvider(CountryCode modelValue, in ScalarConversionContext context) =>
        modelValue.Value.ToUpperInvariant();

    public override CountryCode FromProvider(string providerValue, in ScalarConversionContext context) =>
        new(providerValue.Trim().ToUpperInvariant());
}
```

```csharp
[Column("country")]
[Type(DatabaseType.MySQL, "char", 2)]
[Type(DatabaseType.SQLite, "text")]
[ScalarConverter(typeof(CountryCodeConverter))]
public abstract CountryCode Country { get; }
```

This is useful for legacy schemas where a column is technically `TEXT` but semantically holds a code, path, URI, email address, normalized phone number, external ID, or other string-backed domain value.

The brutal caveat: parsing arbitrary strings is where bad data stops being hypothetical. The converter API needs a clear failure story:

- conversion failures while reading rows should throw a DataLinq-specific exception with table, column, provider value, and target model type
- validation tooling should optionally sample or scan rows for parseability, but schema validation alone cannot prove every row is valid
- converters should avoid culture-sensitive parsing unless the converter explicitly owns the culture

## JSON Parsing Use Case

Scalar conversion can support JSON-as-value-object before DataLinq supports JSON path querying.

```csharp
public sealed record UserSettings(string Theme, bool NotificationsEnabled);

public sealed class UserSettingsConverter
    : DataLinqScalarConverter<UserSettings, string>
{
    public override string ToProvider(UserSettings modelValue, in ScalarConversionContext context) =>
        JsonSerializer.Serialize(modelValue, UserSettingsJsonContext.Default.UserSettings);

    public override UserSettings FromProvider(string providerValue, in ScalarConversionContext context) =>
        JsonSerializer.Deserialize(providerValue, UserSettingsJsonContext.Default.UserSettings)
            ?? throw new InvalidOperationException("JSON settings deserialized to null.");
}
```

```csharp
[Column("settings")]
[Type(DatabaseType.MySQL, "json")]
[Type(DatabaseType.SQLite, "text")]
[ScalarConverter(typeof(UserSettingsConverter))]
public abstract UserSettings Settings { get; }
```

This gives immediate read/write support. It does not imply that this query should translate:

```csharp
db.Query().Users.Where(x => x.Settings.Theme == "dark")
```

That belongs to provider-native JSON path support. The existing JSON plan should either build on scalar converters or explicitly define why JSON needs special storage behavior beyond scalar conversion. The honest division:

- scalar converter: whole-value serialization and deserialization
- JSON provider feature: JSON path predicates, JSON indexes, partial updates, provider capability checks

## Metadata Model

Column metadata needs to distinguish public model type from provider storage type:

```csharp
public sealed class ColumnScalarMapping
{
    public CsTypeDeclaration ModelType { get; }
    public CsTypeDeclaration ProviderType { get; }
    public IDataLinqScalarConverter? Converter { get; }
    public bool HasConverter => Converter is not null;
}
```

`ColumnDefinition` should expose something conceptually equivalent to:

```csharp
public CsTypeDeclaration ModelClrType => ValueProperty.CsType;
public CsTypeDeclaration ProviderClrType { get; }
public IDataLinqScalarConverter? ScalarConverter { get; }
```

The provider CLR type should drive:

- default SQL type mapping
- reader primitive selection
- writer primitive conversion
- key creation
- query parameter values
- schema comparison expectations

The model CLR type should drive:

- generated property signatures
- user-facing mutation API
- expression tree member matching
- relation property shape
- diagnostics and docs

## Runtime Touch Points

The feature is only real if every path normalizes through the same conversion layer.

Required changes:

1. **Row reading**
   - Provider reader reads the provider CLR value.
   - DataLinq converts provider value to model value before exposing the property.
   - Existing public model-facing `RowData` remains model-valued. Public indexers, `GetValues()`, and `GetRowData()` already expose that behavior.
   - Backends use a separate internal canonical-provider-value buffer for storage, comparison, keys, and query execution, then materialize model-valued `RowData` through the column converter.

2. **Mutation writing**
   - `MutableRowData` accepts model values.
   - SQL writers convert model values to canonical provider values before a column/provider codec applies physical conversion such as `Guid` to a specific `BINARY(16)` layout.

3. **Query constants**
   - `x.CustomerId == customerId` stores the provider value as the SQL parameter.
   - `ids.Contains(x.CustomerId)` maps every local model ID to provider values.
   - Unsupported expressions like `x.CustomerId.Value > 10` should be rejected unless value-member unwrapping is explicitly supported.

4. **Primary and foreign keys**
   - `KeyFactory` should create keys from provider values, not model values, so `CustomerId(1)` and raw provider `1` do not become different cache keys for the same row.
   - FK relation indexes should normalize both sides through provider values.

5. **Auto-increment/default values**
   - Database-generated provider values must be converted back to model values before the mutable model is updated.

6. **Schema validation and diff**
   - Schema validation should compare database column type against the provider CLR type mapping and explicit `[Type(...)]`, not against the model CLR type.

## Query Translation Boundary

Supported v1 query shapes should be narrow:

```csharp
var id = new CustomerId(42);

db.Query().Customers.Where(x => x.Id == id);
db.Query().Customers.Where(x => ids.Contains(x.Id));
db.Query().Orders.Join(
    db.Query().Customers,
    order => order.CustomerId,
    customer => customer.Id,
    (order, customer) => new { order, customer });
```

Potentially supported when the converter exposes a recognized provider value member:

```csharp
db.Query().Customers.Where(x => x.Id.Value == 42);
```

But this should be treated carefully. Supporting `.Value` for typed IDs is convenient, but it leaks implementation details into query translation. The better v1 target is direct typed-ID equality and membership.

Not supported by scalar conversion alone:

```csharp
db.Query().Users.Where(x => x.Settings.Theme == "dark");
db.Query().Customers.Where(x => x.Email.Domain == "example.com");
db.Query().Orders.Where(x => x.Money.Currency == "SEK");
```

Those are structured value-object queries. They require provider-specific translation support, computed columns, indexes, or explicit query helper APIs.

## Plugin Model

After explicit attributes and assembly registrations work, a plugin/provider model can add convention-based converter discovery:

```csharp
public interface IScalarConverterPlugin
{
    bool TryResolveConverter(
        ScalarConverterResolutionContext context,
        out IDataLinqScalarConverter converter);
}

public readonly record struct ScalarConverterResolutionContext(
    ColumnDefinition Column,
    CsTypeDeclaration ModelType,
    DatabaseType DatabaseType);
```

Useful plugin scenarios:

- map every `*Id` record struct with a single `Value` property to its value type
- map Vogen value objects by detecting generated `From(...)` and `Value`
- map StronglyTypedId values by detecting constructor and `Value`
- map specific string-backed types like `EmailAddress`, `Slug`, `CountryCode`
- map provider-specific legacy values, such as `char(1)` flags to tiny value objects

Plugins should not run on every hot-path conversion. They should resolve metadata once, then DataLinq should cache concrete converter delegates.

## Source Generation

The model generator should eventually offer a typed-key generation option:

```json
{
  "modelGeneration": {
    "stronglyTypedKeys": true,
    "keyTypeSuffix": "Id"
  }
}
```

Possible generated output:

```csharp
public readonly partial record struct CustomerId(int Value);
public readonly partial record struct OrderId(int Value);

[PrimaryKey]
[Column("id")]
public abstract CustomerId? Id { get; }

[ForeignKey("customers", "id", "orders_customer_id_fk")]
[Column("customer_id")]
public abstract CustomerId CustomerId { get; }
```

Generation rules should be conservative:

- generate typed IDs only for single-column primary keys and foreign keys
- FK column type must match the referenced key type
- composite keys stay as primitive columns or existing composite key support
- generated converter types should be internal unless public output is explicitly requested
- generated names must be stable and collision-checked

## AOT and Trimming

Scalar converters are a good AOT feature if implemented with source-generated or statically registered delegates. They become an AOT liability if DataLinq discovers constructors, `Value` properties, or `From(...)` methods by reflection at runtime.

Rules:

- reflection-based plugin discovery is allowed during build/source generation or metadata construction when the target app is not trimmed
- runtime conversion should use concrete converter instances and cached delegates
- JSON converters should prefer `JsonSerializerContext` examples and documentation
- diagnostics should warn when a converter requires reflection-heavy behavior in AOT-sensitive modes

## Implementation Plan

1. Build on the Phase 10 key-shape seam by resolving the existing model CLR type, provider CLR type, and scalar converter handle placeholders.
2. Add explicit `[ScalarConverter]` and assembly-level converter registration attributes.
3. Add converter resolution during metadata parsing from runtime types and Roslyn source models.
4. Update provider readers/writers so values flow through provider CLR values before model conversion.
5. Update query constant normalization for equality, local `Contains`, local `Any(predicate)`, explicit joins, relation predicates, and direct PK lookup. Existing key APIs accept model-valued typed-ID components through normalization; new generated `Find(TId)` overloads are not required for 0.9.
6. Update relation index caches, `DataLinqKey` fallback creation, and table cache lookups to normalize keys through provider values. Disable existing generated provider-key/relation fast paths for converted components until a genuinely provider-typed generated accessor exists.
7. Add schema validation and SQL generation tests proving provider type inference is based on provider CLR type.
8. Add compliance tests for typed IDs across SQLite, MySQL, and MariaDB.
9. Add docs explaining scalar conversion as a planned feature, then promote to user docs only after the runtime behavior lands.
10. Add optional typed-key generation once explicit manual converters are stable.

## Test Matrix

Minimum coverage:

- read/write typed `int`, `long`, `Guid`, and `string` IDs
- nullable typed IDs on auto-increment primary keys
- FK relation loading where PK and FK both use the same typed ID
- direct `Where(x => x.Id == id)`
- local `ids.Contains(x.Id)`
- explicit join on typed key selectors
- insert with auto-increment typed ID populated after save
- schema diff does not report a false type mismatch for typed IDs over primitive columns
- string parsing converter trims/normalizes read values and writes canonical provider values
- JSON whole-value converter reads and writes a POCO/value object without claiming JSON path query support

## Resolved 0.9 Storage Decision

The first implementation converts eagerly into model-valued `RowData`. Any later conversion memoization must be internal and preserve the same public row/indexer behavior.

## Open Questions

- Should a later generated API add ergonomic `Find(TId)` overloads after the normalized fallback is proven?
- What explicit lifetime model would justify service-dependent converters after 0.9?
- How much `.Value` unwrapping should query translation support for typed IDs?
- Should DataLinq ship a small built-in typed ID generator, or only the scalar converter layer plus optional adapter packages?
- Should converter failures be recoverable diagnostics in validation mode but hard exceptions at runtime?
