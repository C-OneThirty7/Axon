using Axon.Control.Configuration;

namespace Axon.Control.Tests.Configuration;

public sealed class AxonOptionsTests
{
    [Theory]
    [InlineData("10.77.77.2", 24, "10.77.77.1", true)]
    [InlineData("10.77.77.42", 24, "10.77.77.1", true)]
    [InlineData("10.77.77.254", 24, "10.77.77.1", true)]
    [InlineData("10.77.77.1", 24, "10.77.77.1", true)]
    [InlineData("192.168.1.2", 24, "10.77.77.1", true)]
    [InlineData("172.20.4.10", 24, "172.20.4.1", true)]
    [InlineData("8.8.8.8", 24, "10.77.77.1", false)]
    [InlineData("10.77.77.2", 16, "10.77.77.1", false)]
    [InlineData("10.77.77.255", 24, "10.77.77.1", false)]
    public void Network_values_accept_usable_private_subnets(
        string bindIp,
        int prefix,
        string routerIp,
        bool valid)
    {
        var options = AxonOptions.Default with
        {
            BindIp = bindIp,
            PrefixLength = prefix,
            RouterIp = routerIp
        };

        Assert.Equal(valid, options.Validate().Count == 0);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(60, true)]
    [InlineData(2880, true)]
    [InlineData(10080, true)]
    [InlineData(10081, false)]
    public void Retention_is_bounded(int minutes, bool valid)
    {
        var options = AxonOptions.Default with { RetentionMinutes = minutes };

        Assert.Equal(valid, options.Validate().Count == 0);
    }

    [Fact]
    public void Defaults_match_the_approved_design()
    {
        var options = AxonOptions.Default;

        Assert.Equal("axon.home.arpa", options.ServerName);
        Assert.Equal("10.77.77.42", options.BindIp);
        Assert.Equal(24, options.PrefixLength);
        Assert.Equal("10.77.77.1", options.RouterIp);
        Assert.Equal("LocalSubnet", options.AllowedRemoteAddress);
        Assert.Equal(80, options.ClientPort);
        Assert.Equal(8780, options.ControlPort);
        Assert.Equal(2880, options.RetentionMinutes);
        Assert.Equal("axon", options.ComposeProjectName);
        Assert.Empty(options.Validate());
    }

    [Fact]
    public void Current_DHCP_address_can_be_selected_at_install_time()
    {
        var options = AxonOptions.Default with { BindIp = "10.77.77.42" };

        Assert.Empty(options.Validate());
    }
}
