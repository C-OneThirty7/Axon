using Axon.Control.Tests.Deploy;

namespace Axon.Control.Tests.Scripts;

public sealed class WindowsScriptContractTests
{
    [Fact]
    public void Installer_is_elevated_offline_resumable_and_root_relative()
    {
        var script = DeployTestFiles.Read("scripts/Install-Axon.ps1");
        var launcher = DeployTestFiles.Read("Install Axon.cmd");

        Assert.Contains("#Requires -RunAsAdministrator", script);
        Assert.Contains("$PSScriptRoot", script);
        Assert.Contains("install-state.json", script);
        Assert.Contains("Docker Desktop Installer.exe", script);
        Assert.Contains("Microsoft-Windows-Subsystem-Linux", script);
        Assert.Contains("VirtualMachinePlatform", script);
        Assert.Contains("[Version]\"2.1.5\"", script);
        Assert.Contains("WSL version:", script);
        Assert.Contains("msiexec.exe", script);
        Assert.Contains("install --accept-license --backend=wsl-2 --always-run-service", script);
        Assert.Contains("Docker Desktop.exe", script);
        Assert.Contains("Wait-DockerEngine", script);
        Assert.Contains("[ValidateSet(\"Strict\", \"Warn\", \"Skip\")]", script);
        Assert.Contains("$ChecksumMode", script);
        Assert.Contains("Axon.Common.psm1", script);
        Assert.Contains("8GB", script);
        Assert.Contains("Write-Warning", script);
        Assert.Contains("$StrictPreflight", script);
        Assert.Contains("$env:LOCALAPPDATA", script);
        Assert.Contains("-ExecutionPolicy Bypass", launcher);
        Assert.Contains("-Verb RunAs", launcher);
        Assert.Contains("Install-Axon.ps1", launcher);
        Assert.Contains("pause", launcher, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-WebRequest", script, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            script.LastIndexOf("Test-AxonBundleChecksums", StringComparison.Ordinal) <
            script.IndexOf("Get-CimInstance Win32_OperatingSystem", StringComparison.Ordinal));
    }

    [Fact]
    public void Installer_requires_exact_NIC_confirmation_and_scopes_the_firewall()
    {
        var script = DeployTestFiles.Read("scripts/Install-Axon.ps1");

        Assert.Contains("Get-NetAdapter -Physical", script);
        Assert.Contains("[ValidateSet(\"Preserve\", \"Configure\")]", script);
        Assert.Contains("$NicMode", script);
        Assert.Contains("detectedByAddress", script);
        Assert.Contains("Preserving existing IPv4", script);
        Assert.Contains("New-NetIPAddress", script);
        Assert.True(
            script.IndexOf("New-NetIPAddress", StringComparison.Ordinal) <
            script.IndexOf("Remove-NetIPAddress", StringComparison.Ordinal));
        Assert.Contains("New-NetFirewallRule", script);
        Assert.Contains("$AllowedRemoteAddress", script);
        Assert.Contains("\"LocalSubnet\"", script);
        Assert.Contains("Axon Matrix LAN", script);
        Assert.Contains("render-runtime", script);
        Assert.Contains("Reusing the existing protected Axon runtime and secrets", script);
        Assert.Contains("ConvertFrom-StringData", script);
        Assert.DoesNotContain("docker volume inspect", script);
        Assert.Contains("chown", script);
        Assert.Contains("Assert-RuntimeFiles", script);
        Assert.Contains("Show-ComposeDiagnostics", script);
        Assert.Contains("New-ScheduledTaskAction", script);
        Assert.Contains("Listen -LocalAddress \"127.0.0.1\" -LocalPort 8780", script);
        Assert.Contains("http://127.0.0.1:8780", script);
        Assert.Contains("Axon Control.url", script);
        Assert.Contains("--admin --config /config/homeserver.yaml", script);
        Assert.Contains("Get-ChildItem -LiteralPath $ImageDirectory -Filter \"*.tar\"", script);
        Assert.Contains("docker compose", script);
        Assert.DoesNotContain("-DefaultGateway", script);
        Assert.Contains("Install will continue", script);
        Assert.Contains("Get-DockerDesktopExecutable", script);
    }

