using Microsoft.UI.Xaml.Controls;

namespace Machine.App.Features;

internal static class InventoryPresentation
{
    internal static string GetSelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";

    internal static string CreateStatus(
        bool isComplete,
        int readFailureCount,
        int truncatedItemCount)
    {
        if (isComplete)
        {
            return string.Empty;
        }

        var parts = new List<string> { "Inventory is partial" };
        if (readFailureCount > 0)
        {
            parts.Add($"{readFailureCount:N0} read " +
                (readFailureCount == 1 ? "failure" : "failures"));
        }
        if (truncatedItemCount > 0)
        {
            parts.Add($"{truncatedItemCount:N0} items beyond the bound");
        }
        return string.Join(" · ", parts);
    }
}
