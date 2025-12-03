using AgentCommandEnvironment.Core.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using System.ComponentModel;

namespace AgentCommandEnvironment.Presentation.Views;

internal sealed class TaskMonitorWindow : Window
{
    private readonly TextBox logTextBox;
    private readonly SmartTaskExecutionContext monitoredTaskItem;

    public SmartTaskExecutionContext MonitoredTaskItem => monitoredTaskItem;

    public TaskMonitorWindow(SmartTaskExecutionContext taskItemToMonitor)
    {
        monitoredTaskItem = taskItemToMonitor ?? throw new ArgumentNullException(nameof(taskItemToMonitor));

        Title = "Monitor Task " + monitoredTaskItem.TaskNumber + " - " + monitoredTaskItem.Label;
        Width = 720;
        Height = 420;
        MinWidth = 520;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = true;

        IBrush foregroundBrush = GetThemeBrush("ThemeForegroundBrush", Brushes.White);

        logTextBox = new TextBox
        {
            Margin = new Thickness(12),
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalContentAlignment = VerticalAlignment.Top,
            FontFamily = new FontFamily("Consolas, Segoe UI, monospace"),
            FontSize = 13,
            IsReadOnly = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = foregroundBrush,
            CaretBrush = foregroundBrush
        };

        ScrollViewer scrollViewer = new ScrollViewer
        {
            Content = logTextBox,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        Content = new Border
        {
            Padding = new Thickness(4),
            Child = scrollViewer
        };

        monitoredTaskItem.PropertyChanged += OnTaskItemPropertyChanged;

        UpdateLogTextFromTask();
    }

    protected override void OnClosed(EventArgs e)
    {
        monitoredTaskItem.PropertyChanged -= OnTaskItemPropertyChanged;
        base.OnClosed(e);
    }

    private void OnTaskItemPropertyChanged(Object? sender, PropertyChangedEventArgs e)
    {
        if (String.Equals(e.PropertyName, nameof(SmartTaskExecutionContext.TaskLogText), StringComparison.Ordinal))
        {
            UpdateLogTextFromTask();
        }
        else if (String.Equals(e.PropertyName, nameof(SmartTaskExecutionContext.Label), StringComparison.Ordinal) ||
                 String.Equals(e.PropertyName, nameof(SmartTaskExecutionContext.TaskNumber), StringComparison.Ordinal))
        {
            Title = "Monitor Task " + monitoredTaskItem.TaskNumber + " - " + monitoredTaskItem.Label;
        }
    }

    private void UpdateLogTextFromTask()
    {
        String text = monitoredTaskItem.TaskLogText ?? String.Empty;
        logTextBox.Text = text;
        logTextBox.CaretIndex = logTextBox.Text?.Length ?? 0;
    }

    private static IBrush GetThemeBrush(String resourceKey, IBrush fallback)
    {
        if (Application.Current != null && Application.Current.TryFindResource(resourceKey, out Object? resource) && resource is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }
}

