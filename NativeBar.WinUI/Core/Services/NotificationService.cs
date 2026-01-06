using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace NativeBar.WinUI.Core.Services;

/// <summary>
/// Native Windows notification service using Windows App SDK
/// </summary>
public class NotificationService
{
    private static NotificationService? _instance;
    public static NotificationService Instance => _instance ??= new NotificationService();

    private bool _isRegistered;

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
    /// Show usage warning notification
    /// </summary>
    public void ShowUsageWarning(string providerName, double percentage)
    {
        try
        {
            if (!_isRegistered) return;
            if (!SettingsService.Instance.Settings.UsageAlertsEnabled) return;

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

    /// <summary>
    /// Show critical usage notification
    /// </summary>
    public void ShowUsageCritical(string providerName, double percentage)
    {
        try
        {
            if (!_isRegistered) return;
            if (!SettingsService.Instance.Settings.UsageAlertsEnabled) return;

            var builder = new AppNotificationBuilder()
                .AddText($"🚨 {providerName} Usage Critical!")
                .AddText($"You've used {percentage:F0}% of your quota. Consider reducing usage.")
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
