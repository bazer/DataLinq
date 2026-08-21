# Scalar Converters and Typed IDs

Scalar converters let a generated model expose a domain type while DataLinq stores, compares, queries, and caches a supported scalar provider value. Typed IDs are the obvious use case, but the same contract applies to any deterministic one-value mapping.

The boundary has three layers:

| Layer | Example | Owner |
| --- | --- | --- |
| Model value | `EmployeeId` | Your generated model and application API |
| Canonical provider CLR value | `Guid` | The scalar converter, query bindings, provider rows, keys, and caches |
| Physical or wire value | MariaDB `UUID`, dashed text, compact text, or one of two 16-byte layouts | The active SQL provider and the column's UUID codec |

Do not collapse the last two layers. An `EmployeeId -> Guid` converter is provider-neutral. Whether that `Guid` becomes SQLite `TEXT`, MariaDB `UUID`, or MySQL `BINARY(16)` is separate column metadata; see [UUID storage](#uuid-backed-typed-ids).

## A Complete Typed-ID Converter

This converter is concrete, non-generic, visible to generated code, and has a public parameterless constructor:

```csharp
using DataLinq;

namespace MyApp.Models;

public readonly record struct EmployeeId(Guid Value);

public sealed class EmployeeIdConverter
    : DataLinqScalarConverter<EmployeeId, Guid>
{
    public override Guid ToProvider(
        EmployeeId modelValue,
        in ScalarConversionContext context) =>
        modelValue.Value;

    public override EmployeeId FromProvider(
        Guid providerValue,
        in ScalarConversionContext context) =>
        new(providerValue);
}
```

`ScalarConversionContext.Column` identifies the mapped column. The converter contract deliberately excludes provider identity: provider-specific physical encoding belongs to type and UUID-storage metadata, not to converter branches.

Apply the converter to one value property:

```csharp
using DataLinq.Attributes;

[PrimaryKey]
[Column("employee_id")]
[ScalarConverter(typeof(EmployeeIdConverter))]
public abstract EmployeeId Id { get; }
```

Or register it once for that exact model type in the consuming assembly:

```csharp
using DataLinq.Attributes;
using MyApp.Models;

[assembly: ScalarConverterRegistration(
    typeof(EmployeeId),
    typeof(EmployeeIdConverter))]
```

Assembly registration applies to value properties of the registered model type. A property's `[ScalarConverter(...)]` is more specific and wins over the assembly registration. Assembly registrations must be unique per model type; duplicate registrations are model errors.

## Converter Requirements

The source generator validates the converter before emitting runtime metadata:

- it must derive from `DataLinqScalarConverter<TModel,TProvider>` with the property's exact non-null model type;
- it must be a concrete, closed, non-generic class with a public parameterless constructor;
- the converter and every containing type must be accessible from generated code (`public`, `internal`, or `protected internal`);
- generic converter, model, or containing-type identities are outside the 0.9 boundary;
- `TProvider` must be one supported scalar: a primitive, enum, `string`, `byte[]`, `Guid`, `DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset`, or `TimeSpan`;
- `[ScalarConverter]` is valid only on mapped value properties, not relation properties.

DataLinq owns null propagation. `Nullable<T>` converter contract arguments are rejected, and the object-level adapter returns null without calling `ToProvider(...)` or `FromProvider(...)`. Write the two converter methods for non-null values and put `[Nullable]` on the mapped property when the column is nullable.

## Correctness Rules

Converters run at identity and query boundaries, so they have to be boring:

- conversion must be deterministic;
- `FromProvider(ToProvider(value))` must preserve the model value used by equality;
- model values that compare equal must produce canonical provider values that compare equal;
- the converter must not depend on ambient provider, culture, time, randomness, or mutable process state;
- `byte[]` provider values must be treated as value data, not caller-owned mutable identity.

DataLinq validates types and, for converted joins, requires both sides to resolve the same model type, canonical provider type, and converter CLR type. That is useful fencing, not a proof of semantics. Two uses of the same converter class can still behave incorrectly if the converter consults mutable state or interprets columns differently.

## Supported Runtime Boundaries

Within the documented query and provider surface, the canonical provider value is used consistently for:

- SQL and Memory row decoding and model materialization;
- inserts, updates, parameter binding, and supported database-generated value hydration;
- generated single-column `Get(...)` methods and exact-key terminals;
- row-cache identity, relation/index keys, and cache invalidation;
- equality and comparison values, captured constants, and supported local `Contains(...)` membership;
- relation keys and supported implicit or explicit join keys;
- `DataLinq.Memory` seeding, `Find(...)`, predicates, and direct scalar projection;
- schema validation against the canonical provider CLR type and the configured physical SQL type.

Support at the conversion boundary does not make every surrounding LINQ shape legal. The operator still has to be listed in [Supported LINQ Queries](Supported%20LINQ%20Queries.md), and the selected backend must accept the normalized plan. Memory intentionally supports a much smaller subset than SQL.

## Known Limits

- Arbitrary value-object member translation is unsupported. Query `row.Id == id`; do not assume `row.Id.Value == rawGuid` can be translated.
- `Sum`, `Min`, `Max`, and `Average` over converter-backed selectors are rejected before SQL execution. A converter defines value conversion, not order- or arithmetic-preserving semantics. `Count` and `Any` do not aggregate the converted value and remain available in otherwise supported shapes.
- Converter-backed source defaults are not a general feature. Supported provider-generated canonical values can be converted during insert hydration, but `[DefaultNewUUID]` is currently a direct-`Guid` contract and does not generate a typed ID. Converter-backed static/default import and generation still have gaps.
- Converter construction is parameterless. Dependency injection, per-property constructor arguments, and provider-specific converter instances are not supported.
- Converter type equality is not behavioral equality. Schema and join validation cannot prove that a converter is deterministic, equality-preserving, or compatible with existing stored data.

## UUID-Backed Typed IDs

When the canonical provider type is `Guid`, `[GuidStorage(...)]` applies to the converted property exactly as it does to a direct `Guid` property:

```csharp
[PrimaryKey]
[Column("employee_id")]
[ScalarConverter(typeof(EmployeeIdConverter))]
[Type(DatabaseType.MySQL, "binary", 16)]
[GuidStorage(DatabaseType.MySQL, GuidStorageFormat.Binary16Rfc4122)]
[Type(DatabaseType.MariaDB, "uuid")]
[GuidStorage(DatabaseType.MariaDB, GuidStorageFormat.NativeUuid)]
[Type(DatabaseType.SQLite, "text")]
[GuidStorage(DatabaseType.SQLite, GuidStorageFormat.Text36)]
public abstract EmployeeId Id { get; }
```

The converter still sees only `Guid`. The SQL provider applies the selected physical format after `ToProvider(...)` and reverses it before `FromProvider(...)`.

For format defaults, legacy byte order, ambiguity, schema validation, and migration rules, see the authoritative [`[GuidStorage]` contract](Attributes%20and%20Model%20Definitions.md#guidstorage) and the provider guides.

## Regeneration and Verification

Keep the typed-ID declaration and converter outside files that `datalinq generate models` may replace. Apply the property attribute in the supported model-declaration edit surface, or use assembly registration when one type has one application-wide mapping. After changing a converter or UUID format:

1. rebuild so the source generator validates and emits the resolved mapping;
2. regenerate models where the upgrade requires it;
3. run `datalinq validate` against every configured SQL provider;
4. test reads, writes, generated lookup, relations/joins, and existing stored values;
5. treat a physical UUID-format change as a data migration, not a metadata cleanup.
