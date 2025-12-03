using AgentCommandEnvironment.Core.Controllers;
using AgentCommandEnvironment.Core.Interfaces;
using AgentCommandEnvironment.Core.Models;
using AgentCommandEnvironment.Core.Results;
using System.Globalization;

namespace AgentCommandEnvironment.Core.Services;

public sealed class AssignmentLogService
{
    private readonly AssignmentController assignmentController;
    private readonly IUiDispatcherService dispatcherService;

    public AssignmentLogService(AssignmentController assignmentController, IUiDispatcherService dispatcherService)
    {
        this.assignmentController = assignmentController ?? throw new ArgumentNullException(nameof(assignmentController));
        this.dispatcherService = dispatcherService ?? throw new ArgumentNullException(nameof(dispatcherService));
    }

    public void AppendSystemLog(String message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        assignmentController.AddSystemLogEntry(message);
    }

    public void AppendTaskLog(SmartTaskExecutionContext taskItem, String message)
    {
        if (taskItem == null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        dispatcherService.Invoke(() =>
        {
            DateTime timestamp = DateTime.Now;
            String timeText = timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            String prefix = "[" + timeText + "] ";
            String lineText = prefix + message;

            if (string.IsNullOrEmpty(taskItem.TaskLogText))
            {
                taskItem.TaskLogText = lineText;
            }
            else
            {
                taskItem.TaskLogText = taskItem.TaskLogText + Environment.NewLine + lineText;
            }
        });
    }

    public void AppendCommandOutput(SmartTaskExecutionContext taskContext, AgentCommandDescription commandDescription, CommandExecutionResult commandResult)
    {
        if (taskContext == null || commandDescription == null || commandResult == null)
        {
            return;
        }

        String executableText = commandDescription.Executable ?? string.Empty;
        String argumentsText = commandDescription.Arguments ?? string.Empty;
        String commandLine = executableText + (string.IsNullOrWhiteSpace(argumentsText) ? string.Empty : " " + argumentsText);

        AppendTaskLog(taskContext, "Command result: " + commandLine + " (exit code " + commandResult.ExitCode + ")");

        if (commandResult.TimedOut)
        {
            AppendTaskLog(taskContext, "Command timed out and was terminated early.");
        }

        if (commandResult.RanInBackground)
        {
            if (commandResult.BackgroundProcessId.HasValue)
            {
                AppendTaskLog(taskContext, "Command continues in background (PID " + commandResult.BackgroundProcessId.Value + ").");
            }
            else
            {
                AppendTaskLog(taskContext, "Command continues in background.");
            }
        }

        if (!string.IsNullOrWhiteSpace(commandResult.StandardOutputText))
        {
            AppendTaskLog(taskContext, "Standard output:\n" + commandResult.StandardOutputText.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(commandResult.StandardErrorText))
        {
            AppendTaskLog(taskContext, "Standard error:\n" + commandResult.StandardErrorText.TrimEnd());
        }
    }
}

