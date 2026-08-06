using System.Net;
using System.Text;
using System.Text.Json;
using Machine.Core;
using Machine.Ollama;

namespace Machine.Tests;

public sealed class OllamaMachineStateExplainerTests
{
    private const string ModelName = "qwen3.5:4b";
    private const string StableOpening = "Stable ako ngayon.";
    private static readonly Uri LoopbackBaseAddress =
        new("http://127.0.0.1:11434/");

    [Fact]
    public async Task ExplainAsyncSendsHardenedRequestAndReturnsLocalModel()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(
                $"  {StableOpening} Kumpleto ang current capacity data.  ",
                "qwen3.5:4b-runtime"));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        var explanation = await explainer.ExplainAsync(
            CreateExplanationRequest());

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/chat", handler.RequestUri?.AbsolutePath);
        Assert.Equal(
            ModelName,
            handler.RequestJson.GetProperty("model").GetString());
        Assert.False(handler.RequestJson
            .GetProperty("stream")
            .GetBoolean());
        Assert.False(handler.RequestJson
            .GetProperty("think")
            .GetBoolean());
        Assert.Equal(
            "5m",
            handler.RequestJson
                .GetProperty("keep_alive")
                .GetString());
        var options = handler.RequestJson.GetProperty("options");
        Assert.Equal(
            0.1d,
            options.GetProperty("temperature").GetDouble());
        Assert.Equal(
            4096,
            options.GetProperty("num_ctx").GetInt32());
        Assert.Equal(
            96,
            options.GetProperty("num_predict").GetInt32());
        Assert.Equal(
            $"{StableOpening} Kumpleto ang current capacity data.",
            explanation.Text);
        Assert.Equal("qwen3.5:4b-runtime", explanation.Model);
        Assert.Equal(
            MachineExplanationSource.LocalModel,
            explanation.Source);
    }

    [Fact]
    public async Task ExplainAsyncPayloadExcludesAllProcessData()
    {
        const string requiredOpening =
            "Medyo busy ako ngayon—72.4% ang CPU usage.";
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(requiredOpening, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        var request = CreateExplanationRequest() with
        {
            Resources = new MachineResourceSnapshot(
                CpuUsagePercent: 72.4d,
                TotalMemoryBytes: 34_359_738_368,
                UsedMemoryBytes: 12_884_901_888,
                CapturedAt: DateTimeOffset.UnixEpoch),
            TopProcesses =
            [
                new MachineProcessSnapshot(
                    ProcessId: 4242,
                    Name: "render-worker-unique",
                    CpuUsagePercent: 17.25d,
                    WorkingSetBytes: 536_870_913)
            ],
            Findings = new MachineFindingsSnapshot(
                OverallState: MachineOverallState.Attention,
                Findings:
                [
                    CreateFinding(
                        "cpu.usage.high",
                        MachineFindingSeverity.Attention)
                ])
        };

        await explainer.ExplainAsync(request);

        var payload = GetUserPayload(handler.RequestJson);
        Assert.Equal(
            requiredOpening,
            payload.GetProperty("required_opening").GetString());
        Assert.Equal(
            72.4d,
            payload.GetProperty("cpu_usage_percent").GetDouble());
        Assert.Equal(
            12_884_901_888UL,
            payload.GetProperty("used_memory_bytes").GetUInt64());
        Assert.Equal(
            34_359_738_368UL,
            payload.GetProperty("total_memory_bytes").GetUInt64());

        var payloadJson = payload.GetRawText();
        Assert.DoesNotContain(
            "top_processes",
            payloadJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "render-worker-unique",
            payloadJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4242", payloadJson);
        Assert.DoesNotContain("17.25", payloadJson);
        Assert.DoesNotContain("536870913", payloadJson);
        Assert.DoesNotContain(
            "working_set",
            payloadJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "pid",
            payloadJson,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplainAsyncSendsOnlyBoundedAllowedContext()
    {
        const string opening =
            "May critical storage condition akong nakikita ngayon.";
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(opening, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        var request = CreateExplanationRequest() with
        {
            Identity = new MachineIdentity(
                "DESKTOP-PRIVATE",
                "Windows 11 Pro",
                "X64"),
            Storage = new MachineStorageExplanationContext(
                SystemVolumeRoot: "C:\\",
                TotalSizeBytes: 1_000_000_000_000,
                AvailableSizeBytes: 9_000_000_000,
                LargeFolderScan:
                    new MachineFolderScanExplanationContext(
                        Folders:
                        [
                            new("Users-private", 400, false),
                            new("Windows-private", 300, true)
                        ],
                        IsComplete: false)),
            Software = new MachineSoftwareExplanationContext(
                ClassicDesktop:
                    new MachineSoftwareInventoryExplanationSummary(
                        RegistrationCount: 143,
                        IsComplete: true,
                        SkippedEntryCount: 0),
                PackagedApplications:
                    new MachineSoftwareInventoryExplanationSummary(
                        RegistrationCount: 128,
                        IsComplete: false,
                        SkippedEntryCount: 2)),
            Startup = new MachineStartupExplanationContext(
                RegistrationCount: 18,
                RegistryRunCount: 14,
                StartupFolderCount: 4,
                MachineCount: 7,
                CurrentUserCount: 11,
                IsComplete: false,
                Names: ["Private Startup Name"]),
            Findings = new MachineFindingsSnapshot(
                OverallState: MachineOverallState.Critical,
                Findings:
                [
                    CreateFinding(
                        "storage.system-volume.low-free-space",
                        MachineFindingSeverity.Critical)
                ])
        };

        await explainer.ExplainAsync(request);

        var payload = GetUserPayload(handler.RequestJson);
        var storage = payload.GetProperty("storage");
        Assert.Equal(
            "C:\\",
            storage.GetProperty("system_volume_root").GetString());
        Assert.Equal(
            1_000_000_000_000,
            storage.GetProperty("total_bytes").GetInt64());
        Assert.Equal(
            9_000_000_000,
            storage.GetProperty("available_bytes").GetInt64());
        Assert.False(storage
            .GetProperty("large_folder_scan_is_complete")
            .GetBoolean());

        var software = payload.GetProperty("software");
        Assert.Equal(
            143,
            software.GetProperty("classic_desktop")
                .GetProperty("registration_count")
                .GetInt32());
        Assert.Equal(
            128,
            software.GetProperty("packaged_applications")
                .GetProperty("registration_count")
                .GetInt32());
        var startup = payload.GetProperty("startup");
        Assert.Equal(
            18,
            startup.GetProperty("registration_count").GetInt32());
        Assert.Equal(
            14,
            startup.GetProperty("registry_run_count").GetInt32());
        Assert.Equal(
            4,
            startup.GetProperty("startup_folder_count").GetInt32());
        Assert.False(startup.GetProperty("is_complete").GetBoolean());

        var payloadJson = payload.GetRawText();
        Assert.DoesNotContain("DESKTOP-PRIVATE", payloadJson);
        Assert.DoesNotContain("Windows 11 Pro", payloadJson);
        Assert.DoesNotContain("Users-private", payloadJson);
        Assert.DoesNotContain("Windows-private", payloadJson);
        Assert.DoesNotContain("Private Startup Name", payloadJson);
        Assert.DoesNotContain(
            "folders",
            payloadJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "names",
            payloadJson,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplainAsyncSendsBoundedDeterministicFindings()
    {
        const string opening =
            "May critical condition akong nakikita ngayon.";
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(opening, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        var request = CreateExplanationRequest() with
        {
            Findings = new MachineFindingsSnapshot(
                OverallState: MachineOverallState.Critical,
                Findings:
                [
                    CreateFinding("info-z", MachineFindingSeverity.Info),
                    CreateFinding("attention-b", MachineFindingSeverity.Attention),
                    CreateFinding("warning-b", MachineFindingSeverity.Warning),
                    CreateFinding("info-d", MachineFindingSeverity.Info),
                    CreateFinding("critical-z", MachineFindingSeverity.Critical),
                    CreateFinding("warning-a", MachineFindingSeverity.Warning),
                    CreateFinding("info-c", MachineFindingSeverity.Info),
                    CreateFinding("attention-a", MachineFindingSeverity.Attention),
                    CreateFinding("info-b", MachineFindingSeverity.Info),
                    CreateFinding("info-a", MachineFindingSeverity.Info)
                ])
        };

        await explainer.ExplainAsync(request);

        var findingsSnapshot = GetUserPayload(handler.RequestJson)
            .GetProperty("findings");
        Assert.Equal(
            "Critical",
            findingsSnapshot.GetProperty("overall_state").GetString());
        var findings = findingsSnapshot.GetProperty("findings")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(8, findings.Length);
        Assert.Equal(
            [
                "critical-z",
                "warning-a",
                "warning-b",
                "attention-a",
                "attention-b",
                "info-a",
                "info-b",
                "info-c"
            ],
            findings.Select(finding =>
                finding.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task ExplainAsyncRepresentsUnavailableContextAsNull()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(StableOpening, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        await explainer.ExplainAsync(CreateExplanationRequest());

        var payload = GetUserPayload(handler.RequestJson);
        Assert.Equal(
            JsonValueKind.Null,
            payload.GetProperty("storage").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            payload.GetProperty("software").ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            payload.GetProperty("startup").ValueKind);
    }

    [Fact]
    public async Task ExplainAsyncSendsRequiredSystemGuardrails()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(StableOpening, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        await explainer.ExplainAsync(CreateExplanationRequest());

        var systemMessage = GetMessageContent(
            handler.RequestJson,
            "system");
        Assert.Contains(
            "required_opening",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "at most one short supporting observation",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Never mention a process name",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "no more than 45 words",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "declarative sentences only",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Never discuss permission",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Never attribute a cause unless an exact deterministic finding",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "process kasi",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Do not mention being an AI, language model, or Ollama.",
            systemMessage,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ibang opening ito.")]
    [InlineData("Stable ako ngayon. Code ang sanhi nito.")]
    [InlineData("Stable ako ngayon. Sabihin mo lang at aayusin ko.")]
    [InlineData("Stable ako ngayon. Okay ba talaga?")]
    public async Task ExplainAsyncRejectsUnsafeOutputWithoutRetry(
        string modelOutput)
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(modelOutput, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        var request = CreateExplanationRequest() with
        {
            TopProcesses =
            [
                new MachineProcessSnapshot(1, "Code", 1d, 1)
            ]
        };

        var explanation = await explainer.ExplainAsync(request);

        Assert.Equal(StableOpening, explanation.Text);
        Assert.Equal(
            MachineExplanationSource.DeterministicFallback,
            explanation.Source);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExplainAsyncFallbackUsesApplicableFindingAndSource()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse("   ", ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        var request = CreateExplanationRequest() with
        {
            Findings = new MachineFindingsSnapshot(
                OverallState: MachineOverallState.Stable,
                Findings:
                [
                    new MachineFinding(
                        Code: "data.folder-scan.partial",
                        Severity: MachineFindingSeverity.Info,
                        Title: "Storage inspection is partial",
                        Detail: "Measured folder sizes are lower bounds.")
                ])
        };

        var explanation = await explainer.ExplainAsync(request);

        Assert.Equal(
            "Stable ako ngayon. Partial pa ang storage inspection, " +
                "kaya lower bounds lang ang measured folder sizes.",
            explanation.Text);
        Assert.Equal(ModelName, explanation.Model);
        Assert.Equal(
            MachineExplanationSource.DeterministicFallback,
            explanation.Source);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExplainAsyncWithToolCallReturnsFallbackWithoutRetry()
    {
        using var handler = new CapturingHttpMessageHandler(
            ToolCallResponse);
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        var explanation = await explainer.ExplainAsync(
            CreateExplanationRequest());

        Assert.Equal(StableOpening, explanation.Text);
        Assert.Equal(
            MachineExplanationSource.DeterministicFallback,
            explanation.Source);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExplainAsyncWithMalformedResponseReturnsFallback()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{not-json",
                    Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        var explanation = await explainer.ExplainAsync(
            CreateExplanationRequest());

        Assert.Equal(StableOpening, explanation.Text);
        Assert.Equal(
            MachineExplanationSource.DeterministicFallback,
            explanation.Source);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExplainAsyncWithTransportFailureStillThrows()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            explainer.ExplainAsync(CreateExplanationRequest()));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExplainAsyncWithTimeoutStillThrows()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            throw new TaskCanceledException("Simulated timeout."));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            explainer.ExplainAsync(CreateExplanationRequest()));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExplainAsyncWithPreCancelledTokenThrows()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            throw new InvalidOperationException(
                "No request should be sent for caller cancellation."));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        using var cancellationTokenSource =
            new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            explainer.ExplainAsync(
                CreateExplanationRequest(),
                cancellationTokenSource.Token));
        Assert.Equal(0, handler.CallCount);
    }

    private static MachineStateExplanationRequest
        CreateExplanationRequest() =>
        new(
            Identity: new MachineIdentity(
                "DESKTOP-TEST",
                "Windows 11",
                "X64"),
            Resources: new MachineResourceSnapshot(
                CpuUsagePercent: 25d,
                TotalMemoryBytes: 16_000_000_000,
                UsedMemoryBytes: 8_000_000_000,
                CapturedAt: DateTimeOffset.UnixEpoch),
            TopProcesses:
            [
                new MachineProcessSnapshot(
                    ProcessId: 100,
                    Name: "test-process",
                    CpuUsagePercent: 5d,
                    WorkingSetBytes: 250_000_000)
            ],
            Findings: new MachineFindingsSnapshot(
                OverallState: MachineOverallState.Stable,
                Findings: Array.Empty<MachineFinding>()));

    private static MachineFinding CreateFinding(
        string code,
        MachineFindingSeverity severity) =>
        new(
            Code: code,
            Severity: severity,
            Title: $"Title for {code}",
            Detail: $"Detail for {code}.");

    private static HttpClient CreateHttpClient(
        HttpMessageHandler handler) =>
        new(handler)
        {
            BaseAddress = LoopbackBaseAddress
        };

    private static HttpResponseMessage ChatResponse(
        string content,
        string model)
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            model,
            message = new
            {
                role = "assistant",
                content
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                responseJson,
                Encoding.UTF8,
                "application/json")
        };
    }

    private static HttpResponseMessage ToolCallResponse()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            model = ModelName,
            message = new
            {
                role = "assistant",
                content = StableOpening,
                tool_calls = new[]
                {
                    new
                    {
                        function = new
                        {
                            name = "not_allowed"
                        }
                    }
                }
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                responseJson,
                Encoding.UTF8,
                "application/json")
        };
    }

    private static string GetMessageContent(
        JsonElement requestJson,
        string role) =>
        requestJson
            .GetProperty("messages")
            .EnumerateArray()
            .Single(message =>
                message.GetProperty("role").GetString() == role)
            .GetProperty("content")
            .GetString() ?? string.Empty;

    private static JsonElement GetUserPayload(
        JsonElement requestJson)
    {
        const string payloadPrefix =
            "Explain this verified machine snapshot:";
        var userMessage = GetMessageContent(requestJson, "user");
        Assert.StartsWith(payloadPrefix, userMessage);

        using var payloadDocument = JsonDocument.Parse(
            userMessage[payloadPrefix.Length..].Trim());

        return payloadDocument.RootElement.Clone();
    }

    private sealed class CapturingHttpMessageHandler(
        Func<HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public JsonElement RequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;

            var requestBody = request.Content is null
                ? throw new InvalidOperationException(
                    "Expected a JSON request body.")
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);
            using var requestDocument = JsonDocument.Parse(
                requestBody);
            RequestJson = requestDocument.RootElement.Clone();

            return responseFactory();
        }
    }
}
