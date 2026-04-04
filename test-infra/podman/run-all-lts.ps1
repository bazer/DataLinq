[CmdletBinding()]
param(
    [string]$Project = 'src\DataLinq.Tests.Compliance\DataLinq.Tests.Compliance.csproj',
    [string]$Configuration = 'Debug',
    [ValidateRange(1, 32)]
    [int]$BatchSize = 2,
    [switch]$KeepLastBatchRunning
)

. "$PSScriptRoot\common.ps1"

Assert-PodmanAvailable

$settings = Get-TestInfraSettings
$defaultTargetIds = @((Get-ServerProfile -Settings $settings -ProfileId 'current-lts').targets)
$allLtsTargetIds = @(
    $settings.Matrix.targets |
        Where-Object { $_.isLts } |
        ForEach-Object { $_.id }
)
$orderedTargetIds = @(
    $defaultTargetIds +
    ($allLtsTargetIds | Where-Object { $_ -notin $defaultTargetIds })
) | Select-Object -Unique

if ($orderedTargetIds.Count -eq 0) {
    throw "Could not find any LTS compatibility targets in '$($settings.MatrixPath)'."
}

$batches = [System.Collections.Generic.List[object[]]]::new()
for ($index = 0; $index -lt $orderedTargetIds.Count; $index += $BatchSize) {
    $count = [Math]::Min($BatchSize, $orderedTargetIds.Count - $index)
    $batches.Add($orderedTargetIds[$index..($index + $count - 1)])
}

$results = [System.Collections.Generic.List[object]]::new()
$originalProfile = $env:DATALINQ_TEST_PROFILE
$originalProviderSet = $env:DATALINQ_TEST_PROVIDER_SET
$originalTargetIds = $env:DATALINQ_TEST_TARGETS
$originalIncludeSQLite = $env:DATALINQ_TEST_INCLUDE_SQLITE
$originalDotnetCliHome = $env:DOTNET_CLI_HOME
$originalSkipFirstTime = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$originalNoLogo = $env:DOTNET_NOLOGO

$env:DOTNET_CLI_HOME = Join-Path (Get-RepositoryRoot) '.dotnet'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'

try {
    Write-Host "Building compliance test project once before target fanout..."
    & dotnet build $Project -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build '$Project'."
    }

    for ($batchIndex = 0; $batchIndex -lt $batches.Count; $batchIndex++) {
        $batchTargetIds = [string[]]$batches[$batchIndex]
        $includeSQLite = $batchIndex -eq 0

        Write-Host ""
        Write-Host "=== Running compliance suite for targets [$($batchTargetIds -join ', ')] ==="

        & "$PSScriptRoot\reset.ps1" -TargetIds $batchTargetIds
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to provision target batch [$($batchTargetIds -join ', ')]."
        }

        $env:DATALINQ_TEST_PROVIDER_SET = 'targets'
        $env:DATALINQ_TEST_TARGETS = $batchTargetIds -join ','
        $env:DATALINQ_TEST_INCLUDE_SQLITE = if ($includeSQLite) { 'true' } else { 'false' }

        $start = Get-Date
        $output = & dotnet run --project $Project -c $Configuration --no-build 2>&1
        $exitCode = $LASTEXITCODE
        $end = Get-Date

        $output | Out-Host

        $summaryLine = $output | Where-Object { $_ -match '^\s*total:\s+\d+' } | Select-Object -Last 1
        $failedLine = $output | Where-Object { $_ -match '^\s*failed:\s+\d+' } | Select-Object -Last 1
        $succeededLine = $output | Where-Object { $_ -match '^\s*succeeded:\s+\d+' } | Select-Object -Last 1

        $results.Add([pscustomobject]@{
            Batch = $batchIndex + 1
            Targets = $batchTargetIds -join ', '
            ExitCode = $exitCode
            DurationSeconds = [Math]::Round(($end - $start).TotalSeconds, 1)
            Total = if ($summaryLine -match 'total:\s+(\d+)') { [int]$Matches[1] } else { $null }
            Failed = if ($failedLine -match 'failed:\s+(\d+)') { [int]$Matches[1] } else { $null }
            Succeeded = if ($succeededLine -match 'succeeded:\s+(\d+)') { [int]$Matches[1] } else { $null }
            IncludedSQLite = $includeSQLite
        })

        if ($exitCode -ne 0) {
            if (-not $KeepLastBatchRunning) {
                & "$PSScriptRoot\down.ps1"
            }

            throw "Compliance suite failed for target batch [$($batchTargetIds -join ', ')]."
        }
    }

    if (-not $KeepLastBatchRunning) {
        & "$PSScriptRoot\down.ps1"
    }

    Write-Host ""
    Write-Host "=== LTS Summary ==="
    $results |
        Select-Object Batch, Targets, IncludedSQLite, ExitCode, Total, Succeeded, Failed, DurationSeconds |
        Format-Table -AutoSize |
        Out-Host
}
finally {
    $env:DATALINQ_TEST_PROFILE = $originalProfile
    $env:DATALINQ_TEST_PROVIDER_SET = $originalProviderSet
    $env:DATALINQ_TEST_TARGETS = $originalTargetIds
    $env:DATALINQ_TEST_INCLUDE_SQLITE = $originalIncludeSQLite
    $env:DOTNET_CLI_HOME = $originalDotnetCliHome
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $originalSkipFirstTime
    $env:DOTNET_NOLOGO = $originalNoLogo
}
