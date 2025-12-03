using System;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Models;

public sealed class ChatCompletionUsage
{
    [JsonPropertyName("prompt_tokens")]
    public Int64 PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public Int64 CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public Int64 TotalTokens { get; set; }
}
