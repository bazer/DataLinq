# DataLinq Project Specification

## 1. Introduction

### 1.1 Purpose
DataLinq is designed to be a lightweight, high-performance Object-Relational Mapping (ORM) framework that leverages immutable objects and robust caching to optimize read-heavy scenarios. By focusing on minimal memory allocations and rapid data retrieval, DataLinq aims to provide a reliable and efficient solution for smaller projects and databases. Additionally, the framework is built to support multiple backend data sources—including traditional relational databases (like MariaDB and SQLite) as well as non-relational formats (such as JSON, CSV, and XML)—thus offering flexibility for diverse application requirements.

### 1.2 Scope
DataLinq encompasses:
- **Immutable Entity Management:** Utilizing a source generator, the framework automatically creates both immutable and mutable classes for each data model. Immutable objects ensure thread-safety and predictability during read operations.
- **Caching:** A dual-level caching strategy is implemented:
  - A **global cache** stores immutable entities for reuse across sessions and requests.
  - A **transaction-specific cache** holds objects undergoing updates to maintain consistency.
  - Cache invalidation is managed automatically during mutations, manually by the user, via time-based expiration, backend polling, or notifications (e.g., through Apache Kafka).
- **LINQ Integration:** The primary querying mechanism is LINQ, offering a concise and familiar interface for developers.
- **Backend Flexibility:** The design facilitates easy swapping of backend sources with minimal code changes. This is essential for projects that may need to switch data storage strategies over time.
- **Testability:** 
  - **Data Layer Testing:** DataLinq is designed to make it straightforward for developers to test the data layer of their projects by providing clear interfaces and dedicated mocking classes.
  - **ORM Test Suite:** In parallel, DataLinq itself includes a comprehensive suite of unit, integration, and benchmarking tests to ensure reliability and performance.

### 1.3 Audience
This document is primarily intended for:
- **Developers:** Who will be integrating DataLinq into their applications and need to understand its design, usage, and customization points.
- **Contributors:** Who are interested in extending or improving the framework, ensuring adherence to its architectural principles and performance targets.


## 2. Project Overview

### 2.1 Goals and Objectives
DataLinq is driven by several key objectives:
- **Performance:** Optimize read operations through aggressive caching and an immutable object model, with a target of zero memory allocations on cache hits.
- **Simplicity and Flexibility:** Provide a minimal yet powerful API modeled after Entity Framework, enabling seamless switching between different backend data sources with minimal code adjustments.
- **Extensibility:** Support various data sources including MariaDB, SQLite, JSON, CSV, and XML, thereby accommodating diverse application needs.
- **Testability:** 
  - Enable developers to easily test the data layers of their projects by offering clear interfaces and built-in mocking capabilities.
  - Maintain a robust internal test suite to validate the ORM's performance and correctness.
- **Scalability:** Cater primarily to heavy-read scenarios in small-to-medium projects while laying the groundwork for potential expansion.

### 2.2 High-Level Architecture
The architecture of DataLinq is organized around several core components:
- **Immutable Entity Model:**  
  - Data models are represented as immutable objects to ensure thread-safety and consistency.
  - Mutations are handled by converting immutable objects into new mutable instances for modification. Once updated, these changes are reflected back into the cache by generating a new immutable object.
- **Caching Mechanism:**  
  - A **global cache** holds immutable objects to maximize reusability across sessions.
  - A **transactional cache** manages updates within individual operations, ensuring that changes are consistently applied before synchronizing with the global cache.
  - Multiple cache invalidation strategies are employed, including automatic invalidation on mutation, manual refresh, timer-based expiry, backend polling, and external notifications.
- **LINQ-Based Querying:**  
  - The ORM leverages LINQ as its primary query language, providing a concise and familiar syntax for data retrieval and manipulation.
- **Backend Integration:**  
  - Initially, DataLinq supports MariaDB and SQLite.
  - Its modular design makes it straightforward to switch backends or add new ones, ensuring minimal impact on existing code when migrating between data sources.
