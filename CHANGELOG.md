# DataLinq Changelog

All notable changes to this project will be documented in this file.

---

## [DataLinq v0.6.9 - Benchmark Evidence, Generator Hardening, and SQL Hot Paths](https://github.com/bazer/DataLinq/releases/tag/0.6.9)

**Released on:** 2026-04-28

This is a big patch release. The theme is trust: better telemetry, better benchmark evidence, sharper generator diagnostics, and lower allocation pressure in the query hot path. It is not a “everything is magically faster now” release. The honest claim is narrower and better: DataLinq now has stronger measurement infrastructure, fewer avoidable runtime allocations, and much better failure messages when model metadata is wrong.

### Highlights

* **SQL generation now allocates less on measured repeated-query paths.**  
  DataLinq now uses provider-neutral SQL parameter bindings, avoids several LINQ/string-formatting allocations during SQL rendering, and reuses bounded SQL templates for narrow repeated `SELECT` shapes such as equality predicates and fixed-slot `IN` predicates.

* **Generator and metadata diagnostics are much more actionable.**  
  Many metadata failures that previously surfaced as vague generator errors now report source-located `DLG001` / `DLG002` diagnostics on the offending attribute or model declaration.

* **Runtime observability and benchmarking grew up.**  
  The benchmark CLI now reports telemetry deltas, supports smoke/default/heavy profiles, writes machine-readable history artifacts, and feeds benchmark result pages. Runtime telemetry now covers commands, transactions, mutations, query spans, cache occupancy, and cleanup behavior.

* **Fixed MySQL/MariaDB updates against reserved-keyword columns.**  
  `UPDATE SET` now quotes column names correctly, fixing mutation failures for columns named things like `References`.

### Query, SQL, and Runtime

* Added `SqlParameterBinding` so generated SQL can carry provider-neutral parameter values until SQLite/MySQL command creation.
* Preserved explicit provider parameters for literal SQL compatibility.
* Moved provider-specific parameter materialization to the command boundary.
* Reduced SQL rendering allocations in selected-column, `ORDER BY`, `INSERT`, `WHERE`, provider-parameter, operand, and selector paths.
* Added bounded SQL template caching for simple repeated `SELECT` shapes:
  * single equality predicates
  * up to four `AND`-connected equality predicates
  * fixed-slot `IN` / `NOT IN` predicates
* Kept cached SQL templates value-safe: runtime parameter values are rebound into a fresh `Sql` instance on each use.
* Replaced result-operator string matching with typed Remotion result-operator handling for `Single`, `First`, `Last`, `Any`, and `Count`.

### Generator and Metadata

* Added source-location plumbing through metadata failures so diagnostics can point at the actual source.
* Improved diagnostics for duplicate databases, duplicate tables, duplicate columns, invalid index columns, invalid index declarations, unresolved foreign keys, unresolved relations, missing primary keys, invalid `DbRead` model references, invalid cache attributes, invalid type/interface attributes, and unsupported enum values.
* Added normalized generator model declaration inputs and structural equality so trivia-only edits do not force unnecessary generator work.
* Cached parsed metadata inside the incremental generator pipeline.
* Made default-value compatibility validation non-mutating so parsed metadata can be reused safely.
* Generated immutable model classes now expose a fast row-factory hook used before falling back to constructor expression compilation.
* Generated database models can now provide table metadata bootstrap information, reducing duplicated runtime reflection.
* Database model factory delegates are cached instead of using repeated `Activator` construction.

### Telemetry and Benchmarking

* Added standard .NET `Meter` and `ActivitySource` coverage for database commands and transactions.
* Added mutation metrics for insert, update, delete, affected rows, durations, and failures.
* Added higher-level query activities so command spans can be correlated with query execution.
* Added cache occupancy and cleanup metrics, including row counts, byte estimates, index-entry counts, cleanup operations, rows removed, and cleanup duration.
* Expanded benchmark output with normalized telemetry deltas, not just time and allocation numbers.
* Added benchmark scenarios for startup/first query, provider initialization, mutations, macro CRUD workflows, and Phase 2 / Phase 3 watchpoints.
* Added benchmark history artifacts, baseline comparisons, scheduled CI history, and documentation-site benchmark result rendering.
* Added a heavier local benchmark profile while keeping shorter CI-oriented runs practical.

### Testing, Tooling, and CI

* Removed the legacy xUnit projects after the TUnit migration and retired the old parity cutover tooling.
* Added and hardened repo-local dev/test CLI workflows for concise builds, test execution, sandbox profiles, quiet output, and Podman-backed provider tests.
* Fixed default test CLI behavior on fresh checkouts by only passing `--no-build` after an explicit build.
* Fixed Windows Podman socket transport behavior so missing images are pulled before retrying container creation.
* Fixed MySQL 8.4 test container authentication by enabling public key retrieval for non-TLS test connections.
* Hardened benchmark parsing for BenchmarkDotNet CSV output, microsecond units, MSBuild-safe job names, and profile selection.

### Documentation and Roadmap

* Documented local dev and test environment setup, including Podman/WSL and provider-backed test lanes.
* Closed out the Phase 1 benchmarking/observability and Phase 2 generator/metadata implementation plans.
* Added and closed out the Phase 3 query/runtime hot-path plan.
* Added the Phase 4 product-trust plan, with schema drift detection and conservative validation tooling as the next serious direction.
* Updated older dev-plan specs so they distinguish shipped behavior from future architecture ideas.

### Upgrade Notes

* No intentional consumer-facing breaking change is called out in this changelog.
* The stricter generator diagnostics may expose invalid metadata earlier than before. That is a good thing, but it can feel like a new failure if a model was previously limping through with ambiguous metadata.
* The SQL template cache is deliberately narrow. It targets common repeated simple query shapes, not arbitrary LINQ expression caching.
* The performance claim should be read precisely: lower allocation pressure on measured repeated-query paths. Broad wall-clock speedup claims would be overconfident.

### Full Changelog

https://github.com/bazer/DataLinq/compare/0.6.8...0.6.9

---

