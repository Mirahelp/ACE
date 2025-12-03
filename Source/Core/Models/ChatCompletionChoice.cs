using System.Text.Json.Serialization;
using System;

namespace AgentCommandEnvironment.Core.Models;

public sealed class ChatCompletionChoice
{
    [JsonPropertyName("message")]
    public ChatCompletionMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public String? FinishReason { get; set; }
}
