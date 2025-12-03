using System.Collections.Generic;
using System.Text.Json.Serialization;
using AgentCommandEnvironment.Core.Models;

namespace AgentCommandEnvironment.Core.Results;

public sealed class ChatCompletionStreamResult
{
    [JsonPropertyName("choices")]
    public List<ChatCompletionStreamChoiceResult>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public ChatCompletionUsage? Usage { get; set; }
}
