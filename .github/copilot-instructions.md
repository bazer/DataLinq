# DataLinq Project: Context, Learnings, and Status for AI Assistant

**Last Updated:** 2025-06-05 *(Updated to reflect recent fixes)*
**Last Discussed Topics:** Finalizing `WeakEventManager` optimizations, fixing intermittent parallel test failures, ensuring correct SQL generation for complex LINQ `WHERE` clauses (boolean logic, empty list `Contains`/`Any`, JOIN/WHERE separation), writing release notes for v0.5.3.

## 1. Project Overview & Core Concepts

### 1.1. Purpose & Vision
DataLinq is a lightweight, high-performance .NET Object-Relational Mapper (ORM) designed with a primary focus on **efficient read operations** and **low memory allocations**. It leverages **immutable data models** by default, with a controlled mutation pattern. The project aims to reduce boilerplate code through **C# Source Generators** and provide flexibility with **multiple database backend support**. It's positioned as an alternative for scenarios where heavier ORMs like Entity Framework Core might be excessive, particularly for smaller to medium-sized projects with read-heavy workloads.

### 1.2. Key Architectural Pillars
*   **Immutability:** Data retrieved from the database is represented by immutable objects (`Immutable<T, M>`) for thread-safety, predictability, and cacheability.
*   **Controlled Mutation:** Updates are handled by:
    1.  Calling `.Mutate()` on an immutable instance to get a `Mutable<T>` wrapper.
    2.  Modifying properties on the `Mutable<T>` instance.
    3.  Calling `.Save()` (or `Insert()`/`Update()`) within a `Transaction` to persist changes and generate a new immutable instance.
*   **Source Generation (`DataLinq.Generators`):**
    *   The Roslyn-based Source Generator (`DataLinq.Generators.ModelGenerator`) reads developer-defined abstract partial model classes (decorated with attributes).
    *   Generates: Concrete `Immutable[ModelName]` classes, `Mutable[ModelName]` classes, public interfaces (`I[ModelName]`), and extension methods.
*   **Caching & Event Handling:**
    *   Multi-layered: `RowCache` (PK -> Instance), `IndexCache` (FK -> PKs), `ImmutableRelation` object cache.
    *   **`WeakEventManager` (`DataLinq.Utils.WeakEventManager`):** Used by `TableCache.RowChanged` to prevent memory leaks by holding weak references to subscribers (e.g., `ImmutableRelation`, `ImmutableForeignKey` instances), allowing them to be garbage collected. This was a key fix in v0.5.3.
    *   "Primary Key First" query strategy. Relation loading also uses a layered cache approach.
    *   Configurable cache limits and cleanup. Cache history and snapshotting.
*   **LINQ Provider (`DataLinq.Linq`):** Uses `Remotion.Linq` as a base. Contains custom visitors (`WhereVisitor`, `OrderByVisitor`) to translate LINQ expression trees into `SqlQuery` objects.
    *   Recent work (v0.5.3) significantly improved handling of complex boolean logic (`AND`/`OR` precedence, grouping with `NOT`), `Contains()` and `Any()` with empty lists or complex predicates (including those with `Convert` nodes), and correct SQL parentheses generation.
*   **Database Providers:** Modular design (MySQL, SQLite). Each provider handles SQL dialect specifics, metadata reading, and data access.
*   **CLI Tool (`DataLinq.CLI`):** Provides commands for scaffolding abstract models, generating SQL schema, creating databases, and listing configurations.
*   **Configuration:** Uses `datalinq.json` and `datalinq.user.json`.

### 1.3. Coding Styles & Libraries
*   **Language:** Modern C# (.NET 9 for runtime, `netstandard2.0` for generator).
*   **Nullable Reference Types:** Enabled.
*   **Error Handling:** `Option<TSuccess, TFailure>` (`ThrowAway.Option`) with `DLOptionFailure`.
*   **Key NuGet Dependencies:** `Microsoft.CodeAnalysis.CSharp`, `Remotion.Linq`, `ThrowAway`, DB connectors, `CommandLineParser`, `Bogus`, `xUnit`, `SourceGenerator.Foundations`.

## 2. Project Structure & Build Learnings

### 2.1. Current Structure (Summary - see v0.5.2 notes for more detail if needed)
*   `DataLinq.SharedCore` (linked sources) for common types.
*   `DataLinq.Generators` (Source Generator, `netstandard2.0`).
*   `DataLinq` (Main ORM runtime, .NET 8/9, packages generator & its deps in `analyzers/`).
*   DB Providers (`DataLinq.MySql`, `DataLinq.SQLite`).
*   `DataLinq.Tools`, `DataLinq.CLI`.
*   Test Projects (`DataLinq.Tests`, `DataLinq.Generators.Tests`, `DataLinq.Tests.Models`, `DataLinq.Benchmark`).

