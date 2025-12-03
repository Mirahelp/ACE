using System;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class HeuristicEvaluationResult
{
    [JsonPropertyName("index")]
    public Int32 Index { get; set; }

    [JsonPropertyName("passed")]
    public Boolean Passed { get; set; }

    [JsonPropertyName("notes")]
    public String? Notes { get; set; }
}
