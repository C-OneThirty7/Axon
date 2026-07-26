using Axon.Control.Runtime;

namespace Axon.Control.Tests.Runtime;

public sealed class DockerRuntimeTests
{
    private readonly RecordingRunner _runner = new();
    private readonly string _bundle = Path.Combine("root", "bundle");
    private readonly string _data = Path.Combine("root", "data");

    [Fact]
    public async Task Emits_exact_safe_Docker_commands()
    {
        var runtime = new DockerRuntime(_runner, _bundle, _data);

        await runtime.CheckAsync();
        await runtime.LoadImagesAsync();
        await runtime.UpAsync();
        await runtime.GetStatusAsync();

        Assert.Collection(
            _runner.Requests,
            request => AssertRequest(request, "docker", "version", "--format", "{{.Server.Version}}"),
            request => AssertRequest(request, "docker", "load", "--input", Path.Combine(_bundle, "images", "axon-images.tar")),
            request => AssertRequest(
                request,
                "docker",
                "compose", "--project-name", "axon", "--env-file", Path.Combine(_data, ".env"),
                "--file", Path.Combine(_bundle, "deploy", "compose.yaml"), "up", "--detach", "--wait"),
            request => AssertRequest(
                request,
                "docker",
                "compose", "--project-name", "axon", "--env-file", Path.Combine(_data, ".env"),
                "--file", Path.Combine(_bundle, "deploy", "compose.yaml"), "ps", "--format", "json"));
    }

    [Fact]
    public async Task Lifecycle_commands_use_the_same_scoped_compose_project()
    {
        var runtime = new DockerRuntime(_runner, _bundle, _data);

        await runtime.RestartAsync();
        await runtime.StopAsync();
        await runtime.DownAsync();
        await runtime.ControlServiceAsync("synapse", "start");
        await runtime.ControlServiceAsync("gateway", "stop");

        Assert.Equal("restart", _runner.Requests[0].Arguments[^1]);
        Assert.Equal("stop", _runner.Requests[1].Arguments[^1]);
        Assert.Equal("down", _runner.Requests[2].Arguments[^1]);
        Assert.Equal(
            ["up", "--detach", "--wait", "synapse"],
            _runner.Requests[3].Arguments.Skip(_runner.Requests[3].Arguments.Count - 4));
        Assert.Equal(
            ["stop", "gateway"],
            _runner.Requests[4].Arguments.Skip(_runner.Requests[4].Arguments.Count - 2));
        Assert.All(_runner.Requests, request =>
        {
            Assert.Contains("--project-name", request.Arguments);
            Assert.Contains("axon", request.Arguments);
        });
    }

    [Fact]
    public async Task Rejects_unknown_services_and_actions()
    {
        var runtime = new DockerRuntime(_runner, _bundle, _data);

        await Assert.ThrowsAsync<ArgumentException>(() => runtime.ControlServiceAsync("redis", "start"));
        await Assert.ThrowsAsync<ArgumentException>(() => runtime.ControlServiceAsync("synapse", "delete"));
        Assert.Empty(_runner.Requests);
    }

    private static void AssertRequest(CommandRequest request, string executable, params string[] arguments)
    {
        Assert.Equal(executable, request.FileName);
        Assert.Equal(arguments, request.Arguments);
    }

    private sealed class RecordingRunner : ICommandRunner
    {
        public List<CommandRequest> Requests { get; } = [];

        public Task<CommandResult> RunAsync(CommandRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new CommandResult(0, string.Empty, string.Empty, TimeSpan.Zero, false));
        }
    }
}
