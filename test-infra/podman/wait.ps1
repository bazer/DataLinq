[CmdletBinding()]
param(
    [string]$Profile
)

. "$PSScriptRoot\common.ps1"

$settings = Get-TestInfraSettings
$profileConfig = Get-ServerProfile -Settings $settings -ProfileId $Profile
$mySqlTarget = Get-ProfileTargetByFamily -Settings $settings -Profile $profileConfig -Family 'MySql'
$mariaDbTarget = Get-ProfileTargetByFamily -Settings $settings -Profile $profileConfig -Family 'MariaDb'
Assert-PodmanAvailable

if ($null -ne $mySqlTarget) {
    Write-Host "Waiting for $($mySqlTarget.displayName) on port $($settings.MySqlPort)..."
    Wait-ForMySqlAdmin -ContainerName $settings.MySqlContainerName -Password $settings.AdminPassword
}

if ($null -ne $mariaDbTarget) {
    Write-Host "Waiting for $($mariaDbTarget.displayName) on port $($settings.MariaDbPort)..."
    Wait-ForMySqlAdmin -ContainerName $settings.MariaDbContainerName -Password $settings.AdminPassword
}

Write-Host "Podman test databases for profile '$($profileConfig.id)' are ready."
