using System;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class DelegatorDecisionResult
{
    [JsonPropertyName("strategy")]
    public String? Strategy { get; set; }

    [JsonPropertyName("reason")]
    public String? Reason { get; set; }

    [JsonPropertyName("notes")]
    public String? Notes { get; set; }
}
