[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ConsumerAssembly,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [int]$FirstPort = 13321
)

# The ordinary Testing CLI catalog has no server-start argument override.
# These dedicated, disposable containers do not replace its targets or state.
$ErrorActionPreference = 'Stop'
$consumer = (Resolve-Path -LiteralPath $ConsumerAssembly).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a fresh output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
$podman = (Get-Command podman -CommandType Application).Source
$dotnet = (Get-Command dotnet -CommandType Application).Source
$records = [Collections.Generic.List[object]]::new()
$report = [ordered]@{ Passed = $false; Consumer = $consumer; ConsumerSha256 = (Get-FileHash -LiteralPath $consumer -Algorithm SHA256).Hash.ToLowerInvariant() }
$previousConnection = $env:DATALINQ_W5_SERVER_CONNECTION
$port = $FirstPort
try {
    foreach ($family in 'MySQL','MariaDB') {
        foreach ($mode in 1,2) {
            $name = 'datalinq-w5-case-' + [Guid]::NewGuid().ToString('N')
            $image = if ($family -eq 'MySQL') { 'mysql:9.7' } else { 'mariadb:12.3' }
            $prefix = if ($family -eq 'MySQL') { 'MYSQL' } else { 'MARIADB' }
            $password = [Guid]::NewGuid().ToString('N')
            $envFile = Join-Path $output "$family-$mode.env"
            [IO.File]::WriteAllLines($envFile, @("${prefix}_ROOT_PASSWORD=$password", "${prefix}_ROOT_HOST=%", "${prefix}_DATABASE=w5_fixture"))
            $created = $false
            try {
                $container = & $podman run -d --name $name --env-file $envFile -p "127.0.0.1:${port}:3306" $image "--lower-case-table-names=$mode" --character-set-server=utf8mb4 --collation-server=utf8mb4_unicode_ci
                if ($LASTEXITCODE -ne 0) { throw "Could not start $family mode $mode fixture." }
                $created = $true
                Remove-Item -LiteralPath $envFile
                $env:DATALINQ_W5_SERVER_CONNECTION = "Server=127.0.0.1;Port=$port;User ID=root;Password=$password;Database=w5_fixture;SslMode=None;AllowPublicKeyRetrieval=True;Pooling=False;Connection Timeout=2"
                $resultPath = Join-Path $output "$family-$mode.json"
                $lines = @(& $dotnet $consumer --server-case $family $mode $resultPath 2>&1)
                $exit = $LASTEXITCODE
                $lines | Set-Content -LiteralPath (Join-Path $output "$family-$mode.log")
                if ($exit -ne 0) { throw "$family mode $mode consumer failed; see retained log." }
                $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
                if ($mode -eq 1 -and (!$result.Passed -or $result.Observed -ne 1)) { throw 'Native mode 1 was not verified.' }
                $imageIdentity = & $podman inspect --format '{{.Image}}' $name
                if ($LASTEXITCODE -ne 0) { throw 'Could not record container image identity.' }
                $records.Add(@{ Family = $family; Mode = $mode; Image = $image; ImageId = $imageIdentity; Container = $container; Result = $result })
            } finally {
                if ($created) {
                    & $podman logs $name 2>&1 | Set-Content -LiteralPath (Join-Path $output "$family-$mode-server.log")
                    & $podman rm -f $name | Out-Null
                    if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove dedicated fixture container $name." }
                }
                if (Test-Path -LiteralPath $envFile) { Remove-Item -LiteralPath $envFile }
                $port++
            }
        }
    }
    $report.Passed = $true
} finally {
    $env:DATALINQ_W5_SERVER_CONNECTION = $previousConnection
    $report.Records = $records.ToArray()
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'report.json')
}
Write-Output "Native mode-1 checks and explicit mode-2 dispositions recorded at $output/report.json"
