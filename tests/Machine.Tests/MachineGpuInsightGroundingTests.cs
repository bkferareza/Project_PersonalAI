using Machine.Core;

namespace Machine.Tests;

public sealed class MachineGpuInsightGroundingTests
{
    [Fact]
    public void CurrentGpuClaimsRequireSuppliedExactValues()
    {
        var context = new MachineGpuInsightContext(37, 46, 58, 112);

        Assert.True(MachineExplanationValidator.IsValid(
            "GPU utilization is 37%, VRAM is 46%, GPU temperature is 58 C, and GPU board power is 112 W.",
            [],
            null,
            gpu: context));
        Assert.False(MachineExplanationValidator.IsValid(
            "GPU utilization is 81%.",
            [],
            null,
            gpu: context));
    }

    [Fact]
    public void MissingGpuValuesAreNotTreatedAsZero()
    {
        var context = new MachineGpuInsightContext(0, null, null, null);

        Assert.True(MachineExplanationValidator.IsValid(
            "GPU utilization is 0%.",
            [],
            null,
            gpu: context));
        Assert.False(MachineExplanationValidator.IsValid(
            "VRAM is 0%.",
            [],
            null,
            gpu: context));
    }

    [Fact]
    public void RawGpuMetricCannotClaimSeverity()
    {
        var context = new MachineGpuInsightContext(37, 46, 58, 112);

        Assert.False(MachineExplanationValidator.IsValid(
            "GPU is stable at 37%.",
            [],
            null,
            gpu: context));
    }
}
