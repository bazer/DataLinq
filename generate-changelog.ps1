# generate-changelog.ps1

# --- Configuration ---
$owner = "bazer"
$repo = "DataLinq"
$outputFile = "CHANGELOG.md"

# --- Optional: For private repos or to avoid rate limits, create a GitHub Personal Access Token (PAT) ---
# --- and uncomment the line below. Make sure the PAT has `repo` scope.                      ---
# $githubToken = "YOUR_PERSONAL_ACCESS_TOKEN_HERE"
# $headers = @{ "Authorization" = "Bearer $githubToken" }

# --- Script ---
$releasesApiUrl = "https://api.github.com/repos/$owner/$repo/releases"

Write-Host "Fetching releases from $releasesApiUrl..."

try {
    # If you've configured a token, use it. Otherwise, make an unauthenticated request.
    if ($PSBoundParameters.ContainsKey('headers')) {
        $releases = Invoke-RestMethod -Uri $releasesApiUrl -Headers $headers
    } else {
        $releases = Invoke-RestMethod -Uri $releasesApiUrl
    }
} catch {
    Write-Error "Failed to fetch releases. Check your network or token. Error: $_"
    exit 1
}

Write-Host "Found $($releases.Count) releases. Generating $outputFile..."

# Start building the Markdown content
# Using a StringBuilder is more efficient for building large strings in a loop
$markdownBuilder = [System.Text.StringBuilder]::new()
$null = $markdownBuilder.AppendLine("# DataLinq Changelog")
$null = $markdownBuilder.AppendLine()
$null = $markdownBuilder.AppendLine("All notable changes to this project will be documented in this file.")
$null = $markdownBuilder.AppendLine()
$null = $markdownBuilder.AppendLine("---")
$null = $markdownBuilder.AppendLine()


# Loop through each release to fetch its tag's commit date
foreach ($release in $releases) {
    $releaseTitle = if ([string]::IsNullOrEmpty($release.name)) { $release.tag_name } else { $release.name }
    
    # --- NEW: Fetch commit data based on the release's tag_name ---
    $tagName = $release.tag_name
    $commitApiUrl = "https://api.github.com/repos/$owner/$repo/commits/$tagName"
    $commitDate = $null

    Write-Host "  -> Processing tag: $tagName"

    try {
        if ($PSBoundParameters.ContainsKey('headers')) {
            $commitDetails = Invoke-RestMethod -Uri $commitApiUrl -Headers $headers
        } else {
            $commitDetails = Invoke-RestMethod -Uri $commitApiUrl
        }
        
        # Get the committer date, which is usually the most accurate timestamp
        $commitDate = Get-Date($commitDetails.commit.committer.date) -Format "yyyy-MM-dd"

    } catch {
        Write-Warning "Could not fetch commit details for tag '$tagName'. Falling back to release publish date. Error: $_"
        # Fallback to the original release date if the commit can't be found (e.g., for draft releases)
        $commitDate = Get-Date($release.published_at) -Format "yyyy-MM-dd"
    }
    # --- END NEW ---

    $null = $markdownBuilder.AppendLine("## [$releaseTitle]($($release.html_url))")
    $null = $markdownBuilder.AppendLine()
    $null = $markdownBuilder.AppendLine("**Released on:** $commitDate") # Use the commit date
    $null = $markdownBuilder.AppendLine()
    $null = $markdownBuilder.AppendLine($($release.body))
    $null = $markdownBuilder.AppendLine()
    $null = $markdownBuilder.AppendLine("---")
    $null = $markdownBuilder.AppendLine()
}

# Write the final content to the file
Set-Content -Path $outputFile -Value $markdownBuilder.ToString() -Encoding UTF8

Write-Host "Successfully created $outputFile."