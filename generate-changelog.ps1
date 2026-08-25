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

$publishedReleases = @($releases | Where-Object {
    -not $_.draft -and -not [string]::IsNullOrWhiteSpace([string]$_.published_at)
})

Write-Host "Found $($publishedReleases.Count) published releases. Generating $outputFile..."

# Start building the Markdown content
# Using a StringBuilder is more efficient for building large strings in a loop
$markdownBuilder = [System.Text.StringBuilder]::new()
$null = $markdownBuilder.AppendLine("# DataLinq Changelog")
$null = $markdownBuilder.AppendLine()
$null = $markdownBuilder.AppendLine("All notable changes to this project will be documented in this file.")
$null = $markdownBuilder.AppendLine()
$null = $markdownBuilder.AppendLine("---")
$null = $markdownBuilder.AppendLine()


# Use GitHub's publication timestamp rather than the tagged commit date. A release
# can be published on a later day than its commit or tag, and this changelog labels
# the date as "Released on".
foreach ($release in $publishedReleases) {
    $releaseTitle = if ([string]::IsNullOrEmpty($release.name)) { $release.tag_name } else { $release.name }
    $tagName = $release.tag_name
    $releaseDate = ([DateTimeOffset]$release.published_at).UtcDateTime.ToString("yyyy-MM-dd")

    Write-Host "  -> Processing tag: $tagName"

    $null = $markdownBuilder.AppendLine("## [$releaseTitle]($($release.html_url))")
    $null = $markdownBuilder.AppendLine()
    $null = $markdownBuilder.AppendLine("**Released on:** $releaseDate")
    $null = $markdownBuilder.AppendLine()
    $null = $markdownBuilder.AppendLine($($release.body))
    $null = $markdownBuilder.AppendLine()
    $null = $markdownBuilder.AppendLine("---")
    $null = $markdownBuilder.AppendLine()
}

# Write the final content to the file
Set-Content -Path $outputFile -Value $markdownBuilder.ToString() -Encoding UTF8

Write-Host "Successfully created $outputFile."
