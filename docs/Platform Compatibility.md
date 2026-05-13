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

DataLinq has a proven generated SQLite smoke path for Native AOT and trimmed publish.

The runtime package graph has also been cleaned up for the public runtime packages: Roslyn/compiler assemblies are not runtime dependencies of `DataLinq`, `DataLinq.SQLite`, or `DataLinq.MySql`. The source generator is packaged under `DataLinq` analyzer assets, which is the right place for build-time code generation and the wrong place for runtime payload.

That means the tested and packaged boundary is:

- generated SQLite database models
- generated metadata hooks
- generated mutable and immutable instance factories
- schema creation from generated metadata
- ordinary SQLite insert/query/relation/projection smoke behavior
- the documented LINQ subset used by the smoke path
- runtime package dependency groups without `Microsoft.CodeAnalysis.*`
- generator assets under `analyzers/dotnet/cs`

That does not mean every DataLinq scenario is AOT-compatible. Reflection-discovered model metadata, arbitrary client projection expressions, and broad provider coverage are not public support claims.

The current blockers to a stronger claim are still concrete:

- the query pipeline still uses `Remotion.Linq` outside a dedicated generated/AOT query boundary
- `Remotion.Linq` still emits Native AOT and trimming warnings
- SQLitePCLRaw WebAssembly AOT warning cleanup is deferred
- generated SQLite smoke coverage is not broad provider coverage

## Blazor WebAssembly

The generated SQLite smoke path publishes and runs under Blazor WebAssembly AOT.

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

> DataLinq has a proven generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT smoke boundary, with Roslyn kept out of the runtime package dependency groups.

Not accurate yet:

> DataLinq is broadly AOT-compatible.

The second statement has to wait until the query dependency boundary and remaining third-party warning work are cleaned up.

For the engineering evidence, see the Phase 8 [Compatibility Results](dev-plans/archive/roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md) and the repo-local `DataLinq.Dev.CLI` `size-report` and `package-report` commands.
