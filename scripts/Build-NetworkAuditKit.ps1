#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) "dist")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$SourceRoot = Split-Path -Parent $PSScriptRoot
$stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$PackageRoot = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) "Axon-Network-Audit-Kit-$stamp"
$ScriptsRoot = Join-Path $PackageRoot "scripts\network-audit"
$DocsRoot = Join-Path $PackageRoot "docs"
$ZipPath = "$PackageRoot.zip"

if (Test-Path -LiteralPath $PackageRoot) { throw "Output already exists: $PackageRoot" }
New-Item -ItemType Directory -Force -Path $ScriptsRoot, $DocsRoot | Out-Null

Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\network-audit\Start-AxonTrafficAudit.ps1") `
    -Destination (Join-Path $ScriptsRoot "Start-AxonTrafficAudit.ps1")
Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\network-audit\Summarize-AxonPcap.ps1") `
    -Destination (Join-Path $ScriptsRoot "Summarize-AxonPcap.ps1")
Copy-Item -LiteralPath (Join-Path $SourceRoot "docs\operator\NETWORK_BANDWIDTH_AUDIT.md") `
    -Destination (Join-Path $DocsRoot "NETWORK_BANDWIDTH_AUDIT.md")

@"
AXON NETWORK AUDIT KIT

1. Extract this complete folder beneath the original Axon folder's updates directory.
2. Read docs\NETWORK_BANDWIDTH_AUDIT.md.
3. Open Windows PowerShell as Administrator.
4. Run scripts\network-audit\Start-AxonTrafficAudit.ps1 with the current Axon IP and one named scenario.

Packet captures are sensitive because Axon currently uses HTTP.
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
Write-Host "Axon network audit kit: $PackageRoot"
Write-Host "Axon network audit ZIP: $ZipPath"
