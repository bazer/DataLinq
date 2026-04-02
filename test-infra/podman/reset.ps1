[CmdletBinding()]
param()

. "$PSScriptRoot\common.ps1"

Assert-PodmanAvailable

& "$PSScriptRoot\down.ps1" -Remove
& "$PSScriptRoot\up.ps1"
