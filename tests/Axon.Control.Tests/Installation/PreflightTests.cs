using System.Runtime.InteropServices;
using Axon.Control.Installation;

namespace Axon.Control.Tests.Installation;

public sealed class PreflightTests
{
    public static IEnumerable<object[]> InvalidHosts()
    {
        yield return [Valid() with { IsWindows = false }, "windows"];
        yield return [Valid() with { Architecture = Architecture.Arm64 }, "architecture"];
        yield return [Valid() with { WindowsBuild = 22000 }, "windows-build"];
        yield return [Valid() with { VirtualizationEnabled = false }, "virtualization"];
        yield return [Valid() with { WslVersion = new Version(2, 1, 4) }, "wsl"];
        yield return [Valid() with { DockerEngineAvailable = false }, "docker"];
        yield return [Valid() with { Port80OwningProcessId = 1234 }, "port-80"];
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Capacity_shortfalls_warn_but_do_not_block(bool lowMemory, bool lowDisk)
    {
        var snapshot = Valid() with
        {
            TotalMemoryBytes = lowMemory ? 4L * 1024 * 1024 * 1024 : Valid().TotalMemoryBytes,
            FreeDiskBytes = lowDisk ? 10L * 1024 * 1024 * 1024 : Valid().FreeDiskBytes
        };

        var report = await new Preflight(new FakeProbe(snapshot)).RunAsync();

        Assert.True(report.CanInstall);
        Assert.Contains(report.Items, item =>
            item.Severity == PreflightSeverity.Warn &&
            item.Code == (lowMemory ? "memory" : "disk"));
    }

    [Theory]
    [MemberData(nameof(InvalidHosts))]
    public async Task Required_host_failures_are_explicit(HostSnapshot snapshot, string expectedCode)
    {
        var report = await new Preflight(new FakeProbe(snapshot)).RunAsync();

        Assert.False(report.CanInstall);
        var item = Assert.Single(report.Items, item => item.Code == expectedCode);
        Assert.Equal(PreflightSeverity.Fail, item.Severity);
    }

    [Fact]
    public async Task Supported_host_reports_pass_items_and_can_install()
    {
        var report = await new Preflight(new FakeProbe(Valid())).RunAsync();

        Assert.True(report.CanInstall);
        Assert.DoesNotContain(report.Items, item => item.Severity == PreflightSeverity.Fail);
        Assert.All(report.Items, item => Assert.Equal(PreflightSeverity.Pass, item.Severity));
    }

    private static HostSnapshot Valid() => new(
        IsWindows: true,
        Architecture: Architecture.X64,
        WindowsBuild: 22631,
        TotalMemoryBytes: 16L * 1024 * 1024 * 1024,
        FreeDiskBytes: 100L * 1024 * 1024 * 1024,
        VirtualizationEnabled: true,
        WslVersion: new Version(2, 1, 5),
        DockerEngineAvailable: true,
        Port80OwningProcessId: null);

    private sealed class FakeProbe(HostSnapshot snapshot) : IHostProbe
    {
        public Task<HostSnapshot> CaptureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }
}
