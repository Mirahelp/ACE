using AgentCommandEnvironment.Core.Constants;
using AgentCommandEnvironment.Core.Events;
using AgentCommandEnvironment.Core.Interfaces;
using AgentCommandEnvironment.Core.Models;
using AgentCommandEnvironment.Core.Results;
using AgentCommandEnvironment.Core.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Core.Controllers;

public sealed class AssignmentController : IDisposable
{
    private readonly HttpClient httpClient;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly ObservableCollection<SmartTaskExecutionContext> assignmentTaskItems;
    private readonly ObservableCollection<WorkspaceFileItem> workspaceFileItems;
    private readonly ObservableCollection<SmartTask> aceRootTasks;
    private readonly ObservableCollection<SemanticFactRecord> aceFacts;
    private readonly ObservableCollection<PolicyDecisionItem> policyDecisionItems;
    private readonly List<String> openAiAvailableModelIds;
    private readonly HashSet<String> completedTaskIntents;
    private readonly HashSet<String> agentTaskIds;
    private readonly HashSet<SmartTask> smartTasksWithRecordedOutcome;
    private readonly Dictionary<String, SmartTask> smartTaskNodesByAgentTaskId;
    private readonly ObservableCollection<SuccessHeuristicItem> assignmentSuccessHeuristics;
    private readonly WorkspaceStateTrackerService workspaceStateTracker;
    private readonly List<BackgroundCommandHandleService> backgroundCommandHandles;
    private readonly Object backgroundCommandHandlesLock = new Object();
    private readonly List<String> systemLogEntries;
    private readonly Object systemLogLock = new Object();
    private readonly Object usageLock = new Object();
    private readonly Object budgetLock = new Object();
    private readonly Object agentTaskIdLock = new Object();
    private readonly Object creationOrderLock = new Object();
    private readonly Object completedIntentsLock = new Object();
    private readonly GlobalContext globalContext;
    private readonly IUiDispatcherService dispatcherService;
    private SmartTaskSchedulerService? activeSmartTaskScheduler;
    private const Int32 MaxSystemLogEntries = 2000;
    private const Int32 AssignmentRequestBudget = 80;
    private const Int32 SmartTaskExecutionBudget = 60;

    private SmartTask? assignmentRootSmartTask;
    private CancellationTokenSource? assignmentCancellationTokenSource;

    private String? openAiApiKey;
    private Boolean isOpenAiConfigured;
    private Boolean isLoadingModels;
    private Boolean isAssignmentRunning;
    private Boolean isAssignmentPaused;
    private String? openAiSelectedModelId;
    private String assignmentStatusText = "Idle";

    private Int32 usageTotalRequests;
    private Int32 usagePlannerRequests;
    private Int32 usageRepairRequests;
    private Int32 usageFailureResolutionRequests;
    private Int32 usageRequestSucceededCount;
    private Int32 usageRequestFailedCount;
    private Int64 usagePromptTokens;
    private Int64 usageCompletionTokens;
    private Int64 usagePlannerTokens;
    private Int64 usageRepairTokens;
    private Int64 usageFailureResolutionTokens;
    private Int32 usageTasksSucceededCount;
    private Int32 usageTasksFailedCount;
    private Int32 usageTasksSkippedCount;
    private DateTime usageLastUpdatedUtc;

    private Double recursionExitBiasBase;
    private Double recursionExitBiasIncrement;
    private Int32 assignmentOpenAiRequestCount;
    private Int32 smartTaskExecutionCount;
    private Int32 nextTaskCreationOrder;
    private String assignmentAnswerOutputText = String.Empty;
    private String assignmentRawOutputText = String.Empty;
    private String commandsSummaryOutputText = String.Empty;
    private String assignmentResultHeadline = String.Empty;
    private String assignmentResultDetail = String.Empty;
    private String? assignmentFailureReason;

    public event EventHandler<SystemLogEventArgs>? SystemLogEntryAdded;
    public event EventHandler? StateChanged;
    public event EventHandler? UsageChanged;

    public AssignmentController(
        IUiDispatcherService dispatcherService,
        HttpClient httpClient,
        JsonSerializerOptions jsonSerializerOptions,
        WorkspaceStateTrackerService workspaceStateTracker,
        GlobalContext globalContext,
        SmartTaskSchedulerService smartTaskScheduler)
    {
        this.dispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.jsonSerializerOptions = jsonSerializerOptions ?? throw new ArgumentNullException(nameof(jsonSerializerOptions));
        this.workspaceStateTracker = workspaceStateTracker ?? throw new ArgumentNullException(nameof(workspaceStateTracker));
        this.globalContext = globalContext ?? throw new ArgumentNullException(nameof(globalContext));
        activeSmartTaskScheduler = smartTaskScheduler ?? throw new ArgumentNullException(nameof(smartTaskScheduler));

        assignmentTaskItems = new ObservableCollection<SmartTaskExecutionContext>();
        workspaceFileItems = new ObservableCollection<WorkspaceFileItem>();
        aceRootTasks = new ObservableCollection<SmartTask>();
        aceFacts = new ObservableCollection<SemanticFactRecord>();
        policyDecisionItems = new ObservableCollection<PolicyDecisionItem>();
        openAiAvailableModelIds = new List<String>();
        completedTaskIntents = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        agentTaskIds = new HashSet<String>(StringComparer.OrdinalIgnoreCase);
        smartTasksWithRecordedOutcome = new HashSet<SmartTask>();
        smartTaskNodesByAgentTaskId = new Dictionary<String, SmartTask>(StringComparer.OrdinalIgnoreCase);
        assignmentSuccessHeuristics = new ObservableCollection<SuccessHeuristicItem>();
        backgroundCommandHandles = new List<BackgroundCommandHandleService>();
        systemLogEntries = new List<String>();

        recursionExitBiasBase = 1.0;
        recursionExitBiasIncrement = 0.1;
        assignmentStatusText = "Idle";
        usageLastUpdatedUtc = DateTime.UtcNow;
    }

