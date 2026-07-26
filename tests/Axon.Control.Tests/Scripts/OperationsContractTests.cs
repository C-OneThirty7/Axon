using Axon.Control.Tests.Deploy;

namespace Axon.Control.Tests.Scripts;

public sealed class OperationsContractTests
{
    [Fact]
    public void Operations_menu_keeps_control_separate_from_messaging_services()
    {
        var script = DeployTestFiles.Read("scripts/Axon-Operations.ps1");
        var launcher = DeployTestFiles.Read("Axon Operations.cmd");
        var startLauncher = DeployTestFiles.Read("Start Axon.cmd");

        Assert.Contains("\"Start\"", script);
        Assert.Contains("\"Stop\"", script);
        Assert.Contains("\"Restart\"", script);
        Assert.Contains("\"Status\"", script);
        Assert.Contains("\"StartControl\"", script);
        Assert.Contains("\"StopControl\"", script);
        Assert.Contains("Invoke-Compose stop", script);
        Assert.Contains("Start-ControlPanel", script);
        Assert.Contains("Messaging services stopped. Axon Control remains available.", script);
        Assert.Contains("docker compose --project-name axon", script);
        Assert.Contains("--detach --wait", script);
        Assert.Contains("Starting Docker Desktop", script);
        Assert.Contains("STOP CONTROL", script);
        Assert.Contains("-ExecutionPolicy Bypass", launcher);
        Assert.Contains("-Action Start", startLauncher);
        Assert.Contains("-BundleRoot \"%~dp0\"", startLauncher);
        Assert.Contains("-BundleRoot \"%~dp0\"", launcher);
        Assert.Contains("-ExecutionPolicy Bypass", startLauncher);
        Assert.DoesNotContain("Set-ExecutionPolicy", launcher);
        Assert.DoesNotContain("(Split-Path -Parent $PSScriptRoot)", script.Split("Set-StrictMode")[0]);
    }

    [Fact]
    public void Operations_kit_contains_root_launcher_script_and_guide()
    {
        var builder = DeployTestFiles.Read("scripts/Build-OperationsKit.ps1");
        var installer = DeployTestFiles.Read("scripts/Install-AxonOperations.ps1");
        var guide = DeployTestFiles.Read("docs/operator/START_STOP_RESTART.md");

        Assert.Contains("Axon Operations.cmd", builder);
        Assert.Contains("Axon-Operations.ps1", builder);
        Assert.Contains("START_STOP_RESTART.md", builder);
        Assert.Contains("Install Axon Operations.cmd", builder);
        Assert.Contains("original Axon folder's updates directory", builder);
        Assert.Contains("deploy\\compose.yaml", installer);
        Assert.Contains("Axon Operations.lnk", installer);
        Assert.Contains("Start Axon.lnk", installer);
        Assert.Contains("Start Axon.cmd", builder);
        Assert.Contains("Copy-Item", installer);
        Assert.Contains("Pause all services", guide);
        Assert.Contains("The GUI process remains running.", guide);
        Assert.Contains("Do not sign out while", guide);
        Assert.Contains("Synapse is stopped", guide);
    }
}
