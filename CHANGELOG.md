# DataLinq Changelog

All notable changes to this project will be documented in this file.

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


