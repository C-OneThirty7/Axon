namespace Axon.Control.Installation;

public sealed record NetworkPlan(
    string InterfaceAlias,
    int InterfaceIndex,
    string Address,
    int PrefixLength,
    string? Gateway,
    IReadOnlyList<string> DnsServers,
    IReadOnlyList<string> AddressesToRemove,
    bool RequiresAddressChange,
    bool RequiresOperatorConfirmation,
    bool AppliedChanges)
{
    public static NetworkPlan Create(
        NicCandidate candidate,
        string desiredAddress,
        int prefixLength,
        bool operatorConfirmed)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsPhysical || !candidate.IsConnected || candidate.Kind != NicKind.Ethernet)
        {
            throw new ArgumentException("The selected adapter must be connected physical Ethernet.", nameof(candidate));
        }

        var desired = new NicAddress(desiredAddress, prefixLength);
        if (!NicSelector.IsUsableAxonAddress(desired))
        {
            throw new ArgumentException(
                "The desired address must be a usable private IPv4 /24 host address.",
                nameof(desiredAddress));
        }

        var alreadyConfigured = candidate.Addresses.Any(address =>
            string.Equals(address.IpAddress, desiredAddress, StringComparison.Ordinal) &&
            address.PrefixLength == prefixLength);
        var addressesToRemove = !alreadyConfigured && operatorConfirmed
            ? candidate.Addresses
                .Where(address => !string.Equals(address.IpAddress, desiredAddress, StringComparison.Ordinal))
                .Select(address => address.IpAddress)
                .ToArray()
            : [];

        return new NetworkPlan(
            candidate.Name,
            candidate.InterfaceIndex,
            desiredAddress,
            prefixLength,
            Gateway: null,
            DnsServers: [],
            AddressesToRemove: addressesToRemove,
            RequiresAddressChange: !alreadyConfigured,
            RequiresOperatorConfirmation: !alreadyConfigured && !operatorConfirmed,
            AppliedChanges: false);
    }
}
