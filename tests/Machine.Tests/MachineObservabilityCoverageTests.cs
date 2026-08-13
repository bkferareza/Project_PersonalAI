using Machine.Core;

namespace Machine.Tests;

public sealed class MachineObservabilityCoverageTests
{
    [Fact]
    public void ReadOnlyV1DeclarationIncludesEveryRegisteredCapability()
    {
        var expectedKeys = new[]
        {
            "resources",
            "processes",
            "storage",
            "software",
            "startup",
            "network-session",
            "activity",
            "uptime",
            "windows-update",
            "reboot-pending",
            "reliability",
            "services",
            "tasks",
            "devices-drivers",
            "sleep-resume",
            "ollama-runtime"
        };

        Assert.Equal("READ_ONLY_OBSERVABILITY_V1_COMPLETE",
            MachineObservabilityCoverage.V1CompletionDeclaration);
        Assert.Equal(expectedKeys, MachineObservabilityCoverage.V1.Select(
            capability => capability.Key));
        Assert.All(MachineObservabilityCoverage.V1, capability =>
        {
            Assert.Equal(MachineObservabilityCoverageStatus.Complete,
                capability.Status);
            Assert.True(capability.IsReadOnly);
        });
    }

    [Fact]
    public void V2DeclaresOnlyGpuAsImplemented()
    {
        var gpu = Assert.Single(MachineObservabilityCoverage.V2,
            capability => capability.Status ==
                MachineObservabilityCoverageStatus.InitialImplementation);

        Assert.Equal("gpu", gpu.Key);
        Assert.True(gpu.IsReadOnly);
        Assert.All(MachineObservabilityCoverage.V2, capability =>
            Assert.True(capability.IsReadOnly));
    }
}
