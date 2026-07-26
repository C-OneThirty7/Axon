[CmdletBinding()]
param(
    [string]$DataRoot = (Join-Path $env:ProgramData "Axon"),
    [string]$BindIp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$BundleRoot = Split-Path -Parent $PSScriptRoot
$ComposeFile = Join-Path $BundleRoot "deploy\compose.yaml"
$EnvFile = Join-Path $DataRoot ".env"
$HomeserverFile = Join-Path $DataRoot "runtime\synapse\homeserver.yaml"
$NginxFile = Join-Path $DataRoot "runtime\nginx\default.conf"

if ([string]::IsNullOrWhiteSpace($BindIp) -and (Test-Path -LiteralPath $EnvFile -PathType Leaf)) {
    $savedEnvironment = ConvertFrom-StringData (Get-Content -LiteralPath $EnvFile -Raw)
    $BindIp = $savedEnvironment.AXON_BIND_IP
}
if ([string]::IsNullOrWhiteSpace($BindIp)) {
    throw "Axon's bind IP was not supplied and could not be read from $EnvFile."
}

foreach ($path in @($EnvFile, $HomeserverFile, $NginxFile)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required Axon runtime file is missing: $path. Run Repair-Axon.ps1."
    }
}

& docker compose --project-name axon --env-file $EnvFile --file $ComposeFile ps
if ($LASTEXITCODE -ne 0) {
    & docker compose --project-name axon --env-file $EnvFile --file $ComposeFile logs --no-color --tail 120
    throw "Axon Compose status failed."
}

$tcp = Test-NetConnection $BindIp -Port 80 -WarningAction SilentlyContinue
if (-not $tcp.TcpTestSucceeded) { throw "Axon TCP port 80 is unreachable at $BindIp." }

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.UseProxy = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(10)
try {
    $versions = $client.GetAsync("http://$BindIp/_matrix/client/versions").GetAwaiter().GetResult()
    if (-not $versions.IsSuccessStatusCode) { throw "Matrix versions returned HTTP $([int]$versions.StatusCode)." }
    $admin = $client.GetAsync("http://$BindIp/_synapse/admin/v1/server_version").GetAwaiter().GetResult()
    if ([int]$admin.StatusCode -ne 404) { throw "The Synapse admin API was not blocked by nginx." }
} finally {
    $client.Dispose()
    $handler.Dispose()
}

$controlTcp = Test-NetConnection "127.0.0.1" -Port 8780 -WarningAction SilentlyContinue
if (-not $controlTcp.TcpTestSucceeded) { throw "Axon Control is not listening on host-only TCP 8780." }
$synapseLoopback = Test-NetConnection "127.0.0.1" -Port 8008 -WarningAction SilentlyContinue
if (-not $synapseLoopback.TcpTestSucceeded) { throw "Synapse's host-only control path is not listening on loopback TCP 8008." }

foreach ($blockedPort in @(8008, 8780, 5432)) {
    $result = Test-NetConnection $BindIp -Port $blockedPort -WarningAction SilentlyContinue
    if ($result.TcpTestSucceeded) { throw "Unexpected LAN listener on TCP $blockedPort." }
}
Write-Host "Axon audit passed for http://$BindIp"
