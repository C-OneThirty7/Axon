#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$OutputRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) "dist"),
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = "0.1.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$SourceRoot = Split-Path -Parent $PSScriptRoot
$InputPath = Join-Path $SourceRoot "manifests\release-inputs.json"
$inputs = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
$headers = @{ "User-Agent" = "Axon-Offline-Packager"; "Accept" = "application/vnd.github+json" }

function Assert-StableRelease {
    param([Parameter(Mandatory)]$Release, [Parameter(Mandatory)][string]$Name)
    if ($Release.draft -or $Release.prerelease) { throw "$Name returned a draft or prerelease." }
    if ([string]::IsNullOrWhiteSpace([string]$Release.tag_name)) { throw "$Name returned no tag." }
}

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw "docker $($Arguments -join ' ') failed." }
}

function Get-RepoDigest {
    param([Parameter(Mandatory)][string]$Reference)
    $digest = (& docker image inspect $Reference --format '{{index .RepoDigests 0}}').Trim()
    if ($LASTEXITCODE -ne 0 -or $digest -notmatch '@sha256:[a-fA-F0-9]{64}$') {
        throw "No immutable RepoDigests value was available for $Reference."
    }
    return $digest
}

function Export-OfflineImage {
    param(
        [Parameter(Mandatory)][string]$SourceDigest,
        [Parameter(Mandatory)][string]$Component,
        [Parameter(Mandatory)][string]$DestinationDirectory
    )

    $digestHex = ($SourceDigest -split '@sha256:')[1]
    if ($digestHex -notmatch '^[a-fA-F0-9]{64}$') { throw "Invalid upstream digest for $Component." }
    $localReference = "axon.local/${Component}:sha256-$digestHex"
    $archivePath = Join-Path $DestinationDirectory "$Component-linux-amd64.tar"
    $temporaryContext = Join-Path ([IO.Path]::GetTempPath()) "axon-image-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Force -Path $temporaryContext | Out-Null
    try {
        "FROM $SourceDigest" | Set-Content -LiteralPath (Join-Path $temporaryContext "Dockerfile") -Encoding ASCII
        & docker buildx build --platform linux/amd64 --pull `
            --tag $localReference --output "type=docker,dest=$archivePath" $temporaryContext
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $archivePath) -or (Get-Item $archivePath).Length -eq 0) {
            throw "docker buildx build did not export $Component for linux/amd64."
        }
    } finally {
        Remove-Item -LiteralPath $temporaryContext -Recurse -Force -ErrorAction SilentlyContinue
    }
    return $localReference
}

$synapseRelease = Invoke-RestMethod -Uri $inputs.synapseLatestReleaseApi -Headers $headers
Assert-StableRelease -Release $synapseRelease -Name "Synapse release API"
if ([string]$synapseRelease.tag_name -notmatch '^v\d+\.\d+\.\d+$') {
    throw "Synapse latest tag is not a stable semantic version: $($synapseRelease.tag_name)"
}
$synapseTagged = "$($inputs.synapseImage):$($synapseRelease.tag_name)"

$wslRelease = Invoke-RestMethod -Uri $inputs.wslLatestReleaseApi -Headers $headers
Assert-StableRelease -Release $wslRelease -Name "WSL release API"
$wslAssets = @($wslRelease.assets | Where-Object {
    $_.name -match '(?i)x64.*\.msi$' -and $_.name -notmatch '(?i)arm64'
})
if ($wslAssets.Count -ne 1) { throw "Expected exactly one x64 WSL MSI asset." }
$wslAsset = $wslAssets[0]

$BundleRoot = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) "Axon-v$Version-offline-win-x64"
$ZipPath = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) "Axon-v$Version-offline-win-x64.zip"
if (Test-Path -LiteralPath $BundleRoot) { throw "Output already exists: $BundleRoot" }
New-Item -ItemType Directory -Force -Path $BundleRoot | Out-Null
foreach ($directory in @("bin", "deploy", "docs", "docs\security", "docs\user-guides", "images", "installers", "manifests", "scripts")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $BundleRoot $directory) | Out-Null
}

Copy-Item -LiteralPath (Join-Path $SourceRoot "deploy\compose.yaml") -Destination (Join-Path $BundleRoot "deploy\compose.yaml")
Copy-Item -LiteralPath (Join-Path $SourceRoot "deploy\nginx") -Destination (Join-Path $BundleRoot "deploy\nginx") -Recurse
Copy-Item -LiteralPath (Join-Path $SourceRoot "deploy\synapse") -Destination (Join-Path $BundleRoot "deploy\synapse") -Recurse
Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\Install-Axon.ps1") -Destination (Join-Path $BundleRoot "scripts\Install-Axon.ps1")
Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\Axon.Common.psm1") -Destination (Join-Path $BundleRoot "scripts\Axon.Common.psm1")
Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\Test-AxonBundle.ps1") -Destination (Join-Path $BundleRoot "scripts\Test-AxonBundle.ps1")
Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\Repair-Axon.ps1") -Destination (Join-Path $BundleRoot "scripts\Repair-Axon.ps1")
Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\Test-Axon.ps1") -Destination (Join-Path $BundleRoot "scripts\Test-Axon.ps1")
Copy-Item -LiteralPath (Join-Path $SourceRoot "scripts\Uninstall-Axon.ps1") -Destination (Join-Path $BundleRoot "scripts\Uninstall-Axon.ps1")
Copy-Item -LiteralPath (Join-Path $SourceRoot "Install Axon.cmd") -Destination (Join-Path $BundleRoot "Install Axon.cmd")
Copy-Item -LiteralPath (Join-Path $SourceRoot "docs\operator") -Destination (Join-Path $BundleRoot "docs\operator") -Recurse
Copy-Item -LiteralPath (Join-Path $SourceRoot "docs\security\Axon_v0.1.0_Security_Audit.md") -Destination (Join-Path $BundleRoot "docs\security")
$PdfGuideSource = Join-Path $SourceRoot "output\pdf"
if (-not (Test-Path -LiteralPath $PdfGuideSource -PathType Container)) {
    throw "PDF user guides are missing: $PdfGuideSource"
}
Get-ChildItem -LiteralPath $PdfGuideSource -Filter "*.pdf" -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $BundleRoot "docs\user-guides\$($_.Name)")
}
Copy-Item -LiteralPath $InputPath -Destination (Join-Path $BundleRoot "manifests\release-inputs.json")

foreach ($reference in @($synapseTagged, [string]$inputs.postgresImage, [string]$inputs.nginxImage)) {
    Invoke-Docker pull --platform linux/amd64 $reference
}
$synapseDigest = Get-RepoDigest -Reference $synapseTagged
$postgresDigest = Get-RepoDigest -Reference ([string]$inputs.postgresImage)
$nginxDigest = Get-RepoDigest -Reference ([string]$inputs.nginxImage)

$imageDirectory = Join-Path $BundleRoot "images"
$synapseBundle = Export-OfflineImage -SourceDigest $synapseDigest -Component "synapse" -DestinationDirectory $imageDirectory
$postgresBundle = Export-OfflineImage -SourceDigest $postgresDigest -Component "postgres" -DestinationDirectory $imageDirectory
$nginxBundle = Export-OfflineImage -SourceDigest $nginxDigest -Component "nginx" -DestinationDirectory $imageDirectory

& dotnet publish (Join-Path $SourceRoot "src\Axon.Control\Axon.Control.csproj") `
    -c Release -r win-x64 --self-contained true -o (Join-Path $BundleRoot "bin")
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $BundleRoot "bin\Axon.Control.exe"))) {
    throw "dotnet publish did not produce Axon.Control.exe."
}

