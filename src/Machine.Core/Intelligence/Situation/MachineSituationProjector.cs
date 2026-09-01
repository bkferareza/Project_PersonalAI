using System.Globalization;
using System.Text.RegularExpressions;

namespace Machine.Core;

public static partial class MachineSituationProjector
{
    public static MachineSituationSnapshot Project(
        MachineSituationInput input,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        var awareness = CreateLearningAwareness(input);
        var candidates = new List<MachineSituationEvidenceItem>();

        AddPostureAndFindings(candidates, input, now);
        AddCurrentResources(candidates, input, now);
        AddCurrentHardware(candidates, input, now);
        AddCurrentNetworkAndSession(candidates, input, now);
        AddCurrentHealth(candidates, input, now);
        AddRecentEvidence(candidates, input, now);
        AddLearnedNormal(candidates, input, awareness);
        AddToday(candidates, input, now);
        AddForward(candidates, input);
        AddActionContext(candidates, input);
        AddLearningAwareness(candidates, awareness);
        AddSelfHealth(candidates, input, now);

        var selected = MachineSituationEvidenceSelector.Select(candidates);
        return new(
            MachineSituationSnapshot.CurrentSchemaVersion,
            now,
            input.Findings?.OverallState ?? MachineOverallState.Unknown,
            candidates.Count,
            selected,
            awareness);
    }

    private static MachineLearningAwareness CreateLearningAwareness(
        MachineSituationInput input)
    {
        var learning = input.Learning;
        var baseline = learning?.CurrentBaseline;
        MachineLearningContextKey? currentContext = baseline is null
            ? null
            : new(baseline.LocalHour, baseline.ActivityState);
        var applicablePattern = currentContext is null || learning is null
            ? null
            : learning.BroaderPatterns
                .Where(pattern => pattern.MemberContexts.Contains(
                    currentContext.Value))
                .OrderByDescending(pattern => pattern.Confidence)
                .ThenBy(pattern => pattern.Freshness)
                .ThenByDescending(pattern => pattern.CombinedSampleCount)
                .FirstOrDefault();
        return new(
            learning?.Readiness.MemoryState ??
                MachineLearningMemoryState.Calibrating,
            learning?.Metadata.LifetimeAcceptedObservationCount ?? 0,
            learning?.RawObservationCount ?? 0,
            learning?.Baselines.Count ?? 0,
            learning?.ContextProfiles.Count ?? 0,
            learning?.Baselines.Count(item => item.Confidence ==
                MachineLearningConfidence.Established) ?? 0,
            currentContext,
            baseline?.SampleCount ?? 0,
            baseline?.ObservedDayCount ?? 0,
            baseline?.Confidence ?? MachineLearningConfidence.Calibrating,
            baseline?.Freshness,
            baseline?.EstimatedWallPowerMaturity ??
                MachineLearningEvidenceMaturity.Insufficient,
            applicablePattern,
            learning?.Readiness.PatternReadiness.PrimaryBlocker ??
                MachineLearningPatternReadinessBlocker.
                    InsufficientProfiles,
            input.Forecast?.AvailabilityReason ??
                MachineUsageForecastAvailabilityReason.
                    NoHistoricalActivityEvidence,
            Math.Clamp(input.Forecast?.ForecastCoverage ?? 0d, 0d, 1d));
    }

    private static void AddPostureAndFindings(
        ICollection<MachineSituationEvidenceItem> items,
        MachineSituationInput input,
        DateTimeOffset now)
    {
        var posture = input.Findings?.OverallState ??
            MachineOverallState.Unknown;
        items.Add(Create(
            "now.posture",
            MachineSituationCategory.Now,
            MachineSituationTimeScope.Current,
            posture switch
            {
                MachineOverallState.Critical =>
                    MachineSituationImportance.Critical,
                MachineOverallState.Warning =>
                    MachineSituationImportance.Important,
                MachineOverallState.Attention =>
                    MachineSituationImportance.Notable,
                _ => MachineSituationImportance.Routine
            },
            MachineSituationFreshness.Current,
            MachineSituationEvidenceMaturity.Verified,
            $"Deterministic global posture: {FormatEnum(posture)}.",
            [FormatEnum(posture)]));

        foreach (var finding in input.Findings?.Findings ?? [])
        {
            items.Add(Create(
                $"finding.{finding.Code}",
                MachineSituationCategory.Now,
                MachineSituationTimeScope.Current,
                MapImportance(finding.Severity),
                MachineSituationFreshness.Current,
                MachineSituationEvidenceMaturity.Verified,
                $"{finding.Title}: {finding.Detail}",
                [FormatEnum(finding.Severity)],
                ExtractEntityNames(finding.Title, finding.Detail)));
        }
    }

    private static void AddCurrentResources(
        ICollection<MachineSituationEvidenceItem> items,
        MachineSituationInput input,
        DateTimeOffset now)
    {
        if (input.Resources is not { } resources ||
            resources.TotalMemoryBytes == 0)
        {
            return;
        }
        var memoryPercent = Math.Clamp(
            resources.UsedMemoryBytes /
                (double)resources.TotalMemoryBytes * 100d,
            0d,
            100d);
        var cpu = Percent(resources.CpuUsagePercent);
        var memory = Percent(memoryPercent);
        var usedMemory = Gibibytes(resources.UsedMemoryBytes);
        var totalMemory = Gibibytes(resources.TotalMemoryBytes);
        items.Add(Create(
            "now.resources",
            MachineSituationCategory.Now,
            MachineSituationTimeScope.Current,
            MachineSituationImportance.Context,
            GetFreshness(resources.CapturedAt, now),
            MachineSituationEvidenceMaturity.Verified,
            $"Current resource use: CPU {cpu}; memory {memory} " +
                $"({usedMemory} / {totalMemory}).",
            [cpu, memory, usedMemory, totalMemory]));
    }

