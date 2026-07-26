using System.Net;
using Axon.Control.Matrix;

namespace Axon.Control.Tests.Matrix;

public sealed class SynapseClientTests
{
    [Fact]
    public async Task Health_uses_the_loopback_health_endpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var client = new SynapseClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8008") });

        var result = await client.CheckHealthAsync();

        Assert.True(result.Healthy);
        Assert.Equal(new Uri("http://127.0.0.1:8008/health"), handler.LastUri);
    }

    [Fact]
    public async Task Health_failure_does_not_return_response_content()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.InternalServerError,
            "secret diagnostic body");
        var client = new SynapseClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8008") });

        var result = await client.CheckHealthAsync();

        Assert.False(result.Healthy);
        Assert.DoesNotContain("secret diagnostic body", result.Message, StringComparison.Ordinal);
        Assert.Contains("HTTP 500", result.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string content = "OK") : HttpMessageHandler
    {
        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(content)
            });
        }
    }
}
