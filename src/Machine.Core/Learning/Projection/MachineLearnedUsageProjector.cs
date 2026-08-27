namespace Machine.Core;

public sealed record MachineLearnedHourlyUsageProfile(
    int LocalHour,
    double ActiveFraction,
    double IdleFraction,
    TimeSpan TypicalActiveDuration,
    TimeSpan TypicalIdleDuration,
    TimeSpan TypicalObservedDuration,
    int HistoricalDayCount,
    int ObservedDayCount,
    double ClassifiedActivityCoverage,
    MachineLearningEvidenceMaturity Maturity)
{
    public bool HasUsableEvidence =>
        Maturity != MachineLearningEvidenceMaturity.Insufficient &&
        TypicalObservedDuration > TimeSpan.Zero;
}

public sealed record MachineLearnedUsageSnapshot(
    DateTimeOffset CapturedAt,
    DateOnly? HistoricalStartDate,
    DateOnly? HistoricalEndDate,
    int HistoricalDayCount,
    IReadOnlyList<MachineLearnedHourlyUsageProfile> HourlyProfiles);

public static class MachineLearnedUsageProjector
{
    public const int MaximumHistoricalDayCount = 30;
    public const int ProvisionalObservedDayCount = 2;
    public const int EstablishedObservedDayCount = 7;

    public static MachineLearnedUsageSnapshot Project(
        IEnumerable<MachineHistoryRollup> hourlyRollups,
        DateTimeOffset capturedAt,
        TimeZoneInfo? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(hourlyRollups);

        var zone = timeZone ?? TimeZoneInfo.Local;
        var localNow = TimeZoneInfo.ConvertTime(capturedAt, zone);
        var currentDate = DateOnly.FromDateTime(localNow.Date);
        var earliestAllowedDate = currentDate.AddDays(
            -MaximumHistoricalDayCount);
        var byDayAndHour = new Dictionary<LocalDayHour,
            DailyHourEvidence>();

        foreach (var rollup in hourlyRollups)
        {
            var localStart = TimeZoneInfo.ConvertTime(
                rollup.BucketStart,
                zone);
            var localDate = DateOnly.FromDateTime(localStart.Date);
            if (localDate >= currentDate ||
                localDate < earliestAllowedDate ||
                rollup.BucketStart >= capturedAt.ToUniversalTime())
            {
                continue;
            }

            var maximumTicks = Math.Max(
                0,
                (rollup.BucketEnd - rollup.BucketStart).Ticks);
            var observedTicks = Math.Min(
                Math.Max(0, rollup.ObservedDurationTicks),
                maximumTicks);
            if (observedTicks <= 0)
            {
                continue;
            }

            var activeTicks = Math.Min(
                Math.Max(0, rollup.ActivityDurations.ActiveTicks),
                observedTicks);
            var idleTicks = Math.Min(
                Math.Max(0, rollup.ActivityDurations.IdleTicks),
                observedTicks - activeTicks);
            var key = new LocalDayHour(localDate, localStart.Hour);
            if (!byDayAndHour.TryGetValue(key, out var evidence))
            {
                evidence = new DailyHourEvidence();
                byDayAndHour.Add(key, evidence);
            }
            evidence.Add(observedTicks, activeTicks, idleTicks);
        }

        if (byDayAndHour.Count == 0)
        {
            return new(
                capturedAt,
                null,
                null,
                0,
                []);
        }

        var historicalStart = byDayAndHour.Keys.Min(key => key.LocalDate);
        var historicalEnd = currentDate.AddDays(-1);
        var historicalDayCount = Math.Clamp(
            currentDate.DayNumber - historicalStart.DayNumber,
            1,
            MaximumHistoricalDayCount);
        var profiles = byDayAndHour
            .GroupBy(item => item.Key.LocalHour)
            .OrderBy(group => group.Key)
            .Select(group => CreateProfile(
                group.Key,
                group.Select(item => item.Value).ToArray(),
                historicalDayCount))
            .Where(profile => profile.TypicalObservedDuration >
                TimeSpan.Zero)
            .ToArray();

        return new(
            capturedAt,
            historicalStart,
            historicalEnd,
            historicalDayCount,
            profiles);
    }

    private static MachineLearnedHourlyUsageProfile CreateProfile(
        int localHour,
        IReadOnlyList<DailyHourEvidence> dailyEvidence,
        int historicalDayCount)
    {
        var observedTicks = dailyEvidence.Aggregate(
            0L,
            (total, item) => SaturatingAdd(total, item.ObservedTicks));
        var activeTicks = dailyEvidence.Aggregate(
            0L,
            (total, item) => SaturatingAdd(total, item.ActiveTicks));
        var idleTicks = dailyEvidence.Aggregate(
            0L,
            (total, item) => SaturatingAdd(total, item.IdleTicks));
        var classifiedTicks = SaturatingAdd(activeTicks, idleTicks);
        var observedDayCount = dailyEvidence.Count(item =>
            item.ClassifiedTicks > 0);
        var activeFraction = classifiedTicks > 0
            ? activeTicks / (double)classifiedTicks
            : 0d;
        var idleFraction = classifiedTicks > 0
            ? idleTicks / (double)classifiedTicks
            : 0d;
        var divisor = Math.Max(1, historicalDayCount);

        return new(
            localHour,
            Math.Clamp(activeFraction, 0d, 1d),
            Math.Clamp(idleFraction, 0d, 1d),
            TimeSpan.FromTicks(activeTicks / divisor),
            TimeSpan.FromTicks(idleTicks / divisor),
            TimeSpan.FromTicks(classifiedTicks / divisor),
            historicalDayCount,
            observedDayCount,
            observedTicks > 0
                ? Math.Clamp(classifiedTicks / (double)observedTicks,
                    0d,
                    1d)
                : 0d,
            GetMaturity(observedDayCount));
    }

    private static MachineLearningEvidenceMaturity GetMaturity(
        int observedDayCount) =>
        observedDayCount >= EstablishedObservedDayCount
            ? MachineLearningEvidenceMaturity.Established
            : observedDayCount >= ProvisionalObservedDayCount
                ? MachineLearningEvidenceMaturity.Provisional
                : MachineLearningEvidenceMaturity.Insufficient;

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right
            ? long.MaxValue
            : left + right;

    private readonly record struct LocalDayHour(
        DateOnly LocalDate,
        int LocalHour);

    private sealed class DailyHourEvidence
    {
        public long ObservedTicks { get; private set; }
        public long ActiveTicks { get; private set; }
        public long IdleTicks { get; private set; }
        public long ClassifiedTicks => SaturatingAdd(
            ActiveTicks,
            IdleTicks);

        public void Add(
            long observedTicks,
            long activeTicks,
            long idleTicks)
        {
            ObservedTicks = SaturatingAdd(ObservedTicks, observedTicks);
            ActiveTicks = SaturatingAdd(ActiveTicks, activeTicks);
            IdleTicks = SaturatingAdd(IdleTicks, idleTicks);
        }
    }
}
