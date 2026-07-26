#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({
        try { [void][Net.IPAddress]::Parse($_); $true }
        catch { $false }
    })]
    [string]$BindIp,

    [Parameter(Mandatory)]
    [ValidateSet(
        "idle-one-client",
        "idle-two-clients",
        "login",
        "room-create",
        "single-message",
        "steady-messaging",
        "offline-send",
        "reconnect",
        "custom")]
    [string]$Scenario,

    [ValidateRange(15, 3600)]
    [int]$DurationSeconds = 120,

    [ValidateRange(128, 65535)]
    [int]$PacketBytes = 256,

    [ValidateRange(64, 4096)]
    [int]$MaxCaptureMB = 512,

    [ValidateRange(1, 60)]
    [int]$ResourceSampleSeconds = 5,

    [string]$InterfaceAlias,

    [string]$BundleRoot,

    [string]$OutputRoot = (Join-Path $env:ProgramData "Axon\network-audits")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-AxonConnections {
    Get-NetTCPConnection -ErrorAction SilentlyContinue |
        Where-Object { $_.LocalPort -eq 80 -or $_.RemotePort -eq 80 } |
        Select-Object LocalAddress, LocalPort, RemoteAddress, RemotePort, State,
            OwningProcess, CreationTime
}

function Get-AdapterSnapshot {
    param([Parameter(Mandatory)][string]$Alias)

    $stats = Get-NetAdapterStatistics -Name $Alias
    [ordered]@{
        capturedUtc = [DateTime]::UtcNow.ToString("o")
        receivedBytes = [uint64]$stats.ReceivedBytes
        sentBytes = [uint64]$stats.SentBytes
        receivedUnicastPackets = [uint64]$stats.ReceivedUnicastPackets
        sentUnicastPackets = [uint64]$stats.SentUnicastPackets
        receivedDiscardedPackets = [uint64]$stats.ReceivedDiscardedPackets
        outboundDiscardedPackets = [uint64]$stats.OutboundDiscardedPackets
        receivedPacketErrors = [uint64]$stats.ReceivedPacketErrors
        outboundPacketErrors = [uint64]$stats.OutboundPacketErrors
    }
}

if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    $cursor = $PSScriptRoot
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
    throw "Axon bundle root was not found. Pass -BundleRoot with the original Axon installation folder."
}
$BundleRoot = [IO.Path]::GetFullPath($BundleRoot)
$DataRoot = Join-Path $env:ProgramData "Axon"
$ComposeFile = Join-Path $BundleRoot "deploy\compose.yaml"
$EnvironmentFile = Join-Path $DataRoot ".env"

if (-not (Get-Command pktmon.exe -ErrorAction SilentlyContinue)) {
    throw "Windows Packet Monitor (pktmon.exe) is unavailable."
}
if (-not (Get-Command docker.exe -ErrorAction SilentlyContinue)) {
    throw "docker.exe is unavailable. Start Docker Desktop and retry."
}
if (-not (Test-Path -LiteralPath $ComposeFile -PathType Leaf)) {
    throw "Axon Compose file is missing: $ComposeFile"
}
if (-not (Test-Path -LiteralPath $EnvironmentFile -PathType Leaf)) {
    throw "Axon runtime environment is missing: $EnvironmentFile"
}

$addressMatches = @(Get-NetIPAddress -AddressFamily IPv4 -IPAddress $BindIp -ErrorAction SilentlyContinue)
if ($addressMatches.Count -ne 1) {
    throw "BindIp $BindIp must exist on exactly one Windows interface."
}
$detectedAlias = [string]$addressMatches[0].InterfaceAlias
if ([string]::IsNullOrWhiteSpace($InterfaceAlias)) {
    $InterfaceAlias = $detectedAlias
} elseif ($InterfaceAlias -ne $detectedAlias) {
    throw "BindIp $BindIp belongs to '$detectedAlias', not '$InterfaceAlias'."
}

$adapter = Get-NetAdapter -Name $InterfaceAlias -ErrorAction Stop
if ($adapter.Status -ne "Up") {
    throw "Interface '$InterfaceAlias' is not Up."
}

