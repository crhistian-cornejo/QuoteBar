using System.IO;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace QuoteBar.Core.Services;

/// <summary>
/// Service for checking and applying application updates from GitHub Releases
/// </summary>
public sealed class UpdateService : IDisposable
{
    private static UpdateService? _instance;
    public static UpdateService Instance => _instance ??= new UpdateService();

    private const string GitHubOwner = "crhistian-cornejo";
    private const string GitHubRepo = "QuoteBar";
    private const string GitHubApiUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    private const string UserAgent = "QuoteBar-WinUI";

    private readonly HttpClient _httpClient;
    private readonly SettingsService _settings;
    private Timer? _checkTimer;
    private GitHubRelease? _latestRelease;

    /// <summary>
    /// Event fired when a new update is available
    /// </summary>
    public event Action<GitHubRelease>? UpdateAvailable;

    /// <summary>
    /// Event fired when update download is complete
    /// </summary>
    public event Action<string>? UpdateDownloaded;

    /// <summary>
    /// Event fired when update check fails
    /// </summary>
    public event Action<string>? UpdateCheckFailed;

    private UpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

        _settings = SettingsService.Instance;

        LoadUpdateState();
    }

    /// <summary>
    /// Start periodic update checking
    /// </summary>
    public void StartPeriodicChecks()
    {
        if (_checkTimer != null) return;

        // Check once on startup
        _ = CheckForUpdatesAsync();

        // Then check every 24 hours
        // Use a synchronous callback that wraps async operation to avoid async void issues
        _checkTimer = new Timer(
            _ => OnTimerCallback(),
            null,
            TimeSpan.FromHours(24),
            TimeSpan.FromHours(24)
        );

        DebugLogger.Log("UpdateService", "Started periodic update checks (every 24h)");
    }

    /// <summary>
    /// Timer callback wrapper that properly handles async operations and exceptions
    /// </summary>
    private void OnTimerCallback()
    {
        // Fire and forget, but handle exceptions properly
        Task.Run(async () =>
        {
            try
            {
                await CheckForUpdatesAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("UpdateService", "Timer callback error", ex);
            }
        });
    }

    /// <summary>
    /// Stop periodic update checking
    /// </summary>
    public void StopPeriodicChecks()
    {
        _checkTimer?.Dispose();
        _checkTimer = null;
        DebugLogger.Log("UpdateService", "Stopped periodic update checks");
    }

    /// <summary>
    /// Check for updates from GitHub
    /// </summary>
    public async Task<GitHubRelease?> CheckForUpdatesAsync(bool force = false)
    {
        try
        {
            DebugLogger.Log("UpdateService", $"Checking for updates (force={force})");

            // Check if auto-updates are disabled
            if (!force && !_settings.Settings.AutoCheckForUpdates)
            {
                DebugLogger.Log("UpdateService", "Auto-update checks disabled, skipping");
                return null;
            }

            // Don't check too frequently (respect GitHub rate limits)
            if (!force && !ShouldCheckForUpdate())
            {
                DebugLogger.Log("UpdateService", "Too soon to check again, skipping");
                return null;
            }

            var response = await _httpClient.GetAsync(GitHubApiUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            _latestRelease = JsonSerializer.Deserialize<GitHubRelease>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }
            );

            if (_latestRelease == null)
            {
                DebugLogger.LogError("UpdateService", "Failed to deserialize release");
                return null;
            }

            var currentVersion = GetCurrentVersion();
            var latestVersion = new Version(_latestRelease.Version);

            DebugLogger.Log("UpdateService", $"Current: {currentVersion}, Latest: {latestVersion}");

            if (latestVersion > currentVersion)
            {
                DebugLogger.Log("UpdateService", $"New version available: {_latestRelease.Version}");

                SaveLastCheckTime();
                UpdateAvailable?.Invoke(_latestRelease);

                return _latestRelease;
            }
            else
            {
                DebugLogger.Log("UpdateService", "Already on latest version");
                SaveLastCheckTime();
                return null;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("UpdateService", "Update check failed", ex);
            UpdateCheckFailed?.Invoke(ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Download the latest update
    /// </summary>
    public async Task<string?> DownloadUpdateAsync(GitHubRelease release, IProgress<double>? progress = null)
    {
        try
        {
            var asset = release.FindPortableAsset();
            if (asset == null)
            {
                DebugLogger.LogError("UpdateService", "No portable asset found in release");
                return null;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "QuoteBar-Update");
            Directory.CreateDirectory(tempDir);

            var zipPath = Path.Combine(tempDir, asset.Name);
            var extractDir = Path.Combine(tempDir, "extracted");

            DebugLogger.Log("UpdateService", $"Downloading {asset.Name} ({asset.FormattedSize})");

            // Download file with progress
            var response = await _httpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var buffer = new byte[8192];
            var bytesRead = 0L;

            await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var stream = await response.Content.ReadAsStreamAsync())
            {
                var readCount = 0;
                while ((readCount = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, readCount);
                    bytesRead += readCount;

                    if (totalBytes > 0 && progress != null)
                    {
                        progress.Report((double)bytesRead / totalBytes * 100);
                    }
                }
            }

            DebugLogger.Log("UpdateService", $"Download complete: {zipPath}");

            // Extract zip
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            DebugLogger.Log("UpdateService", $"Extracted to: {extractDir}");

            UpdateDownloaded?.Invoke(extractDir);

            return extractDir;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("UpdateService", "Download failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Prepare and launch the updater helper
    /// </summary>
    public bool PrepareUpdater(string updateExtractPath, string currentAppPath)
    {
        try
        {
            // Create updater script (PowerShell)
            var scriptPath = Path.Combine(Path.GetTempPath(), "QuoteBar-Updater.ps1");
            var script = GenerateUpdateScript(updateExtractPath, currentAppPath);

            File.WriteAllText(scriptPath, script);

            DebugLogger.Log("UpdateService", $"Created updater script: {scriptPath}");

            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("UpdateService", "Failed to create updater", ex);
            return false;
        }
    }

    private string GenerateUpdateScript(string updateExtractPath, string currentAppPath)
    {
        return $@"
# QuoteBar Auto-Updater Script
# Generated automatically by UpdateService

$ErrorActionPreference = 'Stop'

Write-Host 'QuoteBar Update Installer' -ForegroundColor Cyan
Write-Host '=============================' -ForegroundColor Cyan
Write-Host ''

# Wait for main app to close
Write-Host 'Waiting for QuoteBar to close...' -ForegroundColor Yellow
$process = Get-Process -Name 'QuoteBar' -ErrorAction SilentlyContinue
$timeout = 30
$elapsed = 0

while ($process -and $elapsed -lt $timeout) {{
    Start-Sleep -Seconds 1
    $elapsed++
    $process = Get-Process -Name 'QuoteBar' -ErrorAction SilentlyContinue
}}

if ($process) {{
    Write-Host 'QuoteBar did not close. Please close it manually and run this script again.' -ForegroundColor Red
    Read-Host 'Press Enter to exit'
    exit 1
}}

Write-Host 'QuoteBar closed successfully.' -ForegroundColor Green
Write-Host ''

# Backup current version
Write-Host 'Creating backup...' -ForegroundColor Yellow
$backupPath = '{Path.Combine(Path.GetTempPath(), "QuoteBar-Backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"))}'
if (Test-Path '{currentAppPath}') {{
    Copy-Item -Path '{currentAppPath}' -Destination $backupPath -Recurse -Force
    Write-Host 'Backup created at: ' -NoNewline
    Write-Host $backupPath -ForegroundColor Green
}}

# Copy new files
Write-Host ''
Write-Host 'Installing update...' -ForegroundColor Yellow
Copy-Item -Path '{updateExtractPath}\*' -Destination '{currentAppPath}' -Recurse -Force

Write-Host 'Update installed successfully!' -ForegroundColor Green
Write-Host ''

# Launch updated app
Write-Host 'Starting QuoteBar...' -ForegroundColor Yellow
& '{Path.Combine(currentAppPath, "QuoteBar.exe")}'

Write-Host ''
Write-Host 'Press any key to close this window...'
$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')
";
    }

    /// <summary>
    /// Get current application version
    /// </summary>
    public static Version GetCurrentVersion()
    {
        var version = typeof(App).Assembly.GetName().Version;
        return version ?? new Version("1.0.0");
    }

    /// <summary>
    /// Check if enough time has passed since last check
    /// </summary>
    private bool ShouldCheckForUpdate()
    {
        var lastCheck = _settings.Settings.LastUpdateCheckTime;
        if (lastCheck == default) return true;

        var hoursSinceLastCheck = (DateTime.UtcNow - lastCheck).TotalHours;
        return hoursSinceLastCheck >= 24;
    }

    /// <summary>
    /// Save last check time
    /// </summary>
    private void SaveLastCheckTime()
    {
        _settings.Settings.LastUpdateCheckTime = DateTime.UtcNow;
        _settings.Save();
    }

    /// <summary>
    /// Load update state from settings
    /// </summary>
    private void LoadUpdateState()
    {
        // Update state can be loaded from settings if needed
    }

    /// <summary>
    /// Save update state to settings
    /// </summary>
    private void SaveUpdateState()
    {
        // Update state can be persisted to settings if needed
    }

    public void Dispose()
    {
        StopPeriodicChecks();
        _httpClient?.Dispose();
    }
}
