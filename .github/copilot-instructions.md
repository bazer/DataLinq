# DataLinq Project: Context, Learnings, and Status for AI Assistant

**Last Updated:** 2025-06-12
**Last Discussed Topics:** Finalizing the `CacheNotificationManager`, fixing the `ConcurrentBag` memory leak, implementing improved type mapping for MySQL (unsigned integers, etc.), adding the `--overwrite-types` CLI flag, and simplifying configuration with a unified `Include` list.

## 1. Project Overview & Core Concepts

### 1.1. Purpose & Vision
DataLinq is a lightweight, high-performance .NET ORM focused on efficient read operations and low memory allocations. It uses immutable data models, a source generator to reduce boilerplate, and a flexible provider model for multiple database backends.

### 1.2. Key Architectural Pillars
*   **Immutability:** Data is represented by immutable objects for thread-safety and predictability.
*   **Controlled Mutation:** Updates are handled via a `.Mutate()` method on immutable instances, with changes saved transactionally.
*   **Source Generation (`DataLinq.Generators`):** Reads abstract partial model classes to generate concrete `Immutable`, `Mutable`, interface, and extension method classes.
*   **Caching & Notification System:**
    *   **`CacheNotificationManager` (in `TableCache.cs`):** The primary mechanism for cache invalidation of relations between tables. It uses a **lock-free, array-swapping pattern** with `Interlocked.CompareExchange` to manage a list of `WeakReference`s to subscribers (like `ImmutableRelation`). This prevents memory leaks by allowing subscribers to be garbage collected, while avoiding the performance pitfalls of reflection or the complexities of finalizers. This system replaced the previous `WeakEventManager` and `ConditionalWeakTable` attempts.
    *   Caches include `RowCache` (PK -> Instance) and `IndexCache` (FK -> PKs).
*   **LINQ Provider (`DataLinq.Linq`):** Uses `Remotion.Linq` as a base, with custom visitors (`WhereVisitor`, `OrderByVisitor`) to translate LINQ expression trees into `SqlQuery` objects. Robustly handles complex boolean logic and `Contains`/`Any` methods.
*   **Database Providers & CLI:** Modular providers for MySQL and SQLite. The CLI tool scaffolds models from the database schema and generates SQL from the models.
*   **Configuration:** Uses `datalinq.json` and `datalinq.user.json` with a now-unified `Include` list for specifying which tables and views to process.

### 1.3. Coding Styles & Libraries
*   **Language:** Modern C# (.NET 9 for runtime, `netstandard2.0` for generator).
*   **Error Handling:** Uses `Option<TSuccess, TFailure>` (`ThrowAway.Option`) with `DLOptionFailure`.
*   **Key NuGet Dependencies:** `Microsoft.CodeAnalysis.CSharp`, `Remotion.Linq`, `ThrowAway`, DB connectors, `CommandLineParser`, `Bogus`, `xUnit`.

## 2. Project Structure & Build Learnings

*   `DataLinq.SharedCore` (linked sources) for common types.
*   `DataLinq.Generators` (Source Generator, `netstandard2.0`).
*   `DataLinq` (Main ORM runtime, packages generator & its deps in `analyzers/`).
*   DB Providers (`DataLinq.MySql`, `DataLinq.SQLite`).
*   `DataLinq.Tools`, `DataLinq.CLI`.
*   Test Projects (`DataLinq.Tests`, `DataLinq.Generators.Tests`, etc.).

## 3. Current Status & Focus Areas (Post v0.5.4)

### 3.1. Key Fixes & Enhancements in v0.5.4
*   **Critical Memory Leak Fixed:** Replaced the previous event/notification system with a custom `CacheNotificationManager` that uses a lock-free array-swapping pattern. This resolves the memory leak where `ImmutableRelation` and `ImmutableForeignKey` instances were not being garbage collected.
*   **`IndexCache` Memory Leak Fixed:** Corrected a bug where `TableCache.ClearCache()` failed to clear all internal dictionaries within the `IndexCache`, leading to a memory leak during cache flushes.
*   **Greatly Improved Schema-to-Model Type Mapping:**
    *   The MySQL metadata factory now correctly generates unsigned C# types (`uint`, `byte`, `ushort`, `ulong`) for `UNSIGNED` database columns.
    *   It also correctly maps `TINYINT`, `SMALLINT`, `BIGINT`, etc., to their appropriately sized C# counterparts (`sbyte`, `short`, `long`).
