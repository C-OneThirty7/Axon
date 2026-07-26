using System.Text.Json;
using Axon.Control.Tests.Deploy;

namespace Axon.Control.Tests.Scripts;

public sealed class OfflineBundleContractTests
{
    [Fact]
    public void Release_inputs_use_only_reviewed_primary_sources()
    {
        var json = DeployTestFiles.Read("manifests/release-inputs.json");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.StartsWith("https://api.github.com/repos/element-hq/synapse/", root.GetProperty("synapseLatestReleaseApi").GetString());
        Assert.Equal("matrixdotorg/synapse", root.GetProperty("synapseImage").GetString());
        Assert.StartsWith("postgres:", root.GetProperty("postgresImage").GetString());
        Assert.StartsWith("nginx:", root.GetProperty("nginxImage").GetString());
        Assert.StartsWith("https://desktop.docker.com/win/main/amd64/", root.GetProperty("dockerDesktopWindowsAmd64").GetString());
        Assert.StartsWith("https://api.github.com/repos/microsoft/WSL/", root.GetProperty("wslLatestReleaseApi").GetString());
    }

    [Fact]
    public void Packager_pins_downloads_checksums_and_preserves_expanded_bundle()
    {
        var script = DeployTestFiles.Read("scripts/Build-OfflineBundle.ps1");

        Assert.Contains("prerelease", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("linux/amd64", script);
        Assert.Contains("RepoDigests", script);
        Assert.Contains("docker buildx build", script);
        Assert.Contains("type=docker", script);
        Assert.Contains("axon.local", script);
        Assert.Contains("Axon.Common.psm1", script);
        Assert.Contains("Test-AxonBundle.ps1", script);
        Assert.Contains("output\\pdf", script);
        Assert.Contains("dotnet publish", script);
        Assert.Contains("win-x64", script);
        Assert.Contains("Get-FileHash", script);
        Assert.Contains("SHA256SUMS", script);
        Assert.Contains("Compress-Archive", script);
        Assert.Contains("Axon-v$Version-offline-win-x64.zip", script);
        Assert.Contains("Install Axon.cmd", script);
        Assert.Contains("README_FIRST.txt", script);
        Assert.Contains("INSTALL_WINDOWS.md", script);
        Assert.Contains("THIRD_PARTY_NOTICES.md", script);
        Assert.Contains("\"LICENSE\"", script);
        Assert.DoesNotContain("Axon Operations.cmd", script);
        Assert.DoesNotContain("Start Axon.cmd", script);
        Assert.DoesNotContain("Remove-Item $BundleRoot", script, StringComparison.OrdinalIgnoreCase);
    }
}
