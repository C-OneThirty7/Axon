using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Axon.Control.Updates;

public sealed record UpdatePlatform(
    string Id,
    string Label,
    string AssetSuffix,
    bool Supported,
    string Message)
{
    public static UpdatePlatform Detect()
    {
        var architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(architecture))
        {
            return Unsupported($"Unsupported host architecture: {RuntimeInformation.OSArchitecture}.");
        }

        if (OperatingSystem.IsWindows())
        {
            return architecture == "amd64"
                ? new UpdatePlatform(
                    "win-x64",
                    "Windows 11 x64",
                    "-offline-win-x64.zip",
                    true,
                    "Windows offline release channel.")
                : Unsupported("Axon does not currently publish a Windows ARM64 bundle.");
        }

        if (OperatingSystem.IsLinux())
        {
            var osRelease = ReadOsRelease();
            var id = osRelease.GetValueOrDefault("ID", string.Empty).ToLowerInvariant();
            var version = osRelease.GetValueOrDefault("VERSION_ID", string.Empty);
            return (id, version, architecture) switch
            {
                ("ubuntu", "24.04", "amd64") => Linux(
                    "ubuntu-24.04-amd64",
                    "Ubuntu 24.04 AMD64",
                    "ubuntu-24.04-amd64"),
                ("ubuntu", "24.04", "arm64") => Linux(
                    "ubuntu-24.04-arm64",
                    "Ubuntu 24.04 ARM64",
                    "ubuntu-24.04-arm64"),
                ("ubuntu", "26.04", "amd64") => Linux(
                    "ubuntu-26.04-amd64",
                    "Ubuntu 26.04 AMD64",
                    "ubuntu-26.04-amd64"),
                ("ubuntu", "26.04", "arm64") => Linux(
                    "ubuntu-26.04-arm64",
                    "Ubuntu 26.04 ARM64",
                    "ubuntu-26.04-arm64"),
                ("debian", "13", "amd64") => Linux(
                    "debian-13-amd64",
                    "Debian 13 AMD64",
                    "debian-13-amd64"),
                ("debian", "13", "arm64") => Linux(
                    "debian-13-arm64",
                    "Debian 13 ARM64",
                    "debian-13-arm64"),
                _ => Unsupported(
                    $"No release channel is configured for {id} {version} {architecture}.")
            };
        }

        return Unsupported("Axon does not currently publish a server bundle for this operating system.");
    }

    private static UpdatePlatform Linux(string id, string label, string suffix)
    {
        return new UpdatePlatform(
            id,
            label,
            $"-offline-{suffix}.tar.gz",
            true,
            "Linux offline release channel.");
    }

    private static UpdatePlatform Unsupported(string message)
    {
        return new UpdatePlatform("unsupported", "Unsupported host", string.Empty, false, message);
    }

    private static Dictionary<string, string> ReadOsRelease()
    {
        try
        {
            return File.ReadLines("/etc/os-release")
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(
                    parts => parts[0],
                    parts => parts[1].Trim().Trim('"'),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

public sealed record UpdateAsset(
    string Name,
    long Size,
    string DownloadUrl);

public sealed record UpdateCheckResult(
    string CurrentVersion,
    string Platform,
    bool Supported,
    bool Reachable,
    bool UpdateAvailable,
    string Message,
    string? LatestVersion = null,
    string? TagName = null,
    string? ReleaseName = null,
    string? ReleaseUrl = null,
    bool Prerelease = false,
    DateTimeOffset? PublishedAt = null,
    UpdateAsset? Package = null,
    UpdateAsset? Checksum = null,
    UpdateAsset? Signature = null);

public sealed class GithubReleaseClient(HttpClient httpClient)
{
    private const string ReleasesPath =
        "https://api.github.com/repos/C-OneThirty7/Axon/releases?per_page=30";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);

    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Axon-Control", ProductInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    public async Task<UpdateCheckResult> CheckAsync(
        string currentVersion,
        UpdatePlatform platform,
        bool includePrereleases = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentVersion);
        ArgumentNullException.ThrowIfNull(platform);

        if (!platform.Supported)
        {
            return new UpdateCheckResult(
                currentVersion,
                platform.Label,
                false,
                false,
                false,
                platform.Message);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            using var response = await httpClient.GetAsync(ReleasesPath, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Unreachable(
                    currentVersion,
                    platform,
                    $"GitHub returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var releases = await JsonSerializer.DeserializeAsync<List<GithubRelease>>(
                stream,
                cancellationToken: timeout.Token) ?? [];

            return SelectRelease(currentVersion, platform, includePrereleases, releases);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unreachable(currentVersion, platform, "GitHub update check timed out.");
        }
        catch (HttpRequestException)
        {
            return Unreachable(currentVersion, platform, "GitHub is unreachable from this host.");
        }
        catch (JsonException)
        {
            return Unreachable(currentVersion, platform, "GitHub returned an invalid release response.");
        }
    }

    private static UpdateCheckResult SelectRelease(
        string currentVersion,
        UpdatePlatform platform,
        bool includePrereleases,
        IEnumerable<GithubRelease> releases)
    {
        _ = Version.TryParse(currentVersion.TrimStart('v', 'V'), out var installedVersion);
        var candidates = releases
            .Where(release => !release.Draft && (includePrereleases || !release.Prerelease))
            .Select(release => CreateCandidate(release, platform))
            .Where(candidate => candidate is not null)
            .Cast<ReleaseCandidate>()
            .OrderByDescending(candidate => candidate.Version)
            .ThenByDescending(candidate => candidate.PublishedAt)
            .ToList();

        if (candidates.Count == 0)
        {
            return new UpdateCheckResult(
                currentVersion,
                platform.Label,
                true,
                true,
                false,
                includePrereleases
                    ? "No compatible release asset was found."
                    : "No compatible stable release asset was found.");
        }

        var latest = candidates[0];
        var available = installedVersion is null || latest.Version > installedVersion;
        return new UpdateCheckResult(
            currentVersion,
            platform.Label,
            true,
            true,
            available,
            available
                ? $"Axon {latest.Version} is available for {platform.Label}."
                : $"Axon {currentVersion} is current for {platform.Label}.",
            latest.Version.ToString(),
            latest.TagName,
            latest.Name,
            latest.ReleaseUrl.ToString(),
            latest.Prerelease,
            latest.PublishedAt,
            latest.Package,
            latest.Checksum,
            latest.Signature);
    }

    private static ReleaseCandidate? CreateCandidate(
        GithubRelease release,
        UpdatePlatform platform)
    {
        if (string.IsNullOrWhiteSpace(release.TagName) ||
            !Version.TryParse(release.TagName.TrimStart('v', 'V'), out var version))
        {
            return null;
        }
        var releaseUrl = TrustedGithubUri(release.HtmlUrl);
        if (releaseUrl is null)
        {
            return null;
        }

        var assets = release.Assets ?? [];
        var packageDto = assets.FirstOrDefault(asset =>
            !string.IsNullOrWhiteSpace(asset.Name) &&
            asset.Name.StartsWith("Axon-v", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.EndsWith(platform.AssetSuffix, StringComparison.OrdinalIgnoreCase));
        if (packageDto is null)
        {
            return null;
        }
        var packageUrl = TrustedGithubUri(packageDto.BrowserDownloadUrl);
        if (packageUrl is null)
        {
            return null;
        }

        var checksumDto = assets.FirstOrDefault(asset =>
            string.Equals(
                asset.Name,
                packageDto.Name + ".sha256",
                StringComparison.OrdinalIgnoreCase));
        var checksumUrl = checksumDto is null
            ? null
            : TrustedGithubUri(checksumDto.BrowserDownloadUrl);
        var signatureDto = assets.FirstOrDefault(asset =>
            string.Equals(
                asset.Name,
                packageDto.Name + ".sig",
                StringComparison.OrdinalIgnoreCase));
        var signatureUrl = signatureDto is null
            ? null
            : TrustedGithubUri(signatureDto.BrowserDownloadUrl);

        return new ReleaseCandidate(
            version,
            release.TagName,
            string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
            releaseUrl,
            release.Prerelease,
            release.PublishedAt,
            new UpdateAsset(packageDto.Name!, packageDto.Size, packageUrl.ToString()),
            checksumDto is not null && checksumUrl is not null
                ? new UpdateAsset(checksumDto.Name!, checksumDto.Size, checksumUrl.ToString())
                : null,
            signatureDto is not null && signatureUrl is not null
                ? new UpdateAsset(signatureDto.Name!, signatureDto.Size, signatureUrl.ToString())
                : null);
    }

    private static Uri? TrustedGithubUri(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
    }

    private static UpdateCheckResult Unreachable(
        string currentVersion,
        UpdatePlatform platform,
        string message)
    {
        return new UpdateCheckResult(
            currentVersion,
            platform.Label,
            true,
            false,
            false,
            message);
    }

    private sealed record ReleaseCandidate(
        Version Version,
        string TagName,
        string Name,
        Uri ReleaseUrl,
        bool Prerelease,
        DateTimeOffset? PublishedAt,
        UpdateAsset Package,
        UpdateAsset? Checksum,
        UpdateAsset? Signature);

    private sealed record GithubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("assets")] List<GithubAsset>? Assets);

    private sealed record GithubAsset(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl);
}
