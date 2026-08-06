using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Machine.Core;

namespace Machine.Ollama;

public sealed partial class OllamaStatusProvider : IOllamaStatusProvider
{
    private const string VersionEndpoint = "api/version";
    private const string RunningModelsEndpoint = "api/ps";

    private readonly HttpClient _httpClient;

    public OllamaStatusProvider(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
    }

    public async Task<OllamaStatusSnapshot> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        VersionResponse? versionResponse;

        try
        {
            versionResponse = await GetResponseAsync<VersionResponse>(
                VersionEndpoint,
                OllamaJsonSerializerContext.Default.VersionResponse,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (IsOrdinaryStatusFailure(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateUnavailableSnapshot();
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (versionResponse is null)
        {
            return CreateUnavailableSnapshot();
        }

        var version = string.IsNullOrWhiteSpace(
            versionResponse.Version)
            ? null
            : versionResponse.Version;

        RunningModelsResponse? runningModelsResponse;

        try
        {
            runningModelsResponse =
                await GetResponseAsync<RunningModelsResponse>(
                    RunningModelsEndpoint,
                    OllamaJsonSerializerContext.Default.RunningModelsResponse,
                    cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (IsOrdinaryStatusFailure(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CreateRunningModelsUnavailableSnapshot(version);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (runningModelsResponse?.Models is null ||
            !TryMapRunningModels(
                runningModelsResponse.Models,
                out var runningModels))
        {
            return CreateRunningModelsUnavailableSnapshot(version);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new OllamaStatusSnapshot(
            IsServiceAvailable: true,
            Version: version,
            IsRunningModelStatusAvailable: true,
            RunningModels: runningModels,
            CapturedAt: DateTimeOffset.UtcNow);
    }

    private async Task<T?> GetResponseAsync<T>(
        string requestUri,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            requestUri,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(
            jsonTypeInfo,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static bool TryMapRunningModels(
        IReadOnlyList<RunningModelResponse?> responses,
        out IReadOnlyList<OllamaRunningModel> runningModels)
    {
        var mappedModels = new List<OllamaRunningModel>(
            responses.Count);

        foreach (var response in responses)
        {
            if (response is null ||
                string.IsNullOrWhiteSpace(response.Name) ||
                response.SizeBytes is null ||
                response.SizeVramBytes is null ||
                response.ContextLength is null)
            {
                runningModels = Array.Empty<OllamaRunningModel>();
                return false;
            }

            mappedModels.Add(new OllamaRunningModel(
                Name: response.Name,
                ParameterSize: response.Details?.ParameterSize,
                QuantizationLevel:
                    response.Details?.QuantizationLevel,
                SizeBytes: response.SizeBytes.Value,
                SizeVramBytes: response.SizeVramBytes.Value,
                ContextLength: response.ContextLength.Value,
                ExpiresAt: response.ExpiresAt));
        }

        runningModels = mappedModels
            .OrderBy(
                model => model.Name,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(
                model => model.Name,
                StringComparer.Ordinal)
            .ToArray();

        return true;
    }

    private static OllamaStatusSnapshot CreateUnavailableSnapshot() =>
        new(
            IsServiceAvailable: false,
            Version: null,
            IsRunningModelStatusAvailable: false,
            RunningModels: Array.Empty<OllamaRunningModel>(),
            CapturedAt: DateTimeOffset.UtcNow);

    private static OllamaStatusSnapshot
        CreateRunningModelsUnavailableSnapshot(string? version) =>
            new(
                IsServiceAvailable: true,
                Version: version,
                IsRunningModelStatusAvailable: false,
                RunningModels: Array.Empty<OllamaRunningModel>(),
                CapturedAt: DateTimeOffset.UtcNow);

    private static bool IsOrdinaryStatusFailure(
        Exception exception) =>
        exception is HttpRequestException or
            IOException or
            JsonException or
            NotSupportedException or
            OperationCanceledException;

    private sealed record VersionResponse(
        [property: JsonPropertyName("version")]
        string? Version);

    private sealed record RunningModelsResponse(
        [property: JsonPropertyName("models")]
        RunningModelResponse?[]? Models);

    private sealed record RunningModelResponse(
        [property: JsonPropertyName("name")]
        string? Name,
        [property: JsonPropertyName("size")]
        long? SizeBytes,
        [property: JsonPropertyName("size_vram")]
        long? SizeVramBytes,
        [property: JsonPropertyName("context_length")]
        int? ContextLength,
        [property: JsonPropertyName("expires_at")]
        DateTimeOffset? ExpiresAt,
        [property: JsonPropertyName("details")]
        ModelDetailsResponse? Details);

    private sealed record ModelDetailsResponse(
        [property: JsonPropertyName("parameter_size")]
        string? ParameterSize,
        [property: JsonPropertyName("quantization_level")]
        string? QuantizationLevel);

    [JsonSerializable(typeof(VersionResponse))]
    [JsonSerializable(typeof(RunningModelsResponse))]
    private sealed partial class OllamaJsonSerializerContext
        : JsonSerializerContext
    {
    }
}
