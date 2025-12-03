using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Models;

public sealed class ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<ChatCompletionChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public ChatCompletionUsage? Usage { get; set; }
}
