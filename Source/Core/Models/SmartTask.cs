using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Core.Models;

public sealed class SmartTask : INotifyPropertyChanged
{
    private String id = String.Empty;
    private String intent = String.Empty;
    private SmartTaskTypeOptions type;
    private SmartTaskStateOptions state;
    private SmartTaskStrategyOptions? strategy;
    private ObservableCollection<SmartTask> subtasks;
    private SmartTaskExecutionContext? boundAssignmentTask;
    private String? parentId;
    private String? phase;
    private String stage = "Observation";
    private DateTime lastUpdatedUtc = DateTime.UtcNow;
    private Int32 depth;
    private Double workRetentionFraction;
    private Double delegationFraction;

    public SmartTask()
    {
        subtasks = new ObservableCollection<SmartTask>();
    }

    public String Id
    {
        get { return id; }
        set { SetProperty(ref id, value); }
    }

    public String Intent
    {
        get { return intent; }
        set { SetProperty(ref intent, value); }
    }

    public SmartTaskTypeOptions Type
    {
        get { return type; }
        set { SetProperty(ref type, value); }
    }

    public SmartTaskStateOptions State
    {
        get { return state; }
        set
        {
            if (SetProperty(ref state, value))
            {
                OnPropertyChanged(nameof(StateIcon));
                OnPropertyChanged(nameof(StateDisplay));
            }
        }
    }

    public SmartTaskStrategyOptions? Strategy
    {
        get { return strategy; }
        set
        {
            if (SetProperty(ref strategy, value))
            {
                OnPropertyChanged(nameof(StrategyDisplay));
            }
        }
    }

    public ObservableCollection<SmartTask> Subtasks
    {
        get { return subtasks; }
        set { SetProperty(ref subtasks, value ?? new ObservableCollection<SmartTask>()); }
    }

    public SmartTaskExecutionContext? BoundAssignmentTask
    {
        get { return boundAssignmentTask; }
        set { SetProperty(ref boundAssignmentTask, value); }
    }

    public String? ParentId
    {
        get { return parentId; }
        set { SetProperty(ref parentId, value); }
    }

    public String? Phase
    {
        get { return phase; }
        set { SetProperty(ref phase, value); }
    }

    public String Stage
    {
        get { return stage; }
        set { SetProperty(ref stage, value); }
    }

    public Int32 Depth
    {
        get { return depth; }
        set { SetProperty(ref depth, value); }
    }

    public Double WorkRetentionFraction
    {
        get { return workRetentionFraction; }
        set
        {
            if (SetProperty(ref workRetentionFraction, value))
            {
                OnPropertyChanged(nameof(WorkRetentionPercentDisplay));
                OnPropertyChanged(nameof(DelegationPercentDisplay));
            }
        }
    }

    public Double DelegationFraction
    {
        get { return delegationFraction; }
        set
        {
            if (SetProperty(ref delegationFraction, value))
            {
                OnPropertyChanged(nameof(DelegationPercentDisplay));
            }
        }
    }

    public String WorkRetentionPercentDisplay
    {
        get { return (workRetentionFraction * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + "%"; }
    }

    public String DelegationPercentDisplay
    {
        get { return (delegationFraction * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + "%"; }
    }

    public String StateIcon
    {
        get
        {
                
                return state switch
                {
                    SmartTaskStateOptions.Pending => "\u23F3",
                    SmartTaskStateOptions.Planning => "\u270D",
                    SmartTaskStateOptions.Executing => "\u2699",
                    SmartTaskStateOptions.Verifying => "\uD83D\uDD0D",
                    SmartTaskStateOptions.Succeeded => "\u2714",
                    SmartTaskStateOptions.Failed => "\u2716",
                    SmartTaskStateOptions.Skipped => "\u25CF",
                    _ => "\u25EF"
                };
        }
    }

    public String StateDisplay
    {
        get { return state.ToString(); }
    }

    public String StrategyDisplay
    {
        get { return strategy?.ToString() ?? "Pending"; }
    }

    public DateTime LastUpdatedUtc
    {
        get { return lastUpdatedUtc; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private Boolean SetProperty<T>(ref T field, T value, [CallerMemberName] String? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        lastUpdatedUtc = DateTime.UtcNow;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastUpdatedUtc)));
        return true;
    }

    private void OnPropertyChanged(String propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


