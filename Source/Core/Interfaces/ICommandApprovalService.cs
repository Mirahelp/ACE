namespace AgentCommandEnvironment.Core.Interfaces;

public interface ICommandApprovalService
{
    Task<Boolean> ConfirmCommandAsync(String commandDescription, Boolean isPotentiallyDangerous, Boolean isCriticallyDangerous, CancellationToken cancellationToken);
}

