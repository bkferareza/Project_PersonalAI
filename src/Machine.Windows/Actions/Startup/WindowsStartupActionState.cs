using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Machine.Core;

namespace Machine.Windows;

internal static class WindowsStartupActionState
{
    internal const string Disabled = "disabled";
    internal const string Missing = "missing";

    internal static string RegistryEnabled(
        MachineStartupRegistryValueKind kind,
        string unexpandedData) =>
        $"enabled|kind={(int)kind}|sha256={Hash(unexpandedData)}";

    internal static string FolderEnabled(long length, string sha256) =>
        $"enabled|length={length.ToString(CultureInfo.InvariantCulture)}" +
        $"|sha256={sha256}";

    internal static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    internal static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}

internal static class WindowsStartupSelfProtection
{
    private const string PackageIdentity =
        "848F7F02-C9D0-4C05-BD8B-B04298378EE4";

    internal static bool IsMatasuri(string? name, string? commandOrPath) =>
        Contains(name, "Matasuri") ||
        Contains(name, "Machine.App") ||
        Contains(commandOrPath, "Machine.App.exe") ||
        Contains(commandOrPath, PackageIdentity);

    private static bool Contains(string? value, string token) =>
        value?.Contains(token, StringComparison.OrdinalIgnoreCase) == true;
}
