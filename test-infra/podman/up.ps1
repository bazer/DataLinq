[CmdletBinding()]
param(
    [string]$Profile,
    [switch]$Recreate
)

. "$PSScriptRoot\common.ps1"

$settings = Get-TestInfraSettings
$profileConfig = Get-ServerProfile -Settings $settings -ProfileId $Profile
$mySqlTarget = Get-ProfileTargetByFamily -Settings $settings -Profile $profileConfig -Family 'MySql'
$mariaDbTarget = Get-ProfileTargetByFamily -Settings $settings -Profile $profileConfig -Family 'MariaDb'
Assert-PodmanAvailable

if ($Recreate) {
    & "$PSScriptRoot\down.ps1" -Remove
}

if (-not (Test-PodExists -Name $settings.PodName)) {
    Write-Host "Creating pod '$($settings.PodName)'..."
    & podman pod create --name $settings.PodName -p "$($settings.MySqlPort):3306" -p "$($settings.MariaDbPort):3306"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create pod '$($settings.PodName)'."
    }
}

if ($null -ne $mySqlTarget -and -not (Test-ContainerExists -Name $settings.MySqlContainerName)) {
    Write-Host "Creating MySQL container '$($settings.MySqlContainerName)'..."
    & podman run -d `
        --name $settings.MySqlContainerName `
        --pod $settings.PodName `
        -e "MYSQL_ROOT_PASSWORD=$($settings.AdminPassword)" `
        -e "MYSQL_DATABASE=$($settings.EmployeesDatabase)" `
        -e "MYSQL_USER=$($settings.ApplicationUser)" `
        -e "MYSQL_PASSWORD=$($settings.ApplicationPassword)" `
        $mySqlTarget.image `
        --character-set-server=utf8mb4 `
        --collation-server=utf8mb4_unicode_ci

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create MySQL container '$($settings.MySqlContainerName)'."
    }
}
elseif ($null -ne $mySqlTarget -and -not (Test-ContainerRunning -Name $settings.MySqlContainerName)) {
    Write-Host "Starting MySQL container '$($settings.MySqlContainerName)'..."
    & podman start $settings.MySqlContainerName
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start MySQL container '$($settings.MySqlContainerName)'."
    }
}

if ($null -ne $mariaDbTarget -and -not (Test-ContainerExists -Name $settings.MariaDbContainerName)) {
    Write-Host "Creating MariaDB container '$($settings.MariaDbContainerName)'..."
    & podman run -d `
        --name $settings.MariaDbContainerName `
        --pod $settings.PodName `
        -e "MARIADB_ROOT_PASSWORD=$($settings.AdminPassword)" `
        -e "MARIADB_DATABASE=$($settings.EmployeesDatabase)" `
        -e "MARIADB_USER=$($settings.ApplicationUser)" `
        -e "MARIADB_PASSWORD=$($settings.ApplicationPassword)" `
        $mariaDbTarget.image `
        --character-set-server=utf8mb4 `
        --collation-server=utf8mb4_unicode_ci

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create MariaDB container '$($settings.MariaDbContainerName)'."
    }
}
elseif ($null -ne $mariaDbTarget -and -not (Test-ContainerRunning -Name $settings.MariaDbContainerName)) {
    Write-Host "Starting MariaDB container '$($settings.MariaDbContainerName)'..."
    & podman start $settings.MariaDbContainerName
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start MariaDB container '$($settings.MariaDbContainerName)'."
    }
}

& "$PSScriptRoot\wait.ps1" -Profile $profileConfig.id
