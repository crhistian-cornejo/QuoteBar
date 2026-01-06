using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Providers;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.ViewModels;

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
        if (window?.ResetsAt == null)
            return "";

        var remaining = window.ResetsAt.Value - DateTime.UtcNow;

        if (remaining.TotalSeconds <= 0)
            return "Resetting...";

        if (remaining.TotalMinutes < 60)
            return $"Resets in {(int)remaining.TotalMinutes}m";

        if (remaining.TotalHours < 24)
            return $"Resets in {(int)remaining.TotalHours}h {remaining.Minutes}m";

        return $"Resets in {(int)remaining.TotalDays}d";
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
