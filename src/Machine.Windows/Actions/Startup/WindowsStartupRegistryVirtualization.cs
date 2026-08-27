namespace Machine.Windows;

internal static class WindowsStartupRegistryVirtualization
{
    // virtualization:RegistryWriteVirtualization is available from build
    // 20348. Older systems keep HKCU Run entries read-only because packaged
    // writes would otherwise be isolated in the package's private hive.
    internal const int MinimumWindowsBuild = 20_348;

    internal static bool IsSupported =>
        WindowsMachineStartupInventoryProvider
            .IsUnvirtualizedRegistryWriteSupported(
                Environment.OSVersion.Version);
}
