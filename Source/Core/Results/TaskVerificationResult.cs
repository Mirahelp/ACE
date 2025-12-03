using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class TaskVerificationResult
{
    [JsonPropertyName("accepted")]
    public List<ArchitectPlannedSubtaskResult>? Accepted { get; set; }

    [JsonPropertyName("rejected")]
    public List<TaskVerificationRejectionResult>? Rejected { get; set; }

    [JsonPropertyName("notes")]
    public String? Notes { get; set; }
}