$pktmonStatus = (& pktmon status 2>&1 | Out-String)
if ($pktmonStatus -match "(?im)Running") {
    throw "Pktmon is already running. Stop the existing capture before starting an Axon audit."
}

$stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$safeScenario = $Scenario -replace "[^a-z0-9-]", "-"
$RunRoot = Join-Path $OutputRoot "$stamp-$safeScenario"
New-Item -ItemType Directory -Force -Path $RunRoot | Out-Null

$etlPath = Join-Path $RunRoot "axon-traffic.etl"
$pcapPath = Join-Path $RunRoot "axon-traffic.pcapng"
$resourcePath = Join-Path $RunRoot "docker-resources.csv"
$beforePath = Join-Path $RunRoot "adapter-before.json"
$afterPath = Join-Path $RunRoot "adapter-after.json"
$summaryPath = Join-Path $RunRoot "capture-summary.json"

$metadata = [ordered]@{
    schemaVersion = 1
    capturedUtc = [DateTime]::UtcNow.ToString("o")
    scenario = $Scenario
    requestedDurationSeconds = $DurationSeconds
    bindIp = $BindIp
    interfaceAlias = $InterfaceAlias
    interfaceDescription = [string]$adapter.InterfaceDescription
    linkSpeed = [string]$adapter.LinkSpeed
    packetBytesCaptured = $PacketBytes
    packetPayloadWarning = "PCAP data can expose HTTP credentials, access tokens, metadata, and packet contents. Do not share raw captures."
    host = [Environment]::MachineName
    windows = [Environment]::OSVersion.VersionString
}
$metadata | ConvertTo-Json -Depth 4 |
    Set-Content -LiteralPath (Join-Path $RunRoot "metadata.json") -Encoding UTF8

Get-NetIPConfiguration -InterfaceAlias $InterfaceAlias |
    Format-List * |
    Out-File -LiteralPath (Join-Path $RunRoot "ip-configuration.txt") -Encoding UTF8
Get-NetRoute -InterfaceAlias $InterfaceAlias |
    Sort-Object DestinationPrefix, RouteMetric |
    Format-Table -AutoSize |
    Out-File -LiteralPath (Join-Path $RunRoot "routes.txt") -Encoding UTF8
Get-AxonConnections |
    Export-Csv -LiteralPath (Join-Path $RunRoot "connections-before.csv") -NoTypeInformation
& docker compose --project-name axon --env-file $EnvironmentFile `
    --file $ComposeFile ps --format json 2>&1 |
    Out-File -LiteralPath (Join-Path $RunRoot "docker-ps-before.jsonl") -Encoding UTF8
& docker version 2>&1 |
    Out-File -LiteralPath (Join-Path $RunRoot "docker-version.txt") -Encoding UTF8

$adapterBefore = Get-AdapterSnapshot -Alias $InterfaceAlias
$adapterBefore | ConvertTo-Json |
    Set-Content -LiteralPath $beforePath -Encoding UTF8

$resourceRows = New-Object Collections.Generic.List[object]
$captureStarted = $false
$startedUtc = [DateTime]::UtcNow

try {
    Invoke-Checked pktmon.exe filter remove | Out-Null
    Invoke-Checked pktmon.exe filter add AxonMatrix -i $BindIp -p 80 -t TCP | Out-Null
    Invoke-Checked pktmon.exe start --capture --comp nics --type flow `
        --pkt-size $PacketBytes --file-name $etlPath --file-size $MaxCaptureMB `
        --log-mode circular | Out-Null
    $captureStarted = $true
    $startedUtc = [DateTime]::UtcNow
    $deadline = $startedUtc.AddSeconds($DurationSeconds)

    Write-Host "Axon traffic audit started."
    Write-Host "Scenario: $Scenario"
    Write-Host "Interface: $InterfaceAlias ($BindIp)"
    Write-Host "Perform only the named test until the timer finishes."

    do {
        $sampleUtc = [DateTime]::UtcNow.ToString("o")
        $statsLines = @(& docker stats --no-stream --format "{{json .}}" `
            axon-postgres-1 axon-synapse-1 axon-gateway-1 2>$null)
        foreach ($line in $statsLines) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                $stat = $line | ConvertFrom-Json
                $resourceRows.Add([pscustomobject]@{
                    capturedUtc = $sampleUtc
                    container = [string]$stat.Name
                    cpu = [string]$stat.CPUPerc
                    memory = [string]$stat.MemUsage
                    netIo = [string]$stat.NetIO
                    blockIo = [string]$stat.BlockIO
                    pids = [string]$stat.PIDs
                })
            } catch {
                Write-Warning "A Docker resource sample could not be parsed."
            }
        }

        $remaining = [int][Math]::Ceiling(($deadline - [DateTime]::UtcNow).TotalSeconds)
        if ($remaining -gt 0) {
            Start-Sleep -Seconds ([Math]::Min($ResourceSampleSeconds, $remaining))
        }
    } while ([DateTime]::UtcNow -lt $deadline)

    & pktmon counters --json 2>&1 |
        Out-File -LiteralPath (Join-Path $RunRoot "pktmon-counters.json") -Encoding UTF8
} finally {
    if ($captureStarted) {
        & pktmon stop 2>&1 |
            Out-File -LiteralPath (Join-Path $RunRoot "pktmon-stop.txt") -Encoding UTF8
    }
    & pktmon filter remove 2>&1 |
        Out-File -LiteralPath (Join-Path $RunRoot "pktmon-filter-cleanup.txt") -Encoding UTF8
}

