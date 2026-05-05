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

That means the tested boundary is:

- generated SQLite database models
- generated metadata hooks
- generated mutable and immutable instance factories
- schema creation from generated metadata
- ordinary SQLite insert/query/relation/projection smoke behavior
- the documented LINQ subset used by the smoke path

That does not mean every DataLinq scenario is AOT-compatible. Reflection-discovered model metadata, arbitrary client projection expressions, and broad provider coverage are not public support claims.

The current blockers to a stronger claim are concrete:

- the runtime package still references Roslyn/compiler APIs
- `Remotion.Linq` still emits Native AOT and trimming warnings
- practical size reporting is not automated yet

## Blazor WebAssembly

The generated SQLite smoke path publishes and runs under Blazor WebAssembly AOT.

The no-AOT browser WebAssembly path is not supported for SQLite/DataLinq right now. The build can publish, but the Mono interpreter fails on the actual DataLinq/SQLite path. Treat that as unsupported until the runtime path is proven, not as a configuration issue.

The browser proof is also intentionally narrow:

- SQLite only
- generated models only
- in-memory smoke behavior only
- WebAssembly AOT only

It does not prove MySQL/MariaDB browser support, OPFS/file-backed browser storage, arbitrary LINQ, or a small production payload.

## What To Claim

Accurate:

> DataLinq has a proven generated SQLite Native AOT, trimmed publish, and Blazor WebAssembly AOT smoke boundary.

Not accurate yet:

> DataLinq is broadly AOT-compatible.

The second statement has to wait until the package graph and query dependency warnings are cleaned up.

For the engineering evidence, see the Phase 8 [Compatibility Results](dev-plans/roadmap-implementation/phase-8-native-aot-and-webassembly-readiness/Compatibility%20Results.md).
