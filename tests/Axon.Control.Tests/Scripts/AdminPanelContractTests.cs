using Axon.Control.Tests.Deploy;

namespace Axon.Control.Tests.Scripts;

public sealed class AdminPanelContractTests
{
    [Fact]
    public void Panel_is_host_only_and_supports_user_batches_and_operations()
    {
        var program = DeployTestFiles.Read("src/Axon.Control/Program.cs");
        var html = DeployTestFiles.Read("src/Axon.Control/wwwroot/index.html");
        var javascript = DeployTestFiles.Read("src/Axon.Control/wwwroot/app.js");
        var css = DeployTestFiles.Read("src/Axon.Control/wwwroot/app.css");

        Assert.Contains("ListenLocalhost(8780)", program);
        Assert.Contains("Count is < 1 or > 200", program);
        Assert.Contains("requireNew: true", program);
        Assert.Contains("OperatorSessions", program);
        Assert.Contains("Content-Security-Policy", program);
        Assert.Contains("127.0.0.1:8780", program);
        Assert.Contains("minlength=\"10\"", html);
        Assert.DoesNotContain("value=\"example-password\"", html);
        Assert.Contains("Batch issue", html);
        Assert.Contains("Rooms", html);
        Assert.Contains("Create room", html);
        Assert.Contains("Room members", html);
        Assert.Contains("Pause all services", html);
        Assert.Contains("Download issued-account CSV", html);
        Assert.Contains("stock_password", javascript);
        Assert.Contains("Current admin", javascript);
        Assert.Contains("/api/rooms", javascript);
        Assert.Contains("Recently active", javascript);
        Assert.Contains("data-service-action", javascript);
        Assert.Contains("Take room control", javascript);
        Assert.Contains("MapGet(\"/api/rooms\"", program);
        Assert.Contains("MapDelete(\"/api/rooms/{roomId}\"", program);
        Assert.Contains("MapPost(\"/api/rooms/{roomId}/members\"", program);
        Assert.Contains("MapGet(\"/api/update\"", program);
        Assert.Contains("MapPost(\"/api/update/download\"", program);
        Assert.Contains("MapPost(\"/api/update/install\"", program);
        Assert.Contains("Check for updates", html);
        Assert.Contains("Download and verify", html);
        Assert.Contains("Install update", html);
        Assert.Contains("include-prereleases", html);
        Assert.Contains("/api/update?includePrereleases=", javascript);
        Assert.Contains("/api/update/download?includePrereleases=", javascript);
        Assert.Contains("/api/update/install", javascript);
        Assert.Contains("github.com", javascript);
        Assert.DoesNotContain("setInterval", javascript);
        Assert.DoesNotMatch(
            @"boot\(\)[\s\S]*checkForUpdates\(",
            javascript);
        Assert.Contains("[hidden] { display: none !important; }", css);
        Assert.DoesNotContain("cdn.", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
    }
}
