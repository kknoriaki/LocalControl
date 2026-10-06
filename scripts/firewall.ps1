[CmdletBinding()]
param([switch]$Remove,[ValidateRange(1024,65535)][int]$Port=41017)
$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this firewall helper in an elevated PowerShell window. Run LocalControl itself as a normal user.' }
$ruleName = 'LocalControl-LAN-41017' # Stable rule identity; configured port may change.
if ($Remove) { Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule; return }
$exe = Join-Path $PSScriptRoot 'LocalControl.exe'
if (-not (Test-Path $exe -PathType Leaf)) { throw 'Place this helper beside the installed LocalControl.exe.' }
# The helper performs only this explicit firewall action. It never enables the
# listener, public profile, arbitrary ports, or a broad executable wildcard.
Get-NetFirewallRule -Name $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -Name $ruleName -DisplayName 'LocalControl LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Program (Resolve-Path $exe).Path -Profile Private -RemoteAddress LocalSubnet