$dockerInstaller = Join-Path $BundleRoot "installers\Docker Desktop Installer.exe"
Invoke-WebRequest -Uri $inputs.dockerDesktopWindowsAmd64 -OutFile $dockerInstaller
if ((Get-Item $dockerInstaller).Length -eq 0) { throw "Docker Desktop download was empty." }
$wslInstaller = Join-Path $BundleRoot ("installers\" + $wslAsset.name)
Invoke-WebRequest -Uri $wslAsset.browser_download_url -OutFile $wslInstaller
if ((Get-Item $wslInstaller).Length -eq 0) { throw "WSL MSI download was empty." }

[ordered]@{
    synapse = $synapseBundle
    postgres = $postgresBundle
    nginx = $nginxBundle
    upstream = [ordered]@{
        synapse = $synapseDigest
        postgres = $postgresDigest
        nginx = $nginxDigest
    }
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $BundleRoot "manifests\image-digests.json") -Encoding UTF8

[ordered]@{
    axon = $Version
    builtUtc = [DateTime]::UtcNow.ToString("o")
    synapse = [string]$synapseRelease.tag_name
    postgres = [string]$inputs.postgresImage
    nginx = [string]$inputs.nginxImage
    wsl = [string]$wslRelease.tag_name
    target = "win-x64"
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $BundleRoot "manifests\versions.json") -Encoding UTF8

@"
AXON v$Version - OFFLINE WINDOWS INSTALLER
==========================================

1. Copy this complete folder to a local NTFS drive on the Windows 11 host.
2. Double-click "Install Axon.cmd".
3. Approve the Windows administrator prompt.
4. Select the connected adapter and address when prompted.
5. If Windows requests a restart, restart and double-click "Install Axon.cmd" again.
6. When installation completes, use the "Axon Control" Desktop shortcut.

No internet connection is required on the target computer.
Do not run the installer from inside the ZIP, an SD card, or a network share.
Confirm that the operator is authorized to accept and use Docker Desktop under
Docker's current license before installation.
Hardware capacity warnings do not stop a normal installation. Use
-StrictPreflight only when enforcing the recommended sizing as a hard policy.

Start with:
  docs\user-guides\Axon_Windows_Setup_Guide.pdf
"@ | Set-Content -LiteralPath (Join-Path $BundleRoot "README_FIRST.txt") -Encoding ASCII

[ordered]@{
    synapseReleaseApi = [string]$inputs.synapseLatestReleaseApi
    dockerDesktop = [string]$inputs.dockerDesktopWindowsAmd64
    wslReleaseApi = [string]$inputs.wslLatestReleaseApi
    wslMsi = [string]$wslAsset.browser_download_url
    resolvedUtc = [DateTime]::UtcNow.ToString("o")
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $BundleRoot "manifests\sources.json") -Encoding UTF8

$checksumFile = Join-Path $BundleRoot "manifests\SHA256SUMS"
$checksumLines = Get-ChildItem -LiteralPath $BundleRoot -File -Recurse |
    Where-Object FullName -ne $checksumFile |
    Sort-Object FullName |
    ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($BundleRoot, $_.FullName).Replace('\', '/')
        "{0} *{1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relative
    }
$checksumLines | Set-Content -LiteralPath $checksumFile -Encoding ASCII

Compress-Archive -Path (Join-Path $BundleRoot "*") -DestinationPath $ZipPath -CompressionLevel Optimal
Write-Host "Expanded offline bundle: $BundleRoot"
Write-Host "Offline bundle ZIP: $ZipPath"
