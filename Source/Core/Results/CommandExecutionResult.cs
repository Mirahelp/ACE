namespace AgentCommandEnvironment.Core.Results;

public sealed class CommandExecutionResult
{
    public Int32 ExitCode { get; set; }
    public String StandardOutputText { get; set; } = String.Empty;
    public String StandardErrorText { get; set; } = String.Empty;
    public Boolean TimedOut { get; set; }
    public Boolean RanInBackground { get; set; }
    public Int32? BackgroundProcessId { get; set; }
}

