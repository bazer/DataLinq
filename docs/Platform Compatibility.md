# Platform Compatibility

DataLinq has a useful constrained-platform proof now, but the honest public claim is still narrow.

## Supported Runtime Baseline

The ordinary supported runtime path is .NET on server, desktop, or test hosts using generated DataLinq models with the SQLite, MySQL, or MariaDB providers.

Current package and repo builds target .NET 8, .NET 9, and .NET 10. Provider behavior is documented separately:

- [SQLite](backends/SQLite.md)
- [MySQL & MariaDB](backends/MySQL-MariaDB.md)
- [Provider Metadata Support Matrix](support-matrices/Provider%20Metadata%20Support%20Matrix.md)
- [LINQ Translation Support Matrix](support-matrices/LINQ%20Translation%20Support%20Matrix.md)

## Native AOT And Trimming

DataLinq has constrained-platform smoke projects for generated SQLite Native AOT, trimmed publish, Blazor WebAssembly AOT, and Blazor WebAssembly no-AOT. Current local compatibility reporting proves the generated SQLite Native AOT and trimmed publish paths on Windows when the .NET WebAssembly workload and Visual Studio C++ linker toolchain are installed. Phase 23 also proves the narrow generated SQLite browser smoke under WebAssembly AOT and no-AOT from repeatable Playwright-backed reports.

The runtime package graph has also been cleaned up for the public runtime packages: Roslyn/compiler assemblies and `Remotion.Linq` are not runtime dependencies of `DataLinq`, `DataLinq.SQLite`, or `DataLinq.MySql`. The source generator is packaged under `DataLinq` analyzer assets, which is the right place for build-time code generation and the wrong place for runtime payload.

The 0.8 parser-removal work is no longer the compatibility blocker. The constrained smoke path executes the documented query subset through DataLinq's expression parser and query-plan SQL renderer instead of relying on a Remotion parser boundary.

That means the tested and packaged boundary is:

- generated SQLite database models
- generated metadata hooks
- generated mutable and immutable instance factories
- schema creation from generated metadata
- ordinary SQLite insert/query/relation/projection smoke behavior
- the documented LINQ subset used by the smoke path
- runtime package dependency groups without `Microsoft.CodeAnalysis.*`
- runtime package dependency groups without `Remotion.Linq`
- generator assets under `analyzers/dotnet/cs`

That does not mean every DataLinq scenario is AOT-compatible. Reflection-discovered model metadata, arbitrary client projection expressions, broad provider coverage, and every possible LINQ expression shape are not public support claims.

The current caveats to a stronger claim are still concrete:

- Native AOT verification requires the local Native AOT platform toolchain; missing MSVC linker prerequisites are environment failures, not query-pipeline evidence
- browser runtime verification now runs through `size-report` for WebAssembly targets, and Phase 23 fixed the generated metadata startup path that previously failed with `MONO_WASM: function signature mismatch`
- clean-output WebAssembly publishes still fail before browser execution with the Blazor SDK `ResolveWasmOutputs` target issue, so final release evidence must keep that separate from runtime support
- SQLitePCLRaw WebAssembly varargs warning disposition still needs a broader call-graph answer when fresh publishes emit `WASM0001`
- generated SQLite smoke coverage is not broad provider coverage
- the LINQ translator is intentionally limited to the documented subset

## Blazor WebAssembly

Current `size-report` tooling can publish WebAssembly targets, serve the published output over HTTP, and run the smoke page in a headless Chromium-compatible browser through Playwright.

The current generated SQLite AOT browser evidence is positive for the narrow smoke boundary. A host-side `wasm-aot` report at `artifacts/dev/compat-size-report/20260629-210510424/` publishes successfully, serves the app, opens Edge through Playwright, and reaches `passed` at `verifying-strict-parser-projection`. The smoke covers generated metadata draft/definition construction, raw SQLite open/version/PRAGMAs, the keep-alive plus second-connection pattern, generated database construction, schema creation, insert, relation/projection queries, and parser route evidence. Publish success alone is still not browser proof; this claim depends on the browser smoke log.

The current no-AOT browser WebAssembly path also passes the same generated SQLite in-memory smoke boundary. The host-side `wasm` report at `artifacts/dev/compat-size-report/20260629-205114951/` publishes successfully and reaches the same `verifying-strict-parser-projection` browser stage. Treat this as a narrow current smoke result, not as a promise that every no-AOT browser configuration, storage mode, or query shape is supported.

Clean-output WebAssembly publishes are a separate problem. Both `wasm-aot` and `wasm` clean-output reports can fail before browser execution with `MSB4057` for missing `ResolveWasmOutputs`; those failures are classified as SDK/WebAssembly toolchain evidence rather than DataLinq query/runtime evidence.

The intended browser proof is also intentionally narrow when it passes:

- SQLite only
- generated models only
- in-memory smoke behavior only
- WebAssembly AOT as the release-priority browser support path, with no-AOT documented only at the same generated SQLite smoke boundary when current evidence remains green
- no background memory-pressure cache cleanup

It does not prove MySQL/MariaDB browser support, OPFS/file-backed browser storage, arbitrary LINQ, or a small production payload.

Memory-pressure cleanup is a server/desktop runtime feature. Browser/WebAssembly runtimes report it as unsupported and do not start the pressure cleanup worker, even if ordinary model-level cache cleanup metadata exists.

Payload numbers should be read from the compatibility size report with symbol files excluded, and symbol packages should be treated as separate release artifacts. Counting `.pdb` or `.snupkg` payload as deployed constrained-platform runtime size is misleading accounting.

## What To Claim

Accurate:

> DataLinq has a proven generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT smoke boundary for the documented query subset, keeps Roslyn and Remotion out of runtime package dependency groups, and records clean-output WebAssembly SDK failures plus SQLitePCLRaw `WASM0001` warnings as separate release-evidence caveats.

Not accurate yet:

> DataLinq is broadly AOT-compatible.

The second statement has to wait until Native AOT toolchain proof, broader provider/query coverage, and the remaining third-party WebAssembly warning/toolchain work are cleaned up.

The detailed engineering notes live in the repo's internal `docs/dev-plans` tree. The public verification hooks are the repo-local `DataLinq.Dev.CLI` `size-report` and `package-report` commands plus the constrained-platform smoke projects that back this narrow claim.
