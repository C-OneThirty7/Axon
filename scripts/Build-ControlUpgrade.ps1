#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) "dist")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$SourceRoot = Split-Path -Parent $PSScriptRoot
$stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$PackageRoot = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) "Axon-Control-Upgrade-$stamp-win-x64"
$PayloadRoot = Join-Path $PackageRoot "payload"
$ScriptRoot = Join-Path $PackageRoot "scripts"
$ZipPath = "$PackageRoot.zip"

if (Test-Path -LiteralPath $PackageRoot) { throw "Output already exists: $PackageRoot" }
New-Item -ItemType Directory -Force -Path $PayloadRoot, $ScriptRoot | Out-Null

& dotnet publish (Join-Path $SourceRoot "src\Axon.Control\Axon.Control.csproj") `
    -c Release -r win-x64 --self-contained true -o $PayloadRoot
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $PayloadRoot "Axon.Control.exe"))) {
    throw "dotnet publish did not produce Axon.Control.exe."
}

Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\Update-AxonControl.ps1") `
    -Destination (Join-Path $ScriptRoot "Update-AxonControl.ps1")
Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\Update-AxonControl.cmd") `
    -Destination (Join-Path $PackageRoot "Update Axon Control.cmd")
Copy-Item -LiteralPath (Join-Path $SourceRoot "docs\operator\AXON_CONTROL.md") `
    -Destination (Join-Path $PackageRoot "Axon Control Operator Guide.md")
Copy-Item -LiteralPath (Join-Path $SourceRoot "output\pdf\Axon_Maintenance_and_Tuning_Guide.pdf") `
    -Destination (Join-Path $PackageRoot "Axon Maintenance and Tuning Guide.pdf")

$manifest = Get-ChildItem -LiteralPath $PayloadRoot -File -Recurse |
    Sort-Object FullName |
    ForEach-Object {
        [ordered]@{
            path = [IO.Path]::GetRelativePath($PayloadRoot, $_.FullName)
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
[ordered]@{
    schemaVersion = 1
    files = @($manifest)
} | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $PackageRoot "manifest.json") -Encoding UTF8

Compress-Archive -Path (Join-Path $PackageRoot "*") -DestinationPath $ZipPath -CompressionLevel Optimal
Write-Host "Axon Control upgrade: $PackageRoot"
Write-Host "Axon Control upgrade ZIP: $ZipPath"
