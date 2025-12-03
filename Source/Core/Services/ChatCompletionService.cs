using AgentCommandEnvironment.Core.Controllers;
using AgentCommandEnvironment.Core.Interfaces;
using AgentCommandEnvironment.Core.Models;
using AgentCommandEnvironment.Core.Results;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Core.Services;

public sealed class ChatCompletionService : IChatCompletionService
{
    private readonly AssignmentController assignmentController;
    private readonly HttpClient httpClient;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly AssignmentLogService assignmentLogService;

    public ChatCompletionService(
        AssignmentController assignmentController,
        HttpClient httpClient,
        JsonSerializerOptions jsonSerializerOptions,
        AssignmentLogService assignmentLogService)
    {
        this.assignmentController = assignmentController ?? throw new ArgumentNullException(nameof(assignmentController));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.jsonSerializerOptions = jsonSerializerOptions ?? throw new ArgumentNullException(nameof(jsonSerializerOptions));
        this.assignmentLogService = assignmentLogService ?? throw new ArgumentNullException(nameof(assignmentLogService));
    }

    public ChatCompletionMessage[] BuildPersonaMessages(String systemInstruction, String userInstruction)
    {
        ChatCompletionMessage systemMessage = new ChatCompletionMessage
        {
            Role = "system",
            Content = systemInstruction
        };

        ChatCompletionMessage userMessage = new ChatCompletionMessage
        {
            Role = "user",
            Content = userInstruction
        };

        return new[] { systemMessage, userMessage };
    }

    public async Task<ChatStreamingResult?> SendChatCompletionRequestAsync(
        ChatCompletionMessage[] messages,
        UsageChannelOptions UsageChannelOptions,
        String scopeDescription,
        CancellationToken cancellationToken,
        Boolean reserveBudget = true)
    {
        if (messages == null || messages.Length == 0)
        {
            throw new ArgumentException("At least one message is required.", nameof(messages));
        }

        String? apiKey = assignmentController.OpenAiApiKey;
        String? modelId = assignmentController.SelectedOpenAiModelId;
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        if (reserveBudget && !assignmentController.TryReserveOpenAiRequest(scopeDescription))
        {
            return null;
        }

        ChatCompletionRequest requestPayload = new ChatCompletionRequest
        {
            Model = modelId,
            Messages = messages,
            Stream = true,
            StreamOptions = new ChatCompletionStreamOptions
            {
                IncludeUsage = true
            }
        };

        String requestJson = JsonSerializer.Serialize(requestPayload, jsonSerializerOptions);

        try
        {
            using HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            requestMessage.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            assignmentController.RecordUsageRequest(scopeDescription, UsageChannelOptions);

            using HttpResponseMessage responseMessage = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!responseMessage.IsSuccessStatusCode)
            {
                assignmentController.RecordUsageFailure(scopeDescription, UsageChannelOptions);
                String errorBody = await responseMessage.Content.ReadAsStringAsync(cancellationToken);
                assignmentLogService.AppendSystemLog("OpenAI request (" + scopeDescription + ") failed: " + responseMessage.StatusCode + " - " + errorBody);
                return null;
            }

            Boolean logAsPlanner = UsageChannelOptions == UsageChannelOptions.Planner;
            ChatStreamingResult? streamingResult = await ReadChatCompletionStreamingContentAsync(responseMessage, logAsPlanner, cancellationToken);
            if (streamingResult == null)
            {
                assignmentController.RecordUsageFailure(scopeDescription, UsageChannelOptions);
                assignmentLogService.AppendSystemLog("OpenAI streaming response could not be read for scope '" + scopeDescription + "'.");
                return null;
            }

            assignmentController.RecordUsageSuccess(scopeDescription, streamingResult.Usage, UsageChannelOptions);
            return streamingResult;
        }
        catch
        {
            assignmentController.RecordUsageFailure(scopeDescription, UsageChannelOptions);
            throw;
        }
    }

    private async Task<ChatStreamingResult?> ReadChatCompletionStreamingContentAsync(HttpResponseMessage responseMessage, Boolean isPlanner, CancellationToken cancellationToken)
    {
        ChatStreamingResult? result = await Task.Run(async () =>
        {
            Stream responseStream = await responseMessage.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using (responseStream)
            using (StreamReader reader = new StreamReader(responseStream, Encoding.UTF8))
            {
                StringBuilder contentBuilder = new StringBuilder();
                ChatCompletionUsage? streamUsage = null;
                Int32 chunkCount = 0;
                DateTime lastStatusUpdateTime = DateTime.UtcNow;

                if (isPlanner)
                {
                    assignmentLogService.AppendSystemLog("Planner: waiting for streaming response from OpenAI...");
                }

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    String? line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                    {
                        break;
                    }

                    if (line.Length == 0 || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    String dataPart = line.Substring("data:".Length).Trim();
                    if (string.Equals(dataPart, "[DONE]", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    ChatCompletionStreamResult? streamResponse = null;
                    try
                    {
                        streamResponse = JsonSerializer.Deserialize<ChatCompletionStreamResult>(dataPart, jsonSerializerOptions);
                    }
                    catch
                    {
                        streamResponse = null;
                    }

                    if (streamResponse == null)
                    {
                        continue;
                    }

                    if (streamResponse.Choices != null)
                    {
                        for (Int32 index = 0; index < streamResponse.Choices.Count; index++)
                        {
                            ChatCompletionStreamChoiceResult choice = streamResponse.Choices[index];
                            if (choice.Delta != null && !string.IsNullOrEmpty(choice.Delta.Content))
                            {
                                contentBuilder.Append(choice.Delta.Content);
                                chunkCount = chunkCount + 1;
                            }
                        }
                    }

                    if (streamResponse.Usage != null)
                    {
                        streamUsage = streamResponse.Usage;
                    }

                    if (isPlanner)
                    {
                        DateTime now = DateTime.UtcNow;
                        TimeSpan elapsed = now - lastStatusUpdateTime;
                        if (elapsed.TotalSeconds >= 5.0)
                        {
                            Int32 characterCount = contentBuilder.Length;
                            assignmentLogService.AppendSystemLog("Planner: receiving response... " + chunkCount + " chunks, " + characterCount + " characters captured so far.");
                            lastStatusUpdateTime = now;
                        }
                    }
                }

                ChatStreamingResult innerResult = new ChatStreamingResult
                {
                    RawContent = contentBuilder.ToString(),
                    Usage = streamUsage
                };

                return innerResult;
            }
        }, cancellationToken).ConfigureAwait(false);

        return result;
    }
}


