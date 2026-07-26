using System.Net;
using System.Security.Cryptography;
using System.Text;
using Axon.Control.Updates;

namespace Axon.Control.Tests.Updates;

public sealed class UpdateManagerTests
{
    private const string TestSignature =
        "MEYCIQDv5oSR17c+i/vzIW5hUrDzngi+HUmN3rJLoH+a/9rdhQIhAP52ECUy1FuYg27FIuyNVZJvR2nuTwTvmlOcAUXAG7gM";

    [Fact]
    public async Task Download_requires_and_verifies_matching_checksum()
    {
        var payload = Encoding.UTF8.GetBytes("verified Axon release payload");
        var signature = Convert.FromBase64String(TestSignature);
        var hash = Convert.ToHexStringLower(SHA256.HashData(payload));
        var packageName = "Axon-v0.4.0-offline-win-x64.zip";
        var releaseJson =
            $$"""
            [
              {
                "tag_name": "v0.4.0",
                "name": "Axon v0.4.0",
                "html_url": "https://github.com/C-OneThirty7/Axon/releases/tag/v0.4.0",
                "draft": false,
                "prerelease": false,
                "published_at": "2026-07-26T15:00:00Z",
                "assets": [
                  {
                    "name": "{{packageName}}",
                    "size": {{payload.Length}},
                    "browser_download_url": "https://github.com/C-OneThirty7/Axon/releases/download/v0.4.0/{{packageName}}"
                  },
                  {
                    "name": "{{packageName}}.sha256",
                    "size": 100,
                    "browser_download_url": "https://github.com/C-OneThirty7/Axon/releases/download/v0.4.0/{{packageName}}.sha256"
                  },
                  {
                    "name": "{{packageName}}.sig",
                    "size": {{signature.Length}},
                    "browser_download_url": "https://github.com/C-OneThirty7/Axon/releases/download/v0.4.0/{{packageName}}.sig"
                  }
                ]
              }
            ]
            """;
        var releaseHandler = new StaticHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson)
            });
        var downloadHandler = new StaticHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{hash}  {packageName}\n")
                };
            }
            if (request.RequestUri.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(signature)
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
        });
        var root = Path.Combine(Path.GetTempPath(), $"axon-update-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var manager = new UpdateManager(
                new HttpClient(downloadHandler),
                new GithubReleaseClient(new HttpClient(releaseHandler)),
                root,
                root,
                () => new UpdatePlatform(
                    "win-x64",
                    "Windows 11 x64",
                    "-offline-win-x64.zip",
                    true,
                    "Windows release channel."),
                root);

            manager.StartDownload(includePrereleases: false);
            UpdateOperationStatus status;
            for (var attempt = 0; ; attempt++)
            {
                status = manager.GetStatus();
                if (status.State is "downloaded" or "failed") break;
                Assert.True(attempt < 200, "Update download did not complete.");
                await Task.Delay(10);
            }

            Assert.Equal("downloaded", status.State);
            Assert.Equal("0.4.0", status.Version);
            Assert.Equal(payload.Length, status.BytesReceived);
            Assert.Single(Directory.GetFiles(root, packageName, SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Signature_verifier_rejects_modified_release_content()
    {
        var root = Path.Combine(Path.GetTempPath(), $"axon-signature-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, "release.zip");
        var signature = Path.Combine(root, "release.zip.sig");
        try
        {
            await File.WriteAllTextAsync(archive, "modified Axon release payload");
            await File.WriteAllBytesAsync(signature, Convert.FromBase64String(TestSignature));

            Assert.False(await ReleaseSignatureVerifier.VerifyAsync(archive, signature));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StaticHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
