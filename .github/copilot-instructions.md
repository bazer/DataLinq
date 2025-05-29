# DataLinq Project: Context, Learnings, and Status for AI Assistant

**Last Updated:** 2025-05-29
**Last Discussed Topics:** Refining this copilot-instructions document, `SourceGenerator.Foundations` usage, Rider/Contracts issue with SGF.

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
    *   Generates:
        *   Concrete `Immutable[ModelName]` classes.
        *   Concrete `Mutable[ModelName]` classes.
        *   Public interfaces (`I[ModelName]`) for the models.
        *   Extension methods for mutation (`.Save()`, `.Mutate()`, etc.).
    *   This is distinct from `DataLinq.Tools.ModelGenerator` which is used by the CLI for scaffolding abstract models *from* a database schema.
*   **Caching:**
    *   Multi-layered: `RowCache` (PK -> Instance), `IndexCache` (FK -> PKs), `ImmutableRelation` object cache.
    *   "Primary Key First" query strategy: Fetch PKs, check cache, then fetch full rows for misses.
    *   Relation loading also uses a layered cache approach.
    *   Configurable cache limits and cleanup via attributes and background worker.
    *   Cache history and snapshotting for diagnostics.
*   **LINQ Provider:** Uses `Remotion.Linq` as a base for translating LINQ queries into SQL.
*   **Database Providers:** Modular design to support different databases (currently MySQL, SQLite). Each provider handles SQL dialect specifics, metadata reading, and data access.
*   **CLI Tool (`DataLinq.CLI`):** Provides commands for:
    *   `create-models`: Generates abstract model files from a database schema (uses `DataLinq.Tools.ModelGenerator`).
    *   `create-sql`: Generates SQL schema from model definitions (uses `DataLinq.Tools.SqlGenerator`).
    *   `create-database`: Creates a database from model definitions (uses `DataLinq.Tools.DatabaseCreator`).
    *   `list`: Lists configured databases.
*   **Configuration:** Uses `datalinq.json` (primary) and `datalinq.user.json` (optional overrides) for database connections and model generation settings.

### 1.3. Coding Styles & Libraries
*   **Language:** Modern C# (currently targeting .NET 8 & .NET 9, with the generator itself on `netstandard2.0`).
*   **Nullable Reference Types:** Enabled and actively being managed (CS8618 warnings are a known point).
*   **Error Handling:** Uses a custom `Option<TSuccess, TFailure>` pattern (implemented via `ThrowAway.Option`) with `DLOptionFailure` and `DLOptionFailureException` for many internal operations, particularly in factories.
*   **Key NuGet Dependencies:**
    *   `Microsoft.CodeAnalysis.CSharp` (for Roslyn APIs in generator and syntax parsing tests).
    *   `Remotion.Linq` (for LINQ query parsing).
    *   `ThrowAway` (for the `Option` type).
    *   `MySqlConnector`, `Microsoft.Data.Sqlite` (for DB providers).
    *   `CommandLineParser` (for CLI).
    *   `Bogus` (for test data generation).
    *   `xUnit` (for testing).
    *   `SourceGenerator.Foundations` (used for its `SGF.IncrementalGenerator` base class, simplifying generator setup, and for development-time generator execution/debugging features. A known issue exists with Rider and `SourceGenerator.Foundations.Contracts`).

## 2. Project Structure & Build Learnings

### 2.1. Current Structure
*   **`DataLinq.SharedCore` (directory):** Contains core data structures (metadata classes like `DatabaseDefinition`, `TableDefinition`, `ColumnDefinition`, `ModelDefinition`, attributes, `DLOptionFailure`, interfaces like `IModelInstance`). Its `.cs` source files are *directly linked* into `DataLinq.csproj` (for `DataLinq.dll`) and `DataLinq.Generators.csproj` (for `DataLinq.Generators.dll`) during compilation.
*   **`DataLinq.Generators` (.csproj):**
    *   The C# Source Generator. Targets `netstandard2.0`.
    *   Compiles linked files from `DataLinq.SharedCore`.
    *   Depends on `Microsoft.CodeAnalysis.CSharp`, `ThrowAway`, and `SourceGenerator.Foundations`.
