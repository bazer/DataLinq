[CmdletBinding()]
param(
    [switch]$Remove
)

. "$PSScriptRoot\common.ps1"

$settings = Get-TestInfraSettings
Assert-PodmanAvailable

if (-not (Test-PodExists -Name $settings.PodName)) {
    Write-Host "Pod '$($settings.PodName)' does not exist."
    return
}

Write-Host "Stopping pod '$($settings.PodName)'..."
& podman pod stop $settings.PodName *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Failed to stop pod '$($settings.PodName)'."
}

if ($Remove) {
    Write-Host "Removing pod '$($settings.PodName)'..."
    & podman pod rm -f $settings.PodName *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to remove pod '$($settings.PodName)'."
    }
}
