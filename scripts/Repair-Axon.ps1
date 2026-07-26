#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$DataRoot = (Join-Path $env:ProgramData "Axon"),
    [string]$BindIp,
    [string]$InterfaceAlias,
    [string[]]$AllowedRemoteAddress = @("LocalSubnet"),
    [ValidateSet("Strict", "Warn", "Skip")][string]$ChecksumMode = "Strict",
    [ValidateSet("Preserve", "Configure")][string]$NicMode = "Preserve",
    [switch]$StrictPreflight
)

$installScript = Join-Path $PSScriptRoot "Install-Axon.ps1"
& $installScript -DataRoot $DataRoot -BindIp $BindIp -InterfaceAlias $InterfaceAlias `
    -AllowedRemoteAddress $AllowedRemoteAddress `
    -ChecksumMode $ChecksumMode -NicMode $NicMode -StrictPreflight:$StrictPreflight `
    -Repair -SkipInitialUser
exit $LASTEXITCODE
