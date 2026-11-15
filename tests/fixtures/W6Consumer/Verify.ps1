[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$CandidateVersion,
    [string]$DotNet9 = 'dotnet',
    [string]$DependencySource = 'https://api.nuget.org/v3/index.json'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$feed = (Resolve-Path -LiteralPath $CandidateDirectory).Path
$run = Join-Path $repo ("artifacts/w6-consumers/" + [Guid]::NewGuid().ToString('N'))
$cache = Join-Path $run 'packages'
New-Item -ItemType Directory -Path $run | Out-Null
$config = Join-Path $run 'NuGet.Config'
$xml = [xml]@'
<configuration>
  <packageSources><clear/><add key="candidate" value=""/><add key="dependencies" value=""/></packageSources>
  <packageSourceMapping>
    <packageSource key="candidate"><package pattern="DataLinq*"/></packageSource>
    <packageSource key="dependencies"><package pattern="*"/></packageSource>
  </packageSourceMapping>
</configuration>
'@
$xml.configuration.packageSources.add[0].SetAttribute('value', $feed)
$xml.configuration.packageSources.add[1].SetAttribute('value', $DependencySource)
$xml.Save($config)

$project = Join-Path $PSScriptRoot 'W6Consumer.csproj'
$properties = @("-p:DataLinqCandidateVersion=$CandidateVersion", "-p:BaseIntermediateOutputPath=$run/obj/", "-p:BaseOutputPath=$run/bin/")
& dotnet restore $project @properties --packages $cache --configfile $config
if ($LASTEXITCODE -ne 0) { throw 'Consumer restore failed.' }

$assets = Get-Content -Raw -LiteralPath (Join-Path $run 'obj/project.assets.json') | ConvertFrom-Json -AsHashtable
$ids = @('DataLinq', 'DataLinq.SQLite', 'DataLinq.Testing')
foreach ($id in $ids) {
    $file = "$id.$CandidateVersion.nupkg"
    $resolved = Join-Path $cache $assets.libraries["$id/$CandidateVersion"].path
    if ((Get-FileHash (Join-Path $feed $file)).Hash -ne (Get-FileHash (Join-Path $resolved $file.ToLowerInvariant())).Hash) {
        throw "Restored archive differs from the candidate for $id."
    }
}
foreach ($tfm in 'net8.0', 'net9.0', 'net10.0') {
    $graph = $assets.targets[$tfm]
    $packages = @($graph.Keys | ForEach-Object { ($_ -split '/')[0] })
    $actual = @($packages | Where-Object { $_ -like 'DataLinq*' })
    if (Compare-Object $ids $actual) { throw "Unexpected DataLinq closure on $tfm ($actual)." }
    if ($packages -match '^(Bogus|MySqlConnector|TUnit|Testcontainers|xunit|NUnit)(\.|$)') {
        throw "Repository harness dependency in the $tfm graph."
    }
    if ($packages -notcontains 'Microsoft.Data.Sqlite' -or $packages -notcontains 'SQLitePCLRaw.bundle_e_sqlite3') {
        throw "Missing SQLite runtime dependency on $tfm."
    }
    & dotnet build $project @properties --no-restore -c Release -f $tfm
    if ($LASTEXITCODE -ne 0) { throw "Consumer build failed for $tfm." }
    $runtime = if ($tfm -eq 'net9.0') { $DotNet9 } else { 'dotnet' }
    & $runtime (Join-Path $run "bin/Release/$tfm/W6Consumer.dll")
    if ($LASTEXITCODE -ne 0) { throw "Consumer failed on $tfm." }
}
Write-Output "Verified candidate $CandidateVersion. Restore assets and binaries: $run"
