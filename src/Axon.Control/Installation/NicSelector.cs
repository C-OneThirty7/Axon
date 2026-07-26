using System.Globalization;
using System.Net;

namespace Axon.Control.Installation;

public enum NicKind
{
    Ethernet,
    Wifi,
    Bluetooth,
    Loopback,
    Virtual,
    Other
}

public sealed record NicAddress(string IpAddress, int PrefixLength);

public sealed record NicCandidate(
    string Name,
    int InterfaceIndex,
    bool IsPhysical,
    bool IsConnected,
    NicKind Kind,
    string MacAddress,
    long LinkSpeedBitsPerSecond,
    IReadOnlyList<NicAddress> Addresses);

public sealed record NicDisplay(
    string Name,
    int InterfaceIndex,
    string MacAddress,
    string LinkSpeed,
    IReadOnlyList<string> Addresses);

public sealed record NicSelection(
    NicCandidate Candidate,
    string? ExistingAxonAddress,
    bool RequiresAddressChange,
    bool AppliedChanges);

public static class NicSelector
{
    public static IReadOnlyList<NicCandidate> Eligible(IEnumerable<NicCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .Where(candidate =>
                candidate.IsPhysical &&
                candidate.IsConnected &&
                candidate.Kind == NicKind.Ethernet)
            .OrderBy(candidate => candidate.InterfaceIndex)
            .ToArray();
    }

    public static NicCandidate? Preferred(IEnumerable<NicCandidate> candidates)
    {
        return Eligible(candidates).FirstOrDefault(candidate =>
            candidate.Addresses.Any(IsUsableAxonAddress));
    }

    public static NicSelection Choose(IEnumerable<NicCandidate> candidates, int interfaceIndex)
    {
        var candidate = Eligible(candidates).SingleOrDefault(item => item.InterfaceIndex == interfaceIndex)
            ?? throw new ArgumentException($"Interface {interfaceIndex} is not an eligible physical Ethernet adapter.", nameof(interfaceIndex));
        var existing = candidate.Addresses.FirstOrDefault(IsUsableAxonAddress)?.IpAddress;
        return new NicSelection(
            candidate,
            existing,
            RequiresAddressChange: existing is null,
            AppliedChanges: false);
    }

    public static NicDisplay Describe(NicCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new NicDisplay(
            candidate.Name,
            candidate.InterfaceIndex,
            candidate.MacAddress,
            FormatLinkSpeed(candidate.LinkSpeedBitsPerSecond),
            candidate.Addresses.Select(address => $"{address.IpAddress}/{address.PrefixLength}").ToArray());
    }

    public static bool IsUsableAxonAddress(NicAddress address)
    {
        if (address.PrefixLength != 24 || !IPAddress.TryParse(address.IpAddress, out var parsed))
        {
            return false;
        }

        var bytes = parsed.GetAddressBytes();
        var privateAddress = bytes switch
        {
            [10, _, _, _] => true,
            [172, >= 16 and <= 31, _, _] => true,
            [192, 168, _, _] => true,
            _ => false
        };
        return privateAddress && bytes[3] is >= 1 and <= 254;
    }

    private static string FormatLinkSpeed(long bitsPerSecond)
    {
        if (bitsPerSecond >= 1_000_000_000)
        {
            return $"{bitsPerSecond / 1_000_000_000d:0.#} Gbps";
        }

        if (bitsPerSecond >= 1_000_000)
        {
            return $"{bitsPerSecond / 1_000_000d:0.#} Mbps";
        }

        return bitsPerSecond.ToString(CultureInfo.InvariantCulture) + " bps";
    }
}