- **Source Generation:**  
  - A source generator automates the creation of immutable and mutable classes for each model, reducing boilerplate code and enforcing consistency across the codebase.
- **Testability:**  
  - The framework is built with testability in mind, offering interfaces and mocking classes to facilitate both the testing of applications that use DataLinq and the rigorous internal testing of the ORM itself.


Below is the updated **System Architecture and Design** section incorporating your feedback:


## 3. System Architecture and Design

### 3.1 Immutable Entity Model
- **Immutable Objects:**  
  Data models in DataLinq are represented as immutable objects, ensuring thread-safety and consistency. Once created, the state of these objects cannot change.
  
- **Mutation Workflow:**  
  When an update is required, the framework provides a `Mutate()` method. This method converts the immutable object into a mutable version for modifications. After saving these changes to the backend within a transaction, a new immutable instance is generated to replace the previous version in the cache. Notably, mutable objects are only used transiently in user code and are never stored in any cache.

- **Source Generation:**  
  A source generator automates the creation of both immutable and mutable classes from abstract model classes. This minimizes boilerplate code and ensures consistency across the codebase.

### 3.2 Caching Mechanism
- **Global Cache:**  
  A static, application-wide cache holds immutable objects. These objects are shared across sessions and threads, allowing rapid access with zero memory allocations on cache hits.
  
- **Transactional Cache:**  
  During a transaction, any updated objects are read back as new immutable objects after being saved to the backend. These new immutable objects are stored in a dedicated transaction cache to maintain consistency until the transaction is complete.

- **Cache Invalidation Strategies:**  
  Cache consistency is maintained through several mechanisms:
  - **Automatic Invalidation:** Cache entries are automatically updated when objects are mutated within the library.
  - **Manual Refresh:** Developers can explicitly refresh cache entries when necessary.
  - **Time-Based Expiry:** Entries can expire based on a configurable timer.
  - **Backend Polling:** The system may poll the backend using lightweight techniques (e.g., hash or timestamp comparisons) to detect changes.
  - **Event-Driven Updates:** External notifications (such as through Apache Kafka) can trigger immediate cache invalidation upon data modifications.

### 3.3 LINQ-Based Querying
- **Primary Query Interface:**  
  DataLinq uses LINQ as its core querying language, offering a concise, expressive, and familiar syntax for data retrieval and manipulation.
  
- **Query Translation:**  
  LINQ queries are translated into the appropriate backend-specific commands, abstracting the underlying data source so that the same syntax works regardless of whether data comes from MariaDB, SQLite, or other supported formats.

### 3.4 Backend Integration and Modularity
- **Pluggable Architecture:**  
  DataLinq is designed with a modular architecture that allows developers to easily swap one backend for another with minimal code changes. Backend interactions are abstracted behind interfaces and adapter patterns.
  
- **Initial and Future Backends:**  
  While initial support is focused on MariaDB and SQLite, the architecture is readily extendable to additional data sources such as JSON, CSV, and XML.

### 3.5 Concurrency and Thread-Safety
- **Immutability Benefits:**  
  The immutable design reduces the need for complex synchronization since immutable objects can be safely shared across threads.
  
- **Thread-Safe Collections:**  
  For mutable scenarios, such as managing the transactional cache, thread-safety is ensured using locking mechanisms and thread-safe collections like `ConcurrentDictionary`.
  
- **Minimized Locking:**  
  The overall design minimizes locking by isolating mutable operations and leveraging immutable data structures, which enhances performance in concurrent environments.

### 3.6 Source Generation and Code Consistency
- **Automated Code Generation:**  
  The source generator creates both immutable and mutable classes automatically from abstract model definitions. This enforces a consistent pattern across data models and reduces the need for repetitive code.
  
- **Reduction of Boilerplate:**  
  Automating the generation of model classes allows developers to focus on business logic, leading to more maintainable and readable code.

