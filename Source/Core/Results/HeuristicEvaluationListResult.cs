using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class HeuristicEvaluationListResult
{
    [JsonPropertyName("heuristics")]
    public List<HeuristicEvaluationResult>? Heuristics { get; set; }

    [JsonPropertyName("summary")]
    public String? Summary { get; set; }
}
