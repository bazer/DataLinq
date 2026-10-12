# W4 session consumer

This non-friend executable checks the participant, owner and factory contracts against actual .NET 8/9/10 runtimes. It exercises generated mutation helpers, transaction-local prepared scalar/row/sequence queries, invocation snapshots, nullable/no-value results, rollback intent, owner-controlled completion and factory-owned callbacks. `Bindings.cs` compiles the same sync/async/named/static/method-group expressions with base, typed, inferred and participant transaction types. The generator tests compile those exact files with C# 12/13/14 and nullable analysis enabled/disabled. The fixture also links the unchanged W3 generated-mutation consumer source.

From the repository root:

```powershell
.\scripts\dotnet-sandbox.ps1 restore tests/fixtures/W4SessionConsumer/W4SessionConsumer.csproj
.\scripts\dotnet-sandbox.ps1 build tests/fixtures/W4SessionConsumer/W4SessionConsumer.csproj -c Release
.\scripts\dotnet-sandbox.ps1 tests/fixtures/W4SessionConsumer/bin/Release/net8.0/W4SessionConsumer.dll
.\scripts\dotnet-sandbox.ps1 tests/fixtures/W4SessionConsumer/bin/Release/net10.0/W4SessionConsumer.dll
```

Run the net9.0 output with an actual .NET 9 host. The development checkout's W3 verifier uses `artifacts/runtimes/dotnet9-9.0.20/dotnet.exe`; a major-version roll-forward is not evidence of .NET 9 execution.

`RegistrationConsumer.cs` uses the public registration API and Microsoft DI 10.0.11 to check lazy construction, keyed/default identity, scope survival, sync/async shutdown and a custom provider whose disposal is deliberately counted before its own guard. It also compiles a generated read model with authored disposal: borrowed aliases must receive zero container disposal calls, and an owned registration must reject that unsupported combination.

`ConfigurationConsumer.cs` exercises typed SQLite configuration, named connection-string replacement of a bound literal, first-resolution capture, independent keyed settings, default aliases and factory execution. The fixture uses the application's configuration implementation; DataLinq itself references only the configuration abstractions.

`ServiceBindings.cs` is shared with the generator compilation tests. It covers primary and traditional constructor factories, optional/keyed constructor dependencies, ordinary/custom/native/no-value sync/async results, named/default/token calls and explicit result-aware opt-in. `ServiceConsumer.cs` executes those generated entry points, including differently bound standalone/participant factories, prepared participation, mapped nested vetoes, nullable/default result handling and concurrent independent owned sessions. Service lifetime registration is verified separately as that layer is added.

`ServiceRegistrationConsumer.cs` uses the public provider builder's singleton/scoped/transient service methods. It checks both default-selection orders, scoped and singleton alias identity, different same-model registration factories, ordinary optional dependencies and exactly one authored disposal invocation for each disposable transient activation under both shutdown modes.

By default this fixture references the current source projects. Supplying an exact `DataLinqCandidateVersion` selects package references instead, for later W4 packed-consumer verification. Source-project results alone do not prove the packed dependency graph or analyzer payload.
