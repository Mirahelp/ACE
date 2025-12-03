using System;
using AgentCommandEnvironment.Core.Models;

namespace AgentCommandEnvironment.Core.Results;

public sealed class ChatStreamingResult
{
    public String RawContent { get; set; } = String.Empty;
    public ChatCompletionUsage? Usage { get; set; }
}

