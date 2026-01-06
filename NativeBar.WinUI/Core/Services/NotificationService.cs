using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using NativeBar.WinUI.Core.Models;

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
    /// Key: "providerId:threshold", Value: DateTime when sent
    /// Alerts reset when usage drops below threshold or after 24h
    /// </summary>
    private readonly Dictionary<string, DateTime> _sentAlerts = new();
    
    /// <summary>
    /// Cooldown period before the same alert can be sent again (even if conditions still met)
    /// </summary>
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromHours(4);

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

            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] NotificationService initialized\n");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] NotificationService.Initialize ERROR: {ex.Message}\n");
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
            // Parse arguments from the notification
            var action = "";
            var provider = "";

            foreach (var (key, value) in args.Arguments)
            {
                if (key == "action") action = value;
                if (key == "provider") provider = value;
            }

            NotificationActivated?.Invoke(action, provider);
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] OnNotificationInvoked ERROR: {ex.Message}\n");
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
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] ShowToast ERROR: {ex.Message}\n");
        }
    }

    /// <summary>
    /// Main entry point: Check a provider's usage snapshot and send appropriate alerts.
    /// Call this after each refresh. Handles deduplication and cooldowns internally.
    /// </summary>
    public void CheckAndNotifyUsage(string providerId, string providerDisplayName, UsageSnapshot? snapshot)
    {
        if (snapshot == null || snapshot.IsLoading || !string.IsNullOrEmpty(snapshot.ErrorMessage))
            return;

        if (!SettingsService.Instance.Settings.UsageAlertsEnabled)
            return;

        var settings = SettingsService.Instance.Settings;
        var warningThreshold = settings.WarningThreshold;
        var criticalThreshold = settings.CriticalThreshold;

        // Check primary rate window (most important)
        CheckRateWindow(providerId, providerDisplayName, snapshot.Primary, warningThreshold, criticalThreshold);
        
        // Also check secondary if exists (some providers have multiple limits)
        CheckRateWindow(providerId, providerDisplayName, snapshot.Secondary, warningThreshold, criticalThreshold, "secondary");
    }

    private void CheckRateWindow(string providerId, string providerDisplayName, RateWindow? window, 
        int warningThreshold, int criticalThreshold, string windowSuffix = "")
    {
        if (window == null) return;

        var percentage = window.UsedPercent;
        var alertKeyPrefix = string.IsNullOrEmpty(windowSuffix) ? providerId : $"{providerId}:{windowSuffix}";

        // Critical threshold (100% or configured)
        if (percentage >= criticalThreshold)
        {
            if (ShouldSendAlert(alertKeyPrefix, AlertThreshold.Critical))
            {
                ShowUsageCriticalInternal(providerDisplayName, percentage, window.ResetsAt);
                MarkAlertSent(alertKeyPrefix, AlertThreshold.Critical);
            }
        }
        // Warning threshold (80% or configured)
        else if (percentage >= warningThreshold)
        {
            if (ShouldSendAlert(alertKeyPrefix, AlertThreshold.Warning))
            {
                ShowUsageWarningInternal(providerDisplayName, percentage);
                MarkAlertSent(alertKeyPrefix, AlertThreshold.Warning);
            }
            // Reset critical alert if usage dropped below critical
            ClearAlert(alertKeyPrefix, AlertThreshold.Critical);
        }
        else
        {
            // Usage is low - clear all alerts for this provider so they can fire again
            ClearAlert(alertKeyPrefix, AlertThreshold.Warning);
            ClearAlert(alertKeyPrefix, AlertThreshold.Critical);
        }
    }

    private string GetAlertKey(string providerId, AlertThreshold threshold) 
        => $"{providerId}:{threshold}";

    private bool ShouldSendAlert(string providerId, AlertThreshold threshold)
    {
        var key = GetAlertKey(providerId, threshold);
        
        if (!_sentAlerts.TryGetValue(key, out var lastSent))
            return true; // Never sent before

        // Allow resending after cooldown period
        return DateTime.UtcNow - lastSent > AlertCooldown;
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
    /// Clear all sent alerts (useful when user changes threshold settings)
    /// </summary>
    public void ClearAllAlerts()
    {
        _sentAlerts.Clear();
    }

    private void ShowUsageWarningInternal(string providerName, double percentage)
    {
        try
        {
            if (!_isRegistered) return;

            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] Sending WARNING alert for {providerName} at {percentage:F0}%\n");

            var builder = new AppNotificationBuilder()
                .AddText($"⚠️ {providerName} Usage Warning")
                .AddText($"You've used {percentage:F0}% of your quota")
                .AddArgument("provider", providerName)
                .AddButton(new AppNotificationButton("View Details")
                    .AddArgument("action", "view")
                    .AddArgument("provider", providerName))
                .AddButton(new AppNotificationButton("Dismiss")
                    .AddArgument("action", "dismiss"));

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] ShowUsageWarning ERROR: {ex.Message}\n");
        }
    }

    private void ShowUsageCriticalInternal(string providerName, double percentage, DateTime? resetsAt)
    {
        try
        {
            if (!_isRegistered) return;

            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] Sending CRITICAL alert for {providerName} at {percentage:F0}%\n");

            var resetText = resetsAt.HasValue 
                ? $"Resets: {FormatResetTime(resetsAt.Value)}" 
                : "Consider reducing usage.";

            var builder = new AppNotificationBuilder()
                .AddText($"🚨 {providerName} Limit Reached!")
                .AddText($"You've used {percentage:F0}% of your quota. {resetText}")
                .AddArgument("provider", providerName)
                .AddButton(new AppNotificationButton("View Details")
                    .AddArgument("action", "view")
                    .AddArgument("provider", providerName));

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] ShowUsageCritical ERROR: {ex.Message}\n");
        }
    }

    private static string FormatResetTime(DateTime resetUtc)
    {
        var local = resetUtc.ToLocalTime();
        var diff = resetUtc - DateTime.UtcNow;

        if (diff.TotalMinutes < 60)
            return $"in {diff.TotalMinutes:F0} min";
        if (diff.TotalHours < 24)
            return $"in {diff.TotalHours:F1} hours";
        
        return local.ToString("MMM d, h:mm tt");
    }

    /// <summary>
    /// Show usage warning notification (legacy - prefer CheckAndNotifyUsage)
    /// </summary>
    public void ShowUsageWarning(string providerName, double percentage)
    {
        ShowUsageWarningInternal(providerName, percentage);
    }

    /// <summary>
    /// Show critical usage notification (legacy - prefer CheckAndNotifyUsage)
    /// </summary>
    public void ShowUsageCritical(string providerName, double percentage)
    {
        ShowUsageCriticalInternal(providerName, percentage, null);
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
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] ShowProviderStatus ERROR: {ex.Message}\n");
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
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] ShowDailySummary ERROR: {ex.Message}\n");
        }
    }
}