### 3.7 Testability and Mocking
- **Clear Interfaces:**  
  The architecture is built around well-defined interfaces, making it simple to substitute real implementations with mocks during testing.
  
- **Mocking Capabilities:**  
  Dedicated mocking classes are provided, enabling developers to write comprehensive tests for their applications without needing a live backend connection.
  
- **Internal Test Suite:**  
  DataLinq includes a robust internal test suite with unit tests, integration tests, and performance benchmarks to ensure both correctness and efficiency.

### 3.8 CLI Tool and Code Generation
- **Model Class Generation:**  
  DataLinq provides a CLI tool that reads the database structure and generates abstract model classes. These abstract classes serve as the basis from which immutable and mutable classes are generated via the source generator.
  
- **Database Script Generation:**  
  The CLI tool can also generate a SQL script to create the database schema based on the model classes. This feature ensures consistency between the codebase and the actual database structure, facilitating smoother migrations and initial setups.


## 4. Functional Requirements

### 4.1 Data Access Operations
- **CRUD Support:**  
  - **Create:** Developers can insert new records by creating a new mutable instance derived from the abstract model. Once the instance is saved to the backend, a corresponding immutable object is generated and added to the global cache.
  - **Read:** LINQ serves as the primary interface for querying data. Immutable objects are fetched from the global cache when available, ensuring minimal memory allocations and rapid retrieval.
  - **Update:** Updates are initiated by calling the `Mutate()` method on an immutable object to obtain a mutable version. After modifications are saved to the backend within a transaction, a new immutable instance is created and cached.
  - **Delete:** Deletion operations remove records from the backend. Upon successful deletion, the relevant immutable object is removed from both the global and transactional caches.

- **Transaction Management:**  
  - Each operation that involves mutations takes place within a transactional context. This ensures that all updates within a transaction are managed consistently. The transactional cache holds the new immutable objects after successful backend writes until the transaction is complete.

### 4.2 Query Processing and LINQ Integration
- **LINQ-Based Queries:**  
  - Developers write queries using LINQ syntax, which is then translated into backend-specific commands (e.g., SQL for MariaDB or SQLite). This translation layer abstracts away backend details, allowing a unified querying experience.
  - Advanced query capabilities, including filtering, ordering, grouping, and joining across entities, are supported through standard LINQ expressions.

- **Query Translation Layer:**  
  - The translation component maps LINQ expressions to the specific SQL dialect or other query languages supported by the backend. This ensures that queries are both efficient and compatible with the targeted data source.

### 4.3 Data Mapping and Model Management
- **Model Class Generation:**  
  - A CLI tool is provided to generate abstract model classes by reading the database schema. These abstract classes serve as the blueprint for both immutable and mutable classes produced by the source generator.
  - This process ensures that the generated model accurately reflects the structure of the underlying database, reducing manual coding and potential errors.

- **Database Schema Generation:**  
  - The CLI tool can also generate SQL scripts that create the database schema based on the model classes. This ensures consistency between the data models in the code and the actual database structure, simplifying initial setup and migrations.

### 4.4 Caching Behavior
- **Global Cache Operations:**  
  - When a read operation is performed, the system first checks the global cache for an immutable object. If present, the object is returned immediately without additional allocations.
  - Cache misses trigger a backend query, after which the retrieved data is converted into an immutable object and stored in the global cache.

- **Transactional Cache Operations:**  
  - Updated objects within a transaction are handled by storing their newly generated immutable versions in a transactional cache. This cache isolates changes until the transaction is fully committed, after which the global cache is updated accordingly.

- **Cache Invalidation:**  
  - The system supports multiple invalidation strategies to ensure data consistency. These include:
    - **Automatic Invalidation:** Upon mutation, affected cache entries are immediately refreshed.
    - **Manual Refresh:** Developers can explicitly trigger a cache update.
    - **Time-Based Expiry:** Cache entries can be configured to expire after a set period.
    - **Backend Polling and Notifications:** Lightweight checks (via hash or timestamp) or external notifications (e.g., Apache Kafka) ensure the cache reflects the current state of the backend.

