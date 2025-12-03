using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Core.Models;

public sealed class SmartTaskExecutionContext : INotifyPropertyChanged
{
    private Int32 taskNumber;
    private Int32 creationOrder;
    private String label = String.Empty;
    private String type = String.Empty;
    private String status = String.Empty;
    private String statusIcon = String.Empty;
    private AssignmentTaskStatusOptions statusKind;
    private DateTime createdAt;
    private DateTime? startedAt;
    private DateTime? completedAt;
    private String? lastResultText;
    private String agentTaskId = String.Empty;
    private List<AgentCommandDescription> associatedCommands = new List<AgentCommandDescription>();
    private Int32 attemptCount;
    private Int32 maxRepairAttempts;
    private String taskLogText = String.Empty;
    private List<String> dependencies = new List<String>();
    private String? parentTaskId;
    private String? taskContext;
    private String? aggregatedContextSnapshot;
    private readonly List<String> repairAttemptHistory = new List<String>();
    private readonly List<String> producedContextEntries = new List<String>();
    private String priority = "Normal";
    private Int32 priorityScore = 2;
    private String? phase;
    private List<String> contextTags = new List<String>();
    private Boolean allowsDependentsToProceed;
    private Boolean requiresCommandExecution;
    private Boolean hasScheduledRepairTask;

    public Int32 TaskNumber
    {
        get { return taskNumber; }
        set
        {
            if (taskNumber != value)
            {
                taskNumber = value;
                OnPropertyChanged();
            }
        }
    }

    public Int32 CreationOrder
    {
        get { return creationOrder; }
    }

    internal void SetCreationOrder(Int32 value)
    {
        if (creationOrder != value)
        {
            creationOrder = value;
            OnPropertyChanged(nameof(CreationOrder));
        }
    }

    public String Label
    {
        get { return label; }
        set
        {
            if (!String.Equals(label, value, StringComparison.Ordinal))
            {
                label = value;
                OnPropertyChanged();
            }
        }
    }

    public String Type
    {
        get { return type; }
        set
        {
            String normalized = NormalizeTaskType(value);
            if (!String.Equals(type, normalized, StringComparison.Ordinal))
            {
                type = normalized;
                OnPropertyChanged();
            }
        }
    }

    public String Status
    {
        get { return status; }
        private set
        {
            if (!String.Equals(status, value, StringComparison.Ordinal))
            {
                status = value;
                OnPropertyChanged();
            }
        }
    }

    public String StatusIcon
    {
        get { return statusIcon; }
        private set
        {
            if (!String.Equals(statusIcon, value, StringComparison.Ordinal))
            {
                statusIcon = value;
                OnPropertyChanged();
            }
        }
    }

