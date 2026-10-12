# W4 host consumer

This non-friend executable exercises generated services through real application hosts. Generic Host runs on .NET 8, 9 and 10; the HTTP fixture uses ASP.NET Core 10. All targets compile with C# 12 and warnings as errors.

The worker fixture verifies lazy database creation, cancellation during an admitted write, confirmed rollback and session cleanup before root shutdown. It exercises both `Host.Dispose` and `Host.DisposeAsync`. Microsoft Hosting dispatches asynchronous root cleanup in both cases; direct container synchronous disposal is tested separately by `W4SessionConsumer` and the unit suites.

The HTTP fixture starts Kestrel on an ephemeral loopback port. Sixteen concurrent requests share a singleton generated service while retaining distinct sessions and transactions. Aborting an admitted write verifies rollback and cleanup; graceful shutdown waits for another admitted request to commit before database disposal. The fixture also checks automatic host logging integration. Schema creation is explicit fixture setup, not runtime startup validation.

From the repository root:

```powershell
.\scripts\dotnet-sandbox.ps1 restore tests/fixtures/W4HostConsumer/W4HostConsumer.csproj
.\scripts\dotnet-sandbox.ps1 build tests/fixtures/W4HostConsumer/W4HostConsumer.csproj -c Release
.\scripts\dotnet-sandbox.ps1 tests/fixtures/W4HostConsumer/bin/Release/net8.0/W4HostConsumer.dll
& .\artifacts\runtimes\dotnet9-9.0.20\dotnet.exe tests/fixtures/W4HostConsumer/bin/Release/net9.0/W4HostConsumer.dll
.\scripts\dotnet-sandbox.ps1 tests/fixtures/W4HostConsumer/bin/Release/net10.0/W4HostConsumer.dll
```

Use an actual .NET 9 host; major-version roll-forward does not verify that runtime. By default the fixture references source projects. An exact `DataLinqCandidateVersion` selects package references for independent packed-consumer verification. Hosting and ASP.NET dependencies belong to this application fixture, not DataLinq's runtime packages.
