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

DataLinq has constrained-platform smoke projects for generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT. Current local compatibility reporting proves the generated SQLite Native AOT and trimmed publish paths on Windows when the .NET WebAssembly workload and Visual Studio C++ linker toolchain are installed.

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

The current blockers to a stronger claim are still concrete:

- Native AOT verification requires the local Native AOT platform toolchain; missing MSVC linker prerequisites are environment failures, not query-pipeline evidence
- browser runtime verification is not automated by `size-report`; WebAssembly publish success is not the same thing as a browser smoke run
- SQLitePCLRaw WebAssembly AOT varargs warning disposition still needs a clean call-graph answer when fresh publishes emit `WASM0001`
- generated SQLite smoke coverage is not broad provider coverage
- the LINQ translator is intentionally limited to the documented subset

## Blazor WebAssembly

The generated SQLite smoke path has published and run under Blazor WebAssembly AOT in the Phase 8 browser proof. Current `size-report` output refreshes the publish and payload evidence, but reports browser smoke as not automated.

The no-AOT browser WebAssembly path is not supported for SQLite/DataLinq right now. The build can publish, but the Mono interpreter fails on the actual DataLinq/SQLite path. Treat that as unsupported until the runtime path is proven, not as a configuration issue.

The browser proof is also intentionally narrow:

- SQLite only
- generated models only
- in-memory smoke behavior only
- WebAssembly AOT only
- no background memory-pressure cache cleanup

It does not prove MySQL/MariaDB browser support, OPFS/file-backed browser storage, arbitrary LINQ, or a small production payload.

Memory-pressure cleanup is a server/desktop runtime feature. Browser/WebAssembly runtimes report it as unsupported and do not start the pressure cleanup worker, even if ordinary model-level cache cleanup metadata exists.

Payload numbers should be read from the compatibility size report with symbol files excluded, and symbol packages should be treated as separate release artifacts. Counting `.pdb` or `.snupkg` payload as deployed constrained-platform runtime size is misleading accounting.

## What To Claim

Accurate:

> DataLinq has a proven generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT smoke boundary for the documented query subset, keeps Roslyn and Remotion out of runtime package dependency groups, and treats browser/no-AOT/provider expansion as separate compatibility work.

Not accurate yet:

> DataLinq is broadly AOT-compatible.

The second statement has to wait until Native AOT toolchain proof, broader provider/query coverage, and remaining third-party WebAssembly warning work are cleaned up.

The detailed engineering notes live in the repo's internal `docs/dev-plans` tree. The public verification hooks are the repo-local `DataLinq.Dev.CLI` `size-report` and `package-report` commands plus the constrained-platform smoke projects that back this narrow claim.
