using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Providers;
using System.Diagnostics;

namespace NativeBar.WinUI.Core.Services;

/// <summary>
/// Alert threshold types for tracking sent notifications
/// </summary>
public enum AlertThreshold
{
    Warning,  // e.g., 80%
    Critical  // e.g., 100%
}

/// <summary>
/// Native Windows notification service using Windows App SDK
/// </summary>
public class NotificationService
{
    private static NotificationService? _instance;
    public static NotificationService Instance => _instance ??= new NotificationService();

    private bool _isRegistered;

    /// <summary>
    /// Tracks which alerts have been sent per provider to avoid notification spam.
    /// Key: "providerId:windowType:threshold", Value: DateTime when sent
    /// </summary>
    private readonly Dictionary<string, DateTime> _sentAlerts = new();

    /// <summary>
    /// Cooldown periods based on window type
    /// </summary>
    private static readonly Dictionary<UsageWindowType, TimeSpan> CooldownByWindowType = new()
    {
        { UsageWindowType.Session, TimeSpan.FromMinutes(30) },  // Session: notify frequently
        { UsageWindowType.Daily, TimeSpan.FromHours(12) },      // Daily: once per 12h
        { UsageWindowType.Weekly, TimeSpan.FromDays(3) },       // Weekly: once per reset period
        { UsageWindowType.Monthly, TimeSpan.FromDays(7) }       // Monthly: once per week
    };

    public event Action<string, string>? NotificationActivated;

    private NotificationService()
    {
    }

