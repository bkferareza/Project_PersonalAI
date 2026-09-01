using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Text.RegularExpressions;
using Machine.Core;

namespace Machine.Inference;

public sealed partial class BundledLlamaInferenceRuntime
    : ILocalInferenceRuntime
{
    private static readonly TimeSpan ReadinessPollInterval =
        TimeSpan.FromMilliseconds(250);
    private readonly BundledInferenceConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _generationGate = new(1, 1);
    private readonly BoundedInferenceDiagnostics _diagnostics = new();
    private readonly object _stateSync = new();
    private Process? _process;
    private WindowsKillOnCloseJob? _job;
    private Uri? _baseAddress;
    private string? _apiKey;
    private DateTimeOffset? _startedAt;
    private int? _port;
    private LocalInferenceModelState _state =
        LocalInferenceModelState.Asleep;
    private LocalInferenceFailure? _failure;
    private CancellationTokenSource? _residencyCancellation;
    private DateTimeOffset? _residencyExpiresAt;
    private TimeSpan? _lastLoadDuration;
    private TimeSpan? _lastGenerationDuration;
    private long _modelGpuResidentBytes;
    private bool _artifactsValidated;
    private bool _intentionalStop;
    private bool _shutdownStarted;

    public BundledLlamaInferenceRuntime()
        : this(BundledInferenceConfiguration.LoadDefault())
    {
    }

    public BundledLlamaInferenceRuntime(
        BundledInferenceConfiguration configuration,
        HttpMessageHandler? httpMessageHandler = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
        _httpClient = httpMessageHandler is null
            ? new HttpClient()
            : new HttpClient(httpMessageHandler);
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<LocalInferenceStartResult> EnsureAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_shutdownStarted, this);
            if (IsCurrentProcessAlive())
            {
                ScheduleResidency();
                return new(true, false, true);
            }

            CleanupExitedProcess();
            SetState(LocalInferenceModelState.Loading, failure: null);
            try
            {
                if (!_artifactsValidated)
                {
                    await InferenceArtifactValidator.ValidateAsync(
                        _configuration,
                        cancellationToken).ConfigureAwait(false);
                    _artifactsValidated = true;
                }

                var port = ReserveLoopbackPort();
                var apiKey = CreateApiKey();
                var startInfo = CreateStartInfo(port, apiKey);
                lock (_stateSync)
                {
                    _modelGpuResidentBytes = 0;
                }
                var loadStopwatch = Stopwatch.StartNew();
                var process = Process.Start(startInfo) ??
                    throw new InvalidOperationException(
                        "The private inference process could not start.");
                process.EnableRaisingEvents = true;
                process.OutputDataReceived += OnOutputDataReceived;
                process.ErrorDataReceived += OnErrorDataReceived;
                process.Exited += OnProcessExited;
                WindowsKillOnCloseJob? job = null;
                try
                {
                    job = WindowsKillOnCloseJob.CreateAndAssign(process);
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                catch
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: false);
                    }
                    process.Dispose();
                    job?.Dispose();
                    throw;
                }

                _process = process;
                _job = job;
                _baseAddress = new Uri(
                    $"http://127.0.0.1:{port}/",
                    UriKind.Absolute);
                _apiKey = apiKey;
                _port = port;
                _startedAt = DateTimeOffset.UtcNow;
                _intentionalStop = false;

                await WaitForReadinessAsync(
                    process,
                    cancellationToken).ConfigureAwait(false);
                loadStopwatch.Stop();
                _lastLoadDuration = loadStopwatch.Elapsed;
                SetState(LocalInferenceModelState.Ready, failure: null);
                ScheduleResidency();
                return new(true, true, true);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                await StopProcessCoreAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception exception)
            {
                _diagnostics.Add("host", exception.GetType().Name);
                await StopProcessCoreAsync().ConfigureAwait(false);
                SetState(
                    LocalInferenceModelState.Faulted,
                    ToFailure(exception));
                return new(false, false, false);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task<LocalInferenceStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateSync)
        {
            var processId = TryGetProcessId(_process);
            var isResident = processId is not null &&
                _state is LocalInferenceModelState.Ready or
                    LocalInferenceModelState.Generating;
            IReadOnlyList<LocalInferenceLoadedModel> models = isResident
                ?
                [
                    new LocalInferenceLoadedModel(
                        _configuration.ModelName,
                        "4B",
                        _configuration.Quantization,
                        _configuration.ModelSizeBytes,
                        ResidentBytes: _modelGpuResidentBytes,
                        ContextLength: _configuration.ContextLength,
                        ExpiresAt: null)
                ]
                : [];
            var artifactsPresent =
                _configuration.RuntimeFiles.All(file => File.Exists(
                    Path.Combine(
                        _configuration.RuntimeDirectory,
                        file.Name))) &&
                File.Exists(_configuration.ModelPath);
            var failure = _failure;
            if (!artifactsPresent && failure is null)
            {
                failure = new LocalInferenceFailure(
                    LocalInferenceFailureKind.RuntimeUnavailable,
                    "Pinned local inference artifacts are not staged.");
            }

            return Task.FromResult(new LocalInferenceStatus(
                IsRuntimeAvailable: artifactsPresent,
                _configuration.RuntimeName,
                _configuration.RuntimeVersion,
                artifactsPresent
                    ? _state
                    : LocalInferenceModelState.Faulted,
                models,
                processId,
                IsProcessOwned: processId is not null,
                DateTimeOffset.UtcNow,
                failure,
                _startedAt,
                _port,
                _configuration.Backend,
                _configuration.ContextLength,
                _configuration.ModelSha256,
                GetResidencyRemaining(),
                _diagnostics.Snapshot(),
                _lastLoadDuration,
                _lastGenerationDuration,
                _configuration.ModelName,
                _configuration.Quantization,
                _configuration.ModelSizeBytes,
                _configuration.RuntimeCommit,
                _configuration.GpuLayerCount));
        }
    }

    public async Task<LocalInferenceResult> GenerateAsync(
        LocalInferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        await _generationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var started = await EnsureAvailableAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!started.IsAvailable)
            {
                return new(
                    null,
                    null,
                    Failure: CurrentFailure() ?? new LocalInferenceFailure(
                        LocalInferenceFailureKind.RuntimeUnavailable,
                        "The local inference runtime is unavailable."));
            }

            var baseAddress = _baseAddress ??
                throw new InvalidOperationException(
                    "The inference endpoint was not initialized.");
            var apiKey = _apiKey ??
                throw new InvalidOperationException(
                    "The inference credential was not initialized.");
            CancelResidency();
            SetState(LocalInferenceModelState.Generating, failure: null);
            using var timeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(BoundTimeout(request.Timeout));
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(baseAddress, "v1/chat/completions"));
            message.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
            message.Content = JsonContent.Create(
                CreateChatRequest(request),
                LlamaServerJsonContext.Default.ChatRequest);

            try
            {
                var generationStopwatch = Stopwatch.StartNew();
                using var response = await _httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                ChatResponse? payload;
                try
                {
                    payload = await response.Content.ReadFromJsonAsync(
                        LlamaServerJsonContext.Default.ChatResponse,
                        timeout.Token).ConfigureAwait(false);
                }
                catch (JsonException)
                {
                    return InvalidResponse();
                }

                var choice = payload?.Choices?.FirstOrDefault();
                if (choice?.Message is null)
                {
                    return InvalidResponse();
                }

                generationStopwatch.Stop();
                _lastGenerationDuration = generationStopwatch.Elapsed;
                return new(
                    choice.Message.Content,
                    string.IsNullOrWhiteSpace(payload?.Model)
                        ? _configuration.ModelAlias
                        : payload.Model,
                    ContainsToolCalls(choice.Message.ToolCalls),
                    PromptTokenCount: payload?.Usage?.PromptTokens,
                    OutputTokenCount: payload?.Usage?.CompletionTokens,
                    LoadDuration: started.WasStarted
                        ? _lastLoadDuration
                        : null,
                    GenerationDuration:
                        Milliseconds(payload?.Timings?.PredictedMs));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return new(
                    null,
                    null,
                    Failure: new LocalInferenceFailure(
                        LocalInferenceFailureKind.Timeout,
                        "Local inference timed out."));
            }
            catch (HttpRequestException exception)
            {
                _diagnostics.Add("request", exception.GetType().Name);
                return new(
                    null,
                    null,
                    Failure: new LocalInferenceFailure(
                        LocalInferenceFailureKind.Transport,
                        "The private inference host could not complete the request."));
            }
            finally
            {
                if (IsCurrentProcessAlive())
                {
                    SetState(LocalInferenceModelState.Ready, failure: null);
                    ScheduleResidency();
                }
            }
        }
        finally
        {
            _generationGate.Release();
        }
    }

    public async Task RequestUnloadAsync(
        CancellationToken cancellationToken = default)
    {
        await _generationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await StopProcessCoreAsync().ConfigureAwait(false);
                SetState(LocalInferenceModelState.Asleep, failure: null);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }
        finally
        {
            _generationGate.Release();
        }
    }

    public async Task ShutdownAsync(
        CancellationToken cancellationToken = default)
    {
        await _generationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await _lifecycleGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                if (_shutdownStarted)
                {
                    return;
                }

                _shutdownStarted = true;
                await StopProcessCoreAsync().ConfigureAwait(false);
                SetState(LocalInferenceModelState.Asleep, failure: null);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }
        finally
        {
            _generationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await ShutdownAsync().ConfigureAwait(false);
        }
        finally
        {
            _httpClient.Dispose();
            _lifecycleGate.Dispose();
            _generationGate.Dispose();
        }
    }

    private ProcessStartInfo CreateStartInfo(int port, string apiKey)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _configuration.ExecutablePath,
            WorkingDirectory = _configuration.RuntimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["CUDA_MODULE_LOADING"] = "LAZY";
        LlamaServerArguments.AddTo(
            startInfo,
            _configuration,
            port,
            apiKey);
        return startInfo;
    }

    private async Task WaitForReadinessAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_configuration.StartupTimeout);
        var baseAddress = _baseAddress ??
            throw new InvalidOperationException(
                "The inference endpoint was not initialized.");
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "The private inference process exited during startup.");
            }

            try
            {
                using var response = await _httpClient.GetAsync(
                    new Uri(baseAddress, "health"),
                    timeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    await VerifyAuthenticationAsync(
                        baseAddress,
                        timeout.Token).ConfigureAwait(false);
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The private listener is still starting.
            }

            await Task.Delay(ReadinessPollInterval, timeout.Token)
                .ConfigureAwait(false);
        }
    }

    private async Task VerifyAuthenticationAsync(
        Uri baseAddress,
        CancellationToken cancellationToken)
    {
        using var unauthenticated = await _httpClient.GetAsync(
            new Uri(baseAddress, "v1/models"),
            cancellationToken).ConfigureAwait(false);
        if (unauthenticated.StatusCode != HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "The private inference endpoint did not enforce authentication.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseAddress, "v1/models"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _apiKey);
        using var authenticated = await _httpClient.SendAsync(
            request,
            cancellationToken).ConfigureAwait(false);
        authenticated.EnsureSuccessStatusCode();
    }

    private async Task StopProcessCoreAsync()
    {
        CancelResidency();
        var process = _process;
        var job = _job;
        _process = null;
        _job = null;
        _baseAddress = null;
        _apiKey = null;
        _port = null;
        _startedAt = null;
        _intentionalStop = true;
        if (process is null)
        {
            job?.Dispose();
            return;
        }

        process.Exited -= OnProcessExited;
        process.OutputDataReceived -= OnOutputDataReceived;
        process.ErrorDataReceived -= OnErrorDataReceived;
        try
        {
            job?.Dispose();
            if (!process.HasExited)
            {
                using var timeout = new CancellationTokenSource(
                    _configuration.StopTimeout);
                try
                {
                    await process.WaitForExitAsync(timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (timeout.IsCancellationRequested)
                {
                    process.Kill(entireProcessTree: false);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // The exact owned child exited between inspection and cleanup.
        }
        finally
        {
            process.Dispose();
        }
    }

    private void CleanupExitedProcess()
    {
        var process = _process;
        if (process is null || !process.HasExited)
        {
            return;
        }

        process.Exited -= OnProcessExited;
        process.OutputDataReceived -= OnOutputDataReceived;
        process.ErrorDataReceived -= OnErrorDataReceived;
        process.Dispose();
        _process = null;
        _job?.Dispose();
        _job = null;
        _baseAddress = null;
        _apiKey = null;
        _port = null;
        _startedAt = null;
        CancelResidency();
    }

    private bool IsCurrentProcessAlive()
    {
        try
        {
            return _process is { HasExited: false };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        if (_intentionalStop || !ReferenceEquals(sender, _process))
        {
            return;
        }

        SetState(
            LocalInferenceModelState.Faulted,
            new LocalInferenceFailure(
                LocalInferenceFailureKind.ProcessExited,
                "The private inference host exited unexpectedly."));
    }

    private void OnOutputDataReceived(
        object sender,
        DataReceivedEventArgs args) =>
        _diagnostics.Add("stdout", args.Data);

    private void OnErrorDataReceived(
        object sender,
        DataReceivedEventArgs args)
    {
        _diagnostics.Add("stderr", args.Data);
        if (args.Data is not { } line)
        {
            return;
        }

        var match = GpuModelBufferRegex().Match(line);
        if (!match.Success ||
            !double.TryParse(match.Groups["mib"].Value,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var mebibytes) ||
            !double.IsFinite(mebibytes) ||
            mebibytes <= 0d ||
            mebibytes > 65_536d)
        {
            return;
        }

        var bytes = (long)Math.Round(
            mebibytes * 1024d * 1024d,
            MidpointRounding.AwayFromZero);
        lock (_stateSync)
        {
            _modelGpuResidentBytes = Math.Min(
                64L * 1024L * 1024L * 1024L,
                _modelGpuResidentBytes + bytes);
        }
    }

    [GeneratedRegex(
        @"\bCUDA\d+\s+model buffer size\s*=\s*(?<mib>\d+(?:\.\d+)?)\s+MiB\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex GpuModelBufferRegex();

    private void SetState(
        LocalInferenceModelState state,
        LocalInferenceFailure? failure)
    {
        lock (_stateSync)
        {
            _state = state;
            _failure = failure;
        }
    }

    private LocalInferenceFailure? CurrentFailure()
    {
        lock (_stateSync)
        {
            return _failure;
        }
    }

    private static LocalInferenceFailure ToFailure(Exception exception) =>
        exception switch
        {
            FileNotFoundException or InvalidDataException =>
                new LocalInferenceFailure(
                    LocalInferenceFailureKind.RuntimeUnavailable,
                    "Pinned local inference artifacts failed validation."),
            OperationCanceledException => new LocalInferenceFailure(
                LocalInferenceFailureKind.Timeout,
                "The private inference host did not become ready in time."),
            _ => new LocalInferenceFailure(
                LocalInferenceFailureKind.ProcessExited,
                "The private inference host could not start.")
        };

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string CreateApiKey() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private void ScheduleResidency()
    {
        var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_stateSync)
        {
            previous = _residencyCancellation;
            _residencyCancellation = cancellation;
            _residencyExpiresAt = DateTimeOffset.UtcNow +
                _configuration.ResidencyDuration;
        }

        previous?.Cancel();
        previous?.Dispose();
        _ = ExpireResidencyAsync(cancellation);
    }

    private void CancelResidency()
    {
        CancellationTokenSource? cancellation;
        lock (_stateSync)
        {
            cancellation = _residencyCancellation;
            _residencyCancellation = null;
            _residencyExpiresAt = null;
        }

        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private TimeSpan? GetResidencyRemaining()
    {
        lock (_stateSync)
        {
            if (_residencyExpiresAt is not { } expiresAt)
            {
                return null;
            }

            var remaining = expiresAt - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero
                ? remaining
                : TimeSpan.Zero;
        }
    }

    private async Task ExpireResidencyAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(
                _configuration.ResidencyDuration,
                cancellation.Token).ConfigureAwait(false);
            await _lifecycleGate.WaitAsync(cancellation.Token)
                .ConfigureAwait(false);
            try
            {
                lock (_stateSync)
                {
                    if (!ReferenceEquals(
                            _residencyCancellation,
                            cancellation))
                    {
                        return;
                    }

                    _residencyCancellation = null;
                    _residencyExpiresAt = null;
                }

                await StopProcessCoreAsync().ConfigureAwait(false);
                SetState(LocalInferenceModelState.Asleep, failure: null);
            }
            finally
            {
                _lifecycleGate.Release();
            }
        }
        catch (OperationCanceledException)
            when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private TimeSpan BoundTimeout(TimeSpan? requested)
    {
        if (requested is null || requested <= TimeSpan.Zero)
        {
            return _configuration.GenerationTimeout;
        }

        return requested <= _configuration.GenerationTimeout
            ? requested.Value
            : _configuration.GenerationTimeout;
    }

    private void ValidateRequest(LocalInferenceRequest request)
    {
        if (!string.Equals(
                request.Model,
                _configuration.ModelAlias,
                StringComparison.Ordinal) &&
            !string.Equals(
                request.Model,
                _configuration.ModelName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Only the pinned app-owned model may be requested.",
                nameof(request));
        }

        if (request.ContextLength is <= 0 ||
            request.ContextLength > _configuration.ContextLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The request exceeds the fixed local context limit.");
        }

        if (request.MaximumOutputTokens is <= 0 or > 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "The requested output-token limit is invalid.");
        }

        if (!double.IsFinite(request.Temperature) ||
            request.Temperature is < 0d or > 2d)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        if (request.Messages.Count is < 1 or > 8 ||
            request.Messages.Any(message =>
                string.IsNullOrWhiteSpace(message.Content)))
        {
            throw new ArgumentException(
                "The bounded local inference message set is invalid.",
                nameof(request));
        }

        ValidateOutputJsonSchema(request.OutputJsonSchema);
    }

    private ChatRequest CreateChatRequest(LocalInferenceRequest request) =>
        new(
            _configuration.ModelAlias,
            request.Messages.Select(message => new ChatMessage(
                message.Role switch
                {
                    LocalInferenceMessageRole.System => "system",
                    LocalInferenceMessageRole.User => "user",
                    LocalInferenceMessageRole.Assistant => "assistant",
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(request))
                },
                message.Content)).ToArray(),
            Stream: false,
            request.Temperature,
            request.MaximumOutputTokens,
            ParseOutputJsonSchema(request.OutputJsonSchema),
            new ChatTemplateArguments(
                EnableThinking: !request.DisableReasoning));

    private static void ValidateOutputJsonSchema(string? schema)
    {
        if (schema is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(schema) || schema.Length > 32_768)
        {
            throw new ArgumentException(
                "The bounded output JSON schema is invalid.",
                nameof(schema));
        }

        try
        {
            using var document = JsonDocument.Parse(schema);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException(
                    "The output JSON schema must be an object.",
                    nameof(schema));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The bounded output JSON schema is invalid.",
                nameof(schema),
                exception);
        }
    }

    private static JsonElement? ParseOutputJsonSchema(string? schema)
    {
        if (schema is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }

    private static LocalInferenceResult InvalidResponse() =>
        new(
            null,
            null,
            Failure: new LocalInferenceFailure(
                LocalInferenceFailureKind.InvalidResponse,
                "The local model returned an invalid response."));

    private static bool ContainsToolCalls(JsonElement toolCalls) =>
        toolCalls.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => false,
            JsonValueKind.Array => toolCalls.GetArrayLength() > 0,
            _ => true
        };

    private static TimeSpan? Milliseconds(double? value) =>
        value is { } milliseconds &&
        double.IsFinite(milliseconds) && milliseconds >= 0d
            ? TimeSpan.FromMilliseconds(milliseconds)
            : null;

    private static int? TryGetProcessId(Process? process)
    {
        try
        {
            return process is { HasExited: false } ? process.Id : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] ChatMessage[] Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("max_tokens")] int MaximumTokens,
        [property: JsonPropertyName("json_schema")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        JsonElement? JsonSchema,
        [property: JsonPropertyName("chat_template_kwargs")]
        ChatTemplateArguments ChatTemplateArguments);

    private sealed record ChatTemplateArguments(
        [property: JsonPropertyName("enable_thinking")]
        bool EnableThinking);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatResponse(
        [property: JsonPropertyName("model")] string? Model,
        [property: JsonPropertyName("choices")] ChatChoice[]? Choices,
        [property: JsonPropertyName("usage")] ChatUsage? Usage,
        [property: JsonPropertyName("timings")] ChatTimings? Timings);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatResponseMessage? Message);

    private sealed record ChatResponseMessage(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("tool_calls")] JsonElement ToolCalls);

    private sealed record ChatUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")]
        int? CompletionTokens);

    private sealed record ChatTimings(
        [property: JsonPropertyName("prompt_ms")] double? PromptMs,
        [property: JsonPropertyName("predicted_ms")] double? PredictedMs);

    [JsonSerializable(typeof(ChatRequest))]
    [JsonSerializable(typeof(ChatResponse))]
    private sealed partial class LlamaServerJsonContext
        : JsonSerializerContext;
}
