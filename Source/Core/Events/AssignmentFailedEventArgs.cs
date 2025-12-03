namespace AgentCommandEnvironment.Core.Events;

public sealed class AssignmentFailedEventArgs : EventArgs
{
    public AssignmentFailedEventArgs(String title, String message, String? details)
    {
        Title = title;
        Message = message;
        Details = details;
    }

    public String Title { get; }
    public String Message { get; }
    public String? Details { get; }
}

