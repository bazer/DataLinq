[CmdletBinding()]
param(
    [switch]$Remove
)

. "$PSScriptRoot\common.ps1"

$settings = Get-TestInfraSettings
Assert-PodmanAvailable

if (Test-ContainerExists -Name $settings.MySqlContainerName) {
    if (Test-ContainerRunning -Name $settings.MySqlContainerName) {
        Write-Host "Stopping container '$($settings.MySqlContainerName)'..."
        & podman stop $settings.MySqlContainerName *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to stop container '$($settings.MySqlContainerName)'."
        }
    }

    if ($Remove) {
        Write-Host "Removing container '$($settings.MySqlContainerName)'..."
        & podman rm -f $settings.MySqlContainerName *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to remove container '$($settings.MySqlContainerName)'."
        }
    }
}

if (Test-ContainerExists -Name $settings.MariaDbContainerName) {
    if (Test-ContainerRunning -Name $settings.MariaDbContainerName) {
        Write-Host "Stopping container '$($settings.MariaDbContainerName)'..."
        & podman stop $settings.MariaDbContainerName *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to stop container '$($settings.MariaDbContainerName)'."
        }
    }

    if ($Remove) {
        Write-Host "Removing container '$($settings.MariaDbContainerName)'..."
        & podman rm -f $settings.MariaDbContainerName *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to remove container '$($settings.MariaDbContainerName)'."
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

if (-not (Test-ContainerExists -Name $settings.MySqlContainerName) -and
    -not (Test-ContainerExists -Name $settings.MariaDbContainerName) -and
    -not (Test-PodExists -Name $settings.PodName)) {
    if ($Remove) {
        Remove-TestInfraState
    }
    Write-Host "No Podman test containers are present."
}
