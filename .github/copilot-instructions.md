# DataLinq Project: Context, Learnings, and Status for AI Assistant

**Last Updated:** 2026-03-31
**Last Discussed Topics:** Released `v0.6.7`, added and validated a local `publish-nuget.ps1` workflow, fixed `DataLinq` symbol package contents, cleaned up stale CLI tool install behavior, and drafted the `v0.6.7` release message from the detailed git log.

## 1. Project Overview & Core Concepts

### 1.1. Purpose & Vision
DataLinq is a lightweight, high-performance .NET ORM focused on efficient read operations and low memory allocations. It uses immutable data models, source generation to reduce boilerplate, and a provider-based architecture for multiple database backends.

### 1.2. Key Architectural Pillars
*   **Immutability:** Data is represented by immutable objects for thread-safety and predictable behavior.
*   **Controlled Mutation:** Writes happen through transactional workflows and mutable wrappers created from immutable entities.
*   **Source Generation (`DataLinq.Generators`):** Reads abstract partial model classes and generates concrete immutable, mutable, interface, and helper code.
*   **Caching & Notification System:**
    *   `CacheNotificationManager` in `TableCache.cs` is the primary mechanism for invalidating relation caches.
    *   Current preferred pattern: write operations (`Subscribe`, `Clean`) use a locked mutable collection, while the hot `Notify` path reads from a `volatile` snapshot to stay lock-free.
    *   Core caches include `RowCache` (primary key to instance) and `IndexCache` (foreign key to primary keys).
*   **LINQ Provider (`DataLinq.Linq`):**
    *   The provider is centered around `QueryBuilder`, with expression traversal and SQL construction separated more cleanly than in earlier versions.
    *   The system supports member comparisons, string functions, date/time members, chained string calls, and increasingly optimized query execution paths.
    *   `Remotion.Linq` is used as a foundation.
*   **Database Providers & CLI:** Modular providers exist for MySQL, MariaDB, and SQLite. The CLI scaffolds models from schemas and can generate SQL from models.
*   **Configuration:** Uses `datalinq.json` and `datalinq.user.json`.

### 1.3. Coding Styles & Libraries
*   **Language & Targets:** Runtime projects are multi-targeted via `src/Directory.Build.props` for `net8.0`, `net9.0`, and `net10.0`. The source generator targets `netstandard2.0`.
*   **C# Settings:** Nullable is enabled and the repository currently uses C# language version `14.0`.
*   **Error Handling:** Uses `Option<TSuccess, TFailure>` (`ThrowAway.Option`) with `DLOptionFailure`.
*   **Key NuGet Dependencies:** `Microsoft.CodeAnalysis.CSharp`, `Remotion.Linq`, `ThrowAway`, `Microsoft.Data.Sqlite`, database connectors, `CommandLineParser`, `Bogus`, and `xUnit`.

## 2. Project Structure & Build Learnings

*   `DataLinq.SharedCore` contains shared types that are linked into the main runtime and generator projects.
*   `DataLinq.Generators` is the Roslyn source generator.
*   `DataLinq` is the main runtime package and manually packages the generator into `analyzers/`.
*   `DataLinq` should let normal build output flow into the package so `.snupkg` files include real PDBs; only the analyzer payload should be added manually.
*   Provider projects include `DataLinq.MySql` and `DataLinq.SQLite`.
*   Supporting projects include `DataLinq.Tools`, `DataLinq.CLI`, `DataLinq.Benchmark`, and several test projects.
*   Development plans and architectural notes now live under `docs/dev-plans/`.
*   `CHANGELOG.md` is generated from GitHub releases by `generate-changelog.ps1`, which calls the GitHub Releases API and uses each release tag's commit date as the displayed release date.
*   `gitlog-detailed.md` is a local helper artifact used to summarize commits between releases when preparing release notes.
*   Local NuGet publishing now goes through `publish-nuget.ps1`, which packs `DataLinq`, `DataLinq.SQLite`, `DataLinq.MySql`, `DataLinq.CLI`, and `DataLinq.Tools` into a fresh staging directory and pushes `.nupkg` and `.snupkg` files explicitly.

## 3. Current Status & Focus Areas

### 3.1. Recently Released
*   **v0.6.5**
    *   Added explicit multi-targeting for .NET 8, 9, and 10.
    *   Improved LINQ support for chained string operations and collection handling.
    *   Refined handling of expression evaluation and transaction tests across providers.
