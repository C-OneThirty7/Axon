[CmdletBinding()]
param(
    [ValidateSet("Strict", "Warn", "Skip")][string]$Mode = "Strict"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$bundleRoot = Split-Path -Parent $PSScriptRoot
$modulePath = Join-Path $PSScriptRoot "Axon.Common.psm1"
$manifestPath = Join-Path $bundleRoot "manifests\SHA256SUMS"

Import-Module -Name $modulePath -Force
$valid = Test-AxonBundleChecksums -BundleRoot $bundleRoot -ManifestPath $manifestPath -Mode $Mode
if (-not $valid) { exit 1 }
Write-Host "Axon bundle is ready for installation."
