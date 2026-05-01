$ErrorActionPreference = 'Stop'
$DotNetArguments = $args

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path -LiteralPath (Join-Path $scriptRoot '..')
$dotnetCliHome = Join-Path $repoRoot '.dotnet-cli'
$nugetConfig = Join-Path $dotnetCliHome 'NuGet.Config'
$nugetHttpCache = Join-Path $dotnetCliHome 'nuget-http-cache'
$nugetScratch = Join-Path $dotnetCliHome 'nuget-scratch'
$profileRoot = Join-Path $dotnetCliHome 'profile'
$appData = Join-Path $profileRoot 'AppData\Roaming'
$localAppData = Join-Path $profileRoot 'AppData\Local'
$tempPath = Join-Path $profileRoot 'Temp'
$originalLocalAppData = $env:LOCALAPPDATA
$defaultPodmanPath = if ($originalLocalAppData) { Join-Path $originalLocalAppData 'Programs\Podman\podman.exe' } else { $null }
$defaultPodmanSocket = if ($originalLocalAppData) { Join-Path $originalLocalAppData 'Temp\podman\podman-machine-default-api.sock' } else { $null }
$testInfraStatePath = Join-Path $repoRoot 'artifacts\testdata\testinfra-state.json'

New-Item -ItemType Directory -Force -Path $dotnetCliHome | Out-Null
New-Item -ItemType Directory -Force -Path $nugetHttpCache | Out-Null
New-Item -ItemType Directory -Force -Path $nugetScratch | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $appData 'NuGet') | Out-Null
New-Item -ItemType Directory -Force -Path $localAppData | Out-Null
New-Item -ItemType Directory -Force -Path $tempPath | Out-Null

if (-not (Test-Path -LiteralPath $nugetConfig)) {
    @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<configuration>'
        '  <packageSources>'
        '    <clear />'
        '    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />'
        '  </packageSources>'
        '</configuration>'
    ) | Set-Content -LiteralPath $nugetConfig -Encoding utf8
}

$env:DOTNET_CLI_HOME = $dotnetCliHome
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:APPDATA = $appData
$env:LOCALAPPDATA = $localAppData
$env:TEMP = $tempPath
$env:TMP = $tempPath
$env:RestoreConfigFile = $nugetConfig
$env:NUGET_HTTP_CACHE_PATH = $nugetHttpCache
$env:NUGET_SCRATCH = $nugetScratch

if (-not $env:DATALINQ_TEST_PODMAN_PATH -and $defaultPodmanPath -and (Test-Path -LiteralPath $defaultPodmanPath)) {
    $env:DATALINQ_TEST_PODMAN_PATH = $defaultPodmanPath
}

if (-not $env:DATALINQ_TEST_PODMAN_SOCKET -and $defaultPodmanSocket -and (Test-Path -LiteralPath $defaultPodmanSocket)) {
    $env:DATALINQ_TEST_PODMAN_SOCKET = $defaultPodmanSocket
}

if (-not $env:DATALINQ_TEST_DB_HOST -and (Test-Path -LiteralPath $testInfraStatePath)) {
    try {
        $testInfraState = Get-Content -LiteralPath $testInfraStatePath -Raw | ConvertFrom-Json
        $persistedHost = $testInfraState.Host
        if ($persistedHost -and $persistedHost -notin @('127.0.0.1', 'localhost', '::1')) {
            $env:DATALINQ_TEST_DB_HOST = $persistedHost
        }
    }
    catch {
        # Ignore stale or partial state; the Testing CLI will surface any real infra failure.
    }
}

$arguments = [System.Collections.Generic.List[string]]::new()
foreach ($argument in $DotNetArguments) {
    $arguments.Add($argument)
}

$commandsWithImplicitRestore = @('build', 'run', 'test', 'pack', 'publish')
if ($arguments.Count -gt 0 -and $commandsWithImplicitRestore -contains $arguments[0] -and -not $arguments.Contains('--no-restore')) {
    $appArgumentSeparatorIndex = $arguments.IndexOf('--')

    if ($appArgumentSeparatorIndex -ge 0) {
        $arguments.Insert($appArgumentSeparatorIndex, '--no-restore')
    }
    else {
        $arguments.Add('--no-restore')
    }
}

$commandsWithRestoreConfig = @('restore')
if ($arguments.Count -gt 0 -and $commandsWithRestoreConfig -contains $arguments[0]) {
    $restoreConfigProperty = "-p:RestoreConfigFile=$nugetConfig"
    $ignoreFailedSourcesProperty = '-p:RestoreIgnoreFailedSources=true'
    $appArgumentSeparatorIndex = $arguments.IndexOf('--')

    if ($appArgumentSeparatorIndex -ge 0) {
        $arguments.Insert($appArgumentSeparatorIndex, $restoreConfigProperty)
        $arguments.Insert($appArgumentSeparatorIndex + 1, $ignoreFailedSourcesProperty)
    }
    else {
        $arguments.Add($restoreConfigProperty)
        $arguments.Add($ignoreFailedSourcesProperty)
    }
}

& dotnet @arguments
exit $LASTEXITCODE
