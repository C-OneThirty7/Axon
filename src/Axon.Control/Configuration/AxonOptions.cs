using System.Net;

namespace Axon.Control.Configuration;

public sealed record AxonOptions
{
    public string ServerName { get; init; } = "axon.home.arpa";

    public string BindIp { get; init; } = "10.77.77.42";

    public int PrefixLength { get; init; } = 24;

    public string RouterIp { get; init; } = "10.77.77.1";

    public string AllowedRemoteAddress { get; init; } = "LocalSubnet";

    public int ClientPort { get; init; } = 80;

    public int ControlPort { get; init; } = 8780;

    public int RetentionMinutes { get; init; } = 2880;

    public string ComposeProjectName { get; init; } = "axon";

    public static AxonOptions Default => new();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!string.Equals(ServerName, "axon.home.arpa", StringComparison.Ordinal))
        {
            errors.Add("ServerName must remain axon.home.arpa.");
        }

        if (!IPAddress.TryParse(BindIp, out var bindIp) ||
            bindIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !IsPrivate(bindIp) ||
            bindIp.GetAddressBytes()[3] is 0 or 255)
        {
            errors.Add("BindIp must be a usable private IPv4 host address.");
        }

        if (PrefixLength != 24)
        {
            errors.Add("PrefixLength must be 24 (255.255.255.0).");
        }

        if (!IPAddress.TryParse(RouterIp, out var routerIp) ||
            routerIp.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            errors.Add("RouterIp must be an IPv4 address.");
        }

        if (string.IsNullOrWhiteSpace(AllowedRemoteAddress))
        {
            errors.Add("AllowedRemoteAddress is required.");
        }

        if (ClientPort != 80)
        {
            errors.Add("ClientPort must be 80 for the HTTP proof of concept.");
        }

        if (ControlPort is < 1024 or > 65535)
        {
            errors.Add("ControlPort must be between 1024 and 65535.");
        }

        if (RetentionMinutes is < 60 or > 10080)
        {
            errors.Add("RetentionMinutes must be between 60 minutes and 7 days.");
        }

        if (!string.Equals(ComposeProjectName, "axon", StringComparison.Ordinal))
        {
            errors.Add("ComposeProjectName must be axon.");
        }

        return errors;
    }

    private static bool IsPrivate(IPAddress address)
    {
        return address.GetAddressBytes() switch
        {
            [10, _, _, _] => true,
            [172, >= 16 and <= 31, _, _] => true,
            [192, 168, _, _] => true,
            _ => false
        };
    }
}
