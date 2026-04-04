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

function Get-RepositoryRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Get-TestInfraStatePath {
    $repositoryRoot = Get-RepositoryRoot
    return Join-Path $repositoryRoot 'artifacts\testdata\podman-settings.json'
}

function Resolve-PublishedDatabaseHost {
    $configuredHost = [Environment]::GetEnvironmentVariable('DATALINQ_TEST_DB_HOST')
    if (-not [string]::IsNullOrWhiteSpace($configuredHost)) {
        return $configuredHost
    }

    if ($IsWindows -and (Get-Command podman -ErrorAction SilentlyContinue)) {
        try {
            $machineAddressOutput = & podman machine ssh "ip -o -4 addr show scope global" 2>$null
            if ($LASTEXITCODE -eq 0) {
                foreach ($line in $machineAddressOutput) {
                    if ($line -match '^\d+:\s+([^\s]+)\s+inet\s+(\d+\.\d+\.\d+\.\d+)') {
                        $interfaceName = $Matches[1]
                        $candidate = $Matches[2]
                        if ($interfaceName -ne 'lo' -and $interfaceName -ne 'podman0') {
                            return $candidate
                        }
                    }
                }
            }
        }
        catch {
        }
    }

    return '127.0.0.1'
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
        Host = Resolve-PublishedDatabaseHost
        AdminUser = Get-EnvOrDefault -Name 'DATALINQ_TEST_DB_ADMIN_USER' -DefaultValue 'datalinq'
        AdminPassword = Get-EnvOrDefault -Name 'DATALINQ_TEST_DB_ADMIN_PASSWORD' -DefaultValue 'datalinq'
        ApplicationUser = Get-EnvOrDefault -Name 'DATALINQ_TEST_DB_APP_USER' -DefaultValue 'datalinq'
        ApplicationPassword = Get-EnvOrDefault -Name 'DATALINQ_TEST_DB_APP_PASSWORD' -DefaultValue 'datalinq'
        EmployeesDatabase = Get-EnvOrDefault -Name 'DATALINQ_TEST_EMPLOYEES_DB' -DefaultValue 'datalinq_employees'
        LegacyMySqlContainerName = "$podName-mysql"
        LegacyMariaDbContainerName = "$podName-mariadb"
    }
}

function Write-TestInfraState {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings,

        [Parameter(Mandatory = $true)]
        [object[]]$Targets
    )

    $statePath = Get-TestInfraStatePath
    $stateDirectory = Split-Path -Parent $statePath
    if (-not (Test-Path $stateDirectory)) {
        New-Item -ItemType Directory -Path $stateDirectory -Force *> $null
    }

    $state = [pscustomobject]@{
        podName = $Settings.PodName
        profileId = $Settings.ProfileId
        host = $Settings.Host
        adminUser = $Settings.AdminUser
        adminPassword = $Settings.AdminPassword
        applicationUser = $Settings.ApplicationUser
        applicationPassword = $Settings.ApplicationPassword
        targets = @(
            $Targets | ForEach-Object {
                [pscustomobject]@{
                    id = $_.id
                    port = Get-TargetHostPort -Settings $Settings -Target $_
                }
            }
        )
    }

    $state | ConvertTo-Json -Depth 8 | Set-Content -Path $statePath -Encoding UTF8
}

function Remove-TestInfraState {
    $statePath = Get-TestInfraStatePath
    if (Test-Path $statePath) {
        Remove-Item -LiteralPath $statePath -Force
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

function Get-TargetContainerName {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings,

        [Parameter(Mandatory = $true)]
        [object]$Target
    )

    return "$($Settings.PodName)-$($Target.id.ToLowerInvariant())"
}

function Get-TargetHostPort {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings,

        [Parameter(Mandatory = $true)]
        [object]$Target
    )

    if ($Target.PSObject.Properties.Name -contains 'hostPort') {
        return [int]$Target.hostPort
    }

    switch ($Target.family) {
        'MySql' {
            return Get-IntEnvOrDefault -Name 'DATALINQ_TEST_MYSQL_PORT' -DefaultValue 3307
        }
        'MariaDb' {
            return Get-IntEnvOrDefault -Name 'DATALINQ_TEST_MARIADB_PORT' -DefaultValue 3308
        }
        default {
            throw "Server target '$($Target.id)' has no configured host port."
        }
    }
}

function Resolve-ServerTargets {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings,

        [string]$ProfileId,

        [string[]]$TargetIds,

        [switch]$AllLts
    )

    $hasProfile = -not [string]::IsNullOrWhiteSpace($ProfileId)
    $hasTargetIds = $null -ne $TargetIds -and $TargetIds.Count -gt 0

    if ($AllLts -and ($hasProfile -or $hasTargetIds)) {
        throw "Use either -AllLts, -Profile, or -TargetIds. These selection options are mutually exclusive."
    }

    if ($hasProfile -and $hasTargetIds) {
        throw "Use either -Profile or -TargetIds, not both."
    }

    if ($AllLts) {
        return @(
            $Settings.Matrix.targets |
                Where-Object { $_.isLts } |
                Sort-Object -Property family, version
        )
    }

    if ($hasTargetIds) {
        return @(
            $TargetIds |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Select-Object -Unique |
                ForEach-Object { Get-ServerTarget -Settings $Settings -TargetId $_ }
        )
    }

    $profile = Get-ServerProfile -Settings $Settings -ProfileId $ProfileId
    return @(
        $profile.targets | ForEach-Object { Get-ServerTarget -Settings $Settings -TargetId $_ }
    )
}

