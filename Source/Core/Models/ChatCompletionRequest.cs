using System.Text.Json.Serialization;
using System;

namespace AgentCommandEnvironment.Core.Models;

public sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public String? Model { get; set; }

    [JsonPropertyName("messages")]
    public ChatCompletionMessage[]? Messages { get; set; }

    [JsonPropertyName("stream")]
    public Boolean Stream { get; set; }

    [JsonPropertyName("stream_options")]
    public ChatCompletionStreamOptions? StreamOptions { get; set; }
}
