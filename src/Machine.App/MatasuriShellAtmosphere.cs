using Machine.Core;

namespace Machine.App;

public readonly record struct MatasuriColor(
    byte Alpha,
    byte Red,
    byte Green,
    byte Blue);

public sealed record MatasuriShellAtmosphere(
    MachineOverallState DeterministicState,
    MatasuriColor Atmosphere,
    MatasuriColor Accent,
    bool IsGenerating,
    TimeSpan TransitionDuration,
    bool AnimateGeneratingOverlay);

public static class MatasuriShellAtmospherePolicy
{
    public static readonly TimeSpan DefaultTransitionDuration =
        TimeSpan.FromMilliseconds(700);

    public static MatasuriShellAtmosphere Select(
        MachineOverallState currentState,
        bool isGenerating,
        bool animationsEnabled)
    {
        var (atmosphere, accent) = currentState switch
        {
            MachineOverallState.Stable => (
                new MatasuriColor(0x32, 0x4B, 0x68, 0x69),
                new MatasuriColor(0xFF, 0x71, 0x8A, 0x84)),
            MachineOverallState.Attention => (
                new MatasuriColor(0x34, 0x79, 0x66, 0x49),
                new MatasuriColor(0xFF, 0x9B, 0x85, 0x62)),
            MachineOverallState.Warning => (
                new MatasuriColor(0x38, 0x82, 0x50, 0x35),
                new MatasuriColor(0xFF, 0xB3, 0x76, 0x52)),
            MachineOverallState.Critical => (
                new MatasuriColor(0x40, 0x74, 0x39, 0x33),
                new MatasuriColor(0xFF, 0xA4, 0x53, 0x46)),
            _ => (
                new MatasuriColor(0x2E, 0x59, 0x61, 0x69),
                new MatasuriColor(0xFF, 0x75, 0x7E, 0x87))
        };
        return new(
            Enum.IsDefined(currentState)
                ? currentState
                : MachineOverallState.Unknown,
            atmosphere,
            accent,
            isGenerating,
            animationsEnabled ? DefaultTransitionDuration : TimeSpan.Zero,
            isGenerating && animationsEnabled);
    }
}
