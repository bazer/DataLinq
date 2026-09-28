[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$CandidateVersion,
    [Parameter(Mandatory)][string]$BaselineDirectory,
    [Parameter(Mandatory)][string]$DotNet9,
    [Parameter(Mandatory)][string]$OutputDirectory
)

# Run after restoring/building the four fixtures described in W3 Consumers.md.
# This is bounded consumer evidence, not a replacement for api-report/package-report.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$baseline = (Resolve-Path -LiteralPath $BaselineDirectory).Path
$runtime9 = (Resolve-Path -LiteralPath $DotNet9).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$records = [Collections.Generic.List[object]]::new()
$packageRecords = [Collections.Generic.List[object]]::new()
$binaryRecords = [Collections.Generic.List[object]]::new()
$sourceRecords = [Collections.Generic.List[object]]::new()
$lock = Get-Content (Join-Path $repo 'test-infra/api-compatibility/v0.9.2-packages.json') -Raw | ConvertFrom-Json
$report = [ordered]@{ SchemaVersion = 'w3.consumer-output-verification.v1'; Passed = $false; CandidateVersion = $CandidateVersion }

function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function CheckGraph([string]$fixture, [string]$version, [string]$feed) {
    $assets = Get-Content (Join-Path $PSScriptRoot "$fixture/obj/project.assets.json") -Raw | ConvertFrom-Json -AsHashtable
    if ($assets.targets.Count -ne 3) { throw "$fixture must resolve exactly three TFMs." }
    foreach ($tfm in 'net8.0','net9.0','net10.0') {
        $target = $assets.targets[$tfm]
        if (!$target) { throw "Missing $tfm graph." }
        if ($target.Values.Where({ $_.type -ne 'package' }).Count) { throw 'A project/non-package dependency escaped into the fixture.' }
        if ($version -eq $CandidateVersion) {
            $asyncPackages = @($target.Keys | Where-Object { $_ -like 'System.Linq.AsyncEnumerable/*' })
            if ($tfm -eq 'net10.0') {
                if ($asyncPackages.Count) { throw '.NET 10 unexpectedly consumes the async LINQ package.' }
            } elseif ($asyncPackages.Count -ne 1 -or $asyncPackages[0] -ne 'System.Linq.AsyncEnumerable/10.0.12') {
                throw 'Missing or unexpected transitive async LINQ version.'
            }
        }
    }
    if ($fixture -eq 'AsyncMemoryConsumer' -and @($assets.libraries.Keys | Where-Object { $_ -match '(?i)sqlite|mysql|maria' }).Count) {
        throw 'A SQL dependency appeared in the Memory-only graph.'
    }
    foreach ($key in $assets.libraries.Keys | Where-Object { $_ -like 'DataLinq/*' -or $_ -like 'DataLinq.*/*' }) {
        $id, $actualVersion = $key.Split('/')
        if ($actualVersion -ne $version) { throw "Mixed DataLinq versions in $fixture." }
        $matches = @($assets.packageFolders.Keys | ForEach-Object {
            Join-Path $_ "$($id.ToLowerInvariant())/$version/$($id.ToLowerInvariant()).$version.nupkg"
        } | Where-Object { Test-Path -LiteralPath $_ })
        if ($matches.Count -ne 1) { throw "Ambiguous restored package: $key." }
        $expected = Join-Path $feed "$id.$version.nupkg"
        $hash = Hash $expected
        if ((Hash $matches[0]) -ne $hash) { throw "Restored bytes do not match $expected." }
        if ($version -eq '0.9.2' -and ($lock.packages | Where-Object id -eq $id).sha256 -ne $hash) {
            throw "Baseline bytes do not match the tracked lock: $id."
        }
        $packageRecords.Add(@{ Fixture = $fixture; Id = $id; Version = $version; Sha256 = $hash })
    }
    $sourceRecords.Add(@{ Path = "$fixture/obj/project.assets.json"; Sha256 = Hash (Join-Path $PSScriptRoot "$fixture/obj/project.assets.json") })
}

Push-Location $repo
try {
    CheckGraph 'Legacy092Consumer' '0.9.2' $baseline
    foreach ($fixture in 'AsyncPackageConsumer','AsyncMemoryConsumer','AsyncEfCoexistence') {
        CheckGraph $fixture $CandidateVersion $candidate
    }
    foreach ($tfm in 'net8.0','net9.0','net10.0') {
        $legacy = Join-Path $PSScriptRoot "Legacy092Consumer/bin/Release/$tfm/Legacy092Consumer.dll"
        $frozen = Join-Path $output "Legacy092Consumer-$tfm.dll"
        Copy-Item -LiteralPath $legacy -Destination $frozen
        $binaryRecords.Add(@{ Framework = $tfm; Source = $legacy; Frozen = $frozen; Sha256 = Hash $frozen })
        foreach ($fixture in 'AsyncPackageConsumer','AsyncMemoryConsumer','AsyncEfCoexistence') {
            $dll = Join-Path $PSScriptRoot "$fixture/bin/Release/$tfm/$fixture.dll"
            # Check the assembly actually executed, not just its NuGet assets file.
            $runtimeCore = Join-Path (Split-Path $dll) 'DataLinq.dll'
            $assetFile = Get-Content (Join-Path $PSScriptRoot "$fixture/obj/project.assets.json") -Raw | ConvertFrom-Json -AsHashtable
            $packageCache = @($assetFile.packageFolders.Keys)[0]
            $packageCore = Join-Path $packageCache "datalinq/$CandidateVersion/lib/$tfm/DataLinq.dll"
            if ((Hash $runtimeCore) -ne (Hash $packageCore)) { throw 'Executed core does not match the restored candidate.' }
            $arguments = @($dll)
            if ($fixture -eq 'AsyncPackageConsumer') { $arguments += $frozen }
            $log = Join-Path $output "$fixture-$tfm.log"
            if ($tfm -eq 'net9.0') { $lines = @(& $runtime9 @arguments 2>&1) }
            else { $lines = @(& (Join-Path $repo 'scripts/dotnet-sandbox.ps1') @arguments 2>&1) }
            $exit = $LASTEXITCODE
            $lines | Set-Content -LiteralPath $log
            $major = $tfm.Substring(3).Split('.')[0]
            if ($exit -ne 0 -or ($lines -join "`n") -notmatch "on \.NET $major\.") { throw "$fixture/$tfm failed or ran on another runtime; see $log." }
            $records.Add(@{ Fixture = $fixture; Framework = $tfm; ExitCode = $exit; AssemblySha256 = Hash $dll; Log = $log })
        }
        if ((Hash $legacy) -ne (Hash $frozen)) { throw 'The old consumer changed during execution.' }
    }
    foreach ($fixture in 'Legacy092Consumer','AsyncPackageConsumer','AsyncMemoryConsumer','AsyncEfCoexistence','PublicAsyncQueryConsumer') {
        foreach ($file in Get-ChildItem (Join-Path $PSScriptRoot $fixture) -File | Where-Object Extension -in '.cs','.csproj') {
            $sourceRecords.Add(@{ Path = "$fixture/$($file.Name)"; Sha256 = Hash $file.FullName })
        }
    }
    $report.Passed = $true
} finally {
    $report.Executions = $records.ToArray()
    $report.Packages = $packageRecords.ToArray()
    $report.LegacyBinaries = $binaryRecords.ToArray()
    $report.Inputs = $sourceRecords.ToArray()
    $report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'report.json')
    Pop-Location
}
Write-Output "Verified $($records.Count) consumer executions and unchanged 0.9.2 binaries. Report: $output/report.json"
