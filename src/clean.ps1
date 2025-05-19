# Get the current directory (should be your solution root)
$solutionRoot = Get-Location

Write-Host "Searching for 'bin' and 'obj' folders in: $($solutionRoot.Path)"
Write-Host "This will permanently delete these folders and their contents."
$confirmation = Read-Host "Are you sure you want to continue? (y/n)"

if ($confirmation -ne 'y') {
    Write-Host "Operation cancelled by user."
    Exit
}

# Find and delete 'bin' folders
Get-ChildItem -Path $solutionRoot -Recurse -Directory -Filter "bin" | ForEach-Object {
    Write-Host "Deleting: $($_.FullName)"
    Remove-Item -Recurse -Force $_.FullName
}

# Find and delete 'obj' folders
Get-ChildItem -Path $solutionRoot -Recurse -Directory -Filter "obj" | ForEach-Object {
    Write-Host "Deleting: $($_.FullName)"
    Remove-Item -Recurse -Force $_.FullName
}

Write-Host "Deletion complete."