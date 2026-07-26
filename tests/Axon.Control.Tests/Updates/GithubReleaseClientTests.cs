using System.Net;
using Axon.Control.Updates;

namespace Axon.Control.Tests.Updates;

public sealed class GithubReleaseClientTests
{
    private static readonly UpdatePlatform WindowsPlatform = new(
        "win-x64",
        "Windows 11 x64",
        "-offline-win-x64.zip",
        true,
        "Windows release channel.");

    [Fact]
    public async Task Check_selects_newest_compatible_release_and_checksum()
    {
        var handler = new JsonHandler(
            """
            [
              {
                "tag_name": "v0.3.0",
                "name": "Axon v0.3.0",
                "html_url": "https://github.com/C-OneThirty7/Axon/releases/tag/v0.3.0",
                "draft": false,
                "prerelease": false,
                "published_at": "2026-07-26T15:00:00Z",
                "assets": [
                  {
                    "name": "Axon-v0.3.0-offline-win-x64.zip",
                    "size": 1234,
                    "browser_download_url": "https://github.com/C-OneThirty7/Axon/releases/download/v0.3.0/Axon-v0.3.0-offline-win-x64.zip"
                  },
                  {
                    "name": "Axon-v0.3.0-offline-win-x64.zip.sha256",
                    "size": 98,
                    "browser_download_url": "https://github.com/C-OneThirty7/Axon/releases/download/v0.3.0/Axon-v0.3.0-offline-win-x64.zip.sha256"
                  },
                  {
                    "name": "Axon-v0.3.0-offline-win-x64.zip.sig",
                    "size": 72,
                    "browser_download_url": "https://github.com/C-OneThirty7/Axon/releases/download/v0.3.0/Axon-v0.3.0-offline-win-x64.zip.sig"
                  }
                ]
              },
              {
                "tag_name": "v9.0.0",
                "name": "Wrong platform",
                "html_url": "https://github.com/C-OneThirty7/Axon/releases/tag/v9.0.0",
                "draft": false,
                "prerelease": false,
                "published_at": "2026-07-26T16:00:00Z",
                "assets": [
                  {
                    "name": "Axon-v9.0.0-offline-ubuntu-24.04-amd64.tar.gz",
                    "size": 4567,
                    "browser_download_url": "https://github.com/C-OneThirty7/Axon/releases/download/v9.0.0/Axon-v9.0.0-offline-ubuntu-24.04-amd64.tar.gz"
                  }
                ]
              }
            ]
            """);
        var client = new GithubReleaseClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.2.0", WindowsPlatform);

        Assert.True(result.Reachable);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("0.3.0", result.LatestVersion);
        Assert.Equal("Axon-v0.3.0-offline-win-x64.zip", result.Package?.Name);
        Assert.Equal("Axon-v0.3.0-offline-win-x64.zip.sha256", result.Checksum?.Name);
        Assert.Equal("Axon-v0.3.0-offline-win-x64.zip.sig", result.Signature?.Name);
        Assert.Equal("api.github.com", handler.LastUri?.Host);
    }

    [Fact]
    public void Production_http_client_identifies_Axon_to_Github()
    {
        using var client = GithubReleaseClient.CreateHttpClient();

        Assert.Contains("Axon-Control", client.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public async Task Check_excludes_prereleases_by_default()
    {
        var handler = new JsonHandler(
            """
            [
              {
                "tag_name": "v0.4.0",
                "name": "Preview",
                "html_url": "https://github.com/C-OneThirty7/Axon/releases/tag/v0.4.0",
                "draft": false,
                "prerelease": true,
                "published_at": "2026-07-26T15:00:00Z",
                "assets": [
                  {
                    "name": "Axon-v0.4.0-offline-win-x64.zip",
                    "size": 1234,
                    "browser_download_url": "https://github.com/C-OneThirty7/Axon/releases/download/v0.4.0/Axon-v0.4.0-offline-win-x64.zip"
                  }
                ]
              }
            ]
            """);
        var client = new GithubReleaseClient(new HttpClient(handler));

        var stable = await client.CheckAsync("0.2.0", WindowsPlatform);
        var preview = await client.CheckAsync("0.2.0", WindowsPlatform, includePrereleases: true);

        Assert.False(stable.UpdateAvailable);
        Assert.Null(stable.Package);
        Assert.True(preview.UpdateAvailable);
        Assert.True(preview.Prerelease);
    }

    [Fact]
    public async Task Check_rejects_non_github_download_urls()
    {
        var handler = new JsonHandler(
            """
            [
              {
                "tag_name": "v0.3.0",
                "name": "Untrusted",
                "html_url": "https://github.com/C-OneThirty7/Axon/releases/tag/v0.3.0",
                "draft": false,
                "prerelease": false,
                "published_at": "2026-07-26T15:00:00Z",
                "assets": [
                  {
                    "name": "Axon-v0.3.0-offline-win-x64.zip",
                    "size": 1234,
                    "browser_download_url": "https://example.invalid/Axon-v0.3.0-offline-win-x64.zip"
                  }
                ]
              }
            ]
            """);
        var client = new GithubReleaseClient(new HttpClient(handler));

        var result = await client.CheckAsync("0.2.0", WindowsPlatform);

        Assert.False(result.UpdateAvailable);
        Assert.Null(result.Package);
    }

    [Fact]
    public async Task Check_returns_safe_offline_status()
    {
        var client = new GithubReleaseClient(new HttpClient(new FailureHandler()));

        var result = await client.CheckAsync("0.2.0", WindowsPlatform);

        Assert.False(result.Reachable);
        Assert.False(result.UpdateAvailable);
        Assert.Equal("GitHub is unreachable from this host.", result.Message);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("offline");
        }
    }
}
