#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$PcapPath,

    [Parameter(Mandatory)]
    [string]$BindIp,

    [int[]]$ComparisonUdpPort = @(),

    [string[]]$ComparisonMulticastAddress = @(),

    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$tshark = Get-Command tshark.exe -ErrorAction SilentlyContinue
if (-not $tshark) {
    $tshark = Get-Command tshark -ErrorAction SilentlyContinue
}
if (-not $tshark) {
    throw "TShark is required for packet-level summarization. Install Wireshark/TShark or analyze the PCAPNG on the connected analysis workstation."
}

$resolvedPcap = (Resolve-Path -LiteralPath $PcapPath).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Split-Path -Parent $resolvedPcap
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$packetCsv = Join-Path $OutputDirectory "packets.csv"
$summaryJson = Join-Path $OutputDirectory "packet-summary.json"
$conversationsText = Join-Path $OutputDirectory "tcp-conversations.txt"

& $tshark.Source -r $resolvedPcap -T fields `
    -E header=y -E separator=, -E quote=d -E occurrence=f `
    -e frame.time_epoch `
    -e frame.len `
    -e frame.cap_len `
    -e ip.src `
    -e tcp.srcport `
    -e ip.dst `
    -e tcp.dstport `
    -e tcp.len `
    -e tcp.analysis.retransmission `
    -e udp.srcport `
    -e udp.dstport `
    -e udp.length |
    Set-Content -LiteralPath $packetCsv -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw "TShark packet extraction failed." }

& $tshark.Source -r $resolvedPcap -q -z conv,tcp |
    Set-Content -LiteralPath $conversationsText -Encoding UTF8
if ($LASTEXITCODE -ne 0) { throw "TShark conversation analysis failed." }

$packets = @(Import-Csv -LiteralPath $packetCsv)
if ($packets.Count -eq 0) { throw "The capture contains no decoded IP/TCP packets." }

$lengths = @($packets |
    ForEach-Object { [int64]$_.'frame.len' } |
    Sort-Object)
$tcpPayload = [int64](($packets |
    Measure-Object -Property 'tcp.len' -Sum).Sum)
$inbound = @($packets | Where-Object { $_.'ip.dst' -eq $BindIp })
$outbound = @($packets | Where-Object { $_.'ip.src' -eq $BindIp })
$matrixPackets = @($packets | Where-Object {
    ($_.'ip.src' -eq $BindIp -or $_.'ip.dst' -eq $BindIp) -and
    ($_.'tcp.srcport' -eq "80" -or $_.'tcp.dstport' -eq "80")
})
$comparisonPortStrings = @($ComparisonUdpPort | ForEach-Object { $_.ToString() })
$comparisonPackets = @($packets | Where-Object {
    ($comparisonPortStrings.Count -gt 0 -and (
        $comparisonPortStrings -contains $_.'udp.srcport' -or
        $comparisonPortStrings -contains $_.'udp.dstport'
    )) -or
    ($ComparisonMulticastAddress.Count -gt 0 -and (
        $ComparisonMulticastAddress -contains $_.'ip.src' -or
        $ComparisonMulticastAddress -contains $_.'ip.dst'
    ))
})
$remoteAddresses = @($packets |
    ForEach-Object {
        if ($_.'ip.src' -eq $BindIp) { $_.'ip.dst' }
        elseif ($_.'ip.dst' -eq $BindIp) { $_.'ip.src' }
    } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Sort-Object -Unique)

function Get-Percentile {
    param(
        [Parameter(Mandatory)][array]$Values,
        [Parameter(Mandatory)][double]$Percent
    )

    if ($Values.Count -eq 0) { return 0 }
    $index = [Math]::Ceiling(($Percent / 100.0) * $Values.Count) - 1
    return [int64]$Values[[Math]::Max(0, [Math]::Min($Values.Count - 1, $index))]
}

$firstTime = [double]$packets[0].'frame.time_epoch'
$lastTime = [double]$packets[$packets.Count - 1].'frame.time_epoch'
$duration = [Math]::Max(0.001, $lastTime - $firstTime)
$wireBytes = [int64](($packets | Measure-Object -Property 'frame.len' -Sum).Sum)
$inboundBytes = [int64](($inbound | Measure-Object -Property 'frame.len' -Sum).Sum)
$outboundBytes = [int64](($outbound | Measure-Object -Property 'frame.len' -Sum).Sum)
$retransmissions = @($packets |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.'tcp.analysis.retransmission') }).Count
$matrixWireBytes = [int64](($matrixPackets |
    Measure-Object -Property 'frame.len' -Sum).Sum)
$comparisonWireBytes = [int64](($comparisonPackets |
    Measure-Object -Property 'frame.len' -Sum).Sum)

$summary = [ordered]@{
    schemaVersion = 2
    pcap = $resolvedPcap
    bindIp = $BindIp
    measuredSeconds = [Math]::Round($duration, 6)
    frames = $packets.Count
    wireBytes = $wireBytes
    inboundWireBytes = $inboundBytes
    outboundWireBytes = $outboundBytes
    tcpPayloadBytes = $tcpPayload
    averageWireBitsPerSecond = [Math]::Round(($wireBytes * 8) / $duration, 2)
    trafficClasses = [ordered]@{
        matrix = [ordered]@{
            frames = $matrixPackets.Count
            wireBytes = $matrixWireBytes
            averageBitsPerSecond = [Math]::Round(($matrixWireBytes * 8) / $duration, 2)
            selector = "$BindIp TCP/80"
        }
        comparison = [ordered]@{
            frames = $comparisonPackets.Count
            wireBytes = $comparisonWireBytes
            averageBitsPerSecond = [Math]::Round(($comparisonWireBytes * 8) / $duration, 2)
            udpPorts = $ComparisonUdpPort
            multicastAddresses = $ComparisonMulticastAddress
        }
        other = [ordered]@{
            frames = $packets.Count - $matrixPackets.Count - $comparisonPackets.Count
            wireBytes = $wireBytes - $matrixWireBytes - $comparisonWireBytes
        }
    }
    packetSizeBytes = [ordered]@{
        minimum = [int64]$lengths[0]
        p50 = Get-Percentile -Values $lengths -Percent 50
        p95 = Get-Percentile -Values $lengths -Percent 95
        maximum = [int64]$lengths[$lengths.Count - 1]
    }
    tcpRetransmissionFrames = $retransmissions
    remoteAddresses = $remoteAddresses
    notes = @(
        "Wire bytes are Ethernet/IP capture values observed at the selected capture interface.",
        "Pktmon must capture NIC components only to avoid duplicate snapshots of one packet.",
        "Raw PCAP and packet CSV data are sensitive because Axon currently uses HTTP."
    )
}
$summary | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $summaryJson -Encoding UTF8
$summary | ConvertTo-Json -Depth 5