### 4.5 Backend Flexibility
- **Seamless Backend Switching:**  
  - DataLinq’s architecture abstracts backend-specific details through interfaces and adapter patterns. This allows developers to switch from one data source to another (e.g., from MariaDB to SQLite) with minimal or no changes to the application code.
  - The modular design ensures that backend-specific optimizations or query translations can be implemented independently without affecting the overall API.

### 4.6 Error Handling and Logging
- **Robust Exception Management:**  
  - All CRUD and query operations include error handling to manage scenarios like connection failures, query timeouts, or data inconsistencies.
  - Detailed logging mechanisms are integrated to capture the sequence of operations, errors, and any cache invalidation events, aiding in troubleshooting and performance tuning.


## 5. Non-Functional Requirements

### 5.1 Performance
- **Optimized Read Operations:**  
  DataLinq is designed for heavy-read scenarios, with a target of zero memory allocations when fetching immutable objects from the cache. This is achieved through aggressive caching and careful management of object creation.
  
- **Efficient Query Translation:**  
  LINQ queries are translated into backend-specific commands with minimal overhead, ensuring that query execution remains fast and efficient across different data sources.

- **Benchmarking:**  
  A suite of performance benchmarks will be maintained to measure key metrics such as query latency, cache hit rates, and overall system throughput. These benchmarks will guide ongoing optimizations and ensure that performance targets are met.

### 5.2 Scalability
- **Designed for Small-to-Medium Projects:**  
  While DataLinq is optimized for projects with smaller databases and heavy-read operations, the architecture is modular enough to be extended to larger datasets if needed.
  
- **Modular Backend Integration:**  
  The ability to switch backends with minimal code changes ensures that the system can scale horizontally by integrating with more powerful data sources or distributed systems as project demands grow.

- **Concurrent Access:**  
  The use of immutable objects and thread-safe collections minimizes the need for locks and supports high levels of concurrent access without significant performance degradation.

### 5.3 Maintainability and Extensibility
- **Automated Code Generation:**  
  The use of a source generator to create both immutable and mutable classes from abstract model definitions reduces boilerplate code, leading to a more maintainable and consistent codebase.
  
- **Clear Separation of Concerns:**  
  By abstracting backend interactions behind interfaces and adapter patterns, DataLinq allows developers to add or update components without affecting the overall system. This design simplifies future enhancements and troubleshooting.

- **Comprehensive Documentation:**  
  Detailed documentation, including this specification, usage guides, and API references, will be maintained to ensure that developers and contributors can quickly understand and work with the framework.

### 5.4 Reliability and Robustness
- **Robust Error Handling:**  
  All operations, including CRUD actions and query processing, are designed with robust exception management and logging. This ensures that failures are handled gracefully, and sufficient diagnostic information is available for troubleshooting.

- **Internal Test Suite:**  
  A comprehensive suite of unit tests, integration tests, and performance benchmarks will be continually run to ensure that any changes maintain the expected behavior and performance characteristics of DataLinq.

### 5.5 Security Considerations
- **Data Integrity:**  
  Mechanisms such as transactional caches and backend polling help maintain data consistency, reducing the risk of stale or inconsistent data being served.
  
- **Secure Access:**  
  Although DataLinq focuses primarily on read performance, care is taken to ensure that backend connections and query executions adhere to security best practices, including proper exception handling and input validation.


## 6. API Design and Query Interface

### 6.1 Overview
DataLinq’s API is designed to be both intuitive and powerful, drawing inspiration from established ORM frameworks like Entity Framework. The API is primarily built around LINQ, ensuring that developers can use a familiar and expressive syntax for data access and manipulation while benefiting from DataLinq’s high-performance caching and immutable data structures.

