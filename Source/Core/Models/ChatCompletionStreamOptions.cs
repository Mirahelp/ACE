using System;
using System.Text.Json.Serialization;

namespace AgentCommandEnvironment.Core.Models;

public sealed class ChatCompletionStreamOptions
{
    [JsonPropertyName("include_usage")]
    public Boolean IncludeUsage { get; set; }
}
