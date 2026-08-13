using System.Text.Json;
using Machine.Core;

namespace Machine.Tests;

public sealed class MachineHistoryTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-08-14T00:00:00Z");

    [Fact]
    public void FiveMinuteRollupStoresSufficientStatisticsOnly()
    {
        var service = CreateService(
            maximumGap: TimeSpan.FromMinutes(5));

        service.Observe(Observation(Now, cpu: 10, memory: 40));
        service.Observe(Observation(
            Now.AddSeconds(30),
            cpu: 30,
            memory: 60));
        service.Observe(Observation(
            Now.AddMinutes(5),
            cpu: 20,
            memory: 50));

        var snapshot = service.GetSnapshot(
            MachineHistoryRange.Last24Hours,
            Now.AddMinutes(5));
        var completed = snapshot.Rollups.Single(rollup =>
            rollup.BucketStart == Now);

        Assert.Equal(2, completed.CpuUtilizationPercent?.SampleCount);
        Assert.Equal(10, completed.CpuUtilizationPercent?.Minimum);
        Assert.Equal(30, completed.CpuUtilizationPercent?.Maximum);
        Assert.Equal(20, completed.CpuUtilizationPercent?.Mean);
        Assert.Equal(40, completed.MemoryUtilizationPercent?.Minimum);
        Assert.Equal(60, completed.MemoryUtilizationPercent?.Maximum);
        Assert.Equal(TimeSpan.FromMinutes(5), completed.ObservedDuration);
        Assert.Equal(TimeSpan.FromMinutes(5),
            completed.ActivityDurations.Active);
        Assert.Equal(TimeSpan.FromMinutes(5),
            completed.StateDurations.Stable);
    }

    [Fact]
    public void PromotionPreservesWeightedMeansAndDurations()
    {
        var service = CreateService();
        for (var minute = 0; minute <= 60; minute++)
        {
            service.Observe(Observation(
                Now.AddMinutes(minute),
                cpu: minute < 30 ? 10 : 30,
                memory: 50,
                activity: minute < 20
                    ? MachineUserActivityState.Active
                    : MachineUserActivityState.Idle,
                state: minute < 45
                    ? MachineOverallState.Stable
                    : MachineOverallState.Attention));
        }

        var hourly = service.GetSnapshot(
            MachineHistoryRange.Last7Days,
            Now.AddHours(1));
        var first = hourly.Rollups.Single(rollup =>
            rollup.BucketStart == Now);

        Assert.Equal(60, first.CpuUtilizationPercent?.SampleCount);
        Assert.Equal(20, first.CpuUtilizationPercent?.Mean);
        Assert.Equal(TimeSpan.FromHours(1), first.ObservedDuration);
        Assert.Equal(TimeSpan.FromMinutes(20),
            first.ActivityDurations.Active);
        Assert.Equal(TimeSpan.FromMinutes(40),
            first.ActivityDurations.Idle);
        Assert.Equal(TimeSpan.FromMinutes(45),
            first.StateDurations.Stable);
        Assert.Equal(TimeSpan.FromMinutes(15),
            first.StateDurations.Attention);
    }

    [Fact]
    public void DateAndMonthBoundariesPromoteWithoutFillingGaps()
    {
        var service = CreateService(
            maximumGap: TimeSpan.FromMinutes(5));
        var start = DateTimeOffset.Parse("2026-01-31T23:55:00Z");

        service.Observe(Observation(start));
        service.Observe(Observation(start.AddMinutes(5)));
        service.Observe(Observation(start.AddMinutes(10)));
        service.Observe(Observation(start.AddDays(1).AddMinutes(5)));

        var all = service.GetSnapshot(
            MachineHistoryRange.All,
            start.AddDays(1).AddMinutes(5));

        Assert.Contains(all.Rollups, rollup =>
            rollup.BucketStart ==
                DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        Assert.Contains(all.Rollups, rollup =>
            rollup.BucketStart ==
                DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        Assert.Equal(TimeSpan.FromMinutes(10),
            all.Rollups.Aggregate(
                TimeSpan.Zero,
                (total, rollup) => total + rollup.ObservedDuration));
    }

    [Fact]
    public void OfflineGapRemainsMissingAndIsNotIdle()
    {
        var service = CreateService();
        ObserveSpan(service, Now, TimeSpan.FromHours(4));
        var secondSession = Now.AddHours(14);
        ObserveSpan(service, secondSession, TimeSpan.FromHours(2));

        var history = service.GetSnapshot(
            MachineHistoryRange.Last24Hours,
            secondSession.AddHours(2));

        Assert.Equal(TimeSpan.FromHours(6), history.TotalObservedDuration);
        Assert.Equal(TimeSpan.FromHours(6), history.Rollups.Aggregate(
            TimeSpan.Zero,
            (total, rollup) =>
                total + rollup.ActivityDurations.Active));
        Assert.Equal(TimeSpan.Zero, history.Rollups.Aggregate(
            TimeSpan.Zero,
            (total, rollup) =>
                total + rollup.ActivityDurations.Idle));
    }

    [Fact]
    public void SuspendBreaksDurationContinuity()
    {
        var service = CreateService();
        service.Observe(Observation(Now));
        service.Observe(Observation(Now.AddSeconds(30)));
        service.RecordPowerTransition(
            MachineHistoryEventKind.SystemSuspend,
            Now.AddMinutes(1));
        service.RecordPowerTransition(
            MachineHistoryEventKind.SystemResumeSuspend,
            Now.AddHours(3));
        service.Observe(Observation(Now.AddHours(3).AddSeconds(30)));

        var history = service.GetSnapshot(
            MachineHistoryRange.Last24Hours,
            Now.AddHours(4));

        Assert.Equal(TimeSpan.FromSeconds(30),
            history.TotalObservedDuration);
        Assert.Contains(history.Events, item =>
            item.Kind == MachineHistoryEventKind.SystemSuspend);
        Assert.Contains(history.Events, item =>
            item.Kind == MachineHistoryEventKind.SystemResumeSuspend);
    }

    [Fact]
    public void MissingGpuMetricsRemainMissingInsteadOfZero()
    {
        var service = CreateService();
        service.Observe(Observation(Now));
        service.Observe(Observation(
            Now.AddSeconds(30),
            gpu: 42,
            gpuMemory: 50,
            gpuTemperature: 54,
            gpuPower: 108));

        var rollup = Assert.Single(service.GetSnapshot(
            MachineHistoryRange.Last24Hours,
            Now.AddSeconds(30)).Rollups);

        Assert.Equal(1, rollup.GpuUtilizationPercent?.SampleCount);
        Assert.Equal(42, rollup.GpuUtilizationPercent?.Mean);
        Assert.Equal(54, rollup.GpuTemperatureCelsius?.Mean);
        Assert.Equal(108, rollup.GpuBoardPowerWatts?.Mean);
    }

    [Fact]
    public async Task ActiveBucketsPersistAcrossRestartWithoutBridgingGap()
    {
        var store = new RecordingHistoryStore();
        var first = CreateService();
        first.BeginSession(Now);
        first.Observe(Observation(Now));
        first.Observe(Observation(Now.AddSeconds(30)));
        Assert.True(await first.SaveIfDueAsync(
            store,
            Now.AddMinutes(1),
            force: true));

        var restored = CreateService();
        await restored.LoadAsync(store);
        restored.BeginSession(Now.AddHours(10));
        restored.Observe(Observation(Now.AddHours(10)));
        restored.Observe(Observation(Now.AddHours(10).AddSeconds(30)));
        var history = restored.GetSnapshot(
            MachineHistoryRange.Last24Hours,
            Now.AddHours(11));

        Assert.Equal(TimeSpan.FromMinutes(1),
            history.TotalObservedDuration);
        Assert.Contains(history.Events, item =>
            item.Kind ==
                MachineHistoryEventKind.PreviousSessionInterrupted);
        Assert.Equal(2, history.Events.Count(item =>
            item.Kind == MachineHistoryEventKind.MatasuriSessionStarted));
    }

    [Fact]
    public void ReliabilityEventsAreDeduplicatedAndFailuresCanBeGrouped()
    {
        var service = CreateService();
        var incidents = new[]
        {
            Incident(Now.AddMinutes(10), "sample.exe"),
            Incident(Now.AddMinutes(40), "sample.exe")
        };
        var reliability = MachineReliabilityAggregator.Aggregate(
            incidents,
            Now.AddHours(1));

        service.ObserveHealth(null, null, reliability, Now.AddHours(1));
        service.ObserveHealth(null, null, reliability, Now.AddHours(2));
        var events = service.GetSnapshot(
            MachineHistoryRange.Last24Hours,
            Now.AddHours(2)).Events;
        var grouped = MachineHistoryEventGrouper.GroupForDisplay(events);

        Assert.Equal(2, events.Count(item => item.Kind ==
            MachineHistoryEventKind.ApplicationFailureRecorded));
        var item = Assert.Single(grouped, item => item.Kind ==
            MachineHistoryEventKind.ApplicationFailureRecorded);
        Assert.Equal(2, item.Count);
        Assert.Equal("sample.exe", item.Detail);
        Assert.Equal(Now.AddMinutes(10), item.PeriodStart);
        Assert.Equal(Now.AddMinutes(40), item.PeriodEnd);
    }

    [Fact]
    public void UpdateAndRestartEventsAreEmittedOnlyForTransitions()
    {
        var service = CreateService();
        service.ObserveHealth(
            Update(MachineWindowsUpdateState.UpToDate, Now),
            Restart(false, Now),
            null,
            Now);
        service.ObserveHealth(
            Update(MachineWindowsUpdateState.UpToDate, Now.AddMinutes(1)),
            Restart(false, Now.AddMinutes(1)),
            null,
            Now.AddMinutes(1));
        service.ObserveHealth(
            Update(
                MachineWindowsUpdateState.InstallPending,
                Now.AddMinutes(2)),
            Restart(true, Now.AddMinutes(2)),
            null,
            Now.AddMinutes(2));

        var events = service.GetSnapshot(
            MachineHistoryRange.Last24Hours,
            Now.AddMinutes(3)).Events;

        Assert.Single(events, item => item.Kind ==
            MachineHistoryEventKind.WindowsUpdateStateChanged);
        Assert.Single(events, item => item.Kind ==
            MachineHistoryEventKind.RestartPendingChanged);
    }

    [Theory]
    [InlineData(MachineHistoryRange.Last24Hours,
        MachineHistoryResolution.FiveMinutes)]
    [InlineData(MachineHistoryRange.Last7Days,
        MachineHistoryResolution.Hour)]
    [InlineData(MachineHistoryRange.Last30Days,
        MachineHistoryResolution.Hour)]
    [InlineData(MachineHistoryRange.All,
        MachineHistoryResolution.Month)]
    public void RangeSelectsBoundedResolution(
        MachineHistoryRange range,
        MachineHistoryResolution expected)
    {
        Assert.Equal(
            expected,
            MachineHistoryRangePolicy.SelectResolution(range));
    }

    [Fact]
    public void InsightProjectionContainsOnlyTwoAggregatesAndOneEvent()
    {
        var service = CreateService(
            maximumGap: TimeSpan.FromMinutes(5));
        for (var minute = 0; minute <= 65; minute += 5)
        {
            service.Observe(Observation(
                Now.AddMinutes(minute),
                cpu: 20 + minute / 5));
        }
        service.ObserveHealth(
            Update(MachineWindowsUpdateState.UpToDate,
                Now.AddMinutes(68)),
            Restart(false, Now.AddMinutes(68)),
            MachineReliabilityAggregator.Aggregate(
                [Incident(Now.AddMinutes(64), "sample.exe")],
                Now.AddMinutes(68)),
            Now.AddMinutes(68));

        var context = MachineHistoryInsightProjector.Project(
            service.GetSnapshot(
                MachineHistoryRange.Last7Days,
                Now.AddMinutes(70)));

        Assert.NotNull(context);
        Assert.NotNull(context.RecentComparable);
        Assert.Equal(
            MachineHistoryEventKind.ApplicationFailureRecorded,
            context.SignificantEvent?.Kind);
        Assert.Equal("sample.exe", context.SignificantEvent?.Detail);
    }

    [Fact]
    public void RecentComparisonLanguageRequiresHistoricalAggregate()
    {
        const string text =
            "May verified recent CPU average sa bounded history.";
        var period = new MachineHistoryInsightPeriod(
            Now,
            Now.AddHours(1),
            3600,
            30,
            50,
            null,
            null,
            null,
            null,
            null,
            null);

        Assert.False(MachineExplanationValidator.IsValid(
            text,
            [],
            null));
        Assert.True(MachineExplanationValidator.IsValid(
            text,
            [],
            null,
            history: new(period, period with
            {
                StartedAt = Now.AddHours(-1),
                EndedAt = Now
            }, null)));
    }

    [Fact]
    public async Task CorruptStoreRecoversWithoutStoppingCollection()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "MatasuriHistoryTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(directory, FileMachineHistoryStore.FileName),
                "{ not-json }");
            var service = CreateService();
            await service.LoadAsync(new FileMachineHistoryStore(directory));

            Assert.Equal(
                MachineHistoryDataStatus.RecoveredFromInvalidState,
                service.DataStatus);
            Assert.True(service.Observe(Observation(Now)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AtomicPersistenceContainsNoPrivateTelemetryFields()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "MatasuriHistoryTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var service = CreateService();
            service.Observe(Observation(Now));
            await service.SaveIfDueAsync(
                new FileMachineHistoryStore(directory),
                Now,
                force: true);
            var json = await File.ReadAllTextAsync(Path.Combine(
                directory,
                FileMachineHistoryStore.FileName));

            Assert.DoesNotContain("Process", json,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Address", json,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Endpoint", json,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CommandLine", json,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(
                directory,
                FileMachineHistoryStore.FileName + ".tmp")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void PartialObservationKeepsMissingMetricsNull()
    {
        var service = CreateService();
        service.Observe(new MachineHistoryObservation(
            Now,
            null,
            null,
            null,
            null,
            null,
            null));
        service.Observe(new MachineHistoryObservation(
            Now.AddSeconds(30),
            null,
            null,
            null,
            null,
            null,
            null));

        var rollup = Assert.Single(service.GetSnapshot(
            MachineHistoryRange.Last24Hours,
            Now.AddMinutes(1)).Rollups);

        Assert.Null(rollup.CpuUtilizationPercent);
        Assert.Null(rollup.MemoryUtilizationPercent);
        Assert.Null(rollup.NetworkReceiveBytesPerSecond);
        Assert.Null(rollup.GpuUtilizationPercent);
        Assert.Equal(TimeSpan.FromSeconds(30), rollup.ObservedDuration);
        Assert.Equal(TimeSpan.FromSeconds(30),
            rollup.StateDurations.Unknown);
        Assert.Equal(TimeSpan.Zero, rollup.ActivityDurations.Active);
        Assert.Equal(TimeSpan.Zero, rollup.ActivityDurations.Idle);
    }

    [Fact]
    public void SparseEventTimelineIsHardBounded()
    {
        var service = CreateService();
        for (var index = 0; index <=
             MachineHistoryService.MaximumEventCount / 2; index++)
        {
            var start = Now.AddSeconds(index * 2);
            service.BeginSession(start);
            service.EndSession(start.AddSeconds(1));
        }

        var events = service.GetSnapshot(
            MachineHistoryRange.All,
            Now.AddHours(1)).Events;

        Assert.Equal(MachineHistoryService.MaximumEventCount, events.Count);
        Assert.Equal(events.Count,
            events.Select(item => item.Fingerprint)
                .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task DirtyPersistenceUsesTenMinuteCadenceAndFailureBackoff()
    {
        var service = CreateService();
        var store = new RecordingHistoryStore();
        service.Observe(Observation(Now));

        Assert.True(await service.SaveIfDueAsync(store, Now));
        service.Observe(Observation(Now.AddSeconds(30)));
        Assert.False(await service.SaveIfDueAsync(
            store,
            Now.AddMinutes(9)));
        Assert.True(await service.SaveIfDueAsync(
            store,
            Now.AddMinutes(10)));

        var unavailable = new FailingHistoryStore();
        service.Observe(Observation(Now.AddMinutes(11)));
        Assert.False(await service.SaveIfDueAsync(
            unavailable,
            Now.AddMinutes(20)));
        Assert.Equal(
            MachineHistoryDataStatus.PersistenceTemporarilyUnavailable,
            service.DataStatus);
        unavailable.Fail = false;
        Assert.False(await service.SaveIfDueAsync(
            unavailable,
            Now.AddMinutes(24)));
        Assert.True(await service.SaveIfDueAsync(
            unavailable,
            Now.AddMinutes(25)));
    }

    [Fact]
    public async Task OneYearSimulationRemainsHierarchicalAndLowMegabytes()
    {
        var denseCadence = TimeSpan.FromMinutes(5);
        var longCadence = TimeSpan.FromHours(1);
        var service = CreateService(
            minimumInterval: denseCadence,
            maximumGap: longCadence);
        var end = Now.AddDays(365);
        var denseEnd = Now.AddDays(2);
        long step = 0;
        for (var timestamp = Now; timestamp <= denseEnd;
             timestamp = timestamp.Add(denseCadence), step++)
        {
            service.Observe(Observation(
                timestamp,
                cpu: 10 + step % 60,
                memory: 40 + step % 20,
                activity: step % 3 == 0
                    ? MachineUserActivityState.Idle
                    : MachineUserActivityState.Active,
                state: MachineOverallState.Stable,
                gpu: step % 4 == 0 ? null : 20 + step % 50));
        }
        for (var timestamp = denseEnd.Add(longCadence);
             timestamp <= end;
             timestamp = timestamp.Add(longCadence), step++)
        {
            service.Observe(Observation(
                timestamp,
                cpu: 10 + step % 60,
                memory: 40 + step % 20,
                activity: step % 3 == 0
                    ? MachineUserActivityState.Idle
                    : MachineUserActivityState.Active,
                state: MachineOverallState.Stable,
                gpu: step % 4 == 0 ? null : 20 + step % 50));
        }

        var store = new RecordingHistoryStore();
        await service.SaveIfDueAsync(store, end, force: true);
        var state = Assert.IsType<MachineHistoryPersistedState>(store.State);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state).Length;
        Console.WriteLine(
            $"Synthetic 365-day history: {bytes:N0} bytes " +
            $"({state.FiveMinuteRollups.Count} five-minute, " +
            $"{state.HourlyRollups.Count} hourly, " +
            $"{state.DailyRollups.Count} daily, " +
            $"{state.MonthlyRollups.Count} monthly rollups).");

        Assert.True(state.FiveMinuteRollups.Count <=
            MachineHistoryService.MaximumFiveMinuteRollupCount);
        Assert.True(state.HourlyRollups.Count <=
            MachineHistoryService.MaximumHourlyRollupCount);
        Assert.True(state.DailyRollups.Count <=
            MachineHistoryService.MaximumDailyRollupCount);
        Assert.True(state.MonthlyRollups.Count <=
            MachineHistoryService.MaximumMonthlyRollupCount);
        Assert.True(state.Events.Count <=
            MachineHistoryService.MaximumEventCount);
        Assert.True(bytes < 5 * 1024 * 1024,
            $"Serialized history was {bytes:N0} bytes.");
    }

    private static MachineHistoryService CreateService(
        TimeSpan? minimumInterval = null,
        TimeSpan? maximumGap = null) => new(
        minimumInterval ?? TimeSpan.FromSeconds(30),
        maximumGap ?? TimeSpan.FromSeconds(90));

    private static void ObserveSpan(
        MachineHistoryService service,
        DateTimeOffset start,
        TimeSpan duration)
    {
        for (var elapsed = TimeSpan.Zero; elapsed <= duration;
             elapsed += TimeSpan.FromSeconds(30))
        {
            service.Observe(Observation(start + elapsed));
        }
    }

    private static MachineHistoryObservation Observation(
        DateTimeOffset timestamp,
        double cpu = 20,
        double memory = 50,
        MachineUserActivityState activity =
            MachineUserActivityState.Active,
        MachineOverallState state = MachineOverallState.Stable,
        double? gpu = null,
        double? gpuMemory = null,
        double? gpuTemperature = null,
        double? gpuPower = null) => new(
        timestamp,
        cpu,
        memory,
        1_000,
        500,
        activity,
        state,
        60,
        gpu,
        gpuMemory,
        gpuTemperature,
        gpuPower);

    private static MachineReliabilityIncident Incident(
        DateTimeOffset timestamp,
        string applicationName) => new(
        timestamp,
        MachineReliabilityIncidentCategory.ApplicationCrash,
        MachineReliabilityIncidentSeverity.Notice,
        "Application Error",
        applicationName,
        null,
        null,
        1000,
        "application.crash");

    private static MachineWindowsUpdateSnapshot Update(
        MachineWindowsUpdateState state,
        DateTimeOffset timestamp) => new(
        timestamp,
        timestamp,
        true,
        timestamp,
        timestamp,
        0,
        0,
        state,
        [],
        MachineHealthDataStatus.Complete,
        MachineWindowsUpdateRefreshStatus.Verified);

    private static MachineRebootPendingSnapshot Restart(
        bool pending,
        DateTimeOffset timestamp) => new(
        timestamp,
        pending,
        MachineRebootPendingConfidence.Verified,
        pending ? [MachineRebootPendingReason.WindowsUpdate] : [],
        [],
        false);

    private sealed class RecordingHistoryStore : IMachineHistoryStore
    {
        public MachineHistoryPersistedState? State { get; private set; }

        public Task<MachineHistoryPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(State);

        public Task SaveAsync(
            MachineHistoryPersistedState state,
            CancellationToken cancellationToken = default)
        {
            State = state;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingHistoryStore : IMachineHistoryStore
    {
        public bool Fail { get; set; } = true;

        public Task<MachineHistoryPersistedState?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MachineHistoryPersistedState?>(null);

        public Task SaveAsync(
            MachineHistoryPersistedState state,
            CancellationToken cancellationToken = default) => Fail
                ? Task.FromException(new IOException("Synthetic failure."))
                : Task.CompletedTask;
    }
}
