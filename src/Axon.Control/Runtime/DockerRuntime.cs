namespace Axon.Control.Runtime;

public sealed class DockerRuntime(
    ICommandRunner commandRunner,
    string bundleRoot,
    string dataRoot)
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LifecycleTimeout = TimeSpan.FromMinutes(5);

    public Task<CommandResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(["version", "--format", "{{.Server.Version}}"], cancellationToken: cancellationToken);
    }

    public Task<CommandResult> LoadImagesAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(
            ["load", "--input", Path.Combine(bundleRoot, "images", "axon-images.tar")],
            LoadTimeout,
            cancellationToken);
    }

    public Task<CommandResult> UpAsync(CancellationToken cancellationToken = default)
    {
        return RunComposeAsync(["up", "--detach", "--wait"], StartupTimeout, cancellationToken);
    }

    public Task<CommandResult> DownAsync(CancellationToken cancellationToken = default)
    {
        return RunComposeAsync(["down"], LifecycleTimeout, cancellationToken);
    }

    public Task<CommandResult> StopAsync(CancellationToken cancellationToken = default)
    {
        return RunComposeAsync(["stop"], LifecycleTimeout, cancellationToken);
    }

    public Task<CommandResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        return RunComposeAsync(["restart"], LifecycleTimeout, cancellationToken);
    }

    public Task<CommandResult> RestartServiceAsync(
        string service,
        CancellationToken cancellationToken = default)
    {
        if (service is not ("gateway" or "synapse" or "postgres"))
        {
            throw new ArgumentException("Unknown Axon service.", nameof(service));
        }
        return RunComposeAsync(["restart", service], LifecycleTimeout, cancellationToken);
    }

    public Task<CommandResult> ControlServiceAsync(
        string service,
        string action,
        CancellationToken cancellationToken = default)
    {
        if (service is not ("gateway" or "synapse" or "postgres"))
        {
            throw new ArgumentException("Unknown Axon service.", nameof(service));
        }
        return action switch
        {
            "start" => RunComposeAsync(
                ["up", "--detach", "--wait", service],
                StartupTimeout,
                cancellationToken),
            "stop" => RunComposeAsync(["stop", service], LifecycleTimeout, cancellationToken),
            "restart" => RunComposeAsync(["restart", service], LifecycleTimeout, cancellationToken),
            _ => throw new ArgumentException("Unknown service action.", nameof(action))
        };
    }

    public Task<CommandResult> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(
            [
                "stats", "--no-stream", "--format", "{{json .}}",
                "axon-postgres-1", "axon-synapse-1", "axon-gateway-1"
            ],
            LifecycleTimeout,
            cancellationToken);
    }

    public Task<CommandResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return RunComposeAsync(["ps", "--format", "json"], cancellationToken: cancellationToken);
    }

    public Task<CommandResult> GetLogsAsync(
        int lines = 200,
        CancellationToken cancellationToken = default)
    {
        return RunComposeAsync(
            ["logs", "--no-color", "--tail", Math.Clamp(lines, 20, 500).ToString(), "gateway", "synapse", "postgres"],
            LifecycleTimeout,
            cancellationToken);
    }

    private Task<CommandResult> RunComposeAsync(
        IReadOnlyList<string> operation,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string>
        {
            "compose",
            "--project-name", "axon",
            "--env-file", Path.Combine(dataRoot, ".env"),
            "--file", Path.Combine(bundleRoot, "deploy", "compose.yaml")
        };
        arguments.AddRange(operation);
        return RunAsync(arguments, timeout, cancellationToken);
    }

    private Task<CommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return commandRunner.RunAsync(
            new CommandRequest("docker", arguments, timeout),
            cancellationToken);
    }
}
