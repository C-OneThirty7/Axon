#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$DataRoot = (Join-Path $env:ProgramData "Axon"),
    [string]$BindIp,
    [string]$InterfaceAlias,
    [ValidateRange(1, 32)][int]$PrefixLength = 24,
    [string[]]$AllowedRemoteAddress = @("LocalSubnet"),
    [string]$SynapseImage,
    [string]$PostgresImage,
    [string]$NginxImage,
    [ValidateSet("Strict", "Warn", "Skip")][string]$ChecksumMode = "Strict",
    [ValidateSet("Preserve", "Configure")][string]$NicMode = "Preserve",
    [switch]$StrictPreflight,
    [switch]$Repair,
    [switch]$Upgrade,
    [switch]$SkipInitialUser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$BundleRoot = Split-Path -Parent $PSScriptRoot
$ComposeFile = Join-Path $BundleRoot "deploy\compose.yaml"
$StatePath = Join-Path $DataRoot "install-state.json"
$ChecksumPath = Join-Path $BundleRoot "manifests\SHA256SUMS"
$ImageDirectory = Join-Path $BundleRoot "images"
$ImageManifestPath = Join-Path $BundleRoot "manifests\image-digests.json"
$EnvironmentPath = Join-Path $DataRoot ".env"
$HomeserverPath = Join-Path $DataRoot "runtime\synapse\homeserver.yaml"
$NginxPath = Join-Path $DataRoot "runtime\nginx\default.conf"
$ControlCandidates = @(
    (Join-Path $BundleRoot "bin\Axon.Control.exe"),
    (Join-Path $BundleRoot "artifacts\win-x64\Axon.Control.exe")
)
$CommonModule = Join-Path $PSScriptRoot "Axon.Common.psm1"
if (-not (Test-Path -LiteralPath $CommonModule -PathType Leaf)) {
    throw "Axon installer support module is missing: $CommonModule"
}
Import-Module -Name $CommonModule -Force

function Save-InstallState {
    param([Parameter(Mandatory)][string]$Stage)
    New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
    [pscustomobject]@{
        stage = $Stage
        updatedUtc = [DateTime]::UtcNow.ToString("o")
        bindIp = $BindIp
        interfaceAlias = $InterfaceAlias
        allowedRemoteAddress = $AllowedRemoteAddress
    } | ConvertTo-Json | Set-Content -LiteralPath $StatePath -Encoding UTF8
}

