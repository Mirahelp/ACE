using AgentCommandEnvironment.Core.Models;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class StructuredAgentResult
{
    [JsonPropertyName("answer")]
    public String? Answer { get; set; }

    [JsonPropertyName("explanation")]
    public String? Explanation { get; set; }

    [JsonPropertyName("tasks")]
    public List<AgentPlannedTask>? Tasks { get; set; }

    [JsonIgnore]
    public Boolean IsStructured { get; set; }

    [JsonIgnore]
    public String RawContent { get; set; } = String.Empty;
}

