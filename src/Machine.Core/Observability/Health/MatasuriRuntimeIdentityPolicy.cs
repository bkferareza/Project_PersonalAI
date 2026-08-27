namespace Machine.Core;

public static class MatasuriRuntimeIdentityPolicy
{
    public const string ExecutableName = "Machine.App.exe";
    public const string PackageIdentityName =
        "848F7F02-C9D0-4C05-BD8B-B04298378EE4";
    public const string PackagePublisherId = "1z32rh13vfry6";
    public const string PackageFamilyName =
        PackageIdentityName + "_" + PackagePublisherId;
    public const string PackageApplicationUserModelId =
        PackageFamilyName + "!App";

    public static bool IsOwnedRuntimeIncident(
        MachineReliabilityIncident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        return incident.Category is (
                MachineReliabilityIncidentCategory.ApplicationCrash or
                MachineReliabilityIncidentCategory.ApplicationHang) &&
            IsOwnedApplicationIdentity(incident.ApplicationName);
    }

    public static bool IsOwnedApplicationIdentity(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return false;
        }

        var normalized = identity.Trim().Trim('"', '\'');
        var finalSeparator = Math.Max(
            normalized.LastIndexOf('\\'),
            normalized.LastIndexOf('/'));
        if (finalSeparator >= 0 && finalSeparator < normalized.Length - 1)
        {
            normalized = normalized[(finalSeparator + 1)..];
        }

        if (string.Equals(
                normalized,
                ExecutableName,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                normalized,
                PackageIdentityName,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                normalized,
                PackageFamilyName,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                normalized,
                PackageApplicationUserModelId,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsCurrentPackageFullName(normalized);
    }

    private static bool IsCurrentPackageFullName(string identity) =>
        identity.StartsWith(
            PackageIdentityName + "_",
            StringComparison.OrdinalIgnoreCase) &&
        identity.EndsWith(
            "__" + PackagePublisherId,
            StringComparison.OrdinalIgnoreCase);
}
