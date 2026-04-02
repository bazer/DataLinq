[CmdletBinding()]
param(
    [string]$Profile
)

. "$PSScriptRoot\common.ps1"

Assert-PodmanAvailable

& "$PSScriptRoot\down.ps1" -Remove
& "$PSScriptRoot\up.ps1" -Profile $Profile