$endedUtc = [DateTime]::UtcNow
$resourceRows | Export-Csv -LiteralPath $resourcePath -NoTypeInformation

if (-not (Test-Path -LiteralPath $etlPath -PathType Leaf)) {
    throw "Pktmon did not create the ETL capture."
}
Invoke-Checked pktmon.exe etl2pcap $etlPath --out $pcapPath | Out-Null

$adapterAfter = Get-AdapterSnapshot -Alias $InterfaceAlias
$adapterAfter | ConvertTo-Json |
    Set-Content -LiteralPath $afterPath -Encoding UTF8
Get-AxonConnections |
    Export-Csv -LiteralPath (Join-Path $RunRoot "connections-after.csv") -NoTypeInformation
& docker compose --project-name axon --env-file $EnvironmentFile `
    --file $ComposeFile ps --format json 2>&1 |
    Out-File -LiteralPath (Join-Path $RunRoot "docker-ps-after.jsonl") -Encoding UTF8
& docker logs --since $startedUtc.ToString("o") --timestamps axon-synapse-1 2>&1 |
    Select-String -SimpleMatch "Processed request:" |
    ForEach-Object { $_.Line } |
    Set-Content -LiteralPath (Join-Path $RunRoot "synapse-processed-requests.log") -Encoding UTF8

$elapsed = [Math]::Max(0.001, ($endedUtc - $startedUtc).TotalSeconds)
$receivedDelta = [uint64]$adapterAfter.receivedBytes - [uint64]$adapterBefore.receivedBytes
$sentDelta = [uint64]$adapterAfter.sentBytes - [uint64]$adapterBefore.sentBytes
$summary = [ordered]@{
    schemaVersion = 1
    scenario = $Scenario
    startedUtc = $startedUtc.ToString("o")
    endedUtc = $endedUtc.ToString("o")
    measuredSeconds = [Math]::Round($elapsed, 3)
    interfaceAlias = $InterfaceAlias
    bindIp = $BindIp
    adapterAllTraffic = [ordered]@{
        receivedBytes = $receivedDelta
        sentBytes = $sentDelta
        totalBytes = $receivedDelta + $sentDelta
        averageBitsPerSecond = [Math]::Round((($receivedDelta + $sentDelta) * 8) / $elapsed, 2)
        warning = "Adapter counters include all traffic on this NIC. Use the filtered PCAP for Axon-only totals."
    }
    capture = [ordered]@{
        etl = $etlPath
        pcapng = $pcapPath
        pcapngFileBytes = (Get-Item -LiteralPath $pcapPath).Length
        packetBytesCaptured = $PacketBytes
    }
}
$summary | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ""
Write-Host "Axon traffic audit complete:"
Write-Host $RunRoot
Write-Host "Raw packet capture is sensitive. Do not send it without review."
