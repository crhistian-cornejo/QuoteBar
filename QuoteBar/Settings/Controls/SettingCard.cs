using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuoteBar.Core.Services;

namespace QuoteBar.Settings.Controls;

/// <summary>
/// Reusable setting card control with title, description, and control
/// Follows WinUI 3 design patterns with proper accessibility support
/// </summary>
public static class SettingCard
{
    /// <summary>
    /// Create a setting card with title, description, and control
    /// </summary>
    public static Border Create(string title, string description, FrameworkElement control)
    {
        var theme = ThemeService.Instance;

        var card = new Border
        {
            Background = new SolidColorBrush(theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(20, 16, 20, 16),
            Margin = new Thickness(0, 0, 0, 8),
            BorderBrush = new SolidColorBrush(theme.BorderColor),
            BorderThickness = new Thickness(1)
        };

        // Set accessibility properties for the card
        AutomationProperties.SetName(card, title);
        AutomationProperties.SetHelpText(card, description);

        var grid = new Grid
        {
            MinHeight = 40
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 24, 0) // Space before control
        };

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal
        };
        AutomationProperties.SetHeadingLevel(titleBlock, AutomationHeadingLevel.Level3);
        textStack.Children.Add(titleBlock);

        // Improved contrast - removed Opacity reduction
        var descBlock = new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = new SolidColorBrush(theme.SecondaryTextColor),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
            // Removed Opacity = 0.7 for better contrast
        };
        textStack.Children.Add(descBlock);
        Grid.SetColumn(textStack, 0);

        // Wrap control in a container for consistent alignment
        var controlContainer = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 60 // Ensure consistent width for toggles
        };
        controlContainer.Children.Add(control);
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;

        // Set accessibility label on control
        if (string.IsNullOrEmpty(AutomationProperties.GetName(control)))
        {
            AutomationProperties.SetName(control, title);
        }
        AutomationProperties.SetLabeledBy(control, titleBlock);

        Grid.SetColumn(controlContainer, 1);

        grid.Children.Add(textStack);
        grid.Children.Add(controlContainer);
        card.Child = grid;

        return card;
    }

    /// <summary>
    /// Create a toggle switch with proper alignment and accessibility
    /// </summary>
    public static ToggleSwitch CreateToggleSwitch(bool isOn, string? accessibleName = null)
    {
        var toggle = new ToggleSwitch
        {
            IsOn = isOn,
            OnContent = null,
            OffContent = null,
            MinWidth = 45,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        if (!string.IsNullOrEmpty(accessibleName))
        {
            AutomationProperties.SetName(toggle, accessibleName);
        }

        return toggle;
    }

    /// <summary>
    /// Create a section header with proper heading level for accessibility
    /// </summary>
    public static TextBlock CreateHeader(string text, double topMargin = 0)
    {
        var header = new TextBlock
        {
            Text = text,
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, topMargin, 0, 16)
        };

        // Mark as heading for screen readers
        AutomationProperties.SetHeadingLevel(header, AutomationHeadingLevel.Level1);

        return header;
    }

    /// <summary>
    /// Create a subheader/description text with proper contrast
    /// </summary>
    public static TextBlock CreateSubheader(string text)
    {
        var theme = ThemeService.Instance;
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = new SolidColorBrush(theme.SecondaryTextColor),
            Margin = new Thickness(0, 0, 0, 16),
            TextWrapping = TextWrapping.Wrap
            // Removed Opacity = 0.8 for better contrast compliance
        };
    }

    /// <summary>
    /// Create a section subheader with heading level
    /// </summary>
    public static TextBlock CreateSectionHeader(string text, double topMargin = 16)
    {
        var theme = ThemeService.Instance;
        var header = new TextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, topMargin, 0, 8)
        };

        AutomationProperties.SetHeadingLevel(header, AutomationHeadingLevel.Level2);

        return header;
    }
}
