using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Axon.Control.Updates;

public sealed record UpdateOperationStatus(
    string State,
    string Message,
    string? Version = null,
    long BytesReceived = 0,
    long TotalBytes = 0,
    DateTimeOffset? UpdatedAt = null);

public sealed class UpdateManager
{
    private static readonly Regex ChecksumPattern = new(
        @"\A(?<hash>[a-fA-F0-9]{64})\s+[*]?(?<name>[^\r\n]+)\s*\z",
        RegexOptions.CultureInvariant);
    private readonly object sync = new();
    private readonly HttpClient httpClient;
    private readonly GithubReleaseClient releases;
    private readonly string bundleRoot;
    private readonly string dataRoot;
    private readonly string updateRoot;
    private readonly Func<UpdatePlatform> platformDetector;
    private UpdateOperationStatus status =
        new("idle", "No update operation is active.", UpdatedAt: DateTimeOffset.UtcNow);
    private DownloadedRelease? downloaded;
    private Task? downloadTask;

    public UpdateManager(
        HttpClient httpClient,
        GithubReleaseClient releases,
        string bundleRoot,
        string dataRoot,
        Func<UpdatePlatform>? platformDetector = null)
    {
        this.httpClient = httpClient;
        this.releases = releases;
        this.bundleRoot = Path.GetFullPath(bundleRoot);
        this.dataRoot = Path.GetFullPath(dataRoot);
        this.platformDetector = platformDetector ?? UpdatePlatform.Detect;
        updateRoot = OperatingSystem.IsLinux()
            ? "/var/lib/axon/updates"
            : Path.Combine(this.dataRoot, "updates");
    }

    public static HttpClient CreateHttpClient()
    {
        return new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public UpdateOperationStatus GetStatus()
    {
        lock (sync)
        {
            return status;
        }
    }

    public UpdateOperationStatus StartDownload(bool includePrereleases)
    {
        lock (sync)
        {
            if (downloadTask is { IsCompleted: false } ||
                string.Equals(status.State, "installing", StringComparison.Ordinal))
            {
                return status;
            }

            downloaded = null;
            SetStatusLocked(new(
                "checking",
                "Checking GitHub for a compatible release.",
                UpdatedAt: DateTimeOffset.UtcNow));
            downloadTask = Task.Run(() => DownloadCoreAsync(includePrereleases));
            return status;
        }
    }

    public async Task<UpdateOperationStatus> StartInstallAsync(
        CancellationToken cancellationToken = default)
    {
        DownloadedRelease release;
        lock (sync)
        {
            if (downloaded is null ||
                !string.Equals(status.State, "downloaded", StringComparison.Ordinal))
            {
                return new UpdateOperationStatus(
                    "failed",
                    "Download and verify an update before installing it.",
                    UpdatedAt: DateTimeOffset.UtcNow);
            }
            release = downloaded;
            SetStatusLocked(new(
                "installing",
                "The verified update is being handed to the host installer.",
                release.Version,
                release.Size,
                release.Size,
                DateTimeOffset.UtcNow));
        }

        var actual = await HashFileAsync(release.ArchivePath, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actual),
                Convert.FromHexString(release.Sha256)))
        {
            return Fail("The staged archive no longer matches its verified SHA-256 checksum.");
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                StartWindowsInstaller(release);
            }
            else if (OperatingSystem.IsLinux())
            {
                await RequestLinuxInstallerAsync(release, cancellationToken);
            }
            else
            {
                return Fail("Click-to-install updates are not supported on this operating system.");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Fail($"Unable to start the host updater: {exception.Message}");
        }

