using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuoteBar.Core.Models;
using QuoteBar.Core.Providers;
using QuoteBar.Core.Services;

namespace QuoteBar.ViewModels;

public partial class TrayPopupViewModel : ObservableObject
{
    private readonly UsageStore _usageStore;

    [ObservableProperty]
    private UsageSnapshot? _currentSnapshot;

    [ObservableProperty]
    private IProviderDescriptor? _currentProvider;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _lastUpdatedText = "Never";

    public TrayPopupViewModel(UsageStore usageStore)
    {
        _usageStore = usageStore;
        RefreshData();
    }

    public void RefreshData()
    {
        CurrentProvider = _usageStore.GetCurrentProvider();
        CurrentSnapshot = _usageStore.GetCurrentSnapshot();
        UpdateLastUpdatedText();
    }

    private void UpdateLastUpdatedText()
    {
        if (CurrentSnapshot == null || CurrentSnapshot.FetchedAt == default)
        {
            LastUpdatedText = "Never";
            return;
        }

        var elapsed = DateTime.UtcNow - CurrentSnapshot.FetchedAt;

        LastUpdatedText = elapsed.TotalSeconds < 60
            ? "Just now"
            : elapsed.TotalMinutes < 60
                ? $"{(int)elapsed.TotalMinutes}m ago"
                : elapsed.TotalHours < 24
                    ? $"{(int)elapsed.TotalHours}h ago"
                    : $"{(int)elapsed.TotalDays}d ago";
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing) return;

        IsRefreshing = true;
        try
        {
            await _usageStore.RefreshAllAsync();
            RefreshData();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    public string FormatResetTime(RateWindow? window)
    {
        if (window == null)
            return "";

        // If we have a ResetsAt timestamp, use it for precise countdown or absolute time
        if (window.ResetsAt.HasValue)
        {
            var resetsAt = window.ResetsAt.Value;
            var remaining = resetsAt - DateTime.UtcNow;

            if (remaining.TotalSeconds <= 0)
                return "Resetting...";

            // Check user preference for absolute vs relative time
            var showAbsolute = SettingsService.Instance.Settings.ShowAbsoluteResetTime;

            if (showAbsolute)
            {
                return FormatAbsoluteResetTime(resetsAt);
            }
            else
            {
                return FormatRelativeResetTime(remaining);
            }
        }

        // Fall back to ResetDescription if available
        if (!string.IsNullOrEmpty(window.ResetDescription))
        {
            // Add "Resets" prefix if not already present
            var desc = window.ResetDescription;
            if (!desc.StartsWith("Resets", StringComparison.OrdinalIgnoreCase) &&
                !desc.StartsWith("in ", StringComparison.OrdinalIgnoreCase))
            {
                return $"Resets {desc}";
            }
            return desc;
        }

        return "";
    }

    /// <summary>
    /// Format reset time as relative ("in 2h 30m")
    /// </summary>
    private static string FormatRelativeResetTime(TimeSpan remaining)
    {
        if (remaining.TotalMinutes < 60)
            return $"Resets in {(int)remaining.TotalMinutes}m";

        if (remaining.TotalHours < 24)
            return $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";

        if (remaining.TotalDays < 7)
            return $"Resets in {(int)remaining.TotalDays}d {remaining.Hours}h";

        return $"Resets in {(int)remaining.TotalDays}d";
    }

    /// <summary>
    /// Format reset time as absolute ("at 2:45 PM" or "Mon 2:45 PM")
    /// </summary>
    private static string FormatAbsoluteResetTime(DateTime resetsAtUtc)
    {
        var localTime = resetsAtUtc.ToLocalTime();
        var now = DateTime.Now;

        // Use system short time format (respects 12h/24h preference)
        var timeFormat = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;

        // If reset is today, just show time
        if (localTime.Date == now.Date)
        {
            return $"Resets at {localTime.ToString(timeFormat)}";
        }

        // If reset is tomorrow, show "Tomorrow" + time
        if (localTime.Date == now.Date.AddDays(1))
        {
            return $"Resets tomorrow {localTime.ToString(timeFormat)}";
        }

        // If reset is within this week, show day name + time
        if (localTime.Date < now.Date.AddDays(7))
        {
            var dayName = localTime.ToString("ddd"); // Mon, Tue, etc.
            return $"Resets {dayName} {localTime.ToString(timeFormat)}";
        }

        // For dates further out, show date + time
        var dateFormat = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
        return $"Resets {localTime.ToString(dateFormat)} {localTime.ToString(timeFormat)}";
    }

    public string FormatUsage(RateWindow? window)
    {
        if (window == null)
            return "0%";

        if (window.Used.HasValue && window.Limit.HasValue)
        {
            // Show decimals if the value has decimal places
            var used = window.Used.Value;
            var limit = window.Limit.Value;
            var unit = window.Unit ?? "";

            // Format based on whether there are decimals
            string usedStr = used % 1 == 0 ? $"{used:N0}" : $"{used:N2}";
            string limitStr = limit % 1 == 0 ? $"{limit:N0}" : $"{limit:N0}";

            return $"{usedStr} / {limitStr} {unit}".Trim();
        }

        if (window.Used.HasValue)
        {
            // Only used value, no limit (e.g., billed amount)
            var used = window.Used.Value;
            var unit = window.Unit ?? "";
            string usedStr = used % 1 == 0 ? $"{used:N0}" : $"{used:N2}";
            return $"{usedStr} {unit}".Trim();
        }

        return $"{window.UsedPercent:F1}%";
    }
}
