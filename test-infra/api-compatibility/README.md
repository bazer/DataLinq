# API Compatibility Baseline

`v0.8.0-packages.json` is the tracked byte, repository-provenance, and inherited-divergence disposition lock for the published DataLinq `0.8.0` API baseline. `api-report` accepts package bytes from an explicit directory, but authoritative evidence is valid only when this canonical tracked lock is unchanged from the checkout.

The two `loadLock` dispositions bind exact ApiCompat diagnostic identities to an explicit rationale. The 0.8 package already exposes `object` on net8 and `System.Threading.Lock` on net9/net10 because `Lock` does not exist on net8. Preserving those per-TFM field signatures avoids breaking subclasses compiled against the published package. The reporter self-validates the locked baseline and downgrades a candidate diagnostic only when both that proof and the exact tracked disposition match; a new, changed, missing, or stale divergence remains a hard failure.

## NuGet.org acquisition record

The five baseline packages were downloaded independently from NuGet.org's v3 flat container on 2026-08-06 UTC:

| Package | Published nupkg SHA-256 |
| --- | --- |
| `DataLinq` | `6af51acf9c45cbd0682ce91a660afe669e26ac383c889bb4375370e526f318d1` |
| `DataLinq.SQLite` | `9e07120795ca5385a74a9f9c69e7186036c103201f22c934157ff5fd1e639765` |
| `DataLinq.MySql` | `0f7ec8fb89fdc536d6f82bdc15cfeb77c63bb0ed93ef26b874e2d0544ede5422` |
| `DataLinq.CLI` | `f64d5a14c009435ee3c06c3530c7b37050d406df97900608415d31a7be523495` |
| `DataLinq.Tools` | `bcfbed905fbddb793fb9eaf4f9a6e601c1b0745644f6dfdd36cb97f03236bddf` |

Each URL has the exact form:

```text
https://api.nuget.org/v3-flatcontainer/<lowercase-package-id>/0.8.0/<lowercase-package-id>.0.8.0.nupkg
```

Reproduce the acquisition from the repository root with PowerShell:

```powershell
$version = "0.8.0"
$output = "artifacts/api-baseline/nuget-org-$version"
$packages = "DataLinq", "DataLinq.SQLite", "DataLinq.MySql", "DataLinq.CLI", "DataLinq.Tools"

if (Test-Path -LiteralPath $output) {
    throw "Refusing existing baseline directory '$output'."
}

New-Item -ItemType Directory -Path $output | Out-Null
foreach ($package in $packages) {
    $id = $package.ToLowerInvariant()
    $target = Join-Path $output "$package.$version.nupkg"
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" -OutFile $target
}

Get-ChildItem -LiteralPath $output -Filter *.nupkg | Get-FileHash -Algorithm SHA256
Get-ChildItem -LiteralPath $output -Filter *.nupkg | ForEach-Object {
    dotnet nuget verify --all $_.FullName
    if ($LASTEXITCODE -ne 0) {
        throw "NuGet verification failed for '$($_.FullName)'."
    }
}
```

Every downloaded archive contains `.signature.p7s`. `dotnet nuget verify --all` exited `0` for all five and identified the NuGet.org repository signature. The verification also emitted `NU3018` and `NU3028` because certificate revocation servers were unreachable from the verification environment, so this record does not claim that online revocation status was checked.

The whole-file hashes differ from the earlier local output at `artifacts/nuget-release/20260708-232626` because NuGet.org added its repository signature. A per-entry SHA-256 comparison, excluding only `.signature.p7s`, found zero entry-name or content differences for all five packages. Their nuspec repository URL is `https://github.com/bazer/DataLinq`, their repository commit is `1a156819e1567a4db3b8bd43e4e09e8da1a5572c`, and the lightweight local tag `0.8.0` resolves to that commit.

The downloaded packages are intentionally ignored build evidence, not tracked source. To refresh them, download into a fresh directory, run `Get-FileHash -Algorithm SHA256`, run `dotnet nuget verify --all`, and compare the results with the tracked lock before running `api-report`. Do not substitute a global NuGet cache or bless different bytes by passing an alternate lock.