    public ObservableCollection<SmartTaskExecutionContext> AssignmentTasks => assignmentTaskItems;
    public ObservableCollection<WorkspaceFileItem> WorkspaceFiles => workspaceFileItems;
    public ObservableCollection<SmartTask> RootTasks => aceRootTasks;
    public ObservableCollection<SemanticFactRecord> Facts => aceFacts;
    public ObservableCollection<PolicyDecisionItem> PolicyDecisions => policyDecisionItems;
    public ObservableCollection<SuccessHeuristicItem> SuccessHeuristics => assignmentSuccessHeuristics;
    public GlobalContext GlobalContext => globalContext;
    public WorkspaceStateTrackerService WorkspaceStateTracker => workspaceStateTracker;
    public SmartTaskSchedulerService? SmartTaskScheduler => activeSmartTaskScheduler;
    public String? OpenAiApiKey => openAiApiKey;
    public String? SelectedOpenAiModelId => openAiSelectedModelId;
    public Boolean IsOpenAiConfigured => isOpenAiConfigured;
    public Boolean IsLoadingModels => isLoadingModels;
    public Boolean IsAssignmentRunning => isAssignmentRunning;
    public Boolean IsAssignmentPaused => isAssignmentPaused;
    public String AssignmentStatusText => assignmentStatusText;
    public String AssignmentAnswerOutputText => assignmentAnswerOutputText;
    public String AssignmentRawOutputText => assignmentRawOutputText;
    public String CommandsSummaryOutputText => commandsSummaryOutputText;
    public String? AssignmentFailureReason => assignmentFailureReason;
    public Double RecursionExitBiasBase => recursionExitBiasBase;
    public Double RecursionExitBiasIncrement => recursionExitBiasIncrement;

    public void ResetUsageMetrics()
    {
        lock (usageLock)
        {
            usageTotalRequests = 0;
            usagePlannerRequests = 0;
            usageRepairRequests = 0;
            usageFailureResolutionRequests = 0;
            usageRequestSucceededCount = 0;
            usageRequestFailedCount = 0;
            usagePromptTokens = 0;
            usageCompletionTokens = 0;
            usagePlannerTokens = 0;
            usageRepairTokens = 0;
            usageFailureResolutionTokens = 0;
            usageTasksSucceededCount = 0;
            usageTasksFailedCount = 0;
            usageTasksSkippedCount = 0;
            usageLastUpdatedUtc = DateTime.UtcNow;
        }

        NotifyUsageChanged();
    }

    public void ResetBudgetTracking()
    {
        lock (budgetLock)
        {
            assignmentOpenAiRequestCount = 0;
            smartTaskExecutionCount = 0;
        }
    }

    public void ResetTaskIdentityTracking()
    {
        lock (agentTaskIdLock)
        {
            agentTaskIds.Clear();
            nextTaskCreationOrder = 0;
        }

        smartTaskNodesByAgentTaskId.Clear();
        lock (completedIntentsLock)
        {
            completedTaskIntents.Clear();
        }
    }

    public void ResetAssignmentOutputs()
    {
        assignmentAnswerOutputText = String.Empty;
        assignmentRawOutputText = String.Empty;
        commandsSummaryOutputText = String.Empty;
        NotifyStateChanged();
    }

    public UsageSnapshot GetUsageSnapshot()
    {
        lock (usageLock)
        {
            return new UsageSnapshot(
                usageTotalRequests,
                usagePlannerRequests,
                usageRepairRequests,
                usageFailureResolutionRequests,
                usageRequestSucceededCount,
                usageRequestFailedCount,
                usagePromptTokens,
                usageCompletionTokens,
                usagePlannerTokens,
                usageRepairTokens,
                usageFailureResolutionTokens,
                usageTasksSucceededCount,
                usageTasksFailedCount,
                usageTasksSkippedCount,
                usageLastUpdatedUtc);
        }
    }

    public void CaptureAssignmentFailureReason(String reason)
    {
        if (String.IsNullOrWhiteSpace(reason))
        {
            return;
        }

        assignmentFailureReason = reason.Trim();
        NotifyStateChanged();
    }

    public void ClearAssignmentFailureReason()
    {
        if (String.IsNullOrWhiteSpace(assignmentFailureReason))
        {
            return;
        }

        assignmentFailureReason = null;
        NotifyStateChanged();
    }

    public String BuildDanglingTaskReason(Boolean assignmentCancelled)
    {
        if (assignmentCancelled)
        {
            return "Assignment run was cancelled while one or more tasks were still running.";
        }

        Int32 pendingCount = 0;
        for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
        {
            AssignmentTaskStatusOptions status = assignmentTaskItems[index].StatusKind;
            if (status == AssignmentTaskStatusOptions.InProgress ||
                status == AssignmentTaskStatusOptions.PendingApproval ||
                status == AssignmentTaskStatusOptions.Planned)
            {
                pendingCount = pendingCount + 1;
            }
        }

        if (pendingCount == 0)
        {
            return "No dangling tasks remained.";
        }

        return pendingCount == 1
            ? "One task was still in progress when the assignment ended."
            : pendingCount.ToString(CultureInfo.InvariantCulture) + " tasks were still in progress when the assignment ended.";
    }

    public Boolean TryReserveSmartTaskSlot()
    {
        lock (budgetLock)
        {
            if (smartTaskExecutionCount >= SmartTaskExecutionBudget)
            {
                return false;
            }

            smartTaskExecutionCount = smartTaskExecutionCount + 1;
            return true;
        }
    }

    public CancellationToken PrepareAssignmentCancellationToken()
    {
        assignmentCancellationTokenSource?.Dispose();
        assignmentCancellationTokenSource = new CancellationTokenSource();
        return assignmentCancellationTokenSource.Token;
    }

    public void UpdateAssignmentRunningState(Boolean running, String? statusText)
    {
        isAssignmentRunning = running;
        if (!String.IsNullOrWhiteSpace(statusText))
        {
            assignmentStatusText = statusText!.Trim();
        }

        if (!running)
        {
            isAssignmentPaused = false;
        }

        NotifyStateChanged();
    }

    public void UpdateAssignmentPausedState(Boolean paused)
    {
        if (isAssignmentPaused == paused)
        {
            return;
        }

        isAssignmentPaused = paused;
        NotifyStateChanged();
    }

    public void UpdateAssignmentStatus(String statusText)
    {
        if (String.IsNullOrWhiteSpace(statusText))
        {
            return;
        }

        assignmentStatusText = statusText.Trim();
        NotifyStateChanged();
    }

    public void UpdateAssignmentOutputs(String answerOutput, String rawOutput, String commandsSummary)
    {
        assignmentAnswerOutputText = answerOutput ?? String.Empty;
        assignmentRawOutputText = rawOutput ?? String.Empty;
        commandsSummaryOutputText = commandsSummary ?? String.Empty;
        NotifyStateChanged();
    }

    public Boolean TryReserveOpenAiRequest(String? scopeDescription)
    {
        lock (budgetLock)
        {
            if (assignmentOpenAiRequestCount >= AssignmentRequestBudget)
            {
                if (!String.IsNullOrWhiteSpace(scopeDescription))
                {
                    AppendLog("OpenAI request skipped for '" + scopeDescription + "' because the per-assignment budget of " + AssignmentRequestBudget.ToString(CultureInfo.InvariantCulture) + " was exceeded.");
                }
                return false;
            }

            assignmentOpenAiRequestCount = assignmentOpenAiRequestCount + 1;
            return true;
        }
    }