function Test-DockerEngine {
    try {
        & docker version --format '{{.Server.Version}}' 2>$null | Out-Null
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

function Get-DockerDesktopExecutable {
    return @(
        (Join-Path $env:ProgramFiles "Docker\Docker\Docker Desktop.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\DockerDesktop\Docker Desktop.exe")
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}

function Initialize-DockerCliPath {
    if (Get-Command docker -ErrorAction SilentlyContinue) { return }

    $dockerCli = @(
        (Join-Path $env:ProgramFiles "Docker\Docker\resources\bin\docker.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\DockerDesktop\resources\bin\docker.exe")
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if ($dockerCli) {
        $env:Path = "$(Split-Path -Parent $dockerCli);$env:Path"
    }
}

function Wait-DockerEngine {
    $deadline = [DateTime]::UtcNow.AddMinutes(5)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-DockerEngine) { return }
        Start-Sleep -Seconds 5
    }
    throw "Docker Desktop's Linux engine did not become ready within five minutes."
}

function Start-DockerDesktopIfNeeded {
    if (Test-DockerEngine) { return }

    $dockerDesktop = Get-DockerDesktopExecutable
    if (-not $dockerDesktop) { throw "Docker Desktop is installed but its executable was not found." }

    Write-Host "Starting Docker Desktop and waiting for its Linux engine..."
    Start-Process -FilePath $dockerDesktop | Out-Null
}

function Resolve-Images {
    if (Test-Path -LiteralPath $ImageManifestPath) {
        $manifest = Get-Content -LiteralPath $ImageManifestPath -Raw | ConvertFrom-Json
        if (-not $SynapseImage) { $script:SynapseImage = [string]$manifest.synapse }
        if (-not $PostgresImage) { $script:PostgresImage = [string]$manifest.postgres }
        if (-not $NginxImage) { $script:NginxImage = [string]$manifest.nginx }
    }
    foreach ($image in @($SynapseImage, $PostgresImage, $NginxImage)) {
        $isUpstreamDigest = $image -match '@sha256:[a-fA-F0-9]{64}$'
        $isOfflineDigestTag = $image -match '^axon\.local/(synapse|postgres|nginx):sha256-[a-fA-F0-9]{64}$'
        if ([string]::IsNullOrWhiteSpace($image) -or (-not $isUpstreamDigest -and -not $isOfflineDigestTag)) {
            throw "All Axon runtime images must be supplied as upstream digests or Axon offline digest tags."
        }
    }
}

function Assert-RuntimeFiles {
    foreach ($path in @($EnvironmentPath, $HomeserverPath, $NginxPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
            throw "Required Axon runtime file was not created: $path"
        }
    }
    if ((Get-Content -LiteralPath $HomeserverPath -Raw) -notmatch 'server_name:\s*["'']?axon\.home\.arpa') {
        throw "Rendered Synapse configuration did not contain the required server name."
    }
}

function Initialize-AxonRuntime {
    Resolve-Images
    $imageArchives = @(Get-ChildItem -LiteralPath $ImageDirectory -Filter "*.tar" -File)
    if ($imageArchives.Count -eq 0) { throw "No offline Docker image archives were found in $ImageDirectory." }
    foreach ($imageArchive in $imageArchives) {
        & docker load --input $imageArchive.FullName
        if ($LASTEXITCODE -ne 0) { throw "Docker image archive failed to load: $($imageArchive.Name)" }
    }

    $AxonControl = $ControlCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $AxonControl) { throw "Axon.Control.exe is missing from the offline bundle." }
    New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
    $runtimeFiles = @($EnvironmentPath, $HomeserverPath, $NginxPath)
    $existingRuntimeFiles = @($runtimeFiles | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    if ($existingRuntimeFiles.Count -eq 0) {
        & $AxonControl render-runtime $BundleRoot $DataRoot $BindIp $SynapseImage $PostgresImage $NginxImage
        if ($LASTEXITCODE -ne 0) { throw "Axon runtime rendering failed with exit code $LASTEXITCODE." }
    } elseif ($existingRuntimeFiles.Count -ne $runtimeFiles.Count) {
        throw "Axon's protected runtime is incomplete. Preserve $DataRoot and investigate before repair."
    } else {
        $savedEnvironment = ConvertFrom-StringData (Get-Content -LiteralPath $EnvironmentPath -Raw)
        $expectedRuntime = @{
            AXON_BIND_IP = $BindIp
            SYNAPSE_IMAGE = $SynapseImage
            POSTGRES_IMAGE = $PostgresImage
            NGINX_IMAGE = $NginxImage
        }
        $changedEntries = @($expectedRuntime.GetEnumerator() | Where-Object {
            $savedEnvironment[$_.Key] -cne $_.Value
        })
        if ($changedEntries.Count -gt 0 -and -not $Upgrade) {
            $names = $changedEntries.Key -join ", "
            throw "Existing runtime image values differ from this bundle ($names). Use Axon Control's verified update process or rerun with -Upgrade."
        }
        if ($changedEntries.Count -gt 0) {
            $environmentLines = @(Get-Content -LiteralPath $EnvironmentPath)
            foreach ($entry in $changedEntries) {
                $found = $false
                for ($index = 0; $index -lt $environmentLines.Count; $index++) {
                    if ($environmentLines[$index] -match "^$([Regex]::Escape($entry.Key))=") {
                        $environmentLines[$index] = "$($entry.Key)=$($entry.Value)"
                        $found = $true
                    }
                }
                if (-not $found) {
                    $environmentLines += "$($entry.Key)=$($entry.Value)"
                }
            }
            [IO.File]::WriteAllLines(
                $EnvironmentPath,
                $environmentLines,
                [Text.UTF8Encoding]::new($false))
            Write-Host "Updated immutable runtime image references for the verified Axon upgrade."
        }
        Write-Host "Reusing the existing protected Axon runtime and secrets."
    }
    Assert-RuntimeFiles

    & docker volume create `
        --label "com.docker.compose.project=axon" `
        --label "com.docker.compose.volume=axon_synapse" `
        axon_axon_synapse | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Axon Synapse volume could not be created or inspected." }
    & docker run --rm --user root --volume axon_axon_synapse`:/data --entrypoint chown $SynapseImage -R 991:991 /data
    if ($LASTEXITCODE -ne 0) { throw "Axon Synapse volume ownership initialization failed." }
}

function Show-ComposeDiagnostics {
    Write-Warning "Axon did not become healthy. Collecting bounded diagnostics (secrets are not printed)."
    & docker compose --project-name axon --env-file $EnvironmentPath --file $ComposeFile ps
    & docker compose --project-name axon --env-file $EnvironmentPath --file $ComposeFile logs --no-color --tail 120 postgres synapse gateway
}

function Install-AxonControlPanel {
    $AxonControl = $ControlCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $AxonControl) { throw "Axon.Control.exe is missing from the offline bundle." }

    $taskName = "Axon Control Panel"
    $operator = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $arguments = "serve --bundle-root `"$BundleRoot`" --data-root `"$DataRoot`""
    $action = New-ScheduledTaskAction `
        -Execute $AxonControl `
        -Argument $arguments `
        -WorkingDirectory (Split-Path -Parent $AxonControl)
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $operator
    $principal = New-ScheduledTaskPrincipal -UserId $operator -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 1)

    Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue |
        Stop-ScheduledTask -ErrorAction SilentlyContinue
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
        -Principal $principal -Settings $settings -Description "Host-only Axon administration at http://127.0.0.1:8780" `
        -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName

    $deadline = [DateTime]::UtcNow.AddMinutes(2)
    while ([DateTime]::UtcNow -lt $deadline) {
        $listener = Get-NetTCPConnection -State Listen -LocalAddress "127.0.0.1" -LocalPort 8780 -ErrorAction SilentlyContinue
        if ($listener) { break }
        Start-Sleep -Seconds 2
    }
    if (-not $listener) { throw "Axon Control did not start on loopback TCP 8780." }

    $shortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "Axon Control.url"
    @(
        "[InternetShortcut]"
        "URL=http://127.0.0.1:8780"
        "IconFile=$AxonControl"
        "IconIndex=0"
    ) | Set-Content -LiteralPath $shortcutPath -Encoding ASCII

}

Test-AxonBundleChecksums -BundleRoot $BundleRoot -ManifestPath $ChecksumPath -Mode $ChecksumMode | Out-Null
Save-InstallState -Stage "checksums-verified"

if (-not [Environment]::Is64BitOperatingSystem) { throw "Axon requires 64-bit Windows." }
$os = Get-CimInstance Win32_OperatingSystem
if ([int]$os.BuildNumber -lt 22631) { throw "Axon requires Windows build 22631 or newer." }
$memoryBytes = [int64]$os.TotalVisibleMemorySize * 1KB
if ($memoryBytes -lt 8GB) {
    $message = "This host has less than Docker Desktop's supported 8 GiB RAM requirement. Axon will continue, but Docker may fail or perform poorly."
    if ($StrictPreflight) { throw $message } else { Write-Warning $message }
}
if ($memoryBytes -lt 16GB) {
    $message = "This host is below Axon's recommended 16 GiB for a 200-user deployment. Install will continue with a reduced expected capacity."
    if ($StrictPreflight) { throw $message } else { Write-Warning $message }
}
$systemDrive = Get-PSDrive -Name $env:SystemDrive.TrimEnd(':')
if ($systemDrive.Free -lt 20GB) {
    $message = "This host has less than 20 GiB free on the system drive. Install will continue, but Docker image extraction may run out of space."
    if ($StrictPreflight) { throw $message } else { Write-Warning $message }
}
if ($systemDrive.Free -lt 100GB) {
    $message = "The host is below Axon's recommended 100 GiB free disk space. Install will continue with reduced storage headroom."
    if ($StrictPreflight) { throw $message } else { Write-Warning $message }
}
$processor = Get-CimInstance Win32_Processor | Select-Object -First 1
if (-not ($processor.VirtualizationFirmwareEnabled -or (Get-CimInstance Win32_ComputerSystem).HypervisorPresent)) {
    throw "Hardware virtualization must be enabled in firmware."
}

$restartRequired = $false
foreach ($featureName in @("Microsoft-Windows-Subsystem-Linux", "VirtualMachinePlatform")) {
    $feature = Get-WindowsOptionalFeature -Online -FeatureName $featureName
    if ($feature.State -ne "Enabled") {
        $featureResult = Enable-WindowsOptionalFeature -Online -FeatureName $featureName -All -NoRestart
        if ($featureResult.RestartNeeded) { $restartRequired = $true }
    }
}
if ($restartRequired) {
    Save-InstallState -Stage "reboot-required"
    throw "Windows features were enabled. Restart Windows and rerun Install-Axon.ps1 to resume."
}

$minimumWslVersion = [Version]"2.1.5"
$wslVersion = $null
try {
    $wslVersionOutput = (& wsl.exe --version 2>$null) -join "`n"
    if ($LASTEXITCODE -eq 0 -and $wslVersionOutput -match '(?im)WSL version:\s*(\d+\.\d+\.\d+(?:\.\d+)?)') {
        $wslVersion = [Version]$Matches[1]
    }
} catch { $wslVersion = $null }
$wslAvailable = $wslVersion -and $wslVersion -ge $minimumWslVersion
if (-not $wslAvailable) {
    Write-Host "Installing the bundled WSL update because WSL $minimumWslVersion or newer was not detected."
    $wslInstaller = Get-ChildItem -LiteralPath (Join-Path $BundleRoot "installers") -Filter "*x64*.msi" -File |
        Where-Object Name -NotMatch "arm64" |
        Select-Object -First 1
    if (-not $wslInstaller) { throw "WSL is unavailable and the offline x64 WSL MSI is missing." }
    Save-InstallState -Stage "wsl-installing"
    $wslProcess = Start-Process -FilePath "msiexec.exe" -ArgumentList @("/i", $wslInstaller.FullName, "/qn", "/norestart") -Wait -PassThru
    if ($wslProcess.ExitCode -notin @(0, 3010)) { throw "WSL installer failed with exit code $($wslProcess.ExitCode)." }
    if ($wslProcess.ExitCode -eq 3010) {
        Save-InstallState -Stage "reboot-required"
        throw "WSL installation requires a restart. Rerun Install-Axon.ps1 afterward."
    }
}

Initialize-DockerCliPath
$dockerDesktop = Get-DockerDesktopExecutable
if (-not $dockerDesktop) {
    $dockerInstaller = Join-Path $BundleRoot "installers\Docker Desktop Installer.exe"
    if (-not (Test-Path -LiteralPath $dockerInstaller)) {
        throw "Docker Desktop is unavailable and its offline installer is missing."
    }
    Save-InstallState -Stage "docker-installing"
    $process = Start-Process -FilePath $dockerInstaller -ArgumentList "install --accept-license --backend=wsl-2 --always-run-service --no-windows-containers" -Wait -PassThru
    if ($process.ExitCode -notin @(0, 3010)) { throw "Docker Desktop installer failed with exit code $($process.ExitCode)." }
    if ($process.ExitCode -eq 3010) {
        Save-InstallState -Stage "reboot-required"
        throw "Windows must restart. Rerun Install-Axon.ps1 after reboot; installation will resume."
    }
    Initialize-DockerCliPath
}
Start-DockerDesktopIfNeeded
Wait-DockerEngine
Save-InstallState -Stage "docker-ready"

$adapters = @(Get-NetAdapter -Physical | Where-Object {
    $_.Status -eq "Up" -and $_.PhysicalMediaType -notmatch 'Bluetooth'
})
if ($adapters.Count -eq 0) { throw "No connected physical network adapter was detected." }
$adapterDisplay = foreach ($adapter in $adapters) {
    $addresses = @(Get-NetIPAddress -InterfaceIndex $adapter.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike "169.254.*" } |
        ForEach-Object { "$($_.IPAddress)/$($_.PrefixLength)" })
    [pscustomobject]@{
        Name = $adapter.Name
        InterfaceIndex = $adapter.InterfaceIndex
        IPv4 = $addresses -join ", "
        LinkSpeed = $adapter.LinkSpeed
    }
}
$adapterDisplay | Format-Table -AutoSize

