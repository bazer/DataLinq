# Platform Compatibility

DataLinq has a useful constrained-platform proof now, but the honest public claim is still narrow.

## Public Support Claim

The ordinary supported runtime path is .NET on server, desktop, or test hosts using generated DataLinq models with the SQLite, MySQL, or MariaDB providers. DataLinq 0.9.0 also ships `DataLinq.Memory` as a separate experimental, provider-free, read-only path. Use the [0.9.0 release notes](releases/0.9.md) for the current release boundary and the [changelog](../CHANGELOG.md) for published history.

Current package and repo builds target .NET 8, .NET 9, and .NET 10. Provider behavior is documented separately:

- [SQLite](backends/SQLite.md)
- [MySQL & MariaDB](backends/MySQL-MariaDB.md)
- [Memory (experimental)](backends/Memory.md)
- [Provider Metadata Support Matrix](support-matrices/Provider%20Metadata%20Support%20Matrix.md)
- [LINQ Translation Support Matrix](support-matrices/LINQ%20Translation%20Support%20Matrix.md)

For constrained platforms, the accurate claim is narrower:

> DataLinq has proven generated SQLite and provider-free Memory Native AOT, trimmed publish, and Blazor WebAssembly smoke boundaries for their documented query subsets. Runtime package dependency groups exclude Roslyn and Remotion, and SQLitePCLRaw `WASM0001` warnings remain a separate visible caveat on the SQLite browser graph.

Not accurate yet:

> DataLinq is broadly AOT-compatible.

The broad claim has to wait until provider coverage, query coverage, browser storage, and remaining third-party WebAssembly warning work are stronger.

## Compiler Host Compatibility

Runtime target frameworks and the compiler/source-generator host are different contracts.

The 0.9 source generator references `Microsoft.CodeAnalysis.CSharp` 5.0.0. Microsoft's supported mapping makes **Visual Studio 2026 version 18.0 the minimum supported Visual Studio host** for that Roslyn package generation. Visual Studio 2022 is therefore not a supported 0.9 source-generator host merely because the consuming project targets .NET 8 or .NET 9.

Command-line builds need a .NET SDK/compiler host containing Roslyn 5.0 or newer. The application output can still target .NET 8, .NET 9, or .NET 10; that runtime TFM says nothing about whether an older IDE compiler can load the analyzer.

The generator is packaged as a build-time analyzer under `analyzers/dotnet/cs`. Roslyn assemblies are excluded from the public runtime dependency groups, so this compiler-host minimum does not mean applications deploy Roslyn with DataLinq.

