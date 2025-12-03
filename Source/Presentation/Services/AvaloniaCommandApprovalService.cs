using AgentCommandEnvironment.Core.Interfaces;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AgentCommandEnvironment.Presentation.Services;

public sealed class AvaloniaCommandApprovalService : ICommandApprovalService
{
    private readonly Window owner;

    public AvaloniaCommandApprovalService(Window owner)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public async Task<Boolean> ConfirmCommandAsync(String commandDescription, Boolean isPotentiallyDangerous, Boolean isCriticallyDangerous, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (!isPotentiallyDangerous && !isCriticallyDangerous)
        {
            return true;
        }

        String title = "Confirmation";
        String severity = isCriticallyDangerous ? "a critically dangerous" : "a potentially dangerous";
        String message = "The system is about to execute " + severity + " command.";

        return await Dispatcher.UIThread.InvokeAsync(
            async () => await DialogService.ShowConfirmationAsync(owner, title, message, "Execute", "Cancel", commandDescription, true),
            DispatcherPriority.Normal);
    }
}
