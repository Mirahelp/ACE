using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class ArchitectPlanResult
{
    [JsonPropertyName("subtasks")]
    public List<ArchitectPlannedSubtaskResult>? Subtasks { get; set; }
}
