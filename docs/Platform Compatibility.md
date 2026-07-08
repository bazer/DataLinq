# Platform Compatibility

DataLinq has a useful constrained-platform proof now, but the honest public claim is still narrow.

## Public Support Claim

The ordinary supported runtime path is .NET on server, desktop, or test hosts using generated DataLinq models with the SQLite, MySQL, or MariaDB providers.

Current package and repo builds target .NET 8, .NET 9, and .NET 10. Provider behavior is documented separately:

- [SQLite](backends/SQLite.md)
- [MySQL & MariaDB](backends/MySQL-MariaDB.md)
- [Provider Metadata Support Matrix](support-matrices/Provider%20Metadata%20Support%20Matrix.md)
- [LINQ Translation Support Matrix](support-matrices/LINQ%20Translation%20Support%20Matrix.md)

For constrained platforms, the accurate claim is narrower:

> DataLinq has a proven generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT smoke boundary for the documented query subset, keeps Roslyn and Remotion out of runtime package dependency groups, and records SQLitePCLRaw `WASM0001` warnings as a separate release-evidence caveat.

Not accurate yet:

> DataLinq is broadly AOT-compatible.

The broad claim has to wait until provider coverage, query coverage, browser storage, and remaining third-party WebAssembly warning work are stronger.

## Constrained-Platform Boundary

DataLinq has constrained-platform smoke projects for generated SQLite Native AOT, trimmed publish, Blazor WebAssembly AOT, and Blazor WebAssembly no-AOT.

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

## Non-Claims

The smoke boundary does not mean every DataLinq scenario is AOT-compatible. These are not public support claims:

- reflection-discovered model metadata on constrained platforms
- arbitrary client projection expressions
- MySQL or MariaDB browser/WebAssembly support
- OPFS or file-backed browser storage
- every possible LINQ expression shape
- small production browser payload size
- background memory-pressure cache cleanup in WebAssembly

The current caveats to a stronger claim are still concrete:

- Native AOT verification requires the local Native AOT platform toolchain; missing MSVC linker prerequisites are environment failures, not query-pipeline evidence
- browser runtime verification now runs through `size-report` for WebAssembly targets, and the generated metadata startup path that previously failed with `MONO_WASM: function signature mismatch` is fixed
- the final clean-output report passes on this machine, but WebAssembly clean-output stability still depends on the installed .NET SDK and workload state
- SQLitePCLRaw WebAssembly varargs warning disposition still needs a broader call-graph answer when fresh publishes emit `WASM0001`
- generated SQLite smoke coverage is not broad provider coverage
- the LINQ translator is intentionally limited to the documented subset

## Blazor WebAssembly

Current `size-report` tooling can publish WebAssembly targets, serve the published output over HTTP, and run the smoke page in a headless Chromium-compatible browser through Playwright.

The current generated SQLite AOT browser evidence is positive for the narrow smoke boundary. The smoke covers generated metadata draft/definition construction, raw SQLite open/version/PRAGMAs, the keep-alive plus second-connection pattern, generated database construction, schema creation, insert, relation/projection queries, documented subset coverage, unsupported diagnostics, and parser route evidence. Publish success alone is still not browser proof; this claim depends on the browser smoke log.

The current no-AOT browser WebAssembly path also passes the same generated SQLite in-memory smoke boundary. Treat this as a narrow smoke result, not as a promise that every no-AOT browser configuration, storage mode, or query shape is supported.

The intended browser proof is intentionally narrow when it passes:

- SQLite only
- generated models only
- in-memory smoke behavior only
- WebAssembly AOT as the release-priority browser support path, with no-AOT documented only at the same generated SQLite smoke boundary when current evidence remains green
- no background memory-pressure cache cleanup

It does not prove MySQL/MariaDB browser support, OPFS/file-backed browser storage, arbitrary LINQ, or a small production payload.

Memory-pressure cleanup is a server/desktop runtime feature. Browser/WebAssembly runtimes report it as unsupported and do not start the pressure cleanup worker, even if ordinary model-level cache cleanup metadata exists.

Payload numbers should be read from the compatibility size report with symbol files excluded, and symbol packages should be treated as separate release artifacts. Counting `.pdb` or `.snupkg` payload as deployed constrained-platform runtime size is misleading accounting.

## Maintainer Verification Evidence

Release evidence is repo-local unless it has been copied into release notes or the changelog. The final 0.8 local compatibility report path was:

```text
artifacts/dev/compat-size-report/20260630-131026977/report.md
```

That report used:

```bash
--clean-output --release-thresholds --fail-on-threshold --fail-on-banned-payload
```

It passed for Native AOT, trimmed publish, WebAssembly no-AOT, and WebAssembly AOT on that SDK/workload setup. It also verified WebAssembly browser execution through the `verifying-strict-parser-projection` smoke stage.

The detailed engineering notes live in the repo's internal `docs/dev-plans` tree. The public verification hooks are the repo-local `DataLinq.Dev.CLI` `size-report` and `package-report` commands plus the constrained-platform smoke projects that back this narrow claim.