*   **Enhanced CLI Control (`--overwrite-types`):**
    *   Added the `--overwrite-types` flag to the `create-models` command.
    *   When used, this flag forces C# property types to be regenerated from the database schema.
    *   The logic intelligently preserves user-defined types like `enum`s and other custom classes, only overwriting standard C# primitive types.
*   **Simplified Configuration:** The `Tables` and `Views` lists in `datalinq.json` have been unified into a single, more intuitive `Include` list.
*   **Improved Nullability Generation:** Corrected the logic in the `ModelFileFactory` to ensure that `AutoIncrement` and columns with `DEFAULT` values are correctly generated as nullable C# types (e.g., `int?`).
*   **Robust Test Suite:** Added new, isolated test fixtures (`MySqlTypeMappingFixture`, `MySqlFilteringTestFixture`) that create temporary databases, making tests for metadata parsing much more reliable. Wrote comprehensive tests for the new type mapping, filtering logic, CLI flag, and the new `CacheNotificationManager`.

### 3.2. Next Tasks for v0.5.5
The user has outlined the following priorities for the next version:
1.  Add support for MariaDB's native `UUID` type.
2.  Add support for more LINQ `string` methods, such as `Length` and `!string.IsNullOrEmpty()`.

### 3.3. Known Minor Issues
*   **`CS8618` Nullable Reference Type Warnings:** Still present in some metadata classes due to multi-phase initialization. The strategy remains to evaluate `null!`, nullable properties, or runtime checks.
*   **Full LINQ Support Documentation:** Needs to be updated to reflect newly supported methods.
*   **Remaining LINQ Operators:** Consider expanding support for more LINQ methods (e.g., `GroupBy`, `Select` projections to different types, more aggregate functions, more `string` methods, `DateTime` properties).
*   **Performance Benchmarking:** Now that core stability is better, more comprehensive write and complex query benchmarks can be developed.
*   **SQLite Isolation in Tests:** The issue where SQLite (with `Cache=Shared` or WAL) might show uncommitted data to `ReadOnlyAccess` (seen in `InsertRelations` test before conditional `OnRowChanged`) is a characteristic of SQLite's setup. Tests should be mindful of this if strict transactional visibility is asserted. The conditional `OnRowChanged` (only firing globally on final commit) mitigates this for external observers.

## 4. AI Assistant Learnings & Preferences

*   **Finalizers are Dangerous:** Learned that using finalizers to call methods on other managed objects is unsafe due to unpredictable GC timing and object state. The `ConditionalWeakTable` pattern or a custom cleanup mechanism is superior.
*   **`ConcurrentBag` Leak:** Learned about the memory leak potential of `ConcurrentBag` in add-only/snapshot-and-rebuild scenarios due to its internal thread-local storage not being reclaimed.
*   **Delegate Equality:** Reinforced the concept that `event -= MyMethod` does not work reliably unless the exact same delegate *instance* is used for both subscription and unsubscription. Storing the delegate in a field is the correct pattern.
*   **Test Isolation:** Using dedicated test fixtures that create temporary, isolated databases (`MySqlTypeMappingFixture`) is a superior pattern for testing schema-related logic compared to relying on a shared, complex database.
*   **Mermaid Diagram Quirks:** Understands limitations with comments, `&` styling, complex labels.
*   **SQL Generation Preference:** For `WHERE` clauses involving multiple top-level `AND`/`OR` groups, the preference is for the SQL to be `(GROUP A) OR (GROUP B)` rather than `WHERE ((GROUP A) OR (GROUP B))` if the outer parentheses are redundant (i.e., the `WHERE` group is not negated). This was achieved by refining parenthesis logic in `WhereGroup.AddCommandString`.
*   **Test Debugging:** Good at tracing LINQ expression visitor logic and correlating it with generated SQL.
*   **Refactoring:** Receptive to refactoring suggestions for clarity and correctness (e.g., `GetComparisonDetails` struct, `Join.On(Action<...>)` pattern).

## 5. Future Ideas & Long-Term
*   Database Migrations.
*   Advanced Caching (Query Caching, Distributed).
*   Tooling (VS Extensions, enhanced CLI).
*   Non-Relational Backends.