## [DataLinq v0.6.8 - Cache Diagnostics, Correctness, and Test Infrastructure](https://github.com/bazer/DataLinq/releases/tag/0.6.8)

**Released on:** 2026-04-17

This is a maintenance-heavy release, but it is not filler. The important work here is cache correctness and observability: a real cache-eviction bug was fixed, cache-notification cleanup is more reliable, and DataLinq now ships its first real metrics API with a hierarchy that matches the actual runtime shape. Around that, this release also starts and finishes a large test-infrastructure transition: the project moves from the old mixed xUnit setup to a TUnit-centered test architecture with new CLI and CI support, and adds a proper benchmark harness.

### Highlights

* **Fixed a real cache correctness bug where table-specific cache cleanup could evict rows from every table.**
  A table-level row limit in `RemoveRowsBySettings` was incorrectly routed through the database-wide cleanup path. In practice, that meant cache limits configured for one table could cause unnecessary evictions and extra churn in unrelated tables. That behavior is now fixed, and regression coverage was added to make sure it stays fixed.

* **Cache-notification cleanup is more robust and much easier to diagnose.**
  `CacheNotificationManager` now compacts dead weak subscribers correctly again in read-heavy workloads, and `Notify()` / `Clean()` were tightened so they do not race each other and lose invalidations. DataLinq also now exposes live notification telemetry such as queue depth, sweep sizes, dropped dead subscribers, and peak queue growth.

* **DataLinq now ships a real hierarchical runtime metrics API.**
  `DataLinqMetrics` is new in this release and reports:
  * runtime totals
  * per-provider-instance metrics
  * per-table metrics within each provider

  That matters because query execution, row-cache behavior, relation loading, and cache-notification churn do not belong to the same scope. The shipped API avoids misleading aggregation and makes multi-provider diagnostics much more trustworthy from day one.

### Runtime Fixes and Observability

* Added a new public diagnostics surface under `DataLinq.Diagnostics.DataLinqMetrics`, including typed snapshot models for runtime, provider, table, query, relation, row-cache, and cache-notification metrics.
* Scoped cache-notification telemetry by provider instance and table so multiple loaded providers with the same logical database name are tracked independently.
* Added stable provider telemetry instance ids so aggregation does not collapse unrelated provider instances together.
* Added live and cumulative cache-notification metrics, including current queue depth, last notify/clean sweep values, sweep totals, dropped dead references, busy clean skips, and approximate peak queue depth.
* Added a dedicated diagnostics and metrics documentation page that explains how to interpret the new hierarchy and which values are counters, gauges, sums, or maxima, without pretending there was an earlier released flat metrics API.
* Fixed a `ThreadWorker` teardown race during fast disposal, improving shutdown reliability in scenarios that rapidly create and dispose providers.

### Benchmarking, Testing, and Tooling

* This release contains a full test suite migration from xUnit to TUnit. The project moved to a TUnit-centered structure across unit, compliance, and generator coverage.
* Added a cross-platform `DataLinq.Testing.CLI` workflow for bringing test infrastructure up/down, waiting, resetting, running suites, listing targets, and validating legacy-to-TUnit parity.
* Moved the test suite completely to Podman containers, with support for all the current LTS versions of MySQL and MariaDB. 
* Added a parity gate so legacy xUnit coverage cannot silently disappear during the migration.
* Cleaned up the test structure and provider matrix so the suite is easier to reason about and less dependent on ad hoc local scripts.
* Added real CI for the project, including a main automated lane plus broader matrix coverage, instead of relying on purely local validation.
* Built that CI around the new testing workflow with more resilient teardown, dedicated MySQL/MariaDB coverage, and machine-readable summaries for badges and reporting.
* Replaced the old benchmark stub with a real BenchmarkDotNet harness, including deterministic SQLite-backed employee benchmarks for cold/warm primary-key fetches and relation traversal.

### Documentation and Maintenance

* Added first-class documentation for diagnostics and metrics, and linked it from the README, site index, and usage docs so it is actually discoverable.
* Reorganized development-plan docs and refined roadmap/async planning material.
* Refreshed NuGet dependencies across the solution.

### Full Changelog

https://github.com/bazer/DataLinq/compare/0.6.7...0.6.8

---

## [DataLinq v0.6.7 - Generator Reliability, Default Handling, and Release Tooling](https://github.com/bazer/DataLinq/releases/tag/0.6.7)

**Released on:** 2026-03-27

This release is mostly about correctness and maintainability, and that is exactly what it needed to be. The biggest themes are a cleaner source-generator pipeline, much better handling of default values across providers, several SQLite and MySQL/MariaDB correctness fixes, a large documentation overhaul, and a far more practical local NuGet publishing workflow.

### Highlights

* **Replaced the old SGF-based generator pipeline with a native Roslyn incremental generator.**
  This is the most important internal change in the release. It reduces moving parts, aligns the generator with the platform it actually runs on, and gives DataLinq a more stable foundation for future analyzer and generation work.

* **Default value handling is significantly more correct across generation, metadata parsing, and SQL output.**
  A large portion of this release fixes subtle but important bugs around default values:
  * generated models now preserve source defaults more accurately, including overridden property types
  * default literal escaping has been fixed in generated models
  * MySQL, MariaDB, and SQLite now parse and emit default values more reliably
  * typed default compatibility is validated more aggressively during generation

* **SQLite behavior is more consistent and less fragile.**
  This release fixes several SQLite-specific issues:
  * in-memory database lifetime and test isolation were improved
  * `Guid` parameter matching for `TEXT` columns was corrected
  * millisecond precision handling was aligned more closely with .NET `DateTime` behavior
  * SQLite default value parsing and SQL generation were expanded and tightened up

* **MySQL and MariaDB SQL/default handling got a substantial correctness pass.**
  Multiple fixes in this release address quoted defaults, typed model properties, date defaults, enum defaults, view parsing fallback behavior, and SQL generation for provider-specific edge cases.

### LINQ and Query Fixes

* Fixed LINQ `char` equality translation across SQLite, MySQL, and MariaDB.
* Corrected several provider-level query and metadata edge cases that were previously easy to miss but could produce the wrong SQL or incorrect defaults.

