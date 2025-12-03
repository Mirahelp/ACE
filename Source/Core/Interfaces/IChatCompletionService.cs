using AgentCommandEnvironment.Core.Models;
using AgentCommandEnvironment.Core.Results;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Core.Interfaces;

public interface IChatCompletionService
{
    ChatCompletionMessage[] BuildPersonaMessages(String systemInstruction, String userInstruction);
    Task<ChatStreamingResult?> SendChatCompletionRequestAsync(
        ChatCompletionMessage[] messages,
        UsageChannelOptions UsageChannelOptions,
        String scopeDescription,
        CancellationToken cancellationToken,
        Boolean reserveBudget = true);
}