    public void RecordUsageRequest(String scopeDescription, UsageChannelOptions UsageChannelOptions)
    {
        lock (usageLock)
        {
            usageTotalRequests = usageTotalRequests + 1;
            if (UsageChannelOptions == UsageChannelOptions.Planner)
            {
                usagePlannerRequests = usagePlannerRequests + 1;
            }
            else if (UsageChannelOptions == UsageChannelOptions.Repair)
            {
                usageRepairRequests = usageRepairRequests + 1;
            }
            else if (UsageChannelOptions == UsageChannelOptions.FailureResolution)
            {
                usageFailureResolutionRequests = usageFailureResolutionRequests + 1;
            }

            usageLastUpdatedUtc = DateTime.UtcNow;
        }

        NotifyUsageChanged();
    }

    public void RecordUsageFailure(String scopeDescription, UsageChannelOptions UsageChannelOptions)
    {
        lock (usageLock)
        {
            usageRequestFailedCount = usageRequestFailedCount + 1;
            usageLastUpdatedUtc = DateTime.UtcNow;
        }

        NotifyUsageChanged();
    }

    public void RecordUsageSuccess(String scopeDescription, ChatCompletionUsage? usage, UsageChannelOptions UsageChannelOptions)
    {
        lock (usageLock)
        {
            usageRequestSucceededCount = usageRequestSucceededCount + 1;
            if (usage != null)
            {
                usagePromptTokens = usagePromptTokens + usage.PromptTokens;
                usageCompletionTokens = usageCompletionTokens + usage.CompletionTokens;
                if (UsageChannelOptions == UsageChannelOptions.Planner)
                {
                    usagePlannerTokens = usagePlannerTokens + usage.TotalTokens;
                }
                else if (UsageChannelOptions == UsageChannelOptions.Repair)
                {
                    usageRepairTokens = usageRepairTokens + usage.TotalTokens;
                }
                else if (UsageChannelOptions == UsageChannelOptions.FailureResolution)
                {
                    usageFailureResolutionTokens = usageFailureResolutionTokens + usage.TotalTokens;
                }
            }

            usageLastUpdatedUtc = DateTime.UtcNow;
        }

        NotifyUsageChanged();
    }

    public String EnsureUniqueAgentTaskId(String? preferredId)
    {
        String baseId = String.IsNullOrWhiteSpace(preferredId) ? "task" : preferredId!.Trim();
        baseId = baseId.Replace(' ', '-');
        lock (agentTaskIdLock)
        {
            if (agentTaskIds.Add(baseId))
            {
                return baseId;
            }

            Int32 suffix = 1;
            String candidate;
            do
            {
                candidate = baseId + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix = suffix + 1;
            }
            while (!agentTaskIds.Add(candidate));

            return candidate;
        }
    }

    public void AssignCreationOrder(SmartTaskExecutionContext context)
    {
        if (context == null)
        {
            return;
        }

        Int32 order;
        lock (creationOrderLock)
        {
            nextTaskCreationOrder = nextTaskCreationOrder + 1;
            order = nextTaskCreationOrder;
        }

        context.SetCreationOrder(order);
    }

    public Boolean HasIntentBeenCompleted(String? intent)
    {
        if (String.IsNullOrWhiteSpace(intent))
        {
            return false;
        }

        lock (completedIntentsLock)
        {
            return completedTaskIntents.Contains(intent);
        }
    }

    public void RegisterCompletedIntent(String? intent)
    {
        if (String.IsNullOrWhiteSpace(intent))
        {
            return;
        }

        lock (completedIntentsLock)
        {
            completedTaskIntents.Add(intent);
        }
    }

    private void NotifyStateChanged()
    {
        dispatcherService.Invoke(() => StateChanged?.Invoke(this, EventArgs.Empty));
    }

    private void NotifyUsageChanged()
    {
        dispatcherService.Invoke(() => UsageChanged?.Invoke(this, EventArgs.Empty));
    }

    public void ClearAssignmentCancellationToken()
    {
        assignmentCancellationTokenSource?.Dispose();
        assignmentCancellationTokenSource = null;
    }

    public void CancelAssignmentRun()
    {
        assignmentCancellationTokenSource?.Cancel();
    }

    public void Dispose()
    {
        assignmentCancellationTokenSource?.Dispose();
    }

    private void InvokeOnUiThread(Action action)
    {
        dispatcherService.Invoke(action);
    }

    private void AppendLog(String message)
    {
        DateTime timestamp = DateTime.Now;
        String timeText = timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        String lineText = "[" + timeText + "] " + message;

        lock (systemLogLock)
        {
            systemLogEntries.Add(lineText);
            if (systemLogEntries.Count > MaxSystemLogEntries)
            {
                Int32 excess = systemLogEntries.Count - MaxSystemLogEntries;
                systemLogEntries.RemoveRange(0, excess);
            }
        }

        SystemLogEntryAdded?.Invoke(this, new SystemLogEventArgs(lineText));
    }

    private void ClearSystemLog()
    {
        lock (systemLogLock)
        {
            systemLogEntries.Clear();
        }
    }

    public String GetSystemLogSnapshot()
    {
        lock (systemLogLock)
        {
            if (systemLogEntries.Count == 0)
            {
                return String.Empty;
            }

            return String.Join(Environment.NewLine, systemLogEntries);
        }
    }

    public void ResetSystemLog()
    {
        ClearSystemLog();
    }

    public void ResetHeuristicEvaluationIndicators()
    {
        dispatcherService.Invoke(() =>
        {
            for (Int32 index = 0; index < assignmentSuccessHeuristics.Count; index++)
            {
                assignmentSuccessHeuristics[index].SetEvaluationResult(SuccessHeuristicEvaluationStatusOptions.Pending, "Pending evaluation");
            }
        });
    }

    public void ApplyHeuristicEvaluationResponse(HeuristicEvaluationListResult evaluation)
    {
        if (evaluation.Heuristics == null || evaluation.Heuristics.Count == 0)
        {
            return;
        }

        dispatcherService.Invoke(() =>
        {
            foreach (HeuristicEvaluationResult record in evaluation.Heuristics)
            {
                if (record.Index < 0 || record.Index >= assignmentSuccessHeuristics.Count)
                {
                    continue;
                }

                SuccessHeuristicItem heuristic = assignmentSuccessHeuristics[record.Index];
                SuccessHeuristicEvaluationStatusOptions status = record.Passed ? SuccessHeuristicEvaluationStatusOptions.Passed : SuccessHeuristicEvaluationStatusOptions.Failed;
                heuristic.SetEvaluationResult(status, record.Notes);
            }
        });
    }