### Source Generator and Analyzer Improvements

* Added analyzer release tracking for `DLG000`.
* Improved validation for model default values.
* Tightened generator test coverage around defaults, syntax parsing, and model generation behavior.
* Fixed transitive Roslyn/source-generator packaging issues so the NuGet experience is more reliable in Visual Studio and downstream projects.

### Packaging and Tooling

* Added a new local `publish-nuget.ps1` release script for packing and publishing public packages.
* The script now stages release artifacts in a fresh folder, prompts for the NuGet API key at publish time, and publishes packages and symbol packages explicitly.
* Fixed `DataLinq` symbol packaging so `.snupkg` files actually contain real PDBs and can be published successfully.
* Improved the local release flow for `DataLinq`, `DataLinq.SQLite`, `DataLinq.MySql`, `DataLinq.CLI`, and `DataLinq.Tools`.

### Documentation

* Performed a broad documentation overhaul and website restructuring.
* Added or substantially improved docs for:
  * installation and getting started
  * configuration and model generation
  * CLI usage
  * LINQ query support
  * transactions
  * troubleshooting
  * backend-specific behavior for SQLite and MySQL/MariaDB
* Fixed docfx homepage routing and cleaned up the site structure.

### Full Changelog

https://github.com/bazer/DataLinq/compare/0.6.6...0.6.7


---

## [DataLinq v0.6.6 - Performance Improvements and SQLite Logging](https://github.com/bazer/DataLinq/releases/tag/0.6.6)

**Released on:** 2025-12-18

This maintenance release improves core query and materialization performance, reduces memory overhead in hot paths, and makes SQLite logging behavior more consistent. It also includes a dependency refresh, source generator cleanup, and a large set of internal planning documents for upcoming releases.

### Highlights

*   **Faster Primary-Key Lookups and LINQ Execution:** Several internal optimizations make common read scenarios faster and cheaper. [31fd6f3]
    *   `Select` can now detect simple primary-key predicates and short-circuit to a direct lookup instead of building and executing a full query.
    *   Expression evaluation now uses a reflection-based fast path for local variable access, avoiding unnecessary lambda compilation in many cases.
    *   `QueryExecutor` now caches standard identity projection delegates to reduce repeated overhead for common queries.
*   **Lower-Overhead `RowData` Storage:** `RowData` has been redesigned to use an indexed object array instead of a dictionary, giving O(1) column access and reducing allocations during row materialization. [2ce6b41]
    *   This change is supported by new column indexing metadata assigned during metadata parsing, ensuring correct alignment even for partial `SELECT` queries.
*   **Improved SQLite Logging Integration:** Logging configuration is now propagated more consistently through SQLite database and transaction classes, improving diagnostics and making SQLite behavior more aligned with the other providers. [db25a31] [07fc896]

### Internal Improvements

*   **Source Generator Cleanup:** Refactored `ModelGenerator` to streamline class declaration processing and improve metadata caching in the generator pipeline. [3300f0f]
*   **Dependency Refresh:** Updated NuGet package dependencies across the runtime, generator, tooling, benchmark, and test projects to newer stable versions. [d16ae98]
*   **Minor Code Cleanup:** Removed unused `using` directives and small bits of dead code as part of the performance work. [ba088a7]

### Documentation & Planning

*   Added a substantial new set of development-plan documents covering batched mutations and optimistic concurrency, in-memory provider support, JSON data type support, metadata architecture, migrations and validation, performance benchmarking, projections and views, query pipeline abstraction, source generator optimizations, SQL generation optimization, result-set caching, testing infrastructure, and recommended application patterns. [59f3de0] [2242bc3] [c2f3547] [ef00311]
*   Updated the documentation workflow to use .NET 10 for static site generation. [ebf70a7]

---

**Full Changelog**: https://github.com/bazer/DataLinq/compare/0.6.5...0.6.6

---

## [DataLinq v0.6.5 - LINQ Enhancements & Multi-Targeting](https://github.com/bazer/DataLinq/releases/tag/0.6.5)

**Released on:** 2025-11-12

This release expands framework support to include .NET 8, 9, and 10, introduces significant improvements to the LINQ query parser for string manipulation and collection handling, and includes internal optimizations for newer .NET runtimes.

### Highlights

*   **Multi-Targeting Support:** DataLinq now explicitly targets **.NET 8.0, .NET 9.0, and .NET 10.0**.
*   **Performance Optimization on .NET 9+:** Implemented conditional compilation to utilize the new `System.Threading.Lock` on .NET 9 and greater, improving thread synchronization performance in `ImmutableRelation` and `ImmutableForeignKey`.
*   **Advanced LINQ Chains:** Added support for chained string functions in queries. You can now write LINQ expressions like `x.Name.Trim().Length`, and they will correctly translate to the corresponding SQL.

### LINQ & Query Engine

*   **Chained String Functions:** The `QueryBuilder` now supports parsing and generating SQL for chained string operations (e.g., `Trim().ToUpper().Length`).
*   **Enhanced Collection Handling:** Improved translation logic for `Contains` and `Any` methods.
    *   Added robust handling for empty lists in `Contains` queries (resolving to `1=0` or `1=1`).
    *   Fixed handling of negated `Contains` conditions.
    *   Added support for `op_Implicit` calls wrapping arrays or spans within queries.
*   **Entity Selection:** Improved `QueryExecutor` to handle selecting the full entity directly in projections (e.g., `source.Select(x => x)`).
*   **Expression Evaluation:** Updated the `Evaluator` to safely handle non-reducible expressions (like `QuerySourceReferenceExpression`), preventing runtime errors during partial evaluation of query trees.

### Bug Fixes & Internal Improvements

*   **Dependency Update:** Updated `ThrowAway` package to version 0.3.1 and updated various test dependencies (Bogus, xUnit, etc.).
*   **SQL Generation:** Added argument validation for `Substring` functions in SQL providers to ensure correct usage.
*   **Test Suite Reliability:** Adjusted transaction tests to correctly account for isolation level differences between SQLite (which may expose uncommitted writes) and MySQL/MariaDB.
*   **Type Safety:** Added specific handling to skip unsupported tests in MariaDB relating to GUID formats until upstream connector support is clarified.

