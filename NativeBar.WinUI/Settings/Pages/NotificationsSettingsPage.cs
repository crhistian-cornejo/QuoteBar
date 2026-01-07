using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeBar.WinUI.Core.Services;
using NativeBar.WinUI.Settings.Controls;

namespace NativeBar.WinUI.Settings.Pages;

/// <summary>
/// Notifications settings page - usage alerts, thresholds, sound
/// </summary>
public class NotificationsSettingsPage : ISettingsPage
{
    private readonly SettingsService _settings = SettingsService.Instance;
    private ScrollViewer? _content;

    // UI controls
    private ToggleSwitch? _alertsToggle;
    private Slider? _warningSlider;
    private Slider? _criticalSlider;
    private ToggleSwitch? _soundToggle;

    public FrameworkElement Content => _content ??= CreateContent();

    private ScrollViewer CreateContent()
    {
        var scroll = new ScrollViewer
        {
            Padding = new Thickness(28, 20, 28, 24),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(SettingCard.CreateHeader("Notifications"));

        // Usage alerts toggle
        _alertsToggle = SettingCard.CreateToggleSwitch(_settings.Settings.UsageAlertsEnabled);
        _alertsToggle.Toggled += (s, e) =>
        {
            _settings.Settings.UsageAlertsEnabled = _alertsToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "Usage alerts",
            "Show notifications when usage reaches certain thresholds",
            _alertsToggle));

        // Warning threshold
        _warningSlider = new Slider
        {
            Minimum = 50,
            Maximum = 95,
            Value = _settings.Settings.WarningThreshold,
            Width = 150,
            StepFrequency = 5
        };
        _warningSlider.ValueChanged += (s, e) =>
        {
            _settings.Settings.WarningThreshold = (int)_warningSlider.Value;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            $"Warning threshold ({_settings.Settings.WarningThreshold}%)",
            "Show warning when usage exceeds this percentage",
            _warningSlider));

        // Critical threshold
        _criticalSlider = new Slider
        {
            Minimum = 70,
            Maximum = 100,
            Value = _settings.Settings.CriticalThreshold,
            Width = 150,
            StepFrequency = 5
        };
        _criticalSlider.ValueChanged += (s, e) =>
        {
            _settings.Settings.CriticalThreshold = (int)_criticalSlider.Value;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            $"Critical threshold ({_settings.Settings.CriticalThreshold}%)",
            "Show critical alert when usage exceeds this percentage",
            _criticalSlider));

        // Sound toggle
        _soundToggle = SettingCard.CreateToggleSwitch(_settings.Settings.PlaySound);
        _soundToggle.Toggled += (s, e) =>
        {
            _settings.Settings.PlaySound = _soundToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "Play sound",
            "Play a sound when showing notifications",
            _soundToggle));

        scroll.Content = stack;
        return scroll;
    }

    public void OnThemeChanged()
    {
        _content = null; // Force recreation on next access
    }
}