    public Boolean DetermineHeuristicVerdict()
    {
        Boolean hasMandatory = false;
        Boolean optionalFailure = false;

        for (Int32 index = 0; index < assignmentSuccessHeuristics.Count; index++)
        {
            SuccessHeuristicItem heuristic = assignmentSuccessHeuristics[index];
            if (heuristic.Mandatory)
            {
                hasMandatory = true;
                if (heuristic.EvaluationStatus != SuccessHeuristicEvaluationStatusOptions.Passed)
                {
                    return false;
                }
            }
            else if (heuristic.EvaluationStatus == SuccessHeuristicEvaluationStatusOptions.Failed)
            {
                optionalFailure = true;
            }
        }

        return hasMandatory ? true : !optionalFailure;
    }

    public String BuildFailedHeuristicSummary(Boolean requiredOnly)
    {
        List<String> failures = new List<String>();
        for (Int32 index = 0; index < assignmentSuccessHeuristics.Count; index++)
        {
            SuccessHeuristicItem heuristic = assignmentSuccessHeuristics[index];
            if (heuristic.EvaluationStatus != SuccessHeuristicEvaluationStatusOptions.Failed)
            {
                continue;
            }

            if (requiredOnly && !heuristic.Mandatory)
            {
                continue;
            }

            String description = heuristic.Description ?? ("Heuristic " + (index + 1).ToString(CultureInfo.InvariantCulture));
            failures.Add(description);
        }

        if (failures.Count == 0)
        {
            return requiredOnly ? "No required heuristics were marked as failed." : "No heuristics were marked as failed.";
        }

        return "Failed heuristics: " + String.Join(", ", failures);
    }

