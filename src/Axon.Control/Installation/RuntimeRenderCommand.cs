using Axon.Control.Configuration;

namespace Axon.Control.Installation;

public sealed record RuntimeRenderCommand(
    string BundleRoot,
    string DataRoot,
    AxonOptions Options,
    RuntimeImages Images)
{
    public static RuntimeRenderCommand Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count != 6)
        {
            throw new ArgumentException(
                "render-runtime requires six arguments: bundle root, data root, bind IP, Synapse image, PostgreSQL image, and nginx image.",
                nameof(arguments));
        }

        return new RuntimeRenderCommand(
            arguments[0],
            arguments[1],
            AxonOptions.Default with { BindIp = arguments[2] },
            new RuntimeImages(arguments[3], arguments[4], arguments[5]));
    }

    public Task<RuntimeRenderResult> ExecuteAsync(
        IRuntimeRenderer renderer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        return renderer.RenderAsync(BundleRoot, DataRoot, Options, Images, cancellationToken);
    }
}
