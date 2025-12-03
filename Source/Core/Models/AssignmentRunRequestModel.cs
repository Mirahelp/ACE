namespace AgentCommandEnvironment.Core.Models;

public sealed class AssignmentRunRequestModel
{
    public String WorkspacePath { get; init; } = String.Empty;
    public String? AssignmentTitle { get; init; }
    public String AssignmentPrompt { get; init; } = String.Empty;
    public Boolean UseHandsFreeMode { get; init; }
    public Int32 MaxCommandRetryAttempts { get; init; } = 25;
    public Int32 MaxRepairAttemptsPerTask { get; init; } = 5;
    public IReadOnlyList<WorkspaceFileItem> WorkspaceContextFiles { get; init; } = System.Array.Empty<WorkspaceFileItem>();
}

