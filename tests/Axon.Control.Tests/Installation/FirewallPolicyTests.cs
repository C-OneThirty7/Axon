using Axon.Control.Configuration;
using Axon.Control.Installation;

namespace Axon.Control.Tests.Installation;

public sealed class FirewallPolicyTests
{
    [Fact]
    public void Creates_only_the_exact_Matrix_LAN_rule()
    {
        var options = AxonOptions.Default with { BindIp = "10.77.77.42" };

        var policy = FirewallPolicy.Create(options, "USB Ethernet");

        var rule = Assert.Single(policy.Rules);
        Assert.Equal("Axon Matrix LAN", rule.DisplayName);
        Assert.Equal("Axon", rule.Group);
        Assert.Equal(FirewallDirection.Inbound, rule.Direction);
        Assert.Equal("TCP", rule.Protocol);
        Assert.Equal(80, rule.LocalPort);
        Assert.Equal("10.77.77.42", rule.LocalAddress);
        Assert.Equal("LocalSubnet", rule.RemoteAddress);
        Assert.Equal("USB Ethernet", rule.InterfaceAlias);
        Assert.Equal("Private", rule.Profile);
        Assert.Equal(FirewallAction.Allow, rule.Action);
        Assert.DoesNotContain(policy.Rules, candidate => candidate.Protocol == "UDP");
        Assert.DoesNotContain(policy.Rules, candidate => candidate.LocalPort is 8008 or 8780 or 5432);
    }

    [Fact]
    public void Rejects_an_invalid_or_unscoped_interface()
    {
        Assert.Throws<ArgumentException>(() => FirewallPolicy.Create(AxonOptions.Default, " "));
    }
}
