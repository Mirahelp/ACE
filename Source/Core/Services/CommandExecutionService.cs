using AgentCommandEnvironment.Core.Interfaces;
using AgentCommandEnvironment.Core.Models;
using AgentCommandEnvironment.Core.Results;
using System.Diagnostics;
using System.Text;

namespace AgentCommandEnvironment.Core.Services;

public sealed class CommandExecutionService : ICommandExecutionService
{
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MinimumCommandTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumCommandTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan BackgroundCommandStartupVerificationDelay = TimeSpan.FromSeconds(5);

    private readonly AssignmentLogService logService;
    private readonly List<BackgroundCommandHandleService> backgroundCommandHandles = new();
    private readonly object backgroundCommandHandlesLock = new();

    public CommandExecutionService(AssignmentLogService logService)
    {
        this.logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    public async Task<CommandExecutionResult> RunCommandAsync(
        SmartTaskExecutionContext taskItem,
        AgentCommandDescription commandDescription,
        String fileName,
        String arguments,
        String workingDirectory,
        CancellationToken cancellationToken)
    {
        Boolean runInBackground = commandDescription.RunInBackground == true;
        if (runInBackground)
        {
            return await RunBackgroundCommandAsync(taskItem, commandDescription, fileName, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        TimeSpan timeout = DetermineCommandTimeout(commandDescription);
        CommandExecutionResult firstResult = await RunCommandOnceAsync(fileName, arguments, workingDirectory, timeout, cancellationToken).ConfigureAwait(false);

        if (firstResult.ExitCode == 0 || firstResult.TimedOut)
        {
            return firstResult;
        }

        Boolean isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;

        if (isWindows && ShouldRetryWithShell(fileName, firstResult))
        {
            String combined = BuildCommandDisplayText(fileName, arguments);
            String shellArguments = "/C " + combined;
            CommandExecutionResult shellResult = await RunCommandOnceAsync("cmd.exe", shellArguments, workingDirectory, timeout, cancellationToken).ConfigureAwait(false);

            if (shellResult.ExitCode == 0)
            {
                logService.AppendSystemLog("Command succeeded when re-executed through shell: " + combined);
                return shellResult;
            }

            logService.AppendSystemLog("Shell execution also failed for command: " + combined);
            return shellResult;
        }

        return firstResult;
    }

    public void StopAllBackgroundCommands(String reason)
    {
        List<BackgroundCommandHandleService> snapshot;
        lock (backgroundCommandHandlesLock)
        {
            if (backgroundCommandHandles.Count == 0)
            {
                return;
            }

            snapshot = new List<BackgroundCommandHandleService>(backgroundCommandHandles);
        }

        for (Int32 index = 0; index < snapshot.Count; index++)
        {
            CleanupBackgroundCommand(snapshot[index], reason, terminateIfRunning: true);
        }
    }

    private Boolean ShouldRetryWithShell(String fileName, CommandExecutionResult commandExecutionResult)
    {
        if (commandExecutionResult == null)
        {
            return false;
        }

        if (commandExecutionResult.ExitCode != -1)
        {
            return false;
        }

        if (string.Equals(fileName, "cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private async Task<CommandExecutionResult> RunBackgroundCommandAsync(
        SmartTaskExecutionContext taskItem,
        AgentCommandDescription commandDescription,
        String fileName,
        String arguments,
        String workingDirectory,
        CancellationToken cancellationToken)
    {
        CommandExecutionResult result = new CommandExecutionResult();
        Process process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        StringBuilder capturedOutput = new StringBuilder();
        StringBuilder capturedError = new StringBuilder();

        void CaptureLine(StringBuilder builder, String? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            lock (builder)
            {
                const Int32 maxCaptureLength = 6000;
                if (builder.Length >= maxCaptureLength)
                {
                    return;
                }

                Int32 remaining = maxCaptureLength - builder.Length;
                String text = line!;
                if (text.Length > remaining)
                {
                    text = text.Substring(0, remaining);
                }

                builder.AppendLine(text);
            }
        }

        process.OutputDataReceived += (sender, args) => CaptureLine(capturedOutput, args.Data);
        process.ErrorDataReceived += (sender, args) => CaptureLine(capturedError, args.Data);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception exception)
        {
            process.Dispose();
            result.ExitCode = -1;
            result.StandardErrorText = exception.Message;
            result.StandardOutputText = string.Empty;
            return result;
        }

        Task exitTask = process.WaitForExitAsync();
        Task verificationDelayTask = Task.Delay(BackgroundCommandStartupVerificationDelay, cancellationToken);

        Task completedTask = await Task.WhenAny(exitTask, verificationDelayTask).ConfigureAwait(false);

        if (completedTask == exitTask || process.HasExited)
        {
            await exitTask.ConfigureAwait(false);
            TryCancelProcessReaders(process);
            result.ExitCode = process.ExitCode;
            result.StandardOutputText = capturedOutput.ToString();
            result.StandardErrorText = capturedError.ToString();
            process.Dispose();
            return result;
        }

        cancellationToken.ThrowIfCancellationRequested();

        BackgroundCommandHandleService handle = RegisterBackgroundCommand(process, taskItem, BuildCommandDisplayText(fileName, arguments), capturedOutput, capturedError);
        result.ExitCode = 0;
        result.RanInBackground = true;
        result.BackgroundProcessId = process.Id;
        result.StandardOutputText = "Command is running in background (PID " + process.Id + ").";
        result.StandardErrorText = string.Empty;
        return result;
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private static void TryCancelProcessReaders(Process process)
    {
        try
        {
            process.CancelOutputRead();
        }
        catch
        {
        }

        try
        {
            process.CancelErrorRead();
        }
        catch
        {
        }
    }

    private TimeSpan DetermineCommandTimeout(AgentCommandDescription commandDescription)
    {
        int? maxRunSeconds = commandDescription.MaxRunSeconds;
        if (maxRunSeconds.HasValue && maxRunSeconds.Value > 0)
        {
            double seconds = Math.Max(MinimumCommandTimeout.TotalSeconds, Math.Min(MaximumCommandTimeout.TotalSeconds, maxRunSeconds.Value));
            return TimeSpan.FromSeconds(seconds);
        }

        return DefaultCommandTimeout;
    }

    private static String BuildCommandDisplayText(String executable, String arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return executable;
        }

        return executable + " " + arguments;
    }

    private BackgroundCommandHandleService RegisterBackgroundCommand(
        Process process,
        SmartTaskExecutionContext taskItem,
        String description,
        StringBuilder capturedOutput,
        StringBuilder capturedError)
    {
        BackgroundCommandHandleService handle = new BackgroundCommandHandleService(process, taskItem, description, capturedOutput, capturedError);

        process.EnableRaisingEvents = true;
        process.Exited += (sender, args) =>
        {
            CleanupBackgroundCommand(handle, "Process exited", terminateIfRunning: false);
        };

        lock (backgroundCommandHandlesLock)
        {
            backgroundCommandHandles.Add(handle);
        }

        logService.AppendSystemLog("Background command running (PID " + process.Id + "): " + description);
        logService.AppendTaskLog(taskItem, "Background command running (PID " + process.Id + "). It will continue until it exits or the assignment is cancelled.");
        return handle;
    }

    private void CleanupBackgroundCommand(BackgroundCommandHandleService handle, String reason, Boolean terminateIfRunning)
    {
        Boolean removed;
        lock (backgroundCommandHandlesLock)
        {
            removed = backgroundCommandHandles.Remove(handle);
        }

        if (!removed)
        {
            return;
        }

        try
        {
            if (terminateIfRunning && !handle.Process.HasExited)
            {
                handle.Process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception)
        {
            logService.AppendSystemLog("Failed to stop background command (PID " + handle.Process.Id + "): " + exception.Message);
        }

        TryCancelProcessReaders(handle.Process);
        handle.Process.Dispose();

        String outputSnippet = handle.CapturedStandardOutput.Length > 0 ? TextUtilityService.BuildCompactSnippet(handle.CapturedStandardOutput.ToString()) : string.Empty;
        if (!string.IsNullOrWhiteSpace(outputSnippet))
        {
            logService.AppendTaskLog(handle.TaskItem, "Background command output snapshot:" + Environment.NewLine + outputSnippet);
        }

        logService.AppendSystemLog("Background command ended (PID " + handle.Process.Id + "): " + handle.Description + ". Reason: " + reason);
        logService.AppendTaskLog(handle.TaskItem, "Background command ended. Reason: " + reason);
    }

    private async Task<CommandExecutionResult> RunCommandOnceAsync(String fileName, String arguments, String workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        CommandExecutionResult result = new CommandExecutionResult();
        StringBuilder standardOutputBuilder = new StringBuilder();
        StringBuilder standardErrorBuilder = new StringBuilder();

        using Process process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception startException)
        {
            result.ExitCode = -1;
            result.StandardErrorText = startException.Message;
            result.StandardOutputText = string.Empty;
            return result;
        }

        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(() => TryTerminateProcess(process));
        using CancellationTokenSource timeoutCts = new CancellationTokenSource();
        Task timeoutTask = Task.Delay(timeout, timeoutCts.Token);
        Task exitTask = process.WaitForExitAsync();

        Task completedTask = await Task.WhenAny(exitTask, timeoutTask).ConfigureAwait(false);
        if (completedTask == timeoutTask)
        {
            result.TimedOut = true;
            TryTerminateProcess(process);
        }
        else
        {
            timeoutCts.Cancel();
        }

        await exitTask.ConfigureAwait(false);

        try
        {
            String standardOutputRemainder = await standardOutputTask.ConfigureAwait(false);
            if (!string.IsNullOrEmpty(standardOutputRemainder))
            {
                standardOutputBuilder.Append(standardOutputRemainder);
            }
        }
        catch
        {
        }

        try
        {
            String standardErrorRemainder = await standardErrorTask.ConfigureAwait(false);
            if (!string.IsNullOrEmpty(standardErrorRemainder))
            {
                standardErrorBuilder.Append(standardErrorRemainder);
            }
        }
        catch
        {
        }

        result.ExitCode = process.ExitCode;
        result.StandardOutputText = standardOutputBuilder.ToString();
        result.StandardErrorText = standardErrorBuilder.ToString();

        return result;
    }
}

