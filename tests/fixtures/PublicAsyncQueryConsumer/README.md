# Public async query consumer

This executable deliberately has no friend-assembly access. It checks public query/prepared declarations, ordinary/static/alias calls, numeric/nullability return types, foreign-provider rejection and standard local async LINQ on .NET 8/9/10.

It also compiles lookup/reference calls, executes concrete/interface relation defaults, and exercises the new options, callback and inherited async-disposal defaults on a source-level legacy provider that implements no async members. Every synchronous member on that probe throws, so a hidden synchronous fallback fails the run.

It references the source project. It is **not** packed-consumer, old-binary, EF Core coexistence or full W3 compatibility evidence; those remain separate checks.

From the repository root:

```powershell
.\scripts\dotnet-sandbox.ps1 restore tests/fixtures/PublicAsyncQueryConsumer/PublicAsyncQueryConsumer.csproj
.\scripts\dotnet-sandbox.ps1 build tests/fixtures/PublicAsyncQueryConsumer/PublicAsyncQueryConsumer.csproj -c Debug
.\scripts\dotnet-sandbox.ps1 run --no-build --project tests/fixtures/PublicAsyncQueryConsumer -c Debug -f net8.0
.\scripts\dotnet-sandbox.ps1 run --no-build --project tests/fixtures/PublicAsyncQueryConsumer -c Debug -f net9.0
.\scripts\dotnet-sandbox.ps1 run --no-build --project tests/fixtures/PublicAsyncQueryConsumer -c Debug -f net10.0
```

Each run requires the matching runtime. Do not use major-version roll-forward as evidence of execution on a missing runtime. A separately installed workspace-local runtime can execute the corresponding built DLL directly.
