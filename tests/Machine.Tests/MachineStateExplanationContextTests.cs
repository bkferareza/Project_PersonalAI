using Machine.Core;

namespace Machine.Tests;

public sealed class MachineStateExplanationContextTests
{
    [Fact]
    public void StorageContextPreservesMeasuredValues()
    {
        MachineFolderMeasurementExplanationContext[] folders =
        [
            new(
                Name: "Users",
                MeasuredSizeBytes: 123_456_789,
                IsComplete: false)
        ];
        var folderScan = new MachineFolderScanExplanationContext(
            Folders: folders,
            IsComplete: false);
        var storage = new MachineStorageExplanationContext(
            SystemVolumeRoot: "C:\\",
            TotalSizeBytes: 1_000_000_000_000,
            AvailableSizeBytes: 250_000_000_000,
            LargeFolderScan: folderScan);

        Assert.Equal("C:\\", storage.SystemVolumeRoot);
        Assert.Equal(1_000_000_000_000, storage.TotalSizeBytes);
        Assert.Equal(
            250_000_000_000,
            storage.AvailableSizeBytes);
        Assert.Same(folderScan, storage.LargeFolderScan);
        Assert.Same(folders, folderScan.Folders);
        Assert.False(folderScan.IsComplete);
        Assert.Equal("Users", folders[0].Name);
        Assert.Equal(123_456_789, folders[0].MeasuredSizeBytes);
        Assert.False(folders[0].IsComplete);
    }

    [Fact]
    public void SoftwareContextPreservesIndependentSummaries()
    {
        var classic =
            new MachineSoftwareInventoryExplanationSummary(
                RegistrationCount: 120,
                IsComplete: true,
                SkippedEntryCount: 0);
        var packaged =
            new MachineSoftwareInventoryExplanationSummary(
                RegistrationCount: 80,
                IsComplete: false,
                SkippedEntryCount: 2);
        var software = new MachineSoftwareExplanationContext(
            ClassicDesktop: classic,
            PackagedApplications: packaged);

        Assert.Same(classic, software.ClassicDesktop);
        Assert.Same(packaged, software.PackagedApplications);
        Assert.Equal(120, classic.RegistrationCount);
        Assert.True(classic.IsComplete);
        Assert.Equal(0, classic.SkippedEntryCount);
        Assert.Equal(80, packaged.RegistrationCount);
        Assert.False(packaged.IsComplete);
        Assert.Equal(2, packaged.SkippedEntryCount);
    }

    [Fact]
    public void StartupContextPreservesCountsStateAndNames()
    {
        string[] names = ["Machine Agent", "Sync Client"];
        var startup = new MachineStartupExplanationContext(
            RegistrationCount: 8,
            RegistryRunCount: 5,
            StartupFolderCount: 3,
            MachineCount: 2,
            CurrentUserCount: 6,
            IsComplete: false,
            Names: names);

        Assert.Equal(8, startup.RegistrationCount);
        Assert.Equal(5, startup.RegistryRunCount);
        Assert.Equal(3, startup.StartupFolderCount);
        Assert.Equal(2, startup.MachineCount);
        Assert.Equal(6, startup.CurrentUserCount);
        Assert.False(startup.IsComplete);
        Assert.Same(names, startup.Names);
    }

    [Fact]
    public void RequestPreservesOptionalContext()
    {
        var storage = new MachineStorageExplanationContext(
            SystemVolumeRoot: "C:\\",
            TotalSizeBytes: 100,
            AvailableSizeBytes: 25,
            LargeFolderScan: null);
        var software = new MachineSoftwareExplanationContext(
            ClassicDesktop: null,
            PackagedApplications: null);
        var startup = new MachineStartupExplanationContext(
            RegistrationCount: 0,
            RegistryRunCount: 0,
            StartupFolderCount: 0,
            MachineCount: 0,
            CurrentUserCount: 0,
            IsComplete: true,
            Names: Array.Empty<string>());
        var findings = new MachineFindingsSnapshot(
            OverallState: MachineOverallState.Attention,
            Findings:
            [
                new MachineFinding(
                    Code: "cpu.usage.high",
                    Severity: MachineFindingSeverity.Attention,
                    Title: "CPU usage is high",
                    Detail: "Current CPU usage is 70.0%.")
            ]);
        var request = new MachineStateExplanationRequest(
            Identity: new MachineIdentity(
                "MACHINE",
                "Windows",
                "X64"),
            Resources: new MachineResourceSnapshot(
                CpuUsagePercent: 10,
                TotalMemoryBytes: 100,
                UsedMemoryBytes: 25,
                CapturedAt: DateTimeOffset.UnixEpoch),
            TopProcesses: Array.Empty<MachineProcessSnapshot>(),
            Storage: storage,
            Software: software,
            Startup: startup,
            Findings: findings);

        Assert.Same(storage, request.Storage);
        Assert.Same(software, request.Software);
        Assert.Same(startup, request.Startup);
        Assert.Same(findings, request.Findings);
    }
}