### 2.2. Build/Packaging Learnings (Summary - see v0.5.2 notes for historical context)
*   Single `DataLinq` NuGet package bundling runtime, generator, and dependencies remains the goal.
*   Source file linking from `SharedCore` used instead of `.shproj`.
*   Generator dependencies are explicitly packaged into `analyzers/dotnet/cs/`.

## 3. Current Status & Focus Areas (Post v0.5.3)

### 3.1. Key Fixes in v0.5.3
*   **Memory Leak with Event Handlers:** Resolved by implementing and integrating `DataLinq.Utils.WeakEventManager` for `TableCache.RowChanged`. This ensures `ImmutableRelation` and `ImmutableForeignKey` instances can be garbage collected.
*   **`WeakEventManager` Performance:** Optimized the `AddEventHandlerDetail` in `WeakEventManager` by switching from `List<Subscription>` linear scan to `HashSet<Subscription>` for O(1) average time duplicate checks and additions.
*   **LINQ Query Translation Robustness:**
    *   Corrected boolean operator precedence (`AND`/`OR`) in complex `WHERE` clauses, especially with negations and nested groups.
    *   Improved SQL parentheses generation for clarity and correctness.
    *   Added support for `Contains()` and `Any()` (with/without predicates) on empty lists, generating appropriate `1=0` or `1=1` SQL.
    *   Enhanced `Any(predicate)` to handle `Convert` nodes within the predicate (e.g., `list.Any(id == e.NullableInt.Value)`).
    *   Fixed bug where `WHERE` conditions on joined tables could incorrectly merge into the `JOIN ON` clause; ensured they correctly form a separate `WHERE` clause. (Involves `Join<T>.On(Action<...>)` returning `SqlQuery<T>`).
*   **Test Stability:** Refactored several "flaky" tests (especially in `BooleanLogicTests` and `EmptyListTests`) to use smaller, controlled data subsets instead of relying on entire table states, making them more robust against parallel execution interference.

### 3.2. Current Code Health
*   The core LINQ translation logic in `WhereVisitor` is now significantly more robust.
*   The `WeakEventManager` is more performant and thread-safe.
*   Equality comparison for `Immutable<T,M>` and `Mutable<T>` (PK-based for saved, TransientId for new) is established.

### 3.3. Known Minor Issues / Next Considerations
*   **Nullable Reference Type Warnings (CS8618):** Still present in some metadata classes due to multi-phase initialization. Strategy remains to evaluate `null!`, nullable properties, or runtime checks.
*   **Full LINQ Support Documentation:** Needs to be created/updated to reflect all supported and unsupported LINQ operations.
*   **Remaining LINQ Operators:** Consider expanding support for more LINQ methods (e.g., `GroupBy`, `Select` projections to different types, more aggregate functions, more `string` methods, `DateTime` properties).
*   **Test Coverage for `WeakEventManager` Concurrency:** While basic concurrency tests exist, more rigorous stress testing could be beneficial if further issues arise.
*   **Performance Benchmarking:** Now that core stability is better, more comprehensive write and complex query benchmarks can be developed.
*   **SQLite Isolation in Tests:** The issue where SQLite (with `Cache=Shared` or WAL) might show uncommitted data to `ReadOnlyAccess` (seen in `InsertRelations` test before conditional `OnRowChanged`) is a characteristic of SQLite's setup. Tests should be mindful of this if strict transactional visibility is asserted. The conditional `OnRowChanged` (only firing globally on final commit) mitigates this for external observers.

## 4. AI Assistant Learnings & Preferences

*   **Mermaid Diagram Quirks:** Understands limitations with comments, `&` styling, complex labels.
*   **SQL Generation Preference:** For `WHERE` clauses involving multiple top-level `AND`/`OR` groups, the preference is for the SQL to be `(GROUP A) OR (GROUP B)` rather than `WHERE ((GROUP A) OR (GROUP B))` if the outer parentheses are redundant (i.e., the `WHERE` group is not negated). This was achieved by refining parenthesis logic in `WhereGroup.AddCommandString`.
*   **Test Debugging:** Good at tracing LINQ expression visitor logic and correlating it with generated SQL.
*   **Refactoring:** Receptive to refactoring suggestions for clarity and correctness (e.g., `GetComparisonDetails` struct, `Join.On(Action<...>)` pattern).

## 5. Future Ideas & Long-Term
*   Database Migrations.
*   Advanced Caching (Query Caching, Distributed).
*   Tooling (VS Extensions, enhanced CLI).
*   Non-Relational Backends.