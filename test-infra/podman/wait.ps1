[CmdletBinding()]
param()

. "$PSScriptRoot\common.ps1"

$settings = Get-TestInfraSettings
Assert-PodmanAvailable

Write-Host "Waiting for MySQL on port $($settings.MySqlPort)..."
Wait-ForMySqlAdmin -ContainerName $settings.MySqlContainerName -Password $settings.AdminPassword

Write-Host "Waiting for MariaDB on port $($settings.MariaDbPort)..."
Wait-ForMySqlAdmin -ContainerName $settings.MariaDbContainerName -Password $settings.AdminPassword

Write-Host "Podman test databases are ready."
