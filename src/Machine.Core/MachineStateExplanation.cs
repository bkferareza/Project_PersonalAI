namespace Machine.Core;

public sealed record MachineStateExplanation(
    string Text,
    string Model,
    DateTimeOffset GeneratedAt);
