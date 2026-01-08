using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NativeBar.WinUI.Core.Services;
using NativeBar.WinUI.Settings.Controls;

namespace NativeBar.WinUI.Settings.Pages;

/// <summary>
/// General settings page - startup, refresh interval, hover delay, keyboard shortcuts
/// </summary>
public class GeneralSettingsPage : ISettingsPage
{
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;
    private ScrollViewer _content = null!;

    // UI controls
    private ToggleSwitch? _startupToggle;
    private ToggleSwitch? _autoRefreshToggle;
    private ComboBox? _intervalCombo;
    private Slider? _hoverSlider;
    private ToggleSwitch? _autoUpdateToggle;
    private ToggleSwitch? _hotkeyToggle;
    private ComboBox? _hotkeyCombo;

    public FrameworkElement Content => _content ??= CreateContent();

    private ScrollViewer CreateContent()
    {
        DebugLogger.Log("GeneralSettingsPage", "CreateContent START");
        try
        {
            var scroll = new ScrollViewer
            {
                Padding = new Thickness(28, 20, 28, 24),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var stack = new StackPanel { Spacing = 12 };

            stack.Children.Add(SettingCard.CreateHeader("General Settings"));

            // Start at login
            var isStartupEnabled = StartupService.IsStartupEnabled();
            _startupToggle = SettingCard.CreateToggleSwitch(isStartupEnabled);
            _startupToggle.Toggled += async (s, e) =>
            {
                var success = StartupService.SetStartupEnabled(_startupToggle.IsOn);
                _settings.Settings.StartAtLogin = _startupToggle.IsOn;
                _settings.Save();

                if (!success && _content.XamlRoot != null)
                {
                    var dialog = new ContentDialog
                    {
                        Title = "Error",
                        Content = "Failed to update startup setting. You may need to run as administrator.",
                        CloseButtonText = "OK",
                        XamlRoot = _content.XamlRoot
                    };
                    await dialog.ShowAsync();
                    _startupToggle.IsOn = !_startupToggle.IsOn;
                }
            };
            stack.Children.Add(SettingCard.Create(
                "Start at login",
                "Automatically start QuoteBar when you log in to Windows",
                _startupToggle));

        // Refresh interval (initialize first so it can be referenced by the toggle)
        _intervalCombo = new ComboBox { Width = 150 };
        _intervalCombo.Items.Add("1 minute");
        _intervalCombo.Items.Add("5 minutes");
        _intervalCombo.Items.Add("15 minutes");
        _intervalCombo.Items.Add("30 minutes");
        _intervalCombo.SelectedIndex = _settings.Settings.RefreshIntervalMinutes switch
        {
            1 => 0, 5 => 1, 15 => 2, 30 => 3, _ => 1
        };
        _intervalCombo.IsEnabled = _settings.Settings.AutoRefreshEnabled;
        _intervalCombo.SelectionChanged += (s, e) =>
        {
            _settings.Settings.RefreshIntervalMinutes = _intervalCombo.SelectedIndex switch
            {
                0 => 1, 1 => 5, 2 => 15, 3 => 30, _ => 5
            };
            _settings.Save();
        };

            // Auto-refresh toggle
            _autoRefreshToggle = SettingCard.CreateToggleSwitch(_settings.Settings.AutoRefreshEnabled);
            _autoRefreshToggle.Toggled += (s, e) =>
            {
                _settings.Settings.AutoRefreshEnabled = _autoRefreshToggle.IsOn;
                _intervalCombo.IsEnabled = _autoRefreshToggle.IsOn;
                _settings.Save();
            };
            stack.Children.Add(SettingCard.Create(
                "Auto-refresh",
                "Automatically refresh usage data at regular intervals",
                _autoRefreshToggle));

        stack.Children.Add(SettingCard.Create(
            "Refresh interval",
            "How often to check for usage updates",
            _intervalCombo));

        // Hover delay
        var hoverPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _hoverSlider = new Slider
        {
            Minimum = 100,
            Maximum = 1000,
            Value = _settings.Settings.HoverDelayMs,
            Width = 150,
            StepFrequency = 50
        };
        var hoverValueText = new TextBlock
        {
            Text = $"{_settings.Settings.HoverDelayMs}ms",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            MinWidth = 50
        };
        _hoverSlider.ValueChanged += (s, e) =>
        {
            var value = (int)_hoverSlider.Value;
            _settings.Settings.HoverDelayMs = value;
            hoverValueText.Text = $"{value}ms";
            _settings.Save();
        };
        hoverPanel.Children.Add(_hoverSlider);
        hoverPanel.Children.Add(hoverValueText);
        stack.Children.Add(SettingCard.Create(
            "Hover delay",
            "Delay before showing popup on hover",
            hoverPanel));

        // Keyboard shortcuts section
        stack.Children.Add(SettingCard.CreateHeader("Keyboard Shortcuts", topMargin: 24));

        // Global hotkey toggle
        _hotkeyToggle = SettingCard.CreateToggleSwitch(_settings.Settings.HotkeyEnabled);
        _hotkeyToggle.Toggled += (s, e) =>
        {
            _settings.Settings.HotkeyEnabled = _hotkeyToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "Enable global hotkey",
            "Use a keyboard shortcut to toggle the popup from anywhere",
            _hotkeyToggle));

        // Auto-check for updates toggle
        _autoUpdateToggle = SettingCard.CreateToggleSwitch(_settings.Settings.AutoCheckForUpdates);
        _autoUpdateToggle.Toggled += (s, e) =>
        {
            _settings.Settings.AutoCheckForUpdates = _autoUpdateToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "Check for updates automatically",
            "Periodically check for new versions and notify you",
            _autoUpdateToggle));

        // Hotkey selection
        _hotkeyCombo = new ComboBox { Width = 180 };
        _hotkeyCombo.Items.Add("Win + Shift + Q");
        _hotkeyCombo.Items.Add("Win + Alt + Q");
        _hotkeyCombo.Items.Add("Ctrl + Alt + Q");
        _hotkeyCombo.Items.Add("Win + Shift + U");
        _hotkeyCombo.Items.Add("Win + `");

        var currentHotkey = _settings.Settings.HotkeyDisplayString ?? "Win + Shift + Q";
        _hotkeyCombo.SelectedIndex = currentHotkey switch
        {
            "Win + Shift + Q" => 0,
            "Win + Alt + Q" => 1,
            "Ctrl + Alt + Q" => 2,
            "Win + Shift + U" => 3,
            "Win + `" => 4,
            _ => 0
        };

        _hotkeyCombo.SelectionChanged += (s, e) =>
        {
            var selected = _hotkeyCombo.SelectedItem?.ToString() ?? "Win + Shift + Q";
            _settings.Settings.HotkeyDisplayString = selected;
            var (modifiers, key) = ParseHotkeyString(selected);
            _settings.Settings.HotkeyModifiers = modifiers;
            _settings.Settings.HotkeyKey = key;
            _settings.Save();
        };

        stack.Children.Add(SettingCard.Create(
            "Global hotkey",
            "Keyboard shortcut to toggle the popup",
            _hotkeyCombo));

        // Shortcuts reference
        var shortcutsInfo = new TextBlock
        {
            Text = "Popup shortcuts: 1-9 (switch provider), R (refresh), D (dashboard), S (settings), P (pin), ? (help)",
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(shortcutsInfo);

            scroll.Content = stack;
            DebugLogger.Log("GeneralSettingsPage", "CreateContent DONE");
            return scroll;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("GeneralSettingsPage", "CreateContent CRASHED", ex);
            var errorScroll = new ScrollViewer { Padding = new Thickness(24) };
            var errorStack = new StackPanel();
            errorStack.Children.Add(new TextBlock { Text = $"Error loading settings: {ex.Message}", Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red) });
            errorScroll.Content = errorStack;
            return errorScroll;
        }
    }

    private static (List<string> Modifiers, string Key) ParseHotkeyString(string hotkeyString)
    {
        var parts = hotkeyString.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var modifiers = new List<string>();
        var key = "Q";

        foreach (var part in parts)
        {
            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
            {
                modifiers.Add(part);
            }
            else
            {
                key = part;
            }
        }

        return (modifiers, key);
    }

    public void OnThemeChanged()
    {
        // Recreate content to pick up new theme colors
        _content = CreateContent();
    }
}
