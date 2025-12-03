namespace AgentCommandEnvironment.Core.Events;

public sealed class SystemLogEventArgs : EventArgs
{
    public SystemLogEventArgs(String message)
    {
        Message = message;
    }

    public String Message { get; }
}