---

**Full Changelog**: https://github.com/bazer/DataLinq/compare/0.6.4...0.6.5

---

## [DataLinq v0.6.4 - Critical Concurrency & Performance Fixes](https://github.com/bazer/DataLinq/releases/tag/0.6.4)

**Released on:** 2025-08-26

This is a high-priority release that resolves critical performance and stability issues related to the relation caching system under high thread contention. It introduces a more robust, leak-free, and highly performant pattern for handling cache invalidation notifications.

### 🚀 Highlights

*   **Fixed Critical Threading & Performance Issue:** A major bug has been fixed where applications with a high number of loaded relations and concurrent threads could experience severe performance degradation or hangs.
    *   The `CacheNotificationManager` has been re-engineered to use a "fire-and-forget" pattern with a `ConcurrentQueue`. This makes the `Subscribe` operation a lock-free, O(1) action, drastically improving performance in scenarios with many relation accesses.
    *   The `ImmutableRelation` and `ImmutableForeignKey` classes have been hardened with a robust double-checked locking pattern using `volatile`, ensuring that lazy-loaded data is fetched only once and is safe from race conditions, while keeping the "hot path" for accessing already-loaded data lock-free and extremely fast.

### 🐛 Bug Fixes & Internal Improvements

*   **Resolved High-Contention Concurrency Bugs:** Replaced the previous cache notification logic with a new, more robust implementation to prevent thread starvation and potential hangs. This completely overhauls the internal mechanics of relation cache invalidation for better performance and stability. [c1f7380]
*   **Fixed Test Suite Initialization:** Corrected a bug in the `DatabaseFixture` that could prevent test databases from being set up correctly in certain configurations. [8a1c76c]

---

**Full Changelog**: https://github.com/bazer/DataLinq/compare/0.6.3...0.6.4

---

## [DataLinq v0.6.3 - Improved Key Handling and Robustness](https://github.com/bazer/DataLinq/releases/tag/0.6.3)

**Released on:** 2025-08-17

This release focuses on improving the internal robustness of the core data access logic by addressing a key deficiency in index handling and providing a more resilient and correct foundation for future development.

### 🚀 Highlights

*   **Enhanced Key Generation and Indexing:** Improved handling of key types in the index. The `KeyFactory` has been refactored to correctly generate and compare primary keys for all supported data types. This fixes a potential issue where indexes on specific column types (`byte[]`, enums, and other value types) might lead to incorrect results or stability problems.

### 🐛 Bug Fixes

*   **Fixed Incorrect Indexing with `byte[]` and Enums:** Resolved a critical bug in the `KeyFactory` where the comparison logic for `byte[]` and enum-based primary keys did not correctly consider the *content* of the `byte[]` data or the enum value. This could lead to incorrect lookups in caches and index maintenance, especially for the indexes that reference those columns. [91a8eaae]

---

**Full Changelog**: https://github.com/bazer/DataLinq/compare/0.6.2...0.6.3

---

## [DataLinq v0.6.2 - Performance, Stability, and LINQ Fixes](https://github.com/bazer/DataLinq/releases/tag/0.6.2)

**Released on:** 2025-08-17

This is a focused maintenance release that addresses critical performance issues under high thread contention, improves the correctness of the code generation CLI, and adds a commonly requested feature to the LINQ provider.

### 🚀 Highlights

*   **Fixed Critical Performance Issue in Cache Notifications:** Resolved a thread starvation bug in the `CacheNotificationManager` that occurred under high-contention scenarios (many threads, thousands of relations). The previous lock-free `Subscribe` method has been replaced with a more robust and efficient lock-based write and lock-free read pattern, eliminating excessive CPU usage and potential application hangs. [11323da]
*   **Implemented `string.Length` in LINQ Queries:** Added support for using the `.Length` property on string columns within LINQ `WHERE` clauses. [ff3d480]

### 🐛 Bug Fixes & Improvements

*   **Corrected Nullable Reference Type Generation in CLI:** The `datalinq create-models` command now correctly respects the `"UseNullableReferenceTypes": false` setting in `datalinq.json`. It will no longer incorrectly add a `?` to nullable reference types (like `string`) when the feature is disabled, ensuring the generated abstract models are correct for non-NRT projects. [d122ed9]
*   **Improved CLI Help Text:** Corrected a misleading error message in the CLI that suggested an unimplemented `--all` option, preventing user confusion. [a26a8f5]

---

**Full Changelog**: https://github.com/bazer/DataLinq/compare/0.6.1...0.6.2

---

## [DataLinq v0.6.1 - Stability and Code Generation Fixes](https://github.com/bazer/DataLinq/releases/tag/0.6.1)

**Released on:** 2025-08-04

This is a maintenance release that focuses on improving the correctness and robustness of the metadata parsing and source generation engines. It resolves critical bugs related to recursive table relationships and properties that serve as both a primary and foreign key. It also cleans up all known C# compiler warnings in the generated model code for a smoother developer experience.

### 🐛 Bug Fixes

*   **Fixed Recursive Relation Parsing:** A critical bug was fixed where a table with a self-referencing foreign key (e.g., an `employee` table with a `manager_id`) would cause the metadata parser to generate incorrect and duplicate relation properties. [d33c7bc]
    *   The relation parsing logic in the `MetadataFactory` has been refactored to be direction-aware. It now correctly generates one single-entity property for the "many-to-one" side (e.g., `Manager`) and one collection property for the "one-to-many" side (e.g., `Subordinates`) with the appropriate `IImmutableRelation<T>` type.
    *   This fixes crashes in both the `datalinq create-models` command and the source generator when encountering this common database pattern.
*   **Corrected `required` Members for Primary Keys that are also Foreign Keys:** Fixed a bug in the source generator where a column that was both a `[PrimaryKey]` and a `[ForeignKey]` was incorrectly omitted from the required members' constructor in mutable classes. The generator now correctly identifies these properties as required. [dda5e8e]

