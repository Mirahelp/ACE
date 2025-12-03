using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace AgentCommandEnvironment.Presentation.Views;

public partial class PolicyWindow : Window
{
    public PolicyWindow()
    {
        InitializeComponent();

        if (Application.Current?.RequestedThemeVariant is ThemeVariant variant)
        {
            RequestedThemeVariant = variant;
        }
    }

    public PolicyWindow(String title,
        String description,
        String primaryButtonText,
        String? secondaryButtonText,
        String? commandText,
        Boolean showApprovalPrompt)
        : this()
    {
        Title = String.IsNullOrWhiteSpace(title) ? "Confirmation" : title;

        DescriptionTextBlock.Text = description ?? String.Empty;

        Boolean hasCommand = !String.IsNullOrWhiteSpace(commandText);
        CommandContainer.IsVisible = hasCommand;
        CommandTextBlock.Text = hasCommand ? commandText : String.Empty;

        QuestionTextBlock.IsVisible = hasCommand && showApprovalPrompt;

        PrimaryButton.Content = primaryButtonText;
        PrimaryButton.Click += (_, _) => Close(true);

        if (!String.IsNullOrWhiteSpace(secondaryButtonText))
        {
            SecondaryButton.Content = secondaryButtonText;
            SecondaryButton.IsVisible = true;
            SecondaryButton.Click += (_, _) => Close(false);
        }
        else
        {
            SecondaryButton.IsVisible = false;
        }
    }
}
