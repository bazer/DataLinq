<#
.SYNOPSIS
    Generates a detailed Git log for a specified range of releases or up to the latest commit.

.DESCRIPTION
    This script fetches releases from a GitHub repository. It can be run in two modes:
    1. Interactive Mode: If no parameters are provided, it will prompt the user to select a start and end tag/point from a list.
    2. Command-Line Mode: The user can specify a -StartTag and/or -EndTag to generate the log for that specific range non-interactively.

.PARAMETER StartTag
    The Git tag to start the log from (exclusive). If omitted, the log starts from the beginning of the repository.

.PARAMETER EndTag
    The Git tag (or "HEAD") to end the log at (inclusive). If omitted when -StartTag is used, it defaults to "HEAD".

.EXAMPLE
    pwsh -File ./generate-detailed-gitlog.ps1
    # Runs the script in interactive mode.

.EXAMPLE
    pwsh -File ./generate-detailed-gitlog.ps1 -StartTag "v0.5.3"
    # Generates a log for all commits made AFTER v0.5.3 up to the latest commit (HEAD).

.EXAMPLE
    pwsh -File ./generate-detailed-gitlog.ps1 -StartTag "v0.5.2" -EndTag "v0.5.3"
    # Generates a log for all commits between v0.5.2 and v0.5.3.
#>
[CmdletBinding()]
param(
    [string]$StartTag = '',
    [string]$EndTag = ''
)

# --- Configuration ---
$owner = "bazer"
$repo = "DataLinq"
$outputFile = "gitlog-detailed.md"

# --- Optional: For private repos or to avoid rate limits ---
# $githubToken = "YOUR_PERSONAL_ACCESS_TOKEN_HERE"
# $headers = @{ "Authorization" = "Bearer $githubToken" }

# --- Script ---
$releasesApiUrl = "https://api.github.com/repos/$owner/$repo/releases"

Write-Host "Fetching releases from GitHub..."

try {
    if ($PSBoundParameters.ContainsKey('headers')) {
        $releases = Invoke-RestMethod -Uri $releasesApiUrl -Headers $headers
    } else {
        $releases = Invoke-RestMethod -Uri $releasesApiUrl
    }
} catch {
    Write-Error "Failed to fetch releases. Error: $_"
    exit 1
}

$releasesForDisplay = $releases

# --- Variables to hold the final selected range ---
$startRevision = $null
$endRevision = $null
$logTitle = ""

# --- Mode Selection Logic ---
if (-not [string]::IsNullOrEmpty($StartTag) -or -not [string]::IsNullOrEmpty($EndTag)) {
    # --- COMMAND-LINE MODE ---
    Write-Host "Command-line parameters detected. Running in non-interactive mode."
    
    if (-not [string]::IsNullOrEmpty($StartTag) -and -not ($releasesForDisplay.tag_name -contains $StartTag)) {
        Write-Error "Error: StartTag '$StartTag' not found in the list of releases."
        exit 1
    }
    $startRevision = $StartTag

    if (-not [string]::IsNullOrEmpty($EndTag)) {
        if ($EndTag.ToUpper() -ne 'HEAD' -and -not ($releasesForDisplay.tag_name -contains $EndTag)) {
            Write-Error "Error: EndTag '$EndTag' not found in releases (or it's not 'HEAD')."
            exit 1
        }
        $endRevision = $EndTag
    } else {
        $endRevision = "HEAD"
        Write-Host "EndTag not specified, defaulting to the latest commit (HEAD)."
    }

} else {
    # --- INTERACTIVE MODE ---
    Write-Host "`nPlease select the range for the git log." -ForegroundColor Yellow

    # Select START Tag
    Write-Host "`n--- Select START Point ---" -ForegroundColor Green
    Write-Host "0) From the beginning of the repository"
    for ($i = 0; $i -lt $releasesForDisplay.Count; $i++) {
        Write-Host ("{0}) {1} ({2})" -f ($i + 1), $releasesForDisplay[$i].tag_name, $releasesForDisplay[$i].name)
    }
    Write-Host "------------------------" -ForegroundColor Green

    while ($true) {
        try {
            $startChoiceStr = Read-Host -Prompt "Enter START tag number (e.g., '0' for beginning)"
            $startChoice = [int]$startChoiceStr
            if ($startChoice -ge 0 -and $startChoice -le $releasesForDisplay.Count) {
                $startRevision = if ($startChoice -eq 0) { $null } else { $releasesForDisplay[$startChoice - 1].tag_name }
                break
            } else { Write-Warning "Invalid selection." }
        } catch { Write-Warning "Invalid input. Please enter a number." }
    }

    # Select END Tag
    Write-Host "`n--- Select END Point ---" -ForegroundColor Green
    Write-Host "h) The most recent commit (HEAD)"
    for ($i = 0; $i -lt $releasesForDisplay.Count; $i++) {
        Write-Host ("{0}) {1} ({2})" -f ($i + 1), $releasesForDisplay[$i].tag_name, $releasesForDisplay[$i].name)
    }
    Write-Host "----------------------" -ForegroundColor Green

    while ($true) {
        $latestReleaseTag = $releasesForDisplay[0].tag_name
        $endChoiceStr = Read-Host -Prompt "Enter END tag number (or 'h' for HEAD, or press Enter for latest release: $latestReleaseTag)"
        
        if ($endChoiceStr.ToLower() -eq 'h') {
            $endRevision = "HEAD"
            break
        }
        if ([string]::IsNullOrEmpty($endChoiceStr)) {
            $endRevision = $latestReleaseTag
            break
        }
        try {
            $endChoice = [int]$endChoiceStr
            if ($endChoice -ge 1 -and $endChoice -le $releasesForDisplay.Count) {
                $endRevision = $releasesForDisplay[$endChoice - 1].tag_name
                break
            } else { Write-Warning "Invalid selection." }
        } catch { Write-Warning "Invalid input. Please enter 'h' or a number." }
    }
}

# --- GIT LOG GENERATION (COMMON LOGIC) ---
$logTitle = "Log from $($startRevision ?? 'Beginning') to $endRevision"
Write-Host "`nGenerating log for range: $($startRevision ?? 'Beginning') -> $endRevision" -ForegroundColor Cyan

$logBuilder = [System.Text.StringBuilder]::new()
$null = $logBuilder.AppendLine("# DataLinq Detailed Git Log - $logTitle")
$null = $logBuilder.AppendLine()
$null = $logBuilder.AppendLine('```diff')

$prettyFormat = '--pretty=format:"%n--------------------------------------------------%n%C(yellow)commit %H%d%n%C(white)Author: %an <%ae>%nDate:   %ai%n%n%C(reset)    %s%n%n%b"'

$revisionRange = if ([string]::IsNullOrEmpty($startRevision)) {
    $endRevision # Log from beginning up to the end point
} else {
    "$startRevision..$endRevision" # Log between the two points
}

$gitCommand = "git --no-pager log $revisionRange $prettyFormat --stat --patch"

try {
    $commitLog = Invoke-Expression $gitCommand | Out-String
    $null = $logBuilder.AppendLine($commitLog)
} catch {
    Write-Warning "Failed to run git log for range '$revisionRange'. Error: $_"
    $null = $logBuilder.AppendLine("ERROR: Could not generate log for range '$revisionRange'.")
}

$null = $logBuilder.AppendLine('```')
$null = $logBuilder.AppendLine()

Set-Content -Path $outputFile -Value $logBuilder.ToString() -Encoding UTF8

Write-Host "`nSuccessfully created $outputFile."