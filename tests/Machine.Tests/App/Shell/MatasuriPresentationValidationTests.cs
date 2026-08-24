using Machine.App;
using Machine.Core;

namespace Machine.Tests;

public sealed class MatasuriPresentationValidationTests
{
    [Fact]
    public void DefaultsPreserveRealPresentation()
    {
        var result = MatasuriPresentationValidationOptions.Parse(null);

        Assert.Null(result.State);
        Assert.False(result.IsGenerating);
        Assert.False(result.HasNewInsight);
        Assert.False(result.ReducedMotion);
        Assert.Equal(MatasuriPresentationTheme.System, result.Theme);
    }

    [Fact]
    public void ParsesStateGeneratingAndThemeCaseInsensitively()
    {
        var result = MatasuriPresentationValidationOptions.Parse(
            "--matasuri-state=CRITICAL --matasuri-generating " +
            "--matasuri-theme=Light --matasuri-new-insight " +
            "--matasuri-reduced-motion");

        Assert.Equal(MachineOverallState.Critical, result.State);
        Assert.True(result.IsGenerating);
        Assert.True(result.HasNewInsight);
        Assert.True(result.ReducedMotion);
        Assert.Equal(MatasuriPresentationTheme.Light, result.Theme);
    }

    [Fact]
    public void IgnoresUnknownValues()
    {
        var result = MatasuriPresentationValidationOptions.Parse(
            "--matasuri-state=imaginary --matasuri-theme=sepia " +
            "--unrelated=value");

        Assert.Null(result.State);
        Assert.Equal(MatasuriPresentationTheme.System, result.Theme);
    }
}
