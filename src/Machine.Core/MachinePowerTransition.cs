namespace Machine.Core;

public enum MachinePowerTransitionKind
{
    Suspend,
    ResumeAutomatic,
    ResumeSuspend
}

public sealed record MachinePowerTransition(
    MachinePowerTransitionKind Kind,
    DateTimeOffset OccurredAt);