### 🛠️ Code Generation & Developer Experience Improvements

*   **Resolved All Known Nullability Warnings in Generated Code:**
    *   Fixed `CS8603` ('Possible null reference return') for non-nullable relation properties (e.g., `public override Employee employees`) by correctly applying the null-forgiving operator (`!`) only when nullable reference types are enabled and the property is non-nullable. [8efe6b1]
    *   Fixed `CS8618` warnings in the getters of `required` properties by using the null-forgiving operator on the `GetValue(...)` cast, assuring the compiler that the value will not be null after construction. [00af93d]
    *   Suppressed `CS8618` warnings ('Non-nullable property must contain a non-null value') in mutable constructors that take an immutable object, acknowledging that the base constructor correctly initializes all required members. [8efe6b1]
*   **Resolved Member Hiding Warnings:** Added the `new` keyword to generated properties (like `IsDeleted`) that intentionally hide methods from the `Mutable<T>` base class, resolving `CS0108` compiler warnings. [8efe6b1]
*   **Improved Nullability Metadata Parsing:** The logic for parsing relation properties from source files (`SyntaxParser`) now correctly detects and stores whether a relation is declared as nullable (e.g., `public abstract Employee? Manager`). [8efe6b1]

---

**Full Changelog**: https://github.com/bazer/DataLinq/compare/0.6.0...0.6.1

---

## [DataLinq v0.6.0 - Powerful Queries, Dedicated Backends](https://github.com/bazer/DataLinq/releases/tag/0.6.0)

**Released on:** 2025-07-29

This is a major feature release that significantly enhances the power and expressiveness of the LINQ provider, introduces dedicated first-class support for both MariaDB and MySQL, improves the developer experience with more convenient data access methods, and includes a deep refactoring of the query generation engine for greater stability and extensibility.

### 🚀 Highlights

*   **Dedicated MariaDB & MySQL Providers:** The previously unified MySQL provider has been split into two distinct, first-class providers. This major architectural change ensures more accurate, dialect-specific SQL generation and enables dedicated support for features unique to each database, like MariaDB's native `UUID` type.
*   **Massively Expanded LINQ `WHERE` Clause Support:** The LINQ provider is now dramatically more powerful, with support for many common, real-world query patterns:
    *   **Member-to-Member Comparisons:** You can now write queries that compare two columns directly (e.g., `where x.ShippedDate > x.OrderDate`).
    *   **Full Date & Time Property Support:** You can now use all properties of `DateTime`, `DateOnly`, and `TimeOnly` in your queries (e.g., `where x.CreatedAt.Year == 2025` or `where x.LoginTime.Hour < 9`).
    *   **String Function Support:** Added support for common string functions like `.ToUpper()`, `.ToLower()`, `.Trim()`, and `string.IsNullOrEmpty()`.
*   **Major LINQ Provider Refactoring:** The core query translation logic has been completely refactored from a monolithic `WhereVisitor` into a new, cleaner `QueryBuilder` class. This makes the code more robust, easier to maintain, and significantly simplifies the process of adding new LINQ features in the future.
*   **Source-Generated Static `Get` Methods:** Models now have a source-generated static `Get()` method, allowing for a much cleaner and more discoverable way to fetch single entities by their primary key (e.g., `Employee.Get(123, transaction)`).

### ✨ Features & Enhancements

*   **Backend Improvements:**
    *   **Dedicated MariaDB & MySQL Support:** The single MySQL provider has been split to provide dedicated, robust support for both databases. This resolves previous inconsistencies and allows for better, dialect-specific feature implementation and testing.
    *   **MariaDB Native UUID Support:** The new MariaDB provider correctly parses and generates the native `UUID` data type for MariaDB 10.7+.
*   **New Data Access Methods:**
    *   A static, source-generated `Get(primaryKey, IDataSourceAccess)` method is now available on all table models for direct and efficient entity retrieval.
    *   `Transaction<T>` now has a `Get<M>(primaryKey)` method for easily fetching entities within a transaction's scope.
*   **Expanded LINQ Functionality:**
    *   **Date/Time Properties:** Full support for `.Year`, `.Month`, `.Day`, `.DayOfYear`, `.DayOfWeek`, `.Hour`, `.Minute`, `.Second`, and `.Millisecond` in `WHERE` clauses.
    *   **String Functions:** Support for `.ToUpper()`, `.ToLower()`, `.Trim()`, `.Substring()`, `string.IsNullOrEmpty()`, and `string.IsNullOrWhiteSpace()` in `WHERE` clauses.
*   **Corrected Nullability Logic:**
    *   **Nullable Booleans:** The logic for handling nullable boolean comparisons (`x.IsDeleted != true`) has been completely fixed to correctly include `NULL` values, matching C# semantics.
    *   **Default Values:** Fixed a regression where properties with a `[DefaultValue]` attribute were incorrectly generated as non-nullable. They are now correctly nullable.
*   **Default UUID Values:** Added support for `[DefaultNewUUID]` attribute for models, which translates to `UUID()` in generated SQL for MySQL/MariaDB.

### 🛠️ Refactoring & Architectural Changes

*   **`WhereVisitor` to `QueryBuilder`:** The complex logic for parsing LINQ `WHERE` clauses has been moved from the `WhereVisitor` into a new, dedicated `QueryBuilder` class. This separates the concerns of expression tree traversal from SQL construction, improving code clarity and stability.
*   **Instance Creation Refactoring:** The internal logic for creating model instances now relies on `IRowData` and `IDataSourceAccess` interfaces, improving testability and architectural consistency.

### 🐛 Bug Fixes

*   Fixed a bug where relations for newly created models were not being added correctly during metadata transformation.
*   Fixed incorrect casing for property names when using the `CapitaliseNames` option, especially for relation properties.
*   Resolved an issue where `Transaction<T>.Commit()` returned a non-generic `Transaction`, which has been corrected to return `Transaction<T>`.

---

**Full Changelog**: https://github.com/bazer/DataLinq/compare/0.5.4...0.6.0

---

