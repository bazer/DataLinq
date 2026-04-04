[CmdletBinding()]
param(
    [string]$Profile,
    [string[]]$TargetIds,
    [switch]$AllLts
)

. "$PSScriptRoot\common.ps1"

$settings = Get-TestInfraSettings
$targets = Resolve-ServerTargets -Settings $settings -ProfileId $Profile -TargetIds $TargetIds -AllLts:$AllLts
Assert-PodmanAvailable

foreach ($target in $targets) {
    $containerName = Get-TargetContainerName -Settings $settings -Target $target
    $hostPort = Get-TargetHostPort -Settings $settings -Target $target

    Write-Host "Waiting for $($target.displayName) on port $hostPort..."

    switch ($target.family) {
        'MySql' {
            Wait-ForMySqlAdmin -ContainerName $containerName -Password $settings.AdminPassword
            Initialize-MySqlHostAdminUser -Settings $settings -ContainerName $containerName
        }
        'MariaDb' {
            Wait-ForMariaDbReady -ContainerName $containerName
            Initialize-MariaDbHostAdminUser -Settings $settings -ContainerName $containerName
        }
        default {
            throw "Unsupported database family '$($target.family)' for target '$($target.id)'."
        }
    }

    Wait-ForHostPort -HostName $settings.Host -Port $hostPort -Description "host TCP port $($settings.Host):$hostPort"
}

Write-TestInfraState -Settings $settings -Targets $targets
Write-Host "Podman test databases are ready for targets [$($targets.id -join ', ')]."
