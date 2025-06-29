# DataLinq Technical Documentation

## 1. Overview

DataLinq is a lightweight, high-performance ORM designed primarily for read-heavy scenarios in small to medium projects. The library emphasizes immutability, efficient caching, and seamless backend integration. Its core features include:

- **Immutable Models:**  
  Models are represented as immutable objects to ensure thread-safety and minimize side effects during data reads. When updates are necessary, the system creates a mutable copy via a defined mutation workflow.
  
- **Source Generation:**  
  A source generator produces both immutable and mutable classes from abstract model definitions. This reduces boilerplate and enforces a consistent pattern across the codebase.
  
- **LINQ Integration:**  
  Queries are written using standard LINQ expressions, which are translated into backend-specific commands, allowing a unified querying experience.
  
- **Robust Caching:**  
  A multi-layered caching subsystem—including row, index, and key caches—ensures that repeated data accesses incur minimal overhead.

- **Backend Flexibility:**  
  The architecture abstracts backend details behind interfaces and adapters, enabling easy switching between data sources (e.g., MariaDB, SQLite, JSON, CSV).

---

## 2. Architecture

DataLinq is organized into several interconnected layers that work together to deliver its performance and flexibility:

- **Model Layer:**  
  Consists of abstract model classes decorated with attributes (e.g., `[Table]`, `[Column]`, `[PrimaryKey]`) that describe how classes map to database tables. These definitions are used by the source generator to create concrete immutable and mutable classes.

- **Instance Creation and Mutation:**  
  Immutable objects are created dynamically based on `RowData` provided by data readers. When mutation is required, methods like `Mutate()` generate a mutable version, which can be updated and then saved back to the backend. The mutation workflow ensures that only immutable instances are stored in caches, preserving thread-safety and performance.

- **Caching Subsystem:**  
  The caching mechanism is divided into several parts:
  - **RowCache:** Caches immutable row objects keyed by their primary keys, tracking insertion ticks and sizes for eviction based on time, row count, or memory limits.
  - **IndexCache and KeyCache:** Manage mappings between foreign keys and primary keys, and cache key instances for fast lookups.
  - **TableCache:** Aggregates the various caches for an entire table, provides methods to update or remove rows based on changes, and supports preloading indices for faster query responses.

- **Query Engine:**  
  DataLinq uses LINQ as the primary query language. LINQ expressions are parsed and translated into backend-specific SQL (or other query languages), with support for filtering, ordering, grouping, and pagination. The query system leverages caching to avoid unnecessary database round trips.

- **Backend Flexibility:**  
  DataLinq's architecture abstracts backend-specific details behind interfaces and adapter patterns. This allows developers to add new data source providers with minimal changes to the core framework.
  
- **Standardized Default Type System:**
  To enhance portability and simplify provider development, DataLinq defines a set of common, backend-agnostic "default" type names (e.g., `integer`, `text`, `uuid`). Each database provider is responsible for translating these standard types into its own native equivalents. This allows a single DataLinq model to be used to generate schemas for multiple database systems. For a complete list of default types, see the [Implementing a new backend](Implementing%20a%20new%20backend.md) guide.

- **Testing Infrastructure:**  
  The library is accompanied by a comprehensive suite of unit and integration tests. These tests verify everything from model instantiation and mutation to complex LINQ query operations and cache behavior.

---

## 3. Core Components

### 3.1 Model and Source Generation

- **Abstract Models:**  
  Developers define models using abstract classes and decorate them with attributes to specify table names, column types, and relationships. For example, the *Department* class declares properties like `DeptNo` and `Name`, and defines relations to employees and managers.
  
- **Source-Generated Classes:**  
  A source generator processes these abstract definitions to generate:
  - **Immutable classes:** Provide read-only access to data, with lazy loading of related objects.
  - **Mutable classes:** Allow modification of model properties via a `Mutate()` method, and support transactional updates.
  - **Interfaces:** Generated interfaces (e.g., `IDepartmentWithChangedName`) ensure consistency and facilitate mocking in tests.

### 3.2 Instance Management and Mutation

- **Immutable Base Class:**  
  The base class for immutable models handles:
  - Retrieving values from underlying `RowData`.
  - Lazy evaluation of properties.
  - Managing relations through helper methods that load related entities only when needed.
  
- **Mutable Wrapper:**  
  The `Mutable<T>` class encapsulates changes in a separate `MutableRowData` structure. This ensures that modifications are isolated until explicitly committed, after which a new immutable instance is generated to update the cache.

- **Factory Methods:**  
  The `InstanceFactory` provides methods to create immutable instances dynamically. Reflection is used to instantiate models based on metadata extracted from attributes.

### 3.3 Caching Mechanisms

- **RowCache:**  
  Stores immutable instances keyed by their primary keys. Tracks insertion ticks and sizes to enforce eviction policies based on time, count, or memory usage. This ensures repeated reads return cached objects without additional allocations.
  
