namespace AgentCommandEnvironment.Core.Results;

public enum CommandBlockReason
{
    None = 0,
    Unknown,
    MissingContext,
    MissingWorkspace,
    InvalidCommandDefinition,
    OperatorDeclined
}
