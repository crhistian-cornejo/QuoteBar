using System.IO;
using System.Text.Json;

namespace NativeBar.WinUI.Core.Services;

/// <summary>
/// Settings service for persisting app configuration
/// </summary>
public class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuoteBar",
        "settings.json");

    private static SettingsService? _instance;
    public static SettingsService Instance => _instance ??= new SettingsService();

    public AppSettings Settings { get; private set; } = new();

    public event Action? SettingsChanged;

    private SettingsService()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("SettingsService", "Load error", ex);
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);

            // Fire event in a safe manner
            try
            {
                SettingsChanged?.Invoke();
            }
            catch (Exception eventEx)
            {
                DebugLogger.LogError("SettingsService", "SettingsChanged event error", eventEx);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("SettingsService", "Save error", ex);
        }
    }
}

public class AppSettings
{
    // General
    public bool StartAtLogin { get; set; } = true;
    public bool ShowInTaskbar { get; set; } = false;
    public int RefreshIntervalMinutes { get; set; } = 5;
    public int HoverDelayMs { get; set; } = 300;

    // Appearance
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool UseSystemAccentColor { get; set; } = true;
    public bool CompactMode { get; set; } = false;
    public bool ShowProviderIcons { get; set; } = true;

    // Tray Badge Settings
    public bool TrayBadgeEnabled { get; set; } = false;
    public string TrayBadgeProvider { get; set; } = "claude"; // Single provider for tray badge

    // Notifications
    public bool UsageAlertsEnabled { get; set; } = true;
    public int WarningThreshold { get; set; } = 70;
    public int CriticalThreshold { get; set; } = 90;
    public bool PlaySound { get; set; } = false;

    // Provider visibility (which providers show in popup)
    public HashSet<string> EnabledProviders { get; set; } = new() { "codex", "claude", "cursor", "gemini", "copilot", "droid", "antigravity", "zai" };

    // Provider configs
    public Dictionary<string, ProviderConfig> Providers { get; set; } = new();

    // Copilot-specific settings
    // Plan types: "auto", "free", "pro", "pro_plus"
    public string CopilotPlanType { get; set; } = "auto";

    // Hotkey settings
    // Default: Win+Shift+Q (safe, doesn't conflict with Windows shortcuts)
    public bool HotkeyEnabled { get; set; } = true;
    public string HotkeyKey { get; set; } = "Q";
    public List<string> HotkeyModifiers { get; set; } = new() { "Win", "Shift" };
    public string? HotkeyDisplayString { get; set; } = "Win + Shift + Q";

    // Note: API tokens are stored securely in Windows Credential Manager via SecureCredentialStore
    // Do NOT add API tokens as properties here - they would be stored in plain text in settings.json

    public bool IsProviderEnabled(string providerId) => EnabledProviders.Contains(providerId.ToLower());

    public void SetProviderEnabled(string providerId, bool enabled)
    {
        var id = providerId.ToLower();
        if (enabled)
            EnabledProviders.Add(id);
        else
            EnabledProviders.Remove(id);
    }
}

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public class ProviderConfig
{
    public bool Enabled { get; set; } = true;
    public string? ApiKey { get; set; }
    public string? CliPath { get; set; }
}
