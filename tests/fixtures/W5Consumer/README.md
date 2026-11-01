# W5 runtime and startup consumer

This non-friend executable compiles and executes the public validation surface on actual .NET 8, 9 and 10. It covers static/database method groups and optional/null/named arguments, common and provider-specific fluent registration, defaults and canonical keyed/default aliases, result/startup/read exception inspection, compiled-model SQLite roundtrips, captured Include, live drift, typed issues, empty/missing databases, and legacy capability rejection before touching metadata or I/O. Schema creation is explicit application setup before validation.

Generic Host exercises all three startup modes with matching/drifted schemas, both registration orders and both service-start concurrency settings. The .NET 10 application also uses real Kestrel: a public metadata callback pauses a native SQLite observation, verifies that workers have not started and TCP cannot connect, then verifies either a successful HTTP response or continued refusal after schema rejection. ASP.NET is referenced only by this fixture's .NET 10 target.

By default the fixture uses source projects for development. An exact `DataLinqCandidateVersion` switches all DataLinq inputs to packages. `Verify-W5ConsumerOutputs.ps1` checks exact restored archive/runtime hashes, executes all three framework builds and captures public signatures/defaults/nullability. Supply `PublishedDirectory` containing framework-dependent publishes under `net8.0`, `net9.0`, and `net10.0`. The verifier copies each to a fresh temporary directory outside the checkout, checks that source and Tools files are absent, and runs it both without tooling configuration and with deliberately invalid `datalinq.json` and `datalinq.user.json`. Both runs include direct and hosted pass/drift checks. Host connection configuration is application-owned, supplied in memory.

Use a fresh exact package candidate and retain restore/build/publish logs separately. The verifier records input/package/runtime hashes and actual runtime major versions; it is trusted-development evidence, not independent source-to-binary attestation. Run `Verify-W3ConsumerOutputs.ps1 -FixtureMode W5` and the unchanged W4 verifier against the same candidate as well. The explicit W5 mode permits the new surface without weakening the default W3/W4 guard or altering the original binding sources and locked 0.9.2 binary probe. The separate Memory executable continues to verify its SQL-free package graph and lack of a SQL provider/validation surface.

Development commands from the repository root:

```powershell
.\scripts\dotnet-sandbox.ps1 restore tests/fixtures/W5Consumer/W5Consumer.csproj
.\scripts\dotnet-sandbox.ps1 build tests/fixtures/W5Consumer/W5Consumer.csproj -c Release
.\scripts\dotnet-sandbox.ps1 tests/fixtures/W5Consumer/bin/Release/net8.0/W5Consumer.dll artifacts/w5-contract-net8.json
& .\artifacts\runtimes\dotnet9-9.0.20\dotnet.exe tests/fixtures/W5Consumer/bin/Release/net9.0/W5Consumer.dll artifacts/w5-contract-net9.json
.\scripts\dotnet-sandbox.ps1 tests/fixtures/W5Consumer/bin/Release/net10.0/W5Consumer.dll artifacts/w5-contract-net10.json
```
