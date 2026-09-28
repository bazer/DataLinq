# W3 package and old-binary consumers

These fixtures have no friend-assembly access or DataLinq project references. They complement the generated-symbol/unit/provider tests and the strict `api-report`; they do not replace them.

| Fixture | Purpose |
| --- | --- |
| `Legacy092Consumer` | Builds a DLL against exact 0.9.2 core/SQLite packages. Contains synchronous-only interface implementations and subclasses, a static-interface implementer, and a generated SQLite model with synchronous insert/query/transaction execution. |
| `AsyncPackageConsumer` | Loads the unchanged matching-TFM old DLL into a candidate-package process. Exercises new unsupported defaults, old generated execution, the static lookup metadata contract, and public/generated async SQLite calls. Also compiles/runs the existing non-friend public source consumer. |
| `AsyncEfCoexistence` | Uses pinned EF Core 9.0.5 and DataLinq imports. Static aliases compile and reject foreign query providers for scalar, nullable, anonymous, interface and DTO query receivers. `ExpectAmbiguity=true` must fail with CS0121. This fixture does not run an EF database. |
| `AsyncMemoryConsumer` | Resolves core and Memory only. Exercises supported async query/lookup, cancellation, unsupported aggregation and a separate graph mock. Its package graph must contain no SQLite/MySQL/MariaDB dependency. |

All four target net8.0/net9.0/net10.0. The linked simple model is shared with the existing package smoke. These cases do not by themselves prove all composite/converted-key, navigation, DLG004, callback or constructor forms; the W3 evidence matrix must retain the corresponding generator, unit and provider cases.

## Restore and build

First pack a fresh candidate with `publish-nuget.ps1 -PackOnly`, then run the repository `package-report` and `api-report`. Preserve the raw compatibility report, including approved breaks. Never substitute a project build for package evidence.

Use separate NuGet configurations and package caches for baseline and candidate. Each configuration must clear inherited sources and map `DataLinq*` exclusively to its respective local package directory; other dependencies can use NuGet.org. Example configuration, replacing `LOCAL_PACKAGE_DIRECTORY` with an absolute path:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="datalinq-local" value="LOCAL_PACKAGE_DIRECTORY" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="datalinq-local"><package pattern="DataLinq*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

From the repository root, with the exact candidate version and configurations supplied:

```powershell
$candidateVersion = '0.10.0-w3.78d1b3c4'
.\scripts\dotnet-sandbox.ps1 restore tests/fixtures/Legacy092Consumer/Legacy092Consumer.csproj --configfile artifacts/w3-consumers/78d1b3c4/baseline.config --packages artifacts/w3-consumers/78d1b3c4/packages-baseline
.\scripts\dotnet-sandbox.ps1 build tests/fixtures/Legacy092Consumer/Legacy092Consumer.csproj -c Release

foreach ($fixture in 'AsyncPackageConsumer','AsyncMemoryConsumer','AsyncEfCoexistence') {
    $project = "tests/fixtures/$fixture/$fixture.csproj"
    .\scripts\dotnet-sandbox.ps1 restore $project "-p:DataLinqCandidateVersion=$candidateVersion" --configfile artifacts/w3-consumers/78d1b3c4/candidate.config --packages artifacts/w3-consumers/78d1b3c4/packages-candidate
    if ($LASTEXITCODE -ne 0) { throw "Restore failed: $fixture" }
    .\scripts\dotnet-sandbox.ps1 build $project "-p:DataLinqCandidateVersion=$candidateVersion" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $fixture" }
}
```

Run `Verify-W3ConsumerOutputs.ps1` after successful builds. It checks package hashes against the selected archives and the locked baseline, records source/assets and executable hashes, checks the transitive async LINQ version on 8/9 and its absence on 10, rejects SQL dependencies in the Memory graph, and verifies the executed core DLL matches the selected restored package. It copies each old DLL once into the evidence directory and runs all nine fixture/runtime combinations without rebuilding it. Runtime-major checks prevent a major-version roll-forward from posing as missing-runtime evidence. The output directory must be fresh.

```powershell
.\tests\fixtures\Verify-W3ConsumerOutputs.ps1 `
  -CandidateDirectory artifacts/nuget-release/0.10.0-w3.78d1b3c4 `
  -CandidateVersion 0.10.0-w3.78d1b3c4 `
  -BaselineDirectory artifacts/api-baseline/w0-20260916/0.9.2 `
  -DotNet9 artifacts/runtimes/dotnet9-9.0.20/dotnet.exe `
  -OutputDirectory artifacts/w3-consumers/78d1b3c4/verified
```

Finally compile the intentional ambiguity probe. Require a nonzero exit and CS0121 for the `CountAsync` call on each TFM; unrelated errors do not count. Keep its output separately from the positive consumer executions.

```powershell
.\scripts\dotnet-sandbox.ps1 build tests/fixtures/AsyncEfCoexistence/AsyncEfCoexistence.csproj -p:DataLinqCandidateVersion=0.10.0-w3.78d1b3c4 -p:ExpectAmbiguity=true -c Release
```

The verifier is a local trusted-development check of prebuilt fixtures. It records source hashes but does not independently attest the source-to-binary build, toolchain or a clean checkout. Retain build logs and clean candidate provenance separately. Existing `package-smoke` provides its own isolated restore/build receipts; these additional probes cover different behavior.
