using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using AgentCommandEnvironment.Presentation.Views;

namespace AgentCommandEnvironment.Presentation.Services;

internal static class DialogService
{
    public static Task ShowInfoAsync(Window owner, String title, String message, String? primaryButtonText = null)
    {
        String buttonText = String.IsNullOrWhiteSpace(primaryButtonText) ? "OK" : primaryButtonText;
        return ShowDialogAsync(owner, title, message, buttonText, null, null, false);
    }

    public static Task<Boolean> ShowConfirmationAsync(Window owner,
        String title,
        String message,
        String confirmText = "Execute",
        String cancelText = "Cancel",
        String? commandText = null,
        Boolean showApprovalPrompt = false)
    {
        return ShowDialogAsync(owner, title, message, confirmText, cancelText, commandText, showApprovalPrompt);
    }

    private static async Task<Boolean> ShowDialogAsync(Window owner,
        String title,
        String message,
        String primaryButtonText,
        String? secondaryButtonText,
        String? commandText,
        Boolean showApprovalPrompt)
    {
        PolicyWindow dialog = new PolicyWindow(title, message, primaryButtonText, secondaryButtonText, commandText, showApprovalPrompt);

        Boolean? result = await dialog.ShowDialog<Boolean?>(owner);
        if (result.HasValue)
        {
            return result.Value;
        }

        return String.IsNullOrWhiteSpace(secondaryButtonText);
    }
}
