using AgentCommandEnvironment.Core.Enums;
namespace AgentCommandEnvironment.Core.Models;

public sealed class WorkspaceFileChangeRecord
{
    public WorkspaceFileChangeOptions Kind { get; set; }
    public String RelativePath { get; set; } = String.Empty;
    public FileSignature Previous { get; set; } = FileSignature.Empty;
    public FileSignature Current { get; set; } = FileSignature.Empty;
}