- **IndexCache and KeyCache:**  
  - **IndexCache:** Maps foreign keys to arrays of primary keys and maintains a tick queue to remove old entries.
  - **KeyCache:** Caches key instances to prevent redundant key creation, enhancing lookup performance.
  
- **TableCache:**  
  Combines row and index caches for a given table. Handles state changes such as inserts, updates, and deletions by updating the caches accordingly. It also supports methods for preloading indices and retrieving rows with or without ordering.

### 3.4 Query Handling

- **LINQ Integration:**  
  Queries are written in LINQ, and the query engine translates them into backend-specific SQL commands. The translation layer is capable of handling various operations such as:
  - Filtering using standard where clauses.
  - Ordering, grouping, and pagination (using methods like `OrderBy`, `Skip`, and `Take`).
  - Joins and relation traversals by leveraging the relation properties defined in models.
  
- **Cache-Aware Query Execution:**  
  When a query is executed, the system first checks the cache (via `TableCache` and `RowCache`) for existing rows. If a row is missing, it retrieves the row data from the database, creates an immutable instance, and adds it to the cache.

### 3.5 Testing and Examples

- **Unit Tests:**  
  The testing suite covers all aspects of the library:
  - **Cache Tests:** Validate that duplicate rows are not created, and that eviction policies based on time, row count, and memory size work as expected.
  - **Mutation Tests:** Ensure that mutable instances correctly capture changes, can be reset, and that saving changes properly updates the backend and cache.
  - **Query Tests:** Provide extensive examples of LINQ query usage, demonstrating filtering, ordering, grouping, and handling of unsupported operations.
  
- **Integration Tests:**  
  The `DatabaseFixture` sets up real database connections (e.g., to MariaDB and SQLite) and uses generated test data (via Bogus) to ensure that the entire flow—from data retrieval and caching to mutation and query execution—operates correctly.

---

## 4. Detailed Caching Workflow

The caching subsystem is critical for achieving the zero-allocation goal in read-heavy scenarios. Here’s a closer look at the workflow:

1. **Insertion into Cache:**  
   When a new row is fetched from the database, its corresponding immutable instance is created using the `InstanceFactory`. This instance is then stored in the `RowCache` along with metadata (insertion ticks, size). Simultaneously, the `IndexCache` is updated to map foreign keys to this row’s primary key.

2. **Cache Eviction:**  
   - **Time-Based Eviction:** The system can remove rows that were inserted before a specific tick value.
   - **Row Count/Size Limits:** Methods in `RowCache` allow the cache to enforce limits by removing the oldest rows until the count or total size is within the defined thresholds.
   - **Index Cache Maintenance:** The `IndexCache` similarly purges outdated entries using its tick queue mechanism.
   
3. **Cache Retrieval:**  
   Before executing a query, the system checks the `RowCache` for the required rows. If a row is found, it’s returned directly. Otherwise, the query system retrieves the missing rows from the database and updates the cache.

4. **Transaction Awareness:**  
   The `TableCache` can maintain separate caches for transaction-specific data. This ensures that updates within a transaction do not affect the global cache until the transaction is committed.

---

## 5. Mutation and Data Consistency

DataLinq ensures data consistency while allowing mutations through a well-defined process:

1. **Immutable to Mutable Conversion:**  
   The generated `Mutate()` methods allow conversion from an immutable instance to a mutable one. This is achieved using pattern matching, ensuring the proper type is returned regardless of whether the object is already mutable or not.

2. **Tracking Changes:**  
   The `MutableRowData` class tracks modifications in a dictionary. Methods such as `Reset()` allow reverting changes to the original state, while `HasChanges()` reports whether any properties have been modified.

3. **Saving Changes:**  
   When a mutable instance is saved, the updated data is written back to the backend. Upon successful commit, a new immutable instance is created to replace the old one in the cache. Extension methods in the generated code (e.g., `Save`, `Update`, `InsertOrUpdate`) abstract these operations, providing a seamless developer experience.

---

## 6. Future Directions and Developer Notes

- **Additional Backends:**  
  Although initial support focuses on MariaDB and SQLite, the modular design facilitates easy addition of new data sources (e.g., NoSQL, JSON files).

- **Enhanced Query Optimizations:**  
  Future enhancements could include query caching, more advanced translation strategies, and support for more complex LINQ expressions.

- **Developer Contributions:**  
  Clear guidelines and extensive test coverage make it easier for contributors to understand and extend the library. Developers are encouraged to review both the generated code and supporting subsystems (caching, mutation, and query translation) for insights.

- **Documentation Updates:**  
  This technical documentation is intended to evolve with the project. Feedback from developers and contributors is welcomed to ensure that the documentation remains accurate and helpful.

---

## 7. Conclusion

DataLinq’s design centers on immutability, efficient caching, and flexible querying, making it an ideal ORM for heavy-read applications with a focus on performance. The separation of concerns between model mapping, caching, mutation, and query translation ensures that each component can be optimized independently while maintaining a consistent developer experience.