    public String BuildSuccessHeuristicSummary()
    {
        if (assignmentSuccessHeuristics.Count == 0)
        {
            return "No explicit heuristics recorded.";
        }

        StringBuilder builder = new StringBuilder();
        for (Int32 index = 0; index < assignmentSuccessHeuristics.Count; index++)
        {
            SuccessHeuristicItem heuristic = assignmentSuccessHeuristics[index];
            builder.Append(" - [");
            builder.Append(heuristic.Mandatory ? "Must" : "Optional");
            builder.Append("] ");
            builder.Append(heuristic.Description);
            if (!String.IsNullOrWhiteSpace(heuristic.Evidence))
            {
                builder.Append(" | Evidence: ");
                builder.Append(heuristic.Evidence);
            }
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    public void AppendSemanticFactsSection(StringBuilder builder, String heading, Int32 maxFacts = 0, Boolean includeReminder = false)
    {
        builder.AppendLine();
        builder.AppendLine(heading);

        IReadOnlyList<SemanticFactRecord> facts = globalContext.Facts;
        if (facts.Count == 0)
        {
            builder.AppendLine("- (no shared facts yet; record new ones as you learn critical details)");
            return;
        }

        Boolean hasLimit = maxFacts > 0;
        Int32 appended = 0;
        for (Int32 index = facts.Count - 1; index >= 0; index--)
        {
            if (hasLimit && appended >= maxFacts)
            {
                break;
            }

            SemanticFactRecord fact = facts[index];
            builder.AppendLine("- " + fact.Summary + ": " + fact.Detail + FormatFactMetadataSuffix(fact));
            appended = appended + 1;
        }

        if (includeReminder)
        {
            builder.AppendLine("This blackboard captures work already completed. Read it before planning and add new facts as soon as you finish impactful steps.");
        }
    }

    public void RecordSemanticFact(SmartTask smartTask, String summary, String detail, String? filePath = null, SemanticFactOptions factKind = SemanticFactOptions.General)
    {
        if (smartTask == null)
        {
            return;
        }

        if (String.IsNullOrWhiteSpace(summary))
        {
            return;
        }

        String source = smartTask.Intent ?? smartTask.Id ?? "Task";
        globalContext.SetFact(summary, detail ?? String.Empty, source, filePath, factKind);
    }

    public String BuildSemanticFactsSnapshot(Int32 maxFacts = 0)
    {
        IReadOnlyList<SemanticFactRecord> facts = globalContext.Facts;
        if (facts.Count == 0)
        {
            return "- (no shared facts yet; record new ones as you discover them)";
        }

        Boolean hasLimit = maxFacts > 0;
        Int32 appended = 0;
        StringBuilder builder = new StringBuilder();
        for (Int32 index = facts.Count - 1; index >= 0; index--)
        {
            if (hasLimit && appended >= maxFacts)
            {
                break;
            }

            SemanticFactRecord fact = facts[index];
            builder.AppendLine("- " + fact.Summary + ": " + fact.Detail + FormatFactMetadataSuffix(fact));
            appended = appended + 1;
        }

        return builder.ToString().TrimEnd();
    }

    private static String FormatFactMetadataSuffix(SemanticFactRecord fact)
    {
        StringBuilder suffix = new StringBuilder();
        if (!String.IsNullOrWhiteSpace(fact.FilePath))
        {
            suffix.Append(" [file: ");
            suffix.Append(fact.FilePath);
            suffix.Append(']');
        }

        if (!String.IsNullOrWhiteSpace(fact.Source))
        {
            suffix.Append(" [source: ");
            suffix.Append(fact.Source);
            suffix.Append(']');
        }

        if (fact.Kind != SemanticFactOptions.General)
        {
            suffix.Append(" [kind: ");
            suffix.Append(fact.Kind.ToString());
            suffix.Append(']');
        }

        return suffix.ToString();
    }

    public void ClearOpenAiConfiguration()
    {
        openAiApiKey = null;
        openAiSelectedModelId = null;
        isOpenAiConfigured = false;
        openAiAvailableModelIds.Clear();
        NotifyStateChanged();
    }

    public Boolean TrySelectOpenAiModel(String? modelId)
    {
        if (String.IsNullOrWhiteSpace(modelId))
        {
            openAiSelectedModelId = null;
            NotifyStateChanged();
            return false;
        }

        for (Int32 index = 0; index < openAiAvailableModelIds.Count; index++)
        {
            String candidate = openAiAvailableModelIds[index];
            if (String.Equals(candidate, modelId, StringComparison.OrdinalIgnoreCase))
            {
                openAiSelectedModelId = candidate;
                NotifyStateChanged();
                return true;
            }
        }

        AppendLog("Requested OpenAI model '" + modelId + "' is not available.");
        return false;
    }

    public async Task<IReadOnlyList<String>> LoadOpenAiModelsAsync(String apiKey, CancellationToken cancellationToken)
    {
        if (String.IsNullOrWhiteSpace(apiKey))
        {
            ClearOpenAiConfiguration();
            return Array.Empty<String>();
        }

        openAiApiKey = apiKey.Trim();
        isOpenAiConfigured = true;
        isLoadingModels = true;
        NotifyStateChanged();

        try
        {
            AppendLog("Requesting available models from OpenAI.");

            using HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey);

            using HttpResponseMessage responseMessage = await httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
            String responseContent = await responseMessage.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            openAiAvailableModelIds.Clear();
            openAiSelectedModelId = null;

            if (!responseMessage.IsSuccessStatusCode)
            {
                AppendLog("OpenAI returned an error while listing models: " + responseMessage.StatusCode + " - " + responseContent);
                return Array.Empty<String>();
            }

            OpenAiListModelsResult? modelsResponse = JsonSerializer.Deserialize<OpenAiListModelsResult>(responseContent, jsonSerializerOptions);
            if (modelsResponse?.Data != null)
            {
                for (Int32 index = 0; index < modelsResponse.Data.Count; index++)
                {
                    OpenAiModel model = modelsResponse.Data[index];
                    if (!String.IsNullOrWhiteSpace(model.Id))
                    {
                        openAiAvailableModelIds.Add(model.Id);
                    }
                }
            }

            openAiAvailableModelIds.Sort(StringComparer.OrdinalIgnoreCase);

            if (openAiAvailableModelIds.Count > 0)
            {
                openAiSelectedModelId = openAiAvailableModelIds[0];
                AppendLog("Loaded " + openAiAvailableModelIds.Count + " models from OpenAI. Defaulting to '" + openAiSelectedModelId + "'.");
            }
            else
            {
                AppendLog("OpenAI model list did not contain any entries.");
            }

            return openAiAvailableModelIds.ToArray();
        }
        catch (Exception exception)
        {
            AppendLog("Failed to load OpenAI models: " + exception.Message);
            return Array.Empty<String>();
        }
        finally
        {
            isLoadingModels = false;
            NotifyStateChanged();
        }
    }

    public void AddSystemLogEntry(String message)
    {
        if (String.IsNullOrWhiteSpace(message))
        {
            return;
        }

        AppendLog(message);
    }

    public SmartTask EnsureAssignmentRootSmartTask(String? assignmentTitle, String assignmentPrompt)
    {
        String rootIntent = !String.IsNullOrWhiteSpace(assignmentTitle) ? assignmentTitle! : "Assignment";

        SmartTask rootTask = new SmartTask();
        rootTask.Id = "assignment-" + Guid.NewGuid().ToString("N");
        rootTask.Intent = rootIntent;
        rootTask.Type = SmartTaskTypeOptions.Root;
        rootTask.Phase = null;
        rootTask.Stage = "Planning";
        ApplyWorkBudgetToSmartTask(rootTask, 0);

        assignmentRootSmartTask = rootTask;

        InvokeOnUiThread(() =>
        {
            aceRootTasks.Clear();
            aceRootTasks.Add(rootTask);
        });

        if (!String.IsNullOrWhiteSpace(rootTask.Id))
        {
            smartTaskNodesByAgentTaskId[rootTask.Id] = rootTask;
        }

        UpdateSmartTaskState(rootTask, SmartTaskStateOptions.Planning, "Planning");

        return rootTask;
    }

    public SmartTask CreateAndAttachAgentSmartTask(String intent, SmartTaskTypeOptions type, String stage)
    {
        SmartTask agentTask = new SmartTask();
        agentTask.Id = "agent-" + Guid.NewGuid().ToString("N");
        agentTask.Intent = intent;
        agentTask.Type = type;
        agentTask.Stage = stage;
        Int32 depth = assignmentRootSmartTask != null ? assignmentRootSmartTask.Depth + 1 : 0;
        ApplyWorkBudgetToSmartTask(agentTask, depth);

        InvokeOnUiThread(() =>
        {
            SmartTask? parent = assignmentRootSmartTask;
            if (parent != null)
            {
                agentTask.ParentId = parent.Id;
                parent.Subtasks.Add(agentTask);
            }
            else
            {
                aceRootTasks.Add(agentTask);
            }
        });

        if (!String.IsNullOrWhiteSpace(agentTask.Id))
        {
            smartTaskNodesByAgentTaskId[agentTask.Id] = agentTask;
        }

        return agentTask;
    }

    public SmartTaskExecutionContext EnsureExecutionContextForSmartTask(SmartTask smartTask, Int32 maxAllowedRepairAttempts)
    {
        if (smartTask == null)
        {
            throw new ArgumentNullException(nameof(smartTask));
        }

        SmartTaskExecutionContext? bound = smartTask.BoundAssignmentTask;
        if (bound != null)
        {
            if (maxAllowedRepairAttempts > 0 && bound.MaxRepairAttempts != maxAllowedRepairAttempts)
            {
                bound.MaxRepairAttempts = maxAllowedRepairAttempts;
            }
            return bound;
        }

        SmartTaskExecutionContext context = new SmartTaskExecutionContext();
        context.AgentTaskId = !String.IsNullOrWhiteSpace(smartTask.Id) ? smartTask.Id! : EnsureUniqueAgentTaskId("task");
        context.Label = smartTask.Intent ?? "Task";
        context.Type = smartTask.Type.ToString();
        context.CreatedAt = DateTime.Now;
        context.MaxRepairAttempts = maxAllowedRepairAttempts;
        context.TaskContext = smartTask.Intent;
        context.SetStatus(AssignmentTaskStatusOptions.Planned);
        context.RequiresCommandExecution = false;
        AssignCreationOrder(context);

        smartTask.BoundAssignmentTask = context;

        InvokeOnUiThread(() =>
        {
            assignmentTaskItems.Add(context);
            RenumberAssignmentTasks();
        });

        RegisterSmartTaskNode(smartTask);
        return context;
    }

    public void RegisterSmartTaskNode(SmartTask task)
    {
        if (task == null)
        {
            return;
        }

        if (String.IsNullOrWhiteSpace(task.Id))
        {
            return;
        }

        smartTaskNodesByAgentTaskId[task.Id] = task;
        lock (agentTaskIdLock)
        {
            agentTaskIds.Add(task.Id);
        }
    }

    public SmartTask? FindSmartTaskNode(String? agentTaskId)
    {
        if (String.IsNullOrWhiteSpace(agentTaskId))
        {
            return null;
        }

        smartTaskNodesByAgentTaskId.TryGetValue(agentTaskId, out SmartTask? node);
        return node;
    }

    public SmartTask GetOrCreateSmartTaskNode(String agentTaskId, SmartTaskExecutionContext viewItem, String? parentTaskId, String label)
    {
        if (String.IsNullOrWhiteSpace(agentTaskId))
        {
            agentTaskId = EnsureUniqueAgentTaskId("task-node");
            viewItem.AgentTaskId = agentTaskId;
        }

        SmartTask? existing = FindSmartTaskNode(agentTaskId);
        if (existing != null)
        {
            BindSmartTaskToViewItem(existing, viewItem, label);
            Int32 existingDepth = CalculateSmartTaskDepth(existing);
            ApplyWorkBudgetToSmartTask(existing, existingDepth);
            return existing;
        }

        SmartTask smartTask = new SmartTask
        {
            Id = agentTaskId,
            Intent = label,
            Type = SmartTaskTypeOptions.Worker,
            State = SmartTaskStateOptions.Pending,
            Phase = viewItem.Phase
        };

        BindSmartTaskToViewItem(smartTask, viewItem, label);

        SmartTask? parentSmartTask = null;
        if (!String.IsNullOrWhiteSpace(parentTaskId))
        {
            parentSmartTask = FindSmartTaskNode(parentTaskId);
        }

        if (parentSmartTask == null && aceRootTasks.Count > 0)
        {
            parentSmartTask = aceRootTasks[0];
        }

        if (parentSmartTask != null)
        {
            smartTask.ParentId = parentSmartTask.Id;
        }

        Int32 depth = parentSmartTask != null ? parentSmartTask.Depth + 1 : 0;
        ApplyWorkBudgetToSmartTask(smartTask, depth);

        InvokeOnUiThread(() =>
        {
            if (parentSmartTask == null)
            {
                if (!aceRootTasks.Contains(smartTask))
                {
                    aceRootTasks.Add(smartTask);
                }
            }
            else
            {
                if (!parentSmartTask.Subtasks.Contains(smartTask))
                {
                    parentSmartTask.Subtasks.Add(smartTask);
                }
            }
        });

        RegisterSmartTaskNode(smartTask);
        return smartTask;
    }

    public SmartTask? FindSmartTaskForAssignmentTask(SmartTaskExecutionContext taskItem)
    {
        if (taskItem == null || String.IsNullOrWhiteSpace(taskItem.AgentTaskId))
        {
            return null;
        }

        return FindSmartTaskNode(taskItem.AgentTaskId);
    }

    public Int32 CalculateSmartTaskDepth(SmartTask task)
    {
        if (task == null)
        {
            return 0;
        }

        Int32 depth = 0;
        String? currentParentId = task.ParentId;
        while (!String.IsNullOrWhiteSpace(currentParentId))
        {
            SmartTask? parentTask = FindSmartTaskNode(currentParentId);
            if (parentTask == null)
            {
                break;
            }

            depth = depth + 1;
            currentParentId = parentTask.ParentId;
        }

        return depth;
    }

    public void UpdateRecursionExitBiasBase(Double newValue)
    {
        UpdateRecursionExitBiasSettings(newValue, recursionExitBiasIncrement);
    }

    public void UpdateRecursionExitBiasIncrement(Double newValue)
    {
        UpdateRecursionExitBiasSettings(recursionExitBiasBase, newValue);
    }

    public void UpdateRecursionExitBiasSettings(Double baseFraction, Double incrementFraction)
    {
        recursionExitBiasBase = ClampFraction(baseFraction);
        recursionExitBiasIncrement = ClampFraction(incrementFraction);
        RefreshSmartTaskWorkBudgets();
        NotifyStateChanged();
    }

    public (Double WorkRetentionFraction, Double DelegationFraction) CalculateWorkBudgetForDepth(Int32 depth)
    {
        Double retentionFraction = recursionExitBiasBase + (depth * recursionExitBiasIncrement);
        if (retentionFraction < 0.0)
        {
            retentionFraction = 0.0;
        }
        else if (retentionFraction > 1.0)
        {
            retentionFraction = 1.0;
        }

        Double delegationFraction = 1.0 - retentionFraction;
        if (delegationFraction < 0.0)
        {
            delegationFraction = 0.0;
        }

        return (retentionFraction, delegationFraction);
    }

    public Double GetExecutionBiasForDepth(Int32 depth)
    {
        (Double WorkRetentionFraction, Double _) budget = CalculateWorkBudgetForDepth(depth);
        return budget.WorkRetentionFraction;
    }

    public void AllowDependentsToProceed(
        SmartTaskExecutionContext taskItem,
        String? reason,
        Action<SmartTaskExecutionContext, String>? appendTaskLog = null)
    {
        if (taskItem == null || taskItem.AllowsDependentsToProceed)
        {
            return;
        }

        taskItem.AllowsDependentsToProceed = true;
        String message = !String.IsNullOrWhiteSpace(reason)
            ? reason!.Trim()
            : "Dependents unlocked; downstream work may continue.";
        appendTaskLog?.Invoke(taskItem, message);
    }

    public void ApplyWorkBudgetToSmartTask(SmartTask task, Int32 depth)
    {
        if (task == null)
        {
            return;
        }

        (Double WorkRetentionFraction, Double DelegationFraction) budget = CalculateWorkBudgetForDepth(depth);
        task.Depth = depth;
        task.WorkRetentionFraction = budget.WorkRetentionFraction;
        task.DelegationFraction = budget.DelegationFraction;
    }

    public void RefreshSmartTaskWorkBudgets()
    {
        for (Int32 index = 0; index < aceRootTasks.Count; index++)
        {
            SmartTask rootTask = aceRootTasks[index];
            RecalculateSmartTaskBudgetRecursive(rootTask, 0);
        }
    }

    private void RecalculateSmartTaskBudgetRecursive(SmartTask task, Int32 depth)
    {
        if (task == null)
        {
            return;
        }

        ApplyWorkBudgetToSmartTask(task, depth);

        if (task.Subtasks == null)
        {
            return;
        }

        for (Int32 index = 0; index < task.Subtasks.Count; index++)
        {
            SmartTask child = task.Subtasks[index];
            RecalculateSmartTaskBudgetRecursive(child, depth + 1);
        }
    }

    public Boolean TryScheduleRepairSmartTask(
        SmartTask failedTask,
        SmartTaskExecutionContext failedContext,
        SmartTaskSchedulerService pendingTasks,
        Int32 maxAllowedRepairAttempts,
        RepairOrchestrationResult? callbacks)
    {
        if (failedTask == null || failedContext == null || pendingTasks == null)
        {
            return false;
        }

        if (failedContext.HasScheduledRepairTask)
        {
            return false;
        }

        SmartTask repairTask = new SmartTask();
        repairTask.Id = "repair-" + Guid.NewGuid().ToString("N");
        repairTask.Intent = "Repair failed task: " + (failedTask.Intent ?? failedTask.Id ?? "Task");
        repairTask.Type = SmartTaskTypeOptions.Worker;
        repairTask.ParentId = failedTask.Id;
        repairTask.Phase = failedTask.Phase;
        repairTask.Depth = failedTask.Depth + 1;
        repairTask.Stage = "Queued for repair";
        ApplyWorkBudgetToSmartTask(repairTask, repairTask.Depth);

        InvokeOnUiThread(() =>
        {
            failedTask.Subtasks.Add(repairTask);
        });

        RegisterSmartTaskNode(repairTask);

        SmartTaskExecutionContext repairContext = EnsureExecutionContextForSmartTask(repairTask, maxAllowedRepairAttempts);
        repairContext.ParentTaskId = failedContext.AgentTaskId;
        repairContext.TaskContext = BuildRepairTaskContext(failedTask, failedContext);
        repairContext.RequiresCommandExecution = true;
        repairContext.SetContextTags(new List<String> { "repair", "self-heal" });
        callbacks?.AppendTaskLog?.Invoke(repairContext, "Autogenerated repair task for failed scope: " + (failedTask.Intent ?? failedTask.Id ?? "task") + ".");

        failedContext.HasScheduledRepairTask = true;
        PushTasksOntoScheduler(new[] { repairTask }, pendingTasks);

        if (!String.IsNullOrWhiteSpace(assignmentFailureReason) &&
            !String.IsNullOrWhiteSpace(failedTask.Stage) &&
            String.Equals(assignmentFailureReason, failedTask.Stage, StringComparison.Ordinal))
        {
            ClearAssignmentFailureReason();
        }

        callbacks?.AppendTaskLog?.Invoke(failedContext, "Delegated recovery to repair task '" + repairTask.Intent + "'.");
        return true;
    }

    public String BuildRepairTaskContext(SmartTask failedTask, SmartTaskExecutionContext failedContext)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Repair and unblock the failed task.");
        builder.AppendLine("Original intent: " + (failedTask.Intent ?? "(unspecified)") + ".");
        builder.AppendLine("Failure stage: " + (failedTask.Stage ?? "(unspecified)") + ".");

        if (!String.IsNullOrWhiteSpace(failedContext.TaskContext))
        {
            builder.AppendLine();
            builder.AppendLine("Task context summary:");
            builder.AppendLine(TextUtilityService.BuildCompactSnippet(failedContext.TaskContext, 1500));
        }

        if (!String.IsNullOrWhiteSpace(failedContext.LastResultText))
        {
            builder.AppendLine();
            builder.AppendLine("Last output snippet:");
            builder.AppendLine(TextUtilityService.BuildCompactSnippet(failedContext.LastResultText, 1500));
        }

        builder.AppendLine();
        builder.AppendLine("Semantic blackboard snapshot:");
        builder.AppendLine(BuildSemanticFactsSnapshot());

        return builder.ToString();
    }

