#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][string]$SignaturePath,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$CurrentProcessId,
    [Parameter(Mandatory)][string]$DataRoot,
    [Parameter(Mandatory)][string]$CurrentBundleRoot,
    [Parameter(Mandatory)][string]$VerifierPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$DataRoot = [IO.Path]::GetFullPath($DataRoot)
$CurrentBundleRoot = [IO.Path]::GetFullPath($CurrentBundleRoot)
$ArchivePath = [IO.Path]::GetFullPath($ArchivePath)
$SignaturePath = [IO.Path]::GetFullPath($SignaturePath)
$VerifierPath = [IO.Path]::GetFullPath($VerifierPath)
$UpdateRoot = [IO.Path]::GetFullPath((Join-Path $DataRoot "updates"))
$UpdatePrefix = $UpdateRoot.TrimEnd('\') + '\'
$ExpectedName = "Axon-v$Version-offline-win-x64.zip"
$ExpectedSignatureName = "$ExpectedName.sig"
$StatePath = Join-Path $UpdateRoot "status.json"
$LogPath = Join-Path $UpdateRoot "update-v$Version.log"
$EnvironmentPath = Join-Path $DataRoot ".env"
$EnvironmentBackup = Join-Path $UpdateRoot ".env-v$Version.backup"
$InstallStatePath = Join-Path $DataRoot "install-state.json"
$InstallStateBackup = Join-Path $UpdateRoot "install-state-v$Version.backup.json"
$TaskName = "Axon Control Panel"
$TaskBackupPath = Join-Path $UpdateRoot "scheduled-task-v$Version.backup.xml"

function Write-UpdateState {
    param(
        [Parameter(Mandatory)][string]$State,
        [Parameter(Mandatory)][string]$Message
    )
    New-Item -ItemType Directory -Force -Path $UpdateRoot | Out-Null
    [pscustomobject]@{
        state = $State
        message = $Message
        version = $Version
        updatedAt = [DateTimeOffset]::UtcNow.ToString("o")
    } | ConvertTo-Json | Set-Content -LiteralPath $StatePath -Encoding UTF8
}

function Assert-ZipEntriesSafe {
    param([Parameter(Mandatory)][string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $extractionRoot = [IO.Path]::GetFullPath((Join-Path $UpdateRoot "zip-safety-root")).TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        foreach ($entry in $zip.Entries) {
            $candidate = [IO.Path]::GetFullPath((Join-Path $extractionRoot $entry.FullName))
            if (-not $candidate.StartsWith($extractionRoot, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Update archive contains an unsafe path: $($entry.FullName)"
            }
        }
    } finally {
        $zip.Dispose()
    }
}

function Restore-PreviousRuntime {
    if (Test-Path -LiteralPath $EnvironmentBackup -PathType Leaf) {
        Copy-Item -LiteralPath $EnvironmentBackup -Destination $EnvironmentPath -Force
    }
    if (Test-Path -LiteralPath $InstallStateBackup -PathType Leaf) {
        Copy-Item -LiteralPath $InstallStateBackup -Destination $InstallStatePath -Force
    }
    $oldCompose = Join-Path $CurrentBundleRoot "deploy\compose.yaml"
    if ((Test-Path -LiteralPath $oldCompose -PathType Leaf) -and
        (Test-Path -LiteralPath $EnvironmentPath -PathType Leaf)) {
        & docker compose --project-name axon --env-file $EnvironmentPath --file $oldCompose up --detach --wait
    }
    if (Test-Path -LiteralPath $TaskBackupPath -PathType Leaf) {
        Register-ScheduledTask `
            -TaskName $TaskName `
            -Xml (Get-Content -LiteralPath $TaskBackupPath -Raw) `
            -Force | Out-Null
    }
    Start-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
}

try {
    Start-Transcript -LiteralPath $LogPath -Append | Out-Null
    if (-not $ArchivePath.StartsWith($UpdatePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($ArchivePath) -cne $ExpectedName) {
        throw "The update archive is outside Axon's protected update directory."
    }
    if (-not $SignaturePath.StartsWith($UpdatePrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($SignaturePath) -cne $ExpectedSignatureName -or
        -not (Test-Path -LiteralPath $SignaturePath -PathType Leaf)) {
        throw "The update signature is outside Axon's protected update directory."
    }
    if ([IO.Path]::GetFileName($VerifierPath) -cne "Axon.Control.exe" -or
        -not (Test-Path -LiteralPath $VerifierPath -PathType Leaf)) {
        throw "The installed Axon release verifier is unavailable."
    }
    if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
        throw "The verified update archive is missing."
    }

    Write-UpdateState -State "installing" -Message "Waiting for Axon Control to hand off the update."
    Export-ScheduledTask -TaskName $TaskName |
        Set-Content -LiteralPath $TaskBackupPath -Encoding Unicode
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    while ([DateTime]::UtcNow -lt $deadline -and
           (Get-Process -Id $CurrentProcessId -ErrorAction SilentlyContinue)) {
        Start-Sleep -Seconds 1
    }
    if (Get-Process -Id $CurrentProcessId -ErrorAction SilentlyContinue) {
        throw "Axon Control did not stop for the update handoff."
    }
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue

    $actualHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne $ExpectedSha256.ToLowerInvariant()) {
        throw "The update archive failed its final SHA-256 verification."
    }
    & $VerifierPath verify-update $ArchivePath $SignaturePath
    if ($LASTEXITCODE -ne 0) {
        throw "The update archive failed Axon's release-signature verification."
    }
    Assert-ZipEntriesSafe -Path $ArchivePath

    $ReleaseParent = Join-Path $DataRoot "releases"
    $ExtractionRoot = Join-Path $ReleaseParent "v$Version"
    New-Item -ItemType Directory -Force -Path $ReleaseParent | Out-Null
    if (Test-Path -LiteralPath $ExtractionRoot) {
        Remove-Item -LiteralPath $ExtractionRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $ExtractionRoot | Out-Null
    Write-UpdateState -State "installing" -Message "Extracting and validating Axon $Version."
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $ExtractionRoot

    $BundleRoot = Join-Path $ExtractionRoot "Axon-v$Version-offline-win-x64"
    $BundleTest = Join-Path $BundleRoot "scripts\Test-AxonBundle.ps1"
    $Installer = Join-Path $BundleRoot "scripts\Install-Axon.ps1"
    foreach ($required in @($BundleTest, $Installer)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "The update archive is missing required file: $required"
        }
    }
    & $BundleTest
    if ($LASTEXITCODE -ne 0) {
        throw "The extracted Axon bundle failed its internal checksum validation."
    }

    if (-not (Test-Path -LiteralPath $InstallStatePath -PathType Leaf)) {
        throw "Axon's installation state is missing."
    }
    $InstallState = Get-Content -LiteralPath $InstallStatePath -Raw | ConvertFrom-Json
    Copy-Item -LiteralPath $InstallStatePath -Destination $InstallStateBackup -Force
    if (Test-Path -LiteralPath $EnvironmentPath -PathType Leaf) {
        Copy-Item -LiteralPath $EnvironmentPath -Destination $EnvironmentBackup -Force
    }
    $AllowedAddresses = @($InstallState.allowedRemoteAddress)

    Write-UpdateState -State "installing" -Message "Applying Axon $Version and restarting services."
    & $Installer `
        -DataRoot $DataRoot `
        -BindIp ([string]$InstallState.bindIp) `
        -InterfaceAlias ([string]$InstallState.interfaceAlias) `
        -AllowedRemoteAddress $AllowedAddresses `
        -ChecksumMode Strict `
        -NicMode Preserve `
        -Upgrade `
        -SkipInitialUser
    if ($LASTEXITCODE -ne 0) {
        throw "Axon installer exited with code $LASTEXITCODE."
    }

    Write-UpdateState -State "succeeded" -Message "Axon $Version installed successfully."
} catch {
    $failure = "Axon $Version update failed: $($_.Exception.Message)"
    Write-UpdateState -State "failed" -Message $failure
    try { Restore-PreviousRuntime } catch {
        Add-Content -LiteralPath $LogPath -Value "Recovery warning: $($_.Exception.Message)"
    }
} finally {
    try { Stop-Transcript | Out-Null } catch {}
}
