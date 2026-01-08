using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuoteBar.Core.Providers;
using QuoteBar.Core.Services;
using QuoteBar.Settings.Controls;
using QuoteBar.Settings.Helpers;

namespace QuoteBar.Settings.Pages;

/// <summary>
/// Provider order settings page - reorder providers in popup
/// </summary>
public class ProviderOrderSettingsPage : ISettingsPage
{
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly ProviderRegistry _registry = ProviderRegistry.Instance;
    private ScrollViewer? _content;
    private StackPanel? _providersPanel;

    public FrameworkElement Content => _content ??= CreateContent();

    private ScrollViewer CreateContent()
    {
        DebugLogger.Log("ProviderOrderSettingsPage", "CreateContent START");
        try
        {
            var scroll = new ScrollViewer
            {
                Padding = new Thickness(28, 20, 28, 24),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var stack = new StackPanel { Spacing = 16 };

            stack.Children.Add(SettingCard.CreateHeader("Provider Order"));
            stack.Children.Add(SettingCard.CreateSubheader("Drag and drop to reorder providers in the popup"));

            // Provider list container
            var listCard = new Border
            {
                Background = new SolidColorBrush(_theme.CardColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(_theme.BorderColor),
                BorderThickness = new Thickness(1)
            };

            _providersPanel = new StackPanel { Spacing = 4 };
            RefreshProviderList();

            listCard.Child = _providersPanel;
            stack.Children.Add(listCard);

            // Instructions
            var infoCard = new Border
            {
                Background = new SolidColorBrush(_theme.CardColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                BorderBrush = new SolidColorBrush(_theme.BorderColor),
                BorderThickness = new Thickness(1)
            };

            var infoText = new TextBlock
            {
                Text = "Providers will appear in the popup in the order shown above. Drag items to reorder. Click 'Reset to Default' to restore alphabetical order.",
                FontSize = 13,
                Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
                TextWrapping = TextWrapping.Wrap
            };

            infoCard.Child = infoText;
            stack.Children.Add(infoCard);

            // Reset button
            var resetButton = new Button
            {
                Content = "Reset to Default",
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            };

            resetButton.Click += (s, e) =>
            {
                _settings.Settings.ProviderOrder.Clear();
                _settings.Save();
                RefreshProviderList();
            };

            stack.Children.Add(resetButton);

            scroll.Content = stack;
            DebugLogger.Log("ProviderOrderSettingsPage", "CreateContent DONE");
            return scroll;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProviderOrderSettingsPage", "CreateContent CRASHED", ex);
            var errorScroll = new ScrollViewer { Padding = new Thickness(24) };
            var errorStack = new StackPanel();
            errorStack.Children.Add(new TextBlock
            {
                Text = $"Error loading provider order: {ex.Message}",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
                TextWrapping = TextWrapping.Wrap
            });
            errorScroll.Content = errorStack;
            return errorScroll;
        }
    }

    private void RefreshProviderList()
    {
        if (_providersPanel == null) return;

        _providersPanel.Children.Clear();

        var providers = _registry.GetAllProviders().ToList();
        foreach (var provider in providers)
        {
            var providerRow = CreateProviderRow(provider);
            _providersPanel.Children.Add(providerRow);
        }
    }

    private Border CreateProviderRow(IProviderDescriptor provider)
    {
        var border = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(_theme.SurfaceColor),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 2, 0, 2)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Drag handle icon
        var dragIcon = new FontIcon
        {
            Glyph = "\uE712", // Grip icon
            FontSize = 16,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dragIcon, 0);

        // Provider Icon
        var colorHex = GetProviderColor(provider.Id);
        var iconElement = ProviderIconHelper.CreateProviderImage(provider.Id, size: 16, forIconWithBackground: true);
        var iconBorder = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(ProviderIconHelper.ParseColor(colorHex)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            Child = iconElement ?? (FrameworkElement)ProviderIconHelper.CreateProviderInitial(provider.DisplayName, 10)
        };
        Grid.SetColumn(iconBorder, 1);

        // Provider name
        var nameText = new TextBlock
        {
            Text = provider.DisplayName,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = new SolidColorBrush(_theme.TextColor),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 2);

        // Move buttons container
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4
        };

        var moveUpButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE74B", FontSize = 12 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0)
        };
        ToolTipService.SetToolTip(moveUpButton, "Move up");

        var moveDownButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0)
        };
        ToolTipService.SetToolTip(moveDownButton, "Move down");

        moveUpButton.Click += (s, e) => MoveProvider(provider.Id, -1);
        moveDownButton.Click += (s, e) => MoveProvider(provider.Id, 1);

        buttonPanel.Children.Add(moveUpButton);
        buttonPanel.Children.Add(moveDownButton);
        Grid.SetColumn(buttonPanel, 3);

        grid.Children.Add(dragIcon);
        grid.Children.Add(iconBorder);
        grid.Children.Add(nameText);
        grid.Children.Add(buttonPanel);

        border.Child = grid;
        return border;
    }

    private void MoveProvider(string providerId, int direction)
    {
        var currentOrder = _settings.Settings.ProviderOrder;

        if (currentOrder.Count == 0)
        {
            currentOrder = _registry.GetAllProviders().Select(p => p.Id).ToList();
        }

        var currentIndex = currentOrder.IndexOf(providerId);
        if (currentIndex == -1) return;

        var newIndex = currentIndex + direction;
        if (newIndex < 0 || newIndex >= currentOrder.Count) return;

        currentOrder.RemoveAt(currentIndex);
        currentOrder.Insert(newIndex, providerId);

        _settings.Settings.ProviderOrder = currentOrder;
        _settings.Save();

        RefreshProviderList();
    }

    public void OnThemeChanged()
    {
        _content = CreateContent();
    }

    private string GetProviderColor(string providerId)
    {
        return providerId.ToLower() switch
        {
            "antigravity" => "#FF6B6B",
            "augment" => "#3C3C3C",
            "claude" => "#D97757",
            "codex" => "#7C3AED",
            "copilot" => "#24292F",
            "cursor" => "#007AFF",
            "droid" => "#EE6018",
            "gemini" => "#4285F4",
            "minimax" => "#E2167E",
            "zai" => "#E85A6A",
            _ => "#808080"
        };
    }
}