    public Boolean TrySkipFailedRepairTask(
        SmartTask smartTask,
        SmartTaskExecutionContext taskContext,
        String failureStage,
        RepairOrchestrationResult? callbacks)
    {
        if (smartTask == null || taskContext == null)
        {
            return false;
        }

        if (!IsRepairTaskContext(smartTask, taskContext))
        {
            return false;
        }

        taskContext.SetStatus(AssignmentTaskStatusOptions.Skipped);
        AllowDependentsToProceed(taskContext, "Repair task failed; continuing with remaining work.", callbacks?.AppendTaskLog);
        UpdateSmartTaskState(smartTask, SmartTaskStateOptions.Skipped, (failureStage + " (ignored)").Trim());
        callbacks?.AppendTaskLog?.Invoke(taskContext, "Repair task failed but will be skipped to keep the run alive.");
        RecordRepairFailureFact(smartTask, taskContext, failureStage);
        return true;
    }

    private void RecordRepairFailureFact(
        SmartTask smartTask,
        SmartTaskExecutionContext taskContext,
        String failureStage)
    {
        String summary = "Repair fallback failed: " + (smartTask.Intent ?? taskContext.Label ?? "Task");
        StringBuilder detail = new StringBuilder();
        detail.AppendLine(failureStage);

        if (taskContext.RepairAttemptHistory.Count > 0)
        {
            detail.AppendLine();
            detail.AppendLine("Repair attempt history:");
            Int32 limit = Math.Min(taskContext.RepairAttemptHistory.Count, 3);
            for (Int32 index = 0; index < limit; index++)
            {
                detail.AppendLine("- " + taskContext.RepairAttemptHistory[index]);
            }
        }

        if (!String.IsNullOrWhiteSpace(taskContext.LastResultText))
        {
            detail.AppendLine();
            detail.AppendLine("Last output snippet:");
            detail.AppendLine(TextUtilityService.BuildCompactSnippet(taskContext.LastResultText, 600));
        }

        RecordSemanticFact(smartTask, summary, detail.ToString().Trim());
    }


