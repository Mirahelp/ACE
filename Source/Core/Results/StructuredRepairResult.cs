using AgentCommandEnvironment.Core.Models;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class StructuredRepairResult
{
    [JsonPropertyName("repairDecision")]
    public String? RepairDecision { get; set; }

    [JsonPropertyName("reason")]
    public String? Reason { get; set; }

    [JsonPropertyName("replacementCommands")]
    public List<AgentCommandDescription>? ReplacementCommands { get; set; }

    [JsonPropertyName("newTasks")]
    public List<AgentPlannedTask>? NewTasks { get; set; }
}

