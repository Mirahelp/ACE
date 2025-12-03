using System;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class PolicyReviewResult
{
    [JsonPropertyName("allowed")]
    public Boolean Allowed { get; set; }

    [JsonPropertyName("reason")]
    public String? Reason { get; set; }

    [JsonPropertyName("riskLevel")]
    public String? RiskLevel { get; set; }
}