    [Fact]
    public void Bundle_checker_supports_strict_warn_and_skip_modes()
    {
        var module = DeployTestFiles.Read("scripts/Axon.Common.psm1");
        var command = DeployTestFiles.Read("scripts/Test-AxonBundle.ps1");

        Assert.Contains("Test-AxonBundleChecksums", module);
        Assert.Contains("Get-FileHash", module);
        Assert.Contains("TrimStart([char]0xFEFF)", module);
        Assert.Contains("Checksum validation found", module);
        Assert.Contains("Get-AuthenticodeSignature", module);
        Assert.Contains("HashMismatch", module);
        Assert.Contains("NotSigned", module);
        Assert.Contains("-Mode $Mode", command);
    }

    [Fact]
    public void Uninstall_preserves_data_unless_double_confirmed()
    {
        var script = DeployTestFiles.Read("scripts/Uninstall-Axon.ps1");

        Assert.Contains("[switch]$PurgeData", script);
        Assert.Contains("PURGE AXON", script);
        Assert.Contains("Read-Host", script);
        Assert.Contains("--volumes", script);
        Assert.Contains("Get-NetFirewallRule -Group Axon", script);
    }

    [Fact]
    public void Repair_and_test_entry_points_are_offline_and_root_relative()
    {
        var repair = DeployTestFiles.Read("scripts/Repair-Axon.ps1");
        var test = DeployTestFiles.Read("scripts/Test-Axon.ps1");

        Assert.Contains("$PSScriptRoot", repair);
        Assert.Contains("-Repair", repair);
        Assert.Contains("UseProxy = $false", test);
        Assert.Contains("/_matrix/client/versions", test);
        Assert.Contains("/_synapse/admin", test);
        Assert.Contains("\"127.0.0.1\" -Port 8780", test);
        Assert.Contains("\"127.0.0.1\" -Port 8008", test);
    }

    [Fact]
    public void Control_updater_validates_backs_up_and_rolls_back_the_host_only_panel()
    {
        var updater = DeployTestFiles.Read("scripts/Update-AxonControl.ps1");
        var builder = DeployTestFiles.Read("scripts/Build-ControlUpgrade.ps1");
        var launcher = DeployTestFiles.Read("scripts/Update-AxonControl.cmd");

        Assert.Contains("manifest.json", updater);
        Assert.Contains("$manifestDocument.files", updater);
        Assert.Contains("schemaVersion", updater);
        Assert.Contains("Get-FileHash", updater);
        Assert.Contains("Axon Control Panel", updater);
        Assert.Contains("control-backups", updater);
        Assert.Contains("Wait-LoopbackPort -Port 8780", updater);
        Assert.Contains("Restoring the previous control panel", updater);
        Assert.Contains("http://127.0.0.1:8780", updater);
        Assert.Contains("dotnet publish", builder);
        Assert.Contains("-r win-x64 --self-contained true", builder);
        Assert.Contains("schemaVersion = 1", builder);
        Assert.Contains("files = @($manifest)", builder);
        Assert.Contains("Update-AxonControl.ps1", launcher);
    }

    [Fact]
    public void Release_updater_reverifies_extracts_upgrades_and_recovers()
    {
        var updater = DeployTestFiles.Read("scripts/Invoke-AxonUpdate.ps1");
        var installer = DeployTestFiles.Read("scripts/Install-Axon.ps1");

        Assert.Contains("#Requires -RunAsAdministrator", updater);
        Assert.Contains("Get-FileHash", updater);
        Assert.Contains("Assert-ZipEntriesSafe", updater);
        Assert.Contains("Test-AxonBundle.ps1", updater);
        Assert.Contains("install-state.json", updater);
        Assert.Contains("-Upgrade", updater);
        Assert.Contains("Restore-PreviousRuntime", updater);
        Assert.Contains("Start-ScheduledTask", updater);
        Assert.Contains("[switch]$Upgrade", installer);
        Assert.DoesNotContain("Invoke-WebRequest", updater);
    }
}
