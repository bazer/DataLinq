# DataLinq + MySQL & MariaDB

DataLinq provides unified support for both MySQL and MariaDB through the `DataLinq.MySql` NuGet package. It uses the `MySqlConnector` ADO.NET driver, which is compatible with both database systems.

## Type Mapping

DataLinq's `create-models` command reads the schema from `information_schema` and maps MySQL/MariaDB data types to their corresponding C# types. The mapping is aware of `UNSIGNED` flags and column definitions.

| MySQL/MariaDB Type | Maps to C# Type |
| :--- | :--- |
| `INT UNSIGNED` | `uint` |
| `INT` | `int` |
| `BIGINT UNSIGNED`| `ulong` |
| `BIGINT` | `long` |
| `SMALLINT` | `short` |
| `TINYINT` | `sbyte` |
| `TINYINT UNSIGNED`| `byte` |
| `BIT(1)` | `bool` |
| `DECIMAL` | `decimal` |
| `DOUBLE`, `FLOAT`| `double`, `float` |
| `VARCHAR`, `TEXT`, `CHAR`, etc. | `string` |
| `DATE` | `DateOnly` |
| `DATETIME`, `TIMESTAMP` | `DateTime` |
| `TIME` | `TimeOnly` |
| `ENUM` | A generated C# `enum` |
| `BINARY(16)` | `Guid` |
| `BLOB`, `VARBINARY`, etc. | `byte[]` |
| `JSON` | `string` |
| `UUID` (MariaDB only) | `Guid` |


## MariaDB-Specific Features

DataLinq can detect when it is connected to a MariaDB server and enables specific features.

### Native `UUID` Type (MariaDB 10.7+)
- **Reading Schema:** When `create-models` is run against a MariaDB 10.7+ database, it correctly identifies the native `UUID` column type and maps it to a C# `Guid`. This will also add a `[Type(DatabaseType.MariaDB, "uuid")]` attribute to the generated model property.
- **Generating Schema:** When `create-sql` is run, DataLinq respects the `[Type]` attributes in your models. If a `Guid` property has a `[Type(DatabaseType.MariaDB, "uuid")]` attribute, it will generate the `UUID` data type. It **does not** automatically convert a plain C# `Guid` to `UUID`; it defaults to `BINARY(16)` unless the specific attribute is present.

### Provider Configuration
To leverage MariaDB-specific features when reading or writing schemas, ensure your `datalinq.json` specifies `"Type": "MariaDB"` in the connection settings:
```json
"Connections": [
  {
    "Type": "MariaDB",
    "DataSourceName": "my_mariadb_database",
    "ConnectionString": "..."
  }
]```

## SQL Generation

When generating a schema from your DataLinq models:
- For MySQL, C# `Guid` properties are mapped to `BINARY(16)` by default.
- For MariaDB, C# `Guid` properties are also mapped to `BINARY(16)` by default to maintain compatibility. To generate the native `UUID` type, you must explicitly add the `[Type(DatabaseType.MariaDB, "uuid")]` attribute to your model's property.
- `CREATE OR REPLACE VIEW` is used for view definitions to ensure compatibility with both systems.