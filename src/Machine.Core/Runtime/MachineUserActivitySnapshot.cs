namespace Machine.Core;

public sealed record MachineUserActivitySnapshot(
    TimeSpan LastInputAge,
    MachineUserActivityState State,
    DateTimeOffset CapturedAt);
