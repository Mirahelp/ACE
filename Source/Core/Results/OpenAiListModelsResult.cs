using AgentCommandEnvironment.Core.Models;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Results;

public sealed class OpenAiListModelsResult
{
    [JsonPropertyName("data")]
    public List<OpenAiModel>? Data { get; set; }

    [JsonPropertyName("object")]
    public String? Object { get; set; }
}

