namespace Machine.Core;

public sealed record MachineEnergySnapshot(
    double SessionWattHours,
    double TodayWattHours,
    bool HasObservedEnergy);

public sealed class MachineEnergyAccumulator
{
    private readonly long _frequency;
    private readonly long _maximumGapTicks;
    private long? _lastTimestamp;
    private double? _lastWatts;
    private double _sessionWattHours;
    private double _todayWattHours;
    private DateOnly? _today;

    public MachineEnergyAccumulator(long frequency,
        TimeSpan? maximumGap = null)
    {
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        _frequency = frequency;
        _maximumGapTicks = (long)((maximumGap ?? TimeSpan.FromSeconds(6))
            .TotalSeconds * frequency);
    }

    public double Sample(double? watts, long monotonicTimestamp,
        DateTimeOffset wallClock)
    {
        var date = DateOnly.FromDateTime(wallClock.LocalDateTime.Date);
        if (_today is null || date != _today)
        {
            _today = date;
            _todayWattHours = 0d;
        }
        var deltaWh = 0d;
        if (_lastTimestamp is { } previous && _lastWatts is { } previousWatts &&
            watts is { } currentWatts && monotonicTimestamp > previous)
        {
            var elapsed = monotonicTimestamp - previous;
            if (elapsed <= _maximumGapTicks && currentWatts >= 0d &&
                double.IsFinite(currentWatts))
            {
                deltaWh = ((previousWatts + currentWatts) / 2d) *
                    (elapsed / (double)_frequency) / 3600d;
                _sessionWattHours += deltaWh;
                _todayWattHours += deltaWh;
            }
        }
        _lastTimestamp = monotonicTimestamp;
        _lastWatts = watts is { } value && value >= 0d && double.IsFinite(value)
            ? value : null;
        return deltaWh;
    }

    public MachineEnergySnapshot GetSnapshot() => new(_sessionWattHours,
        _todayWattHours, _sessionWattHours > 0d || _todayWattHours > 0d);
}

public sealed record ElectricityRateSnapshot(
    string ProviderName,
    string CurrencyCode,
    decimal RatePerKWh,
    DateOnly EffectiveMonth,
    DateTimeOffset RetrievedAt,
    string SourceIdentity,
    MachinePowerEstimateConfidence Confidence);

public static class MachineElectricityCostCalculator
{
    public static decimal? Calculate(double wattHours,
        ElectricityRateSnapshot? rate)
    {
        if (rate is null || wattHours < 0d || !double.IsFinite(wattHours))
        {
            return null;
        }
        return decimal.Round((decimal)(wattHours / 1000d) *
            rate.RatePerKWh, 2, MidpointRounding.ToEven);
    }
}
