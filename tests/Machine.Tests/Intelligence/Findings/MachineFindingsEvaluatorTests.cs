using Machine.Core;

namespace Machine.Tests;

public sealed class MachineFindingsEvaluatorTests
{
    private const long Gibibyte = 1024L * 1024L * 1024L;

    public static TheoryData<long, long, MachineFindingSeverity?>
        StorageBoundaryCases =>
        new()
        {
            {
                1_000L * Gibibyte,
                10L * Gibibyte,
                MachineFindingSeverity.Critical
            },
            {
                1_000L * Gibibyte,
                11L * Gibibyte,
                MachineFindingSeverity.Warning
            },
            {
                1_000L * Gibibyte,
                50L * Gibibyte,
                MachineFindingSeverity.Warning
            },
            {
                1_000L * Gibibyte,
                51L * Gibibyte,
                MachineFindingSeverity.Attention
            },
            {
                1_000L * Gibibyte,
                100L * Gibibyte,
                MachineFindingSeverity.Attention
            },
            {
                1_000L * Gibibyte,
                101L * Gibibyte,
                null
            },
            {
                50L * Gibibyte,
                Gibibyte,
                MachineFindingSeverity.Critical
            },
            {
                50L * Gibibyte,
                Gibibyte + 1,
                MachineFindingSeverity.Warning
            },
            {
                50L * Gibibyte,
                5L * Gibibyte,
                MachineFindingSeverity.Warning
            },
            {
                50L * Gibibyte,
                5L * Gibibyte + 1,
                MachineFindingSeverity.Attention
            },
            {
                100L * Gibibyte,
                20L * Gibibyte,
                MachineFindingSeverity.Attention
            },
            {
                100L * Gibibyte,
                20L * Gibibyte + 1,
                null
            }
        };

