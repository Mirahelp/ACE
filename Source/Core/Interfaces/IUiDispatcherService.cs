namespace AgentCommandEnvironment.Core.Interfaces;

public interface IUiDispatcherService
{
    Boolean CheckAccess();
    void Invoke(Action action);
}

