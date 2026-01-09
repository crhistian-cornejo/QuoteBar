using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuoteBar.Core.Services;
using QuoteBar.Settings.Controls;

namespace QuoteBar.Settings.Pages;

/// <summary>
/// Notifications settings page - granular notification controls like Quotio
/// </summary>
public class NotificationsSettingsPage : ISettingsPage
{
    private readonly SettingsService _settings = SettingsService.Instance;
    private ScrollViewer? _content;

    // UI controls
    private ToggleSwitch? _alertsToggle;
    private ToggleSwitch? _quotaWarningToggle;
    private ToggleSwitch? _quotaCriticalToggle;
    private ToggleSwitch? _providerStatusToggle;
    private ToggleSwitch? _upgradeToggle;
    private ComboBox? _thresholdPicker;
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

        // Master toggle for usage alerts
        _alertsToggle = SettingCard.CreateToggleSwitch(_settings.Settings.UsageAlertsEnabled);
        _alertsToggle.Toggled += (s, e) =>
        {
            _settings.Settings.UsageAlertsEnabled = _alertsToggle.IsOn;
            _settings.Save();
            UpdateGranularTogglesVisibility();
        };
        stack.Children.Add(SettingCard.Create(
            "Enable notifications",
            "Show system notifications for usage alerts and updates",
            _alertsToggle));

        // Granular toggles section header
        stack.Children.Add(SettingCard.CreateSectionHeader("Notification Types"));

        // Quota Warning toggle
        _quotaWarningToggle = SettingCard.CreateToggleSwitch(_settings.Settings.NotifyOnQuotaWarning);
        _quotaWarningToggle.Toggled += (s, e) =>
        {
            _settings.Settings.NotifyOnQuotaWarning = _quotaWarningToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "Quota warnings",
            "Notify when usage exceeds warning threshold",
            _quotaWarningToggle));

        // Quota Critical toggle
        _quotaCriticalToggle = SettingCard.CreateToggleSwitch(_settings.Settings.NotifyOnQuotaCritical);
        _quotaCriticalToggle.Toggled += (s, e) =>
        {
            _settings.Settings.NotifyOnQuotaCritical = _quotaCriticalToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "Critical alerts",
            "Notify when usage reaches critical threshold",
            _quotaCriticalToggle));

        // Provider Status toggle
        _providerStatusToggle = SettingCard.CreateToggleSwitch(_settings.Settings.NotifyOnProviderStatus);
        _providerStatusToggle.Toggled += (s, e) =>
        {
            _settings.Settings.NotifyOnProviderStatus = _providerStatusToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "Provider status",
            "Notify when providers connect or disconnect",
            _providerStatusToggle));

        // Upgrade Available toggle
        _upgradeToggle = SettingCard.CreateToggleSwitch(_settings.Settings.NotifyOnUpgradeAvailable);
        _upgradeToggle.Toggled += (s, e) =>
        {
            _settings.Settings.NotifyOnUpgradeAvailable = _upgradeToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "App updates",
            "Notify when a new version is available",
            _upgradeToggle));

        // Thresholds section header
        stack.Children.Add(SettingCard.CreateSectionHeader("Alert Thresholds"));

        // Quota Alert Threshold picker (like Quotio: 10%, 20%, 30%, 50%)
        _thresholdPicker = new ComboBox
        {
            Width = 100,
            SelectedIndex = GetThresholdIndex(_settings.Settings.QuotaAlertThreshold)
        };
        _thresholdPicker.Items.Add("10%");
        _thresholdPicker.Items.Add("20%");
        _thresholdPicker.Items.Add("30%");
        _thresholdPicker.Items.Add("50%");
        _thresholdPicker.SelectionChanged += (s, e) =>
        {
            _settings.Settings.QuotaAlertThreshold = GetThresholdValue(_thresholdPicker.SelectedIndex);
            // Also update legacy thresholds to match
            _settings.Settings.WarningThreshold = 100 - _settings.Settings.QuotaAlertThreshold;
            _settings.Settings.CriticalThreshold = 100 - (_settings.Settings.QuotaAlertThreshold / 2);
            _settings.Save();
        };
        stack.Children.Add(SettingCard.Create(
            "Alert threshold",
            "Show warning when remaining quota drops below this level",
            _thresholdPicker));

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

        // Initial visibility update
        UpdateGranularTogglesVisibility();

        return scroll;
    }

    private void UpdateGranularTogglesVisibility()
    {
        var enabled = _alertsToggle?.IsOn ?? true;
        if (_quotaWarningToggle != null) _quotaWarningToggle.IsEnabled = enabled;
        if (_quotaCriticalToggle != null) _quotaCriticalToggle.IsEnabled = enabled;
        if (_providerStatusToggle != null) _providerStatusToggle.IsEnabled = enabled;
        if (_upgradeToggle != null) _upgradeToggle.IsEnabled = enabled;
        if (_thresholdPicker != null) _thresholdPicker.IsEnabled = enabled;
        if (_soundToggle != null) _soundToggle.IsEnabled = enabled;
    }

    private static int GetThresholdIndex(int threshold) => threshold switch
    {
        10 => 0,
        20 => 1,
        30 => 2,
        50 => 3,
        _ => 1 // Default to 20%
    };

    private static int GetThresholdValue(int index) => index switch
    {
        0 => 10,
        1 => 20,
        2 => 30,
        3 => 50,
        _ => 20
    };

    public void OnThemeChanged()
    {
        _content = null; // Force recreation on next access
    }
}
