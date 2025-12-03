namespace AgentCommandEnvironment.Core.Models;

public readonly record struct UsageSnapshot(
    Int32 TotalRequests,
    Int32 PlannerRequests,
    Int32 RepairRequests,
    Int32 FailureResolutionRequests,
    Int32 RequestSucceededCount,
    Int32 RequestFailedCount,
    Int64 PromptTokens,
    Int64 CompletionTokens,
    Int64 PlannerTokens,
    Int64 RepairTokens,
    Int64 FailureResolutionTokens,
    Int32 TasksSucceededCount,
    Int32 TasksFailedCount,
    Int32 TasksSkippedCount,
    DateTime LastUpdatedUtc)
{
    public Int64 TotalTokens => PromptTokens + CompletionTokens;
}