    private static void AddCurrentHardware(
        ICollection<MachineSituationEvidenceItem> items,
        MachineSituationInput input,
        DateTimeOffset now)
    {
        var gpu = input.Gpu?.Adapters
            .OrderBy(adapter => adapter.AdapterIndex)
            .FirstOrDefault();
        if (gpu is not null &&
            (gpu.GpuUtilizationPercent is not null ||
                gpu.MemoryUtilizationPercent is not null ||
                gpu.TemperatureCelsius is not null ||
                gpu.BoardPowerWatts is not null))
        {
            var values = NonNull(
                Prefix("GPU utilization",
                    OptionalPercent(gpu.GpuUtilizationPercent)),
                Prefix("VRAM utilization",
                    OptionalPercent(gpu.MemoryUtilizationPercent)),
                Prefix("GPU temperature",
                    OptionalCelsius(gpu.TemperatureCelsius)),
                Prefix("GPU board power",
                    OptionalWatts(gpu.BoardPowerWatts)));
            items.Add(Create(
                "now.gpu",
                MachineSituationCategory.Now,
                MachineSituationTimeScope.Current,
                MachineSituationImportance.Context,
                GetFreshness(input.Gpu!.CapturedAt, now),
                MachineSituationEvidenceMaturity.Verified,
                $"Current GPU evidence for " +
                    $"{gpu.AdapterName ?? gpu.Vendor}: " +
                    string.Join("; ", values) + ".",
                values,
                [gpu.AdapterName ?? gpu.Vendor]));
        }

        var temperatureValues = NonNull(
            Prefix("CPU temperature",
                OptionalCelsius(input.CpuHardware?.TemperatureCelsius)),
            Prefix("GPU temperature",
                OptionalCelsius(gpu?.TemperatureCelsius)),
            Prefix("Highest storage temperature",
                OptionalCelsius(input.StorageHealth?.Devices
                    .Where(device => device.TemperatureCelsius is not null)
                    .Max(device => device.TemperatureCelsius))));
        if (temperatureValues.Length > 0)
        {
            items.Add(Create(
                "now.temperatures",
                MachineSituationCategory.Now,
                MachineSituationTimeScope.Current,
                MachineSituationImportance.Context,
                GetFreshness(input.CpuHardware?.CapturedAt ??
                    input.Gpu?.CapturedAt ??
                    input.StorageHealth?.CapturedAt ?? now, now),
                MachineSituationEvidenceMaturity.Verified,
                "Available current temperature evidence: " +
                    string.Join("; ", temperatureValues) + ".",
                temperatureValues));
        }

        if (input.Power?.EstimatedWallWatts is { } wallWatts)
        {
            var values = NonNull(
                OptionalWatts(wallWatts),
                input.Power.EstimatedWallLowerWatts is { } lower &&
                    input.Power.EstimatedWallUpperWatts is { } upper
                        ? $"{lower.ToString("F1", CultureInfo.InvariantCulture)}–" +
                            $"{upper.ToString("F1", CultureInfo.InvariantCulture)} W"
                        : null,
                FormatEnum(input.Power.Confidence));
            items.Add(Create(
                "now.power",
                MachineSituationCategory.Now,
                MachineSituationTimeScope.Current,
                MachineSituationImportance.Context,
                GetFreshness(input.Power.CapturedAt, now),
                MachineSituationEvidenceMaturity.Verified,
                "Software-estimated whole-PC wall power: " +
                    string.Join("; ", values) + ".",
                values));
        }

        var systemVolume = input.Storage?.Volumes.FirstOrDefault(volume =>
            volume.IsSystemVolume);
        if (systemVolume is not null && systemVolume.TotalSizeBytes > 0)
        {
            var freePercent = Math.Clamp(
                systemVolume.AvailableFreeSpaceBytes /
                    (double)systemVolume.TotalSizeBytes * 100d,
                0d,
                100d);
            var free = Gibibytes(systemVolume.AvailableFreeSpaceBytes);
            var total = Gibibytes(systemVolume.TotalSizeBytes);
            items.Add(Create(
                "now.storage.system",
                MachineSituationCategory.Now,
                MachineSituationTimeScope.Current,
                MachineSituationImportance.Context,
                GetFreshness(input.Storage!.CapturedAt, now),
                MachineSituationEvidenceMaturity.Verified,
                $"System volume {systemVolume.RootPath} has " +
                    $"{Percent(freePercent)} free ({free} / {total}).",
                [Percent(freePercent), free, total],
                [systemVolume.RootPath]));
        }

        if (input.StorageHealth is { } storageHealth &&
            storageHealth.Devices.Count > 0)
        {
            var unhealthy = storageHealth.Devices.Where(device =>
                    !string.IsNullOrWhiteSpace(device.WindowsHealthStatus) &&
                    !string.Equals(device.WindowsHealthStatus, "Healthy",
                        StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToArray();
            var values = new List<string>
            {
                $"{storageHealth.Devices.Count:N0} devices",
                $"{unhealthy.Length:N0} non-healthy"
            };
            values.AddRange(unhealthy.Select(device =>
                device.WindowsHealthStatus!));
            items.Add(Create(
                "now.storage.health",
                MachineSituationCategory.Now,
                MachineSituationTimeScope.Current,
                unhealthy.Length > 0
                    ? MachineSituationImportance.Important
                    : MachineSituationImportance.Routine,
                GetFreshness(storageHealth.CapturedAt, now),
                MachineSituationEvidenceMaturity.Verified,
                unhealthy.Length == 0
                    ? $"Windows reports no non-healthy state across " +
                        $"{storageHealth.Devices.Count:N0} observed storage devices."
                    : "Windows storage health reports: " +
                        string.Join("; ", unhealthy.Select(device =>
                            $"{device.DisplayName} " +
                            $"{device.WindowsHealthStatus}")) + ".",
                values,
                unhealthy.Select(device => device.DisplayName).ToArray()));
        }
    }

    private static void AddCurrentNetworkAndSession(
        ICollection<MachineSituationEvidenceItem> items,
        MachineSituationInput input,
        DateTimeOffset now)
    {
        if (input.Network is { } network)
        {
            var values = NonNull(
                FormatEnum(network.Aggregate.ActivityClass),
                Prefix("Receive", OptionalRate(
                    network.Aggregate.ReceiveBytesPerSecond)),
                Prefix("Send", OptionalRate(
                    network.Aggregate.SendBytesPerSecond)),
                $"{network.Aggregate.ActiveInterfaceCount:N0} active interfaces");
            items.Add(Create(
                "now.network",
                MachineSituationCategory.Now,
                MachineSituationTimeScope.Current,
                MachineSituationImportance.Routine,
                GetFreshness(network.CapturedAt, now),
                MachineSituationEvidenceMaturity.Verified,
                "Current aggregate network behavior: " +
                    string.Join("; ", values) + ".",
                values));
        }

        if (input.Session is { } session)
        {
            var activity = FormatEnum(session.CurrentUserInputState);
            var idle = FormatDuration(session.CurrentUserIdleDuration);
            var uptime = FormatDuration(session.MachineUptime);
            items.Add(Create(
                "now.session",
                MachineSituationCategory.Now,
                MachineSituationTimeScope.Current,
                MachineSituationImportance.Routine,
                GetFreshness(session.CapturedAt, now),
                MachineSituationEvidenceMaturity.Verified,
                $"Current user activity is {activity}; idle duration " +
                    $"{idle}; machine uptime {uptime}.",
                [activity, idle, uptime]));
        }
    }

    private static void AddCurrentHealth(
        ICollection<MachineSituationEvidenceItem> items,
        MachineSituationInput input,
        DateTimeOffset now)
    {
        if (input.WindowsUpdate is { } update)
        {
            var values = NonNull(
                FormatEnum(update.UpdateState),
                update.PendingUpdateCount is { } count
                    ? $"{count:N0} pending updates"
                    : null);
            items.Add(Create(
                "now.windows_update",
                MachineSituationCategory.Now,
                MachineSituationTimeScope.Current,
                update.UpdateState is MachineWindowsUpdateState.RestartRequired
                    or MachineWindowsUpdateState.InstallPending
                    ? MachineSituationImportance.Notable
                    : MachineSituationImportance.Routine,
                GetFreshness(update.VerifiedAt ?? update.CapturedAt, now),
                MachineSituationEvidenceMaturity.Verified,
                "Windows Update state: " + string.Join("; ", values) + ".",
                values));
        }

        if (input.RebootPending is { } reboot)
        {
            var pending = reboot.IsPending switch
            {
                true => "Restart pending",
                false => "No restart pending",
                _ => "Restart state unknown"
            };
            var values = new List<string> { pending };
            values.AddRange(reboot.Reasons.Select(FormatEnum));
            items.Add(Create(
                "now.reboot",
                MachineSituationCategory.Now,
                MachineSituationTimeScope.Current,
                reboot.IsPending == true
                    ? MachineSituationImportance.Notable
                    : MachineSituationImportance.Routine,
                GetFreshness(reboot.CapturedAt, now),
                reboot.Confidence == MachineRebootPendingConfidence.Verified
                    ? MachineSituationEvidenceMaturity.Verified
                    : MachineSituationEvidenceMaturity.Provisional,
                pending + (reboot.Reasons.Count == 0
                    ? "."
                    : ": " + string.Join(", ",
                        reboot.Reasons.Select(FormatEnum)) + "."),
                values));
        }
    }

    private static void AddRecentEvidence(
        ICollection<MachineSituationEvidenceItem> items,
        MachineSituationInput input,
        DateTimeOffset now)
    {
        if (input.Reliability is { } reliability)
        {
            var sevenDays = reliability.Summary.Last7Days;
            var values = ReliabilityValues(sevenDays);
            items.Add(Create(
                "recent.reliability.7d",
                MachineSituationCategory.Recently,
                MachineSituationTimeScope.Last7Days,
                sevenDays.TotalIncidentCount > 0
                    ? MachineSituationImportance.Notable
                    : MachineSituationImportance.Routine,
                GetFreshness(reliability.VerifiedAt ??
                    reliability.CapturedAt, now),
                MachineSituationEvidenceMaturity.Verified,
                $"Verified reliability history for 7 days: " +
                    $"{sevenDays.TotalIncidentCount:N0} incidents " +
                    $"({string.Join("; ", values)}).",
                [$"{sevenDays.TotalIncidentCount:N0} incidents", .. values]));

            var incident = reliability.Incidents
                .Where(candidate =>
                    !MatasuriRuntimeIdentityPolicy.IsOwnedRuntimeIncident(
                        candidate))
                .OrderByDescending(candidate => candidate.OccurredAt)
                .FirstOrDefault();
            if (incident is not null)
            {
                var entity = incident.ApplicationName ?? incident.Source;
                items.Add(Create(
                    "recent.incident.latest",
                    MachineSituationCategory.Recently,
                    MachineSituationTimeScope.Recent,
                    incident.Severity == MachineReliabilityIncidentSeverity.Severe
                        ? MachineSituationImportance.Important
                        : MachineSituationImportance.Notable,
                    GetFreshness(incident.OccurredAt, now),
                    MachineSituationEvidenceMaturity.Verified,
                    $"Most recent non-Matasuri reliability incident: " +
                        $"{FormatEnum(incident.Category)} for {entity} at " +
                        $"{incident.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}.",
                    [FormatEnum(incident.Category),
                        incident.OccurredAt.ToLocalTime().ToString(
                            "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)],
                    [entity]));
            }

            foreach (var recurring in reliability.Summary
                .RecurringApplications
                .Where(candidate =>
                    !MatasuriRuntimeIdentityPolicy.
                        IsOwnedApplicationIdentity(
                            candidate.ApplicationName))
                .OrderByDescending(candidate =>
                    candidate.IncidentCountLast7Days)
                .ThenBy(candidate => candidate.ApplicationName,
                    StringComparer.OrdinalIgnoreCase)
                .Take(2))
            {
                items.Add(Create(
                    $"recent.recurring.{StableId(recurring.ApplicationName)}",
                    MachineSituationCategory.Recently,
                    MachineSituationTimeScope.Last7Days,
                    recurring.IncidentCountLast7Days > 0
                        ? MachineSituationImportance.Notable
                        : MachineSituationImportance.Context,
                    GetFreshness(recurring.LastOccurredAt, now),
                    MachineSituationEvidenceMaturity.Verified,
                    $"{recurring.ApplicationName} has " +
                        $"{recurring.IncidentCountLast7Days:N0} verified " +
                        $"failures in 7 days and " +
                        $"{recurring.IncidentCountLast30Days:N0} in 30 days.",
                    [$"{recurring.IncidentCountLast7Days:N0} failures",
                        $"{recurring.IncidentCountLast30Days:N0} failures"],
                    [recurring.ApplicationName]));
            }
        }

        foreach (var episode in input.Learning?.RecentEpisodes
            .OrderByDescending(candidate => candidate.EndedAt)
            .Take(2) ?? [])
        {
            items.Add(Create(
                $"recent.learning_episode.{episode.EndedAt.UtcTicks}",
                MachineSituationCategory.Recently,
                MachineSituationTimeScope.Recent,
                episode.OverallState is MachineOverallState.Warning or
                    MachineOverallState.Critical
                    ? MachineSituationImportance.Notable
                    : MachineSituationImportance.Context,
                GetFreshness(episode.EndedAt, now),
                MapMaturity(input.Learning?.CurrentBaseline?.Confidence ??
                    MachineLearningConfidence.Calibrating),
                $"Completed learned episode: " +
                    $"{FormatEnum(episode.ActivityState)} and " +
                    $"{FormatEnum(episode.OverallState)} across " +
                    $"{episode.SampleCount:N0} samples; average CPU " +
                    $"{Percent(episode.AverageCpuUsagePercent)}; average " +
                    $"memory {Percent(episode.AverageMemoryUsagePercent)}.",
                [FormatEnum(episode.ActivityState),
                    FormatEnum(episode.OverallState),
                    $"{episode.SampleCount:N0} samples",
                    Percent(episode.AverageCpuUsagePercent),
                    Percent(episode.AverageMemoryUsagePercent)]));
        }
    }

    private static void AddLearnedNormal(
        ICollection<MachineSituationEvidenceItem> items,
        MachineSituationInput input,
        MachineLearningAwareness awareness)
    {
        var baseline = input.Learning?.CurrentBaseline;
        if (baseline is null)
        {
            return;
        }
        var values = new List<string>
        {
            $"{baseline.LocalHour:00}:00 local",
            FormatEnum(baseline.ActivityState),
            $"{baseline.SampleCount:N0} samples",
            $"{baseline.ObservedDayCount:N0} observed days",
            FormatMaturity(baseline.Confidence),
            FormatEnum(baseline.Freshness),
            Percent(baseline.AdaptiveCpuMean),
            Percent(baseline.AdaptiveMemoryMean)
        };
        if (baseline.CpuTypicalRange is { } cpuRange)
        {
            values.Add(PercentRange(cpuRange));
        }
        if (baseline.MemoryTypicalRange is { } memoryRange)
        {
            values.Add(PercentRange(memoryRange));
        }
        if (baseline.AdaptiveEstimatedWallPowerMeanWatts is { } power)
        {
            values.Add(OptionalWatts(power)!);
        }
        if (baseline.EstimatedWallPowerTypicalRange is { } powerRange)
        {
            values.Add(WattRange(powerRange));
        }
        items.Add(Create(
            "learned.current_context",
            MachineSituationCategory.LearnedNormal,
            MachineSituationTimeScope.CurrentContext,
            MachineSituationImportance.Context,
            MapFreshness(baseline.Freshness),
            MapMaturity(baseline.Confidence),
            $"Learned current context {baseline.LocalHour:00}:00 " +
                $"{FormatEnum(baseline.ActivityState)}: " +
                $"{baseline.SampleCount:N0} samples across " +
                $"{baseline.ObservedDayCount:N0} observed days; " +
                $"adaptive CPU mean {Percent(baseline.AdaptiveCpuMean)}; " +
                $"adaptive memory mean " +
                $"{Percent(baseline.AdaptiveMemoryMean)}.",
            values));

        if (awareness.ApplicableRecurringPattern is { } pattern)
        {
            items.Add(Create(
                "learned.recurring_pattern",
                MachineSituationCategory.LearnedNormal,
                MachineSituationTimeScope.CurrentContext,
                MachineSituationImportance.Context,
                MapFreshness(pattern.Freshness),
                MapMaturity(pattern.Confidence),
                $"Applicable recurring pattern: " +
                    $"{pattern.StartHour:00}:00–" +
                    $"{pattern.EndHourExclusive:00}:00 " +
                    $"{FormatEnum(pattern.ActivityState)} across " +
                    $"{pattern.MemberContexts.Count:N0} contexts and " +
                    $"{pattern.CombinedSampleCount:N0} samples.",
                [$"{pattern.StartHour:00}:00–" +
                    $"{pattern.EndHourExclusive:00}:00",
                    FormatEnum(pattern.ActivityState),
                    $"{pattern.MemberContexts.Count:N0} contexts",
                    $"{pattern.CombinedSampleCount:N0} samples",
                    $"{pattern.MinimumDistinctObservedDayCount:N0} observed days",
                    FormatMaturity(pattern.Confidence)]));
        }
    }

    private static void AddToday(
        ICollection<MachineSituationEvidenceItem> items,
        MachineSituationInput input,
        DateTimeOffset now)
    {
        if (input.Today is { } today)
        {
            var energy = KilowattHours(today.ObservedEnergyWattHours / 1000d);
            var cost = FormatCost(today.EstimatedCost, today.Rate);
            var duration = FormatDuration(today.ObservedDuration);
            var (active, idle) = GetTodayActivity(input.History, now);
            var values = NonNull(
                energy,
                cost,
                duration,
                active > TimeSpan.Zero
                    ? $"{FormatDuration(active)} active"
                    : null,
                idle > TimeSpan.Zero
                    ? $"{FormatDuration(idle)} idle"
                    : null);
            items.Add(Create(
                "today.observed",
                MachineSituationCategory.Today,
                MachineSituationTimeScope.Today,
                MachineSituationImportance.Context,
                MachineSituationFreshness.Current,
                MachineSituationEvidenceMaturity.Verified,
                "Today observed PC evidence: " +
                    string.Join("; ", values) + ".",
                values,
                today.Rate is null ? [] : [today.Rate.ProviderName]));
        }

        if (input.TodayComparison is { } comparison &&
            comparison.ObservedDuration > TimeSpan.Zero)
        {
            var values = new List<string>
            {
                FormatEnum(comparison.ComparisonState),
                Percent(comparison.LearnedCoverage * 100d),
                FormatMaturity(comparison.ComparisonMaturity)
            };
            AddIfNotNull(values, comparison.ExpectedObservedEnergyKilowattHours
                is { } expected ? KilowattHours(expected) : null);
            AddIfNotNull(values, comparison.ExpectedLowerEnergyKilowattHours
                is { } lower &&
                comparison.ExpectedUpperEnergyKilowattHours is { } upper
                    ? KilowattHourRange(lower, upper)
                    : null);
            AddIfNotNull(values, FormatCost(
                comparison.ExpectedEstimatedCost,
                comparison.Rate));
            items.Add(Create(
                "today.learned_comparison",
                MachineSituationCategory.Today,
                MachineSituationTimeScope.Today,
                comparison.ComparisonState is
                    MachineTodayLearnedEnergyComparisonState.AboveLearnedRange
                    or MachineTodayLearnedEnergyComparisonState.BelowLearnedRange
                    ? MachineSituationImportance.Notable
                    : MachineSituationImportance.Context,
                MachineSituationFreshness.Current,
                MapMaturity(comparison.ComparisonMaturity),
                $"Today versus learned same-duration behavior: " +
                    $"{FormatEnum(comparison.ComparisonState)} with " +
                    $"{Percent(comparison.LearnedCoverage * 100d)} learned " +
                    "coverage.",
                values));
        }
    }

    private static void AddForward(
        ICollection<MachineSituationEvidenceItem> items,
        MachineSituationInput input)
    {
        var forecast = input.Forecast;
        if (forecast is null)
        {
            return;
        }
        if (!forecast.HasNextObservedHourForecast)
        {
            var reason = FormatEnum(forecast.AvailabilityReason);
            items.Add(Create(
                "forward.unavailable",
                MachineSituationCategory.Forward,
                MachineSituationTimeScope.NextObservedHour,
                MachineSituationImportance.Context,
                MachineSituationFreshness.Current,
                MachineSituationEvidenceMaturity.Unavailable,
                $"Deterministic forecast is unavailable because {reason}.",
                ["Unavailable", reason],
                allowsCausalLanguage: true));
            return;
        }
        var nextValues = NonNull(
            KilowattHours(forecast.NextObservedHourEnergyKilowattHours!.Value),
            forecast.NextObservedHourEnergyLowerKilowattHours is { } lower &&
                forecast.NextObservedHourEnergyUpperKilowattHours is { } upper
                ? KilowattHourRange(lower, upper)
                : null,
            FormatCost(forecast.NextObservedHourEstimatedCost,
                forecast.RateReference),
            FormatMaturity(forecast.CurrentPowerMaturity));
        items.Add(Create(
            "forward.next_observed_hour",
            MachineSituationCategory.Forward,
            MachineSituationTimeScope.NextObservedHour,
            MachineSituationImportance.Context,
            MachineSituationFreshness.Current,
            MapMaturity(forecast.CurrentPowerMaturity),
            "Deterministic next observed hour forecast: " +
                string.Join("; ", nextValues) + ".",
            nextValues,
            forecast.RateReference is null
                ? []
                : [forecast.RateReference.ProviderName]));

        if (!forecast.HasEndOfDayForecast ||
            forecast.AvailabilityReason !=
                MachineUsageForecastAvailabilityReason.Available)
        {
            return;
        }
        var endValues = NonNull(
            KilowattHours(
                forecast.ProjectedEndOfDayObservedEnergyKilowattHours!.Value),
            forecast.ProjectedEndOfDayLowerKilowattHours is { } endLower &&
                forecast.ProjectedEndOfDayUpperKilowattHours is { } endUpper
                ? KilowattHourRange(endLower, endUpper)
                : null,
            FormatCost(forecast.ProjectedEndOfDayEstimatedCost,
                forecast.RateReference),
            Percent(forecast.ForecastCoverage * 100d),
            FormatMaturity(forecast.ForecastMaturity));
        items.Add(Create(
            "forward.end_of_day",
            MachineSituationCategory.Forward,
            MachineSituationTimeScope.EndOfDay,
            MachineSituationImportance.Context,
            MachineSituationFreshness.Current,
            MapMaturity(forecast.ForecastMaturity),
            "Evidence-covered end-of-day forecast: " +
                string.Join("; ", endValues) + ".",
            endValues,
            forecast.RateReference is null
                ? []
                : [forecast.RateReference.ProviderName]));
    }

    private static void AddActionContext(
        ICollection<MachineSituationEvidenceItem> items,
        MachineSituationInput input)
    {
        if (input.Startup is { } startup)
        {
            var manageable = startup.Items.Count(item =>
                item.ActionAvailability ==
                    MachineStartupActionAvailability.Supported);
            items.Add(Create(
                "actions.startup_inventory",
                MachineSituationCategory.ActionOutcome,
                MachineSituationTimeScope.Current,
                MachineSituationImportance.Routine,
                MachineSituationFreshness.Current,
                MachineSituationEvidenceMaturity.Verified,
                $"Startup inventory: {startup.Items.Count:N0} registrations; " +
                    $"{manageable:N0} manageable; " +
                    (startup.IsComplete ? "complete." : "partial."),
                [$"{startup.Items.Count:N0} registrations",
                    $"{manageable:N0} manageable",
                    startup.IsComplete ? "Complete" : "Partial"]));
        }

        var latest = input.ActionOutcomes
            .Where(outcome => outcome.CompletedAt is not null &&
                (outcome.Result == MachineActionResultStatus.SucceededVerified ||
                    outcome.UndoState ==
                        MachineActionUndoStatus.SucceededVerified))
            .OrderByDescending(outcome => outcome.UndoCompletedAt ??
                outcome.CompletedAt)
            .FirstOrDefault();
        if (latest is not null)
        {
            var restored = latest.UndoState ==
                MachineActionUndoStatus.SucceededVerified;
            var completed = latest.UndoCompletedAt ?? latest.CompletedAt!.Value;
            items.Add(Create(
                "actions.latest_verified",
                MachineSituationCategory.ActionOutcome,
                MachineSituationTimeScope.Recent,
                MachineSituationImportance.Context,
                MachineSituationFreshness.Recent,
                MachineSituationEvidenceMaturity.Verified,
                $"Latest verified controlled action for " +
                    $"{latest.Target.DisplayName}: " +
                    (restored
                        ? "the prior change was restored."
                        : $"{latest.RequestedEffect}; verified succeeded."),
                [restored ? "Restored" : "Succeeded and verified",
                    completed.ToLocalTime().ToString(
                        "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)],
                [latest.Target.DisplayName]));
        }

        var unresolved = input.ActionOutcomes
            .Where(IsUnresolvedAction)
            .OrderByDescending(outcome => outcome.UndoStartedAt ??
                outcome.StartedAt)
            .FirstOrDefault();
        if (unresolved is not null)
        {
            items.Add(Create(
                "actions.unresolved_recovery",
                MachineSituationCategory.ActionOutcome,
                MachineSituationTimeScope.Recent,
                MachineSituationImportance.Important,
                MachineSituationFreshness.Recent,
                MachineSituationEvidenceMaturity.Verified,
                $"Controlled-action recovery remains unresolved for " +
                    $"{unresolved.Target.DisplayName}.",
                ["Recovery unresolved"],
                [unresolved.Target.DisplayName]));
        }
    }

    private static void AddLearningAwareness(
        ICollection<MachineSituationEvidenceItem> items,
        MachineLearningAwareness awareness)
    {
        var values = new List<string>
        {
            $"{awareness.LifetimeAcceptedObservationCount:N0} lifetime observations",
            $"{awareness.RetainedObservationCount:N0} retained observations",
            $"{awareness.LearnedContextCount:N0} learned contexts",
            $"{awareness.CompactProfileCount:N0} compact profiles",
            $"{awareness.EstablishedContextCount:N0} established contexts",
            $"{awareness.CurrentContextSampleCount:N0} current-context samples",
            $"{awareness.CurrentContextObservedDayCount:N0} current-context observed days",
            FormatMaturity(awareness.CurrentContextMaturity),
            FormatMaturity(awareness.CurrentPowerMaturity),
            FormatEnum(awareness.PatternReadinessBlocker),
            FormatEnum(awareness.ForecastAvailability),
            Percent(awareness.ForecastCoverage * 100d)
        };
        items.Add(Create(
            "learning.awareness",
            MachineSituationCategory.LearningConfidence,
            MachineSituationTimeScope.CurrentContext,
            MachineSituationImportance.Context,
            awareness.CurrentContextFreshness is { } freshness
                ? MapFreshness(freshness)
                : MachineSituationFreshness.Unknown,
            MapMaturity(awareness.CurrentContextMaturity),
            $"Learning awareness: " +
                $"{awareness.LearnedContextCount:N0} learned contexts; " +
                $"{awareness.EstablishedContextCount:N0} established; " +
                $"current context {awareness.CurrentContextSampleCount:N0} " +
                $"samples across " +
                $"{awareness.CurrentContextObservedDayCount:N0} observed days; " +
                $"forecast availability " +
                $"{FormatEnum(awareness.ForecastAvailability)}.",
            values));
    }

    private static void AddSelfHealth(
        ICollection<MachineSituationEvidenceItem> items,
        MachineSituationInput input,
        DateTimeOffset now)
    {
        var ownedIncident = input.Reliability?.Incidents
            .Where(MatasuriRuntimeIdentityPolicy.IsOwnedRuntimeIncident)
            .OrderByDescending(incident => incident.OccurredAt)
            .FirstOrDefault();
        if (ownedIncident is not null)
        {
            items.Add(Create(
                "self_health.latest_runtime_incident",
                MachineSituationCategory.SelfHealth,
                MachineSituationTimeScope.Recent,
                MachineSituationImportance.Notable,
                GetFreshness(ownedIncident.OccurredAt, now),
                MachineSituationEvidenceMaturity.Verified,
                $"Matasuri self-health recorded a recent " +
                    $"{FormatEnum(ownedIncident.Category)} at " +
                    $"{ownedIncident.OccurredAt.ToLocalTime():yyyy-MM-dd HH:mm}; " +
                    "this is excluded from global machine posture.",
                [FormatEnum(ownedIncident.Category),
                    ownedIncident.OccurredAt.ToLocalTime().ToString(
                        "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)],
                [ownedIncident.ApplicationName ?? ownedIncident.Source]));
        }

        if (input.InferenceStatus is { } status &&
            (!status.IsRuntimeAvailable ||
                status.ModelState == LocalInferenceModelState.Faulted))
        {
            items.Add(Create(
                "self_health.inference",
                MachineSituationCategory.SelfHealth,
                MachineSituationTimeScope.Current,
                MachineSituationImportance.Notable,
                GetFreshness(status.CapturedAt, now),
                MachineSituationEvidenceMaturity.Verified,
                $"Matasuri Local AI self-health: " +
                    $"{status.Failure?.SafeMessage ?? "runtime faulted"}; " +
                    "this does not change global machine posture.",
                [status.IsRuntimeAvailable ? "Available" : "Unavailable",
                    FormatEnum(status.ModelState)],
                [status.RuntimeName]));
        }
    }

    private static MachineSituationEvidenceItem Create(
        string id,
        MachineSituationCategory category,
        MachineSituationTimeScope timeScope,
        MachineSituationImportance importance,
        MachineSituationFreshness freshness,
        MachineSituationEvidenceMaturity maturity,
        string summary,
        IReadOnlyList<string> displayValues,
        IReadOnlyList<string>? entityNames = null,
        bool allowsCausalLanguage = false) => new(
        id,
        category,
        timeScope,
        importance,
        freshness,
        maturity,
        summary,
        displayValues.Where(value =>
                !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray(),
        (entityNames ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray(),
        allowsCausalLanguage);

    private static MachineSituationImportance MapImportance(
        MachineFindingSeverity severity) => severity switch
        {
            MachineFindingSeverity.Critical =>
                MachineSituationImportance.Critical,
            MachineFindingSeverity.Warning =>
                MachineSituationImportance.Important,
            MachineFindingSeverity.Attention =>
                MachineSituationImportance.Notable,
            _ => MachineSituationImportance.Context
        };

    private static MachineSituationEvidenceMaturity MapMaturity(
        MachineLearningConfidence maturity) => maturity switch
        {
            MachineLearningConfidence.Established =>
                MachineSituationEvidenceMaturity.Established,
            MachineLearningConfidence.Provisional =>
                MachineSituationEvidenceMaturity.Provisional,
            _ => MachineSituationEvidenceMaturity.Early
        };

    private static MachineSituationEvidenceMaturity MapMaturity(
        MachineLearningEvidenceMaturity maturity) => maturity switch
        {
            MachineLearningEvidenceMaturity.Established =>
                MachineSituationEvidenceMaturity.Established,
            MachineLearningEvidenceMaturity.Provisional =>
                MachineSituationEvidenceMaturity.Provisional,
            _ => MachineSituationEvidenceMaturity.Early
        };

    private static MachineSituationFreshness MapFreshness(
        MachineLearningFreshness freshness) => freshness switch
        {
            MachineLearningFreshness.Fresh =>
                MachineSituationFreshness.Current,
            MachineLearningFreshness.Aging =>
                MachineSituationFreshness.Historical,
            MachineLearningFreshness.Stale =>
                MachineSituationFreshness.Stale,
            _ => MachineSituationFreshness.Unknown
        };

    private static MachineSituationFreshness GetFreshness(
        DateTimeOffset capturedAt,
        DateTimeOffset now)
    {
        var age = now <= capturedAt ? TimeSpan.Zero : now - capturedAt;
        return age <= TimeSpan.FromMinutes(5)
            ? MachineSituationFreshness.Current
            : age <= TimeSpan.FromHours(24)
                ? MachineSituationFreshness.Recent
                : age <= TimeSpan.FromDays(30)
                    ? MachineSituationFreshness.Historical
                    : MachineSituationFreshness.Stale;
    }

    private static (TimeSpan Active, TimeSpan Idle) GetTodayActivity(
        MachineHistorySnapshot? history,
        DateTimeOffset now)
    {
        if (history is null)
        {
            return (TimeSpan.Zero, TimeSpan.Zero);
        }
        var localDate = DateOnly.FromDateTime(now.ToLocalTime().Date);
        long activeTicks = 0;
        long idleTicks = 0;
        foreach (var rollup in history.Rollups.Where(rollup =>
            DateOnly.FromDateTime(rollup.BucketStart.ToLocalTime().Date) ==
                localDate))
        {
            activeTicks = SaturatingAdd(activeTicks,
                Math.Max(0, rollup.ActivityDurations.ActiveTicks));
            idleTicks = SaturatingAdd(idleTicks,
                Math.Max(0, rollup.ActivityDurations.IdleTicks));
        }
        return (
            TimeSpan.FromTicks(Math.Clamp(activeTicks, 0,
                TimeSpan.MaxValue.Ticks)),
            TimeSpan.FromTicks(Math.Clamp(idleTicks, 0,
                TimeSpan.MaxValue.Ticks)));
    }

    private static bool IsUnresolvedAction(MachineActionOutcome outcome) =>
        outcome.Result is MachineActionResultStatus.InProgress or
                MachineActionResultStatus.RecoveryUnknown ||
            outcome.UndoState is MachineActionUndoStatus.InProgress or
                MachineActionUndoStatus.RecoveryUnknown ||
            outcome.RecoveryClassification ==
                MachineActionRecoveryClassification.Unknown ||
            outcome.UndoRecoveryClassification ==
                MachineActionRecoveryClassification.Unknown;

    private static string[] ReliabilityValues(
        MachineReliabilityWindowSummary summary) =>
    [
        $"{summary.ApplicationCrashCount:N0} crashes",
        $"{summary.ApplicationHangCount:N0} hangs",
        $"{summary.UnexpectedShutdownCount:N0} unexpected shutdowns",
        $"{summary.UpdateFailureCount:N0} update failures",
        $"{summary.HardwareFailureCount:N0} hardware failures"
    ];

    private static IReadOnlyList<string> ExtractEntityNames(
        params string?[] values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .SelectMany(value => ExecutableNameRegex().Matches(value!)
            .Select(match => match.Value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(8)
        .ToArray();

    private static string StableId(string value)
    {
        var normalized = new string(value.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character)
                ? character
                : '-')
            .ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(normalized)
            ? "unknown"
            : normalized.Length <= 64
                ? normalized
                : normalized[..64];
    }

    private static string FormatEnum<T>(T value) where T : struct, Enum =>
        Regex.Replace(value.ToString(), "(?<!^)([A-Z])", " $1");

    private static string FormatMaturity(
        MachineLearningConfidence maturity) => maturity switch
        {
            MachineLearningConfidence.Established => "Established",
            MachineLearningConfidence.Provisional => "Provisional",
            _ => "Early evidence"
        };

    private static string FormatMaturity(
        MachineLearningEvidenceMaturity maturity) => maturity switch
        {
            MachineLearningEvidenceMaturity.Established => "Established",
            MachineLearningEvidenceMaturity.Provisional => "Provisional",
            _ => "Insufficient evidence"
        };

    private static string Percent(double value) =>
        $"{Math.Clamp(value, 0d, 100d).ToString("F1",
            CultureInfo.InvariantCulture)}%";

    private static string? OptionalPercent(double? value) => value is { } item
        && double.IsFinite(item) ? Percent(item) : null;

    private static string PercentRange(MachineLearningRange range) =>
        $"{range.Low.ToString("F1", CultureInfo.InvariantCulture)}–" +
        $"{range.High.ToString("F1", CultureInfo.InvariantCulture)}%";

    private static string? OptionalWatts(double? value) => value is { } item &&
        double.IsFinite(item) && item >= 0d
            ? $"{item.ToString("F1", CultureInfo.InvariantCulture)} W"
            : null;

    private static string WattRange(MachineLearningRange range) =>
        $"{range.Low.ToString("F1", CultureInfo.InvariantCulture)}–" +
        $"{range.High.ToString("F1", CultureInfo.InvariantCulture)} W";

    private static string KilowattHours(double value) =>
        $"{Math.Max(0d, value).ToString("F3",
            CultureInfo.InvariantCulture)} kWh";

    private static string KilowattHourRange(double lower, double upper) =>
        $"{Math.Max(0d, lower).ToString("F3",
            CultureInfo.InvariantCulture)}–" +
        $"{Math.Max(0d, upper).ToString("F3",
            CultureInfo.InvariantCulture)} kWh";

    private static string? FormatCost(decimal? cost,
        ElectricityRateSnapshot? rate) => cost is { } value && rate is not null
            ? $"{Currency(rate.CurrencyCode)}" +
                value.ToString("F2", CultureInfo.InvariantCulture)
            : null;

    private static string Currency(string currencyCode) =>
        string.Equals(currencyCode, "PHP", StringComparison.OrdinalIgnoreCase)
            ? "₱"
            : currencyCode + " ";

    private static string Gibibytes(ulong value) =>
        $"{(value / (1024d * 1024d * 1024d)).ToString("F1",
            CultureInfo.InvariantCulture)} GiB";

    private static string Gibibytes(long value) =>
        $"{(Math.Max(0, value) / (1024d * 1024d * 1024d)).ToString("F1",
            CultureInfo.InvariantCulture)} GiB";