*   **`DataLinq` (.csproj):**
    *   The main runtime ORM library. Targets `.NET 8` & `.NET 9`.
    *   Compiles linked files from `DataLinq.SharedCore`.
    *   References `DataLinq.Generators` as an `OutputItemType="Analyzer"`.
    *   Responsible for NuGet packaging:
        *   `DataLinq.dll` (containing runtime logic + Core types) goes into `lib/netX.Y/`.
        *   `DataLinq.Generators.dll` and its direct runtime dependencies (e.g., `ThrowAway.dll`, `SourceGenerator.Foundations.Contracts.dll`) go into `analyzers/dotnet/cs/`.
*   **`DataLinq.MySql` / `DataLinq.SQLite` (.csproj):** Database provider implementations. Depend on `DataLinq`.
*   **`DataLinq.CLI` (.csproj):** Command-line tool. Depends on `DataLinq.Tools`.
*   **`DataLinq.Tools` (.csproj):** Contains helper logic for the CLI, such as classes for generating abstract models from a database schema (`ModelGenerator`), generating SQL DDL from metadata (`SqlGenerator`), and creating databases (`DatabaseCreator`). Depends on DB providers and `DataLinq` (for core types).
*   **Test Projects:**
    *   `DataLinq.Tests`: Runtime and integration tests.
    *   `DataLinq.Generators.Tests`: Tests for the source generator logic.
    *   `DataLinq.Tests.Models`: Contains the developer-defined abstract model classes (e.g., for `EmployeesDb`, `AllroundBenchmark`) that serve as the "source of truth" for schema definitions used in testing and benchmarking.
    *   `DataLinq.Benchmark`: Performance benchmarks.

### 2.2. Build/Packaging Pain Points & Solutions Explored
*   **Goal:** Single `DataLinq` NuGet package containing the runtime library, the source generator, and all necessary dependencies for both, without listing internal components as separate NuGet dependencies.
*   **VS Design-Time Generator Failures (`MissingMethodException`, `FileNotFoundException` for generator dependencies):**
    *   **Cause:** Visual Studio's host for generators can have different assembly loading behavior/paths than `dotnet build`. Dependencies of the generator might not be found if not explicitly made available.
    *   **Current Strategy:** Link Core source files into `DataLinq.Generators` and `DataLinq`. Ensure `DataLinq.Generators.csproj` has direct `PackageReference`s for libraries its compiled code needs (e.g., `ThrowAway`, `SourceGenerator.Foundations.Contracts`). The `DataLinq.csproj` packaging target then copies these generator dependencies into the `analyzers/dotnet/cs/` folder of the NuGet package.
    *   The `GetDependencyTargetPaths` target in `DataLinq.Generators.csproj` (using `@(RuntimeCopyLocalItems)`) aims to declare these dependencies as essential outputs for the VS host.
*   **Solution for `DataLinq.Core.dll`:** Linking Core source files directly into `DataLinq.dll` resolved the runtime `FileNotFoundException` for a separate `DataLinq.Core.dll` by eliminating it for the consumer.
*   **Runtime `FileNotFoundException` for `ThrowAway.dll` in Consuming Applications (e.g., `VagFasWebb`):** If `DataLinq.dll` (which now includes Core code using `ThrowAway`) causes a `FileNotFoundException` for `ThrowAway.dll` at runtime in a consuming app, it implies that `ThrowAway.dll` was not copied to the consumer's output directory. This typically *should* happen if `DataLinq.csproj` correctly references `ThrowAway` as a runtime dependency (i.e., `PackageReference` without `PrivateAssets="All"`).
*   **Assembly Version Conflicts (MSB3243 Warnings):**
    *   **Cause:** `DataLinq.dll` (targeting net8/9) and `DataLinq.Generators.dll` (targeting netstandard2.0, which has older baseline System.* libs) might resolve different versions of common NuGet packages like `System.Buffers`.
    *   **Strategy:** Explicitly reference the desired (usually newer) versions of these conflicting packages in `DataLinq.Generators.csproj` to force alignment.
