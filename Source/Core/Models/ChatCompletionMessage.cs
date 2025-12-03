using System.Text.Json.Serialization;
using System;

namespace AgentCommandEnvironment.Core.Models;

public sealed class ChatCompletionMessage
{
    [JsonPropertyName("role")]
    public String? Role { get; set; }

    [JsonPropertyName("content")]
    public String? Content { get; set; }
}
