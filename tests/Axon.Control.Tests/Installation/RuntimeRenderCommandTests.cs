using Axon.Control.Installation;

namespace Axon.Control.Tests.Installation;

public sealed class RuntimeRenderCommandTests
{
    [Fact]
    public void Parses_complete_runtime_render_arguments()
    {
        var command = RuntimeRenderCommand.Parse(
        [
            "/bundle",
            "/data",
            "10.77.77.42",
            $"synapse@sha256:{new string('a', 64)}",
            $"postgres@sha256:{new string('b', 64)}",
            $"nginx@sha256:{new string('c', 64)}"
        ]);

        Assert.Equal("/bundle", command.BundleRoot);
        Assert.Equal("/data", command.DataRoot);
        Assert.Equal("10.77.77.42", command.Options.BindIp);
        Assert.StartsWith("synapse@sha256:", command.Images.Synapse);
    }

    [Fact]
    public void Rejects_missing_arguments()
    {
        var error = Assert.Throws<ArgumentException>(() => RuntimeRenderCommand.Parse(["/bundle"]));

        Assert.Contains("six arguments", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
