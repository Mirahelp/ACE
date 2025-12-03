using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Models;

public sealed class OpenAiModel
{
    [JsonPropertyName("id")]
    public String? Id { get; set; }

    [JsonPropertyName("object")]
    public String? Object { get; set; }

    [JsonPropertyName("owned_by")]
    public String? OwnedBy { get; set; }
}

