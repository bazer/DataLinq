# Implementing a New Backend for DataLinq

DataLinq’s architecture is designed to be backend agnostic by isolating database-specific functionality behind well-defined interfaces and adapter classes. To implement a new backend, you must address three key areas:

1.  **Reading Metadata Definitions**
2.  **The Default Type System Contract**
3.  **Generating SQL Scripts from Models**
4.  **Reading and Writing Data to the Backend**

Below is a breakdown of what each area entails and how existing providers (e.g., MySQL and SQLite) implement these features.

---

## 1. Reading Metadata Definitions

### Purpose
The first step in integrating a new backend is to read and interpret the database’s schema. This process converts system-specific metadata (often stored in system tables like `information_schema`) into DataLinq’s internal representations (such as `DatabaseDefinition`, `TableDefinition`, and `ColumnDefinition`).

### How It’s Done in DataLinq
- **Metadata Factories:**
  DataLinq uses specialized factories (e.g., `MetadataFromMySqlFactory` and `MetadataFromSQLiteFactory`) to connect to the database, query system tables, and build metadata objects. These factories map database-specific types to C# types and determine properties like primary keys, foreign keys, and indices.

- **Core Methods:**
  The factory methods parse table structures, extract column information, and apply attributes (such as `[Table]`, `[Column]`, and `[Relation]`) to construct a complete metadata model. This model then drives both code generation and SQL script creation.

### Steps to Implement
1. **Create a New Metadata Factory:**
   - Implement a factory class similar to `MetadataFromMySqlFactory` that connects to your new backend.
   - Query the backend’s system tables to retrieve schema information.
   - Map the retrieved metadata to DataLinq’s internal types (e.g., creating instances of `TableDefinition` and `ColumnDefinition`).

2. **Type Mapping:**
   - Implement logic to convert your database’s native data types into C# types. Your factory should also be able to translate from DataLinq's default types into your backend's specific types. See the contract below for details.

3. **Testing:**
   - Ensure that the factory properly handles edge cases (e.g., composite primary keys, nullable columns) and that the resulting metadata accurately reflects the database schema.

---

## 2. The Default Type System Contract

To ensure model portability and simplify the creation of new providers, DataLinq uses a standardized set of "default" type names. When generating a schema from a model that does not have a specific type attribute for your backend, your provider's `ISqlFromMetadataFactory` should be able to translate these default types into native types for your target database.

Your new provider must implement a translation from these default types.

| Default Type Name | Represents C# Type(s) | Rationale & Common Mappings |
| :--- | :--- | :--- |
| **Numeric Types** | | |
| `integer` | `int`, `uint`, `short`, `ushort` | A standard 32-bit signed/unsigned integer. Universally understood. Maps to numbers in JSON and CSV. *DBs: `INT`, `INTEGER`.* |
| `big-integer` | `long`, `ulong` | For 64-bit integers. Clearly distinct from a standard `integer`. *DBs: `BIGINT`.* |
| `decimal` | `decimal` | For fixed-point, high-precision numbers (money, etc.). Maps to a number in JSON, but often a string in CSV to preserve precision. *DBs: `DECIMAL`, `NUMERIC`.* |
| `float` | `float` (Single) | Standard single-precision floating-point number. *DBs: `FLOAT`, `REAL`.* |
| `double` | `double` | Standard double-precision floating-point number. *DBs: `DOUBLE`, `FLOAT8`.* |
| **Textual Types** | | |
| `text` | `string`, `char` | The most generic type for variable-length string data. Avoids ambiguity of `VARCHAR` vs. `TEXT` in different DBs. Maps cleanly to JSON strings and CSV fields. *DBs: `TEXT`, `NVARCHAR(MAX)`, `CLOB`.* |
| `boolean` | `bool` | The standard SQL name for a boolean type. Maps to `true`/`false` in JSON. *DBs: `BOOLEAN`, `BIT`.* |
| **Date and Time Types** | | |
| `datetime` | `DateTime` | Represents a specific point in time, usually without timezone information (or assuming local). *DBs: `DATETIME`, `TIMESTAMP WITHOUT TIME ZONE`.* |
| `timestamp` | `DateTime`, `DateTimeOffset` | A good convention for a UTC-based timestamp. This distinction is very useful. *DBs: `TIMESTAMP WITH TIME ZONE`, `DATETIMEOFFSET`.* |
| `date` | `DateOnly` | Represents only the date part. Universally supported. *DBs: `DATE`.* |
| `time` | `TimeOnly` | Represents only the time-of-day part. Universally supported. *DBs: `TIME`.* |
| **Other Data Types** | | |
| `uuid` | `Guid` | The industry-standard name for a Globally Unique Identifier. *DBs: `UUID` (MariaDB, Postgres), `UNIQUEIDENTIFIER` (SQL Server), `BINARY(16)` (MySQL).* |
| `blob` | `byte[]` | "Binary Large Object". The standard term for variable-length binary data. Would be Base64 encoded for JSON/CSV serialization. *DBs: `BLOB`, `VARBINARY(MAX)`.* |
| `json` | `string` (or a JSON object type) | For storing native JSON documents. Supported by most modern databases and directly applicable to a JSON backend. *DBs: `JSON`, `JSONB`.* |
| `xml` | `string` (or an XML object type) | For storing native XML documents. *DBs: `XML`.* |