### 6.2 LINQ-Based Querying
- **Unified Query Syntax:**  
  Developers write queries using standard LINQ expressions. DataLinq translates these queries into the appropriate backend-specific commands (e.g., SQL for MariaDB or SQLite), abstracting the underlying complexity and allowing the same query syntax to work across different data sources.
  
- **Advanced Query Capabilities:**  
  The query interface supports advanced LINQ operations, including filtering, ordering, grouping, and joining, to cater to a wide range of data retrieval scenarios. This flexibility empowers developers to construct complex queries while keeping the code concise and readable.

- **Query Translation Layer:**  
  A dedicated translation layer interprets LINQ expressions and optimizes them for the target backend. This ensures efficient query execution and allows for backend-specific optimizations without requiring changes to the developer’s query code.

### 6.3 Fluent Interface and API Methods
- **Fluent API Design:**  
  DataLinq’s API incorporates a fluent interface for constructing queries and data operations. This design promotes readability and a natural coding style, enabling developers to chain methods together in a clear and coherent manner.
  
- **Core Methods and Operations:**  
  - **Query Initialization:** Methods for initiating LINQ queries that automatically check the global cache before executing a backend query.
  - **CRUD Operations:**  
    - **Create:** Methods to generate new mutable instances from abstract model definitions, followed by saving these instances to the backend and updating the global cache with a new immutable object.
    - **Read:** Methods that prioritize fetching immutable objects from the cache for read operations, falling back to backend queries as needed.
    - **Update:** A `Mutate()` method to create a mutable copy of an immutable object for modifications. After saving changes within a transaction, a new immutable instance is produced and stored.
    - **Delete:** Methods to remove records from the backend along with corresponding cache updates.
  - **Transaction Management:** Methods that allow developers to execute a group of operations within a transactional context, ensuring that all updates are isolated and consistent until the transaction is committed.

### 6.4 Extensibility and Backend Switching
- **Backend Abstraction:**  
  The API is designed with clear separation between the data access layer and backend-specific implementations. This is achieved through well-defined interfaces and adapter patterns, which enable developers to switch between different backends (e.g., MariaDB to SQLite) with minimal code changes.
  
- **Custom Extensions:**  
  Developers can extend the API by implementing custom adapters or overriding default behaviors. This modular design ensures that DataLinq can evolve to support additional backends and specialized query optimizations without altering the core API.

### 6.5 Integration with Testing and Mocking
- **Mocking Capabilities:**  
  To support robust testing of applications using DataLinq, the API exposes interfaces and provides dedicated mocking classes. This allows developers to simulate data layer interactions without requiring a live backend connection.
  
- **Seamless Data Layer Testing:**  
  The API is designed to facilitate the testing of data access code. Clear and consistent interfaces ensure that unit tests can easily substitute real implementations with mocks, enabling comprehensive testing of both query logic and transactional behaviors.

Below is a draft for the remaining sections of the DataLinq specification document:


## 7. Data Models and Backend Integration

### 7.1 Data Models
- **Entity Definitions:**  
  DataLinq’s data models are defined via abstract base classes that represent the schema of the underlying data. These abstract classes are used by a source generator to automatically create both immutable and mutable concrete classes.
  
- **Relationship Mapping:**  
  The framework supports various types of relationships—such as one-to-one, one-to-many, and many-to-many. These relationships are defined within the abstract models and are translated into the corresponding database relationships (foreign keys, join tables, etc.) during the model generation process.

- **Schema Synchronization:**  
  A CLI tool is provided to read the database schema and generate the corresponding abstract model classes. Conversely, the same tool can generate SQL scripts to create or update the database schema based on the current model definitions, ensuring consistency between code and database.

### 7.2 Backend Integration
- **Adapter Pattern:**  
  DataLinq abstracts backend-specific details using well-defined interfaces and adapter patterns. Each supported backend (e.g., MariaDB, SQLite) implements a common interface for CRUD operations and query execution, allowing the core framework to remain agnostic of the underlying data source.