## [DataLinq v0.5.4 - Critical Memory Leak Fixes & Schema Generation Improvements](https://github.com/bazer/DataLinq/releases/tag/0.5.4)

**Released on:** 2025-06-11

This is a high-priority maintenance and enhancement release focused on resolving critical memory leaks, improving the fidelity of the schema-to-model generation process, and giving developers more control over their code generation workflow.

### 🚀 Highlights

*   **Fixed Two Critical Memory Leaks:** Identified and resolved two separate memory leaks related to cache clearing and event handling, drastically improving long-term stability and performance for applications under load.
*   **Greatly Improved Database-to-C# Type Mapping:** The `create-models` CLI tool is now much smarter, correctly generating unsigned types (`uint`, `ulong`, `byte`, etc.) and appropriately sized integer types (`short`, `long`) from MySQL/MariaDB schemas.
*   **New `--overwrite-types` CLI Flag:** Added a new powerful option to the `create-models` command that allows developers to force the regeneration of C# property types directly from the database schema, perfect for a "schema-first" workflow.

---

### 🐛 Critical Bug Fixes

*   **Fixed Major Memory Leak in Relation Caching:**
    *   The previous event handling system for cache notifications (`WeakEventManager`) had a subtle flaw that prevented `ImmutableRelation` and `ImmutableForeignKey` objects from being garbage collected. This could lead to significant memory consumption over time.
    *   **Solution:** The `WeakEventManager` has been completely replaced with a new, highly performant, lock-free `CacheNotificationManager`. This new system uses a custom array-swapping pattern with `Interlocked.CompareExchange` to ensure thread-safety and memory-safety without the overhead of reflection or the risks of finalizers, completely curing the leak.
*   **Fixed Memory Leak in Index Cache:**
    *   A bug was discovered where calling `TableCache.ClearCache()` would not fully clear the `IndexCache`. The reverse mapping dictionary (`primaryKeysToForeignKeys`) was not being cleared, causing it to grow indefinitely. This has been fixed.
*   **Corrected Nullability for Generated Properties:**
    *   Auto-incrementing primary key columns are now correctly generated with nullable C# types (e.g., `int?`) to reflect their `null` state before an entity is inserted into the database.
    *   Columns with a `DEFAULT` value in the database are also now correctly generated as nullable, as the value is optional on insert.

### ✨ Features & Enhancements

*   **Enhanced Schema-to-Model Type Mapping:**
    *   The `MetadataFromMySqlFactory` now correctly maps database integer types to their corresponding C# types based on size and `UNSIGNED` flags. This improves type safety and correctness when generating models from a MySQL/MariaDB database. The new mappings include:
        | MySQL Type | C# Type |
        | :--- | :--- |
        | `TINYINT UNSIGNED` | `byte` |
        | `TINYINT` | `sbyte` |
        | `SMALLINT UNSIGNED` | `ushort` |
        | `SMALLINT` | `short` |
        | `INT UNSIGNED` | `uint` |
        | `INT` | `int` |
        | `BIGINT UNSIGNED` | `ulong` |
        | `BIGINT` | `long` |
*   **New `--overwrite-types` CLI Flag:**
    *   The `datalinq create-models` command now accepts an `--overwrite-types` flag.
    *   **Default Behavior:** DataLinq preserves user-defined types in source code (like custom classes or `enum`s) even if the underlying database column is a primitive type.
    *   **New Behavior:** When `--overwrite-types` is used, DataLinq will force C# property types to be updated based on the schema from the database. This is ideal for when you change a column type in the database (e.g., from `INT` to `BIGINT`) and want your C# model to automatically update. This override intelligently preserves user-defined `enum` types and other custom classes.
*   **Improved CLI Filtering Logic:**
    *   Unified the `Tables` and `Views` configuration lists in `datalinq.json` into a single, more intuitive `Include` list. If the list is empty or omitted, all tables and views are included. Otherwise, only the specified items are included.

### 🛠️ Internal Improvements & Testing

*   **Improved Test Isolation:** Created new, dedicated test fixtures (`MySqlFilteringTestFixture`, `MySqlTypeMappingFixture`) that create temporary databases. This ensures that tests for metadata parsing are fully isolated, faster, and more reliable.
*   **Comprehensive Test Coverage:** Added a full suite of unit tests for the new type mapping logic, CLI filtering behavior, and the `--overwrite-types` feature to prevent future regressions.

---

**Full Changelog**: https://github.com/bazer/DataLinq/compare/0.5.3...0.5.4

---

## [DataLinq v0.5.3 - Maintenance & LINQ Enhancements](https://github.com/bazer/DataLinq/releases/tag/0.5.3)

**Released on:** 2025-06-04

This release focuses on significant improvements to stability, performance, and LINQ query translation capabilities. Key highlights include a fix for potential memory leaks related to event handling, optimizations for the event system, and more robust parsing for complex LINQ queries.

### 🚀 Enhancements & Optimizations

*   **More Robust LINQ Query Parsing (7a7d00d, e57c59e, 770b98a):**
    *   Enhanced the LINQ query translator to better handle more complex scenarios involving `Any()` and `Contains()` methods, including cases with empty lists or conversions within predicates.
    *   Implemented support for LINQ queries using `Enumerable.Any()` (with and without predicates).
    *   Implemented support for LINQ queries using `Contains()` on empty lists, ensuring correct SQL generation (`1=0` or `1=1`).
*   **Improved SQL Parentheses Handling (a9e3fdd):** Refined the SQL generation logic to produce cleaner queries with more accurate and less redundant parentheses, particularly for complex `WHERE` clauses involving multiple `AND`/`OR` groups.

### 🐛 Bug Fixes

*   **Memory Leak Prevention (eff26a3):** Switched from standard `EventHandler` usage to a custom `WeakEventManager` for internal cache update notifications (`TableCache.RowChanged`). This resolves potential memory leaks where `ImmutableRelation` and `ImmutableForeignKey` instances could be kept alive indefinitely by the `TableCache` due to strong event handler references.
*   **Flaky Test Stabilization (f60d1e8):** Several unit tests that were sensitive to overall table row counts or execution order have been rewritten to use smaller, controlled data subsets, improving test reliability and isolation.
*   **JOIN and WHERE Clause Separation (aee298c):** Fixed a bug where a `WHERE` clause condition could sometimes be incorrectly appended to the `ON` clause of a preceding `JOIN` statement. `WHERE` conditions now correctly target the main `WHERE` clause. This also involved a slight internal syntax adjustment for defining `ON` conditions for joins.

