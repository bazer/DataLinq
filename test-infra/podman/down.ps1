[CmdletBinding()]
param(
    [switch]$Remove,
    [string]$Profile,
    [string[]]$TargetIds,
    [switch]$AllLts
)

. "$PSScriptRoot\common.ps1"

$settings = Get-TestInfraSettings
Assert-PodmanAvailable

$selectedTargets = if (-not [string]::IsNullOrWhiteSpace($Profile) -or ($null -ne $TargetIds -and $TargetIds.Count -gt 0) -or $AllLts) {
    Resolve-ServerTargets -Settings $settings -ProfileId $Profile -TargetIds $TargetIds -AllLts:$AllLts
}
else {
    Get-AllKnownTargetContainers -Settings $settings | ForEach-Object { $_.Target }
}

foreach ($target in $selectedTargets) {
    $containerName = Get-TargetContainerName -Settings $settings -Target $target

    if (-not (Test-ContainerExists -Name $containerName)) {
        continue
    }

    if (Test-ContainerRunning -Name $containerName) {
        Write-Host "Stopping container '$containerName'..."
        & podman stop $containerName *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to stop container '$containerName'."
        }
    }

    if ($Remove) {
        Write-Host "Removing container '$containerName'..."
        & podman rm -f $containerName *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to remove container '$containerName'."
        }
    }
}

foreach ($legacyContainerName in @($settings.LegacyMySqlContainerName, $settings.LegacyMariaDbContainerName)) {
    if (-not (Test-ContainerExists -Name $legacyContainerName)) {
        continue
    }

    if (Test-ContainerRunning -Name $legacyContainerName) {
        Write-Host "Stopping legacy container '$legacyContainerName'..."
        & podman stop $legacyContainerName *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to stop legacy container '$legacyContainerName'."
        }
    }

    if ($Remove) {
        Write-Host "Removing legacy container '$legacyContainerName'..."
        & podman rm -f $legacyContainerName *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to remove legacy container '$legacyContainerName'."
        }
    }
}

if (Test-PodExists -Name $settings.PodName) {
    Write-Host "Removing legacy pod '$($settings.PodName)'..."
    & podman pod rm -f $settings.PodName *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to remove legacy pod '$($settings.PodName)'."
    }
}

$runningContainers = @(
    Get-AllKnownTargetContainers -Settings $settings | Where-Object { Test-ContainerRunning -Name $_.ContainerName }
)
$remainingContainers = @(
    Get-AllKnownTargetContainers -Settings $settings | Where-Object { Test-ContainerExists -Name $_.ContainerName }
)

if ($remainingContainers.Count -eq 0 -and -not (Test-PodExists -Name $settings.PodName)) {
    Remove-TestInfraState
    Write-Host "No Podman test containers are present."
}
elseif ($runningContainers.Count -gt 0) {
    Write-TestInfraState -Settings $settings -Targets ($runningContainers | ForEach-Object { $_.Target })
}
else {
    Remove-TestInfraState
}
