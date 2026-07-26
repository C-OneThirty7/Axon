using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Axon.Control.Configuration;
using Axon.Control.Security;

namespace Axon.Control.Installation;

public sealed record RuntimeImages(string Synapse, string Postgres, string Nginx);

public sealed record RuntimeRenderResult(string EnvironmentPath, string HomeserverPath, string NginxPath);

public interface IRuntimeRenderer
{
    Task<RuntimeRenderResult> RenderAsync(
        string bundleRoot,
        string dataRoot,
        AxonOptions options,
        RuntimeImages images,
        CancellationToken cancellationToken = default);
}

public sealed partial class RuntimeRenderer(SecretGenerator secretGenerator) : IRuntimeRenderer
{
    public async Task<RuntimeRenderResult> RenderAsync(
        string bundleRoot,
        string dataRoot,
        AxonOptions options,
        RuntimeImages images,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(images);

        var configurationErrors = options.Validate();
        if (configurationErrors.Count > 0)
        {
            throw new ArgumentException(string.Join(Environment.NewLine, configurationErrors), nameof(options));
        }

        ValidateImage(images.Synapse, nameof(images.Synapse));
        ValidateImage(images.Postgres, nameof(images.Postgres));
        ValidateImage(images.Nginx, nameof(images.Nginx));

        var synapseTemplate = await File.ReadAllTextAsync(
            Path.Combine(bundleRoot, "deploy", "synapse", "homeserver.yaml.template"),
            cancellationToken);
        var nginxTemplate = await File.ReadAllTextAsync(
            Path.Combine(bundleRoot, "deploy", "nginx", "default.conf.template"),
            cancellationToken);

        var postgresPassword = secretGenerator.Generate();
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AXON_BIND_IP"] = options.BindIp,
            ["POSTGRES_PASSWORD"] = postgresPassword,
            ["REGISTRATION_SHARED_SECRET"] = secretGenerator.Generate(),
            ["MACAROON_SECRET_KEY"] = secretGenerator.Generate(),
            ["FORM_SECRET"] = secretGenerator.Generate()
        };
        var homeserver = Render(synapseTemplate, replacements);
        var nginx = Render(nginxTemplate, replacements);
        var environment = string.Join('\n',
            $"AXON_BIND_IP={options.BindIp}",
            $"AXON_DATA_ROOT={dataRoot}",
            $"SYNAPSE_IMAGE={images.Synapse}",
            $"POSTGRES_IMAGE={images.Postgres}",
            $"NGINX_IMAGE={images.Nginx}",
            $"POSTGRES_PASSWORD={postgresPassword}") + '\n';
        EnsureResolved(environment);

        Directory.CreateDirectory(dataRoot);
        var stagingRoot = Path.Combine(dataRoot, $".staging-{Guid.NewGuid():N}");
        var stagedHomeserver = Path.Combine(stagingRoot, "runtime", "synapse", "homeserver.yaml");
        var stagedNginx = Path.Combine(stagingRoot, "runtime", "nginx", "default.conf");
        var stagedEnvironment = Path.Combine(stagingRoot, ".env");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stagedHomeserver)!);
            Directory.CreateDirectory(Path.GetDirectoryName(stagedNginx)!);
            await File.WriteAllTextAsync(stagedHomeserver, homeserver, cancellationToken);
            await File.WriteAllTextAsync(stagedNginx, nginx, cancellationToken);
            await File.WriteAllTextAsync(stagedEnvironment, environment, cancellationToken);
            Protect(stagedHomeserver);
            Protect(stagedNginx);
            Protect(stagedEnvironment);

            var homeserverPath = Path.Combine(dataRoot, "runtime", "synapse", "homeserver.yaml");
            var nginxPath = Path.Combine(dataRoot, "runtime", "nginx", "default.conf");
            var environmentPath = Path.Combine(dataRoot, ".env");
            Directory.CreateDirectory(Path.GetDirectoryName(homeserverPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(nginxPath)!);
            File.Move(stagedHomeserver, homeserverPath, overwrite: true);
            File.Move(stagedNginx, nginxPath, overwrite: true);
            File.Move(stagedEnvironment, environmentPath, overwrite: true);

            return new RuntimeRenderResult(environmentPath, homeserverPath, nginxPath);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> replacements)
    {
        var rendered = template;
        foreach (var replacement in replacements)
        {
            rendered = rendered.Replace($"${{{replacement.Key}}}", replacement.Value, StringComparison.Ordinal);
        }

        EnsureResolved(rendered);
        return rendered;
    }

    private static void EnsureResolved(string content)
    {
        var unresolved = TemplateToken().Match(content);
        if (unresolved.Success)
        {
            throw new InvalidDataException($"Runtime template contains unresolved token {unresolved.Value}.");
        }
    }

    private static void ValidateImage(string image, string parameterName)
    {
        if (!DigestImage().IsMatch(image) && !OfflineBundleImage().IsMatch(image))
        {
            throw new ArgumentException("Runtime images must use an explicit sha256 digest or an Axon offline digest tag.", parameterName);
        }
    }

    private static void Protect(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            ProtectWindows(path);
            return;
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [SupportedOSPlatform("windows")]
    private static void ProtectWindows(string path)
    {
        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The installing Windows user SID is unavailable.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(currentUser);
        AddFullControl(security, currentUser);
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        AddFullControl(security, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        new FileInfo(path).SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void AddFullControl(FileSecurity security, SecurityIdentifier identity)
    {
        security.AddAccessRule(new FileSystemAccessRule(
            identity,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
    }

    [GeneratedRegex(@"\$\{[A-Z0-9_]+\}", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateToken();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/:\-]*@sha256:[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestImage();

    [GeneratedRegex(@"^axon\.local/(synapse|postgres|nginx):sha256-[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex OfflineBundleImage();
}
