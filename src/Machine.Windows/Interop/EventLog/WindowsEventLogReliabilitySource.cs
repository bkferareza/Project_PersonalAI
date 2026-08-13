using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Machine.Core;

namespace Machine.Windows;

internal interface IWindowsReliabilitySource
{
    Task<WindowsReliabilityAcquisition> CaptureAsync(
        CancellationToken cancellationToken);
}

internal sealed record WindowsReliabilityAcquisition(
    IReadOnlyList<MachineReliabilityIncident> Incidents,
    int SuccessfulSourceCount,
    int ReadFailureCount,
    string? FailureCode = null);

internal sealed record WindowsReliabilityEventRecord(
    string ProviderName,
    int EventId,
    DateTimeOffset OccurredAt,
    IReadOnlyDictionary<string, string?> Data);

internal sealed class WindowsEventLogReliabilitySource
    : IWindowsReliabilitySource
{
    private const int MaximumEventsPerChannel = 256;
    private const int MaximumStructuredValueLength = 512;
    private const int EventBatchSize = 16;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoMoreItems = 259;
    private const int MaximumRenderedXmlBytes = 1024 * 1024;
    private const int EvtQueryChannelPath = 0x1;
    private const int EvtQueryReverseDirection = 0x200;
    private const int EvtRenderEventXml = 1;
    private const string TimeFilter =
        "TimeCreated[timediff(@SystemTime) <= 2592300000]";

    private static readonly HashSet<string> RequiredDataNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "AppName",
        "FaultingApplicationName",
        "ApplicationName",
        "ModuleName",
        "FaultingModuleName",
        "EventName",
        "ReportId",
        "BugcheckCode",
        "errorCode",
        "ProductName",
        "updateTitle",
        "KnowledgeBaseId",
        "P1",
        "P4",
        "param1",
        "param4"
    };

    private static readonly QueryDefinition[] Queries =
    [
        new(
            "Application",
            "*[System[" + TimeFilter + " and (" +
            "(Provider[@Name='Application Error'] and EventID=1000) or " +
            "(Provider[@Name='Application Hang'] and EventID=1002) or " +
            "(Provider[@Name='MsiInstaller'] and EventID=11708)" +
            ")]]"),
        new(
            "Application",
            "*[System[" + TimeFilter + " and " +
            "Provider[@Name='Windows Error Reporting'] and EventID=1001] " +
            "and EventData[" +
            "Data[@Name='EventName']='APPCRASH' or " +
            "Data[@Name='EventName']='BEX' or " +
            "Data[@Name='EventName']='BEX64' or " +
            "Data[@Name='EventName']='CLR20r3' or " +
            "Data[@Name='EventName']='AppHangB1' or " +
            "Data[@Name='EventName']='AppHangXProcB1' or " +
            "Data[@Name='EventName']='AppHangXProcB2' or " +
            "Data[@Name='EventName']='AppHangTransient']]"),
        new(
            "System",
            "*[System[" + TimeFilter + " and (" +
            "(Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41) or " +
            "(Provider[@Name='EventLog'] and EventID=6008) or " +
            "(Provider[@Name='Microsoft-Windows-Eventlog'] and EventID=6008) or " +
            "(Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001) or " +
            "(Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and EventID=20) or " +
            "(Provider[@Name='Microsoft-Windows-WHEA-Logger'] and " +
            "(EventID=1 or EventID=17 or EventID=18 or EventID=19 or " +
            "EventID=20 or EventID=46 or EventID=47))" +
            ")]]"),
        new(
            "Microsoft-Windows-WindowsUpdateClient/Operational",
            "*[System[" + TimeFilter + " and " +
            "Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and " +
            "(EventID=25 or EventID=31 or EventID=34 or EventID=35)]]")
    ];

    public Task<WindowsReliabilityAcquisition> CaptureAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(
            () => Capture(cancellationToken),
            CancellationToken.None);
    }

    internal static MachineReliabilityIncident? MapEvent(
        WindowsReliabilityEventRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var provider = record.ProviderName;
        if (ProviderEquals(provider, "Application Error") &&
            record.EventId == 1000)
        {
            return CreateApplicationIncident(
                record,
                MachineReliabilityIncidentCategory.ApplicationCrash,
                "application.crash");
        }

        if (ProviderEquals(provider, "Application Hang") &&
            record.EventId == 1002)
        {
            return CreateApplicationIncident(
                record,
                MachineReliabilityIncidentCategory.ApplicationHang,
                "application.hang");
        }

        if (ProviderEquals(provider, "Windows Error Reporting") &&
            record.EventId == 1001)
        {
            var eventName = GetData(record, "EventName");
            if (eventName is not null &&
                eventName.StartsWith("AppHang", StringComparison.OrdinalIgnoreCase))
            {
                return CreateApplicationIncident(
                    record,
                    MachineReliabilityIncidentCategory.ApplicationHang,
                    "application.hang");
            }

            if (eventName is not null && IsApplicationCrashEvent(eventName))
            {
                return CreateApplicationIncident(
                    record,
                    MachineReliabilityIncidentCategory.ApplicationCrash,
                    "application.crash");
            }

            return null;
        }

        if ((ProviderEquals(provider, "Microsoft-Windows-Kernel-Power") &&
             record.EventId == 41) ||
            ((ProviderEquals(provider, "EventLog") ||
              ProviderEquals(provider, "Microsoft-Windows-Eventlog")) &&
             record.EventId == 6008))
        {
            return new MachineReliabilityIncident(
                record.OccurredAt,
                MachineReliabilityIncidentCategory.UnexpectedShutdown,
                MachineReliabilityIncidentSeverity.Significant,
                provider,
                null,
                null,
                null,
                record.EventId,
                "windows.unexpected-shutdown");
        }

        if (ProviderEquals(
                provider,
                "Microsoft-Windows-WER-SystemErrorReporting") &&
            record.EventId == 1001)
        {
            return new MachineReliabilityIncident(
                record.OccurredAt,
                MachineReliabilityIncidentCategory.WindowsFailure,
                MachineReliabilityIncidentSeverity.Severe,
                provider,
                null,
                null,
                null,
                record.EventId,
                "windows.bugcheck",
                NormalizeFailureCode(GetData(
                    record,
                    "BugcheckCode",
                    "param1")),
                NormalizeCorrelationId(GetData(record, "ReportId")));
        }

        if (ProviderEquals(provider, "Microsoft-Windows-WHEA-Logger") &&
            record.EventId is 1 or 17 or 18 or 19 or 20 or 46 or 47)
        {
            return new MachineReliabilityIncident(
                record.OccurredAt,
                MachineReliabilityIncidentCategory.HardwareFailure,
                record.EventId is 18 or 46
                    ? MachineReliabilityIncidentSeverity.Severe
                    : MachineReliabilityIncidentSeverity.Significant,
                provider,
                null,
                null,
                null,
                record.EventId,
                "windows.hardware-error");
        }

        if (ProviderEquals(
                provider,
                "Microsoft-Windows-WindowsUpdateClient") &&
            record.EventId is 20 or 25 or 31 or 34 or 35)
        {
            var category = record.EventId == 20
                ? MachineReliabilityIncidentCategory.InstallFailure
                : MachineReliabilityIncidentCategory.UpdateFailure;
            return new MachineReliabilityIncident(
                record.OccurredAt,
                category,
                MachineReliabilityIncidentSeverity.Notice,
                provider,
                null,
                null,
                FindKnowledgeBaseIdentifier(record),
                record.EventId,
                category == MachineReliabilityIncidentCategory.InstallFailure
                    ? "windows-update.install-failure"
                    : "windows-update.failure",
                NormalizeFailureCode(GetData(
                    record,
                    "errorCode",
                    "ErrorCode",
                    "param1")));
        }

        if (ProviderEquals(provider, "MsiInstaller") &&
            record.EventId == 11708)
        {
            return new MachineReliabilityIncident(
                record.OccurredAt,
                MachineReliabilityIncidentCategory.InstallFailure,
                MachineReliabilityIncidentSeverity.Notice,
                provider,
                MachineReliabilityAggregator.NormalizeApplicationIdentity(
                    GetData(record, "ProductName")),
                null,
                null,
                record.EventId,
                "application.install-failure");
        }

        return null;
    }

    private static WindowsReliabilityAcquisition Capture(
        CancellationToken cancellationToken)
    {
        var incidents = new List<MachineReliabilityIncident>();
        var successfulSources = 0;
        var failures = 0;
        string? failureCode = null;
        foreach (var query in Queries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = QueryEvents(query, cancellationToken);
                foreach (var record in result.Records)
                {
                    var incident = MapEvent(record);
                    if (incident is not null)
                    {
                        incidents.Add(incident);
                    }
                }
                successfulSources++;
                if (result.WasTruncated)
                {
                    failures++;
                    failureCode ??= "event-limit";
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is Win32Exception or InvalidOperationException or
                    FormatException or System.Xml.XmlException)
            {
                failures++;
                failureCode ??= $"0x{exception.HResult:X8}";
            }
        }

        return new WindowsReliabilityAcquisition(
            incidents,
            successfulSources,
            failures,
            failureCode);
    }

    private static EventQueryResult QueryEvents(
        QueryDefinition definition,
        CancellationToken cancellationToken)
    {
        var queryHandle = EvtQuery(
            IntPtr.Zero,
            definition.ChannelPath,
            definition.XPath,
            EvtQueryChannelPath | EvtQueryReverseDirection);
        if (queryHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var records = new List<WindowsReliabilityEventRecord>();
            var handles = new IntPtr[EventBatchSize];
            while (records.Count <= MaximumEventsPerChannel)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Array.Clear(handles);
                if (!EvtNext(
                        queryHandle,
                        (uint)handles.Length,
                        handles,
                        0,
                        0,
                        out var returned))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
                    }
                    throw new Win32Exception(error);
                }

                try
                {
                    for (var index = 0;
                         index < returned &&
                         records.Count <= MaximumEventsPerChannel;
                         index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var parsed = ParseEventXml(RenderEventXml(
                            handles[index]));
                        if (parsed is not null)
                        {
                            records.Add(parsed);
                        }
                    }
                }
                finally
                {
                    for (var index = 0; index < returned; index++)
                    {
                        if (handles[index] != IntPtr.Zero)
                        {
                            EvtClose(handles[index]);
                            handles[index] = IntPtr.Zero;
                        }
                    }
                }
            }

            return new EventQueryResult(
                records.Take(MaximumEventsPerChannel).ToArray(),
                records.Count > MaximumEventsPerChannel);
        }
        finally
        {
            EvtClose(queryHandle);
        }
    }

    private static string RenderEventXml(IntPtr eventHandle)
    {
        EvtRender(
            IntPtr.Zero,
            eventHandle,
            EvtRenderEventXml,
            0,
            IntPtr.Zero,
            out var bufferUsed,
            out _);
        var error = Marshal.GetLastWin32Error();
        if (error != ErrorInsufficientBuffer ||
            bufferUsed == 0 ||
            bufferUsed > MaximumRenderedXmlBytes)
        {
            throw new Win32Exception(error);
        }

        var buffer = Marshal.AllocHGlobal((int)bufferUsed);
        try
        {
            if (!EvtRender(
                    IntPtr.Zero,
                    eventHandle,
                    EvtRenderEventXml,
                    bufferUsed,
                    buffer,
                    out _,
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return Marshal.PtrToStringUni(buffer) ??
                throw new InvalidOperationException(
                    "Event XML rendering returned no data.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static WindowsReliabilityEventRecord? ParseEventXml(string xml)
    {
        var document = XDocument.Parse(xml, LoadOptions.None);
        XNamespace ns =
            "http://schemas.microsoft.com/win/2004/08/events/event";
        var system = document.Root?.Element(ns + "System");
        var provider = system?.Element(ns + "Provider")?
            .Attribute("Name")?.Value;
        var eventIdText = system?.Element(ns + "EventID")?.Value;
        var systemTime = system?.Element(ns + "TimeCreated")?
            .Attribute("SystemTime")?.Value;
        if (string.IsNullOrWhiteSpace(provider) ||
            !int.TryParse(
                eventIdText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var eventId) ||
            !DateTimeOffset.TryParse(
                systemTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal |
                    DateTimeStyles.AdjustToUniversal,
                out var occurredAt))
        {
            return null;
        }

        var data = new Dictionary<string, string?>(
            StringComparer.OrdinalIgnoreCase);
        var dataElements = document.Root?
            .Element(ns + "EventData")?
            .Elements(ns + "Data") ?? [];
        var index = 0;
        foreach (var element in dataElements)
        {
            var value = string.IsNullOrWhiteSpace(element.Value)
                ? null
                : element.Value.Trim();
            var name = element.Attribute("Name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"param{index + 1}";
            }
            if (value is not null &&
                value.Length <= MaximumStructuredValueLength &&
                IsRequiredDataName(name) &&
                !data.ContainsKey(name))
            {
                data.Add(name, value);
            }
            else if (value is not null &&
                value.Length <= MaximumStructuredValueLength &&
                MachineWindowsUpdatePolicy.NormalizeKnowledgeBaseId(value)
                    is { } knowledgeBaseId)
            {
                data.TryAdd("KnowledgeBaseId", knowledgeBaseId);
            }
            index++;
        }

        return new WindowsReliabilityEventRecord(
            provider.Trim(),
            eventId,
            occurredAt,
            data);
    }

    private static MachineReliabilityIncident CreateApplicationIncident(
        WindowsReliabilityEventRecord record,
        MachineReliabilityIncidentCategory category,
        string summaryCode)
    {
        var application = MachineReliabilityAggregator
            .NormalizeApplicationIdentity(GetData(
                record,
                "AppName",
                "FaultingApplicationName",
                "ApplicationName",
                "P1",
                "param1"));
        var faultModule = MachineReliabilityAggregator
            .NormalizeApplicationIdentity(GetData(
                record,
                "ModuleName",
                "FaultingModuleName",
                "P4",
                "param4"));
        return new MachineReliabilityIncident(
            record.OccurredAt,
            category,
            MachineReliabilityIncidentSeverity.Significant,
            record.ProviderName,
            application,
            faultModule,
            null,
            record.EventId,
            summaryCode,
            null,
            NormalizeCorrelationId(GetData(record, "ReportId")));
    }

    private static bool IsApplicationCrashEvent(string eventName) =>
        eventName.Equals("APPCRASH", StringComparison.OrdinalIgnoreCase) ||
        eventName.Equals("BEX", StringComparison.OrdinalIgnoreCase) ||
        eventName.Equals("BEX64", StringComparison.OrdinalIgnoreCase) ||
        eventName.Equals("CLR20r3", StringComparison.OrdinalIgnoreCase);

    private static string? FindKnowledgeBaseIdentifier(
        WindowsReliabilityEventRecord record)
    {
        foreach (var value in record.Data.Values)
        {
            var identifier =
                MachineWindowsUpdatePolicy.NormalizeKnowledgeBaseId(value);
            if (identifier is not null)
            {
                return identifier;
            }
        }

        return null;
    }

    private static bool IsRequiredDataName(string name) =>
        RequiredDataNames.Contains(name);

    private static string? GetData(
        WindowsReliabilityEventRecord record,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (record.Data.TryGetValue(name, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? NormalizeFailureCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= 32 && normalized.All(character =>
            char.IsAsciiHexDigit(character) ||
            character is 'x' or 'X' or '-' or '+')
                ? normalized
                : null;
    }

    private static string? NormalizeCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Trim('{', '}');
        return Guid.TryParse(normalized, out var guid)
            ? guid.ToString("D", CultureInfo.InvariantCulture)
            : null;
    }

    private static bool ProviderEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    [DllImport(
        "wevtapi.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr EvtQuery(
        IntPtr session,
        string path,
        string query,
        int flags);

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtNext(
        IntPtr resultSet,
        uint eventArraySize,
        [Out] IntPtr[] eventArray,
        uint timeout,
        uint flags,
        out uint returned);

    [DllImport("wevtapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtRender(
        IntPtr context,
        IntPtr fragment,
        int flags,
        uint bufferSize,
        IntPtr buffer,
        out uint bufferUsed,
        out uint propertyCount);

    [DllImport("wevtapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EvtClose(IntPtr objectHandle);

    private sealed record QueryDefinition(
        string ChannelPath,
        string XPath);

    private sealed record EventQueryResult(
        IReadOnlyList<WindowsReliabilityEventRecord> Records,
        bool WasTruncated);
}

internal sealed class WindowsReliabilityAcquisitionException
    : Exception
{
    public WindowsReliabilityAcquisitionException(string failureCode)
        : base(failureCode)
    {
        FailureCode = failureCode;
    }

    public string FailureCode { get; }
}