    private static string? OptionalCelsius(double? value) => value is { } item
        && double.IsFinite(item)
            ? $"{item.ToString("F1", CultureInfo.InvariantCulture)} °C"
            : null;

    private static string? OptionalRate(double? value) => value is { } item &&
        double.IsFinite(item) && item >= 0d
            ? $"{item.ToString("F0", CultureInfo.InvariantCulture)} B/s"
            : null;

    private static string FormatDuration(TimeSpan duration)
    {
        var bounded = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        if (bounded.TotalDays >= 1d)
        {
            return $"{(int)bounded.TotalDays}d {bounded.Hours}h";
        }
        if (bounded.TotalHours >= 1d)
        {
            return $"{(int)bounded.TotalHours}h {bounded.Minutes}m";
        }
        if (bounded.TotalMinutes >= 1d)
        {
            return $"{(int)bounded.TotalMinutes}m";
        }
        return $"{Math.Max(0, bounded.Seconds)}s";
    }

    private static string[] NonNull(params string?[] values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .ToArray();

    private static string? Prefix(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{label} {value}";

    private static void AddIfNotNull(ICollection<string> values,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value);
        }
    }

    private static long SaturatingAdd(long left, long right) =>
        right > 0 && left > long.MaxValue - right
            ? long.MaxValue
            : left + right;

    [GeneratedRegex(@"\b[A-Za-z0-9_.-]+\.exe\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutableNameRegex();
}
