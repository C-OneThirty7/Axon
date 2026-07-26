using Axon.Control.Configuration;
using Axon.Control.Installation;
using Axon.Control.Security;

namespace Axon.Control.Tests.Installation;

public sealed class RuntimeRendererTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"axon-render-{Guid.NewGuid():N}");

    [Fact]
    public async Task Render_writes_complete_runtime_beneath_data_root()
    {
        var bundleRoot = Path.Combine(_root, "bundle");
        var dataRoot = Path.Combine(_root, "data");
        Directory.CreateDirectory(Path.Combine(bundleRoot, "deploy", "synapse"));
        Directory.CreateDirectory(Path.Combine(bundleRoot, "deploy", "nginx"));
        await File.WriteAllTextAsync(
            Path.Combine(bundleRoot, "deploy", "synapse", "homeserver.yaml.template"),
            "url=http://${AXON_BIND_IP}/ db=${POSTGRES_PASSWORD} reg=${REGISTRATION_SHARED_SECRET} mac=${MACAROON_SECRET_KEY} form=${FORM_SECRET}");
        await File.WriteAllTextAsync(
            Path.Combine(bundleRoot, "deploy", "nginx", "default.conf.template"),
            "server { listen 80; }");
        var images = new RuntimeImages(
            $"matrixdotorg/synapse@sha256:{new string('a', 64)}",
            $"postgres@sha256:{new string('b', 64)}",
            $"nginx@sha256:{new string('c', 64)}");
        var options = AxonOptions.Default with { BindIp = "10.77.77.42" };

        await new RuntimeRenderer(new SecretGenerator()).RenderAsync(
            bundleRoot,
            dataRoot,
            options,
            images,
            CancellationToken.None);

        var homeserverPath = Path.Combine(dataRoot, "runtime", "synapse", "homeserver.yaml");
        var nginxPath = Path.Combine(dataRoot, "runtime", "nginx", "default.conf");
        var envPath = Path.Combine(dataRoot, ".env");
        Assert.True(File.Exists(homeserverPath));
        Assert.True(File.Exists(nginxPath));
        Assert.True(File.Exists(envPath));
        Assert.DoesNotContain("${", await File.ReadAllTextAsync(homeserverPath));
        Assert.Contains("http://10.77.77.42/", await File.ReadAllTextAsync(homeserverPath));
        var env = await File.ReadAllTextAsync(envPath);
        Assert.Contains("AXON_BIND_IP=10.77.77.42", env);
        Assert.Contains(images.Synapse, env);
        Assert.Contains(images.Postgres, env);
        Assert.Contains(images.Nginx, env);
        Assert.DoesNotContain("${", env);
        Assert.Empty(Directory.EnumerateDirectories(dataRoot, ".staging-*"));
    }

    [Fact]
    public async Task Render_rejects_unresolved_tokens_before_installing_files()
    {
        var bundleRoot = Path.Combine(_root, "bad-bundle");
        var dataRoot = Path.Combine(_root, "bad-data");
        Directory.CreateDirectory(Path.Combine(bundleRoot, "deploy", "synapse"));
        Directory.CreateDirectory(Path.Combine(bundleRoot, "deploy", "nginx"));
        await File.WriteAllTextAsync(
            Path.Combine(bundleRoot, "deploy", "synapse", "homeserver.yaml.template"),
            "unknown=${UNKNOWN_TOKEN}");
        await File.WriteAllTextAsync(
            Path.Combine(bundleRoot, "deploy", "nginx", "default.conf.template"),
            "server {}");
        var images = new RuntimeImages(
            $"synapse@sha256:{new string('a', 64)}",
            $"postgres@sha256:{new string('b', 64)}",
            $"nginx@sha256:{new string('c', 64)}");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new RuntimeRenderer(new SecretGenerator()).RenderAsync(
                bundleRoot,
                dataRoot,
                AxonOptions.Default,
                images,
                CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(dataRoot, "runtime", "synapse", "homeserver.yaml")));
    }

    [Fact]
    public async Task Render_rejects_image_references_without_a_digest()
    {
        var images = new RuntimeImages("synapse:latest", "postgres:latest", "nginx:latest");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new RuntimeRenderer(new SecretGenerator()).RenderAsync(
                _root,
                Path.Combine(_root, "data"),
                AxonOptions.Default,
                images,
                CancellationToken.None));
    }

    [Fact]
    public async Task Render_accepts_offline_bundle_tags_derived_from_upstream_digests()
    {
        var bundleRoot = Path.Combine(_root, "offline-bundle");
        Directory.CreateDirectory(Path.Combine(bundleRoot, "deploy", "synapse"));
        Directory.CreateDirectory(Path.Combine(bundleRoot, "deploy", "nginx"));
        await File.WriteAllTextAsync(
            Path.Combine(bundleRoot, "deploy", "synapse", "homeserver.yaml.template"),
            "db=${POSTGRES_PASSWORD} reg=${REGISTRATION_SHARED_SECRET} mac=${MACAROON_SECRET_KEY} form=${FORM_SECRET}");
        await File.WriteAllTextAsync(
            Path.Combine(bundleRoot, "deploy", "nginx", "default.conf.template"),
            "server {}");
        var images = new RuntimeImages(
            $"axon.local/synapse:sha256-{new string('a', 64)}",
            $"axon.local/postgres:sha256-{new string('b', 64)}",
            $"axon.local/nginx:sha256-{new string('c', 64)}");

        var result = await new RuntimeRenderer(new SecretGenerator()).RenderAsync(
            bundleRoot,
            Path.Combine(_root, "offline-data"),
            AxonOptions.Default,
            images,
            CancellationToken.None);

        var environment = await File.ReadAllTextAsync(result.EnvironmentPath);
        Assert.Contains(images.Synapse, environment);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
