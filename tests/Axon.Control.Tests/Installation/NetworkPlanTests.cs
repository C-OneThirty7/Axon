using Axon.Control.Installation;

namespace Axon.Control.Tests.Installation;

public sealed class NetworkPlanTests
{
    [Fact]
    public void Preserves_an_existing_matching_static_address()
    {
        var candidate = Ethernet([new NicAddress("10.77.77.42", 24)]);

        var plan = NetworkPlan.Create(candidate, "10.77.77.42", 24, operatorConfirmed: false);

        Assert.False(plan.RequiresAddressChange);
        Assert.False(plan.RequiresOperatorConfirmation);
        Assert.Empty(plan.AddressesToRemove);
        Assert.Null(plan.Gateway);
        Assert.Empty(plan.DnsServers);
        Assert.False(plan.AppliedChanges);
    }

    [Fact]
    public void Proposed_change_requires_confirmation_and_does_not_mutate_the_candidate()
    {
        var addresses = new[] { new NicAddress("192.168.50.4", 24) };
        var candidate = Ethernet(addresses);

        var plan = NetworkPlan.Create(candidate, "10.77.77.42", 24, operatorConfirmed: false);

        Assert.True(plan.RequiresAddressChange);
        Assert.True(plan.RequiresOperatorConfirmation);
        Assert.Empty(plan.AddressesToRemove);
        Assert.Same(addresses, candidate.Addresses);
        Assert.False(plan.AppliedChanges);
    }

    [Fact]
    public void Confirmed_plan_targets_only_conflicting_addresses_on_selected_adapter()
    {
        var candidate = Ethernet(
            [new NicAddress("192.168.50.4", 24), new NicAddress("169.254.4.5", 16)]);

        var plan = NetworkPlan.Create(candidate, "10.77.77.42", 24, operatorConfirmed: true);

        Assert.Equal(17, plan.InterfaceIndex);
        Assert.Equal("USB Ethernet", plan.InterfaceAlias);
        Assert.Equal("10.77.77.42", plan.Address);
        Assert.Equal(24, plan.PrefixLength);
        Assert.Equal(["192.168.50.4", "169.254.4.5"], plan.AddressesToRemove);
        Assert.Null(plan.Gateway);
        Assert.Empty(plan.DnsServers);
        Assert.False(plan.AppliedChanges);
    }

    [Fact]
    public void Rejects_network_broadcast_and_public_addresses()
    {
        var candidate = Ethernet([]);

        Assert.Throws<ArgumentException>(() => NetworkPlan.Create(candidate, "10.77.77.255", 24, true));
        Assert.Throws<ArgumentException>(() => NetworkPlan.Create(candidate, "8.8.8.8", 24, true));
    }

    private static NicCandidate Ethernet(IReadOnlyList<NicAddress> addresses) => new(
        Name: "USB Ethernet",
        InterfaceIndex: 17,
        IsPhysical: true,
        IsConnected: true,
        Kind: NicKind.Ethernet,
        MacAddress: "AA-BB-CC-DD-EE-FF",
        LinkSpeedBitsPerSecond: 1_000_000_000,
        Addresses: addresses);
}