- **Modular Integration:**  
  The modular design facilitates the easy addition of new backends. Developers can implement additional adapters for other data sources (such as JSON, CSV, or XML) without altering the main codebase. This separation of concerns ensures that backend optimizations or changes do not affect the API or core logic.

- **Configuration and Switching:**  
  Configuration options allow developers to specify the desired backend with minimal changes to the application code. The architecture is designed so that switching between supported data sources is a streamlined process.


## 8. Testing and Benchmarking Strategy

### 8.1 Testing Methodology
- **Unit Testing:**  
  Each component of DataLinq, from the immutable model generation to cache management and query translation, is covered by comprehensive unit tests. These tests validate the correctness of individual functions and modules.
  
- **Integration Testing:**  
  Integration tests are used to ensure that the various components work seamlessly together. This includes testing the end-to-end process of data retrieval, manipulation, and caching across different backends.
  
- **Data Layer Testing for Client Applications:**  
  The framework provides clear interfaces and mocking capabilities so that developers can write tests for the data layer of their own projects without needing a live backend connection.

### 8.2 Benchmarking
- **Performance Benchmarks:**  
  A dedicated suite of performance benchmarks is maintained to measure:
  - Query latency and throughput
  - Cache hit rates and memory allocation metrics (with a focus on achieving zero allocations for cache hits)
  - Transaction processing times
  
- **Continuous Integration:**  
  Benchmark tests are integrated into the CI/CD pipeline to ensure that performance regressions are caught early. Regular reporting of benchmark results helps guide ongoing optimizations.

- **Monitoring and Logging:**  
  Detailed logging mechanisms capture performance-related metrics and cache events. These logs provide insights for performance tuning and troubleshooting.


## 9. Future Enhancements and Roadmap

### 9.1 Planned Features
- **Expanded Backend Support:**  
  Future releases may include native support for additional data sources, such as NoSQL databases or distributed storage systems.
  
- **Advanced Query Optimizations:**  
  Enhancements to the LINQ query translation layer could include more sophisticated optimizations, such as query caching and dynamic query planning tailored to specific backends.
  
- **Enhanced Caching Strategies:**  
  Further improvements in caching may involve more granular invalidation policies, adaptive cache sizing, and integration with external cache providers.
  
- **Developer Tooling:**  
  Additional CLI features and graphical tools could be introduced to assist developers in model management, schema migration, and performance monitoring.

### 9.2 Roadmap and Community Involvement
- **Release Phases:**  
  The project roadmap outlines incremental release phases that focus on core functionality first, followed by performance optimizations and expanded backend integrations.
  
- **Community Contributions:**  
  DataLinq welcomes community involvement. Clear contribution guidelines, a roadmap for feature requests, and regular community updates will be provided to foster an active development community.
  
- **Documentation and Support:**  
  Ongoing efforts will be made to enhance documentation and provide comprehensive usage guides, tutorials, and API references to support both new and experienced developers.


## 10. Appendices

### 10.1 Glossary
- **ORM (Object-Relational Mapping):** A programming technique for converting data between incompatible type systems in object-oriented programming languages.
- **LINQ (Language Integrated Query):** A querying syntax integrated into .NET languages for working with data in a consistent manner.
- **Immutable Object:** An object whose state cannot be modified after it is created.
- **Mutable Object:** An object that can be modified after creation.
- **Cache:** A storage layer used to temporarily store frequently accessed data for faster retrieval.
- **Adapter Pattern:** A design pattern that allows incompatible interfaces to work together.
- **CRUD:** An acronym for Create, Read, Update, Delete—basic operations for persistent storage.

### 10.2 References
- **Entity Framework Documentation:** Provides context for LINQ-based querying and ORM design patterns.
- **Design Patterns Literature:** Sources on the adapter pattern, immutability, and caching strategies that inform DataLinq’s architecture.
- **Performance Benchmarking Tools:** Documentation for the benchmarking tools and techniques used within the project.
