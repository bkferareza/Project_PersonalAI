using System.Net;
using System.Text;
using System.Text.Json;
using Machine.Core;

namespace Machine.Tests;

public sealed class LocalMachineIntelligenceGeneratorTests
{
    private const string ModelName = "qwen3.5:4b";
    private const string StableInsight =
        "No deterministic issue is visible in the current snapshot.";
    private const string StableFallback =
        "No deterministic issue is visible in the current snapshot.";
    private static readonly Uri LoopbackBaseAddress =
        new("http://127.0.0.1:24001/");

    [Fact]
    public async Task ExplainAsyncSendsHardenedRequestAndReturnsLocalModel()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(
                $"  {StableInsight}  ",
                "qwen3.5:4b-runtime"));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
            httpClient,
            ModelName);

        var explanation = await explainer.ExplainAsync(
            CreateExplanationRequest());

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/capture", handler.RequestUri?.AbsolutePath);
        Assert.Equal(
            ModelName,
            handler.RequestJson.GetProperty("model").GetString());
        Assert.True(handler.RequestJson
            .GetProperty("disable_reasoning")
            .GetBoolean());
        Assert.Equal(
            (long)TimeSpan.FromMinutes(2).TotalMilliseconds,
            handler.RequestJson
                .GetProperty("timeout_ms")
                .GetInt64());
        var options = handler.RequestJson.GetProperty("options");
        Assert.Equal(
            0.1d,
            options.GetProperty("temperature").GetDouble());
        Assert.Equal(
            4096,
            options.GetProperty("context_length").GetInt32());
        Assert.Equal(
            96,
            options.GetProperty("maximum_output_tokens").GetInt32());
        Assert.Equal(
            StableInsight,
            explanation.Text);
        Assert.Equal("qwen3.5:4b-runtime", explanation.Model);
        Assert.Equal(
            MachineExplanationSource.LocalModel,
            explanation.Source);
    }

    [Fact]
    public async Task ExplainAsyncPayloadExcludesAllProcessData()
    {
        const string insight =
            "Current CPU usage is 72.4%.";
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(insight, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
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
        Assert.False(payload.TryGetProperty(
            "required_opening",
            out _));
        Assert.Equal(
            72.4d,
            payload.GetProperty("cpu_usage_percent").GetDouble());
        Assert.Equal(
            37.5d,
            payload.GetProperty("memory_usage_percent").GetDouble());
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
        const string insight =
            "The verified storage condition is critical.";
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(insight, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
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
    public async Task ExplainAsyncSendsOnlyBoundedHistoryContext()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(StableInsight, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
            httpClient,
            ModelName);
        var current = new MachineHistoryInsightPeriod(
            DateTimeOffset.Parse("2026-08-14T11:00:00Z"),
            DateTimeOffset.Parse("2026-08-14T12:00:00Z"),
            3_300,
            24,
            51,
            1_000,
            500,
            42,
            48,
            54,
            108);
        var request = CreateExplanationRequest() with
        {
            History = new(
                current,
                current with
                {
                    StartedAt = current.StartedAt.AddDays(-7),
                    EndedAt = current.EndedAt.AddDays(-7),
                    CpuMeanPercent = 17
                },
                new(
                    DateTimeOffset.Parse("2026-08-14T04:00:00Z"),
                    MachineHistoryEventKind.ApplicationFailureRecorded,
                    "Application failure recorded",
                    "sample.exe",
                    2))
        };

        await explainer.ExplainAsync(request);

        var history = GetUserPayload(handler.RequestJson)
            .GetProperty("history");
        Assert.Equal(24,
            history.GetProperty("current_period")
                .GetProperty("cpu_mean_percent").GetDouble());
        Assert.Equal(17,
            history.GetProperty("recent_comparable")
                .GetProperty("cpu_mean_percent").GetDouble());
        Assert.Equal(2,
            history.GetProperty("significant_event")
                .GetProperty("count").GetInt32());
        var json = history.GetRawText();
        Assert.DoesNotContain("fingerprint", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rollups", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("series", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplainAsyncSendsOnlyTinyCurrentGpuContext()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(StableInsight, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
            httpClient,
            ModelName);
        var request = CreateExplanationRequest() with
        {
            Gpu = new(
                UtilizationPercent: 37,
                MemoryUtilizationPercent: 46,
                TemperatureCelsius: 58,
                BoardPowerWatts: null)
        };

        await explainer.ExplainAsync(request);

        var gpu = GetUserPayload(handler.RequestJson).GetProperty("gpu");
        Assert.Equal(37,
            gpu.GetProperty("utilization_percent").GetDouble());
        Assert.Equal(46,
            gpu.GetProperty("memory_utilization_percent").GetDouble());
        Assert.Equal(58,
            gpu.GetProperty("temperature_celsius").GetDouble());
        Assert.Equal(JsonValueKind.Null,
            gpu.GetProperty("board_power_watts").ValueKind);
        var json = gpu.GetRawText();
        Assert.DoesNotContain("adapter", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("name", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clock", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fan", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplainAsyncSendsBoundedDeterministicFindings()
    {
        const string insight =
            "The verified condition is critical.";
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(insight, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
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
            ChatResponse(StableInsight, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
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
            ChatResponse(StableInsight, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
            httpClient,
            ModelName);

        await explainer.ExplainAsync(CreateExplanationRequest());

        var systemMessage = GetMessageContent(
            handler.RequestJson,
            "system");
        Assert.DoesNotContain(
            "required_opening",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "renders the deterministic overall state separately",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "one or two short sentences",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Never mention a process name",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "no more than 55 words",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Do not recite every supplied metric",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Keep RAM memory and drive storage separate",
            systemMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "no folder-scan result is available",
            systemMessage,
            StringComparison.Ordinal);
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
            "English only",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "concise, natural, precise English",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Taglish",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Filipino",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "All numeric calculations are already complete",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Never calculate, convert, round, or invent",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "provided formatted monetary values",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Never translate currency",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "observed, learned, expected, estimated, and projected",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "needs to spend or pay",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "household bill",
            systemMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Do not mention being an AI or language model.",
            systemMessage,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Under pressure ako ngayon.")]
    [InlineData("Stable ako ngayon. Code ang sanhi nito.")]
    [InlineData("Stable ako ngayon. Sabihin mo lang at aayusin ko.")]
    [InlineData("Stable ako ngayon. Okay ba talaga?")]
    [InlineData("Okay ba talaga ang current state.")]
    [InlineData("AI ang gumawa ng insight na ito.")]
    [InlineData("Kasi mataas ang load, alerto ako.")]
    [InlineData("Malubha ang kondisyon ng machine.")]
    [InlineData("Ang memory ay may sapat na available space sa C drive.")]
    [InlineData("Walang nakita ang scan na malaking folder.")]
    [InlineData("CPU ay nasa 25% habang ang memory ay gumagamit ng 30%.")]
    [InlineData("CPU ay nasa 99 percent.")]
    public async Task ExplainAsyncRejectsUnsafeOutputWithoutRetry(
        string modelOutput)
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(modelOutput, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
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

        Assert.Equal(StableFallback, explanation.Text);
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
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
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
            "The storage inspection is partial, so measured folder sizes " +
                "are lower bounds.",
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
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
            httpClient,
            ModelName);

        var explanation = await explainer.ExplainAsync(
            CreateExplanationRequest());

        Assert.Equal(StableFallback, explanation.Text);
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
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
            httpClient,
            ModelName);

        var explanation = await explainer.ExplainAsync(
            CreateExplanationRequest());

        Assert.Equal(StableFallback, explanation.Text);
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
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
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
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
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
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
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

    [Fact]
    public async Task ExplicitInsightExplanationCarriesOnlyBoundedInsightEvidence()
    {
        using var handler = new CapturingHttpMessageHandler(() =>
            ChatResponse(StableInsight, ModelName));
        using var httpClient = CreateHttpClient(handler);
        var explainer = new LocalMachineIntelligenceGeneratorTestHarness(
            httpClient,
            ModelName);
        var request = CreateExplanationRequest() with
        {
            CurrentInsight = new MachineInsightExplainContext(
                "learned-energy-today-above",
                MachineInsightKind.LearnedEnergyDeviation,
                "Running heavier than usual",
                "~0.620 kWh observed today",
                "Established range 0.450–0.550 kWh.",
                "Established · 100% learned coverage",
                ActualObservedEnergyKilowattHours: 0.620d,
                ObservedDurationSeconds: 5_400,
                ExpectedObservedEnergyKilowattHours: 0.500d,
                ExpectedLowerEnergyKilowattHours: 0.450d,
                ExpectedUpperEnergyKilowattHours: 0.550d,
                DifferenceKilowattHours: 0.120d,
                DifferencePercent: 24d,
                LearnedCoverage: 1d,
                EvidenceMaturity:
                    MachineLearningEvidenceMaturity.Established,
                ActualEstimatedCost: 9.16m,
                ExpectedEstimatedCost: 7.39m,
                ExpectedLowerCost: 6.65m,
                ExpectedUpperCost: 8.13m,
                ElectricityProvider: "Meralco",
                CurrencyCode: "PHP",
                RatePerKilowattHour: 14.7833m,
                RateEffectiveMonth: new DateOnly(2026, 8, 1))
        };

        await explainer.ExplainAsync(request);

        var payload = GetUserPayload(handler.RequestJson);
        var insight = payload.GetProperty("current_insight");
        Assert.Equal("learned-energy-today-above",
            insight.GetProperty("candidate_id").GetString());
        Assert.Equal("Established",
            insight.GetProperty("evidence_maturity").GetString());
        Assert.Equal(5_400,
            insight.GetProperty("observed_duration_seconds").GetInt64());
        Assert.Equal(0.620d,
            insight.GetProperty("actual_observed_kwh").GetDouble());
        Assert.Equal(0.450d,
            insight.GetProperty("expected_lower_kwh").GetDouble());
        Assert.Equal(0.550d,
            insight.GetProperty("expected_upper_kwh").GetDouble());
        Assert.Equal(1d,
            insight.GetProperty("learned_coverage").GetDouble());
        Assert.DoesNotContain("rollups", insight.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profiles", insight.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("process", insight.GetRawText(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("cpu_usage_percent").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("history").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            payload.GetProperty("learned_context").ValueKind);
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
                content = StableInsight,
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
