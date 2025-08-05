# DataLinq Project: Context, Learnings, and Status for AI Assistant

**Last Updated:** 2025-08-05
**Last Discussed Topics:** Fixed bugs related to recursive foreign keys, primary keys that are also foreign keys, and resolved all C# nullability/hiding warnings in source-generated code. Wrote release notes for v0.6.1.

## 1. Project Overview & Core Concepts

### 1.1. Purpose & Vision
DataLinq is a lightweight, high-performance .NET ORM focused on efficient read operations and low memory allocations. It uses immutable data models, a source generator to reduce boilerplate, and a flexible provider model for multiple database backends.

### 1.2. Key Architectural Pillars
*   **Immutability:** Data is represented by immutable objects for thread-safety and predictability.
*   **Controlled Mutation:** Updates are handled via a `.Mutate()` method on immutable instances, with changes saved transactionally.
*   **Source Generation (`DataLinq.Generators`):** Reads abstract partial model classes to generate concrete `Immutable`, `Mutable`, interface, and extension method classes.
*   **Caching & Notification System:**
    *   **`CacheNotificationManager` (in `TableCache.cs`):** The primary mechanism for cache invalidation of relations between tables. It uses a **lock-free, array-swapping pattern** to manage `WeakReference`s to subscribers, preventing memory leaks.
    *   Caches include `RowCache` (PK -> Instance) and `IndexCache` (FK -> PKs).
*   **LINQ Provider (`DataLinq.Linq`):**
    *   **Refactored Architecture:** The logic is now split between a lightweight `WhereVisitor` and a comprehensive `QueryBuilder`. The visitor traverses the expression tree and delegates all construction logic to the `QueryBuilder`.
    *   The `QueryBuilder`'s `AnalyzeExpression` and `HandleComparison` methods are the core of the translation, correctly handling member-vs-value, member-vs-member, and member-vs-function comparisons.
    *   `Remotion.Linq` is used as a base.
*   **Database Providers & CLI:** Modular providers for MySQL, MariaDB, and SQLite. The CLI tool scaffolds models from the database schema and generates SQL from the models.
*   **Configuration:** Uses `datalinq.json` and `datalinq.user.json` with a unified `Include` list.

### 1.3. Coding Styles & Libraries
*   **Language:** Modern C# (.NET 9 for runtime, `netstandard2.0` for generator).
*   **Error Handling:** Uses `Option<TSuccess, TFailure>` (`ThrowAway.Option`) with `DLOptionFailure`.
*   **Key NuGet Dependencies:** `Microsoft.CodeAnalysis.CSharp`, `Remotion.Linq`, `ThrowAway`, DB connectors, `CommandLineParser`, `Bogus`, `xUnit`.

## 2. Project Structure & Build Learnings

*   `DataLinq.SharedCore` (linked sources) for common types.
*   `DataLinq.Generators` (Source Generator).
*   `DataLinq` (Main ORM runtime, packages generator & its deps in `analyzers/`).
*   DB Providers (`DataLinq.MySql`, `DataLinq.SQLite`).
*   `DataLinq.Tools`, `DataLinq.CLI`.
*   Test Projects (`DataLinq.Tests`, `DataLinq.Generators.Tests`, etc.).

## 3. Current Status & Focus Areas (Post v0.6.1)

### 3.1. Key Enhancements in v0.6.0
*   **LINQ Provider Overhaul:** The query translation logic was refactored into a `QueryBuilder`, making it more robust and extensible.
*   **Expanded LINQ `WHERE` Clause Support:** Added support for member-to-member comparisons, all `DateTime`/`DateOnly`/`TimeOnly` properties, and several string functions.
*   **Architectural Improvements:** Separated MySQL/MariaDB providers and improved instance creation logic.

### 3.2. Key Fixes in v0.6.1
*   **Fixed Recursive Relation Parsing:** Refactored `MetadataFactory.ParseRelations` to correctly handle self-referencing foreign keys. It now correctly generates one single-entity property (many-to-one) and one collection property (one-to-many) with distinct names and types.
*   **Corrected `required` Logic for PK/FKs:** Fixed the `IsMutablePropertyRequired` logic to ensure a column that is both a `PrimaryKey` and a `ForeignKey` is correctly included as a required parameter in mutable class constructors.
*   **Resolved All Known C# Warnings in Generated Code:**
    *   `CS8603` (Possible null reference return): Fixed by correctly applying the null-forgiving operator (`!`) to non-nullable relation property getters.
    *   `CS8618` (Non-nullable property uninitialized): Fixed by using the `!` operator in `required` property getters and suppressing the warning with `#pragma` on constructors that initialize via a base call.
    *   `CS0108` (Member hiding): Fixed by adding the `new` keyword to generated properties (like `IsDeleted`) that hide base class methods.

### 3.3. Next Tasks for v0.7.0
The user has outlined the following priorities for the next feature release:
1.  Implement LINQ support for Date/Time Arithmetic (`AddDays`, `AddHours`, etc.).
2.  Implement LINQ support for Static Date/Time Properties (`DateTime.Now`, `DateTime.UtcNow`).
3.  Implement LINQ support for the `.Date` property on `DateTime` objects.
4.  Investigate `TimeSpan` support for date subtractions.

## 4. AI Assistant Learnings & Preferences

*   **Refactoring is Key:** Acknowledge that major refactors, like the `WhereVisitor` -> `QueryBuilder` split or centralizing the `ParseRelations` logic, are preferable to patching a fragile system.
*   **Provider-Specific Logic:** SQL generation that differs between backends (e.g., date functions, view creation syntax) **must** be handled in the specific provider class, not in a shared component. This is a core design principle.
*   **Centralize Logic:** The `ParseRelations` refactoring taught us that the component with the most context should be responsible for complex logic. The database-specific factories should only *identify* metadata (`ForeignKeyAttribute`), while the central `MetadataFactory` should be the single source of truth for *building* the `RelationProperty` objects from that metadata.
*   **Be Mindful of Framework Compatibility:** Remember .NET Standard 2.0 API limitations (e.g., `string.Replace` overloads) in shared code that is consumed by the source generator. Use compatible alternatives like `Regex.Replace` when necessary.
*   **Handle Generated Code Warnings Precisely:** Use the right tool for each warning: the `!` operator for `required` property getters where null is impossible, `#pragma warning disable/restore` for valid but unanalyzable constructor patterns, and the `new` keyword for intentional member hiding.
*   **Test-Driven Correction:** Use failing tests as the primary driver for fixes. Correcting the tests to reflect new, improved behavior is a key part of the process.
*   **C# vs. SQL Semantics:** Be highly aware of the differences in how C# and SQL handle logic, especially with `null` values (e.g., nullable booleans). The goal of the LINQ provider is to make the SQL behave like C#.
*   **Minimal Changes:** When fixing bugs, prefer targeted, surgical changes over broad, sweeping ones that might cause regressions.
*   **Memory Leak Patterns:** Remember the lessons from `WeakEventManager` and `CacheNotificationManager` regarding event handlers and weak references.
*   **Test Isolation:** Using dedicated test fixtures that create temporary, isolated databases (`MySqlTypeMappingFixture`) is the superior pattern for testing schema-related logic.

## 5. Future Ideas & Long-Term
*   Database Migrations.
*   Advanced Caching (Query Caching, Distributed).
*   Tooling (VS Extensions, enhanced CLI).
*   Non-Relational Backends.