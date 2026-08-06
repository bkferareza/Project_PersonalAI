namespace Machine.Core;

public sealed record MachineFindingsInput(
    MachineResourceSnapshot? Resources = null,
    MachineStorageSnapshot? Storage = null,
    MachineFolderInspectionSnapshot? FolderInspection = null,
    MachineSoftwareInventorySnapshot? ClassicSoftware = null,
    MachinePackagedSoftwareInventorySnapshot? PackagedSoftware = null,
    MachineStartupInventorySnapshot? Startup = null);
