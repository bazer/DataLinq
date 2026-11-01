# W5 package and published consumers

W5 extends the [W3 package workflow](W3%20Consumers.md) and [W4 consumers](W4%20Consumers.md) with [runtime/startup validation](W5Consumer/README.md). Use one exact clean-source candidate for all three waves. Keep the locked 0.9.2 binaries unchanged, retain the original binding sources, and use `Verify-W3ConsumerOutputs.ps1 -FixtureMode W5`; its default mode still rejects an unexpected W5 surface.

Pack all six public packages and symbols with `publish-nuget.ps1 -PackOnly`, then build the Dev CLI non-incrementally on the same clean commit. Run strict `package-report` and `api-report` with the exact candidate version, an explicit local baseline and fresh output paths. Preserve raw compatibility diagnostics and compare their fingerprints with the accepted W3 dispositions. No package publication is part of this workflow.

Create the exclusive local `DataLinq*` source mapping described in W3. Restore/build these six candidate projects with an exact `DataLinqCandidateVersion`: `AsyncPackageConsumer`, `AsyncMemoryConsumer`, `AsyncEfCoexistence`, `W4SessionConsumer`, `W4HostConsumer`, `W5Consumer`. Use separate baseline/candidate caches; the existing dependency cache may be reused because verifiers check archive and runtime hashes against the exact selected candidate. Retain restore/build logs and repeat the expected EF `CountAsync` ambiguity check on all three TFMs.

Publish W5 framework-dependent outputs for each TFM:

```powershell
$version = '<exact-candidate-version>'
$evidence = 'artifacts/w5-consumers/<source-id>'
foreach ($tfm in 'net8.0','net9.0','net10.0') {
    .\scripts\dotnet-sandbox.ps1 publish tests/fixtures/W5Consumer/W5Consumer.csproj `
        -c Release -f $tfm "-p:DataLinqCandidateVersion=$version" `
        --self-contained false -o "$evidence/published/$tfm"
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $tfm" }
}

.\tests\fixtures\Verify-W5ConsumerOutputs.ps1 `
    -CandidateDirectory "artifacts/nuget-release/$version" `
    -CandidateVersion $version `
    -DotNet9 artifacts/runtimes/dotnet9-9.0.20/dotnet.exe `
    -PublishedDirectory "$evidence/published" `
    -OutputDirectory "$evidence/w5-verified"
```

Run the W3 verifier in explicit W5 mode and the unchanged W4 verifier against the same candidate. The W5 verifier records three package executions and six isolated published executions on actual .NET 8/9/10. The isolated copies live outside the checkout, contain no source/Tools inputs, and execute first without tooling files, then with malformed `datalinq.json` and `datalinq.user.json`. Direct code-supplied connections and application-owned in-memory host configuration both cover full/Include scope, pass and drift. The net10 lane also proves the HTTP listener barrier. Logs, runtime identities, source/assets/package/binary hashes and copied-file inventories remain available in its report.

Export the captured public declarations for review:

```powershell
.\tests\fixtures\Export-W3ContractReview.ps1 -Label W5 `
    -Manifest "$evidence/w5-verified/contract-net10.0.json" `
    -Output test-infra/api-compatibility/w5/compiled-contract.txt
```

The contract export supplements ApiCompat and executable binding tests; it does not replace either. These are trusted-development verifiers, not independent compiler/source attestations. Use the repo package/platform tooling separately for dependency, analyzer, Memory isolation, trimming, AOT and browser evidence. The native case-mode runner in the W5 fixture README supplies dedicated server evidence and explicitly distinguishes unavailable mode 2 from passing native coverage.
