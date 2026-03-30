[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Configuration = "Release",
    [string]$Source = "https://api.nuget.org/v3/index.json",
    [string]$ApiKey = $env:NUGET_API_KEY,
    [string]$PackageOutputPath,
    [string]$Version,
    [ValidateSet("Public", "All")]
    [string]$PackageSet = "Public",
    [switch]$PackOnly,
    [switch]$SkipPack,
    [switch]$SkipDuplicate = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    $repoRoot = (Get-Location).Path
}

if ($PackOnly -and $SkipPack) {
    throw "-PackOnly and -SkipPack cannot be used together."
}

$hadDotnetCliHome = Test-Path Env:DOTNET_CLI_HOME
$previousDotnetCliHome = $env:DOTNET_CLI_HOME
$hadDotnetSkipFirstTimeExperience = Test-Path Env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$previousDotnetSkipFirstTimeExperience = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
$hadDotnetNoLogo = Test-Path Env:DOTNET_NOLOGO
$previousDotnetNoLogo = $env:DOTNET_NOLOGO

$dotnetHome = Join-Path $repoRoot ".dotnet"
$env:DOTNET_CLI_HOME = $dotnetHome
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_NOLOGO = "1"

if (-not (Test-Path -LiteralPath $dotnetHome)) {
    New-Item -ItemType Directory -Path $dotnetHome | Out-Null
}

