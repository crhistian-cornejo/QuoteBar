using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace QuoteBar.Core.Services;

/// <summary>
/// Service to manage Windows startup registration
/// Uses the Registry Run key for the current user
/// </summary>
public static class StartupService
{
    private const string AppName = "QuoteBar";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Check if the app is registered to start with Windows
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            var value = key?.GetValue(AppName);
            return value != null;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("StartupService", "Error checking startup status", ex);
            return false;
        }
    }

    /// <summary>
    /// Enable or disable startup with Windows
    /// </summary>
    public static bool SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                DebugLogger.LogError("StartupService", "Failed to open Run registry key", null);
                return false;
            }

            if (enabled)
            {
                // Get the current executable path
                var exePath = GetExecutablePath();
                if (string.IsNullOrEmpty(exePath))
                {
                    DebugLogger.LogError("StartupService", "Could not determine executable path", null);
                    return false;
                }

                // Set the registry value with quoted path for spaces
                key.SetValue(AppName, $"\"{exePath}\"");
                DebugLogger.Log("StartupService", $"Enabled startup: {exePath}");
            }
            else
            {
                // Remove the registry value
                key.DeleteValue(AppName, throwOnMissingValue: false);
                DebugLogger.Log("StartupService", "Disabled startup");
            }

            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("StartupService", "Error setting startup", ex);
            return false;
        }
    }

    /// <summary>
    /// Get the path to the current executable
    /// </summary>
    private static string? GetExecutablePath()
    {
        try
        {
            // Get the current process executable path
            var processPath = Process.GetCurrentProcess().MainModule?.FileName;

            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            {
                return processPath;
            }

            // Fallback: use Environment
            var envPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            {
                return envPath;
            }

            // Fallback: use AppContext
            var baseDir = AppContext.BaseDirectory;
            var exeName = AppDomain.CurrentDomain.FriendlyName + ".exe";
            var fullPath = Path.Combine(baseDir, exeName);

            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("StartupService", "Error getting executable path", ex);
            return null;
        }
    }

    /// <summary>
    /// Sync the startup setting with the current app settings
    /// Call this on app startup to ensure registry matches settings
    /// </summary>
    public static void SyncWithSettings()
    {
        var settings = SettingsService.Instance.Settings;
        var isRegistered = IsStartupEnabled();

        if (settings.StartAtLogin != isRegistered)
        {
            // Settings and registry are out of sync - use settings as source of truth
            SetStartupEnabled(settings.StartAtLogin);
        }

        DebugLogger.Log("StartupService", $"Synced startup: settings={settings.StartAtLogin}, registry={isRegistered}");
    }
}
