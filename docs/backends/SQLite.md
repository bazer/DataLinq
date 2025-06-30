# DataLinq + SQLite

DataLinq provides full support for SQLite through the `DataLinq.SQLite` NuGet package. It uses the `Microsoft.Data.Sqlite` ADO.NET driver to interact with the database.

## Type Mapping and Affinities

SQLite uses a flexible type system with "type affinities" rather than strict static types. A column with an `INTEGER` affinity can still store text if needed. DataLinq's `create-sql` command generates schemas using these standard affinities, while the `create-models` command interprets them to generate appropriate C# types.

### Standard Affinity Mapping

When reading an SQLite schema, DataLinq maps the affinities to C# types as follows:

| SQLite Affinity | Maps to C# Type |
| :--- | :--- |
| `INTEGER` | `int` |
| `REAL` | `double` |
| `TEXT` | `string` |
| `BLOB` | `byte[]` |

### Convention-Based "Smart" Mapping

To provide a richer and more convenient developer experience, DataLinq's `create-models` command for SQLite also uses a **convention-based mapping** that overrides the default affinity mapping when a column's name follows a common pattern.

This means you can get more specific C# types like `DateOnly`, `Guid`, and `bool` generated automatically by simply naming your columns appropriately.

| SQLite Affinity | If Column Name... | Generated C# Type |
| :--- | :--- | :--- |
| `TEXT` | Ends with `_date` (e.g., `hire_date`) | `DateOnly` |
| `TEXT` | Ends with `_time` (e.g., `start_time`) | `TimeOnly` |
| `TEXT` | Ends with `_at` or contains `datetime` or `timestamp` | `DateTime` |
| `TEXT` | Is `guid`/`uuid` or ends with `_guid`/`_uuid` | `Guid` |
| `INTEGER` | Starts with `is_` or `has_` (e.g., `is_active`) | `bool` |
| `BLOB` | Is `guid`/`uuid` or ends with `_guid`/`_uuid` | `Guid` |

**Example:**
A column named `created_at` with a `TEXT` affinity will be mapped to a C# `DateTime` property, not a `string`. A column named `is_deleted` with an `INTEGER` affinity will be mapped to a `bool`.

### Enum Handling
SQLite does not have a native `ENUM` type. DataLinq recommends storing enum values as either `INTEGER` or `TEXT` in the database. When you define a C# `enum` property in your source model, the source generator will handle the mapping correctly, but the `create-models` command **will not** attempt to infer an `enum` from an `INTEGER` column automatically.

## SQL Generation

When generating a schema for SQLite from your DataLinq models:
- C# `Guid` properties are mapped to `TEXT`.
- C# `bool` properties are mapped to `INTEGER`.
- C# `DateOnly`, `DateTime`, etc., are mapped to `TEXT`.
- `AUTOINCREMENT` is correctly used for integer primary keys.

## Best Practices

- **Use Conventions:** Name your columns according to the smart mapping conventions to get richly typed C# models automatically.
- **`Guid` Storage:** It is recommended to store `Guid` values as `TEXT` for readability and compatibility. The provider handles the conversion automatically.
- **Enums:** Define your `enum` types in your C# source models to ensure type safety.