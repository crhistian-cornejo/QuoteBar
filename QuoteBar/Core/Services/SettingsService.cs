using System.IO;
using System.Text.Json;

namespace QuoteBar.Core.Services;

/// <summary>
/// Settings service for persisting app configuration.
/// Uses debouncing to reduce disk I/O when settings change rapidly.
/// </summary>
public class SettingsService : IDisposable
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuoteBar",
        "settings.json");

    private static SettingsService? _instance;
    public static SettingsService Instance => _instance ??= new SettingsService();

    public AppSettings Settings { get; private set; } = new();

    public event Action? SettingsChanged;

    // Debounce settings save to reduce disk I/O
    private Timer? _saveDebounceTimer;
    private readonly object _saveLock = new();
    private const int SaveDebounceMs = 1000; // Wait 1 second before saving

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

    /// <summary>
    /// Queue a save operation. Uses debouncing to batch rapid changes.
    /// </summary>
    public void Save()
    {
        lock (_saveLock)
        {
            // Cancel any pending save
            _saveDebounceTimer?.Dispose();

            // Schedule save after debounce period
            _saveDebounceTimer = new Timer(_ => PerformSave(), null, SaveDebounceMs, Timeout.Infinite);
        }

        // Fire settings changed immediately (UI updates shouldn't wait)
        try
        {
            SettingsChanged?.Invoke();
        }
        catch (Exception eventEx)
        {
            DebugLogger.LogError("SettingsService", "SettingsChanged event error", eventEx);
        }
    }

    /// <summary>
    /// Force immediate save (use when app is closing)
    /// </summary>
    public void SaveImmediate()
    {
        lock (_saveLock)
        {
            _saveDebounceTimer?.Dispose();
            _saveDebounceTimer = null;
        }
        PerformSave();
    }

    private void PerformSave()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Use compact JSON (no indentation) to reduce file size
            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(SettingsPath, json);

            DebugLogger.Log("SettingsService", "Settings saved to disk");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("SettingsService", "Save error", ex);
        }
    }

    public void Dispose()
    {
        // Save any pending changes before disposing
        SaveImmediate();
        _saveDebounceTimer?.Dispose();
    }
}

public class AppSettings
{
    // General
    public bool StartAtLogin { get; set; } = true;
    public bool ShowInTaskbar { get; set; } = false;
    public int RefreshIntervalMinutes { get; set; } = 5;
    public bool AutoRefreshEnabled { get; set; } = true;
    public bool AutoCheckForUpdates { get; set; } = true;
    public DateTime LastUpdateCheckTime { get; set; } = default;
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
    public HashSet<string> EnabledProviders { get; set; } = new() { "codex", "claude", "cursor", "gemini", "copilot", "droid", "antigravity", "zai", "augment" };

    // Provider configs
    public Dictionary<string, ProviderConfig> Providers { get; set; } = new();

    // Provider order in UI (for reordering feature)
    public List<string> ProviderOrder { get; set; } = new();

    // Copilot-specific settings
    // Plan types: "auto", "free", "pro", "pro_plus"
    public string CopilotPlanType { get; set; } = "auto";

    // Hotkey settings
    // Default: Win+Shift+Q (safe, doesn't conflict with Windows shortcuts)
    public bool HotkeyEnabled { get; set; } = true;
    public string HotkeyKey { get; set; } = "Q";
    public List<string> HotkeyModifiers { get; set; } = new() { "Win", "Shift" };
    public string? HotkeyDisplayString { get; set; } = "Win + Shift + Q";

    // Display Settings
    /// <summary>
    /// Currency display mode: "System", "USD", "EUR", "GBP", "JPY", "CNY"
    /// </summary>
    public string CurrencyDisplayMode { get; set; } = "System";

    /// <summary>
    /// Reset time display mode: false = relative ("in 2h 30m"), true = absolute ("at 14:45")
    /// </summary>
    public bool ShowAbsoluteResetTime { get; set; } = false;

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

    /// <summary>
    /// Get provider config, creating a new one if it doesn't exist
    /// </summary>
    public ProviderConfig GetProviderConfig(string providerId)
    {
        var id = providerId.ToLower();
        if (!Providers.TryGetValue(id, out var config))
        {
            config = new ProviderConfig();
            Providers[id] = config;
        }
        return config;
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
    /// <summary>
    /// User's preferred authentication strategy for this provider.
    /// Values: "Auto", "CLI", "OAuth", "Manual"
    /// </summary>
    public string PreferredStrategy { get; set; } = "Auto";

    /// <summary>
    /// Dynamic provider settings (key-value pairs)
    /// </summary>
    public Dictionary<string, string> Settings { get; set; } = new();
}