function Get-AllKnownTargetContainers {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings
    )

    return @(
        $Settings.Matrix.targets | ForEach-Object {
            [pscustomobject]@{
                Target = $_
                ContainerName = Get-TargetContainerName -Settings $Settings -Target $_
            }
        }
    )
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

function Wait-ForMariaDbReady {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ContainerName
    )

    Wait-Until -Description "database readiness for container '$ContainerName'" -Condition {
        & podman exec $ContainerName healthcheck.sh --connect --innodb_initialized *> $null
        return $LASTEXITCODE -eq 0
    }
}

function Wait-ForHostPort {
    param(
        [Parameter(Mandatory = $true)]
        [string]$HostName,

        [Parameter(Mandatory = $true)]
        [int]$Port,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    Wait-Until -Description $Description -Condition {
        $client = $null
        try {
            $client = [System.Net.Sockets.TcpClient]::new()
            $asyncResult = $client.BeginConnect($HostName, $Port, $null, $null)
            $connected = $asyncResult.AsyncWaitHandle.WaitOne(1000)
            if (-not $connected) {
                return $false
            }

            $client.EndConnect($asyncResult)
            return $true
        }
        catch {
            return $false
        }
        finally {
            if ($null -ne $client) {
                $client.Dispose()
            }
        }
    }
}

function Initialize-MySqlHostAdminUser {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings,

        [Parameter(Mandatory = $true)]
        [string]$ContainerName
    )

    $escapedAppPassword = $Settings.ApplicationPassword.Replace("'", "''")
    $escapedAdminPassword = $Settings.AdminPassword.Replace("'", "''")
    $statements = [System.Collections.Generic.List[string]]::new()
    $statements.Add("CREATE USER IF NOT EXISTS '$($Settings.ApplicationUser)'@'%' IDENTIFIED BY '$escapedAppPassword';")
    $statements.Add("ALTER USER '$($Settings.ApplicationUser)'@'%' IDENTIFIED BY '$escapedAppPassword';")

    if ($Settings.AdminUser -eq $Settings.ApplicationUser) {
        $statements.Add("GRANT ALL PRIVILEGES ON *.* TO '$($Settings.ApplicationUser)'@'%' WITH GRANT OPTION;")
    }
    else {
        $statements.Add("GRANT ALL PRIVILEGES ON *.* TO '$($Settings.ApplicationUser)'@'%';")
        $statements.Add("CREATE USER IF NOT EXISTS '$($Settings.AdminUser)'@'%' IDENTIFIED BY '$escapedAdminPassword';")
        $statements.Add("ALTER USER '$($Settings.AdminUser)'@'%' IDENTIFIED BY '$escapedAdminPassword';")
        $statements.Add("GRANT ALL PRIVILEGES ON *.* TO '$($Settings.AdminUser)'@'%' WITH GRANT OPTION;")
    }

    $statements.Add("FLUSH PRIVILEGES;")
    $sql = $statements -join ' '

    & podman exec $ContainerName mysql -h 127.0.0.1 -u root "-p$($Settings.AdminPassword)" -e $sql *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to provision elevated privileges for host admin user '$($Settings.AdminUser)' in container '$ContainerName'."
    }
}

function Initialize-MariaDbHostAdminUser {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Settings,

        [Parameter(Mandatory = $true)]
        [string]$ContainerName
    )

    $escapedAppPassword = $Settings.ApplicationPassword.Replace("'", "''")
    $escapedAdminPassword = $Settings.AdminPassword.Replace("'", "''")
    $statements = [System.Collections.Generic.List[string]]::new()
    $statements.Add("CREATE USER IF NOT EXISTS '$($Settings.ApplicationUser)'@'%' IDENTIFIED BY '$escapedAppPassword';")
    $statements.Add("ALTER USER '$($Settings.ApplicationUser)'@'%' IDENTIFIED BY '$escapedAppPassword';")

    if ($Settings.AdminUser -eq $Settings.ApplicationUser) {
        $statements.Add("GRANT ALL PRIVILEGES ON *.* TO '$($Settings.ApplicationUser)'@'%' WITH GRANT OPTION;")
    }
    else {
        $statements.Add("GRANT ALL PRIVILEGES ON *.* TO '$($Settings.ApplicationUser)'@'%';")
        $statements.Add("CREATE USER IF NOT EXISTS '$($Settings.AdminUser)'@'%' IDENTIFIED BY '$escapedAdminPassword';")
        $statements.Add("ALTER USER '$($Settings.AdminUser)'@'%' IDENTIFIED BY '$escapedAdminPassword';")
        $statements.Add("GRANT ALL PRIVILEGES ON *.* TO '$($Settings.AdminUser)'@'%' WITH GRANT OPTION;")
    }

    $statements.Add("FLUSH PRIVILEGES;")
    $sql = $statements -join ' '

    & podman exec $ContainerName mariadb -h 127.0.0.1 -u root "-p$($Settings.AdminPassword)" -e $sql *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to provision elevated privileges for host admin user '$($Settings.AdminUser)' in container '$ContainerName'."
    }
}
