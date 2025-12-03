namespace AgentCommandEnvironment.Core.Models;

public sealed class CompletedIntentRecord
{
    public String Hash { get; set; } = String.Empty;
    public String Intent { get; set; } = String.Empty;
    public DateTime CompletedAtUtc { get; set; }
}