### 🛠️ Other Changes

*   **Updated NuGet Packages (411e282):** General update of project dependencies to their latest stable versions.
*   **Test Suite Expansion (eff26a3, 4688435):**
    *   Added new unit tests specifically for the `WeakEventManager` to verify its correctness, weak referencing behavior, and thread-safety.
    *   Expanded the test suite with more cases covering boolean logic in LINQ queries.
    *   Improved test cleanup procedures.
*   **Developer Tooling & Documentation (b1130c3, 74df395, 773a51c, da972e9):**
    *   Updated Copilot/AI assistant instructions with current project status and learnings.
    *   Added configuration for the `repomix` tool.
    *   Updated `.editorconfig` for consistent coding style and added spelling configurations.

---

**Special Thanks:**
A big thank you to our AI assistant Gemini for their help in diagnosing and resolving complex issues related to event management and LINQ query translation! (This comment was inserted by Gemini itself :)

---

**Full Changelog**: https://github.com/bazer/DataLinq/compare/0.5.2...0.5.3

---

## [DataLinq v0.5.2 - Build System Overhaul, Stability, and Core Refinements](https://github.com/bazer/DataLinq/releases/tag/0.5.2)

**Released on:** 2025-05-19

This release brings significant improvements to the DataLinq build system, NuGet packaging, and internal stability. It addresses several complexities encountered with cross-targeting, source generator dependencies, and test reliability, laying a more robust foundation for future development.

### 🚀 Key Changes & Improvements

*   **Build System & NuGet Packaging Overhaul (646cb79, 5a8dcbf, ef81b56):**
    *   Resolved a complex issue in the .NET build system by **including the `DataLinq.SharedCore` project's source files directly into the main `DataLinq` and `DataLinq.Generators` projects.** This eliminates a separate `DataLinq.Core.dll` and resolves related assembly loading problems, particularly for the source generator at design time and runtime.
    *   **Explicitly packaged the `ThrowAway.dll` dependency alongside the `DataLinq.Generators.dll`** within the NuGet package's `analyzers` directory. This ensures the source generator has its necessary runtime dependencies available when consumed in other projects.
    *   These changes aim to create a more stable and self-contained NuGet package, simplifying consumption and reducing `FileNotFoundException` issues for generator dependencies.
*   **Cache Invalidation Refinement (51bdaec):** Ensured that `TableCache.ClearCache()` now correctly calls `OnRowChanged`. This is crucial for propagating cache clear events to dependent caches, such as those within `ImmutableRelation` instances, ensuring they are also cleared and re-fetch data when the main table cache is cleared.
*   **Improved Error Handling (a601f5c, 43fde44, f869128):**
    *   Enhanced error handling mechanisms when reading the `datalinq.json` configuration file, providing clearer feedback on parsing issues.
    *   Refactored internal error handling, particularly within metadata parsing factories, by more consistently using the `Option<T, DLOptionFailure>` pattern with `CatchAll` for better exception management and failure reporting.

### 🛠️ Other Fixes & Updates

*   **Test Suite Enhancements & Fixes (85e069a, 0885096, b2941ca, df82d58, e350a03, e1230cf):**
    *   Addressed several failing tests, particularly within `MetadataTransformerTests`.
    *   Fixed various bugs and improved test reliability for `MetadataFromMySqlFactory` and `MetadataFromSQLiteFactory`.
    *   Corrected issues in metadata parsing tests.
*   **Build & Development Environment (0472ba9, acdf7de, ff83cc8):**
    *   Added a PowerShell script (`clean.ps1`) to properly clean the solution folder of `bin` and `obj` directories.
    *   Fixed Visual Studio Code configuration for building and running tests.
    *   Added a `.code-workspace` file for Visual Studio Code.
*   **Dependency Management (4bf4a94, f24eed6):**
    *   Removed several NuGet package dependencies that were no longer required.
    *   Updated the `Bogus` package (used for test data generation) to version 35.6.3.
*   **Documentation & Internal Structure (549bd3d, 8da20b6):**
    *   Initiated work on adding architectural diagrams (Mermaid).
    *   Added initial documentation for querying capabilities.
    *   Includes various merges and internal refactorings.

---

**Why these changes were important:**
The v0.5.1 release cycle highlighted some fundamental challenges with packaging a .NET library that includes both a runtime component and a Roslyn Source Generator with its own dependencies. Version 0.5.2 directly tackles these issues by restructuring how core components are compiled and packaged, aiming for a "just works" experience for consumers of the `DataLinq` NuGet package. The improvements to cache invalidation and error handling further enhance the library's robustness.

**Full Changelog**: https://github.com/bazer/DataLinq/compare/0.5.1...0.5.2

---

## [DataLinq v0.5.1 Release Notes](https://github.com/bazer/DataLinq/releases/tag/0.5.1)

**Released on:** 2025-04-11

This release primarily focuses on critical fixes related to object equality, collection handling, source generator compatibility, and internal refactoring for improved robustness. It addresses key issues identified after the major v0.5.0 release.

**🚀 Highlights & Fixes:**

*   **Corrected Object Equality (`Equals` & `GetHashCode`):**
    *   **Breaking Change (Behavioral):** The core equality comparison for entity instances (`Immutable` and `Mutable`) has been changed. Instead of comparing *all* property values, equality is now correctly based **solely on the Primary Key(s)**. This aligns with standard ORM identity practices.
    *   Two instances representing the same database row (same PK) will now be considered equal (`instance1.Equals(instance2)` returns true), regardless of whether other property values differ (e.g., due to one instance being stale).
    *   `GetHashCode()` is now also based only on the Primary Key, ensuring stability, especially for mutable objects used in hash-based collections.
