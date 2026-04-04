[CmdletBinding()]
param(
    [string]$Profile,
    [string[]]$TargetIds,
    [switch]$AllLts
)

. "$PSScriptRoot\common.ps1"

Assert-PodmanAvailable

& "$PSScriptRoot\down.ps1" -Remove
& "$PSScriptRoot\up.ps1" -Profile $Profile -TargetIds $TargetIds -AllLts:$AllLts
