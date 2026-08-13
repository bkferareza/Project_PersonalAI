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

internal interface IWindowsUpdateSnapshotSource
{
    Task<MachineWindowsUpdateSnapshot> CaptureAsync(
        CancellationToken cancellationToken);
}

internal sealed class WindowsUpdateComSnapshotSource
    : IWindowsUpdateSnapshotSource
{
    private const string UpdateSessionProgramId =
        "Microsoft.Update.Session";
    private const string AutomaticUpdatesProgramId =
        "Microsoft.Update.AutoUpdate";
    private const string SystemInformationProgramId =
        "Microsoft.Update.SystemInfo";
    private const string PendingSearchCriteria =
        "IsInstalled=0 and IsHidden=0";
    private const int InstallationOperation = 1;

    public Task<MachineWindowsUpdateSnapshot> CaptureAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => Capture(cancellationToken),
            CancellationToken.None);
    }

    internal static MachineWindowsUpdateHistoryResult
        MapOperationResultCode(int resultCode) => resultCode switch
    {
        1 => MachineWindowsUpdateHistoryResult.InProgress,
        2 => MachineWindowsUpdateHistoryResult.Succeeded,
        3 => MachineWindowsUpdateHistoryResult.SucceededWithErrors,
        4 => MachineWindowsUpdateHistoryResult.Failed,
        5 => MachineWindowsUpdateHistoryResult.Cancelled,
        _ => MachineWindowsUpdateHistoryResult.Unknown
    };

    private static MachineWindowsUpdateSnapshot Capture(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessionType = Type.GetTypeFromProgID(
            UpdateSessionProgramId,
            throwOnError: false);
        if (sessionType is null)
        {
            throw new WindowsUpdateAcquisitionException(
                "Windows Update Agent is unavailable.",
                serviceAvailable: false);
        }

        object? session = null;
        object? searcher = null;
        object? searchResult = null;
        object? updates = null;
        try
        {
            session = Activator.CreateInstance(sessionType) ??
                throw new WindowsUpdateAcquisitionException(
                    "Windows Update Agent could not be created.",
                    serviceAvailable: false);
            dynamic dynamicSession = session;
            dynamicSession.ClientApplicationID = "Machine";
            searcher = (object?)dynamicSession.CreateUpdateSearcher() ??
                throw new WindowsUpdateAcquisitionException(
                    "Windows Update searcher could not be created.",
                    serviceAvailable: true);
            dynamic dynamicSearcher = searcher;
            dynamicSearcher.Online = false;
            dynamicSearcher.CanAutomaticallyUpgradeService = false;

            cancellationToken.ThrowIfCancellationRequested();
            var history = TryReadHistory(
                searcher,
                cancellationToken);
            var automaticResults = ReadAutomaticUpdateResults();
            var restartRequired = ReadRestartRequired();

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                searchResult = dynamicSearcher.Search(PendingSearchCriteria);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new WindowsUpdateAcquisitionException(
                    "The local Windows Update search failed.",
                    serviceAvailable: true,
                    exception);
            }

            dynamic dynamicSearchResult = searchResult;
            var searchResultCode = Convert.ToInt32(
                (object?)dynamicSearchResult.ResultCode,
                System.Globalization.CultureInfo.InvariantCulture);
            var mappedSearchResult = MapOperationResultCode(searchResultCode);
            if (mappedSearchResult is not
                (MachineWindowsUpdateHistoryResult.Succeeded or
                 MachineWindowsUpdateHistoryResult.SucceededWithErrors))
            {
                throw new WindowsUpdateAcquisitionException(
                    $"The local Windows Update search returned " +
                    $"result code {searchResultCode}.",
                    serviceAvailable: true);
            }

            updates = dynamicSearchResult.Updates;
            dynamic dynamicUpdates = updates;
            var pendingCount = Convert.ToInt32(
                (object?)dynamicUpdates.Count,
                System.Globalization.CultureInfo.InvariantCulture);
            var importantCount = 0;
            var downloadedCount = 0;
            for (var index = 0; index < pendingCount; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? update = null;
                try
                {
                    update = dynamicUpdates.Item(index);
                    dynamic dynamicUpdate = update;
                    if (ReadBoolean((object?)dynamicUpdate.IsDownloaded))
                    {
                        downloadedCount++;
                    }

                    var severity = ReadString(
                        (object?)dynamicUpdate.MsrcSeverity);
                    if (string.Equals(
                            severity,
                            "Critical",
                            StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(
                            severity,
                            "Important",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        importantCount++;
                    }
                }
                finally
                {
                    ReleaseComObject(update);
                }
            }

            var capturedAt = DateTimeOffset.UtcNow;
            var dataStatus = mappedSearchResult ==
                MachineWindowsUpdateHistoryResult.SucceededWithErrors ||
                !history.WasRead || !automaticResults.WasRead ||
                restartRequired is null
                    ? MachineHealthDataStatus.Partial
                    : MachineHealthDataStatus.Complete;
            return new MachineWindowsUpdateSnapshot(
                CapturedAt: capturedAt,
                VerifiedAt: capturedAt,
                UpdateServiceAvailable: true,
                LastSuccessfulUpdateScan: automaticResults.LastSearch,
                LastSuccessfulUpdateInstall: automaticResults.LastInstall,
                PendingUpdateCount: pendingCount,
                PendingImportantUpdateCount: importantCount,
                UpdateState: restartRequired is null
                    ? MachineWindowsUpdateState.Unknown
                    : MachineWindowsUpdatePolicy.EvaluateState(
                        serviceAvailable: true,
                        searchSucceeded: true,
                        pendingUpdateCount: pendingCount,
                        downloadedPendingUpdateCount: downloadedCount,
                        restartRequired: restartRequired.Value),
                RecentUpdateHistory: history.Entries,
                DataStatus: dataStatus,
                RefreshStatus: MachineWindowsUpdateRefreshStatus.Verified);
        }
        catch (WindowsUpdateAcquisitionException)
        {
            throw;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new WindowsUpdateAcquisitionException(
                "Windows Update Agent acquisition failed.",
                serviceAvailable: true,
                exception);
        }
        finally
        {
            ReleaseComObject(updates);
            ReleaseComObject(searchResult);
            ReleaseComObject(searcher);
            ReleaseComObject(session);
        }
    }

    private static IReadOnlyList<MachineWindowsUpdateHistoryEntry>
        ReadHistory(object searcher, CancellationToken cancellationToken)
    {
        dynamic dynamicSearcher = searcher;
        object? entries = null;
        try
        {
            var totalCount = Math.Max(0, Convert.ToInt32(
                (object?)dynamicSearcher.GetTotalHistoryCount(),
                System.Globalization.CultureInfo.InvariantCulture));
            var requestedCount = Math.Min(
                totalCount,
                MachineWindowsUpdatePolicy.MaximumHistoryCount * 2);
            if (requestedCount == 0)
            {
                return [];
            }

            entries = dynamicSearcher.QueryHistory(0, requestedCount);
            dynamic dynamicEntries = entries;
            var count = Convert.ToInt32(
                (object?)dynamicEntries.Count,
                System.Globalization.CultureInfo.InvariantCulture);
            var history = new List<MachineWindowsUpdateHistoryEntry>(count);
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? entry = null;
                object? categories = null;
                try
                {
                    entry = dynamicEntries.Item(index);
                    dynamic dynamicEntry = entry;
                    var operation = Convert.ToInt32(
                        (object?)dynamicEntry.Operation,
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (operation != InstallationOperation)
                    {
                        continue;
                    }

                    var title = ReadString((object?)dynamicEntry.Title);
                    DateTimeOffset? occurredAt = ReadDateTime(
                        (object?)dynamicEntry.Date);
                    if (title is null || occurredAt is null)
                    {
                        continue;
                    }

                    string? category = null;
                    try
                    {
                        categories = dynamicEntry.Categories;
                        dynamic dynamicCategories = categories;
                        if (Convert.ToInt32(
                                (object?)dynamicCategories.Count,
                                System.Globalization.CultureInfo
                                    .InvariantCulture) > 0)
                        {
                            object? categoryItem = null;
                            try
                            {
                                categoryItem = dynamicCategories.Item(0);
                                dynamic dynamicCategory = categoryItem;
                                category = ReadString(
                                    (object?)dynamicCategory.Name);
                            }
                            finally
                            {
                                ReleaseComObject(categoryItem);
                            }
                        }
                    }
                    catch (COMException)
                    {
                    }

                    history.Add(new MachineWindowsUpdateHistoryEntry(
                        occurredAt.Value,
                        title,
                        category,
                        MachineWindowsUpdatePolicy.NormalizeKnowledgeBaseId(
                            title),
                        MapOperationResultCode(Convert.ToInt32(
                            (object?)dynamicEntry.ResultCode,
                            System.Globalization.CultureInfo
                                .InvariantCulture))));
                }
                finally
                {
                    ReleaseComObject(categories);
                    ReleaseComObject(entry);
                }
            }

            return MachineWindowsUpdatePolicy.NormalizeHistory(history);
        }
        finally
        {
            ReleaseComObject(entries);
        }
    }

    private static HistoryReadResult TryReadHistory(
        object searcher,
        CancellationToken cancellationToken)
    {
        try
        {
            return new HistoryReadResult(
                ReadHistory(searcher, cancellationToken),
                true);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            IsAuxiliaryReadException(exception))
        {
            return new HistoryReadResult([], false);
        }
    }

    private static (
        DateTimeOffset? LastSearch,
        DateTimeOffset? LastInstall,
        bool WasRead)
        ReadAutomaticUpdateResults()
    {
        object? automaticUpdates = null;
        object? results = null;
        try
        {
            var type = Type.GetTypeFromProgID(
                AutomaticUpdatesProgramId,
                throwOnError: false);
            if (type is null)
            {
                return (null, null, false);
            }

            automaticUpdates = Activator.CreateInstance(type);
            if (automaticUpdates is null)
            {
                return (null, null, false);
            }

            dynamic dynamicAutomaticUpdates = automaticUpdates;
            results = dynamicAutomaticUpdates.Results;
            dynamic dynamicResults = results;
            return (
                ReadDateTime(
                    (object?)dynamicResults.LastSearchSuccessDate),
                ReadDateTime(
                    (object?)dynamicResults.LastInstallationSuccessDate),
                true);
        }
        catch (Exception exception) when (IsAuxiliaryReadException(exception))
        {
            return (null, null, false);
        }
        finally
        {
            ReleaseComObject(results);
            ReleaseComObject(automaticUpdates);
        }
    }

    private static bool? ReadRestartRequired()
    {
        object? systemInformation = null;
        try
        {
            var type = Type.GetTypeFromProgID(
                SystemInformationProgramId,
                throwOnError: false);
            if (type is null)
            {
                return null;
            }

            systemInformation = Activator.CreateInstance(type);
            if (systemInformation is null)
            {
                return null;
            }

            dynamic dynamicSystemInformation = systemInformation;
            return ReadBoolean(
                (object?)dynamicSystemInformation.RebootRequired);
        }
        catch (Exception exception) when (IsAuxiliaryReadException(exception))
        {
            return null;
        }
        finally
        {
            ReleaseComObject(systemInformation);
        }
    }

    private static DateTimeOffset? ReadDateTime(object? value)
    {
        if (value is not DateTime date || date == DateTime.MinValue)
        {
            return null;
        }

        return new DateTimeOffset(
            DateTime.SpecifyKind(date, DateTimeKind.Utc));
    }

    private static string? ReadString(object? value)
    {
        var text = value as string;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool ReadBoolean(object? value) =>
        value is bool boolean && boolean;

    private static bool IsAuxiliaryReadException(Exception exception) =>
        exception is COMException or
            UnauthorizedAccessException or
            System.Security.SecurityException or
            InvalidCastException or
            FormatException or
            OverflowException or
            Microsoft.CSharp.RuntimeBinder.RuntimeBinderException;

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private readonly record struct HistoryReadResult(
        IReadOnlyList<MachineWindowsUpdateHistoryEntry> Entries,
        bool WasRead);
}

internal sealed class WindowsUpdateAcquisitionException
    : Exception
{
    public WindowsUpdateAcquisitionException(
        string message,
        bool? serviceAvailable,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ServiceAvailable = serviceAvailable;
    }

    public bool? ServiceAvailable { get; }
}
