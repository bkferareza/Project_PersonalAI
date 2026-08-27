using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Machine.Core;

public static class MachineStartupIdentity
{
    public static string CreateRegistryRunEntry(
        MachineStartupScope scope,
        MachineStartupRegistryView registryView,
        string exactValueName)
    {
        ArgumentNullException.ThrowIfNull(exactValueName);
        return Hash(
            "windows-registry-run",
            ((int)scope).ToString(CultureInfo.InvariantCulture),
            ((int)registryView).ToString(CultureInfo.InvariantCulture),
            exactValueName);
    }

    public static string CreateStartupFolderEntry(
        MachineStartupScope scope,
        string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        return Hash(
            "windows-startup-folder",
            ((int)scope).ToString(CultureInfo.InvariantCulture),
            canonicalPath);
    }

    private static string Hash(params string[] groups)
    {
        var builder = new StringBuilder();
        foreach (var value in groups)
        {
            builder.Append(value.Length.ToString(
                CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(value);
            builder.Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }
}