if ([string]::IsNullOrWhiteSpace($InterfaceAlias)) {
    if (-not [string]::IsNullOrWhiteSpace($BindIp)) {
        $detectedByAddress = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
            Where-Object IPAddress -eq $BindIp |
            ForEach-Object InterfaceIndex |
            Select-Object -Unique)
        if ($detectedByAddress.Count -eq 1) {
            $detectedAdapter = $adapters | Where-Object InterfaceIndex -eq $detectedByAddress[0]
            if (@($detectedAdapter).Count -eq 1) {
                $InterfaceAlias = $detectedAdapter.Name
                Write-Host "Detected Axon address $BindIp on adapter '$InterfaceAlias'."
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($InterfaceAlias) -and $adapters.Count -eq 1) {
        $InterfaceAlias = $adapters[0].Name
        Write-Host "Selected the only connected physical adapter: '$InterfaceAlias'."
    }
    if ([string]::IsNullOrWhiteSpace($InterfaceAlias)) {
        $InterfaceAlias = Read-Host "Enter the exact adapter Name Axon should use"
    }
}
$selected = $adapters | Where-Object Name -eq $InterfaceAlias
if (@($selected).Count -ne 1) { throw "Exactly one connected physical adapter must match '$InterfaceAlias'." }

$existing = @(Get-NetIPAddress -InterfaceIndex $selected.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue)
$usable = @($existing | Where-Object {
    $_.IPAddress -notlike "169.254.*" -and $_.IPAddress -ne "0.0.0.0"
})
if ([string]::IsNullOrWhiteSpace($BindIp)) {
    if ($usable.Count -eq 1) {
        $BindIp = $usable[0].IPAddress
        $PrefixLength = $usable[0].PrefixLength
        Write-Host "Selected the adapter's IPv4 address: $BindIp/$PrefixLength."
    } else {
        $BindIp = Read-Host "Enter the IPv4 address Axon should serve on"
    }
}
$parsedBindIp = [Net.IPAddress]::None
if (-not [Net.IPAddress]::TryParse($BindIp, [ref]$parsedBindIp) -or
    $parsedBindIp.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or
    [Net.IPAddress]::IsLoopback($parsedBindIp) -or $parsedBindIp.Equals([Net.IPAddress]::Any)) {
    throw "Axon requires a valid non-loopback IPv4 address."
}
$matching = @($existing | Where-Object IPAddress -eq $BindIp)
if ($NicMode -eq "Preserve") {
    if (-not $matching) {
        throw "Adapter '$InterfaceAlias' does not already own $BindIp. Configure it in Windows first or rerun with -NicMode Configure."
    }
    $PrefixLength = $matching[0].PrefixLength
    Write-Host "Preserving existing IPv4, gateway, and DNS settings on '$InterfaceAlias'."
} elseif (-not $matching) {
    Write-Host "Axon will add and verify $BindIp/$PrefixLength on '$InterfaceAlias' before removing old IPv4 addresses."
    $confirmation = Read-Host "Type the exact adapter Name '$InterfaceAlias' to confirm"
    if ($confirmation -cne $InterfaceAlias) { throw "Adapter confirmation did not match; no network changes were made." }
    Set-NetIPInterface -InterfaceIndex $selected.InterfaceIndex -AddressFamily IPv4 -Dhcp Disabled
    New-NetIPAddress -InterfaceIndex $selected.InterfaceIndex -IPAddress $BindIp -PrefixLength $PrefixLength -PolicyStore PersistentStore | Out-Null
    $verified = Get-NetIPAddress -InterfaceIndex $selected.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -eq $BindIp -and $_.PrefixLength -eq $PrefixLength }
    if (-not $verified) { throw "Windows did not confirm $BindIp/$PrefixLength; existing addresses were preserved." }
    foreach ($address in $existing | Where-Object IPAddress -ne $BindIp) {
        Remove-NetIPAddress -InterfaceIndex $selected.InterfaceIndex -IPAddress $address.IPAddress -Confirm:$false
    }
    Get-NetRoute -InterfaceIndex $selected.InterfaceIndex -AddressFamily IPv4 -DestinationPrefix "0.0.0.0/0" -ErrorAction SilentlyContinue |
        Remove-NetRoute -Confirm:$false
    Set-DnsClientServerAddress -InterfaceIndex $selected.InterfaceIndex -ResetServerAddresses
}
Set-NetConnectionProfile -InterfaceIndex $selected.InterfaceIndex -NetworkCategory Private -ErrorAction SilentlyContinue
Save-InstallState -Stage "network-configured"

