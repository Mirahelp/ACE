using AgentCommandEnvironment.Core.Models;
using System.Diagnostics;
using System.Text;

namespace AgentCommandEnvironment.Core.Services;

public sealed class BackgroundCommandHandleService
{
    public BackgroundCommandHandleService(Process process, SmartTaskExecutionContext taskItem, String description, StringBuilder capturedOutput, StringBuilder capturedError)
    {
        Process = process;
        TaskItem = taskItem;
        Description = description;
        CapturedStandardOutput = capturedOutput;
        CapturedStandardError = capturedError;
    }

    public Process Process { get; }
    public SmartTaskExecutionContext TaskItem { get; }
    public String Description { get; }
    public StringBuilder CapturedStandardOutput { get; }
    public StringBuilder CapturedStandardError { get; }
}

