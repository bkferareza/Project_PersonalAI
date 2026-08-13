namespace Machine.Core;

public sealed record MachinePackagedSoftwareSnapshot(
    string DisplayName,
    string? PublisherDisplayName,
    string PackageFamilyName,
    string PackageFullName,
    string Version,
    MachinePackagedSoftwareArchitecture Architecture,
    string? InstalledLocation,
    bool? IsDevelopmentMode,
    bool? IsStub);
