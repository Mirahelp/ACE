using AgentCommandEnvironment.Core.Models;
using AgentCommandEnvironment.Core.Results;

namespace AgentCommandEnvironment.Core.Interfaces;

public interface IAssignmentRuntimeService
{
    Task<AssignmentRunResult> RunAsync(AssignmentRunRequestModel request, CancellationToken cancellationToken);
    Boolean CaptureWorkspaceChangesAsSemanticFacts(SmartTask smartTask, String workspaceFullPath, String triggerDescription);
    Task<StructuredAgentResult?> RequestPlannerResponseAsync(PlannerRequestContext requestContext, CancellationToken cancellationToken);
    Task<StructuredRepairResult?> RequestRepairResponseAsync(SmartTaskExecutionContext failedTask, String? workspacePath, Boolean trackSmartTask, CancellationToken cancellationToken);
    Task<Boolean> TryHandleTerminalTaskFailureAsync(SmartTaskExecutionContext failedTask, String? workspacePath, FailureResolutionResult? callbacks, CancellationToken cancellationToken);
}


