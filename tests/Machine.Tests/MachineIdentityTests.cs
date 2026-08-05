using Machine.Core;

namespace Machine.Tests;

public sealed class MachineIdentityTests
{
    [Fact]
    public void ConstructorPreservesValues()
    {
        var identity = new MachineIdentity(
            "TestDevice",
            "TestOS",
            "x64");

        Assert.Equal("TestDevice", identity.DeviceName);
        Assert.Equal("TestOS", identity.OperatingSystem);
        Assert.Equal("x64", identity.Architecture);
    }
}
