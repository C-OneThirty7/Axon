#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$DataRoot = (Join-Path $env:ProgramData "Axon"),
    [switch]$PurgeData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$BundleRoot = Split-Path -Parent $PSScriptRoot
$ComposeFile = Join-Path $BundleRoot "deploy\compose.yaml"
$EnvFile = Join-Path $DataRoot ".env"

if (Test-Path -LiteralPath $EnvFile) {
    & docker compose --project-name axon --env-file $EnvFile --file $ComposeFile down
}
Get-ScheduledTask -TaskName "Axon Control Panel" -ErrorAction SilentlyContinue |
    Stop-ScheduledTask -ErrorAction SilentlyContinue
Get-ScheduledTask -TaskName "Axon Control Panel" -ErrorAction SilentlyContinue |
    Unregister-ScheduledTask -Confirm:$false
$controlShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Axon Control.url"
if (Test-Path -LiteralPath $controlShortcut) {
    Remove-Item -LiteralPath $controlShortcut -Force
}
$operationsShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Axon Operations.lnk"
if (Test-Path -LiteralPath $operationsShortcut) {
    Remove-Item -LiteralPath $operationsShortcut -Force
}
$startShortcut = Join-Path ([Environment]::GetFolderPath("Desktop")) "Start Axon.lnk"
if (Test-Path -LiteralPath $startShortcut) {
    Remove-Item -LiteralPath $startShortcut -Force
}
Get-NetFirewallRule -Group Axon -ErrorAction SilentlyContinue | Remove-NetFirewallRule

if ($PurgeData) {
    $confirmation = Read-Host "Type PURGE AXON to permanently delete Axon volumes and data"
    if ($confirmation -cne "PURGE AXON") {
        throw "Purge confirmation did not match. Axon data was preserved."
    }
    if (Test-Path -LiteralPath $EnvFile) {
        & docker compose --project-name axon --env-file $EnvFile --file $ComposeFile down --volumes
    }
    if (Test-Path -LiteralPath $DataRoot) {
        Remove-Item -LiteralPath $DataRoot -Recurse -Force
    }
    Write-Host "Axon application data and Docker volumes were permanently removed."
} else {
    Write-Host "Axon services and firewall rules were removed. Data was preserved at $DataRoot."
}
