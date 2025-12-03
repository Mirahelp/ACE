using AgentCommandEnvironment.Core.Constants;
using AgentCommandEnvironment.Core.Controllers;
using AgentCommandEnvironment.Core.Interfaces;
using AgentCommandEnvironment.Core.Models;
using AgentCommandEnvironment.Core.Results;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Core.Services;

public sealed class AssignmentRuntimeService : IAssignmentRuntimeService
{
    private readonly AssignmentController assignmentController;
    private readonly HttpClient httpClient;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly WorkspaceStateTrackerService workspaceStateTracker;
    private readonly GlobalContext globalContext;
    private readonly SmartTaskSchedulerService smartTaskScheduler;
    private readonly IUiDispatcherService dispatcherService;
    private readonly ICommandApprovalService commandApprovalService;
    private readonly AssignmentLogService assignmentLogService;
    private readonly IChatCompletionService chatCompletionService;
    private readonly ObservableCollection<SmartTaskExecutionContext> assignmentTaskItems;

    public AssignmentRuntimeService(
        AssignmentController assignmentController,
        HttpClient httpClient,
        JsonSerializerOptions jsonSerializerOptions,
        WorkspaceStateTrackerService workspaceStateTracker,
        GlobalContext globalContext,
        SmartTaskSchedulerService smartTaskScheduler,
        IUiDispatcherService dispatcherService,
        ICommandApprovalService commandApprovalService,
        AssignmentLogService assignmentLogService,
        IChatCompletionService chatCompletionService)
    {
        this.assignmentController = assignmentController ?? throw new ArgumentNullException(nameof(assignmentController));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.jsonSerializerOptions = jsonSerializerOptions ?? throw new ArgumentNullException(nameof(jsonSerializerOptions));
        this.workspaceStateTracker = workspaceStateTracker ?? throw new ArgumentNullException(nameof(workspaceStateTracker));
        this.globalContext = globalContext ?? throw new ArgumentNullException(nameof(globalContext));
        this.smartTaskScheduler = smartTaskScheduler ?? throw new ArgumentNullException(nameof(smartTaskScheduler));
        this.dispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));
        this.commandApprovalService = commandApprovalService ?? throw new ArgumentNullException(nameof(commandApprovalService));
        this.assignmentLogService = assignmentLogService ?? throw new ArgumentNullException(nameof(assignmentLogService));
        this.chatCompletionService = chatCompletionService ?? throw new ArgumentNullException(nameof(chatCompletionService));

        assignmentTaskItems = assignmentController.AssignmentTasks;
    }

    public Task<AssignmentRunResult> RunAsync(AssignmentRunRequestModel request, CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        throw new NotImplementedException();
    }

    public async Task<StructuredAgentResult?> RequestPlannerResponseAsync(PlannerRequestContext requestContext, CancellationToken cancellationToken)
    {
        if (requestContext == null)
        {
            throw new ArgumentNullException(nameof(requestContext));
        }

        String? apiKey = assignmentController.OpenAiApiKey;
        String? modelId = assignmentController.SelectedOpenAiModelId;
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        const String usageScopeName = "Planner";
        String scope = requestContext.InvocationKind == PlannerInvocationOptions.AssignmentRoot ? "Planner root" : "Planner subtask";

        String agentIntent = AssignmentController.BuildPlannerAgentIntent(requestContext);
        SmartTask plannerSmartTask = assignmentController.CreateAndAttachAgentSmartTask(agentIntent, SmartTaskTypeOptions.Research, "Planner");
        assignmentController.UpdateSmartTaskState(plannerSmartTask, SmartTaskStateOptions.Executing, "Planner request");
        assignmentLogService.AppendSystemLog("Planner: Starting planner request - " + scope + ".");

        if (!assignmentController.TryReserveOpenAiRequest(scope))
        {
            assignmentLogService.AppendSystemLog("Planner: Skipped request because the assignment request budget was exceeded.");
            assignmentController.UpdateSmartTaskState(plannerSmartTask, SmartTaskStateOptions.Skipped, "Budget exceeded");
            return null;
        }

        try
        {
            String systemInstruction = BuildPlannerSystemInstruction();
            ChatCompletionMessage[] messages = BuildPlannerMessages(systemInstruction, requestContext);

            ChatStreamingResult? streamingResult = await chatCompletionService.SendChatCompletionRequestAsync(
                messages,
                UsageChannelOptions.Planner,
                usageScopeName,
                cancellationToken,
                reserveBudget: false).ConfigureAwait(false);

            if (streamingResult == null)
            {
                assignmentLogService.AppendSystemLog("OpenAI planner streaming response could not be read.");
                assignmentController.UpdateSmartTaskState(plannerSmartTask, SmartTaskStateOptions.Failed, "Planner failed");
                return null;
            }

            String rawContent = streamingResult.RawContent;

            if (string.IsNullOrWhiteSpace(rawContent))
            {
                assignmentLogService.AppendSystemLog("OpenAI planner response did not contain message content.");
                assignmentController.UpdateSmartTaskState(plannerSmartTask, SmartTaskStateOptions.Failed, "Planner failed");
                return null;
            }

            StructuredAgentResult plannerResponse = ParsePlannerResponse(rawContent);
            if (!plannerResponse.IsStructured)
            {
                assignmentLogService.AppendSystemLog("OpenAI planner response could not be parsed as structured JSON. Falling back to raw content.");
            }

            assignmentLogService.AppendSystemLog("Planner: Request completed successfully.");
            assignmentController.UpdateSmartTaskState(plannerSmartTask, SmartTaskStateOptions.Succeeded, "Planner completed");

            return plannerResponse;
        }
        catch (OperationCanceledException)
        {
            assignmentLogService.AppendSystemLog("Planner: Request was cancelled.");
            assignmentController.UpdateSmartTaskState(plannerSmartTask, SmartTaskStateOptions.Skipped, "Cancelled");
            throw;
        }
        catch (Exception exception)
        {
            assignmentController.RecordUsageFailure(usageScopeName, UsageChannelOptions.Planner);
            assignmentLogService.AppendSystemLog("OpenAI planner request failed: " + exception.Message);
            assignmentController.UpdateSmartTaskState(plannerSmartTask, SmartTaskStateOptions.Failed, "Planner failed");
            return null;
        }
    }

    public async Task<StructuredRepairResult?> RequestRepairResponseAsync(SmartTaskExecutionContext failedTask, String? workspacePath, Boolean trackSmartTask, CancellationToken cancellationToken)
    {
        if (failedTask == null)
        {
            return null;
        }

        SmartTask? repairSmartTask = null;
        const String repairUsageScope = "Repair";

        void AuditRepairSmartTask(String message)
        {
            if (repairSmartTask != null)
            {
                assignmentLogService.AppendSystemLog("Repair: " + message);
            }
        }

        void SetRepairSmartTaskState(SmartTaskStateOptions state, String stage)
        {
            if (repairSmartTask != null)
            {
                assignmentController.UpdateSmartTaskState(repairSmartTask, state, stage);
            }
        }

        if (trackSmartTask)
        {
            repairSmartTask = assignmentController.CreateAndAttachAgentSmartTask("Repair: " + failedTask.Label, SmartTaskTypeOptions.Research, "Repair");
            SetRepairSmartTaskState(SmartTaskStateOptions.Executing, "Repair request");
            AuditRepairSmartTask("Starting repair agent workflow for task: " + failedTask.Label + ".");
        }

        const Int32 maxResponseAttempts = 3;
        Boolean includeFormattingReminder = false;
        String systemInstruction = BuildRepairSystemInstruction();

        for (Int32 attempt = 0; attempt < maxResponseAttempts; attempt++)
        {
            try
            {
                ChatCompletionMessage[] messages = BuildRepairMessages(systemInstruction, failedTask, workspacePath, includeFormattingReminder);

                if (!assignmentController.TryReserveOpenAiRequest("Repair agent"))
                {
                    AuditRepairSmartTask("Repair request skipped because the assignment request budget was exceeded.");
                    SetRepairSmartTaskState(SmartTaskStateOptions.Skipped, "Budget exceeded");
                    return null;
                }

                ChatStreamingResult? streamingResult = await chatCompletionService.SendChatCompletionRequestAsync(
                    messages,
                    UsageChannelOptions.Repair,
                    repairUsageScope,
                    cancellationToken,
                    reserveBudget: false).ConfigureAwait(false);

                if (streamingResult == null)
                {
                    assignmentLogService.AppendSystemLog("OpenAI repair request failed: streaming response unavailable.");
                    assignmentLogService.AppendTaskLog(failedTask, "OpenAI repair request failed.");
                    AuditRepairSmartTask("Repair request failed due to missing response.");
                    SetRepairSmartTaskState(SmartTaskStateOptions.Failed, "Repair failed");
                    return null;
                }

                String rawContent = streamingResult.RawContent;

                if (string.IsNullOrWhiteSpace(rawContent))
                {
                    assignmentLogService.AppendSystemLog("OpenAI repair response did not contain message content.");
                    assignmentLogService.AppendTaskLog(failedTask, "OpenAI repair response did not contain message content.");
                    AuditRepairSmartTask("Repair response did not contain message content.");
                    SetRepairSmartTaskState(SmartTaskStateOptions.Failed, "Repair failed");
                    return null;
                }

                assignmentLogService.AppendTaskLog(failedTask, "Repair agent raw response:" + Environment.NewLine + rawContent);

                StructuredRepairResult? repairResponse = TryDeserializeRepairResponse(rawContent);
                if (repairResponse != null)
                {
                    AuditRepairSmartTask("Repair agent returned a structured response.");
                    SetRepairSmartTaskState(SmartTaskStateOptions.Succeeded, "Repair completed");
                    return repairResponse;
                }

                includeFormattingReminder = true;
                assignmentLogService.AppendSystemLog("Repair agent response could not be parsed as JSON. Requesting a corrected response.");
                assignmentLogService.AppendTaskLog(failedTask, "Repair agent response could not be parsed as JSON. Asking for a corrected response.");
                AuditRepairSmartTask("Repair response could not be parsed as JSON; requesting corrected response.");
            }
            catch (OperationCanceledException)
            {
                AuditRepairSmartTask("Repair request was cancelled.");
                SetRepairSmartTaskState(SmartTaskStateOptions.Skipped, "Cancelled");
                throw;
            }
            catch (Exception exception)
            {
                assignmentController.RecordUsageFailure(repairUsageScope, UsageChannelOptions.Repair);
                assignmentLogService.AppendSystemLog("OpenAI repair request failed: " + exception.Message);
                assignmentLogService.AppendTaskLog(failedTask, "OpenAI repair request failed: " + exception.Message);
                AuditRepairSmartTask("Repair request failed: " + exception.Message);
                SetRepairSmartTaskState(SmartTaskStateOptions.Failed, "Repair failed");
                return null;
            }
        }

        assignmentLogService.AppendSystemLog("Repair agent failed to provide valid JSON after multiple attempts. Aborting repair for task '" + failedTask.Label + "'.");
        assignmentLogService.AppendTaskLog(failedTask, "Repair agent failed to provide valid JSON after multiple attempts.");
        AuditRepairSmartTask("Repair agent failed to provide valid JSON after multiple attempts.");
        SetRepairSmartTaskState(SmartTaskStateOptions.Failed, "Repair failed");
        return null;
    }

    public async Task<bool> TryHandleTerminalTaskFailureAsync(
        SmartTaskExecutionContext failedTask,
        String? workspacePath,
        FailureResolutionResult? callbacks,
        CancellationToken cancellationToken)
    {
        StructuredFailureResolutionResult? resolutionResponse = await RequestFailureResolutionResponseAsync(
            failedTask,
            workspacePath,
            callbacks,
            cancellationToken).ConfigureAwait(false);

        if (resolutionResponse == null)
        {
            assignmentLogService.AppendSystemLog("Failure-resolution agent unavailable or returned invalid data for task '" + failedTask.Label + "'. Falling back to recovery workflow.");
            callbacks?.AppendTaskLog?.Invoke(failedTask, "Failure-resolution agent unavailable or response invalid. Defaulting to recovery workflow.");
            return false;
        }

        return ApplyFailureResolutionResponse(failedTask, resolutionResponse, callbacks);
    }

    public Boolean CaptureWorkspaceChangesAsSemanticFacts(SmartTask smartTask, String workspaceFullPath, String triggerDescription)
    {
        if (smartTask == null || workspaceStateTracker == null)
        {
            return false;
        }

        if (String.IsNullOrWhiteSpace(workspaceFullPath))
        {
            return false;
        }

        IReadOnlyList<WorkspaceFileChangeRecord> changes = workspaceStateTracker.DetectChanges(workspaceFullPath);
        if (changes.Count == 0)
        {
            return false;
        }

        Boolean recordedFact = false;
        for (Int32 index = 0; index < changes.Count; index++)
        {
            WorkspaceFileChangeRecord change = changes[index];
            if (String.IsNullOrWhiteSpace(change.RelativePath))
            {
                continue;
            }

            String summary = BuildWorkspaceChangeSummary(change);
            String detail = BuildWorkspaceChangeDetail(change, triggerDescription);
            SemanticFactOptions factKind = MapWorkspaceChangeKind(change.Kind);
            assignmentController.RecordSemanticFact(smartTask, summary, detail, change.RelativePath, factKind);
            recordedFact = true;
        }

        return recordedFact;
    }

    private async Task<StructuredFailureResolutionResult?> RequestFailureResolutionResponseAsync(
        SmartTaskExecutionContext failedTask,
        String? workspacePath,
        FailureResolutionResult? callbacks,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(assignmentController.OpenAiApiKey) || string.IsNullOrWhiteSpace(assignmentController.SelectedOpenAiModelId))
        {
            return null;
        }

        SmartTask resolutionSmartTask = assignmentController.CreateAndAttachAgentSmartTask("Failure resolution: " + failedTask.Label, SmartTaskTypeOptions.Research, "FailureResolution");
        assignmentController.UpdateSmartTaskState(resolutionSmartTask, SmartTaskStateOptions.Executing, "Failure-resolution request");
        assignmentLogService.AppendSystemLog("Failure resolution: Starting workflow for task '" + failedTask.Label + "'.");
        const String failureResolutionUsageScope = "Failure Resolution";

        const Int32 maxResponseAttempts = 3;
        Boolean includeFormattingReminder = false;
        String systemInstruction = BuildFailureResolutionSystemInstruction();

        for (Int32 attempt = 0; attempt < maxResponseAttempts; attempt++)
        {
            try
            {
                ChatCompletionMessage[] messages = BuildFailureResolutionMessages(systemInstruction, failedTask, workspacePath, includeFormattingReminder);

                if (!assignmentController.TryReserveOpenAiRequest("Failure-resolution agent"))
                {
                    assignmentLogService.AppendSystemLog("Failure resolution: Skipped request because the assignment request budget was exceeded.");
                    assignmentController.UpdateSmartTaskState(resolutionSmartTask, SmartTaskStateOptions.Skipped, "Budget exceeded");
                    return null;
                }

                ChatStreamingResult? streamingResult = await chatCompletionService.SendChatCompletionRequestAsync(
                    messages,
                    UsageChannelOptions.FailureResolution,
                    failureResolutionUsageScope,
                    cancellationToken,
                    reserveBudget: false).ConfigureAwait(false);

                if (streamingResult == null)
                {
                    assignmentLogService.AppendSystemLog("OpenAI failure-resolution streaming response could not be read.");
                    callbacks?.AppendTaskLog?.Invoke(failedTask, "OpenAI failure-resolution streaming response could not be read.");
                    assignmentController.UpdateSmartTaskState(resolutionSmartTask, SmartTaskStateOptions.Failed, "Failure-resolution failed");
                    return null;
                }

                String rawContent = streamingResult.RawContent;

                if (string.IsNullOrWhiteSpace(rawContent))
                {
                    assignmentLogService.AppendSystemLog("OpenAI failure-resolution response did not contain message content.");
                    callbacks?.AppendTaskLog?.Invoke(failedTask, "OpenAI failure-resolution response did not contain message content.");
                    assignmentController.UpdateSmartTaskState(resolutionSmartTask, SmartTaskStateOptions.Failed, "Failure-resolution failed");
                    return null;
                }

                callbacks?.AppendTaskLog?.Invoke(failedTask, "Failure-resolution agent raw response:" + Environment.NewLine + rawContent);

                StructuredFailureResolutionResult? resolutionResponse = TryDeserializeFailureResolutionResponse(rawContent);
                if (resolutionResponse != null)
                {
                    assignmentLogService.AppendSystemLog("Failure resolution: Agent returned a structured response.");
                    assignmentController.UpdateSmartTaskState(resolutionSmartTask, SmartTaskStateOptions.Succeeded, "Failure-resolution completed");
                    return resolutionResponse;
                }

                includeFormattingReminder = true;
                assignmentLogService.AppendSystemLog("Failure-resolution agent response could not be parsed as JSON. Requesting a corrected response.");
                callbacks?.AppendTaskLog?.Invoke(failedTask, "Failure-resolution agent response could not be parsed as JSON. Asking for a corrected response.");
            }
            catch (OperationCanceledException)
            {
                assignmentLogService.AppendSystemLog("Failure resolution: Request was cancelled.");
                assignmentController.UpdateSmartTaskState(resolutionSmartTask, SmartTaskStateOptions.Skipped, "Cancelled");
                throw;
            }
            catch (Exception exception)
            {
                assignmentController.RecordUsageFailure(failureResolutionUsageScope, UsageChannelOptions.FailureResolution);
                assignmentLogService.AppendSystemLog("OpenAI failure-resolution request failed: " + exception.Message);
                callbacks?.AppendTaskLog?.Invoke(failedTask, "OpenAI failure-resolution request failed: " + exception.Message);
                assignmentController.UpdateSmartTaskState(resolutionSmartTask, SmartTaskStateOptions.Failed, "Failure-resolution failed");
                return null;
            }
        }

        assignmentLogService.AppendSystemLog("Failure-resolution agent failed to provide valid JSON after multiple attempts. Escalating failure for task '" + failedTask.Label + "'.");
        callbacks?.AppendTaskLog?.Invoke(failedTask, "Failure-resolution agent failed to provide valid JSON after multiple attempts.");
        assignmentController.UpdateSmartTaskState(resolutionSmartTask, SmartTaskStateOptions.Failed, "Failure-resolution failed");
        return null;
    }

    private Boolean ApplyFailureResolutionResponse(
        SmartTaskExecutionContext failedTask,
        StructuredFailureResolutionResult resolutionResponse,
        FailureResolutionResult? callbacks)
    {
        String decision = resolutionResponse.ResolutionDecision ?? string.Empty;
        String decisionNormalized = decision.Trim().ToLowerInvariant();

        Boolean continueAssignment = false;
        Boolean shouldAllowDependents = resolutionResponse.AllowsDependentsToProceed ?? false;

        if (decisionNormalized == "allow_dependents_to_proceed" ||
            decisionNormalized == "continue_with_dependents" ||
            decisionNormalized == "continue_without_task")
        {
            continueAssignment = true;
            shouldAllowDependents = true;
        }
        else if (decisionNormalized == "add_alternative_tasks")
        {
            continueAssignment = true;
            shouldAllowDependents = true;
        }

        if (resolutionResponse.NewTasks != null && resolutionResponse.NewTasks.Count > 0)
        {
            if (callbacks?.InsertTasksAfter != null)
            {
                callbacks.InsertTasksAfter(failedTask, resolutionResponse.NewTasks);
                callbacks.AppendTaskLog?.Invoke(failedTask, "Inserted " + resolutionResponse.NewTasks.Count + " compensating task(s) suggested by the failure-resolution agent.");
                continueAssignment = true;
                shouldAllowDependents = true;
            }
            else
            {
                assignmentLogService.AppendSystemLog("Failure-resolution agent provided new tasks, but no insertion callback was supplied.");
            }
        }

        if (shouldAllowDependents)
        {
            assignmentController.AllowDependentsToProceed(
                failedTask,
                "Dependents unlocked per failure-resolution decision '" + (decision.Length == 0 ? "unspecified" : decision) + "'.",
                callbacks?.AppendTaskLog);
        }

        String reasonText = resolutionResponse.Reason ?? string.Empty;
        String notesText = resolutionResponse.Notes ?? string.Empty;
        String decisionForLog = decision.Length == 0 ? "unspecified" : decision;

        failedTask.RecordRepairHistoryEntry("Failure-resolution decision: " + decisionForLog + ". Reason: " + (reasonText.Length == 0 ? "(none)" : reasonText));

        if (continueAssignment)
        {
            assignmentLogService.AppendSystemLog("Continuing assignment after failure of '" + failedTask.Label + "'. Decision: " + decisionForLog + ". " + reasonText);
            callbacks?.AppendTaskLog?.Invoke(failedTask, "Continuing after failure. Decision: " + decisionForLog + ". Reason: " + reasonText);
            if (!string.IsNullOrWhiteSpace(notesText))
            {
                callbacks?.AppendTaskLog?.Invoke(failedTask, "Notes: " + notesText.Trim());
            }

            return true;
        }

        assignmentLogService.AppendSystemLog("Failure-resolution agent escalated failure of task '" + failedTask.Label + "'. Reason: " + reasonText);
        callbacks?.AppendTaskLog?.Invoke(failedTask, "Failure-resolution agent escalated this task. Reason: " + reasonText);
        if (!string.IsNullOrWhiteSpace(notesText))
        {
            callbacks?.AppendTaskLog?.Invoke(failedTask, "Notes: " + notesText.Trim());
        }

        return false;
    }

    private String BuildFailureResolutionSystemInstruction()
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("You are a failure-resolution coordinator for an autonomous agent-driven coding environment. ");
        builder.Append("You are invoked after a task exhausted all automatic repair attempts and still failed. ");
        builder.Append("Decide if work can safely continue, whether compensating tasks should be added, or whether the assignment must halt. ");
        builder.Append("Respond with a single JSON object (no prose, no code fences) that matches this schema: ");
        builder.Append("{");
        builder.Append("\"resolutionDecision\": \"allow_dependents_to_proceed\" | \"add_alternative_tasks\" | \"escalate_assignment\", ");
        builder.Append("\"reason\": string, ");
        builder.Append("\"allowsDependentsToProceed\": bool, ");
        builder.Append("\"notes\": string, ");
        builder.Append("\"newTasks\": [");
        builder.Append("{");
        builder.Append("\"id\": string, ");
        builder.Append("\"label\": string, ");
        builder.Append("\"type\": string, ");
        builder.Append("\"description\": string, ");
        builder.Append("\"context\": string, ");
        builder.Append("\"priority\": string, ");
        builder.Append("\"phase\": String or null, ");
        builder.Append("\"contextTags\": string[], ");
        builder.Append("\"dependencies\": string[], ");
        builder.Append("\"commands\": [");
        builder.Append("{");
        builder.Append("\"id\": string, ");
        builder.Append("\"description\": string, ");
        builder.Append("\"executable\": string, ");
        builder.Append("\"arguments\": string, ");
        builder.Append("\"workingDirectory\": String or null, ");
        builder.Append("\"dangerLevel\": \"safe\" | \"dangerous\" | \"critical\", ");
        builder.Append("\"expectedExitCode\": int, ");
        builder.Append("\"runInBackground\": Boolean or null, ");
        builder.Append("\"maxRunSeconds\": Int32 or null");
        builder.Append("}");
        builder.Append("]");
        builder.Append("}");
        builder.Append("]");
        builder.Append("}. ");
        builder.Append("Set \"allow_dependents_to_proceed\" when downstream tasks should continue despite this failure (the runtime will unlock dependencies automatically). ");
        builder.Append("Return \"add_alternative_tasks\" only when you populate newTasks with concrete steps that compensate for the failure. ");
        builder.Append("Use \"escalate_assignment\" exclusively when the assignment must stop because no safe automation path remains. ");
        builder.Append("Keep notes short and actionable. ");
        builder.Append("Always respond with the raw JSON object and nothing else.");
        return builder.ToString();
    }

    private ChatCompletionMessage[] BuildFailureResolutionMessages(
        String systemInstruction,
        SmartTaskExecutionContext failedTask,
        String? workspacePath,
        Boolean includeFormattingReminder)
    {
        List<ChatCompletionMessage> messages = new List<ChatCompletionMessage>();

        ChatCompletionMessage systemMessage = new ChatCompletionMessage
        {
            Role = "system",
            Content = systemInstruction
        };

        messages.Add(systemMessage);

        ChatCompletionMessage userMessage = new ChatCompletionMessage
        {
            Role = "user"
        };

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("A task has permanently failed after exhausting local repair attempts.");
        builder.AppendLine();
        builder.AppendLine("Task label: " + failedTask.Label);
        builder.AppendLine("Task type: " + failedTask.Type);
        builder.AppendLine("Task id: " + failedTask.AgentTaskId);
        builder.AppendLine("Current status: " + failedTask.Status);
        builder.AppendLine("Dependents already allowed to proceed: " + failedTask.AllowsDependentsToProceed);
        builder.AppendLine("Total repair attempts: " + failedTask.AttemptCount);
        builder.AppendLine();
        builder.AppendLine("Workspace path:");
        builder.AppendLine(workspacePath ?? "(none)");
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(failedTask.AggregatedContextSnapshot))
        {
            builder.AppendLine("Aggregated task context:");
            builder.AppendLine(failedTask.AggregatedContextSnapshot);
            builder.AppendLine();
        }

        if (failedTask.RepairAttemptHistory.Count > 0)
        {
            builder.AppendLine("Summary of prior repair attempts:");
            for (Int32 index = 0; index < failedTask.RepairAttemptHistory.Count; index++)
            {
                builder.AppendLine("- " + failedTask.RepairAttemptHistory[index]);
            }
            builder.AppendLine();
        }

        if (failedTask.ProducedContextEntries.Count > 0)
        {
            builder.AppendLine("Outputs captured by this task:");
            for (Int32 index = 0; index < failedTask.ProducedContextEntries.Count; index++)
            {
                builder.AppendLine(failedTask.ProducedContextEntries[index]);
            }
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(failedTask.AgentTaskId))
        {
            List<string> dependentSummaries = new List<string>();

            for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
            {
                SmartTaskExecutionContext candidate = assignmentTaskItems[index];
                if (candidate == failedTask || candidate.Dependencies == null || candidate.Dependencies.Count == 0)
                {
                    continue;
                }

                Boolean dependsOnFailedTask = false;
                for (Int32 depIndex = 0; depIndex < candidate.Dependencies.Count; depIndex++)
                {
                    String dependencyId = candidate.Dependencies[depIndex];
                    if (string.Equals(dependencyId, failedTask.AgentTaskId, StringComparison.OrdinalIgnoreCase))
                    {
                        dependsOnFailedTask = true;
                        break;
                    }
                }

                if (!dependsOnFailedTask)
                {
                    continue;
                }

                String dependentId = !string.IsNullOrWhiteSpace(candidate.AgentTaskId) ? candidate.AgentTaskId! : candidate.TaskNumber.ToString(CultureInfo.InvariantCulture);
                String dependentStatus = candidate.Status;
                dependentSummaries.Add(dependentId + " - " + candidate.Label + " (" + dependentStatus + ")");
            }

            if (dependentSummaries.Count > 0)
            {
                builder.AppendLine("Dependent tasks affected by this failure:");
                for (Int32 index = 0; index < dependentSummaries.Count; index++)
                {
                    builder.AppendLine("- " + dependentSummaries[index]);
                }
                builder.AppendLine();
            }
        }

        if (!string.IsNullOrWhiteSpace(failedTask.LastResultText))
        {
            builder.AppendLine("Last combined stdout/stderr: ");
            builder.AppendLine(TextUtilityService.BuildCompactSnippet(failedTask.LastResultText));
            builder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(failedTask.TaskLogText))
        {
            builder.AppendLine("Task log excerpt:");
            builder.AppendLine(TextUtilityService.BuildCompactSnippet(failedTask.TaskLogText, 2000));
            builder.AppendLine();
        }

        assignmentController.AppendSemanticFactsSection(builder, "Semantic blackboard facts shared by the run:", maxFacts: 0, includeReminder: true);

        builder.AppendLine("Respond ONLY with the JSON object described in the system instructions.");

        userMessage.Content = builder.ToString();
        messages.Add(userMessage);

        if (includeFormattingReminder)
        {
            ChatCompletionMessage reminderMessage = new ChatCompletionMessage
            {
                Role = "user",
                Content = "Reminder: respond ONLY with the JSON object that matches the required schema. Do not include commentary, markdown fences, or unescaped literal newlines inside JSON String values."
            };

            messages.Add(reminderMessage);
        }

        return messages.ToArray();
    }

    private StructuredFailureResolutionResult? TryDeserializeFailureResolutionResponse(String rawContent)
    {
        StructuredFailureResolutionResult? parsed = TryDeserializeJson<StructuredFailureResolutionResult>(rawContent);
        if (parsed != null)
        {
            return parsed;
        }

        String? extractedJson = ExtractJsonObject(rawContent);
        if (!string.IsNullOrWhiteSpace(extractedJson))
        {
            parsed = TryDeserializeJson<StructuredFailureResolutionResult>(extractedJson);
            if (parsed != null)
            {
                return parsed;
            }

            String sanitizedExtracted = EscapeInvalidJsonCharactersInsideStrings(extractedJson);
            if (!string.Equals(sanitizedExtracted, extractedJson, StringComparison.Ordinal))
            {
                parsed = TryDeserializeJson<StructuredFailureResolutionResult>(sanitizedExtracted);
                if (parsed != null)
                {
                    return parsed;
                }
            }
        }

        String sanitizedRaw = EscapeInvalidJsonCharactersInsideStrings(rawContent);
        if (!string.Equals(sanitizedRaw, rawContent, StringComparison.Ordinal))
        {
            parsed = TryDeserializeJson<StructuredFailureResolutionResult>(sanitizedRaw);
            if (parsed != null)
            {
                return parsed;
            }
        }

        return null;
    }

    private String BuildPlannerSystemInstruction()
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("You are a planner and architect agent inside an agent-driven coding environment that must operate fully autonomously. ");
        builder.Append("You receive a single high-level assignment and must: ");
        builder.Append("1) provide a clear answer/summary for the user, and ");
        builder.Append("2) produce a structured task graph with concrete commands that the environment can execute without any human interaction. ");
        builder.Append("You may be called multiple times: the first call covers the overall assignment, and subsequent calls focus on expanding a specific parent task. ");
        builder.Append("When expanding a parent task, only plan subtasks relevant to that parent and ensure every returned subtask lists the parent task id in its dependencies. ");
        builder.Append("Each planner invocation (root or recursive) must begin with one or more research/context-gathering subtasks before proposing any implementation work. ");
        builder.Append("Design tasks so they can loop through research and execution phases multiple times—downstream tasks should feel free to call OpenAI again whenever new information is required. ");
        builder.Append("For concrete workspace modifications, always provide at least one task and one command. ");
        builder.Append("Decompose work into deterministic tasks that can be completed independently, and create explicit research or verification tasks whose command output should be used as context for subsequent tasks via dependencies. ");
        builder.Append("Respect the allow_decomposition flag provided in the user message: when it is false you MUST NOT create manager/decomposition subtasks and instead choose research or direct execution tasks, or decide the goal can be skipped if already satisfied. ");
        builder.Append("Always include the relevant context needed to execute each task (for example, files to inspect, command outputs to consume, or research summaries). ");
        builder.Append("When subtasks are necessary, include them via the \"subtasks\" array or by adding dependent tasks, ensuring dependencies reference the parent task ids. ");
        builder.Append("Assign a realistic priority to each task (Critical/High/Medium/Low) so scheduling can be deterministic, and populate optional phase/contextTags fields whenever they aid observability. ");
        builder.Append("Commands must never expect manual confirmation; assume the system will auto-confirm everything.");
        return builder.ToString();
    }

    private ChatCompletionMessage[] BuildPlannerMessages(String systemInstruction, PlannerRequestContext requestContext)
    {
        List<ChatCompletionMessage> messages = new List<ChatCompletionMessage>();

        ChatCompletionMessage systemMessage = new ChatCompletionMessage
        {
            Role = "system",
            Content = systemInstruction
        };

        messages.Add(systemMessage);

        ChatCompletionMessage userMessage = new ChatCompletionMessage
        {
            Role = "user",
            Content = BuildPlannerUserContent(requestContext)
        };

        messages.Add(userMessage);
        return messages.ToArray();
    }

    private String BuildPlannerUserContent(PlannerRequestContext requestContext)
    {
        StringBuilder builder = new StringBuilder();

        if (requestContext.InvocationKind == PlannerInvocationOptions.AssignmentRoot)
        {
            if (!string.IsNullOrWhiteSpace(requestContext.AssignmentTitle))
            {
                builder.AppendLine("Assignment title: " + requestContext.AssignmentTitle!.Trim());
                builder.AppendLine();
            }

            builder.AppendLine("Assignment prompt:");
            builder.AppendLine(requestContext.AssignmentPrompt);
        }
        else
        {
            AgentPlannedTask parentTask = requestContext.ParentTask!;
            builder.AppendLine("Expand the following parent task into concrete executable subtasks.");
            builder.AppendLine();
            builder.AppendLine("Parent task id: " + (parentTask.Id ?? "(missing)"));
            builder.AppendLine("Parent task label: " + (parentTask.Label ?? "(missing)"));
            builder.AppendLine("Parent task type: " + (parentTask.Type ?? "(unspecified)"));
            builder.AppendLine("Parent priority: " + (parentTask.Priority ?? "(unspecified)"));
            builder.AppendLine("Parent description: " + (parentTask.Description ?? "(none)"));
            builder.AppendLine("Parent context: " + (parentTask.Context ?? "(none)"));

            if (parentTask.Dependencies != null && parentTask.Dependencies.Count > 0)
            {
                builder.AppendLine("Parent dependencies: " + string.Join(", ", parentTask.Dependencies));
            }

            Int32 commandCount = parentTask.Commands != null ? parentTask.Commands.Count : 0;
            builder.AppendLine("Parent currently defines " + commandCount + " command(s). Only add subtasks if more work is required.");
            builder.AppendLine("Each returned subtask must list the parent id '" + (parentTask.Id ?? "(missing)") + "' in its dependencies, and should include precise context and commands.");
            builder.AppendLine("Return an empty task list if this parent is already fully specified.");
            builder.AppendLine();
            builder.AppendLine("Original assignment prompt:");
            builder.AppendLine(requestContext.AssignmentPrompt);
        }

        builder.AppendLine();
        double executionBias = requestContext.WorkRetentionFraction;
        Int32 executionBiasPercent = (int)Math.Round(executionBias * 100.0, MidpointRounding.AwayFromZero);
        builder.AppendLine("Convergence state:");
        builder.AppendLine("  Depth: " + requestContext.Depth.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("  Execution bias: " + executionBiasPercent.ToString(CultureInfo.InvariantCulture) + "%");
        builder.AppendLine("  allow_decomposition: " + (requestContext.AllowDecomposition ? "true" : "false"));
        if (!requestContext.AllowDecomposition)
        {
            builder.AppendLine("You MUST NOT propose decomposition/manager subtasks. Prefer research or direct execution subtasks, or decide that the goal can be skipped if already satisfied.");
        }

        AppendWorkBudgetGuidance(builder, requestContext);

        if (!string.IsNullOrWhiteSpace(requestContext.WorkspaceContext))
        {
            builder.AppendLine();
            builder.AppendLine("Workspace context summary:");
            builder.AppendLine(requestContext.WorkspaceContext);
        }

        if (requestContext.AncestorSummaries.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Ancestor chain:");
            for (Int32 index = 0; index < requestContext.AncestorSummaries.Count; index++)
            {
                builder.AppendLine("- " + requestContext.AncestorSummaries[index]);
            }
        }

        assignmentController.AppendSemanticFactsSection(builder, "Semantic blackboard facts available to you:", maxFacts: 0, includeReminder: true);

        builder.AppendLine();
        builder.AppendLine("Respond ONLY with the JSON object that matches the required schema; do not include prose or code fences.");

        return builder.ToString();
    }

    private void AppendWorkBudgetGuidance(StringBuilder builder, PlannerRequestContext requestContext)
    {
        builder.AppendLine();
        builder.AppendLine("Workload budget for this planner call:");
        builder.AppendLine("- You must personally complete at least " + FormatPercent(requestContext.WorkRetentionFraction) + " of this scope.");

        if (!WorkBudgetSettings.HasMeaningfulDelegation(requestContext.DelegationFraction))
        {
            if (requestContext.DelegationFraction <= 0.0)
            {
                builder.AppendLine("- Delegation budget remaining: 0%. Do NOT create any subtasks; finalize this scope yourself with concrete commands.");
            }
            else
            {
                builder.AppendLine("- Delegation budget configured as " + FormatPercent(requestContext.DelegationFraction) + ", but this is below the minimum " + FormatPercent(WorkBudgetSettings.MinimumDelegationBudgetFraction) + " threshold, so you must execute it yourself.");
            }
        }
        else
        {
            builder.AppendLine("- You must delegate approximately " + FormatPercent(requestContext.DelegationFraction) + " via newly created subtasks. Keep them narrowly scoped.");
            builder.AppendLine("- Any subtasks you introduce must each be executable end-to-end and reference this parent task id.");
        }

        builder.AppendLine("- Treat the percentages as workload budget: whatever you delegate, you must still cover the retained portion in this response.");
    }

    private static String FormatPercent(double fraction)
    {
        double percent = fraction * 100.0;
        return percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private StructuredAgentResult ParsePlannerResponse(String rawContent)
    {
        StructuredAgentResult? parsedResponse = TryDeserializePlannerResponse(rawContent);
        if (parsedResponse != null)
        {
            if (parsedResponse.Tasks == null)
            {
                parsedResponse.Tasks = new List<AgentPlannedTask>();
            }

            parsedResponse.IsStructured = true;
            parsedResponse.RawContent = rawContent;
            return parsedResponse;
        }

        String? extractedJson = ExtractJsonObject(rawContent);
        if (!string.IsNullOrWhiteSpace(extractedJson))
        {
            StructuredAgentResult? extractedParsedResponse = TryDeserializePlannerResponse(extractedJson);
            if (extractedParsedResponse != null)
            {
                if (extractedParsedResponse.Tasks == null)
                {
                    extractedParsedResponse.Tasks = new List<AgentPlannedTask>();
                }

                extractedParsedResponse.IsStructured = true;
                extractedParsedResponse.RawContent = rawContent;
                return extractedParsedResponse;
            }
        }

        StructuredAgentResult fallbackResponse = new StructuredAgentResult
        {
            Answer = rawContent,
            Explanation = null,
            Tasks = new List<AgentPlannedTask>(),
            IsStructured = false,
            RawContent = rawContent
        };

        return fallbackResponse;
    }

    private StructuredAgentResult? TryDeserializePlannerResponse(String jsonText)
    {
        return TryDeserializeJson<StructuredAgentResult>(jsonText);
    }

    private String BuildRepairSystemInstruction()
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("You are a repair and self-healing agent inside an agent-driven coding environment that runs without user interaction. ");
        builder.Append("You are called when a task's command failed. ");
        builder.Append("You must decide whether to retry the same task with new commands, to introduce additional tasks (including research subtasks) that should run before retrying, or to give up. ");
        builder.Append("Prefer small, precise fixes over large, complex changes. ");
        builder.Append("If the failure is due to an environment issue like using a shell built-in as an executable, fix the command accordingly, for example by using 'cmd.exe /C ...' on Windows. ");
        builder.Append("Your output must be a single JSON object and nothing else (no Markdown, no prose around it), with no surrounding code fences. ");
        builder.Append("The JSON object must have the following shape: ");
        builder.Append("{");
        builder.Append("\"repairDecision\": \"retry_with_new_commands\" | \"add_new_tasks\" | \"give_up\", ");
        builder.Append("\"reason\": string, ");
        builder.Append("\"replacementCommands\": [");
        builder.Append("{");
        builder.Append("\"id\": string, ");
        builder.Append("\"description\": string, ");
        builder.Append("\"executable\": string, ");
        builder.Append("\"arguments\": string, ");
        builder.Append("\"workingDirectory\": String or null, ");
        builder.Append("\"dangerLevel\": \"safe\" | \"dangerous\" | \"critical\", ");
        builder.Append("\"expectedExitCode\": int, ");
        builder.Append("\"runInBackground\": Boolean or null, ");
        builder.Append("\"maxRunSeconds\": Int32 or null");
        builder.Append("}");
        builder.Append("] , ");
        builder.Append("\"newTasks\": [TaskObject...]");
        builder.Append("}. ");
        builder.Append("Set \"runInBackground\" to true only for long-running services so subsequent tasks may proceed. ");
        builder.Append("Use \"maxRunSeconds\" to adjust command timeouts when necessary. ");
        builder.Append("Use \"retry_with_new_commands\" when small changes suffice, \"add_new_tasks\" when compensating work is required, and \"give_up\" only when no automated repair is possible.");
        return builder.ToString();
    }

    private ChatCompletionMessage[] BuildRepairMessages(String systemInstruction, SmartTaskExecutionContext failedTask, String? workspacePath, Boolean includeFormattingReminder)
    {
        List<ChatCompletionMessage> messages = new List<ChatCompletionMessage>();

        ChatCompletionMessage systemMessage = new ChatCompletionMessage
        {
            Role = "system",
            Content = systemInstruction
        };

        messages.Add(systemMessage);

        ChatCompletionMessage userMessage = new ChatCompletionMessage
        {
            Role = "user"
        };

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("A task has failed in the agent-driven coding environment.");
        builder.AppendLine();
        builder.AppendLine("Task label: " + failedTask.Label);
        builder.AppendLine("Task type: " + failedTask.Type);
        builder.AppendLine("Task id: " + failedTask.AgentTaskId);
        builder.AppendLine("Attempt count so far: " + failedTask.AttemptCount);
        builder.AppendLine();
        builder.AppendLine("Workspace path:");
        builder.AppendLine(string.IsNullOrWhiteSpace(workspacePath) ? "(none)" : workspacePath!);
        builder.AppendLine();

        if (!string.IsNullOrWhiteSpace(failedTask.AggregatedContextSnapshot))
        {
            builder.AppendLine("Aggregated task context:");
            builder.AppendLine(failedTask.AggregatedContextSnapshot);
            builder.AppendLine();
        }

        if (failedTask.ProducedContextEntries.Count > 0)
        {
            builder.AppendLine("Recent outputs from this task:");
            for (Int32 index = 0; index < failedTask.ProducedContextEntries.Count; index++)
            {
                builder.AppendLine(failedTask.ProducedContextEntries[index]);
            }
            builder.AppendLine();
        }

        if (failedTask.RepairAttemptHistory.Count > 0)
        {
            builder.AppendLine("Previous repair attempts:");
            for (Int32 index = 0; index < failedTask.RepairAttemptHistory.Count; index++)
            {
                builder.AppendLine("- " + failedTask.RepairAttemptHistory[index]);
            }
            builder.AppendLine();
        }

        assignmentController.AppendSemanticFactsSection(builder, "Semantic blackboard facts you must honor:", maxFacts: 0, includeReminder: true);

        if (failedTask.AssociatedCommands != null && failedTask.AssociatedCommands.Count > 0)
        {
            AgentCommandDescription lastCommand = failedTask.AssociatedCommands[failedTask.AssociatedCommands.Count - 1];
            builder.AppendLine("Last command specification:");
            builder.AppendLine("Executable: " + (lastCommand.Executable ?? string.Empty));
            builder.AppendLine("Arguments: " + (lastCommand.Arguments ?? string.Empty));
            builder.AppendLine("WorkingDirectory: " + (lastCommand.WorkingDirectory ?? "(workspace)"));
            builder.AppendLine("DangerLevel: " + (lastCommand.DangerLevel ?? "safe"));
            if (lastCommand.ExpectedExitCode.HasValue)
            {
                builder.AppendLine("ExpectedExitCode: " + lastCommand.ExpectedExitCode.Value);
            }
        }

        builder.AppendLine();
        builder.AppendLine("Last known result text from task (stdout+stderr combined):");
        builder.AppendLine(failedTask.LastResultText ?? "(none)");

        userMessage.Content = builder.ToString();
        messages.Add(userMessage);

        if (includeFormattingReminder)
        {
            ChatCompletionMessage reminderMessage = new ChatCompletionMessage
            {
                Role = "user",
                Content = "Reminder: respond ONLY with the JSON object that matches the required schema. Do not include commentary, markdown fences, or unescaped literal newlines inside JSON String values."
            };
            messages.Add(reminderMessage);
        }

        return messages.ToArray();
    }

    private StructuredRepairResult? TryDeserializeRepairResponse(String rawContent)
    {
        StructuredRepairResult? parsed = TryDeserializeJson<StructuredRepairResult>(rawContent);
        if (parsed != null)
        {
            return parsed;
        }

        String? extractedJson = ExtractJsonObject(rawContent);
        if (!string.IsNullOrWhiteSpace(extractedJson))
        {
            parsed = TryDeserializeJson<StructuredRepairResult>(extractedJson);
            if (parsed != null)
            {
                return parsed;
            }

            String sanitizedExtracted = EscapeInvalidJsonCharactersInsideStrings(extractedJson);
            if (!string.Equals(sanitizedExtracted, extractedJson, StringComparison.Ordinal))
            {
                parsed = TryDeserializeJson<StructuredRepairResult>(sanitizedExtracted);
                if (parsed != null)
                {
                    return parsed;
                }
            }
        }

        String sanitizedRaw = EscapeInvalidJsonCharactersInsideStrings(rawContent);
        if (!string.Equals(sanitizedRaw, rawContent, StringComparison.Ordinal))
        {
            parsed = TryDeserializeJson<StructuredRepairResult>(sanitizedRaw);
            if (parsed != null)
            {
                return parsed;
            }
        }

        return null;
    }

    private T? TryDeserializeJson<T>(String jsonText) where T : class
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(jsonText, jsonSerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static String EscapeInvalidJsonCharactersInsideStrings(String text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        StringBuilder builder = new StringBuilder(text.Length);
        Boolean insideString = false;
        Boolean previousWasEscape = false;

        for (Int32 index = 0; index < text.Length; index++)
        {
            char currentChar = text[index];

            if (currentChar == '"' && !previousWasEscape)
            {
                insideString = !insideString;
                builder.Append(currentChar);
                continue;
            }

            if (insideString && (currentChar == '\n' || currentChar == '\r'))
            {
                builder.Append(currentChar == '\r' ? "\\r" : "\\n");
            }
            else
            {
                builder.Append(currentChar);
            }

            if (currentChar == '\\' && !previousWasEscape)
            {
                previousWasEscape = true;
            }
            else
            {
                previousWasEscape = false;
            }
        }

        return builder.ToString();
    }

    private static String? ExtractJsonObject(String text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        Int32 firstIndex = text.IndexOf('{');
        Int32 lastIndex = text.LastIndexOf('}');
        if (firstIndex >= 0 && lastIndex > firstIndex)
        {
            Int32 length = lastIndex - firstIndex + 1;
            return text.Substring(firstIndex, length);
        }

        return null;
    }

    private static String BuildWorkspaceChangeSummary(WorkspaceFileChangeRecord change)
    {
        String prefix = change.Kind switch
        {
            WorkspaceFileChangeOptions.Created => "File created: ",
            WorkspaceFileChangeOptions.Modified => "File updated: ",
            WorkspaceFileChangeOptions.Deleted => "File deleted: ",
            _ => "Workspace change: "
        };

        if (String.IsNullOrWhiteSpace(change.RelativePath))
        {
            return prefix.TrimEnd();
        }

        return prefix + change.RelativePath;
    }

    private static SemanticFactOptions MapWorkspaceChangeKind(WorkspaceFileChangeOptions changeKind)
    {
        return changeKind switch
        {
            WorkspaceFileChangeOptions.Created => SemanticFactOptions.FileCreated,
            WorkspaceFileChangeOptions.Modified => SemanticFactOptions.FileUpdated,
            WorkspaceFileChangeOptions.Deleted => SemanticFactOptions.FileDeleted,
            _ => SemanticFactOptions.General
        };
    }

    private static String BuildWorkspaceChangeDetail(WorkspaceFileChangeRecord change, String triggerDescription)
    {
        String action = change.Kind switch
        {
            WorkspaceFileChangeOptions.Created => "Created",
            WorkspaceFileChangeOptions.Modified => "Modified",
            WorkspaceFileChangeOptions.Deleted => "Removed",
            _ => "Changed"
        };

        StringBuilder builder = new StringBuilder();
        builder.Append(action);
        if (!String.IsNullOrWhiteSpace(triggerDescription))
        {
            builder.Append(" via ");
            builder.Append(TextUtilityService.BuildCompactSnippet(triggerDescription, 160));
        }

        FileSignature signature = change.Kind == WorkspaceFileChangeOptions.Deleted ? change.Previous : change.Current;
        if (signature.Size >= 0)
        {
            builder.Append(" \u00B7 Size ");
            builder.Append(signature.Size.ToString("N0", CultureInfo.InvariantCulture));
            builder.Append(" bytes");
        }

        if (signature.LastWriteUtc != DateTime.MinValue)
        {
            builder.Append(" \u00B7 Timestamp ");
            builder.Append(signature.LastWriteUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}



