using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace AgentCommandEnvironment.Presentation.Views;

internal sealed class AssignmentFailedDialog : Window
{
    public AssignmentFailedDialog(String titleText, String messageText, String detailsText)
    {
        Title = titleText;
        Width = 560;
        Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        IBrush borderBrush = GetThemeBrush("ThemeBorderBrush", new SolidColorBrush(Color.FromUInt32(0xA0D83C3C)));
        IBrush surfaceBrush = GetThemeBrush("ThemeControlLowBrush", new SolidColorBrush(Color.FromRgb(255, 250, 250)));
        IBrush foregroundBrush = GetThemeBrush("ThemeForegroundBrush", Brushes.Black);
        IBrush secondaryForegroundBrush = GetThemeBrush("ThemeForegroundLowBrush", new SolidColorBrush(Color.FromRgb(68, 68, 68)));

        Border outerBorder = new Border
        {
            Margin = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = borderBrush,
            Background = surfaceBrush
        };

        Grid rootGrid = new Grid { Margin = new Thickness(16) };
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        StackPanel headerPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 8)
        };

        TextBlock titleBlock = new TextBlock
        {
            Text = titleText,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = foregroundBrush
        };

        TextBlock messageBlock = new TextBlock
        {
            Text = messageText,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = secondaryForegroundBrush
        };

        headerPanel.Children.Add(titleBlock);
        headerPanel.Children.Add(messageBlock);
        Grid.SetRow(headerPanel, 0);

        TextBox detailsTextBox = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 8),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            IsReadOnly = true,
            BorderThickness = new Thickness(1),
            Background = GetThemeBrush("ThemeControlMidBrush", Brushes.White),
            Foreground = foregroundBrush,
            Text = detailsText ?? String.Empty
        };

        ScrollViewer detailsContainer = new ScrollViewer
        {
            Content = detailsTextBox,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        if (String.IsNullOrWhiteSpace(detailsTextBox.Text))
        {
            detailsContainer.IsVisible = false;
        }

        Grid.SetRow(detailsContainer, 1);

        StackPanel buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button okButton = new Button
        {
            Content = "Close",
            MinWidth = 80,
            Margin = new Thickness(0, 8, 0, 0)
        };
        okButton.Click += (_, _) => Close(true);
        buttonPanel.Children.Add(okButton);
        Grid.SetRow(buttonPanel, 2);

        rootGrid.Children.Add(headerPanel);
        rootGrid.Children.Add(detailsContainer);
        rootGrid.Children.Add(buttonPanel);

        outerBorder.Child = rootGrid;
        Content = outerBorder;
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

