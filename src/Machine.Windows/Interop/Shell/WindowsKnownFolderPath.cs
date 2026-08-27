using System.Runtime.InteropServices;

namespace Machine.Windows;

internal static class WindowsKnownFolderPath
{
    private const uint NoPackageRedirection = 0x00010000;
    private static readonly Guid LocalAppData =
        new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");

    internal static string GetUnredirectedLocalAppData()
    {
        var folderId = LocalAppData;
        var result = SHGetKnownFolderPath(
            ref folderId,
            NoPackageRedirection,
            IntPtr.Zero,
            out var pathPointer);
        Marshal.ThrowExceptionForHR(result);
        try
        {
            var path = Marshal.PtrToStringUni(pathPointer);
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(
                    "The unredirected Local AppData path is unavailable.");
            }

            return Path.GetFullPath(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathPointer);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        ref Guid folderId,
        uint flags,
        IntPtr token,
        out IntPtr path);
}