try {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    if ([string]::IsNullOrWhiteSpace($PackageOutputPath)) {
        $PackageOutputPath = Join-Path $repoRoot "artifacts\nuget-release\$timestamp"
    }
    else {
        if ([System.IO.Path]::IsPathRooted($PackageOutputPath)) {
            $PackageOutputPath = [System.IO.Path]::GetFullPath($PackageOutputPath)
        }
        else {
            $PackageOutputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PackageOutputPath))
        }
    }

    $packageProjects = @(
        [pscustomobject]@{ Name = "DataLinq"; Project = "src\DataLinq\DataLinq.csproj"; Public = $true },
        [pscustomobject]@{ Name = "DataLinq.SQLite"; Project = "src\DataLinq.SQLite\DataLinq.SQLite.csproj"; Public = $true },
        [pscustomobject]@{ Name = "DataLinq.MySql"; Project = "src\DataLinq.MySql\DataLinq.MySql.csproj"; Public = $true },
        [pscustomobject]@{ Name = "DataLinq.CLI"; Project = "src\DataLinq.CLI\DataLinq.CLI.csproj"; Public = $true },
        [pscustomobject]@{ Name = "DataLinq.Tools"; Project = "src\DataLinq.Tools\DataLinq.Tools.csproj"; Public = $true }
    )

    if ($PackageSet -eq "Public") {
        $packageProjects = $packageProjects | Where-Object Public
    }

    if (-not $packageProjects -or $packageProjects.Count -eq 0) {
        throw "No package projects selected."
    }

    if (-not (Test-Path -LiteralPath $PackageOutputPath)) {
        New-Item -ItemType Directory -Path $PackageOutputPath -Force | Out-Null
    }

    function Assert-NoBlockingProcesses {
        $docfxProcesses = Get-Process -Name "docfx" -ErrorAction SilentlyContinue
        if ($null -ne $docfxProcesses) {
            $lockedPath = Join-Path $repoRoot "src\DataLinq.Generators\bin\$Configuration\netstandard2.0\DataLinq.Generators.dll"
            throw "docfx is currently running and is likely locking '$lockedPath'. Stop docfx and rerun publish-nuget.ps1."
        }
    }

    function Invoke-DotNet {
        param(
            [Parameter(Mandatory = $true)]
            [string[]]$Arguments
        )

        & dotnet @Arguments
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
        }
    }

    function Read-ApiKeyFromPrompt {
        if (-not $Host -or -not $Host.UI) {
            throw "No interactive host is available to prompt for the NuGet API key. Pass -ApiKey explicitly."
        }

        $secureApiKey = Read-Host "NuGet API key" -AsSecureString
        if ($null -eq $secureApiKey -or $secureApiKey.Length -eq 0) {
            throw "No NuGet API key was entered."
        }

        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureApiKey)
        try {
            return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        }
        finally {
            if ($bstr -ne [IntPtr]::Zero) {
                [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
            }
        }
    }

    Assert-NoBlockingProcesses

    if (-not $SkipPack) {
        Write-Host "Packing package set '$PackageSet' to '$PackageOutputPath'."

        foreach ($packageProject in $packageProjects) {
            $projectPath = Join-Path $repoRoot $packageProject.Project
            if (-not (Test-Path -LiteralPath $projectPath)) {
                throw "Project not found: $projectPath"
            }

            $arguments = @(
                "pack",
                $projectPath,
                "-c", $Configuration,
                "--output", $PackageOutputPath,
                "--disable-build-servers"
            )

            if (-not [string]::IsNullOrWhiteSpace($Version)) {
                $arguments += "-p:PackageVersion=$Version"
            }

            if ($PSCmdlet.ShouldProcess($packageProject.Name, "dotnet pack")) {
                Invoke-DotNet -Arguments $arguments
            }
        }
    }
    else {
        if (-not (Test-Path -LiteralPath $PackageOutputPath)) {
            throw "Package output path does not exist: $PackageOutputPath"
        }

        Write-Host "Skipping pack. Reusing packages from '$PackageOutputPath'."
    }

    $mainPackages = Get-ChildItem -LiteralPath $PackageOutputPath -Filter *.nupkg -File |
        Where-Object { $_.Name -notlike "*.symbols.nupkg" } |
        Sort-Object Name

    if (-not $mainPackages) {
        throw "No .nupkg files were produced in '$PackageOutputPath'."
    }

    $symbolPackages = Get-ChildItem -LiteralPath $PackageOutputPath -Filter *.snupkg -File |
        Sort-Object Name

    Write-Host ""
    Write-Host "Produced packages:"
    $mainPackages | ForEach-Object { Write-Host "  $($_.Name)" }
    Write-Host ""
    Write-Host "Produced symbol packages:"
    $symbolPackages | ForEach-Object { Write-Host "  $($_.Name)" }

    if ($PackOnly) {
        Write-Host ""
        Write-Host "PackOnly specified. Skipping push."
        return
    }

    if ($WhatIfPreference) {
        Write-Host ""
        Write-Host "WhatIf active. Skipping NuGet API key validation."
    }
    elseif ([string]::IsNullOrWhiteSpace($ApiKey)) {
        $ApiKey = Read-ApiKeyFromPrompt
    }

    foreach ($mainPackage in $mainPackages) {
        $symbolPackagePath = [System.IO.Path]::ChangeExtension($mainPackage.FullName, ".snupkg")

        $arguments = @(
            "nuget",
            "push",
            $mainPackage.FullName,
            "--source", $Source,
            "--api-key", $ApiKey,
            "--no-symbols"
        )

        if ($SkipDuplicate) {
            $arguments += "--skip-duplicate"
        }

        if ($PSCmdlet.ShouldProcess($mainPackage.Name, "dotnet nuget push")) {
            Invoke-DotNet -Arguments $arguments
        }

        if (Test-Path -LiteralPath $symbolPackagePath) {
            $symbolArguments = @(
                "nuget",
                "push",
                $symbolPackagePath,
                "--source", $Source,
                "--api-key", $ApiKey
            )

            if ($SkipDuplicate) {
                $symbolArguments += "--skip-duplicate"
            }

            if ($PSCmdlet.ShouldProcess([System.IO.Path]::GetFileName($symbolPackagePath), "dotnet nuget push")) {
                Invoke-DotNet -Arguments $symbolArguments
            }
        }
    }

    Write-Host ""
    Write-Host "NuGet publish completed."
}
finally {
    if ($hadDotnetCliHome) {
        $env:DOTNET_CLI_HOME = $previousDotnetCliHome
    }
    else {
        Remove-Item Env:DOTNET_CLI_HOME -ErrorAction SilentlyContinue
    }

    if ($hadDotnetSkipFirstTimeExperience) {
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $previousDotnetSkipFirstTimeExperience
    }
    else {
        Remove-Item Env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE -ErrorAction SilentlyContinue
    }

    if ($hadDotnetNoLogo) {
        $env:DOTNET_NOLOGO = $previousDotnetNoLogo
    }
    else {
        Remove-Item Env:DOTNET_NOLOGO -ErrorAction SilentlyContinue
    }
}
