using Machine.Core;

namespace Machine.Tests;

public sealed class MachineExplanationOpeningComposerTests
{
    [Fact]
    public void ComposeReturnsStableOpening()
    {
        var opening = MachineExplanationOpeningComposer.Compose(
            CreateFindings(MachineOverallState.Stable),
            CreateResources(cpuUsagePercent: 25d),
            storage: null);

        Assert.Equal("Stable ako ngayon.", opening);
    }

    [Fact]
    public void ComposeReturnsAttentionOpeningWithVerifiedCpuValue()
    {
        var opening = MachineExplanationOpeningComposer.Compose(
            CreateFindings(
                MachineOverallState.Attention,
                CreateFinding(
                    "cpu.usage.high",
                    MachineFindingSeverity.Attention)),
            CreateResources(cpuUsagePercent: 72.4d),
            storage: null);

        Assert.Equal(
            "Medyo busy ako ngayon—72.4% ang CPU usage.",
            opening);
    }

    [Fact]
    public void ComposeReturnsWarningOpeningWithVerifiedCpuValue()
    {
        var opening = MachineExplanationOpeningComposer.Compose(
            CreateFindings(
                MachineOverallState.Warning,
                CreateFinding(
                    "cpu.usage.high",
                    MachineFindingSeverity.Warning)),
            CreateResources(cpuUsagePercent: 100d),
            storage: null);

        Assert.Equal(
            "Under pressure ako ngayon—100% ang CPU usage.",
            opening);
    }

    [Theory]
    [InlineData(
        MachineOverallState.Attention,
        MachineFindingSeverity.Attention,
        825UL,
        "Medyo busy ako ngayon—82.5% ang memory usage.")]
    [InlineData(
        MachineOverallState.Warning,
        MachineFindingSeverity.Warning,
        950UL,
        "Under pressure ako ngayon—95% ang memory usage.")]
    public void ComposeUsesVerifiedMemoryValue(
        MachineOverallState state,
        MachineFindingSeverity severity,
        ulong usedMemoryBytes,
        string expected)
    {
        var opening = MachineExplanationOpeningComposer.Compose(
            CreateFindings(
                state,
                CreateFinding("memory.usage.high", severity)),
            CreateResources(
                cpuUsagePercent: 10d,
                usedMemoryBytes: usedMemoryBytes,
                totalMemoryBytes: 1_000UL),
            storage: null);

        Assert.Equal(expected, opening);
    }

    [Fact]
    public void ComposeReturnsCriticalStorageOpening()
    {
        var opening = MachineExplanationOpeningComposer.Compose(
            CreateFindings(
                MachineOverallState.Critical,
                CreateFinding(
                    "storage.system-volume.low-free-space",
                    MachineFindingSeverity.Critical)),
            CreateResources(cpuUsagePercent: 10d),
            new MachineStorageExplanationContext(
                SystemVolumeRoot: "C:\\",
                TotalSizeBytes: 100,
                AvailableSizeBytes: 1,
                LargeFolderScan: null));

        Assert.Equal(
            "May critical storage condition akong nakikita ngayon.",
            opening);
    }

    [Fact]
    public void ComposeReturnsUnknownOpening()
    {
        var opening = MachineExplanationOpeningComposer.Compose(
            CreateFindings(MachineOverallState.Unknown),
            resources: null,
            storage: null);

        Assert.Equal(
            "Hindi sapat ang current data para matukoy ang overall state.",
            opening);
    }

    private static MachineFindingsSnapshot CreateFindings(
        MachineOverallState state,
        params MachineFinding[] findings) =>
        new(state, findings);

    private static MachineFinding CreateFinding(
        string code,
        MachineFindingSeverity severity) =>
        new(
            Code: code,
            Severity: severity,
            Title: "Verified finding",
            Detail: "Verified detail.");

    private static MachineResourceSnapshot CreateResources(
        double cpuUsagePercent,
        ulong usedMemoryBytes = 500UL,
        ulong totalMemoryBytes = 1_000UL) =>
        new(
            CpuUsagePercent: cpuUsagePercent,
            TotalMemoryBytes: totalMemoryBytes,
            UsedMemoryBytes: usedMemoryBytes,
            CapturedAt: DateTimeOffset.UnixEpoch);
}