Authoritative mapping: [Microsoft's .NET compiler platform package version reference](https://learn.microsoft.com/en-us/visualstudio/extensibility/roslyn-version-support?view=visualstudio).

## Constrained-Platform Boundary

DataLinq has constrained-platform smoke projects for generated SQLite and Memory Native AOT, trimmed publish, Blazor WebAssembly AOT, and Blazor WebAssembly no-AOT.

The runtime package graph has also been cleaned up for the public runtime packages: Roslyn/compiler assemblies and `Remotion.Linq` are not runtime dependencies of `DataLinq`, `DataLinq.SQLite`, `DataLinq.MySql`, or `DataLinq.Memory`. The Memory package also has no SQL-provider or native-database dependency. The source generator is packaged under `DataLinq` analyzer assets, which is the right place for build-time code generation and the wrong place for runtime payload.

The 0.8 parser-removal work is no longer the compatibility blocker. The constrained smoke path executes the documented query subset through DataLinq's expression parser and query-plan SQL renderer instead of relying on a Remotion parser boundary.

That means the tested and packaged boundary is:

- generated SQLite database models
- generated metadata hooks
- generated mutable and immutable instance factories
- schema creation from generated metadata
- ordinary SQLite insert/update/delete/rollback and query/relation/projection smoke behavior
- the documented LINQ subset used by the smoke path
- runtime package dependency groups without `Microsoft.CodeAnalysis.*`
- runtime package dependency groups without `Remotion.Linq`
- generator assets under `analyzers/dotnet/cs`
- provider-free Memory construction, seeding, typed primary-key lookup, supported filtering/ordering/paging/projection, and deterministic unsupported-query diagnostics

## Non-Claims

The smoke boundary does not mean every DataLinq scenario is AOT-compatible. These are not public support claims:

- reflection-discovered model metadata on constrained platforms
- arbitrary client projection expressions
- MySQL or MariaDB browser/WebAssembly support
- Memory mutation, transactions, persistence, or arbitrary LINQ on constrained platforms
- OPFS or file-backed browser storage
- every possible LINQ expression shape
- small production browser payload size
- background memory-pressure cache cleanup in WebAssembly

The current caveats to a stronger claim are still concrete:

- Native AOT verification requires the local Native AOT platform toolchain; missing MSVC linker prerequisites are environment failures, not query-pipeline evidence
- browser runtime verification now runs through `size-report` for WebAssembly targets, and the generated metadata startup path that previously failed with `MONO_WASM: function signature mismatch` is fixed
- the final clean-output report passes on this machine, but WebAssembly clean-output stability still depends on the installed .NET SDK and workload state
- SQLitePCLRaw WebAssembly varargs warnings remain visible: native extension loading and direct raw configuration calls are outside the verified browser path; see the [configuration call-path audit](dev-plans/SQLite%20WebAssembly%20Configuration%20Boundary.md)
- generated SQLite smoke coverage is not broad provider coverage
- the LINQ translator is intentionally limited to the documented subset

## Blazor WebAssembly

Current `size-report` tooling can publish WebAssembly targets, serve the published output over HTTP, and run the smoke page in a headless Chromium-compatible browser through Playwright.

The current generated SQLite AOT browser evidence is positive for the narrow smoke boundary. The smoke covers generated metadata draft/definition construction, raw SQLite open/version/PRAGMAs, the keep-alive plus second-connection pattern, generated database construction, schema creation, committed insert/update/delete, transaction-local update and rollback, relation/projection queries, documented subset coverage, unsupported diagnostics, and parser route evidence. Publish success alone is still not browser proof; this claim depends on the browser smoke log.

The current no-AOT browser WebAssembly path also passes the same generated SQLite in-memory smoke boundary. Treat this as a narrow smoke result, not as a promise that every no-AOT browser configuration, storage mode, or query shape is supported.

The [SQLite WebAssembly configuration audit](dev-plans/SQLite%20WebAssembly%20Configuration%20Boundary.md) traces the exact dependency versions and records a clean-revision browser run for both variants. Normal generated CRUD/query execution avoids the warned native configuration entry points. Calling `SqliteConnection.LoadExtension` or SQLitePCLRaw configuration/log-hook APIs can reach unsupported signatures and is outside this boundary. The warnings are deliberately unsuppressed.

The provider-free Memory browser paths execute their own generated-model smoke without SQLitePCLRaw or a native database payload. They cover explicit seed, typed and direct `Guid` values, primary-key lookup, the documented predicate/order/page/projection/result subset, and one deterministic unsupported-query diagnostic. They do not add browser persistence or general LINQ support.

The intended browser proof is intentionally narrow when it passes:

- SQLite only
- generated models only
- in-memory smoke behavior only
- WebAssembly AOT as the release-priority browser support path, with no-AOT documented only at the same generated SQLite smoke boundary when current evidence remains green
- no background memory-pressure cache cleanup

It does not prove MySQL/MariaDB browser support, OPFS/file-backed browser storage, arbitrary LINQ, or a small production payload.

Memory-pressure cleanup is a server/desktop runtime feature. Browser/WebAssembly runtimes report it as unsupported and do not start the pressure cleanup worker, even if ordinary model-level cache cleanup metadata exists.

Payload numbers should be read from the compatibility size report with symbol files excluded, and symbol packages should be treated as separate release artifacts. Counting `.pdb` or `.snupkg` payload as deployed constrained-platform runtime size is misleading accounting.

## Verification Evidence

The [0.9.0 GitHub release](https://github.com/bazer/DataLinq/releases/tag/0.9.0) is the current durable published boundary. Its final package-backed constrained-runtime gate passed all eight canonical generated SQLite and provider-free Memory targets: Native AOT, trimmed publish, browser WebAssembly without AOT, and browser WebAssembly with AOT for each backend. The known SQLitePCLRaw `WASM0001` warnings remain visible and third-party-scoped. The package hashes, exact commit, gate disposition, and explicit GO are recorded in [issue #80](https://github.com/bazer/DataLinq/issues/80).

The [0.8 GitHub release](https://github.com/bazer/DataLinq/releases/tag/0.8.0) remains the historical SQLite-only constrained-runtime boundary. Repo-local `artifacts/...` paths mentioned in maintainer records are not website downloads and are not presented here as public package evidence.

Maintainers reproduce the boundary with `DataLinq.Dev.CLI` `size-report` and `package-report`, the constrained-platform smoke projects, clean output, release thresholds, banned-payload checks, browser execution, package hashes, and commit/package identity validation.
