namespace Machine.Core;

public sealed record MachineSessionSnapshot(
    TimeSpan SystemUptime,
    TimeSpan MachineUptime,
    MachineUserActivityState CurrentUserInputState,
    TimeSpan CurrentUserIdleDuration,
    DateTimeOffset CapturedAt);
