[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$CandidateVersion,
    [Parameter(Mandatory)][string]$DotNet9,
    [Parameter(Mandatory)][string]$OutputDirectory
)

# Run after exact-package restore/build; retain those logs separately.
# The W3 verifier remains responsible for locked old binaries and EF/Memory probes.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$runtime9 = (Resolve-Path -LiteralPath $DotNet9).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$executions = [Collections.Generic.List[object]]::new()
$packages = [Collections.Generic.List[object]]::new()
$inputs = [Collections.Generic.List[object]]::new()
$report = [ordered]@{ SchemaVersion = 'w4.consumer-output-verification.v1'; Passed = $false; CandidateVersion = $CandidateVersion }
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }

Push-Location $repo
try {
    foreach ($fixture in 'W4SessionConsumer','W4HostConsumer') {
        $root = Join-Path $PSScriptRoot $fixture
        $assetsPath = Join-Path $root 'obj/project.assets.json'
        $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
        if ($assets.targets.Count -ne 3) { throw "$fixture must resolve exactly three TFMs." }
        $inputs.Add(@{ Path = $assetsPath; Sha256 = Hash $assetsPath })
        foreach ($key in $assets.libraries.Keys | Where-Object { $_ -match '^DataLinq(?:\.[^/]+)?/' }) {
            $id, $version = $key.Split('/')
            if ($version -ne $CandidateVersion) { throw "Mixed DataLinq versions: $key." }
            $matches = @($assets.packageFolders.Keys | ForEach-Object {
                Join-Path $_ "$($id.ToLowerInvariant())/$version/$($id.ToLowerInvariant()).$version.nupkg"
            } | Where-Object { Test-Path -LiteralPath $_ })
            if ($matches.Count -ne 1) { throw "Ambiguous restored archive: $key." }
            $archive = Join-Path $candidate "$id.$version.nupkg"
            if ((Hash $matches[0]) -ne (Hash $archive)) { throw "Restored bytes differ: $key." }
            $packages.Add(@{ Fixture = $fixture; Id = $id; Version = $version; Sha256 = Hash $archive })
        }
        foreach ($tfm in 'net8.0','net9.0','net10.0') {
            $graph = $assets.targets[$tfm]
            if (!$graph -or $graph.Values.Where({ $_.type -ne 'package' }).Count) { throw "$fixture/$tfm has a missing or non-package graph." }
            $asyncLinq = @($graph.Keys | Where-Object { $_ -like 'System.Linq.AsyncEnumerable/*' })
            if (($tfm -eq 'net10.0' -and $asyncLinq.Count) -or
                ($tfm -ne 'net10.0' -and ($asyncLinq.Count -ne 1 -or $asyncLinq[0] -ne 'System.Linq.AsyncEnumerable/10.0.12'))) {
                throw "Unexpected async LINQ graph: $fixture/$tfm."
            }
            $dll = Join-Path $root "bin/Release/$tfm/$fixture.dll"
            foreach ($key in $graph.Keys | Where-Object { $_ -match '^DataLinq(?:\.[^/]+)?/' }) {
                foreach ($asset in $graph[$key].runtime.Keys | Where-Object { $_ -like '*.dll' }) {
                    $restored = @($assets.packageFolders.Keys | ForEach-Object { Join-Path $_ "$($assets.libraries[$key].path)/$asset" } |
                        Where-Object { Test-Path -LiteralPath $_ })
                    if ($restored.Count -ne 1 -or (Hash (Join-Path (Split-Path $dll) (Split-Path $asset -Leaf))) -ne (Hash $restored[0])) {
                        throw "Executed DataLinq assembly differs from restored candidate: $fixture/$tfm/$asset."
                    }
                }
            }
            $arguments = @($dll)
            if ($fixture -eq 'W4SessionConsumer') { $arguments += Join-Path $output "contract-$tfm.json" }
            $log = Join-Path $output "$fixture-$tfm.log"
            if ($tfm -eq 'net9.0') { $lines = @(& $runtime9 @arguments 2>&1) }
            else { $lines = @(& (Join-Path $repo 'scripts/dotnet-sandbox.ps1') @arguments 2>&1) }
            $exit = $LASTEXITCODE
            $lines | Set-Content -LiteralPath $log
            $major = $tfm.Substring(3).Split('.')[0]
            if ($exit -ne 0 -or ($lines -join "`n") -notmatch "on \.NET $major\.") { throw "$fixture/$tfm failed or used another runtime; see $log." }
            $executions.Add(@{ Fixture = $fixture; Framework = $tfm; ExitCode = $exit; AssemblySha256 = Hash $dll; Log = $log })
        }
    }
    foreach ($fixture in 'W4SessionConsumer','W4HostConsumer','AsyncPackageConsumer') {
        foreach ($file in Get-ChildItem (Join-Path $PSScriptRoot $fixture) -File | Where-Object Extension -in '.cs','.csproj') {
            $inputs.Add(@{ Path = "$fixture/$($file.Name)"; Sha256 = Hash $file.FullName })
        }
    }
    $report.Passed = $true
} finally {
    $report.Executions = $executions.ToArray()
    $report.Packages = $packages.ToArray()
    $report.Inputs = $inputs.ToArray()
    $report | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $output 'report.json')
    Pop-Location
}
Write-Output "Verified $($executions.Count) W4 package executions. Report: $output/report.json"
