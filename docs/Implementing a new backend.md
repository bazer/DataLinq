# Implementing a New Backend for DataLinq

DataLinq is extensible, but "backend agnostic" should not be oversold.

The current design is friendliest to SQL-backed providers with ADO.NET-style connections, commands, readers, and transactions. If your target backend fits that shape, adding a provider is realistic. If it does not, expect real friction.

The concrete extension points are:

1. metadata reading
2. SQL generation from metadata
3. runtime provider and transaction behavior
4. provider registration through `PluginHook`

## 1. Metadata Reading

### Purpose

The first job is schema introspection. That means turning backend-specific metadata into DataLinq's internal `DatabaseDefinition`, `TableDefinition`, `ViewDefinition`, and `ColumnDefinition` structures.

### How It's Done in DataLinq

- MySQL and MariaDB use metadata factories built on `information_schema`.
- SQLite uses `sqlite_master` plus PRAGMA queries.
- Both factories finish by running shared metadata passes that parse indices, relations, interfaces, and indexed columns.

### Steps to Implement

1. **Create a new metadata factory**
   - Implement `IMetadataFromDatabaseFactoryCreator`.
   - Return a backend-specific `IMetadataFromSqlFactory`.
   - Read tables, views, columns, indices, and foreign keys from the backend's own metadata source.
2. **Map types honestly**
   - Convert native backend types into C# types and DataLinq metadata.
   - Decide how provider-specific types, nullability, auto-increment, enums, and defaults are represented.
3. **Test edge cases**
   - Cover views, composite keys, nullable columns, foreign keys, and type mapping oddities.

## 2. The Default Type System Contract

To improve portability, DataLinq uses a set of standardized default type names. When a model does not carry a provider-specific `[Type(...)]` override, the provider's `ISqlFromMetadataFactory` should translate these default types into native backend types.

| Default Type Name | Represents C# Type(s) | Typical Native Mapping |
| :--- | :--- | :--- |
| `integer` | `int`, `uint`, `short`, `ushort` | `INT`, `INTEGER` |
| `big-integer` | `long`, `ulong` | `BIGINT` |
| `decimal` | `decimal` | `DECIMAL`, `NUMERIC` |
| `float` | `float` | `FLOAT`, `REAL` |
| `double` | `double` | `DOUBLE` |
| `text` | `string`, `char` | `TEXT`, `VARCHAR`, `CLOB` |
| `boolean` | `bool` | `BOOLEAN`, `BIT` |
| `datetime` | `DateTime` | `DATETIME` |
| `timestamp` | `DateTime` | `TIMESTAMP` |
| `date` | `DateOnly` | `DATE` |
| `time` | `TimeOnly` | `TIME` |
| `uuid` | `Guid` | `UUID`, `UNIQUEIDENTIFIER`, `BINARY(16)` |
| `blob` | `byte[]` | `BLOB`, `VARBINARY` |
| `json` | `string` | `JSON`, `JSONB`, text fallback |
| `xml` | `string` | `XML`, text fallback |

The sharp edge here is obvious: the presence of default type names does not magically make every backend equally natural. They are a portability contract, not a promise of identical semantics.

## 3. SQL Generation From Metadata

### Purpose

For SQL-based backends, DataLinq needs to generate DDL from metadata.

### How It's Done in DataLinq

- Providers register an `ISqlFromMetadataFactory`.
- Existing implementations sort tables and views, emit DDL, and translate DataLinq default types or existing provider-specific types into backend SQL.

### Steps to Implement

1. **Develop a SQL generator**
   - Accept `DatabaseDefinition` and emit backend-specific DDL.
   - Handle quoting, defaults, type names, primary keys, unique indices, and foreign keys.
2. **Align with metadata**
   - Make sure your SQL generator and metadata factory agree on type and constraint semantics.
3. **Support output and execution**
   - Return SQL text and support applying it against the target database or file.

## 4. Runtime Provider and Transaction Behavior

### Purpose

Beyond schema management, a provider must actually run queries and writes. That means connection creation, command execution, reader wrapping, transaction management, and provider-specific SQL behavior.

### How It's Done in DataLinq

- Providers create backend-specific `Database<T>` implementations and transaction classes.
- Transactions are responsible for opening connections, choosing isolation level, executing commands, and committing or rolling back.
- The LINQ layer and SQL builder rely on those provider pieces rather than talking to the backend directly.

### Steps to Implement

1. **Implement the provider creator**
   - Implement an `IDatabaseProviderCreator`.
   - Register a concrete provider that can create `Database<T>` instances for your backend.
2. **Wrap native readers**
   - Make sure parameterized commands and scalar reads behave correctly.
3. **Implement transaction behavior**
   - Choose sane isolation defaults and cooperate with DataLinq's transaction lifecycle.
   - Cache merge behavior after commit matters. Get that wrong and the provider will feel haunted.
4. **Make failures diagnosable**
   - Integrate logging and provide useful error behavior.

## 5. Registering the New Backend

Registration is not just "call `RegisterProvider()` and hope for the best".

In practice, you need to populate the relevant `PluginHook` dictionaries:

- `DatabaseProviders`
- `SqlFromMetadataFactories`
- `MetadataFromSqlFactories`

If one of those is missing, the provider is only half-installed.

## 6. Testing Expectations

Do not ship a provider with only happy-path tests.

At minimum, verify:

- schema introspection
- type mapping in both directions
- DDL generation
- transaction lifecycle
- simple reads and writes
- cache-aware repeated reads

## 7. Summary

Adding a new backend is practical when the backend looks enough like the existing SQL providers. The real work is not just syntax translation. It is getting metadata, transactions, and cache-aware runtime behavior all to agree.
