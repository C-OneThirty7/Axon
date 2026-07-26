#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet(
        "Menu",
        "Start",
        "Stop",
        "Restart",
        "Status",
        "StartControl",
        "StopControl",
        "OpenControl")]
    [string]$Action = "Menu",

    [string]$BundleRoot,

    [string]$DataRoot = (Join-Path $env:ProgramData "Axon")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        throw "Axon could not determine its installation folder."
    }
    $BundleRoot = Split-Path -Parent $PSScriptRoot
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Elevated {
    $arguments = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`"",
        "-Action", $Action,
        "-BundleRoot", "`"$BundleRoot`"",
        "-DataRoot", "`"$DataRoot`""
    ) -join " "
    $process = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList $arguments
    exit $process.ExitCode
}

function Start-DockerDesktopInteractive {
    & docker info *> $null
    if ($LASTEXITCODE -eq 0) { return }

    $runningDesktop = Get-Process -Name "Docker Desktop" -ErrorAction SilentlyContinue
    if ($runningDesktop) { return }

    $desktop = Join-Path $env:ProgramFiles "Docker\Docker\Docker Desktop.exe"
    if (Test-Path -LiteralPath $desktop -PathType Leaf) {
        Start-Process -FilePath $desktop | Out-Null
    }
}

if (-not (Test-IsAdministrator)) {
    if ($Action -in @("Menu", "Start", "Restart")) {
        Start-DockerDesktopInteractive
    }
    Invoke-Elevated
}

$BundleRoot = [IO.Path]::GetFullPath($BundleRoot)
$DataRoot = [IO.Path]::GetFullPath($DataRoot)
$ComposeFile = Join-Path $BundleRoot "deploy\compose.yaml"
$EnvironmentFile = Join-Path $DataRoot ".env"
$TaskName = "Axon Control Panel"
$ControlUrl = "http://127.0.0.1:8780"

foreach ($required in @($ComposeFile, $EnvironmentFile)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required Axon file is missing: $required"
    }
}

function Invoke-Compose {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)

    & docker compose --project-name axon --env-file $EnvironmentFile `
        --file $ComposeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed."
    }
}

function Get-AxonBindIp {
    $line = Get-Content -LiteralPath $EnvironmentFile |
        Where-Object { $_ -match "^AXON_BIND_IP=" } |
        Select-Object -First 1
    if (-not $line) { throw "AXON_BIND_IP is missing from $EnvironmentFile." }
    return ($line -split "=", 2)[1].Trim()
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

function Wait-ControlPanel {
    param([int]$Seconds = 60)

    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        if (Test-LoopbackPort -Port 8780) { return $true }
        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}

function Wait-DockerEngine {
    param([int]$Seconds = 180)

    & docker info *> $null
    if ($LASTEXITCODE -eq 0) { return $true }

    $runningDesktop = Get-Process -Name "Docker Desktop" -ErrorAction SilentlyContinue
    $desktop = Join-Path $env:ProgramFiles "Docker\Docker\Docker Desktop.exe"
    if (-not $runningDesktop -and (Test-Path -LiteralPath $desktop -PathType Leaf)) {
        Write-Host "Starting Docker Desktop..."
        Start-Process -FilePath $desktop | Out-Null
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        Start-Sleep -Seconds 3
        & docker info *> $null
        if ($LASTEXITCODE -eq 0) { return $true }
    } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}

function Start-ControlPanel {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if (-not $task) {
        throw "The '$TaskName' scheduled task is missing. Run Repair-Axon.ps1."
    }
    if (-not (Test-LoopbackPort -Port 8780)) {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        Start-ScheduledTask -TaskName $TaskName
    }
    if (-not (Wait-ControlPanel)) {
        throw "Axon Control did not start on loopback TCP 8780."
    }
}

function Start-AxonServices {
    if (-not (Wait-DockerEngine)) {
        throw "Docker Desktop did not become ready within three minutes."
    }
    Write-Host "Starting PostgreSQL, Synapse, and nginx..."
    Invoke-Compose up --detach --wait
    Start-ControlPanel
    Write-Host "Axon services and control panel are ready."
    Start-Process $ControlUrl
}

function Stop-AxonServices {
    if (-not (Wait-DockerEngine -Seconds 30)) {
        Write-Host "Docker is not running; Axon messaging services are already unavailable."
        Start-ControlPanel
        return
    }
    Write-Host "Stopping PostgreSQL, Synapse, and nginx..."
    Invoke-Compose stop
    Start-ControlPanel
    Write-Host "Messaging services stopped. Axon Control remains available."
}

function Restart-AxonServices {
    if (-not (Wait-DockerEngine)) {
        throw "Docker Desktop did not become ready within three minutes."
    }
    Write-Host "Restarting PostgreSQL, Synapse, and nginx..."
    Invoke-Compose restart
    Invoke-Compose up --detach --wait
    Start-ControlPanel
    Write-Host "Axon services restarted and healthy."
}

function Show-AxonStatus {
    $bindIp = Get-AxonBindIp
    Write-Host ""
    Write-Host "Axon bind IP: $bindIp"
    Write-Host "Control URL: $ControlUrl"
    Write-Host "Control panel: $(if (Test-LoopbackPort -Port 8780) { 'listening' } else { 'stopped' })"

    & docker info *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Docker engine: stopped or unavailable"
        return
    }
    Write-Host "Docker engine: ready"
    Invoke-Compose ps

    $matrix = Test-NetConnection -ComputerName $bindIp -Port 80 -WarningAction SilentlyContinue
    Write-Host "Matrix TCP 80: $(if ($matrix.TcpTestSucceeded) { 'reachable' } else { 'unreachable' })"
}

