namespace Axon.Control.Tests.Deploy;

public sealed class SynapseContractTests
{
    [Fact]
    public void Homeserver_is_private_text_only_and_ephemeral()
    {
        var synapse = DeployTestFiles.Read("deploy/synapse/homeserver.yaml.template");

        Assert.Contains("server_name: \"axon.home.arpa\"", synapse);
        Assert.Contains("public_baseurl: \"http://${AXON_BIND_IP}/\"", synapse);
        Assert.Contains("enable_registration: false", synapse);
        Assert.Contains("enable_media_repo: false", synapse);
        Assert.Contains("report_stats: false", synapse);
        Assert.Contains("url_preview_enabled: false", synapse);
        Assert.Contains("push:", synapse);
        Assert.Contains("enabled: false", synapse);
        Assert.Contains("federation_domain_whitelist: []", synapse);
        Assert.Contains("encryption_enabled_by_default_for_room_type: all", synapse);
        Assert.Contains("rc_login:", synapse);
        Assert.Contains("max_lifetime: 48h", synapse);
        Assert.Contains("interval: 1h", synapse);
        Assert.Contains("names: [client]", synapse);
        Assert.DoesNotContain("names: [federation]", synapse);
    }
}
