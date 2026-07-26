#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) "dist")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$SourceRoot = Split-Path -Parent $PSScriptRoot
$stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$PackageRoot = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) "Axon-Operations-Kit-$stamp"
$ZipPath = "$PackageRoot.zip"

New-Item -ItemType Directory -Force -Path (Join-Path $PackageRoot "scripts"), (Join-Path $PackageRoot "docs") | Out-Null
Copy-Item -LiteralPath (Join-Path $SourceRoot "Axon Operations.cmd") -Destination $PackageRoot
Copy-Item -LiteralPath (Join-Path $SourceRoot "Start Axon.cmd") -Destination $PackageRoot
Copy-Item -LiteralPath (Join-Path $SourceRoot "Install Axon Operations.cmd") -Destination $PackageRoot
Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\Axon-Operations.ps1") -Destination (Join-Path $PackageRoot "scripts")
Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\Install-AxonOperations.ps1") -Destination (Join-Path $PackageRoot "scripts")
Copy-Item -LiteralPath (Join-Path $SourceRoot "docs\operator\START_STOP_RESTART.md") -Destination (Join-Path $PackageRoot "docs")

@"
1. Keep this ZIP in the original Axon folder's updates directory.
2. Extract the complete kit into its own updates subfolder.
3. Double-click Install Axon Operations.cmd.
4. Use the new Start Axon desktop shortcut after a reboot.
5. Use Axon Operations for stop, restart, status, and control-panel actions.
"@ | Set-Content -LiteralPath (Join-Path $PackageRoot "README FIRST.txt") -Encoding ASCII

$files = Get-ChildItem -LiteralPath $PackageRoot -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = [IO.Path]::GetRelativePath($PackageRoot, $_.FullName)
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
[ordered]@{
    schemaVersion = 1
    builtUtc = [DateTime]::UtcNow.ToString("o")
    files = @($files)
} | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $PackageRoot "manifest.json") -Encoding UTF8

Compress-Archive -Path (Join-Path $PackageRoot "*") -DestinationPath $ZipPath -CompressionLevel Optimal
Write-Host "Axon operations kit: $PackageRoot"
Write-Host "Axon operations ZIP: $ZipPath"