function Stop-ControlPanel {
    $confirmation = Read-Host "Type STOP CONTROL to stop the host-only admin panel"
    if ($confirmation -cne "STOP CONTROL") {
        Write-Host "Control-panel stop cancelled."
        return
    }
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Write-Host "Axon Control stopped. Matrix services were not changed."
}

function Open-ControlPanel {
    Start-ControlPanel
    Start-Process $ControlUrl
}

function Invoke-Action {
    param([Parameter(Mandatory)][string]$SelectedAction)

    switch ($SelectedAction) {
        "Start" { Start-AxonServices }
        "Stop" { Stop-AxonServices }
        "Restart" { Restart-AxonServices }
        "Status" { Show-AxonStatus }
        "StartControl" { Start-ControlPanel; Write-Host "Axon Control is ready." }
        "StopControl" { Stop-ControlPanel }
        "OpenControl" { Open-ControlPanel }
        default { throw "Unknown Axon operation." }
    }
}

if ($Action -ne "Menu") {
    Invoke-Action -SelectedAction $Action
    exit 0
}

do {
    Clear-Host
    Write-Host "AXON OPERATIONS"
    Write-Host ""
    Write-Host "1  Start Axon services and control panel"
    Write-Host "2  Stop messaging services (control panel stays up)"
    Write-Host "3  Restart messaging services"
    Write-Host "4  Show status"
    Write-Host "5  Start control panel only"
    Write-Host "6  Open control panel"
    Write-Host "7  Stop control panel"
    Write-Host "Q  Quit"
    Write-Host ""
    $selection = (Read-Host "Select an operation").Trim().ToUpperInvariant()

    try {
        switch ($selection) {
            "1" { Invoke-Action -SelectedAction "Start" }
            "2" { Invoke-Action -SelectedAction "Stop" }
            "3" { Invoke-Action -SelectedAction "Restart" }
            "4" { Invoke-Action -SelectedAction "Status" }
            "5" { Invoke-Action -SelectedAction "StartControl" }
            "6" { Invoke-Action -SelectedAction "OpenControl" }
            "7" { Invoke-Action -SelectedAction "StopControl" }
            "Q" { break }
            default { Write-Host "Unknown selection." }
        }
    } catch {
        Write-Host ""
        Write-Host "Operation failed: $($_.Exception.Message)" -ForegroundColor Red
    }

    if ($selection -ne "Q") {
        Write-Host ""
        Read-Host "Press Enter to return to the menu" | Out-Null
    }
} while ($true)
