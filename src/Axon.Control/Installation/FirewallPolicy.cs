using Axon.Control.Configuration;

namespace Axon.Control.Installation;

public enum FirewallDirection
{
    Inbound,
    Outbound
}

public enum FirewallAction
{
    Allow,
    Block
}

public sealed record FirewallRulePlan(
    string DisplayName,
    string Group,
    FirewallDirection Direction,
    string Protocol,
    int LocalPort,
    string LocalAddress,
    string RemoteAddress,
    string InterfaceAlias,
    string Profile,
    FirewallAction Action);

public sealed record FirewallPolicy(IReadOnlyList<FirewallRulePlan> Rules)
{
    public static FirewallPolicy Create(AxonOptions options, string interfaceAlias)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceAlias);
        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, errors), nameof(options));
        }

        return new FirewallPolicy(
        [
            new FirewallRulePlan(
                DisplayName: "Axon Matrix LAN",
                Group: "Axon",
                Direction: FirewallDirection.Inbound,
                Protocol: "TCP",
                LocalPort: options.ClientPort,
                LocalAddress: options.BindIp,
                RemoteAddress: options.AllowedRemoteAddress,
                InterfaceAlias: interfaceAlias,
                Profile: "Private",
                Action: FirewallAction.Allow)
        ]);
    }
}