    public void Initialize()
    {
        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked += OnNotificationInvoked;
            manager.Register();
            _isRegistered = true;

            DebugLogger.Log("NotificationService", "Initialized");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("NotificationService", "Initialize ERROR", ex);
        }
    }

    public void Shutdown()
    {
        try
        {
            if (_isRegistered)
            {
                AppNotificationManager.Default.Unregister();
                _isRegistered = false;
            }
        }
        catch { }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try
        {
            var action = "";
            var providerId = "";
            var dashboardUrl = "";

            foreach (var (key, value) in args.Arguments)
            {
                if (key == "action") action = value;
                if (key == "providerId") providerId = value;
                if (key == "dashboardUrl") dashboardUrl = value;
            }

            DebugLogger.Log("NotificationService", $"Notification activated: action={action}, providerId={providerId}");

            if (action == "view" && !string.IsNullOrEmpty(dashboardUrl))
            {
                // Open dashboard URL in browser
                OpenDashboard(dashboardUrl);
            }
            else if (action == "view" && !string.IsNullOrEmpty(providerId))
            {
                // Try to get dashboard URL from provider registry
                var descriptor = ProviderRegistry.Instance.GetProvider(providerId);
                if (descriptor?.DashboardUrl != null)
                {
                    OpenDashboard(descriptor.DashboardUrl);
                }
            }

            NotificationActivated?.Invoke(action, providerId);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("NotificationService", "OnNotificationInvoked ERROR", ex);
        }
    }

    private void OpenDashboard(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            DebugLogger.Log("NotificationService", $"Opened dashboard: {url}");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("NotificationService", $"Failed to open dashboard: {url}", ex);
        }
    }

    /// <summary>
    /// Show a simple toast notification
    /// </summary>
    public void ShowToast(string title, string message)
    {
        try
        {
            if (!_isRegistered) return;

            var builder = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message);

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("NotificationService", "ShowToast ERROR", ex);
        }
    }

    /// <summary>
    /// Main entry point: Check a provider's usage snapshot and send appropriate alerts.
    /// Call this after each refresh. Handles deduplication and cooldowns internally.
    /// </summary>
    public void CheckAndNotifyUsage(string providerId, UsageSnapshot? snapshot)
    {
        if (snapshot == null || snapshot.IsLoading || !string.IsNullOrEmpty(snapshot.ErrorMessage))
            return;

        if (!SettingsService.Instance.Settings.UsageAlertsEnabled)
            return;

        var descriptor = ProviderRegistry.Instance.GetProvider(providerId);
        if (descriptor == null) return;

        var settings = SettingsService.Instance.Settings;
        var warningThreshold = settings.WarningThreshold;
        var criticalThreshold = settings.CriticalThreshold;

        // Check primary rate window
        CheckRateWindow(
            descriptor,
            snapshot.Primary,
            descriptor.PrimaryWindowType,
            warningThreshold,
            criticalThreshold,
            "primary");

        // Check secondary if exists
        CheckRateWindow(
            descriptor,
            snapshot.Secondary,
            descriptor.SecondaryWindowType,
            warningThreshold,
            criticalThreshold,
            "secondary");
    }

    private void CheckRateWindow(
        IProviderDescriptor descriptor,
        RateWindow? window,
        UsageWindowType windowType,
        int warningThreshold,
        int criticalThreshold,
        string windowSuffix)
    {
        if (window == null) return;

        var percentage = window.UsedPercent;
        var alertKeyPrefix = $"{descriptor.Id}:{windowSuffix}";

        // Critical threshold
        if (percentage >= criticalThreshold)
        {
            if (ShouldSendAlert(alertKeyPrefix, AlertThreshold.Critical, windowType))
            {
                ShowUsageCriticalInternal(descriptor, percentage, window.ResetsAt, windowSuffix);
                MarkAlertSent(alertKeyPrefix, AlertThreshold.Critical);
            }
        }
        // Warning threshold
        else if (percentage >= warningThreshold)
        {
            if (ShouldSendAlert(alertKeyPrefix, AlertThreshold.Warning, windowType))
            {
                ShowUsageWarningInternal(descriptor, percentage, windowSuffix);
                MarkAlertSent(alertKeyPrefix, AlertThreshold.Warning);
            }
            ClearAlert(alertKeyPrefix, AlertThreshold.Critical);
        }
        else
        {
            // Usage dropped - clear alerts so they can fire again
            ClearAlert(alertKeyPrefix, AlertThreshold.Warning);
            ClearAlert(alertKeyPrefix, AlertThreshold.Critical);
        }
    }

    private string GetAlertKey(string providerId, AlertThreshold threshold)
        => $"{providerId}:{threshold}";

    private bool ShouldSendAlert(string providerId, AlertThreshold threshold, UsageWindowType windowType)
    {
        var key = GetAlertKey(providerId, threshold);

        if (!_sentAlerts.TryGetValue(key, out var lastSent))
            return true;

        // Get cooldown based on window type
        var cooldown = CooldownByWindowType.GetValueOrDefault(windowType, TimeSpan.FromHours(4));
        return DateTime.UtcNow - lastSent > cooldown;
    }

    private void MarkAlertSent(string providerId, AlertThreshold threshold)
    {
        var key = GetAlertKey(providerId, threshold);
        _sentAlerts[key] = DateTime.UtcNow;
    }

    private void ClearAlert(string providerId, AlertThreshold threshold)
    {
        var key = GetAlertKey(providerId, threshold);
        _sentAlerts.Remove(key);
    }

    /// <summary>
    /// Clear all sent alerts
    /// </summary>
    public void ClearAllAlerts()
    {
        _sentAlerts.Clear();
    }

    private void ShowUsageWarningInternal(IProviderDescriptor descriptor, double percentage, string windowSuffix)
    {
        try
        {
            if (!_isRegistered) return;

            var windowLabel = windowSuffix == "secondary" ? descriptor.SecondaryLabel : descriptor.PrimaryLabel;
            DebugLogger.Log("NotificationService", $"Sending WARNING for {descriptor.DisplayName} ({windowLabel}) at {percentage:F0}%");

            var builder = new AppNotificationBuilder()
                .AddText($"⚠️ {descriptor.DisplayName} Usage Warning")
                .AddText($"{windowLabel}: {percentage:F0}% used")
                .AddArgument("providerId", descriptor.Id);

            // Add View Details button with dashboard URL
            if (!string.IsNullOrEmpty(descriptor.DashboardUrl))
            {
                builder.AddButton(new AppNotificationButton("View Details")
                    .AddArgument("action", "view")
                    .AddArgument("providerId", descriptor.Id)
                    .AddArgument("dashboardUrl", descriptor.DashboardUrl));
            }

            builder.AddButton(new AppNotificationButton("Dismiss")
                .AddArgument("action", "dismiss"));

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("NotificationService", "ShowUsageWarning ERROR", ex);
        }
    }

    private void ShowUsageCriticalInternal(IProviderDescriptor descriptor, double percentage, DateTime? resetsAt, string windowSuffix)
    {
        try
        {
            if (!_isRegistered) return;

            var windowLabel = windowSuffix == "secondary" ? descriptor.SecondaryLabel : descriptor.PrimaryLabel;
            DebugLogger.Log("NotificationService", $"Sending CRITICAL for {descriptor.DisplayName} ({windowLabel}) at {percentage:F0}%");

            var resetText = resetsAt.HasValue
                ? $"Resets {FormatResetTime(resetsAt.Value)}"
                : "";

            var builder = new AppNotificationBuilder()
                .AddText($"🚨 {descriptor.DisplayName} Limit Reached!")
                .AddText($"{windowLabel}: {percentage:F0}% used. {resetText}".Trim())
                .AddArgument("providerId", descriptor.Id);

            // Add View Details button with dashboard URL
            if (!string.IsNullOrEmpty(descriptor.DashboardUrl))
            {
                builder.AddButton(new AppNotificationButton("View Details")
                    .AddArgument("action", "view")
                    .AddArgument("providerId", descriptor.Id)
                    .AddArgument("dashboardUrl", descriptor.DashboardUrl));
            }

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("NotificationService", "ShowUsageCritical ERROR", ex);
        }
    }

    private static string FormatResetTime(DateTime resetUtc)
    {
        var local = resetUtc.ToLocalTime();
        var diff = resetUtc - DateTime.UtcNow;

        if (diff.TotalMinutes < 60)
            return $"in {diff.TotalMinutes:F0}m";
        if (diff.TotalHours < 24)
            return $"in {diff.TotalHours:F1}h";

        return local.ToString("MMM d, h:mm tt");
    }

    /// <summary>
    /// Show usage warning notification (legacy)
    /// </summary>
    public void ShowUsageWarning(string providerName, double percentage)
    {
        var descriptor = ProviderRegistry.Instance.GetAllProviders()
            .FirstOrDefault(p => p.DisplayName == providerName);
        if (descriptor != null)
        {
            ShowUsageWarningInternal(descriptor, percentage, "primary");
        }
    }

    /// <summary>
    /// Show critical usage notification (legacy)
    /// </summary>
    public void ShowUsageCritical(string providerName, double percentage)
    {
        var descriptor = ProviderRegistry.Instance.GetAllProviders()
            .FirstOrDefault(p => p.DisplayName == providerName);
        if (descriptor != null)
        {
            ShowUsageCriticalInternal(descriptor, percentage, null, "primary");
        }
    }

    /// <summary>
    /// Show provider connection status notification
    /// </summary>
    public void ShowProviderStatus(string providerName, bool isConnected)
    {
        try
        {
            if (!_isRegistered) return;

            var status = isConnected ? "connected" : "disconnected";
            var icon = isConnected ? "✅" : "❌";

            var builder = new AppNotificationBuilder()
                .AddText($"{icon} {providerName}")
                .AddText($"Provider {status} successfully");

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("NotificationService", "ShowProviderStatus ERROR", ex);
        }
    }

    /// <summary>
    /// Show daily summary notification
    /// </summary>
    public void ShowDailySummary(Dictionary<string, double> usageByProvider, double totalCost)
    {
        try
        {
            if (!_isRegistered) return;

            var summary = string.Join(", ", usageByProvider.Select(p => $"{p.Key}: {p.Value:F0}%"));

            var builder = new AppNotificationBuilder()
                .AddText("📊 Daily Usage Summary")
                .AddText(summary)
                .AddText($"Estimated cost: ${totalCost:F2}")
                .AddButton(new AppNotificationButton("View Full Report")
                    .AddArgument("action", "report"));

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("NotificationService", "ShowDailySummary ERROR", ex);
        }
    }
}
