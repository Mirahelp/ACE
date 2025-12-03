using System;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class ArchitectPlannedSubtaskResult
{
    [JsonPropertyName("intent")]
    public String? Intent { get; set; }

    [JsonPropertyName("type")]
    public String? Type { get; set; }

    [JsonPropertyName("notes")]
    public String? Notes { get; set; }

    [JsonPropertyName("phase")]
    public String? Phase { get; set; }
}