    public AssignmentTaskStatusOptions StatusKind
    {
        get { return statusKind; }
        private set
        {
            if (statusKind != value)
            {
                statusKind = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime CreatedAt
    {
        get { return createdAt; }
        set
        {
            if (createdAt != value)
            {
                createdAt = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? StartedAt
    {
        get { return startedAt; }
        set
        {
            if (startedAt != value)
            {
                startedAt = value;
                OnPropertyChanged();
            }
        }
    }

    public DateTime? CompletedAt
    {
        get { return completedAt; }
        set
        {
            if (completedAt != value)
            {
                completedAt = value;
                OnPropertyChanged();
            }
        }
    }

    public String? LastResultText
    {
        get { return lastResultText; }
        set
        {
            if (!String.Equals(lastResultText, value, StringComparison.Ordinal))
            {
                lastResultText = value;
                OnPropertyChanged();
            }
        }
    }

    public String AgentTaskId
    {
        get { return agentTaskId; }
        set
        {
            if (!String.Equals(agentTaskId, value, StringComparison.Ordinal))
            {
                agentTaskId = value;
                OnPropertyChanged();
            }
        }
    }

    public List<AgentCommandDescription> AssociatedCommands
    {
        get { return associatedCommands; }
        set
        {
            associatedCommands = value ?? new List<AgentCommandDescription>();
            OnPropertyChanged();
        }
    }

    public List<String> Dependencies
    {
        get { return dependencies; }
        set
        {
            dependencies = value ?? new List<String>();
            OnPropertyChanged();
        }
    }

    public String? ParentTaskId
    {
        get { return parentTaskId; }
        set
        {
            if (!String.Equals(parentTaskId, value, StringComparison.Ordinal))
            {
                parentTaskId = value;
                OnPropertyChanged();
            }
        }
    }

    public String? TaskContext
    {
        get { return taskContext; }
        set
        {
            if (!String.Equals(taskContext, value, StringComparison.Ordinal))
            {
                taskContext = value;
                OnPropertyChanged();
            }
        }
    }

    public String? AggregatedContextSnapshot
    {
        get { return aggregatedContextSnapshot; }
        set
        {
            if (!String.Equals(aggregatedContextSnapshot, value, StringComparison.Ordinal))
            {
                aggregatedContextSnapshot = value;
                OnPropertyChanged();
            }
        }
    }

    public IReadOnlyList<String> RepairAttemptHistory
    {
        get { return repairAttemptHistory; }
    }

    public IReadOnlyList<String> ProducedContextEntries
    {
        get { return producedContextEntries; }
    }

    public String Priority
    {
        get { return priority; }
        set
        {
            String normalized = NormalizePriority(value);
            if (!String.Equals(priority, normalized, StringComparison.Ordinal))
            {
                priority = normalized;
                priorityScore = GetPriorityScore(normalized);
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriorityScore));
            }
        }
    }

    public Int32 PriorityScore
    {
        get { return priorityScore; }
    }

    public String? Phase
    {
        get { return phase; }
        set
        {
            if (!String.Equals(phase, value, StringComparison.Ordinal))
            {
                phase = value;
                OnPropertyChanged();
            }
        }
    }

    public List<String> ContextTags
    {
        get { return contextTags; }
        set
        {
            contextTags = value ?? new List<String>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContextTagsDisplay));
        }
    }

    public Boolean AllowsDependentsToProceed
    {
        get { return allowsDependentsToProceed; }
        set
        {
            if (allowsDependentsToProceed != value)
            {
                allowsDependentsToProceed = value;
                OnPropertyChanged();
            }
        }
    }

    public Boolean RequiresCommandExecution
    {
        get { return requiresCommandExecution; }
        set
        {
            if (requiresCommandExecution != value)
            {
                requiresCommandExecution = value;
                OnPropertyChanged();
            }
        }
    }

    public Boolean HasScheduledRepairTask
    {
        get { return hasScheduledRepairTask; }
        set
        {
            if (hasScheduledRepairTask != value)
            {
                hasScheduledRepairTask = value;
                OnPropertyChanged();
            }
        }
    }

    public String ContextTagsDisplay
    {
        get
        {
            if (contextTags == null || contextTags.Count == 0)
            {
                return String.Empty;
            }

            return String.Join(", ", contextTags);
        }
    }

    public Int32 AttemptCount
    {
        get { return attemptCount; }
        set
        {
            if (attemptCount != value)
            {
                attemptCount = value;
                OnPropertyChanged();
            }
        }
    }

    public Int32 MaxRepairAttempts
    {
        get { return maxRepairAttempts; }
        set
        {
            if (maxRepairAttempts != value)
            {
                maxRepairAttempts = value;
                OnPropertyChanged();
            }
        }
    }

    public String TaskLogText
    {
        get { return taskLogText; }
        set
        {
            if (!String.Equals(taskLogText, value, StringComparison.Ordinal))
            {
                taskLogText = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void RecordRepairHistoryEntry(String entry)
    {
        if (String.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        String trimmed = entry.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        repairAttemptHistory.Add(trimmed);
        OnPropertyChanged(nameof(RepairAttemptHistory));
    }

    public void AppendContextEntry(String entry)
    {
        if (String.IsNullOrWhiteSpace(entry))
        {
            return;
        }

        String trimmed = entry.Trim();
        Int32 maxLength = 4000;
        if (trimmed.Length > maxLength)
        {
            trimmed = trimmed.Substring(0, maxLength);
        }

        producedContextEntries.Add(trimmed);
        OnPropertyChanged(nameof(ProducedContextEntries));
    }

    public void SetContextTags(IEnumerable<String>? tags)
    {
        if (tags == null)
        {
            ContextTags = new List<String>();
            return;
        }

        List<String> normalized = new List<String>();
        foreach (String tag in tags)
        {
            if (String.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            normalized.Add(tag.Trim());
        }

        ContextTags = normalized;
    }

    private static String NormalizePriority(String? value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return "Normal";
        }

        String trimmed = value.Trim();
        String lower = trimmed.ToLowerInvariant();
        if (lower == "critical")
        {
            return "Critical";
        }
        if (lower == "high")
        {
            return "High";
        }
        if (lower == "medium")
        {
            return "Medium";
        }
        if (lower == "low")
        {
            return "Low";
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
    }

    private static Int32 GetPriorityScore(String normalizedPriority)
    {
        if (String.Equals(normalizedPriority, "Critical", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }
        if (String.Equals(normalizedPriority, "High", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }
        if (String.Equals(normalizedPriority, "Medium", StringComparison.OrdinalIgnoreCase) ||
            String.Equals(normalizedPriority, "Normal", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }
        if (String.Equals(normalizedPriority, "Low", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 2;
    }

    private static String NormalizeTaskType(String? value)
    {
        if (String.IsNullOrWhiteSpace(value))
        {
            return String.Empty;
        }

        String trimmed = value.Trim();
        if (trimmed.Length == 1)
        {
            return trimmed.ToUpperInvariant();
        }

        Char firstChar = Char.ToUpperInvariant(trimmed[0]);
        String remaining = trimmed.Substring(1);
        return firstChar + remaining;
    }

    public void SetStatus(AssignmentTaskStatusOptions newStatusKind)
    {
        StatusKind = newStatusKind;

        if (newStatusKind == AssignmentTaskStatusOptions.Planned)
        {
            Status = "Planned";
            StatusIcon = "\U0001F557";
        }
        else if (newStatusKind == AssignmentTaskStatusOptions.PendingApproval)
        {
            Status = "Pending";
            StatusIcon = "\u23F3";
        }
        else if (newStatusKind == AssignmentTaskStatusOptions.InProgress)
        {
            Status = "In progress";
            StatusIcon = "\u231B";
        }
        else if (newStatusKind == AssignmentTaskStatusOptions.Succeeded)
        {
            Status = "Completed";
            StatusIcon = "\u2714";
        }
        else if (newStatusKind == AssignmentTaskStatusOptions.Failed)
        {
            Status = "Failed";
            StatusIcon = "\u2716";
        }
        else if (newStatusKind == AssignmentTaskStatusOptions.Skipped)
        {
            Status = "Skipped";
            StatusIcon = "\u25CF";
        }
    }

    private void OnPropertyChanged([CallerMemberName] String? propertyName = null)
    {
        PropertyChangedEventHandler? handler = PropertyChanged;
        if (handler != null)
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}


