using System.Runtime.InteropServices;

namespace Axon.Control.Installation;

public enum PreflightSeverity
{
    Pass,
    Warn,
    Fail
}

public sealed record PreflightItem(string Code, PreflightSeverity Severity, string Message);

public sealed record PreflightReport(IReadOnlyList<PreflightItem> Items)
{
    public bool CanInstall => Items.All(item => item.Severity != PreflightSeverity.Fail);
}

public sealed record HostSnapshot(
    bool IsWindows,
    Architecture Architecture,
    int WindowsBuild,
    long TotalMemoryBytes,
    long FreeDiskBytes,
    bool VirtualizationEnabled,
    Version? WslVersion,
    bool DockerEngineAvailable,
    int? Port80OwningProcessId);

public interface IHostProbe
{
    Task<HostSnapshot> CaptureAsync(CancellationToken cancellationToken = default);
}

public sealed class Preflight(IHostProbe hostProbe)
{
    public const int MinimumWindowsBuild = 22631;
    public const long MinimumMemoryBytes = 16L * 1024 * 1024 * 1024;
    public const long MinimumFreeDiskBytes = 100L * 1024 * 1024 * 1024;
    public static Version MinimumWslVersion { get; } = new(2, 1, 5);

    public async Task<PreflightReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var host = await hostProbe.CaptureAsync(cancellationToken);
        var items = new List<PreflightItem>
        {
            Evaluate("windows", host.IsWindows, "Windows 11 is available.", "Axon requires Windows 11."),
            Evaluate(
                "architecture",
                host.Architecture == Architecture.X64,
                "The host architecture is x64.",
                $"Axon requires x64; detected {host.Architecture}."),
            Evaluate(
                "windows-build",
                host.WindowsBuild >= MinimumWindowsBuild,
                $"Windows build {host.WindowsBuild} meets the minimum.",
                $"Windows build {MinimumWindowsBuild} or newer is required; detected {host.WindowsBuild}."),
            EvaluateCapacity(
                "memory",
                host.TotalMemoryBytes >= MinimumMemoryBytes,
                "At least 16 GiB of RAM is available.",
                "Less than 16 GiB RAM is available; installation may support fewer concurrent users."),
            EvaluateCapacity(
                "disk",
                host.FreeDiskBytes >= MinimumFreeDiskBytes,
                "At least 100 GiB of free disk is available.",
                "Less than 100 GiB free disk is available; monitor storage headroom."),
            Evaluate(
                "virtualization",
                host.VirtualizationEnabled,
                "Hardware virtualization is enabled.",
                "Hardware virtualization must be enabled in firmware."),
            Evaluate(
                "wsl",
                host.WslVersion is not null && host.WslVersion >= MinimumWslVersion,
                $"WSL {host.WslVersion} meets the minimum.",
                $"WSL {MinimumWslVersion} or newer is required; detected {host.WslVersion?.ToString() ?? "none"}."),
            Evaluate(
                "docker",
                host.DockerEngineAvailable,
                "Docker Engine is available.",
                "Docker Desktop's Linux engine is unavailable."),
            Evaluate(
                "port-80",
                host.Port80OwningProcessId is null,
                "TCP port 80 is available.",
                $"TCP port 80 is owned by process {host.Port80OwningProcessId}.")
        };

        return new PreflightReport(items);
    }

    private static PreflightItem Evaluate(string code, bool passed, string success, string failure) =>
        new(code, passed ? PreflightSeverity.Pass : PreflightSeverity.Fail, passed ? success : failure);

    private static PreflightItem EvaluateCapacity(string code, bool passed, string success, string warning) =>
        new(code, passed ? PreflightSeverity.Pass : PreflightSeverity.Warn, passed ? success : warning);
}
