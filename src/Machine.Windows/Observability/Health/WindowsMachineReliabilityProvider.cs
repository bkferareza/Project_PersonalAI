using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineReliabilityProvider
    : IMachineReliabilityProvider
{
    public static readonly TimeSpan RefreshInterval =
        TimeSpan.FromMinutes(10);
    public static readonly TimeSpan FailureRetryInterval =
        TimeSpan.FromMinutes(2);
    public static readonly TimeSpan AcquisitionTimeout =
        TimeSpan.FromSeconds(30);

    private readonly IWindowsReliabilitySource _source;
    private readonly Func<DateTimeOffset> _getUtcNow;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeSpan _failureRetryInterval;
    private readonly TimeSpan _acquisitionTimeout;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private MachineReliabilitySnapshot? _lastVerifiedSnapshot;
    private MachineReliabilitySnapshot? _lastResult;
    private DateTimeOffset? _nextRefreshAt;

    public WindowsMachineReliabilityProvider()
        : this(
            new WindowsEventLogReliabilitySource(),
            () => DateTimeOffset.UtcNow,
            RefreshInterval,
            FailureRetryInterval,
            AcquisitionTimeout)
    {
    }

    internal WindowsMachineReliabilityProvider(
        IWindowsReliabilitySource source,
        Func<DateTimeOffset> getUtcNow,
        TimeSpan refreshInterval,
        TimeSpan failureRetryInterval,
        TimeSpan acquisitionTimeout)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(getUtcNow);
        _source = source;
        _getUtcNow = getUtcNow;
        _refreshInterval = refreshInterval;
        _failureRetryInterval = failureRetryInterval;
        _acquisitionTimeout = acquisitionTimeout;
    }

    public async Task<MachineReliabilitySnapshot> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _getUtcNow();
        if (_lastResult is not null && _nextRefreshAt is not null &&
            now < _nextRefreshAt.Value)
        {
            return _lastResult;
        }

        await _refreshGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            now = _getUtcNow();
            if (_lastResult is not null && _nextRefreshAt is not null &&
                now < _nextRefreshAt.Value)
            {
                return _lastResult;
            }

            try
            {
                var acquisition = await _source
                    .CaptureAsync(cancellationToken)
                    .WaitAsync(_acquisitionTimeout, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var status = acquisition.SuccessfulSourceCount == 0
                    ? MachineHealthDataStatus.Unavailable
                    : acquisition.ReadFailureCount == 0
                        ? MachineHealthDataStatus.Complete
                        : MachineHealthDataStatus.Partial;
                if (status == MachineHealthDataStatus.Unavailable)
                {
                    throw new WindowsReliabilityAcquisitionException(
                        acquisition.FailureCode ?? "event-log-unavailable");
                }

                var snapshot = MachineReliabilityAggregator.Aggregate(
                    acquisition.Incidents,
                    capturedAt: now,
                    dataStatus: status,
                    readFailureCount: acquisition.ReadFailureCount,
                    verifiedAt: now,
                    failureCode: acquisition.FailureCode);
                _lastVerifiedSnapshot = snapshot;
                _lastResult = snapshot;
                _nextRefreshAt = now + _refreshInterval;
                return snapshot;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var failure = CreateFailureSnapshot(now, exception);
                _lastResult = failure;
                _nextRefreshAt = now + _failureRetryInterval;
                return failure;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private MachineReliabilitySnapshot CreateFailureSnapshot(
        DateTimeOffset capturedAt,
        Exception exception)
    {
        var code = exception is TimeoutException
            ? "timeout"
            : exception is WindowsReliabilityAcquisitionException acquisition
                ? acquisition.FailureCode
                : $"0x{exception.HResult:X8}";
        if (_lastVerifiedSnapshot is { } previous)
        {
            return previous with
            {
                CapturedAt = capturedAt,
                DataStatus = MachineHealthDataStatus.Partial,
                ReadFailureCount = Math.Max(1, previous.ReadFailureCount),
                FailureCode = code
            };
        }

        return MachineReliabilityAggregator.Aggregate(
            [],
            capturedAt,
            MachineHealthDataStatus.Unavailable,
            readFailureCount: 1,
            verifiedAt: null,
            failureCode: code);
    }
}