---

## 3. Generating SQL Scripts from Models

### Purpose
For SQL-based databases, it’s essential to generate database schema creation scripts based on the metadata extracted from your models. This script ensures that your database schema aligns with the definitions in your code.

### How It’s Done in DataLinq
- **SQL Generation Factories:**
  Providers like MySQL and SQLite include classes such as `SqlFromMetadataFactory` that take a `DatabaseDefinition` object and produce a complete SQL script.
- **Script Components:**
  The generator constructs SQL commands for creating tables, indices, and constraints. It leverages the metadata information (table names, column definitions, indices) to produce the necessary DDL statements.

### Steps to Implement
1. **Develop a SQL Generator Class:**
   - Create a class similar to `SqlFromMetadataFactory` that accepts the metadata and outputs SQL commands.
   - Handle database-specific syntax, including differences in data types, quoting, and command structure. It must be able to translate from the default type system (see contract above).

2. **Integration with Metadata:**
   - Ensure that your generator reads from the same metadata produced by your new metadata factory.
   - Verify that table definitions, columns (with attributes like `NOT NULL`, `AUTO_INCREMENT`), primary keys, foreign keys, and indices are correctly translated into SQL.

3. **Output Options:**
   - Provide options for writing the script to a file or returning it as a string for further processing.

---

## 4. Reading and Writing Data to the Backend

### Purpose
Beyond schema management, the new backend must support data operations such as querying, inserting, updating, and deleting records. DataLinq abstracts these operations into a set of classes and interfaces to maintain consistency across backends.

### How It’s Done in DataLinq
- **Data Access Classes:**
  Each provider implements classes for data reading and writing. For example, MySQL has `MySqlDataLinqDataReader` and `MySqlDataLinqDataWriter` that wrap native ADO.NET objects.

- **Connection and Transaction Management:**
  Providers include classes such as `MySqlDatabase` and `MySqlDatabaseTransaction` that implement operations defined in the common interfaces (e.g., `IDatabaseProvider`, `IDataSourceAccess`).

- **Query Execution:**
  The LINQ query translator works with the backend provider to execute queries. The provider must support:
  - Executing parameterized queries.
  - Returning results as DataReaders that can be mapped to immutable objects.
  - Handling batch operations for efficient bulk fetches (such as fetching missing rows based on primary key lists).

### Steps to Implement
1. **Implement the Provider Interface:**
   - Create a new provider class (e.g., `NewBackendProvider`) that implements required interfaces like `IDatabaseProvider`.
   - Define methods for opening connections, executing queries, and managing transactions.

2. **Data Reader and Writer Classes:**
   - Develop classes that wrap the new backend’s native data reader/writer objects.
   - Ensure these classes support efficient reading (e.g., supporting the “primary key-first” approach) and writing of data.

3. **Transaction Handling:**
   - Implement a transaction class to support commit and rollback operations.
   - Make sure that updates to the Global Cache and relation caches occur only after successful commits.

4. **Error Handling and Logging:**
   - Integrate robust exception management and logging similar to what is present in the MySQL/SQLite implementations.

---

## 5. Registering the New Backend

Once the new backend classes are implemented, you must register the provider with DataLinq. This is typically done in your application’s startup code:

```csharp
// Register the new backend provider
NewBackendProvider.RegisterProvider();
```

The `RegisterProvider` method should add your provider to DataLinq’s internal registry so that it can be selected via the configuration file (e.g., in `datalinq.json`, specify `"Type": "NewBackend"`).

---

## 5. Summary

To implement a new backend in DataLinq, follow these key steps:

- **Metadata Reading:**  
  Develop a metadata factory to query system tables, map types, and construct a complete schema definition.
  
- **SQL Script Generation:**  
  Create a SQL generator class that translates metadata into DDL statements tailored to your backend’s syntax.
  
- **Data Access:**  
  Implement provider classes for opening connections, executing queries, and managing transactions. Develop data reader and writer classes to support efficient data operations.
  
- **Registration:**  
  Register your new backend so that it becomes available for configuration and use within the DataLinq framework.

This modular approach—demonstrated by the existing MySQL and SQLite implementations—ensures that new backends can be added with minimal impact on the core architecture while offering all the benefits of DataLinq’s caching, mutation, and query optimization strategies.