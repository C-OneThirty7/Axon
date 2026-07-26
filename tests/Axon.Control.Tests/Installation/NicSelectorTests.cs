using Axon.Control.Installation;

namespace Axon.Control.Tests.Installation;

public sealed class NicSelectorTests
{
    [Fact]
    public void Eligibility_excludes_everything_except_connected_physical_Ethernet()
    {
        var candidates = new[]
        {
            Candidate("Axon Ethernet", 7, NicKind.Ethernet),
            Candidate("Loopback", 1, NicKind.Loopback),
            Candidate("Hyper-V", 2, NicKind.Virtual),
            Candidate("Bluetooth", 3, NicKind.Bluetooth),
            Candidate("Wi-Fi", 4, NicKind.Wifi),
            Candidate("Unplugged", 5, NicKind.Ethernet) with { IsConnected = false },
            Candidate("Reported physical but virtual", 6, NicKind.Ethernet) with { IsPhysical = false }
        };

        var eligible = NicSelector.Eligible(candidates);

        Assert.Equal("Axon Ethernet", Assert.Single(eligible).Name);
    }

    [Fact]
    public void Display_contains_operator_identification_and_live_address_details()
    {
        var candidate = Candidate("USB 2.5G Ethernet", 17, NicKind.Ethernet) with
        {
            MacAddress = "A0-CE-C8-F3-53-6A",
            LinkSpeedBitsPerSecond = 2_500_000_000,
            Addresses = [new NicAddress("10.20.30.2", 24), new NicAddress("169.254.1.2", 16)]
        };

        var display = NicSelector.Describe(candidate);

        Assert.Equal("USB 2.5G Ethernet", display.Name);
        Assert.Equal(17, display.InterfaceIndex);
        Assert.Equal("A0-CE-C8-F3-53-6A", display.MacAddress);
        Assert.Equal("2.5 Gbps", display.LinkSpeed);
        Assert.Equal(["10.20.30.2/24", "169.254.1.2/16"], display.Addresses);
    }

    [Fact]
    public void Preferred_candidate_has_an_existing_usable_private_address()
    {
        var candidates = new[]
        {
            Candidate("Other Ethernet", 2, NicKind.Ethernet) with
            {
                Addresses = [new NicAddress("192.168.50.10", 24)]
            },
            Candidate("Axon Ethernet", 7, NicKind.Ethernet) with
            {
                Addresses = [new NicAddress("10.20.30.2", 24)]
            }
        };

        var preferred = NicSelector.Preferred(candidates);

        Assert.NotNull(preferred);
        Assert.Equal(2, preferred.InterfaceIndex);
    }

    [Fact]
    public void Choosing_a_candidate_does_not_mutate_or_reconfigure_it()
    {
        var original = Candidate("Axon Ethernet", 7, NicKind.Ethernet) with
        {
            Addresses = [new NicAddress("10.20.30.2", 24)]
        };

        var selection = NicSelector.Choose([original], 7);

        Assert.Same(original, selection.Candidate);
        Assert.Equal("10.20.30.2", selection.ExistingAxonAddress);
        Assert.False(selection.RequiresAddressChange);
        Assert.False(selection.AppliedChanges);
    }

    private static NicCandidate Candidate(string name, int index, NicKind kind) => new(
        Name: name,
        InterfaceIndex: index,
        IsPhysical: true,
        IsConnected: true,
        Kind: kind,
        MacAddress: "00-11-22-33-44-55",
        LinkSpeedBitsPerSecond: 1_000_000_000,
        Addresses: []);
}
