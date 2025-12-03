using System.Text.Json.Serialization;
using System;

namespace AgentCommandEnvironment.Core.Results;

public sealed class ChatCompletionStreamDeltaResult
{
    [JsonPropertyName("role")]
    public String? Role { get; set; }

    [JsonPropertyName("content")]
    public String? Content { get; set; }
}
