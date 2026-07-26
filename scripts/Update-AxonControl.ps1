#Requires -Version 5.1
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-LoopbackPort {
    param([int]$Port)

    $client = New-Object Net.Sockets.TcpClient
    try {
        $wait = $client.BeginConnect("127.0.0.1", $Port, $null, $null)
        if (-not $wait.AsyncWaitHandle.WaitOne(500)) { return $false }
        $client.EndConnect($wait)
        return $true
    } catch {
        return $false
    } finally {
        $client.Dispose()
    }
}

function Wait-LoopbackPort {
    param(
        [int]$Port,
        [bool]$ExpectedOpen,
        [int]$Seconds = 30
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if ((Test-LoopbackPort -Port $Port) -eq $ExpectedOpen) { return $true }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}

function Copy-Payload {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

if (-not (Test-IsAdministrator)) {
    $elevatedArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    $elevated = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList $elevatedArgs
    exit $elevated.ExitCode
}

$PackageRoot = Split-Path -Parent $PSScriptRoot
$PayloadRoot = Join-Path $PackageRoot "payload"
$ManifestPath = Join-Path $PackageRoot "manifest.json"
if (-not (Test-Path -LiteralPath $PayloadRoot -PathType Container)) {
    throw "The Axon Control payload directory is missing."
}
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "The Axon Control checksum manifest is missing."
}

$manifestDocument = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ([int]$manifestDocument.schemaVersion -ne 1) {
    throw "The Axon Control checksum manifest version is unsupported."
}
$manifestFiles = $manifestDocument.files
if ($null -eq $manifestFiles -or @($manifestFiles).Count -eq 0) {
    throw "The Axon Control checksum manifest is empty."
}
foreach ($entry in $manifestFiles) {
    $payloadFile = Join-Path $PayloadRoot ([string]$entry.path)
    if (-not (Test-Path -LiteralPath $payloadFile -PathType Leaf)) {
        throw "Payload file is missing: $($entry.path)"
    }
    $actual = (Get-FileHash -LiteralPath $payloadFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne ([string]$entry.sha256).ToLowerInvariant()) {
        throw "Payload checksum failed: $($entry.path)"
    }
}

$taskName = "Axon Control Panel"
$task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if (-not $task) {
    throw "The '$taskName' scheduled task was not found. Install Axon before applying this update."
}

$actions = @($task.Actions)
if ($actions.Count -ne 1) {
    throw "The '$taskName' task must contain exactly one action."
}
$TargetExe = [Environment]::ExpandEnvironmentVariables(([string]$actions[0].Execute).Trim('"'))
if ([IO.Path]::GetFileName($TargetExe) -ne "Axon.Control.exe") {
    throw "The scheduled task does not point to Axon.Control.exe."
}
$TargetRoot = Split-Path -Parent $TargetExe
if (-not (Test-Path -LiteralPath $TargetExe -PathType Leaf)) {
    throw "The installed Axon Control executable is missing: $TargetExe"
}
if ([IO.Path]::GetFullPath($TargetRoot).TrimEnd('\') -eq [IO.Path]::GetFullPath($PayloadRoot).TrimEnd('\')) {
    throw "Extract the update ZIP outside the installed Axon Control directory."
}

$BackupRoot = Join-Path $env:ProgramData "Axon\control-backups"
$BackupPath = Join-Path $BackupRoot ([DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $BackupPath | Out-Null

Write-Host "Stopping Axon Control..."
Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if (-not (Wait-LoopbackPort -Port 8780 -ExpectedOpen $false -Seconds 20)) {
    throw "TCP 8780 is still in use. Close the existing Axon Control process and run the updater again."
}

Write-Host "Backing up the installed control panel to $BackupPath"
Copy-Payload -Source $TargetRoot -Destination $BackupPath

try {
    Write-Host "Installing the updated control panel..."
    Copy-Payload -Source $PayloadRoot -Destination $TargetRoot

    foreach ($entry in $manifestFiles) {
        $installedFile = Join-Path $TargetRoot ([string]$entry.path)
        $actual = (Get-FileHash -LiteralPath $installedFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne ([string]$entry.sha256).ToLowerInvariant()) {
            throw "Installed checksum failed: $($entry.path)"
        }
    }

    Start-ScheduledTask -TaskName $taskName
    if (-not (Wait-LoopbackPort -Port 8780 -ExpectedOpen $true -Seconds 40)) {
        throw "The updated Axon Control panel did not start on TCP 8780."
    }
} catch {
    Write-Warning "Update failed. Restoring the previous control panel."
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    Wait-LoopbackPort -Port 8780 -ExpectedOpen $false -Seconds 10 | Out-Null
    Copy-Payload -Source $BackupPath -Destination $TargetRoot
    Start-ScheduledTask -TaskName $taskName
    throw
}

Write-Host "Axon Control update completed successfully."
Write-Host "Backup retained at: $BackupPath"
Start-Process "http://127.0.0.1:8780"