*   **Internal MSBuild Errors with Shared Projects:**
    *   **Previously:** Interactions between shared projects, cross-targeting main libraries, and analyzer/generator references caused issues.
    *   **Current Strategy:** Moved away from `.shproj` to direct source file linking to avoid these MSBuild internal issues.

## 3. Current Issues & Focus Areas

### 3.1. Outstanding Build/Runtime Issues (as of last discussion)
*   **CS0433 Type Ambiguity in `DataLinq.Tests`:** After linking Core files into both `DataLinq.Generators` and `DataLinq`, the `DataLinq.Tests` project (which references both `DataLinq.dll` for runtime testing and `DataLinq.Generators.dll` as an analyzer) sees types like `IDatabaseModel` defined in two places.
    *   **Solution Being Implemented:** Ensure the `ProjectReference` to `DataLinq.Generators` in `DataLinq.Tests.csproj` has `OutputItemType="Analyzer"` and `ReferenceOutputAssembly="false"`. This should make the test project compile against types from `DataLinq.dll` primarily.
*   **Rider & `SourceGenerator.Foundations.Contracts`:** A known issue exists where JetBrains Rider might have trouble resolving or using `SourceGenerator.Foundations.Contracts.dll` when it's part of an analyzer package, impacting the developer experience in that IDE.

### 3.2. Code Implementation Focus
*   **Equality (`Equals`/`GetHashCode`):**
    *   Implemented PK-based equality for `Immutable<T, M>`.
    *   Implemented hybrid equality for `Mutable<T>` (TransientId for new, PK for saved).
    *   Removed `operator ==`/`!=` from base generic classes to resolve ambiguity, now requiring `.Equals()` or `is null`. Testing this impact.
*   **Nullable Reference Type Warnings (CS8618):** Many metadata classes have non-nullable properties not initialized in constructors due to multi-phase/circular setup.
    *   **Strategy:** Evaluate using `null!`, nullable properties (`Type?`), or backing fields with runtime checks for internal setters.

### 3.3. Testing Focus
*   Extensive tests for metadata parsing (`MetadataFactory`, `SyntaxParser`, `MetadataFromTypeFactory`, DB-specific factories).
*   Comprehensive equality tests for `Immutable` and `Mutable` instances, especially around collection behavior and state transitions (new -> saved).

## 4. AI Assistant Learnings & Preferences

*   **Mermaid Diagrams:**
    *   Current environment (Mermaid.live or similar) does not support `%%` comments or trailing semicolons in `classDef`/`style` lines.
    *   Applying styles to multiple nodes with `&` (e.g., `A & B ::: MyStyle`) is not reliably supported; apply styles individually.
    *   Avoid complex HTML or C# code snippets with quotes directly inside node labels; simplify or explain externally.
    *   Numbering nodes (e.g., "1. My Node") can also cause parsing issues.
*   **Project Structure:** Understands the goal of a single primary NuGet package (`DataLinq`) that bundles the runtime, core types, and the source generator with its dependencies.
*   **Problem-Solving Approach:** Iterative; likes to see failing tests first, then apply fixes. Prefers cleaner solutions but understands pragmatic workarounds when facing toolchain issues.

## 5. Future Ideas & Considerations (From AI or User)

*   **Database Migrations:** A significant missing piece for real-world application lifecycles.
*   **Full LINQ Support Documentation:** Critical for users.
*   **Write Performance Benchmarks:** To compare with other ORMs.
*   **Advanced Caching Strategies:** Explore query caching, distributed caching considerations.
*   **Tooling:** Potential for VS extensions or improved CLI diagnostics.
*   **Non-Relational Backend Support:** A long-term architectural consideration.
*   **(From AI):** Investigate using a custom MSBuild SDK for the `DataLinq` project to further encapsulate and simplify the complex packaging and dependency logic for the runtime + analyzer bundle.
*   **(From AI - Needs Update):** Resolve any outstanding integration issues with `SourceGenerator.Foundations` (e.g., the Rider/Contracts issue) to ensure smooth development experience across all IDEs.