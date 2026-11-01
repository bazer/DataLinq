[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$CandidateVersion,
    [Parameter(Mandatory)][string]$DotNet9,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$PublishedDirectory
)

# Run after exact-package restore/build; retain those logs separately.
# The W3 verifier remains responsible for locked old binaries and EF/Memory probes.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$runtime9 = (Resolve-Path -LiteralPath $DotNet9).Path
$nativeDotnet = (Get-Command dotnet -CommandType Application).Source
$deploymentTempRoot = [IO.Path]::GetTempPath()
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$executions = [Collections.Generic.List[object]]::new()
$packages = [Collections.Generic.List[object]]::new()
$inputs = [Collections.Generic.List[object]]::new()
$report = [ordered]@{ SchemaVersion = 'w5.consumer-output-verification.v1'; Passed = $false; CandidateVersion = $CandidateVersion }
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }
$report.VerifierSha256 = Hash $PSCommandPath

Push-Location $repo
try {
    foreach ($fixture in 'W5Consumer') {
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
            if ($fixture -eq 'W5Consumer') { $arguments += Join-Path $output "contract-$tfm.json" }
            $log = Join-Path $output "$fixture-$tfm.log"
            if ($tfm -eq 'net9.0') { $lines = @(& $runtime9 @arguments 2>&1) }
            # Executing an already-built DLL needs no restore/build profile.
            # Keep the caller's environment: the sandbox build wrapper changes
            # TEMP and would move the later deployment back into the checkout.
            else { $lines = @(& $nativeDotnet @arguments 2>&1) }
            $exit = $LASTEXITCODE
            $lines | Set-Content -LiteralPath $log
            $major = $tfm.Substring(3).Split('.')[0]
            if ($exit -ne 0 -or ($lines -join "`n") -notmatch "on \.NET $major\.") { throw "$fixture/$tfm failed or used another runtime; see $log." }
            $executions.Add(@{ Fixture = $fixture; Framework = $tfm; ExitCode = $exit; AssemblySha256 = Hash $dll; Log = $log })
        }
    }
    foreach ($fixture in 'W5Consumer') {
        foreach ($file in Get-ChildItem (Join-Path $PSScriptRoot $fixture) -File | Where-Object Extension -in '.cs','.csproj') {
            $inputs.Add(@{ Path = "$fixture/$($file.Name)"; Sha256 = Hash $file.FullName })
        }
    }
    foreach ($linked in 'W4SessionConsumer/BindingModels.cs','PublicAsyncQueryConsumer/LegacyProvider.cs','Verify-W5ConsumerOutputs.ps1') {
        $inputs.Add(@{ Path = $linked; Sha256 = Hash (Join-Path $PSScriptRoot $linked) })
    }
    # Published bytes are copied outside the checkout, then executed with no
    # tooling/source files and again with deliberately invalid tooling files.
    $published = (Resolve-Path -LiteralPath $PublishedDirectory).Path
    $isolated = Join-Path $deploymentTempRoot ("datalinq-w5-" + [Guid]::NewGuid().ToString('N'))
    if ([IO.Path]::GetFullPath($isolated).StartsWith($repo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The isolated deployment must be outside the development checkout.'
    }
    $report.IsolatedDirectory = $isolated
    $deployment = [Collections.Generic.List[object]]::new()
    $report.Deployment = $deployment
    foreach ($tfm in 'net8.0','net9.0','net10.0') {
        $source = Join-Path $published $tfm
        $destination = Join-Path $isolated $tfm
        if (!(Test-Path -LiteralPath (Join-Path $source 'W5Consumer.dll'))) { throw "Missing published $tfm app." }
        $files = @(Get-ChildItem -LiteralPath $source -File -Recurse)
        if (@($files | Where-Object { $_.Extension -in '.cs','.csproj' -or $_.Name -like 'DataLinq.Tools*' -or $_.Name -like 'datalinq*.json' }).Count) {
            throw 'Published application contains tooling/source inputs.'
        }
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        foreach ($item in Get-ChildItem -LiteralPath $source) { Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse }
        # Compare every deployed DataLinq runtime assembly with the restored
        # candidate already checked above, not just with an untrusted publish.
        $assets = Get-Content (Join-Path $PSScriptRoot 'W5Consumer/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
        foreach ($key in $assets.targets[$tfm].Keys | Where-Object { $_ -match '^DataLinq(?:\.[^/]+)?/' }) {
            foreach ($asset in $assets.targets[$tfm][$key].runtime.Keys | Where-Object { $_ -like '*.dll' }) {
                $restored = @($assets.packageFolders.Keys | ForEach-Object { Join-Path $_ "$($assets.libraries[$key].path)/$asset" } |
                    Where-Object { Test-Path -LiteralPath $_ })
                if ($restored.Count -ne 1 -or (Hash (Join-Path $destination (Split-Path $asset -Leaf))) -ne (Hash $restored[0])) {
                    throw "Published DataLinq assembly differs: $tfm/$asset."
                }
            }
        }
        $deployedHashes = @(Get-ChildItem -LiteralPath $destination -File -Recurse | ForEach-Object {
            @{ Path = [IO.Path]::GetRelativePath($destination, $_.FullName); Sha256 = Hash $_.FullName }
        })
        foreach ($sentinel in $false,$true) {
            if ($sentinel) {
                [IO.File]::WriteAllText((Join-Path $destination 'datalinq.json'), '{ deliberately invalid tooling config')
                [IO.File]::WriteAllText((Join-Path $destination 'datalinq.user.json'), '{ deliberately invalid user companion')
            }
            $runtime = if ($tfm -eq 'net9.0') { $runtime9 } else { $nativeDotnet }
            $log = Join-Path $output "isolated-$tfm-sentinel-$sentinel.log"
            Push-Location $destination
            try {
                $lines = @(& $runtime (Join-Path $destination 'W5Consumer.dll') --isolated 2>&1)
                $exit = $LASTEXITCODE
            } finally { Pop-Location }
            $lines | Set-Content -LiteralPath $log
            $major = $tfm.Substring(3).Split('.')[0]
            if ($exit -ne 0 -or ($lines -join "`n") -notmatch "on \.NET $major\.") { throw "Isolated $tfm/sentinel=$sentinel failed; see $log." }
            $deployment.Add(@{ Framework = $tfm; InvalidToolingFiles = $sentinel; ExitCode = $exit; Log = $log; Files = $deployedHashes })
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
Write-Output "Verified $($executions.Count) W5 package executions. Report: $output/report.json"
