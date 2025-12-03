using System;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class TaskVerificationRejectionResult
{
    [JsonPropertyName("intent")]
    public String? Intent { get; set; }

    [JsonPropertyName("reason")]
    public String? Reason { get; set; }
}
