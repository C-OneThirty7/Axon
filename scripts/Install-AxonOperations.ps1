#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$BundleRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$PackageRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    $cursor = $PackageRoot
    for ($depth = 0; $depth -lt 7; $depth++) {
        if (Test-Path -LiteralPath (Join-Path $cursor "deploy\compose.yaml") -PathType Leaf) {
            $BundleRoot = $cursor
            break
        }
        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) { break }
        $cursor = $parent
    }
}
if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    throw "The original Axon folder was not found. Extract this kit beneath its updates folder or pass -BundleRoot."
}
$BundleRoot = [IO.Path]::GetFullPath($BundleRoot)

$launcherSource = Join-Path $PackageRoot "Axon Operations.cmd"
$startSource = Join-Path $PackageRoot "Start Axon.cmd"
$scriptSource = Join-Path $PSScriptRoot "Axon-Operations.ps1"
$guideSource = Join-Path $PackageRoot "docs\START_STOP_RESTART.md"
foreach ($required in @($launcherSource, $startSource, $scriptSource, $guideSource)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Operations-kit file is missing: $required"
    }
}

$bundleScripts = Join-Path $BundleRoot "scripts"
$bundleDocs = Join-Path $BundleRoot "docs\operator"
New-Item -ItemType Directory -Force -Path $bundleScripts, $bundleDocs | Out-Null
Copy-Item -LiteralPath $launcherSource -Destination (Join-Path $BundleRoot "Axon Operations.cmd") -Force
Copy-Item -LiteralPath $startSource -Destination (Join-Path $BundleRoot "Start Axon.cmd") -Force
Copy-Item -LiteralPath $scriptSource -Destination (Join-Path $bundleScripts "Axon-Operations.ps1") -Force
Copy-Item -LiteralPath $guideSource -Destination (Join-Path $bundleDocs "START_STOP_RESTART.md") -Force

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut(
    (Join-Path ([Environment]::GetFolderPath("Desktop")) "Axon Operations.lnk"))
$shortcut.TargetPath = Join-Path $BundleRoot "Axon Operations.cmd"
$shortcut.WorkingDirectory = $BundleRoot
$shortcut.Description = "Start, stop, restart, and inspect Axon"
$shortcut.Save()

$startShortcut = $shell.CreateShortcut(
    (Join-Path ([Environment]::GetFolderPath("Desktop")) "Start Axon.lnk"))
$startShortcut.TargetPath = Join-Path $BundleRoot "Start Axon.cmd"
$startShortcut.WorkingDirectory = $BundleRoot
$startShortcut.Description = "Start Docker Desktop and all Axon services"
$startShortcut.Save()

Write-Host "Axon Operations installed successfully."
Write-Host "Original Axon folder: $BundleRoot"
Write-Host "Use Start Axon for one-click cold startup or Axon Operations for full control."
