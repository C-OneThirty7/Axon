namespace Axon.Control.Matrix;

public sealed record SynapseHealthResult(bool Healthy, string Message);

public sealed class SynapseClient(HttpClient httpClient)
{
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);

    public static HttpClient CreateLoopbackHttpClient()
    {
        return new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            BaseAddress = new Uri("http://127.0.0.1:8008"),
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<SynapseHealthResult> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HealthTimeout);
        try
        {
            using var response = await httpClient.GetAsync("/health", timeout.Token);
            return response.IsSuccessStatusCode
                ? new SynapseHealthResult(true, "Synapse is healthy.")
                : new SynapseHealthResult(false, $"Synapse health returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SynapseHealthResult(false, "Synapse health timed out.");
        }
        catch (HttpRequestException)
        {
            return new SynapseHealthResult(false, "Synapse health request failed.");
        }
    }
}
