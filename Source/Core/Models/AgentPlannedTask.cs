using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Models;

public sealed class AgentPlannedTask
{
    [JsonPropertyName("id")]
    public String? Id { get; set; }

    [JsonPropertyName("label")]
    public String? Label { get; set; }

    [JsonPropertyName("type")]
    public String? Type { get; set; }

    [JsonPropertyName("description")]
    public String? Description { get; set; }

    [JsonPropertyName("context")]
    public String? Context { get; set; }

    [JsonPropertyName("priority")]
    public String? Priority { get; set; }

    [JsonPropertyName("phase")]
    public String? Phase { get; set; }

    [JsonPropertyName("contextTags")]
    public List<String>? ContextTags { get; set; }

    [JsonPropertyName("dependencies")]
    public List<String>? Dependencies { get; set; }

    [JsonPropertyName("commands")]
    public List<AgentCommandDescription>? Commands { get; set; }

    [JsonPropertyName("subtasks")]
    public List<AgentPlannedTask>? Subtasks { get; set; }
}

