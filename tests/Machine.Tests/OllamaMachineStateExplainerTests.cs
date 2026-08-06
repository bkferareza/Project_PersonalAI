using System.Net;
using System.Text;
using System.Text.Json;
using Machine.Core;
using Machine.Ollama;

namespace Machine.Tests;

public sealed class OllamaMachineStateExplainerTests
{
    private const string ModelName = "qwen3.5:4b";
    private static readonly Uri LoopbackBaseAddress =
        new("http://127.0.0.1:11434/");

    [Fact]
    public async Task ExplainAsyncSendsRequiredChatRequestAndParsesResponse()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(
                "  Kalma lang, verified load lang ito.  ",
                "qwen3.5:4b-runtime"));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        var explanation = await explainer.ExplainAsync(
            CreateExplanationRequest());

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/chat", handler.RequestUri?.AbsolutePath);
        Assert.Equal(ModelName, handler.RequestJson
            .GetProperty("model")
            .GetString());
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
            0.3d,
            options.GetProperty("temperature").GetDouble());
        Assert.Equal(
            4096,
            options.GetProperty("num_ctx").GetInt32());
        Assert.Equal(
            160,
            options.GetProperty("num_predict").GetInt32());
        Assert.Equal(
            "Kalma lang, verified load lang ito.",
            explanation.Text);
        Assert.Equal("qwen3.5:4b-runtime", explanation.Model);
    }

    [Fact]
    public async Task ExplainAsyncSendsVerifiedMachineSnapshot()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse("Verified snapshot received.", ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        var request = new MachineStateExplanationRequest(
            new MachineIdentity(
                "DESKTOP-VERIFIED",
                "Windows 11 Pro",
                "X64"),
            new MachineResourceSnapshot(
                CpuUsagePercent: 42.5,
                TotalMemoryBytes: 34_359_738_368,
                UsedMemoryBytes: 12_884_901_888,
                CapturedAt: new DateTimeOffset(
                    2026,
                    8,
                    6,
                    10,
                    15,
                    0,
                    TimeSpan.Zero)),
            [
                new MachineProcessSnapshot(
                    ProcessId: 4242,
                    Name: "render-worker",
                    CpuUsagePercent: 17.25,
                    WorkingSetBytes: 536_870_912)
            ]);

        await explainer.ExplainAsync(request);

        var userMessage = GetMessageContent(
            handler.RequestJson,
            "user");
        const string payloadPrefix =
            "Explain this verified machine snapshot:";
        Assert.StartsWith(payloadPrefix, userMessage);

        using var payloadDocument = JsonDocument.Parse(
            userMessage[payloadPrefix.Length..].Trim());
        var payloadJson = payloadDocument.RootElement.GetRawText();

        Assert.Contains("DESKTOP-VERIFIED", payloadJson);
        Assert.Contains("42.5", payloadJson);
        Assert.Contains("12884901888", payloadJson);
        Assert.Contains("34359738368", payloadJson);
        Assert.Contains("render-worker", payloadJson);
        Assert.Contains("4242", payloadJson);
    }

    [Fact]
    public async Task ExplainAsyncSendsBoundedVerifiedContext()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse("Verified context received.", ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        var request = CreateExplanationRequest() with
        {
            Storage = new MachineStorageExplanationContext(
                SystemVolumeRoot: "C:\\",
                TotalSizeBytes: 1_000_000_000_000,
                AvailableSizeBytes: 250_000_000_000,
                LargeFolderScan:
                    new MachineFolderScanExplanationContext(
                        Folders:
                        [
                            new("Fourth", 100, true),
                            new("Users", 400, false),
                            new("Windows", 300, true),
                            new("ProgramData", 200, true)
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
                Names:
                [
                    "Zulu",
                    "alpha",
                    "Echo",
                    "bravo",
                    "Delta",
                    "charlie",
                    "Foxtrot"
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
            250_000_000_000,
            storage.GetProperty("available_bytes").GetInt64());
        var folderScan = storage.GetProperty("large_folder_scan");
        Assert.False(
            folderScan.GetProperty("is_complete").GetBoolean());
        var folders = folderScan.GetProperty("folders")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(3, folders.Length);
        Assert.Equal(
            ["Users", "Windows", "ProgramData"],
            folders.Select(folder =>
                folder.GetProperty("name").GetString()));
        Assert.Equal(
            [400L, 300L, 200L],
            folders.Select(folder =>
                folder.GetProperty("measured_bytes").GetInt64()));
        Assert.False(
            folders[0].GetProperty("is_complete").GetBoolean());
        Assert.DoesNotContain(
            folders,
            folder => folder.GetProperty("name").GetString() ==
                "Fourth");

        var software = payload.GetProperty("software");
        var classic = software.GetProperty("classic_desktop");
        Assert.Equal(
            143,
            classic.GetProperty("registration_count").GetInt32());
        Assert.True(
            classic.GetProperty("is_complete").GetBoolean());
        Assert.Equal(
            0,
            classic.GetProperty("skipped_entry_count").GetInt32());
        var packaged =
            software.GetProperty("packaged_applications");
        Assert.Equal(
            128,
            packaged.GetProperty("registration_count").GetInt32());
        Assert.False(
            packaged.GetProperty("is_complete").GetBoolean());
        Assert.Equal(
            2,
            packaged.GetProperty("skipped_entry_count").GetInt32());

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
        Assert.Equal(
            7,
            startup.GetProperty("machine_count").GetInt32());
        Assert.Equal(
            11,
            startup.GetProperty("current_user_count").GetInt32());
        Assert.False(
            startup.GetProperty("is_complete").GetBoolean());
        Assert.Equal(
            ["alpha", "bravo", "charlie", "Delta", "Echo"],
            startup.GetProperty("names")
                .EnumerateArray()
                .Select(name => name.GetString()));
    }

    [Fact]
    public async Task ExplainAsyncRepresentsUnavailableContextAsNull()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse("Unavailable context noted.", ModelName));
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
    public async Task ExplainAsyncPreservesPartialAndNestedUnavailableState()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse("Partial context noted.", ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        var request = CreateExplanationRequest() with
        {
            Storage = new MachineStorageExplanationContext(
                SystemVolumeRoot: "C:\\",
                TotalSizeBytes: 100,
                AvailableSizeBytes: 25,
                LargeFolderScan: null),
            Software = new MachineSoftwareExplanationContext(
                ClassicDesktop:
                    new MachineSoftwareInventoryExplanationSummary(
                        RegistrationCount: 10,
                        IsComplete: false,
                        SkippedEntryCount: 3),
                PackagedApplications: null),
            Startup = new MachineStartupExplanationContext(
                RegistrationCount: 0,
                RegistryRunCount: 0,
                StartupFolderCount: 0,
                MachineCount: 0,
                CurrentUserCount: 0,
                IsComplete: false,
                Names: Array.Empty<string>())
        };

        await explainer.ExplainAsync(request);

        var payload = GetUserPayload(handler.RequestJson);
        Assert.Equal(
            JsonValueKind.Null,
            payload.GetProperty("storage")
                .GetProperty("large_folder_scan")
                .ValueKind);
        var software = payload.GetProperty("software");
        var classic = software.GetProperty("classic_desktop");
        Assert.False(
            classic.GetProperty("is_complete").GetBoolean());
        Assert.Equal(
            3,
            classic.GetProperty("skipped_entry_count").GetInt32());
        Assert.Equal(
            JsonValueKind.Null,
            software.GetProperty("packaged_applications").ValueKind);
        Assert.False(
            payload.GetProperty("startup")
                .GetProperty("is_complete")
                .GetBoolean());
    }

    [Fact]
    public async Task ExplainAsyncDoesNotSendRawInventoryDetails()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse("Bounded context received.", ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);
        var request = CreateExplanationRequest() with
        {
            Software = new MachineSoftwareExplanationContext(
                ClassicDesktop:
                    new MachineSoftwareInventoryExplanationSummary(
                        1,
                        true,
                        0),
                PackagedApplications:
                    new MachineSoftwareInventoryExplanationSummary(
                        1,
                        true,
                        0)),
            Startup = new MachineStartupExplanationContext(
                RegistrationCount: 1,
                RegistryRunCount: 1,
                StartupFolderCount: 0,
                MachineCount: 0,
                CurrentUserCount: 1,
                IsComplete: true,
                Names: ["Machine Agent"])
        };

        await explainer.ExplainAsync(request);

        var payloadJson = GetUserPayload(handler.RequestJson)
            .GetRawText();
        foreach (var forbiddenText in new[]
        {
            "\"items\"",
            "publisher",
            "version",
            "install_location",
            "package_family",
            "package_full_name",
            "command",
            "command_or_path",
            "startup_path",
            "\"path\""
        })
        {
            Assert.DoesNotContain(
                forbiddenText,
                payloadJson,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ExplainAsyncSendsRequiredSystemGuardrails()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse("Verified facts only.", ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        await explainer.ExplainAsync(CreateExplanationRequest());

        var systemMessage = GetMessageContent(
            handler.RequestJson,
            "system");

        Assert.Contains(
            "natural conversational Filipino Taglish",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Start with one concise overall assessment.",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Do not recite every supplied value.",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Use at most one dry or mildly sarcastic remark.",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "no more than 60 words",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Never invent causes, diagnoses, temperatures, hardware details, processes, or actions.",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Do not claim that you changed, fixed, deleted, stopped, or optimized anything.",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "never infer why a process is running or what the owner is doing",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "never end with an offer, invitation, recommendation, or next step",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "null means unavailable and is_complete false means partial",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Never treat a partial folder measurement as a final folder total.",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "An incomplete folder scan means only that its results are partial; never infer why it is incomplete or how much unmeasured data exists.",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Never claim software is unused, harmful, outdated, or removable.",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Never claim startup entries are enabled, expensive, or safe to disable.",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Never recommend deletion, uninstalling, disabling, cleanup, or optimization.",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "without inventory-style recitation",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplainAsyncWithEmptyContentThrows()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse("   ", ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => explainer.ExplainAsync(
                CreateExplanationRequest()));
    }

    [Fact]
    public async Task ExplainAsyncWithToolCallThrows()
    {
        using var handler = new CapturingHttpMessageHandler(
            ToolCallResponse);
        using var httpClient = CreateHttpClient(handler);
        var explainer = new OllamaMachineStateExplainer(
            httpClient,
            ModelName);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => explainer.ExplainAsync(
                CreateExplanationRequest()));
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
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => explainer.ExplainAsync(
                CreateExplanationRequest(),
                cancellationTokenSource.Token));
        Assert.Equal(0, handler.CallCount);
    }

    private static MachineStateExplanationRequest
        CreateExplanationRequest() =>
            new(
                new MachineIdentity(
                    "DESKTOP-TEST",
                    "Windows 11",
                    "X64"),
                new MachineResourceSnapshot(
                    CpuUsagePercent: 25,
                    TotalMemoryBytes: 16_000_000_000,
                    UsedMemoryBytes: 8_000_000_000,
                    CapturedAt: new DateTimeOffset(
                        2026,
                        8,
                        6,
                        10,
                        0,
                        0,
                        TimeSpan.Zero)),
                [
                    new MachineProcessSnapshot(
                        ProcessId: 100,
                        Name: "test-process",
                        CpuUsagePercent: 5,
                        WorkingSetBytes: 250_000_000)
                ]);

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
                content = "Unexpected tool request.",
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
