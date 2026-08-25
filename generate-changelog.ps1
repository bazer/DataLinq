# generate-changelog.ps1

# --- Configuration ---
$owner = "bazer"
$repo = "DataLinq"
$outputFile = "CHANGELOG.md"

# Several historical GitHub release objects were created after their versions had
# already shipped. Keep the established release dates for those backfilled tags;
# releases not listed here use GitHub's authoritative publication timestamp.
$legacyReleaseDates = @{
    "0.8.0" = "2026-07-04"
    "0.7.1" = "2026-06-25"
    "0.7.0" = "2026-05-18"
    "0.6.9" = "2026-04-28"
    "0.6.8" = "2026-04-17"
    "0.6.7" = "2026-03-27"
    "0.6.6" = "2025-12-18"
    "0.6.5" = "2025-11-12"
    "0.6.4" = "2025-08-26"
    "0.6.3" = "2025-08-17"
    "0.6.2" = "2025-08-17"
    "0.6.1" = "2025-08-04"
    "0.6.0" = "2025-07-29"
    "0.5.4" = "2025-06-11"
    "0.5.3" = "2025-06-04"
    "0.5.2" = "2025-05-19"
    "0.5.1" = "2025-04-11"
    "0.5.0" = "2025-04-02"
    "0.0.1" = "2020-06-06"
}

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


# Use the preserved date for a known backfilled release. New releases use GitHub's
# publication timestamp rather than the tagged commit date because the changelog
# labels this value as "Released on".
foreach ($release in $publishedReleases) {
    $releaseTitle = if ([string]::IsNullOrEmpty($release.name)) { $release.tag_name } else { $release.name }
    $tagName = $release.tag_name
    $releaseDate = if ($legacyReleaseDates.ContainsKey($tagName)) {
        $legacyReleaseDates[$tagName]
    } else {
        ([DateTimeOffset]$release.published_at).UtcDateTime.ToString("yyyy-MM-dd")
    }

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
