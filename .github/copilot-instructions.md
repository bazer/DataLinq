# DataLinq Project: Context, Learnings, and Status for AI Assistant

**Last Updated:** 2025-08-17
**Last Discussed Topics:** Fixed a critical high-contention performance bug in `CacheNotificationManager`. Implemented LINQ support for `string.Length`. Fixed a bug in the CLI tool where it incorrectly generated nullable reference types when the feature was disabled. Drafted release notes for v0.6.2.

## 1. Project Overview & Core Concepts

### 1.1. Purpose & Vision
DataLinq is a lightweight, high-performance .NET ORM focused on efficient read operations and low memory allocations. It uses immutable data models, a source generator to reduce boilerplate, and a flexible provider model for multiple database backends.

### 1.2. Key Architectural Pillars
*   **Immutability:** Data is represented by immutable objects for thread-safety and predictability.
*   **Controlled Mutation:** Updates are handled via a `.Mutate()` method on immutable instances, with changes saved transactionally.
*   **Source Generation (`DataLinq.Generators`):** Reads abstract partial model classes to generate concrete `Immutable`, `Mutable`, interface, and extension method classes.
*   **Caching & Notification System:**
    *   **`CacheNotificationManager` (in `TableCache.cs`):** The primary mechanism for cache invalidation of relations. **(Learning from v0.6.2)** It uses a highly performant pattern for high-contention scenarios: write operations (`Subscribe`, `Clean`) are protected by a `lock` and modify an internal `List<T>`, while the read operation (`Notify`) remains lock-free by iterating over a `volatile` array snapshot that is only updated from the list when necessary.
    *   Caches include `RowCache` (PK -> Instance) and `IndexCache` (FK -> PKs).
*   **LINQ Provider (`DataLinq.Linq`):**
    *   **Refactored Architecture:** The logic is now split between a lightweight `WhereVisitor` and a comprehensive `QueryBuilder`. The visitor traverses the expression tree and delegates all construction logic to the `QueryBuilder`.
    *   The `QueryBuilder`'s `AnalyzeExpression` and `HandleComparison` methods are the core of the translation, correctly handling member-vs-value, member-vs-member, and member-vs-function comparisons.
    *   `Remotion.Linq` is used as a base.
*   **Database Providers & CLI:** Modular providers for MySQL, MariaDB, and SQLite. The CLI tool scaffolds models from the database schema and generates SQL from the models.
*   **Configuration:** Uses `datalinq.json` and `datalinq.user.json`.

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

## 3. Current Status & Focus Areas (Post v0.6.2)

### 3.1. Key Fixes in v0.6.1
*   Fixed parsing for recursive/self-referencing foreign keys.
*   Corrected `required` logic for properties that are both a Primary Key and a Foreign Key.
*   Resolved all known C# nullability and member-hiding warnings in generated code.

### 3.2. Key Fixes in v0.6.2
*   **Fixed High-Contention Performance:** Resolved a thread starvation issue in `CacheNotificationManager` by replacing the CAS-loop with a more efficient lock-guarded write path, making subscriptions an O(1) operation.
*   **Implemented `string.Length` in LINQ:** Added support for `string.Length`.
*   **Corrected CLI Nullability Generation:** The `datalinq create-models` command now correctly respects the `"UseNullableReferenceTypes": false` setting and will not generate `?` for reference types like `string` when the feature is disabled.

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
*   **Optimal High-Contention Pattern:** For the cache notification system, the best pattern was a hybrid approach: Use a `lock` to guard a fast, mutable `List<T>` for all write operations (`Subscribe`, `Clean`). The frequently called `Notify` method remains lock-free by reading from a `volatile` array that is only updated from the list when a change has been flagged. This avoids both lock contention on reads and CPU--intensive spinning on writes.
*   **Expression Chain Traversal is Crucial:** The `string.Length` bug was a symptom of the parser not handling chained calls (`.Trim().Length`). The robust solution is a helper (`FindRootColumnExpression`) that can traverse through `MethodCallExpression` and `MemberExpression` nodes to find the root database column, and a parser (`GetSqlFunction`) that can then build the nested SQL functions in the correct order.
*   **Distinguish CLI vs. Source Generator Contexts:** I initially misdiagnosed the nullable reference type (NRT) bug. It's vital to remember there are two code generation stages:
    1.  The **CLI Tool** (`datalinq create-models`) reads `datalinq.json` and writes abstract model files to disk. Its logic is in `DataLinq.Tools`.
    2.  The **Roslyn Source Generator** (`DataLinq.Generators`) runs in the IDE/build and reads the abstract model files in memory to generate the final concrete classes. It does *not* read `datalinq.json`.
*   **C# Nullability Nuances in Code Generation:** When generating code, remember the distinction:
    *   **Value types** (including user-defined enums) that are nullable in the DB **must** have a `?` in C# (`int?`, `MyEnum?`).
    *   **Reference types** (`string`, `byte[]`) are inherently nullable. They should only get the `?` syntax if the user has explicitly enabled NRTs in their configuration. The generator must respect this setting.
*   **Test-Driven Correction:** Use failing tests as the primary driver for fixes. Correcting the tests to reflect new, improved behavior is a key part of the process.
*   **C# vs. SQL Semantics:** Be highly aware of the differences in how C# and SQL handle logic, especially with `null` values. The goal of the LINQ provider is to make the SQL behave like C#.
*   **Minimal Changes:** When fixing bugs, prefer targeted, surgical changes over broad, sweeping ones that might cause regressions.
*   **Memory Leak Patterns:** Remember the lessons from `WeakEventManager` and `CacheNotificationManager` regarding event handlers and weak references.
*   **Test Isolation:** Using dedicated test fixtures that create temporary, isolated databases (`MySqlTypeMappingFixture`) is the superior pattern for testing schema-related logic.

## 5. Future Ideas & Long-Term
*   Database Migrations.
*   Advanced Caching (Query Caching, Distributed).
*   Tooling (VS Extensions, enhanced CLI).
*   Non-Relational Backends.