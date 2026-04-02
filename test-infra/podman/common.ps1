function Get-EnvOrDefault {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$DefaultValue
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value
}

function Get-IntEnvOrDefault {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [int]$DefaultValue
    )

    $value = [Environment]::GetEnvironmentVariable($Name)
    $parsed = 0
    if ([int]::TryParse($value, [ref]$parsed)) {
        return $parsed
    }

    return $DefaultValue
}

function Get-TestInfraSettings {
    $podName = Get-EnvOrDefault -Name 'DATALINQ_TEST_PODMAN_POD' -DefaultValue 'datalinq-tests'
    $profileId = Get-EnvOrDefault -Name 'DATALINQ_TEST_PROFILE' -DefaultValue 'current-lts'
    $matrixPath = Join-Path $PSScriptRoot 'matrix.json'

    if (-not (Test-Path $matrixPath)) {
        throw "The Podman matrix file was not found at '$matrixPath'."
    }

    $matrix = Get-Content -Raw $matrixPath | ConvertFrom-Json -Depth 8

    return [pscustomobject]@{
        PodName = $podName
        ProfileId = $profileId
        MatrixPath = $matrixPath
        Matrix = $matrix
        Host = Get-EnvOrDefault -Name 'DATALINQ_TEST_DB_HOST' -DefaultValue '127.0.0.1'
        MySqlPort = Get-IntEnvOrDefault -Name 'DATALINQ_TEST_MYSQL_PORT' -DefaultValue 3307
        MariaDbPort = Get-IntEnvOrDefault -Name 'DATALINQ_TEST_MARIADB_PORT' -DefaultValue 3308
        AdminUser = Get-EnvOrDefault -Name 'DATALINQ_TEST_DB_ADMIN_USER' -DefaultValue 'root'
        AdminPassword = Get-EnvOrDefault -Name 'DATALINQ_TEST_DB_ADMIN_PASSWORD' -DefaultValue 'datalinq-root'
        ApplicationUser = Get-EnvOrDefault -Name 'DATALINQ_TEST_DB_APP_USER' -DefaultValue 'datalinq'
        ApplicationPassword = Get-EnvOrDefault -Name 'DATALINQ_TEST_DB_APP_PASSWORD' -DefaultValue 'datalinq'
        EmployeesDatabase = Get-EnvOrDefault -Name 'DATALINQ_TEST_EMPLOYEES_DB' -DefaultValue 'datalinq_employees'
        MySqlContainerName = "$podName-mysql"
        MariaDbContainerName = "$podName-mariadb"
    }
}

function Get-ServerTarget {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings,

        [Parameter(Mandatory = $true)]
        [string]$TargetId
    )

    $target = $Settings.Matrix.targets | Where-Object { $_.id -eq $TargetId } | Select-Object -First 1
    if ($null -eq $target) {
        throw "Could not find server target '$TargetId' in '$($Settings.MatrixPath)'."
    }

    return $target
}

function Get-ServerProfile {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings,

        [string]$ProfileId
    )

    $resolvedId = if ([string]::IsNullOrWhiteSpace($ProfileId)) { $Settings.ProfileId } else { $ProfileId }
    $profile = $Settings.Matrix.profiles | Where-Object { $_.id -eq $resolvedId } | Select-Object -First 1
    if ($null -eq $profile) {
        throw "Could not find server profile '$resolvedId' in '$($Settings.MatrixPath)'."
    }

    return $profile
}

function Get-ProfileTargetByFamily {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings,

        [Parameter(Mandatory = $true)]
        [object]$Profile,

        [Parameter(Mandatory = $true)]
        [string]$Family
    )

    foreach ($targetId in $Profile.targets) {
        $target = Get-ServerTarget -Settings $Settings -TargetId $targetId
        if ($target.family -eq $Family) {
            return $target
        }
    }

    return $null
}

function Assert-PodmanAvailable {
    if (-not (Get-Command podman -ErrorAction SilentlyContinue)) {
        throw "The 'podman' command was not found. Install Podman and make sure it is on PATH before using test-infra/podman scripts."
    }
}

function Test-PodExists {
    param([Parameter(Mandatory = $true)][string]$Name)

    & podman pod exists $Name *> $null
    return $LASTEXITCODE -eq 0
}

function Test-ContainerExists {
    param([Parameter(Mandatory = $true)][string]$Name)

    & podman container exists $Name *> $null
    return $LASTEXITCODE -eq 0
}

function Test-ContainerRunning {
    param([Parameter(Mandatory = $true)][string]$Name)

    $state = & podman inspect --format '{{.State.Running}}' $Name 2>$null
    return $LASTEXITCODE -eq 0 -and $state -match 'true'
}

function Wait-Until {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Description,

        [int]$TimeoutSeconds = 90,

        [int]$SleepMilliseconds = 1000
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) {
            return
        }

        Start-Sleep -Milliseconds $SleepMilliseconds
    }

    throw "Timed out waiting for $Description."
}

function Wait-ForMySqlAdmin {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName,

        [Parameter(Mandatory = $true)]
        [string]$Password
    )

    Wait-Until -Description "database readiness for container '$ContainerName'" -Condition {
        & podman exec $ContainerName mysqladmin ping -h 127.0.0.1 -u root "-p$Password" --silent *> $null
        return $LASTEXITCODE -eq 0
    }
}