    [Theory]
    [InlineData(69.999d, null)]
    [InlineData(70d, MachineFindingSeverity.Attention)]
    [InlineData(89.999d, MachineFindingSeverity.Attention)]
    [InlineData(90d, MachineFindingSeverity.Warning)]
    public void EvaluateAppliesCpuBoundaries(
        double cpuUsagePercent,
        MachineFindingSeverity? expectedSeverity)
    {
        var snapshot = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: CreateResources(
                    cpuUsagePercent,
                    usedMemoryBytes: 500,
                    totalMemoryBytes: 1_000)));

        AssertFindingSeverity(
            snapshot,
            "cpu.usage.high",
            expectedSeverity);
    }

    [Theory]
    [InlineData(799, 1_000, null)]
    [InlineData(800, 1_000, MachineFindingSeverity.Attention)]
    [InlineData(899, 1_000, MachineFindingSeverity.Attention)]
    [InlineData(900, 1_000, MachineFindingSeverity.Warning)]
    public void EvaluateAppliesMemoryBoundaries(
        int usedMemoryBytes,
        int totalMemoryBytes,
        MachineFindingSeverity? expectedSeverity)
    {
        var snapshot = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: CreateResources(
                    cpuUsagePercent: 0,
                    usedMemoryBytes: (ulong)usedMemoryBytes,
                    totalMemoryBytes: (ulong)totalMemoryBytes)));

        AssertFindingSeverity(
            snapshot,
            "memory.usage.high",
            expectedSeverity);
    }

    [Theory]
    [MemberData(nameof(StorageBoundaryCases))]
    public void EvaluateAppliesStorageBoundaries(
        long totalSizeBytes,
        long availableSizeBytes,
        MachineFindingSeverity? expectedSeverity)
    {
        var snapshot = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Storage: CreateStorage(
                    totalSizeBytes,
                    availableSizeBytes)));

        AssertFindingSeverity(
            snapshot,
            "storage.system-volume.low-free-space",
            expectedSeverity);
    }

    [Fact]
    public void EvaluateCreatesOnlyHighestFindingPerMetric()
    {
        var snapshot = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: CreateResources(
                    cpuUsagePercent: 95,
                    usedMemoryBytes: 950,
                    totalMemoryBytes: 1_000),
                Storage: CreateStorage(
                    totalSizeBytes: 100L * Gibibyte,
                    availableSizeBytes: Gibibyte)));

        Assert.Equal(3, snapshot.Findings.Count);
        Assert.Equal(
            MachineFindingSeverity.Warning,
            Assert.Single(snapshot.Findings, finding =>
                finding.Code == "cpu.usage.high").Severity);
        Assert.Equal(
            MachineFindingSeverity.Warning,
            Assert.Single(snapshot.Findings, finding =>
                finding.Code == "memory.usage.high").Severity);
        Assert.Equal(
            MachineFindingSeverity.Critical,
            Assert.Single(snapshot.Findings, finding =>
                finding.Code ==
                    "storage.system-volume.low-free-space").Severity);
    }

    [Fact]
    public void EvaluateCreatesPartialDataQualityFindingsWithCounts()
    {
        var snapshot = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: CreateResources(10, 500, 1_000),
                FolderInspection: new MachineFolderInspectionSnapshot(
                    RootPath: "C:\\",
                    Folders:
                    [
                        new MachineFolderSizeSnapshot(
                            Path: "C:\\Users",
                            SizeBytes: 900L * Gibibyte,
                            FileCount: 10,
                            IsComplete: false)
                    ],
                    IsComplete: false,
                    SkippedDirectoryCount: 10,
                    CapturedAt: DateTimeOffset.UnixEpoch),
                ClassicSoftware: new MachineSoftwareInventorySnapshot(
                    Items: Array.Empty<MachineInstalledSoftwareSnapshot>(),
                    IsComplete: false,
                    SkippedEntryCount: 2,
                    CapturedAt: DateTimeOffset.UnixEpoch),
                PackagedSoftware:
                    new MachinePackagedSoftwareInventorySnapshot(
                        Items:
                            Array.Empty<MachinePackagedSoftwareSnapshot>(),
                        IsComplete: false,
                        SkippedEntryCount: 3,
                        OptionalPropertyFailureCount: 0,
                        ExcludedFrameworkPackageCount: 0,
                        ExcludedResourcePackageCount: 0,
                        CapturedAt: DateTimeOffset.UnixEpoch),
                Startup: new MachineStartupInventorySnapshot(
                    Items:
                        Array.Empty<MachineStartupApplicationSnapshot>(),
                    IsComplete: false,
                    ReadFailureCount: 4,
                    CapturedAt: DateTimeOffset.UnixEpoch)));

        Assert.Equal(MachineOverallState.Stable, snapshot.OverallState);
        Assert.Equal(4, snapshot.Findings.Count);
        Assert.All(
            snapshot.Findings,
            finding => Assert.Equal(
                MachineFindingSeverity.Info,
                finding.Severity));
        Assert.Contains("10", snapshot.Findings[0].Detail);
        Assert.Contains("2", snapshot.Findings[1].Detail);
        Assert.Contains("3", snapshot.Findings[2].Detail);
        Assert.Contains("4", snapshot.Findings[3].Detail);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(101, 100)]
    public void EvaluateIgnoresInvalidMemoryTotals(
        int usedMemoryBytes,
        int totalMemoryBytes)
    {
        var snapshot = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: CreateResources(
                    cpuUsagePercent: 10,
                    usedMemoryBytes: (ulong)usedMemoryBytes,
                    totalMemoryBytes: (ulong)totalMemoryBytes)));

        Assert.DoesNotContain(
            snapshot.Findings,
            finding => finding.Code == "memory.usage.high");
        Assert.Equal(MachineOverallState.Stable, snapshot.OverallState);
    }

    [Fact]
    public void EvaluateReturnsUnknownWithoutUsableCapacityState()
    {
        var noSnapshots = MachineFindingsEvaluator.Evaluate(new());
        var nonSystemVolume = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Storage: new MachineStorageSnapshot(
                    Volumes:
                    [
                        new MachineStorageVolumeSnapshot(
                            RootPath: "D:\\",
                            VolumeLabel: null,
                            FileSystem: "NTFS",
                            TotalSizeBytes: 100L * Gibibyte,
                            AvailableFreeSpaceBytes: 50L * Gibibyte,
                            IsSystemVolume: false)
                    ],
                    CapturedAt: DateTimeOffset.UnixEpoch)));
        var invalidSystemVolume = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Storage: CreateStorage(
                    totalSizeBytes: 0,
                    availableSizeBytes: 0)));

        Assert.Equal(
            MachineOverallState.Unknown,
            noSnapshots.OverallState);
        Assert.Equal(
            MachineOverallState.Unknown,
            nonSystemVolume.OverallState);
        Assert.Equal(
            MachineOverallState.Unknown,
            invalidSystemVolume.OverallState);
    }

    [Fact]
    public void EvaluateReturnsStableForTelemetryOrReadableStorage()
    {
        var telemetryOnly = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: CreateResources(20, 500, 1_000)));
        var storageOnly = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Storage: CreateStorage(
                    totalSizeBytes: 100L * Gibibyte,
                    availableSizeBytes: 50L * Gibibyte)));

        Assert.Equal(
            MachineOverallState.Stable,
            telemetryOnly.OverallState);
        Assert.Empty(telemetryOnly.Findings);
        Assert.Equal(
            MachineOverallState.Stable,
            storageOnly.OverallState);
        Assert.Empty(storageOnly.Findings);
    }

    [Fact]
    public void EvaluateAggregatesHighestNonInfoSeverity()
    {
        var attention = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: CreateResources(70, 500, 1_000)));
        var warning = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: CreateResources(90, 500, 1_000)));
        var critical = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: CreateResources(90, 500, 1_000),
                Storage: CreateStorage(
                    totalSizeBytes: 100L * Gibibyte,
                    availableSizeBytes: Gibibyte)));

        Assert.Equal(
            MachineOverallState.Attention,
            attention.OverallState);
        Assert.Equal(
            MachineOverallState.Warning,
            warning.OverallState);
        Assert.Equal(
            MachineOverallState.Critical,
            critical.OverallState);
    }

    [Fact]
    public void EvaluateSortsBySeverityThenStableCode()
    {
        var snapshot = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: CreateResources(95, 950, 1_000),
                Storage: CreateStorage(
                    totalSizeBytes: 100L * Gibibyte,
                    availableSizeBytes: Gibibyte),
                ClassicSoftware: new MachineSoftwareInventorySnapshot(
                    Items: Array.Empty<MachineInstalledSoftwareSnapshot>(),
                    IsComplete: false,
                    SkippedEntryCount: 0,
                    CapturedAt: DateTimeOffset.UnixEpoch),
                Startup: new MachineStartupInventorySnapshot(
                    Items:
                        Array.Empty<MachineStartupApplicationSnapshot>(),
                    IsComplete: false,
                    ReadFailureCount: 0,
                    CapturedAt: DateTimeOffset.UnixEpoch)));

        Assert.Equal(
            [
                "storage.system-volume.low-free-space",
                "cpu.usage.high",
                "memory.usage.high",
                "data.software.classic.partial",
                "data.startup.partial"
            ],
            snapshot.Findings.Select(finding => finding.Code));
    }

    [Fact]
    public void EvaluateDoesNotInferFindingsFromCountsOrFolderSizes()
    {
        var snapshot = MachineFindingsEvaluator.Evaluate(
            new MachineFindingsInput(
                Resources: CreateResources(10, 500, 1_000),
                FolderInspection: new MachineFolderInspectionSnapshot(
                    RootPath: "C:\\",
                    Folders:
                    [
                        new MachineFolderSizeSnapshot(
                            Path: "C:\\Large",
                            SizeBytes: long.MaxValue,
                            FileCount: long.MaxValue,
                            IsComplete: true)
                    ],
                    IsComplete: true,
                    SkippedDirectoryCount: 0,
                    CapturedAt: DateTimeOffset.UnixEpoch),
                ClassicSoftware: new MachineSoftwareInventorySnapshot(
                    Items: Enumerable.Range(0, 250)
                        .Select(index =>
                            new MachineInstalledSoftwareSnapshot(
                                Name: $"Classic {index}",
                                Version: null,
                                Publisher: null,
                                InstallLocation: null,
                                EstimatedSizeBytes: null,
                                Scope: MachineSoftwareScope.LocalMachine,
                                RegistryView:
                                    MachineSoftwareRegistryView.Registry64))
                        .ToArray(),
                    IsComplete: true,
                    SkippedEntryCount: 0,
                    CapturedAt: DateTimeOffset.UnixEpoch),
                PackagedSoftware:
                    new MachinePackagedSoftwareInventorySnapshot(
                        Items: Enumerable.Range(0, 250)
                            .Select(index =>
                                new MachinePackagedSoftwareSnapshot(
                                    DisplayName: $"Package {index}",
                                    PublisherDisplayName: null,
                                    PackageFamilyName:
                                        $"PackageFamily{index}",
                                    PackageFullName:
                                        $"PackageFullName{index}",
                                    Version: "1.0.0.0",
                                    Architecture:
                                        MachinePackagedSoftwareArchitecture.X64,
                                    InstalledLocation: null,
                                    IsDevelopmentMode: false,
                                    IsStub: false))
                            .ToArray(),
                        IsComplete: true,
                        SkippedEntryCount: 0,
                        OptionalPropertyFailureCount: 0,
                        ExcludedFrameworkPackageCount: int.MaxValue,
                        ExcludedResourcePackageCount: int.MaxValue,
                        CapturedAt: DateTimeOffset.UnixEpoch),
                Startup: new MachineStartupInventorySnapshot(
                    Items: Enumerable.Range(0, 250)
                        .Select(index =>
                            new MachineStartupApplicationSnapshot(
                                Name: $"Startup {index}",
                                CommandOrPath: $"command-{index}",
                                Source:
                                    MachineStartupSource.RegistryRunKey,
                                Scope: MachineStartupScope.CurrentUser,
                                RegistryView:
                                    MachineStartupRegistryView.Registry64))
                        .ToArray(),
                    IsComplete: true,
                    ReadFailureCount: 0,
                    CapturedAt: DateTimeOffset.UnixEpoch)));

        Assert.Empty(snapshot.Findings);
        Assert.Equal(MachineOverallState.Stable, snapshot.OverallState);
    }

    private static void AssertFindingSeverity(
        MachineFindingsSnapshot snapshot,
        string code,
        MachineFindingSeverity? expectedSeverity)
    {
        var finding = snapshot.Findings.SingleOrDefault(candidate =>
            candidate.Code == code);

        if (expectedSeverity is null)
        {
            Assert.Null(finding);
        }
        else
        {
            Assert.NotNull(finding);
            Assert.Equal(expectedSeverity.Value, finding.Severity);
        }
    }

    private static MachineResourceSnapshot CreateResources(
        double cpuUsagePercent,
        ulong usedMemoryBytes,
        ulong totalMemoryBytes) =>
        new(
            CpuUsagePercent: cpuUsagePercent,
            TotalMemoryBytes: totalMemoryBytes,
            UsedMemoryBytes: usedMemoryBytes,
            CapturedAt: DateTimeOffset.UnixEpoch);

    private static MachineStorageSnapshot CreateStorage(
        long totalSizeBytes,
        long availableSizeBytes) =>
        new(
            Volumes:
            [
                new MachineStorageVolumeSnapshot(
                    RootPath: "C:\\",
                    VolumeLabel: "System",
                    FileSystem: "NTFS",
                    TotalSizeBytes: totalSizeBytes,
                    AvailableFreeSpaceBytes: availableSizeBytes,
                    IsSystemVolume: true)
            ],
            CapturedAt: DateTimeOffset.UnixEpoch);
}
