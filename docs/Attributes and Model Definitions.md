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
public partial class AppDb(DataSourceAccess dataSource) : IDatabaseModel
{
    public DbRead<User> Users { get; } = new(dataSource);
}

[Table("users")]
public abstract partial class User(IRowData rowData, IDataSourceAccess dataSource)
    : Immutable<User, AppDb>(rowData, dataSource), ITableModel<AppDb>
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

### `[Enum(...)]`

Associates enum values with a database enum representation.

### `[Default(...)]`

Assigns a default value in the DataLinq metadata model.

This is not just decorative schema metadata. DataLinq uses these defaults in generated mutable models so default behavior stays consistent across providers instead of depending on whether the backend happens to support SQL defaults well.

Practical implications:

- the default expression must be compatible with the property type
- source-defined defaults are validated by the source generator
- invalid defaults are reported on the model source itself rather than surfacing later as broken generated code
- when `create-models` preserves an overridden C# property type from an existing source model, it also preserves the existing source default expression for that property instead of blindly replacing it with a database primitive

Examples:

```csharp
[Default("active")]
public abstract string Status { get; }
```

```csharp
[Default(AccountStatus.Active)]
public abstract AccountStatus Status { get; }
```

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
- Keep relation definitions simple and explicit.
- Treat cache attributes as advanced tuning, not as required setup.
- Use `[Definition(...)]` for views you expect `create-sql` to emit correctly.
- If you are unsure how an attribute should look, copy a real example from the generated test models and adjust it rather than inventing syntax from memory.