        return GetStatus();
    }

    private async Task DownloadCoreAsync(bool includePrereleases)
    {
        try
        {
            var platform = platformDetector();
            var release = await releases.CheckAsync(
                ProductInfo.Version,
                platform,
                includePrereleases,
                CancellationToken.None);
            if (!release.UpdateAvailable ||
                release.Package is null ||
                release.Checksum is null ||
                release.Signature is null ||
                string.IsNullOrWhiteSpace(release.LatestVersion))
            {
                Fail(
                    release.UpdateAvailable
                        ? "Automated installation requires a release package, SHA-256 asset, and Axon signature."
                        : release.Message);
                return;
            }

            var versionRoot = Path.Combine(updateRoot, $"v{release.LatestVersion}");
            Directory.CreateDirectory(versionRoot);
            var archivePath = SafeChildPath(versionRoot, release.Package.Name);
            var checksumPath = SafeChildPath(versionRoot, release.Checksum.Name);
            var signaturePath = SafeChildPath(versionRoot, release.Signature.Name);
            var partialPath = archivePath + ".part";

            SetStatus(new(
                "downloading",
                $"Downloading Axon {release.LatestVersion}.",
                release.LatestVersion,
                0,
                release.Package.Size,
                DateTimeOffset.UtcNow));

            var checksumText = await DownloadChecksumAsync(
                release.Checksum.DownloadUrl,
                checksumPath);
            var expectedHash = ParseChecksum(checksumText, release.Package.Name);
            await DownloadSmallBinaryAsync(
                release.Signature.DownloadUrl,
                signaturePath,
                1024,
                "Release signature");
            await DownloadArchiveAsync(
                release.Package.DownloadUrl,
                partialPath,
                release.Package.Size,
                release.LatestVersion);

            File.Move(partialPath, archivePath, true);
            var actualHash = await HashFileAsync(archivePath, CancellationToken.None);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(expectedHash)))
            {
                File.Delete(archivePath);
                Fail("Downloaded archive failed SHA-256 verification.");
                return;
            }
            if (!await ReleaseSignatureVerifier.VerifyAsync(
                    archivePath,
                    signaturePath,
                    CancellationToken.None))
            {
                File.Delete(archivePath);
                Fail("Downloaded archive failed Axon release-signature verification.");
                return;
            }

            lock (sync)
            {
                downloaded = new DownloadedRelease(
                    release.LatestVersion,
                    archivePath,
                    signaturePath,
                    expectedHash,
                    release.Package.Size);
                SetStatusLocked(new(
                    "downloaded",
                    $"Axon {release.LatestVersion} is downloaded and verified.",
                    release.LatestVersion,
                    release.Package.Size,
                    release.Package.Size,
                    DateTimeOffset.UtcNow));
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or UnauthorizedAccessException or
            InvalidDataException or CryptographicException)
        {
            Fail($"Update download failed: {exception.Message}");
        }
    }

    private async Task<string> DownloadChecksumAsync(string url, string destination)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 4096)
        {
            throw new InvalidDataException("Checksum asset is unexpectedly large.");
        }
        var text = await response.Content.ReadAsStringAsync();
        await File.WriteAllTextAsync(destination, text);
        return text;
    }

    private async Task DownloadSmallBinaryAsync(
        string url,
        string destination,
        int maximumBytes,
        string label)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maximumBytes)
        {
            throw new InvalidDataException($"{label} is unexpectedly large.");
        }
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length == 0 || bytes.Length > maximumBytes)
        {
            throw new InvalidDataException($"{label} has an invalid size.");
        }
        await File.WriteAllBytesAsync(destination, bytes);
    }

    private async Task DownloadArchiveAsync(
        string url,
        string destination,
        long advertisedSize,
        string version)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? advertisedSize;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[1024 * 1024];
        long received = 0;
        var lastReport = DateTimeOffset.MinValue;
        while (true)
        {
            var count = await input.ReadAsync(buffer);
            if (count == 0) break;
            await output.WriteAsync(buffer.AsMemory(0, count));
            received += count;
            var now = DateTimeOffset.UtcNow;
            if (now - lastReport >= TimeSpan.FromMilliseconds(350))
            {
                SetStatus(new(
                    "downloading",
                    $"Downloading Axon {version}.",
                    version,
                    received,
                    total,
                    now));
                lastReport = now;
            }
        }
        await output.FlushAsync();
        if (advertisedSize > 0 && received != advertisedSize)
        {
            throw new InvalidDataException(
                $"Downloaded size {received} did not match release size {advertisedSize}.");
        }
    }

    private void StartWindowsInstaller(DownloadedRelease release)
    {
        var script = Path.Combine(bundleRoot, "scripts", "Invoke-AxonUpdate.ps1");
        if (!File.Exists(script))
        {
            throw new InvalidOperationException("The Windows update helper is missing.");
        }
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
            "-ArchivePath", release.ArchivePath,
            "-SignaturePath", release.SignaturePath,
            "-ExpectedSha256", release.Sha256,
            "-Version", release.Version,
            "-CurrentProcessId", Environment.ProcessId.ToString(),
            "-DataRoot", dataRoot,
            "-CurrentBundleRoot", bundleRoot,
            "-VerifierPath", Environment.ProcessPath ??
                throw new InvalidOperationException("Axon Control executable path is unavailable.")
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        Process.Start(startInfo)?.Dispose();
    }

    private async Task RequestLinuxInstallerAsync(
        DownloadedRelease release,
        CancellationToken cancellationToken)
    {
        var requestPath = Path.Combine(updateRoot, "install-request.json");
        var temporaryPath = requestPath + ".tmp";
        var payload =
            $"AXON_UPDATE_VERSION={release.Version}\n" +
            $"AXON_UPDATE_ARCHIVE={release.ArchivePath}\n" +
            $"AXON_UPDATE_SIGNATURE={release.SignaturePath}\n" +
            $"AXON_UPDATE_SHA256={release.Sha256}\n";
        await File.WriteAllTextAsync(temporaryPath, payload, cancellationToken);
        File.Move(temporaryPath, requestPath, true);
    }

    private static string ParseChecksum(string text, string expectedName)
    {
        var match = ChecksumPattern.Match(text);
        if (!match.Success ||
            !string.Equals(
                Path.GetFileName(match.Groups["name"].Value.Trim()),
                expectedName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Checksum asset did not identify the selected package.");
        }
        return match.Groups["hash"].Value.ToLowerInvariant();
    }

    private static async Task<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    private static string SafeChildPath(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Release asset name is unsafe.");
        }
        var rootPath = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(rootPath, name));
        if (!candidate.StartsWith(rootPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Release asset path escaped the update directory.");
        }
        return candidate;
    }

    private UpdateOperationStatus Fail(string message)
    {
        lock (sync)
        {
            SetStatusLocked(new(
                "failed",
                message,
                downloaded?.Version,
                UpdatedAt: DateTimeOffset.UtcNow));
            return status;
        }
    }

    private void SetStatus(UpdateOperationStatus next)
    {
        lock (sync)
        {
            SetStatusLocked(next);
        }
    }

    private void SetStatusLocked(UpdateOperationStatus next)
    {
        status = next;
    }

    private sealed record DownloadedRelease(
        string Version,
        string ArchivePath,
        string SignaturePath,
        string Sha256,
        long Size);
}
