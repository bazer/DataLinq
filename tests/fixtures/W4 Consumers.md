# W4 package consumers

Use the [W3 package workflow](W3%20Consumers.md) for the locked 0.9.2 binary, public async, Memory-only and EF coexistence probes. W4 adds [session/registration/service](W4SessionConsumer/README.md) and [host/request/shutdown](W4HostConsumer/README.md) executables. Each targets .NET 8/9/10; only the net10 host target uses ASP.NET Core.

Pack a fresh exact version using `publish-nuget.ps1 -PackOnly`. Build the Dev CLI non-incrementally on that clean commit before `package-report` and `api-report`; otherwise runner provenance is invalid even if package inspection passes. Preserve the raw API result and compare its exact diagnostics with the [accepted W3 dispositions](../../docs/dev-plans/roadmap-implementation/v0.10/W3%20Compatibility%20Evidence.md). No suppression should turn accepted exceptions into a blanket compatibility pass.

Restore the five candidate fixtures with an exact `DataLinqCandidateVersion`. Use a configuration that clears inherited sources and maps `DataLinq*` exclusively to the fresh local feed, as in W3. Baseline and candidate caches must be separate. An existing candidate cache may be reused for dependencies: the verifiers check every restored DataLinq archive against the selected candidate bytes, reject mixed versions/project references, and verify executed runtime assemblies.

```powershell
$version = '0.10.0-w4.002bf74b'
foreach ($fixture in 'AsyncPackageConsumer','AsyncMemoryConsumer','AsyncEfCoexistence','W4SessionConsumer','W4HostConsumer') {
    $project = "tests/fixtures/$fixture/$fixture.csproj"
    .\scripts\dotnet-sandbox.ps1 restore $project "-p:DataLinqCandidateVersion=$version" --configfile artifacts/w4-consumers/002bf74b/candidate.config --packages artifacts/w3-consumers/78d1b3c4/packages-candidate
    if ($LASTEXITCODE -ne 0) { throw "Restore failed: $fixture" }
    .\scripts\dotnet-sandbox.ps1 build $project "-p:DataLinqCandidateVersion=$version" -c Release
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $fixture" }
}

.\tests\fixtures\Verify-W3ConsumerOutputs.ps1 `
    -CandidateDirectory artifacts/nuget-release/0.10.0-w4.002bf74b `
    -CandidateVersion $version `
    -BaselineDirectory artifacts/api-baseline/w0-20260916/0.9.2 `
    -DotNet9 artifacts/runtimes/dotnet9-9.0.20/dotnet.exe `
    -OutputDirectory artifacts/w4-consumers/002bf74b/w3-reproduced

.\tests\fixtures\Verify-W4ConsumerOutputs.ps1 `
    -CandidateDirectory artifacts/nuget-release/0.10.0-w4.002bf74b `
    -CandidateVersion $version `
    -DotNet9 artifacts/runtimes/dotnet9-9.0.20/dotnet.exe `
    -OutputDirectory artifacts/w4-consumers/002bf74b/w4-reproduced
```

Use fresh output directories. Retain restore/build logs separately; these trusted-development verifiers record source/assets/binary hashes but do not independently attest the compiler or source-to-binary build. Actual runtime-major checks prevent .NET 9 roll-forward from masquerading as .NET 9 execution. The W3 verifier retains the original nineteen generated async mutation declarations and separately checks W4's twenty additive overloads. Its linked baseline binding sources remain unchanged.

The W4 verifier records six executions and the session fixture's compiled contracts. The W3 verifier records nine executions, unchanged baseline binaries and provider contracts. Repeat W3's intentional `ExpectAmbiguity=true` build and require only the expected `CS0121` CountAsync errors on all TFMs. See the [W4 contract record](../../test-infra/api-compatibility/w4/README.md) for exports and exact evidence identities.
