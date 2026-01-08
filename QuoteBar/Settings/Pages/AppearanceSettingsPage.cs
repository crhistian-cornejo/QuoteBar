using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using QuoteBar.Core.Services;
using QuoteBar.Settings.Controls;
using QuoteBar.Settings.Helpers;

namespace QuoteBar.Settings.Pages;

/// <summary>
/// Appearance settings page - theme, accent color, compact mode, tray badge
/// </summary>
public class AppearanceSettingsPage : ISettingsPage
{
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;
    private ScrollViewer? _content;

    // UI controls
    private ComboBox? _themeCombo;
    private ToggleSwitch? _accentToggle;
    private ToggleSwitch? _compactToggle;
    private ToggleSwitch? _iconsToggle;
    private ToggleSwitch? _trayBadgeToggle;
    private StackPanel? _trayBadgeProvidersPanel;

    /// <summary>
    /// Event to notify parent when theme changes
    /// </summary>
    public event Action<ElementTheme>? ThemeChanged;

    public FrameworkElement Content => _content ??= CreateContent();

    private ScrollViewer CreateContent()
    {
        var scroll = new ScrollViewer
        {
            Padding = new Thickness(28, 20, 28, 24),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(SettingCard.CreateHeader("Appearance"));

        // Theme selection
        _themeCombo = new ComboBox { Width = 180 };
        _themeCombo.Items.Add("System default");
        _themeCombo.Items.Add("Light");
        _themeCombo.Items.Add("Dark");
        _themeCombo.SelectedIndex = (int)_settings.Settings.Theme;
        _themeCombo.SelectionChanged += (s, e) =>
        {
            var newTheme = (ThemeMode)_themeCombo.SelectedIndex;
            _theme.SetTheme(newTheme);
            ThemeChanged?.Invoke(_theme.GetEffectiveTheme());
        };
        stack.Children.Add(SettingCard.Create(
            "Theme",
            "Choose your preferred color scheme",
            _themeCombo));

        // Use system accent color
        _accentToggle = SettingCard.CreateToggleSwitch(_settings.Settings.UseSystemAccentColor);
        _accentToggle.Toggled += (s, e) =>
        {
            _settings.Settings.UseSystemAccentColor = _accentToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "Use system accent color",
            "Match the Windows accent color for highlights",
            _accentToggle));

        // Compact mode
        _compactToggle = SettingCard.CreateToggleSwitch(_settings.Settings.CompactMode);
        _compactToggle.Toggled += (s, e) =>
        {
            _settings.Settings.CompactMode = _compactToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "Compact mode",
            "Use a smaller popup with less information",
            _compactToggle));

        // Show provider icons
        _iconsToggle = SettingCard.CreateToggleSwitch(_settings.Settings.ShowProviderIcons);
        _iconsToggle.Toggled += (s, e) =>
        {
            _settings.Settings.ShowProviderIcons = _iconsToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "Show provider icons",
            "Display icons next to provider names in tabs",
            _iconsToggle));

        // Tray Badge section
        stack.Children.Add(new TextBlock
        {
            Text = "System Tray Badge",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 8)
        });

        // Tray Badge toggle
        _trayBadgeToggle = SettingCard.CreateToggleSwitch(_settings.Settings.TrayBadgeEnabled);
        _trayBadgeToggle.Toggled += (s, e) =>
        {
            _settings.Settings.TrayBadgeEnabled = _trayBadgeToggle.IsOn;
            _settings.Save();
            UpdateTrayBadgeProvidersVisibility();
        };
        stack.Children.Add(SettingCard.Create(
            "Show usage badges in tray",
            "Replace tray icon with usage percentages (remaining %)",
            _trayBadgeToggle));

        // Tray Badge providers selection
        _trayBadgeProvidersPanel = CreateTrayBadgeProvidersPanel();
        stack.Children.Add(_trayBadgeProvidersPanel);
        UpdateTrayBadgeProvidersVisibility();

        scroll.Content = stack;
        return scroll;
    }

    private StackPanel CreateTrayBadgeProvidersPanel()
    {
        var panel = new StackPanel { Spacing = 8 };

        var card = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1)
        };

        var innerStack = new StackPanel { Spacing = 12 };

        innerStack.Children.Add(new TextBlock
        {
            Text = "Select provider for tray badge",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        innerStack.Children.Add(new TextBlock
        {
            Text = "Shows remaining % (100 - used) for the selected provider",
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            Margin = new Thickness(0, 0, 0, 8)
        });

        var providers = new[]
        {
            ("Claude", "claude", "#D97757"),
            ("Gemini", "gemini", "#4285F4"),
            ("Copilot", "copilot", "#24292F"),
            ("Cursor", "cursor", "#007AFF"),
            ("Codex", "codex", "#7C3AED"),
            ("Droid", "droid", "#EE6018"),
            ("Antigravity", "antigravity", "#FF6B6B"),
            ("z.ai", "zai", "#E85A6A"),
            ("MiniMax", "minimax", "#E2167E")
        };

        foreach (var (displayName, id, color) in providers)
        {
            var isSelected = _settings.Settings.TrayBadgeProvider == id;
            innerStack.Children.Add(CreateTrayBadgeProviderRadio(displayName, id, color, isSelected));
        }

        card.Child = innerStack;
        panel.Children.Add(card);
        return panel;
    }

    private Grid CreateTrayBadgeProviderRadio(string displayName, string providerId, string colorHex, bool isChecked)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var radio = new RadioButton
        {
            IsChecked = isChecked,
            Tag = providerId,
            GroupName = "TrayBadgeProvider",
            VerticalAlignment = VerticalAlignment.Center
        };
        radio.Checked += OnTrayBadgeProviderRadioChanged;
        Grid.SetColumn(radio, 0);

        // Provider icon
        FrameworkElement iconElement;
        var svgFileName = ProviderIconHelper.GetProviderSvgFileName(providerId);
        if (!string.IsNullOrEmpty(svgFileName))
        {
            var svgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", svgFileName);
            if (System.IO.File.Exists(svgPath))
            {
                iconElement = new Image
                {
                    Width = 16,
                    Height = 16,
                    Source = new SvgImageSource(new Uri(svgPath, UriKind.Absolute)),
                    Margin = new Thickness(4, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                iconElement = CreateColorDot(colorHex);
            }
        }
        else
        {
            iconElement = CreateColorDot(colorHex);
        }
        Grid.SetColumn(iconElement, 1);

        var nameText = new TextBlock
        {
            Text = displayName,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 2);

        grid.Children.Add(radio);
        grid.Children.Add(iconElement);
        grid.Children.Add(nameText);

        return grid;
    }

    private Ellipse CreateColorDot(string colorHex)
    {
        return new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(ProviderIconHelper.ParseColor(colorHex)),
            Margin = new Thickness(4, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void OnTrayBadgeProviderRadioChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radio) return;
        var providerId = radio.Tag?.ToString();
        if (string.IsNullOrEmpty(providerId)) return;

        _settings.Settings.TrayBadgeProvider = providerId;
        _settings.Save();
    }

    private void UpdateTrayBadgeProvidersVisibility()
    {
        if (_trayBadgeProvidersPanel != null)
        {
            _trayBadgeProvidersPanel.Visibility = _settings.Settings.TrayBadgeEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    public void OnThemeChanged()
    {
        _content = null; // Force recreation on next access
    }
}
