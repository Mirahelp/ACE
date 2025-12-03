using AgentCommandEnvironment.Core.Interfaces;
using Avalonia.Threading;

namespace AgentCommandEnvironment.Presentation.Services;

public sealed class AvaloniaDispatcherService : IUiDispatcherService
{
    public Boolean CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }

    public void Invoke(Action action)
    {
        if (action == null)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(_ => action(), DispatcherPriority.Background);
        }
    }
}

