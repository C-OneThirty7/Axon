namespace Axon.Control.Tests.Deploy;

public sealed class NginxContractTests
{
    [Fact]
    public void Gateway_allows_client_paths_and_hides_administration()
    {
        var nginx = DeployTestFiles.Read("deploy/nginx/default.conf.template");

        Assert.Contains("location ^~ /_synapse/admin", nginx);
        Assert.Contains("return 404", nginx);
        Assert.Contains("location ^~ /_matrix/", nginx);
        Assert.Contains("location ^~ /_synapse/client/", nginx);
        Assert.Contains("proxy_pass http://synapse:8008", nginx);
        Assert.Contains("client_max_body_size 64k", nginx);
        Assert.Contains("access_log off", nginx);
        Assert.Contains("proxy_set_header X-Forwarded-For $remote_addr", nginx);
        Assert.DoesNotContain("$proxy_add_x_forwarded_for", nginx);
        Assert.Contains("X-Content-Type-Options", nginx);
        Assert.Contains("location = /axon-health", nginx);
    }
}
