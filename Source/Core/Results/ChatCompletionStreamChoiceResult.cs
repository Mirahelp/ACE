using System.Text.Json.Serialization;
using System;

namespace AgentCommandEnvironment.Core.Results;

public sealed class ChatCompletionStreamChoiceResult
{
    [JsonPropertyName("delta")]
    public ChatCompletionStreamDeltaResult? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public String? FinishReason { get; set; }
}
