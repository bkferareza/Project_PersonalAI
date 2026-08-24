using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Machine.Core;

public static class MachineActionFingerprint
{
    public static string CreatePrecondition(
        MachineActionTarget target,
        string normalizedState,
        params string[] providerEvidence)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(providerEvidence);
        MachineActionGuard.RequireText(normalizedState, 4_096,
            nameof(normalizedState));
        return Hash(
            ((int)target.Kind).ToString(CultureInfo.InvariantCulture),
            target.StableIdentity,
            normalizedState,
            providerEvidence);
    }

    internal static string CreatePlan(MachineActionPlan plan) => Hash(
        plan.ActionId.ToString("N", CultureInfo.InvariantCulture),
        ((int)plan.Capability).ToString(CultureInfo.InvariantCulture),
        ((int)plan.Target.Kind).ToString(CultureInfo.InvariantCulture),
        plan.Target.StableIdentity,
        plan.Target.DisplayName,
        plan.CurrentState,
        plan.CurrentNormalizedState,
        plan.RequestedState,
        plan.RequestedNormalizedState,
        plan.ChangeCategory,
        plan.ExpectedEffect,
        plan.NotAffected,
        plan.Reversible ? "1" : "0",
        plan.RequiresElevation ? "1" : "0",
        plan.Verification,
        plan.Limitations,
        plan.PreconditionFingerprint,
        plan.RecoveryPayload?.Version.ToString(
            CultureInfo.InvariantCulture) ?? string.Empty,
        plan.RecoveryPayload?.ProviderData ?? string.Empty,
        plan.CreatedAt.ToUniversalTime().ToString(
            "O", CultureInfo.InvariantCulture));

    internal static string CreateUndoPlan(MachineActionUndoPlan plan) => Hash(
        plan.UndoId.ToString("N", CultureInfo.InvariantCulture),
        plan.OriginalActionId.ToString("N", CultureInfo.InvariantCulture),
        ((int)plan.Capability).ToString(CultureInfo.InvariantCulture),
        ((int)plan.Target.Kind).ToString(CultureInfo.InvariantCulture),
        plan.Target.StableIdentity,
        plan.Target.DisplayName,
        plan.CurrentNormalizedState,
        plan.RestoreNormalizedState,
        plan.PreconditionFingerprint,
        plan.RecoveryPayload?.Version.ToString(
            CultureInfo.InvariantCulture) ?? string.Empty,
        plan.RecoveryPayload?.ProviderData ?? string.Empty,
        plan.CreatedAt.ToUniversalTime().ToString(
            "O", CultureInfo.InvariantCulture));

    private static string Hash(params object[] groups)
    {
        var builder = new StringBuilder();
        foreach (var group in groups)
        {
            if (group is string value)
            {
                Append(builder, value);
                continue;
            }

            if (group is IEnumerable<string> values)
            {
                foreach (var item in values)
                {
                    Append(builder, item ?? string.Empty);
                }
            }
        }

        return Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(value);
        builder.Append(';');
    }
}