Initialize-AxonRuntime
Save-InstallState -Stage "runtime-ready"

if (-not $Repair) {
    $portOwner = Get-NetTCPConnection -State Listen -LocalPort 80 -ErrorAction SilentlyContinue |
        Where-Object LocalAddress -in @($BindIp, "0.0.0.0", "::") |
        Select-Object -First 1
    if ($portOwner) { throw "TCP port 80 is already owned by process $($portOwner.OwningProcess)." }
}

Get-NetFirewallRule -Group Axon -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName "Axon Matrix LAN" -Group "Axon" -Direction Inbound `
    -Action Allow -Protocol TCP -LocalPort 80 -LocalAddress $BindIp `
    -RemoteAddress $AllowedRemoteAddress -InterfaceAlias $InterfaceAlias -Profile Private | Out-Null
Save-InstallState -Stage "firewall-configured"

& docker compose --project-name axon --env-file $EnvironmentPath --file $ComposeFile config --quiet
if ($LASTEXITCODE -ne 0) { throw "Axon's rendered Docker Compose configuration is invalid." }

& docker compose --project-name axon --env-file $EnvironmentPath --file $ComposeFile up --detach --wait --wait-timeout 300
if ($LASTEXITCODE -ne 0) {
    Show-ComposeDiagnostics
    throw "Axon containers failed to become healthy. Review the diagnostics above and rerun Repair-Axon.ps1 after correcting the cause."
}
Save-InstallState -Stage "running"

if (-not $SkipInitialUser) {
    $createUser = Read-Host "Create the initial Matrix server administrator now? [Y/n]"
    if ($createUser -notmatch '^[Nn]') {
        Write-Host "The password prompt is handled inside the Synapse container and is not logged by Axon."
        & docker exec -it axon-synapse-1 register_new_matrix_user --admin --config /config/homeserver.yaml http://127.0.0.1:8008
        if ($LASTEXITCODE -ne 0) { throw "Initial Matrix administrator creation failed." }
    }
}

Install-AxonControlPanel
Save-InstallState -Stage "complete"
Write-Host "Axon installation complete."
Write-Host "Element homeserver: http://$BindIp"
Write-Host "Matrix identity domain: axon.home.arpa"
Write-Host "Host-only administration: http://127.0.0.1:8780"
Start-Process "http://127.0.0.1:8780"
