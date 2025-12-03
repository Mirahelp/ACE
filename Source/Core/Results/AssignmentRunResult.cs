namespace AgentCommandEnvironment.Core.Results;

public sealed class AssignmentRunResult
{
    public Boolean Succeeded { get; }
    public Boolean Cancelled { get; }
    public String? FailureTitle { get; }
    public String? FailureMessage { get; }
    public String? FailureDetails { get; }

    private AssignmentRunResult(Boolean succeeded, Boolean cancelled, String? failureTitle, String? failureMessage, String? failureDetails)
    {
        Succeeded = succeeded;
        Cancelled = cancelled;
        FailureTitle = failureTitle;
        FailureMessage = failureMessage;
        FailureDetails = failureDetails;
    }

    public static AssignmentRunResult Success()
    {
        return new AssignmentRunResult(true, false, null, null, null);
    }

    public static AssignmentRunResult CancelledRun(String? failureReason)
    {
        return new AssignmentRunResult(false, true, "Assignment cancelled", failureReason, null);
    }

    public static AssignmentRunResult Failure(String? title, String? message, String? details)
    {
        return new AssignmentRunResult(false, false, title, message, details);
    }
}

