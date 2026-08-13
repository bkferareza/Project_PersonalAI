using Machine.App;
using Machine.Core;

namespace Machine.Tests;

public sealed class MatasuriShellAtmosphereTests
{
    [Theory]
    [InlineData(MachineOverallState.Stable, 0x71, 0x8A, 0x84)]
    [InlineData(MachineOverallState.Attention, 0x9B, 0x85, 0x62)]
    [InlineData(MachineOverallState.Warning, 0xB3, 0x76, 0x52)]
    [InlineData(MachineOverallState.Critical, 0xA4, 0x53, 0x46)]
    [InlineData(MachineOverallState.Unknown, 0x75, 0x7E, 0x87)]
    public void CurrentDeterministicStateSelectsSemanticPalette(
        MachineOverallState state,
        byte red,
        byte green,
        byte blue)
    {
        var result = MatasuriShellAtmospherePolicy.Select(
            state,
            isGenerating: false,
            animationsEnabled: true);

        Assert.Equal(state, result.DeterministicState);
        Assert.Equal(new MatasuriColor(0xFF, red, green, blue),
            result.Accent);
        Assert.Equal(TimeSpan.FromMilliseconds(700),
            result.TransitionDuration);
        Assert.False(result.IsGenerating);
    }

    [Fact]
    public void GeneratingAddsOverlayWithoutChangingCurrentStatePalette()
    {
        var stable = MatasuriShellAtmospherePolicy.Select(
            MachineOverallState.Stable,
            false,
            true);
        var generating = MatasuriShellAtmospherePolicy.Select(
            MachineOverallState.Stable,
            true,
            true);

        Assert.Equal(stable.Atmosphere, generating.Atmosphere);
        Assert.Equal(stable.Accent, generating.Accent);
        Assert.Equal(MachineOverallState.Stable,
            generating.DeterministicState);
        Assert.True(generating.AnimateGeneratingOverlay);
    }

    [Fact]
    public void ReducedMotionMakesAppearanceChangeImmediate()
    {
        var result = MatasuriShellAtmospherePolicy.Select(
            MachineOverallState.Critical,
            true,
            animationsEnabled: false);

        Assert.Equal(TimeSpan.Zero, result.TransitionDuration);
        Assert.False(result.AnimateGeneratingOverlay);
        Assert.True(result.IsGenerating);
    }

    [Fact]
    public void HistoricalSeverityDoesNotEnterCurrentAtmospherePolicy()
    {
        var historicalCriticalEvent = new MachineHistoryEvent(
            DateTimeOffset.UnixEpoch,
            MachineHistoryEventKind.MachineStateChanged,
            "Historical Critical state",
            "Warning to Critical",
            "test",
            new string('A', 64));

        var result = MatasuriShellAtmospherePolicy.Select(
            MachineOverallState.Stable,
            isGenerating: false,
            animationsEnabled: true);

        Assert.Equal(MachineOverallState.Critical.ToString(),
            historicalCriticalEvent.Detail?.Split(' ').Last());
        Assert.Equal(MachineOverallState.Stable,
            result.DeterministicState);
        Assert.Equal(new MatasuriColor(0xFF, 0x71, 0x8A, 0x84),
            result.Accent);
    }
}
