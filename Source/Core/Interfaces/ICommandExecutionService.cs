using AgentCommandEnvironment.Core.Models;
using AgentCommandEnvironment.Core.Results;

namespace AgentCommandEnvironment.Core.Interfaces;

public interface ICommandExecutionService
{
    Task<CommandExecutionResult> RunCommandAsync(
        SmartTaskExecutionContext taskItem,
        AgentCommandDescription commandDescription,
        String fileName,
        String arguments,
        String workingDirectory,
        CancellationToken cancellationToken);

    void StopAllBackgroundCommands(String reason);
}

