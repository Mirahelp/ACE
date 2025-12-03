using System.Collections.Generic;
using System.Text.Json.Serialization;
using AgentCommandEnvironment.Core.Models;

namespace AgentCommandEnvironment.Core.Results;

public sealed class SuccessHeuristicPlanResult
{
    [JsonPropertyName("heuristics")]
    public List<SuccessHeuristicItem>? Heuristics { get; set; }
}
