using AgentCommandEnvironment.Core.Models;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class StructuredFailureResolutionResult
{
    [JsonPropertyName("resolutionDecision")]
    public String? ResolutionDecision { get; set; }

    [JsonPropertyName("reason")]
    public String? Reason { get; set; }

    [JsonPropertyName("allowsDependentsToProceed")]
    public Boolean? AllowsDependentsToProceed { get; set; }

    [JsonPropertyName("notes")]
    public String? Notes { get; set; }

    [JsonPropertyName("newTasks")]
    public List<AgentPlannedTask>? NewTasks { get; set; }
}

