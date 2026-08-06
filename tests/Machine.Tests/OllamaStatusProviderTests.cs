using System.Net;
using System.Text;
using Machine.Ollama;

namespace Machine.Tests;

public sealed class OllamaStatusProviderTests
{
    private static readonly Uri LoopbackBaseAddress =
        new("http://127.0.0.1:11434/");

    [Fact]
    public async Task GetStatusAsyncReturnsOnlineStatusWithOneLoadedModel()
    {
        using var handler = new StubHttpMessageHandler(request =>
            request.RequestUri?.AbsolutePath switch
            {
                "/api/version" => JsonResponse(
                    """
                    { "version": "0.12.3" }
                    """),
                "/api/ps" => JsonResponse(
                    """
                    {
                      "models": [
                        {
                          "name": "qwen3.5:4b",
                          "size": 4294967296,
                          "size_vram": 3650722201,
                          "context_length": 16384,
                          "expires_at": "2026-08-06T10:00:00Z",
                          "details": {
                            "parameter_size": "4.7B",
                            "quantization_level": "Q4_K_M"
                          }
                        }
                      ]
                    }
                    """),
                _ => throw new InvalidOperationException(
                    $"Unexpected request: {request.RequestUri}")
            });
        using var httpClient = CreateHttpClient(handler);
        var provider = new OllamaStatusProvider(httpClient);

        var snapshot = await provider.GetStatusAsync();

        Assert.True(snapshot.IsServiceAvailable);
        Assert.Equal("0.12.3", snapshot.Version);
        Assert.True(snapshot.IsRunningModelStatusAvailable);
        var model = Assert.Single(snapshot.RunningModels);
        Assert.Equal("qwen3.5:4b", model.Name);
        Assert.Equal("4.7B", model.ParameterSize);
        Assert.Equal("Q4_K_M", model.QuantizationLevel);
        Assert.Equal(3650722201, model.SizeVramBytes);
        Assert.Equal(16384, model.ContextLength);
        Assert.All(handler.Requests, request =>
            Assert.Equal(HttpMethod.Get, request.Method));
    }

    [Fact]
    public async Task GetStatusAsyncReturnsOnlineStatusWithNoLoadedModels()
    {
        using var handler = new StubHttpMessageHandler(request =>
            request.RequestUri?.AbsolutePath switch
            {
                "/api/version" => JsonResponse(
                    """{ "version": "0.12.3" }"""),
                "/api/ps" => JsonResponse("""{ "models": [] }"""),
                _ => throw new InvalidOperationException(
                    $"Unexpected request: {request.RequestUri}")
            });
        using var httpClient = CreateHttpClient(handler);
        var provider = new OllamaStatusProvider(httpClient);

        var snapshot = await provider.GetStatusAsync();

        Assert.True(snapshot.IsServiceAvailable);
        Assert.Equal("0.12.3", snapshot.Version);
        Assert.True(snapshot.IsRunningModelStatusAvailable);
        Assert.Empty(snapshot.RunningModels);
    }

    [Fact]
    public async Task GetStatusAsyncReturnsUnavailableWhenServiceRequestFails()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new HttpRequestException("Ollama is unavailable."));
        using var httpClient = CreateHttpClient(handler);
        var provider = new OllamaStatusProvider(httpClient);

        var snapshot = await provider.GetStatusAsync();

        Assert.False(snapshot.IsServiceAvailable);
        Assert.Null(snapshot.Version);
        Assert.False(snapshot.IsRunningModelStatusAvailable);
        Assert.Empty(snapshot.RunningModels);
        Assert.Single(handler.Requests);
        Assert.Equal(
            "/api/version",
            handler.Requests[0].RequestUri?.AbsolutePath);
    }

    [Fact]
    public async Task GetStatusAsyncPreservesOnlineStatusWhenRunningModelRequestFails()
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/api/version")
            {
                return JsonResponse("""{ "version": "0.12.3" }""");
            }

            if (request.RequestUri?.AbsolutePath == "/api/ps")
            {
                throw new HttpRequestException(
                    "Loaded-model status is unavailable.");
            }

            throw new InvalidOperationException(
                $"Unexpected request: {request.RequestUri}");
        });
        using var httpClient = CreateHttpClient(handler);
        var provider = new OllamaStatusProvider(httpClient);

        var snapshot = await provider.GetStatusAsync();

        Assert.True(snapshot.IsServiceAvailable);
        Assert.Equal("0.12.3", snapshot.Version);
        Assert.False(snapshot.IsRunningModelStatusAvailable);
        Assert.Empty(snapshot.RunningModels);
    }

    [Fact]
    public async Task GetStatusAsyncWithPreCancelledTokenThrows()
    {
        using var handler = new StubHttpMessageHandler(_ =>
            throw new InvalidOperationException(
                "No request should be sent for caller cancellation."));
        using var httpClient = CreateHttpClient(handler);
        var provider = new OllamaStatusProvider(httpClient);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => provider.GetStatusAsync(
                cancellationTokenSource.Token));
    }

    private static HttpClient CreateHttpClient(
        HttpMessageHandler handler) =>
        new(handler)
        {
            BaseAddress = LoopbackBaseAddress
        };

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json")
        };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);

            return Task.FromResult(responseFactory(request));
        }
    }
}
