using System;
using AgentCommandEnvironment.Core.Models;
using Avalonia.Controls;

namespace AgentCommandEnvironment.Presentation.Views;

public partial class SmartTaskWindow : Window
{
    public SmartTaskWindow()
    {
        InitializeComponent();
    }

    public SmartTaskWindow(SmartTask task) : this()
    {
        DataContext = task ?? throw new ArgumentNullException(nameof(task));
    }
}