*   **Improved Handling of New/Unsaved Instances:**
    *   `Mutable<T>` instances created via `new()` (before being saved to the database and receiving a real Primary Key, especially auto-increments) now use an internal, temporary `TransientId` (a Guid) for `Equals` and `GetHashCode`.
    *   This allows distinct *new* instances to be correctly differentiated and managed in collections (`List`, `HashSet`, `Dictionary` keys, `GroupBy`) *before* they are saved.
    *   **Important Note:** As documented previously, the hash code of a mutable instance *will change* upon saving when it transitions from using the `TransientId` to using the database Primary Key. Avoid placing *new* instances in hash-based collections if you intend to save them later without removing/re-adding.
*   **Source Generator Compatibility:**
    *   Downgraded the required `Microsoft.CodeAnalysis.CSharp` package version from 4.13 to **4.12**. This resolves runtime loading errors for users with slightly older (but still common) Visual Studio 2022 / .NET SDK versions, the minimum supported version is now **Visual Studio 2022 version 17.12**
    *   Fixed a generator crash that occurred if only a database class was present without any table models.
*   **Internal Refinements & Cleanup:**
    *   Refactored relation handling internally for better encapsulation.
    *   Removed remnants of unsupported `ICustomTableModel` implementation.
    *   Improved internal error handling.
    *   Cleaned up NuGet packaging configuration for clarity.

**🐛 Other Bug Fixes:**

*   Resolved build/dependency issues in the source generator test project.

**📝 Notes:**

*   This release addresses significant behavioral issues related to equality checks that could cause problems when using DataLinq entities in standard .NET collections or with LINQ operators like `GroupBy`. The new PK-based equality is the standard and correct approach for ORM entities.
*   The generator compatibility fix ensures wider usability across common developer environments.

---

## [DataLinq v0.5.0 Release Notes](https://github.com/bazer/DataLinq/releases/tag/0.5.0)

**Released on:** 2025-04-02

This release marks a significant step forward for DataLinq, switching to using a source generator instead of reflection, improving performance by reducing memory allocations, major internal refactorings for robustness, and adding foundational features like logging and improved error handling. I also made substantial progress on documentation, including a new DocFX-based website.

**🚨 Breaking Changes:**

*   **Target Frameworks Updated:** DataLinq now targets **.NET 8.0 and .NET 9.0**. Support for .NET 6 and .NET 7 has been **removed**. Please update your projects accordingly.

**🚀 Highlights:**

*   **Major work on a new Source Generator:** A new source generator to replace the old Castle.Core based code.
    *   Generates two new classes Mutable[model] and Immutable[model].
    *   Supports C# nullable reference types (`#nullable enable`).
    *   Correctly generates `required` properties on mutable models based on database constraints and default values.
    *   Generates code respecting `[DefaultValue]` attributes from your models.
    *   Improved error reporting and internal structure (moved to `DataLinq.Generators` project).
    *   Reliably generates interfaces (e.g., `IEmployee`) alongside model classes.
*   **Performance Boost & Reduced Allocations:**
    *   Introduced `ReadOnlyAccess` for significantly faster, lower-allocation reads compared to using a `Transaction`.
    *   Optimized primary and foreign key handling using value type `IKey` structs (like `IntKey`, `GuidKey`, `CompositeKey`), reducing object allocations.
    *   Improved performance for `Count()` and `Any()` LINQ operations.
    *   Optimized relation loading with `ImmutableRelation<T>` and `ImmutableForeignKey<T>`.
*   **Logging Integration:** Added basic logging using `Microsoft.Extensions.Logging`. SQL commands, cache events, and transaction information can now be logged.
*   **Enhanced Error Handling:** Improved the internal `DLOptionFailure` system and exception messages for clearer diagnostics.
*   **Documentation Website:** Introduced a documentation website generated using DocFX for easier navigation and access to information. (See project README for link when available).
*   **Default Value Support:** Models generated from databases now include `[DefaultValue]` attributes, and the source generator respects these when creating mutable classes. SQL generation also handles default values.

**✨ Features & Enhancements:**

*   Added provider-specific SQL identifier escaping (e.g., `` ` `` for MySQL, `"` for SQLite).
*   Refined mutation and save method workflows.
*   Improved handling of `Nullable<T>` types internally and in LINQ queries.

**🛠 Refactoring & Internal Improvements:**

*   Significant refactoring of core metadata classes (`DatabaseDefinition`, `TableDefinition`, `ModelDefinition`, etc.) for improved immutability, clarity, and reduced internal nullability warnings.
*   Refactored `RowCache` into its own class.
*   Cleaned up build system using `Directory.Build.props`.
*   Moved testing models to a dedicated `DataLinq.Tests.Models` project.

**🐛 Bug Fixes:**

*   Fixed loading issues with nullable vs. non-nullable values.
*   Fixed an issue where calling `Update` with no actual changes would cause an error.
*   Fixed mutable classes being incorrectly generated for database Views.
*   Fixed incorrect column length parsing for MySQL numeric types.
*   Fixed issues with multiple foreign key relations pointing to the same table.
*   Fixed LINQ query translation involving nullable booleans.
*   Fixed loading of nullable `DateOnly` values.
*   Addressed numerous nullability warnings throughout the codebase.
*   Fixed preservation of original database model class names during generation/updates.
*   Fixed various bugs related to SQLite compatibility (AutoIncrement, index types).

**📦 Dependencies:**

*   Updated various NuGet package dependencies, including `MySqlConnector`, `Microsoft.Data.Sqlite`, `Microsoft.CodeAnalysis`, and testing libraries.

---

## [0.0.1 - Will probably eat your data](https://github.com/bazer/DataLinq/releases/tag/0.0.1)

**Released on:** 2020-06-06

First release with basic functionality.
* Row cache 
* Query builder
* Linq queries
* MySql support
* Database metadata
* Support for automatic creation of data models from database
* Working CRUD
* Transaction support
* Support for Select, Insert, Update, Delete, Where, Limit, OrderBy and nested And/Or
* Support for lazy loading of model properties

---


