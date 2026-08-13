using System.Runtime.InteropServices;
using Machine.Core;
using Machine.Windows;

namespace Machine.Tests;

public sealed class WindowsMachineScheduledTaskInventoryProviderTests
{
    [Fact]
    public async Task RecursivelyEnumeratesFoldersAndNormalizesSafeFields()
    {
        var session = new FakeTaskSession();
        session.Folders["\\"] = new(
            [Task("Root", "\\Root")],
            ["\\Microsoft", "\\Custom"],
            0,
            true);
        session.Folders["\\Microsoft"] = new(
            [Task("System", "\\Microsoft\\System", state: 4)],
            ["\\Microsoft\\Windows"],
            0,
            true);
        session.Folders["\\Microsoft\\Windows"] = new(
            [Task("Update", "\\Microsoft\\Windows\\Update")],
            [],
            0,
            true);
        session.Folders["\\Custom"] = new([], [], 0, true);
        var source = new FakeTaskSource(session);

        var snapshot = await new
            WindowsMachineScheduledTaskInventoryProvider(source).GetAsync();

        Assert.True(snapshot.IsComplete);
        Assert.Equal(3, snapshot.Items.Count);
        Assert.Equal(
            ["\\", "\\Custom", "\\Microsoft", "\\Microsoft\\Windows"],
            session.ReadPaths);
        var root = snapshot.Items.Single(item => item.Name == "Root");
        Assert.Equal("agent.exe", root.ExecutableName);
        Assert.Equal(
            [MachineScheduledTaskTriggerCategory.Time,
             MachineScheduledTaskTriggerCategory.Logon],
            root.TriggerCategories);
        Assert.Equal("Verified author", root.Author);
        Assert.DoesNotContain(
            typeof(MachineScheduledTaskSnapshot).GetProperties(),
            property => property.Name.Contains(
                "Argument",
                StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains(
                    "Xml",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AccessDeniedFolderAndDisappearingTaskRemainPartial()
    {
        var session = new FakeTaskSession();
        session.Folders["\\"] = new(
            [Task("One", "\\One")],
            ["\\Denied", "\\Partial"],
            0,
            true);
        session.Failures.Add("\\Denied");
        session.Folders["\\Partial"] = new(
            [],
            [],
            1,
            false);

        var snapshot = await new
            WindowsMachineScheduledTaskInventoryProvider(
                new FakeTaskSource(session)).GetAsync();

        Assert.False(snapshot.IsComplete);
        Assert.Equal(2, snapshot.ReadFailureCount);
        Assert.Single(snapshot.Items);
    }

    [Theory]
    [InlineData(0, MachineScheduledTaskState.Unknown)]
    [InlineData(1, MachineScheduledTaskState.Disabled)]
    [InlineData(2, MachineScheduledTaskState.Queued)]
    [InlineData(3, MachineScheduledTaskState.Ready)]
    [InlineData(4, MachineScheduledTaskState.Running)]
    public void MapsRuntimeState(
        int value,
        MachineScheduledTaskState expected)
    {
        Assert.Equal(expected,
            WindowsMachineScheduledTaskInventoryProvider.MapState(value));
    }

    [Theory]
    [InlineData(0, MachineScheduledTaskTriggerCategory.Event)]
    [InlineData(1, MachineScheduledTaskTriggerCategory.Time)]
    [InlineData(2, MachineScheduledTaskTriggerCategory.Calendar)]
    [InlineData(6, MachineScheduledTaskTriggerCategory.Idle)]
    [InlineData(8, MachineScheduledTaskTriggerCategory.Boot)]
    [InlineData(9, MachineScheduledTaskTriggerCategory.Logon)]
    [InlineData(11, MachineScheduledTaskTriggerCategory.Session)]
    [InlineData(99, MachineScheduledTaskTriggerCategory.Unknown)]
    public void NormalizesTriggerType(
        int value,
        MachineScheduledTaskTriggerCategory expected)
    {
        Assert.Equal(expected,
            WindowsMachineScheduledTaskInventoryProvider.MapTrigger(value));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0x41301, false)]
    [InlineData(1, true)]
    [InlineData(unchecked((int)0x80070005), true)]
    public void ClassifiesFailedLastResult(int result, bool expected)
    {
        Assert.Equal(
            expected,
            MachineScheduledTaskPolicy.IsFailedResult(result));
    }

    [Fact]
    public void ProjectionNeverRetainsActionArguments()
    {
        var raw = Task("Safe", "\\Safe") with
        {
            ExecutablePaths =
            [" C:\\Tools\\agent.exe ", "C:\\Other\\second.exe"]
        };

        var mapped =
            WindowsMachineScheduledTaskInventoryProvider.Map(raw);

        Assert.NotNull(mapped);
        Assert.Equal("agent.exe", mapped.ExecutableName);
        Assert.DoesNotContain("Tools", mapped.ExecutableName);
    }

    [Fact]
    public async Task ProjectionIsHardBounded()
    {
        var session = new FakeTaskSession();
        session.Folders["\\"] = new(
            Enumerable.Range(
                    0,
                    WindowsMachineScheduledTaskInventoryProvider
                        .MaximumTaskCount + 2)
                .Select(index => Task(
                    $"Task {index:D4}",
                    $"\\Task {index:D4}"))
                .ToArray(),
            [],
            0,
            true);

        var snapshot = await new
            WindowsMachineScheduledTaskInventoryProvider(
                new FakeTaskSource(session)).GetAsync();

        Assert.False(snapshot.IsComplete);
        Assert.Equal(
            WindowsMachineScheduledTaskInventoryProvider.MaximumTaskCount,
            snapshot.Items.Count);
        Assert.Equal(2, snapshot.TruncatedItemCount);
    }

    [Fact]
    public async Task PreCancelledRequestDoesNotOpenComSession()
    {
        var source = new FakeTaskSource(new FakeTaskSession());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new WindowsMachineScheduledTaskInventoryProvider(source)
                .GetAsync(cancellation.Token));

        Assert.Equal(0, source.OpenCount);
    }

    private static NativeScheduledTask Task(
        string name,
        string path,
        int state = 3) => new(
        name,
        path,
        true,
        state,
        DateTimeOffset.Parse("2026-08-14T01:00:00Z"),
        DateTimeOffset.Parse("2026-08-15T01:00:00Z"),
        0,
        [1, 9, 1],
        " Verified author ",
        ["C:\\Program Files\\Agent\\agent.exe"]);

    private sealed class FakeTaskSource(FakeTaskSession session)
        : IWindowsScheduledTaskSource
    {
        public int OpenCount { get; private set; }

        public IWindowsScheduledTaskSession Open(
            CancellationToken cancellationToken)
        {
            OpenCount++;
            return session;
        }
    }

    private sealed class FakeTaskSession : IWindowsScheduledTaskSession
    {
        public Dictionary<string, NativeScheduledTaskFolder> Folders
            { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Failures { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public List<string> ReadPaths { get; } = [];

        public NativeScheduledTaskFolder ReadFolder(
            string path,
            CancellationToken cancellationToken)
        {
            ReadPaths.Add(path);
            if (Failures.Contains(path))
            {
                throw new COMException("Access denied.");
            }
            return Folders[path];
        }

        public void Dispose()
        {
        }
    }
}
