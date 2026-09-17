[CmdletBinding()]
param(
    [switch]$SelfTestOnly,
    [string]$OutputDirectory,
    [string]$AuditConfig
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts/dependency-audit'
}
if ([string]::IsNullOrWhiteSpace($AuditConfig)) {
    $AuditConfig = Join-Path $PSScriptRoot 'nuget-audit.config'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$AuditConfig = (Resolve-Path -LiteralPath $AuditConfig).Path
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function Invoke-AuditRestore([string]$Project, [string]$Config, [string]$LogPath, [bool]$RequireCoverage, [bool]$AuditEnabled = $true) {
    $restoreArguments = @(
        'restore', $Project, '--force', '--force-evaluate', '--no-http-cache',
        '--configfile', $Config, '--verbosity', 'minimal', '-m:1',
        "-p:NuGetAudit=$($AuditEnabled.ToString().ToLowerInvariant())", '-p:NuGetAuditMode=all', '-p:NuGetAuditLevel=low',
        '-p:NoWarn=', '-p:WarningsNotAsErrors=',
        '-warnaserror:NU1900,NU1901,NU1902,NU1903,NU1904,NU1905',
        '-p:RestoreUseStaticGraphEvaluation=false', '-p:RestoreIgnoreFailedSources=false'
    )
    if ($RequireCoverage) { $restoreArguments += '-p:DataLinqAuditRequired=true' }
    if ($IsWindows) {
        & (Join-Path $PSScriptRoot 'dotnet-sandbox.ps1') @restoreArguments *> $LogPath
    } else {
        & dotnet @restoreArguments *> $LogPath
    }
    $result = $LASTEXITCODE
    Get-Content -LiteralPath $LogPath | ForEach-Object { Write-Host $_ }
    return $result
}

function New-AuditFixture([string]$Directory, [string]$Package, [string]$Version) {
    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    $project = Join-Path $Directory 'AuditFixture.csproj'
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <NuGetAudit>false</NuGetAudit>
    <NuGetAuditMode>direct</NuGetAuditMode>
    <NoWarn>NU1900;NU1901;NU1902;NU1903;NU1904;NU1905</NoWarn>
    <WarningsNotAsErrors>NU1903</WarningsNotAsErrors>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="$Package" Version="$Version" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding utf8
    return $project
}

if ($SelfTestOnly) {
    # These fixtures are restored only, never built or executed. Their deliberate
    # advisory is GHSA-5crp-9r3c-p9vr (Newtonsoft.Json versions below 13.0.1).
    $fixtureRoot = Join-Path $OutputDirectory ('fixtures-' + [Guid]::NewGuid().ToString('N'))
    $cases = @(
        @{ Name = 'direct'; Package = 'Newtonsoft.Json'; Version = '12.0.3' },
        @{ Name = 'transitive'; Package = 'Microsoft.AspNet.WebApi.Client'; Version = '5.2.7' }
    )
    foreach ($case in $cases) {
        $project = New-AuditFixture (Join-Path $fixtureRoot $case.Name) $case.Package $case.Version
        $log = Join-Path $OutputDirectory ($case.Name + '.log')
        $exitCode = Invoke-AuditRestore $project $AuditConfig $log $false
        $output = Get-Content -Raw -LiteralPath $log
        if ($exitCode -eq 0 -or $output -notmatch 'NU1903.*Newtonsoft.Json.*GHSA-5crp-9r3c-p9vr') {
            throw "The $($case.Name) advisory fixture did not fail for the expected vulnerability. See $log"
        }
        Write-Host "PASS: $($case.Name) advisory rejected despite fixture suppression settings."
    }

    $cleanProject = New-AuditFixture (Join-Path $fixtureRoot 'clean') 'Newtonsoft.Json' '13.0.4'
    $cleanLog = Join-Path $OutputDirectory 'clean.log'
    if ((Invoke-AuditRestore $cleanProject $AuditConfig $cleanLog $false) -ne 0) {
        throw "The clean fixture failed; this is not evidence of a working clean audit. See $cleanLog"
    }
    Write-Host 'PASS: clean fixture audited successfully.'

    $fixtureSolution = Join-Path $fixtureRoot 'Coverage.slnx'
    '<Solution><Project Path="clean/AuditFixture.csproj" /></Solution>' | Set-Content -LiteralPath $fixtureSolution -Encoding utf8
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src/Directory.Solution.targets') -Destination (Join-Path $fixtureRoot 'Directory.Solution.targets')
    $coverageLog = Join-Path $OutputDirectory 'disabled-audit.log'
    $coverageExit = Invoke-AuditRestore $fixtureSolution $AuditConfig $coverageLog $true $false
    if ($coverageExit -eq 0 -or (Get-Content -Raw -LiteralPath $coverageLog) -notmatch 'DLAUDIT001') {
        throw "The solution coverage guard did not reject disabled auditing. See $coverageLog"
    }
    Write-Host 'PASS: disabled solution auditing rejected by the coverage guard.'

    [xml]$unavailable = Get-Content -Raw -LiteralPath $AuditConfig
    $unavailable.configuration.auditSources.add.SetAttribute('value', 'https://127.0.0.1:9/index.json')
    $unavailableConfig = Join-Path $fixtureRoot 'unavailable.config'
    $unavailable.Save($unavailableConfig)
    $feedLog = Join-Path $OutputDirectory 'unavailable-feed.log'
    $feedExit = Invoke-AuditRestore $cleanProject $unavailableConfig $feedLog $false
    if ($feedExit -eq 0 -or (Get-Content -Raw -LiteralPath $feedLog) -notmatch 'NU1900|NU1905') {
        throw "The unavailable advisory feed was not rejected explicitly. See $feedLog"
    }
    Write-Host 'PASS: unavailable advisory feed rejected.'
    exit 0
}

$solution = Join-Path $repositoryRoot 'src/DataLinq.sln'
$auditLog = Join-Path $OutputDirectory 'solution-audit.log'
$auditExit = Invoke-AuditRestore $solution $AuditConfig $auditLog $true
if ($auditExit -ne 0) {
    throw "Dependency audit failed. Advisory warnings, unavailable feeds, and incomplete restores all fail this gate. See $auditLog"
}
$auditText = Get-Content -Raw -LiteralPath $auditLog
$coverage = [regex]::Match($auditText, 'DATALINQ_AUDIT_COVERAGE: (\d+)/(\d+)')
if (-not $coverage.Success -or [int]$coverage.Groups[1].Value -le 0 -or $coverage.Groups[1].Value -ne $coverage.Groups[2].Value) {
    throw "Restore did not report complete solution audit coverage. See $auditLog"
}
Write-Host "PASS: all $($coverage.Groups[2].Value) solution projects audited; no known advisories reported by the configured feed."