    private static Boolean IsRepairTaskContext(SmartTask smartTask, SmartTaskExecutionContext? taskContext)
    {
        if (taskContext == null)
        {
            return false;
        }

        if (ContextHasTag(taskContext, "repair") || ContextHasTag(taskContext, "self-heal"))
        {
            return true;
        }

        if (!String.IsNullOrWhiteSpace(taskContext.Type) && taskContext.Type.Equals("Recovery", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!String.IsNullOrWhiteSpace(taskContext.Label) && taskContext.Label.IndexOf("repair", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (smartTask.Intent != null && smartTask.Intent.IndexOf("repair", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return false;
    }

    private static Boolean ContextHasTag(SmartTaskExecutionContext context, String tag)
    {
        if (context.ContextTags == null || context.ContextTags.Count == 0)
        {
            return false;
        }

        for (Int32 index = 0; index < context.ContextTags.Count; index++)
        {
            String entry = context.ContextTags[index];
            if (!String.IsNullOrWhiteSpace(entry) && entry.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void PushTasksOntoScheduler(IEnumerable<SmartTask> tasks, SmartTaskSchedulerService? scheduler)
    {
        if (scheduler == null || tasks == null)
        {
            return;
        }

        List<SmartTask> orderedTasks = new List<SmartTask>();
        foreach (SmartTask task in tasks)
        {
            if (task != null)
            {
                orderedTasks.Add(task);
            }
        }

        if (orderedTasks.Count == 0)
        {
            return;
        }

        for (Int32 index = orderedTasks.Count - 1; index >= 0; index--)
        {
            scheduler.Push(orderedTasks[index]);
        }
    }

    private void RenumberAssignmentTasks()
    {
        for (Int32 index = 0; index < assignmentTaskItems.Count; index++)
        {
            assignmentTaskItems[index].TaskNumber = index + 1;
        }
    }

    private static void BindSmartTaskToViewItem(SmartTask smartTask, SmartTaskExecutionContext viewItem, String label)
    {
        if (smartTask == null || viewItem == null)
        {
            return;
        }

        smartTask.BoundAssignmentTask = viewItem;
        smartTask.Intent = String.IsNullOrWhiteSpace(viewItem.TaskContext) ? label : viewItem.TaskContext!;
        if (!String.IsNullOrWhiteSpace(viewItem.Phase))
        {
            smartTask.Phase = viewItem.Phase;
        }
    }

    public static String BuildPlannerAgentIntent(PlannerRequestContext requestContext)
    {
        if (requestContext.InvocationKind == PlannerInvocationOptions.AssignmentRoot)
        {
            return "Planner: Plan assignment";
        }

        AgentPlannedTask? parentTask = requestContext.ParentTask;
        if (parentTask == null)
        {
            return "Planner: Expand task";
        }

        String label;
        if (!String.IsNullOrWhiteSpace(parentTask.Label))
        {
            label = parentTask.Label!;
        }
        else if (!String.IsNullOrWhiteSpace(parentTask.Id))
        {
            label = parentTask.Id!;
        }
        else
        {
            label = "Unnamed task";
        }

        return "Planner: Expand task " + label;
    }

    public void FinalizeAssignmentRootSmartTask()
    {
        SmartTask? rootTask = assignmentRootSmartTask;
        if (rootTask == null)
        {
            return;
        }

        SmartTaskStateOptions finalState = SmartTaskStateOptions.Succeeded;
        String stage = "Completed";

        if (String.Equals(assignmentStatusText, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            finalState = SmartTaskStateOptions.Failed;
            stage = "Failed";
        }
        else if (String.Equals(assignmentStatusText, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            finalState = SmartTaskStateOptions.Skipped;
            stage = "Cancelled";
        }

        UpdateSmartTaskState(rootTask, finalState, stage);
    }

    public void UpdateSmartTaskState(SmartTask task, SmartTaskStateOptions newState, String? stage = null)
    {
        if (task == null)
        {
            return;
        }

        InvokeOnUiThread(() =>
        {
            task.State = newState;
            if (!String.IsNullOrWhiteSpace(stage))
            {
                task.Stage = stage!;
            }

            if (IsTerminalSmartTaskState(newState))
            {
                RegisterSmartTaskOutcome(task, newState);
            }

            SmartTaskExecutionContext? bound = task.BoundAssignmentTask;
            if (bound != null)
            {
                AssignmentTaskStatusOptions mappedStatus = MapSmartTaskStateToAssignmentStatus(newState);
                bound.SetStatus(mappedStatus);
            }

            PropagateSmartTaskStateUpwards(task);
        });
    }

    public void UpdateSmartTaskStage(SmartTask task, String stage)
    {
        if (task == null || String.IsNullOrWhiteSpace(stage))
        {
            return;
        }

        InvokeOnUiThread(() => task.Stage = stage);
    }

    public void UpdateSmartTaskStrategy(SmartTask task, SmartTaskStrategyOptions strategy)
    {
        if (task == null)
        {
            return;
        }

        InvokeOnUiThread(() => task.Strategy = strategy);
    }

    public void ReplaceSmartTaskChildren(SmartTask parent, IEnumerable<SmartTask> children)
    {
        if (parent == null)
        {
            return;
        }

        InvokeOnUiThread(() =>
        {
            parent.Subtasks.Clear();
            if (children == null)
            {
                return;
            }

            foreach (SmartTask child in children)
            {
                child.ParentId = parent.Id;
                parent.Subtasks.Add(child);
                if (!String.IsNullOrWhiteSpace(child.Id))
                {
                    smartTaskNodesByAgentTaskId[child.Id] = child;
                }
            }
        });
    }

    private void PropagateSmartTaskStateUpwards(SmartTask task)
    {
        if (task.ParentId == null)
        {
            return;
        }

        if (!smartTaskNodesByAgentTaskId.TryGetValue(task.ParentId, out SmartTask? parentTask) || parentTask == null)
        {
            return;
        }

        if (parentTask.Subtasks == null || parentTask.Subtasks.Count == 0)
        {
            return;
        }

        Boolean allTerminal = true;
        Boolean anyFailed = false;

        foreach (SmartTask subtask in parentTask.Subtasks)
        {
            if (!IsTerminalSmartTaskState(subtask.State))
            {
                allTerminal = false;
                break;
            }

            if (subtask.State == SmartTaskStateOptions.Failed)
            {
                anyFailed = true;
            }
        }

        if (allTerminal)
        {
            SmartTaskStateOptions parentNewState = anyFailed ? SmartTaskStateOptions.Failed : SmartTaskStateOptions.Succeeded;
            String reason = anyFailed ? "One or more subtasks failed" : "All subtasks completed";

            if (parentTask.State != parentNewState)
            {
                UpdateSmartTaskState(parentTask, parentNewState, reason);
            }
        }
    }

    private static AssignmentTaskStatusOptions MapSmartTaskStateToAssignmentStatus(SmartTaskStateOptions newState)
    {
        if (newState == SmartTaskStateOptions.Succeeded)
        {
            return AssignmentTaskStatusOptions.Succeeded;
        }

        if (newState == SmartTaskStateOptions.Failed)
        {
            return AssignmentTaskStatusOptions.Failed;
        }

        if (newState == SmartTaskStateOptions.Skipped)
        {
            return AssignmentTaskStatusOptions.Skipped;
        }

        if (newState == SmartTaskStateOptions.Executing)
        {
            return AssignmentTaskStatusOptions.InProgress;
        }

        if (newState == SmartTaskStateOptions.Planning || newState == SmartTaskStateOptions.Verifying)
        {
            return AssignmentTaskStatusOptions.PendingApproval;
        }

        return AssignmentTaskStatusOptions.Planned;
    }

    private static Boolean IsTerminalSmartTaskState(SmartTaskStateOptions state)
    {
        return state == SmartTaskStateOptions.Succeeded ||
               state == SmartTaskStateOptions.Failed ||
               state == SmartTaskStateOptions.Skipped;
    }

    private void RegisterSmartTaskOutcome(SmartTask task, SmartTaskStateOptions outcomeState)
    {
        if (task == null)
        {
            return;
        }

        if (!smartTasksWithRecordedOutcome.Add(task))
        {
            return;
        }

        Boolean changed = false;
        lock (usageLock)
        {
            if (outcomeState == SmartTaskStateOptions.Succeeded)
            {
                usageTasksSucceededCount = usageTasksSucceededCount + 1;
            }
            else if (outcomeState == SmartTaskStateOptions.Failed)
            {
                usageTasksFailedCount = usageTasksFailedCount + 1;
            }
            else
            {
                usageTasksSkippedCount = usageTasksSkippedCount + 1;
            }

            usageLastUpdatedUtc = DateTime.UtcNow;
            changed = true;
        }

        if (changed)
        {
            NotifyUsageChanged();
        }
    }

    private static Double ClampFraction(Double value)
    {
        if (Double.IsNaN(value))
        {
            return 0.0;
        }

        if (value < 0.0)
        {
            return 0.0;
        }

        if (value > 1.0)
        {
            return 1.0;
        }

        return value;
    }
}


