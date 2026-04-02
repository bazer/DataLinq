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
    Initialize-MySqlHostAdminUser -Settings $settings -ContainerName $settings.MySqlContainerName
    Wait-ForHostPort -HostName $settings.Host -Port $settings.MySqlPort -Description "host TCP port $($settings.Host):$($settings.MySqlPort)"
}

if ($null -ne $mariaDbTarget) {
    Write-Host "Waiting for $($mariaDbTarget.displayName) on port $($settings.MariaDbPort)..."
    Wait-ForMariaDbReady -ContainerName $settings.MariaDbContainerName
    Initialize-MariaDbHostAdminUser -Settings $settings -ContainerName $settings.MariaDbContainerName
    Wait-ForHostPort -HostName $settings.Host -Port $settings.MariaDbPort -Description "host TCP port $($settings.Host):$($settings.MariaDbPort)"
}

Write-TestInfraState -Settings $settings
Write-Host "Podman test databases for profile '$($profileConfig.id)' are ready."
