using Axon.Control.Tests.Deploy;

namespace Axon.Control.Tests.Scripts;

public sealed class LinuxDeploymentContractTests
{
    [Fact]
    public void Installer_preserves_network_and_initializes_runtime_before_compose()
    {
        var installer = DeployTestFiles.Read("installer/linux/install.sh");

        Assert.Contains("Axon does not rewrite NIC settings", installer);
        Assert.Contains("ip -o -4 addr show scope global", installer);
        Assert.DoesNotContain("ip addr add", installer);
        Assert.DoesNotContain("ip route add", installer);
        Assert.Contains("render_runtime", installer);
        Assert.Contains("initialize_synapse_volume", installer);
        Assert.Contains("start_and_test", installer);
        Assert.True(
            installer.IndexOf("render_runtime", StringComparison.Ordinal) <
            installer.LastIndexOf("start_and_test", StringComparison.Ordinal));
        Assert.Contains("capacity below 200 users is expected", installer);
        Assert.Contains("--strict-preflight", installer);
    }

    [Fact]
    public void Systemd_keeps_control_available_separately_and_on_loopback()
    {
        var stack = DeployTestFiles.Read("deploy/systemd/axon-stack.service");
        var control = DeployTestFiles.Read("deploy/systemd/axon-control.service");
        var operations = DeployTestFiles.Read("installer/linux/axon");

        Assert.Contains("WantedBy=multi-user.target", stack);
        Assert.Contains("Requires=docker.service", stack);
        Assert.Contains("configure-firewall.sh", stack);
        Assert.Contains("User=axon", control);
        Assert.Contains("NoNewPrivileges=yes", control);
        Assert.Contains("ProtectSystem=strict", control);
        Assert.Contains("127.0.0.1:8780", operations);
        Assert.Contains("systemctl stop axon-stack.service", operations);
        Assert.Contains("systemctl start axon-control.service", operations);
    }

    [Fact]
    public void Firewall_uses_scoped_docker_user_chain()
    {
        var firewall = DeployTestFiles.Read("installer/linux/configure-firewall.sh");

        Assert.Contains("AXON-INGRESS", firewall);
        Assert.Contains("DOCKER-USER", firewall);
        Assert.Contains("--ctorigdst", firewall);
        Assert.Contains("--ctorigdstport", firewall);
        Assert.Contains("AXON_ALLOWED_CIDRS", firewall);
        Assert.DoesNotContain("iptables -F DOCKER-USER", firewall);
    }

    [Fact]
    public void Linux_packager_exports_immutable_multi_arch_images_and_checksums()
    {
        var packager = DeployTestFiles.Read("packaging/linux/build-release.sh");

        Assert.Contains("linux-x64", packager);
        Assert.Contains("linux-arm64", packager);
        Assert.Contains("docker buildx build", packager);
        Assert.Contains("type=docker", packager);
        Assert.Contains("RepoDigests", packager);
        Assert.Contains("image-digests.json", packager);
        Assert.Contains("sbom.cdx.json", packager);
        Assert.Contains("sources.json", packager);
        Assert.Contains("SHA256SUMS", packager);
        Assert.Contains("${bundle_flavor}-${distro}-${arch}", packager);
        Assert.Contains("git -C \"$SOURCE_ROOT\" ls-files", packager);
        Assert.DoesNotContain("git push", packager);
        Assert.DoesNotContain("gh release", packager);
    }

    [Fact]
    public void Repository_excludes_generated_and_sensitive_payloads()
    {
        var ignore = DeployTestFiles.Read(".gitignore");
        var security = DeployTestFiles.Read("SECURITY.md");

        Assert.Contains("dist/", ignore);
        Assert.Contains("packages/", ignore);
        Assert.Contains("images/", ignore);
        Assert.Contains("*.pcap", ignore);
        Assert.Contains(".env", ignore);
        Assert.Contains("Never expose PostgreSQL", security);
        Assert.Contains("48-hour", security);
    }
}
