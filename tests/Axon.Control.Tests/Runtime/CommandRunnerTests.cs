using System.Diagnostics;
using Axon.Control.Runtime;

namespace Axon.Control.Tests.Runtime;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task Captures_stdout_from_argument_list_without_a_shell()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await new CommandRunner().RunAsync(
            new CommandRequest("/usr/bin/printf", ["%s", "Axon output"]));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Axon output", result.StdOut);
        Assert.Empty(result.StdErr);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task Captures_nonzero_exit_code()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var result = await new CommandRunner().RunAsync(new CommandRequest("/usr/bin/false", []));

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public void Default_timeout_is_thirty_seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), CommandRunner.DefaultTimeout);
    }

    [Fact]
    public async Task Timeout_kills_the_process_and_returns_a_timed_out_result()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await new CommandRunner().RunAsync(
            new CommandRequest("/bin/sleep", ["10"], TimeSpan.FromMilliseconds(100)));

        Assert.True(result.TimedOut);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Redacts_secrets_from_stdout_and_stderr()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const string secret = "axon-super-secret";
        var result = await new CommandRunner().RunAsync(
            new CommandRequest("/bin/sh", ["-c", "printf '%s' \"$1\"; printf '%s' \"$1\" >&2", "axon", secret], Secrets: [secret]));

        Assert.DoesNotContain(secret, result.StdOut);
        Assert.DoesNotContain(secret, result.StdErr);
        Assert.Contains("***REDACTED***", result.StdOut);
        Assert.Contains("***REDACTED***", result.StdErr);
    }

    [Fact]
    public async Task External_cancellation_terminates_the_process()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CommandRunner().RunAsync(
                new CommandRequest("/bin/sleep", ["10"]),
                cancellation.Token));
    }
}
