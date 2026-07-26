using System.Diagnostics;

namespace Axon.Control.Runtime;

public sealed record CommandRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan? Timeout = null,
    IReadOnlyCollection<string>? Secrets = null);

public sealed record CommandResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    TimeSpan Duration,
    bool TimedOut);

public interface ICommandRunner
{
    Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default);
}

public sealed class CommandRunner : ICommandRunner
{
    public static TimeSpan DefaultTimeout { get; } = TimeSpan.FromSeconds(30);

    public async Task<CommandResult> RunAsync(
        CommandRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Command timeout must be positive.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start {request.FileName}.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken: CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken: CancellationToken.None);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(combinedCancellation.Token);
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        stopwatch.Stop();
        return new CommandResult(
            process.ExitCode,
            Redact(stdout, request.Secrets),
            Redact(stderr, request.Secrets),
            stopwatch.Elapsed,
            timedOut);
    }

    private static void KillProcessTree(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    private static string Redact(string value, IReadOnlyCollection<string>? secrets)
    {
        if (secrets is null || secrets.Count == 0)
        {
            return value;
        }

        var redacted = value;
        foreach (var secret in secrets.Where(secret => !string.IsNullOrEmpty(secret)).OrderByDescending(secret => secret.Length))
        {
            redacted = redacted.Replace(secret, "***REDACTED***", StringComparison.Ordinal);
        }

        return redacted;
    }
}
