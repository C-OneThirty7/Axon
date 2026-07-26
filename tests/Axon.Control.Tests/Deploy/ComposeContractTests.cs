namespace Axon.Control.Tests.Deploy;

public sealed class ComposeContractTests
{
    [Fact]
    public void Runtime_exposes_only_the_gateway_to_the_LAN()
    {
        var compose = DeployTestFiles.Read("deploy/compose.yaml");

        Assert.Contains("${AXON_BIND_IP}:${AXON_CLIENT_PORT:-80}:80", compose);
        Assert.Contains("${AXON_CONTROL_BIND_IP:-127.0.0.1}", compose);
        Assert.Contains("${AXON_SYNAPSE_ADMIN_PORT:-8008}:8008", compose);
        Assert.DoesNotContain("\"0.0.0.0:8008:8008\"", compose);
        Assert.DoesNotContain("\"${AXON_BIND_IP}:8008:8008\"", compose);
        Assert.Contains("internal: true", compose);
        Assert.Contains("axon_ingress:", compose);
        Assert.DoesNotContain("8448", compose);
        Assert.DoesNotContain("latest", compose, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("5432:5432", compose);
        Assert.Equal(3, Count(compose, "pull_policy: never"));
        Assert.Contains("SYNAPSE_CONFIG_PATH: /config/homeserver.yaml", compose);
        Assert.Contains("target: /config/homeserver.yaml", compose);
        Assert.DoesNotContain("target: /data/homeserver.yaml", compose);
        Assert.Contains("UID: 991", compose);
        Assert.Contains("GID: 991", compose);
    }

    [Fact]
    public void Every_service_drops_privilege_escalation()
    {
        var compose = DeployTestFiles.Read("deploy/compose.yaml");

        Assert.Equal(3, Count(compose, "no-new-privileges:true"));
    }

    [Fact]
    public void PostgreSql_18_volume_uses_the_supported_parent_mount()
    {
        var compose = DeployTestFiles.Read("deploy/compose.yaml");

        Assert.Contains("axon_postgres:/var/lib/postgresql", compose);
        Assert.DoesNotContain("axon_postgres:/var/lib/postgresql/data", compose);
    }

    private static int Count(string value, string fragment)
    {
        return value.Split(fragment, StringSplitOptions.None).Length - 1;
    }
}
