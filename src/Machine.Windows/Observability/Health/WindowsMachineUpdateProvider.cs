using System.Runtime.InteropServices;
using Machine.Core;

namespace Machine.Windows;

public sealed class WindowsMachineUpdateProvider
    : IMachineWindowsUpdateProvider
{
    public static readonly TimeSpan RefreshInterval =
        TimeSpan.FromMinutes(45);
    public static readonly TimeSpan FailureRetryInterval =
        TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AcquisitionTimeout =
        TimeSpan.FromMinutes(3);

    private readonly IWindowsUpdateSnapshotSource _source;
    private readonly Func<DateTimeOffset> _getUtcNow;
    private readonly TimeSpan _refreshInterval;
    private readonly TimeSpan _failureRetryInterval;
    private readonly TimeSpan _acquisitionTimeout;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private MachineWindowsUpdateSnapshot? _lastVerifiedSnapshot;
    private MachineWindowsUpdateSnapshot? _lastResult;
    private DateTimeOffset? _nextRefreshAt;

    public WindowsMachineUpdateProvider()
        : this(
            new WindowsUpdateComSnapshotSource(),
            () => DateTimeOffset.UtcNow,
            RefreshInterval,
            FailureRetryInterval,
            AcquisitionTimeout)
    {
    }

    internal WindowsMachineUpdateProvider(
        IWindowsUpdateSnapshotSource source,
        Func<DateTimeOffset> getUtcNow,
        TimeSpan refreshInterval,
        TimeSpan failureRetryInterval,
        TimeSpan acquisitionTimeout)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(getUtcNow);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            refreshInterval,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            failureRetryInterval,
            TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(
            acquisitionTimeout,
            TimeSpan.Zero);
        _source = source;
        _getUtcNow = getUtcNow;
        _refreshInterval = refreshInterval;
        _failureRetryInterval = failureRetryInterval;
        _acquisitionTimeout = acquisitionTimeout;
    }

    public async Task<MachineWindowsUpdateSnapshot> GetAsync(
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
                var snapshot = await _source
                    .CaptureAsync(cancellationToken)
                    .WaitAsync(_acquisitionTimeout, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
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

    private MachineWindowsUpdateSnapshot CreateFailureSnapshot(
        DateTimeOffset capturedAt,
        Exception exception)
    {
        var failureCode = exception is TimeoutException
            ? "timeout"
            : $"0x{exception.HResult:X8}";
        var serviceAvailable = exception is
            WindowsUpdateAcquisitionException acquisition
                ? acquisition.ServiceAvailable
                : null;

        if (_lastVerifiedSnapshot is { } previous)
        {
            return previous with
            {
                CapturedAt = capturedAt,
                DataStatus = MachineHealthDataStatus.Partial,
                RefreshStatus =
                    MachineWindowsUpdateRefreshStatus.CachedAfterFailure,
                FailureCode = failureCode
            };
        }

        return new MachineWindowsUpdateSnapshot(
            CapturedAt: capturedAt,
            VerifiedAt: null,
            UpdateServiceAvailable: serviceAvailable,
            LastSuccessfulUpdateScan: null,
            LastSuccessfulUpdateInstall: null,
            PendingUpdateCount: null,
            PendingImportantUpdateCount: null,
            UpdateState: MachineWindowsUpdateState.Unknown,
            RecentUpdateHistory: [],
            DataStatus: MachineHealthDataStatus.Unavailable,
            RefreshStatus: MachineWindowsUpdateRefreshStatus.Unavailable,
            FailureCode: failureCode);
    }
}