*   **v0.6.6**
    *   Optimized primary-key lookups by extracting simple PK predicates and short-circuiting common entity reads.
    *   Added a faster local-variable evaluation path in the LINQ evaluator and cached standard identity projections in `QueryExecutor`.
    *   Reworked `RowData` to use indexed array storage instead of dictionary-based storage, reducing overhead and improving access speed.
    *   Improved SQLite logging propagation by threading logging configuration more consistently through SQLite database and transaction classes.
    *   Refactored `ModelGenerator`, refreshed package dependencies, and added a large set of planning/specification docs for upcoming releases.

### 3.2. Immediate Release Direction
*   **`v0.6.7` has shipped** and was correctly treated as a bugfix/stability release rather than a feature release.
*   The release centered on source-generator modernization, default-value correctness across providers, SQLite/MySQL/MariaDB parsing and SQL fixes, documentation cleanup, and NuGet release tooling.
*   There are many forward-looking design documents for `v0.7` and `v0.8`, but short-term work should stay grounded in bug fixing, stability, packaging reliability, and polishing existing behavior unless the user explicitly shifts priorities.

### 3.3. Longer-Term Roadmap Notes
Recent planning documents cover:
*   batched mutations and optimistic concurrency
*   in-memory provider support
*   JSON data type support
*   metadata architecture
*   migrations and validation
*   performance benchmarking
*   projections and views
*   query pipeline abstraction
*   source generator optimizations
*   SQL generation optimization
*   result-set caching
*   testing infrastructure
*   application architecture patterns and memory management

## 4. AI Assistant Learnings & Preferences

*   **Refactoring is often the right fix:** The project has repeatedly benefited from centralizing logic instead of layering more patches onto brittle code.
*   **Provider-specific SQL belongs in provider code:** Differences between MySQL, MariaDB, and SQLite should be handled in the provider-specific layer, not hidden in shared generic logic.
*   **Centralize metadata logic when possible:** Database-specific factories should identify metadata; the central metadata pipeline should build and transform it.
*   **Mind `netstandard2.0` limitations in generator/shared code:** Avoid newer APIs in code that must run in the generator context unless compatibility is guaranteed.
*   **Distinguish CLI generation from source generation:**
    1.  The CLI (`DataLinq.Tools` / `datalinq create-models`) reads config files and writes abstract model code to disk.
    2.  The Roslyn generator reads source in-memory and does not read `datalinq.json`.
*   **Be precise with nullability generation:** Value types need `?` when nullable; reference types should only get nullable annotations when NRTs are intended and enabled.
*   **Expression chain traversal matters:** Chained member/method calls in LINQ often require walking back to the root database column before building SQL.
*   **Prefer targeted fixes:** Small, surgical changes are safer than broad rewrites unless the subsystem is already clearly the wrong shape.
*   **Test-driven correction works well here:** Adding focused regression tests is usually the cleanest way to lock in behavior and understand intent.
*   **Provider behavior can differ subtly:** Transaction visibility, GUID handling, SQL syntax, and function support may differ between SQLite and MySQL/MariaDB, so assumptions should be validated in provider-specific tests.
*   **Performance work often centers on hot paths:** Query execution, row materialization, cache invalidation, and key/index handling are recurring hotspots worth checking before assuming a bug is elsewhere.
*   **Release notes workflow:** When asked to update release notes, prefer drafting the GitHub release body first, then regenerate `CHANGELOG.md` with `generate-changelog.ps1` rather than editing the changelog manually as the final source of truth.
*   **Manual release workflow is preferred:** The user wants to run NuGet publishes personally; helpers should improve the local release flow rather than assuming CI-first publishing.
*   **Prompt for release secrets, do not persist them by default:** For local/manual publishing, prompting for the NuGet API key at execution time is preferred over recommending long-lived environment variables or `NuGet.Config` storage.
*   **Be careful with `DOTNET_CLI_HOME`:** If a script temporarily sets `DOTNET_CLI_HOME`, it should restore the previous environment afterward; leaking it can redirect later `dotnet tool --global` installs and uninstalls into confusing locations.

## 5. Practical Tips

*   Check `.github/copilot-instructions.md` and `CHANGELOG.md` first when you need quick project context.
*   Look in `docs/dev-plans/` before proposing larger architectural changes, because many future ideas are already documented there.
*   For release-note work, use `gitlog-detailed.md`, `git log`, and the existing GitHub release style to separate real user-facing changes from internal planning/docs commits.
*   `generate-detailed-gitlog.ps1` validates end tags against GitHub releases, not just local git tags. When drafting notes for a local tag that has not been published as a GitHub release yet, use the previous release tag to `HEAD`.
*   When working on SQLite, remember that logging/configuration and in-memory behavior have both been active areas of change.

## 6. Future Ideas & Long-Term

*   Database migrations
*   Advanced caching and result-set caching
*   Better tooling and developer ergonomics
*   Non-relational backends
