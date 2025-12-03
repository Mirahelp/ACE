using System;

namespace AgentCommandEnvironment.Core.Results;

public sealed class CommandRunResult
{
    public Boolean Succeeded { get; }
    public Boolean CommandsAttempted { get; }
    public String? FailureReason { get; }
    public CommandBlockReason BlockReason { get; }

    private CommandRunResult(Boolean succeeded, Boolean commandsAttempted, String? failureReason, CommandBlockReason blockReason)
    {
        Succeeded = succeeded;
        CommandsAttempted = commandsAttempted;
        FailureReason = failureReason;
        BlockReason = blockReason;
    }

    public static CommandRunResult Success()
    {
        return new CommandRunResult(true, true, null, CommandBlockReason.None);
    }

    public static CommandRunResult Failure(String? reason)
    {
        return new CommandRunResult(false, true, reason, CommandBlockReason.None);
    }

    public static CommandRunResult Blocked(String? reason, CommandBlockReason blockReason = CommandBlockReason.Unknown)
    {
        return new CommandRunResult(false, false, reason, blockReason);
    }
}

