[CmdletBinding()]
param(
    [string]$Profile,
    [string[]]$TargetIds,
    [switch]$AllLts,
    [switch]$Recreate
)

. "$PSScriptRoot\common.ps1"

$settings = Get-TestInfraSettings
$targets = Resolve-ServerTargets -Settings $settings -ProfileId $Profile -TargetIds $TargetIds -AllLts:$AllLts
Assert-PodmanAvailable

if ($Recreate) {
    & "$PSScriptRoot\down.ps1" -Remove
}

foreach ($target in $targets) {
    $containerName = Get-TargetContainerName -Settings $settings -Target $target
    $hostPort = Get-TargetHostPort -Settings $settings -Target $target

    if (-not (Test-ContainerExists -Name $containerName)) {
        Write-Host "Creating $($target.displayName) container '$containerName'..."

        $envVars = switch ($target.family) {
            'MySql' {
                @(
                    "-e", "MYSQL_ROOT_PASSWORD=$($settings.AdminPassword)",
                    "-e", "MYSQL_ROOT_HOST=%",
                    "-e", "MYSQL_DATABASE=$($settings.EmployeesDatabase)",
                    "-e", "MYSQL_USER=$($settings.ApplicationUser)",
                    "-e", "MYSQL_PASSWORD=$($settings.ApplicationPassword)"
                )
            }
            'MariaDb' {
                @(
                    "-e", "MARIADB_ROOT_PASSWORD=$($settings.AdminPassword)",
                    "-e", "MARIADB_ROOT_HOST=%",
                    "-e", "MARIADB_DATABASE=$($settings.EmployeesDatabase)",
                    "-e", "MARIADB_USER=$($settings.ApplicationUser)",
                    "-e", "MARIADB_PASSWORD=$($settings.ApplicationPassword)"
                )
            }
            default {
                throw "Unsupported database family '$($target.family)' for target '$($target.id)'."
            }
        }

        $arguments = @(
            'run',
            '-d',
            '--name', $containerName,
            '-p', "$hostPort`:3306"
        ) + $envVars + @(
            $target.image,
            '--character-set-server=utf8mb4',
            '--collation-server=utf8mb4_unicode_ci'
        )

        & podman @arguments

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create $($target.displayName) container '$containerName'."
        }
    }
    elseif (-not (Test-ContainerRunning -Name $containerName)) {
        Write-Host "Starting $($target.displayName) container '$containerName'..."
        & podman start $containerName
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to start $($target.displayName) container '$containerName'."
        }
    }
}

& "$PSScriptRoot\wait.ps1" -Profile $Profile -TargetIds $TargetIds -AllLts:$AllLts
