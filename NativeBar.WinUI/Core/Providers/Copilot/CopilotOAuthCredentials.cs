using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NativeBar.WinUI.Core.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NativeBar.WinUI.Core.Providers.Copilot;

/// <summary>
/// Represents OAuth credentials for GitHub Copilot API
/// Copilot uses GitHub's OAuth tokens for authentication
/// </summary>
public sealed class CopilotOAuthCredentials
{
    public string AccessToken { get; init; } = string.Empty;
    public string? RefreshToken { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? Username { get; init; }
    public string? TokenType { get; init; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;

    public TimeSpan? ExpiresIn => ExpiresAt.HasValue 
        ? ExpiresAt.Value - DateTime.UtcNow 
        : null;
}

public class CopilotOAuthCredentialsException : Exception
{
    public CopilotOAuthCredentialsException(string message) : base(message) { }
    public CopilotOAuthCredentialsException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Manages loading GitHub Copilot OAuth credentials from safe sources:
/// 1. Environment variables (GITHUB_TOKEN, GH_TOKEN, COPILOT_TOKEN)
/// 2. GitHub CLI hosts.yml config file (user's own authenticated session)
/// 
/// SECURITY NOTE: This class intentionally does NOT access:
/// - Windows Credential Manager entries from other applications (VS Code, Git)
/// - Browser credential databases
/// - Any third-party application's stored credentials
/// 
/// This avoids antivirus/EDR detections for credential theft (MITRE T1555.003/T1555.004)
/// </summary>
public static class CopilotOAuthCredentialsStore
{
    // GitHub CLI config path - this is the user's own authenticated session
    private static readonly string GhCliConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GitHub CLI", "hosts.yml");

    // Cache to avoid repeated file access
    private static CopilotOAuthCredentials? _cachedCredentials;
    private static DateTime? _cacheTimestamp;
    private static readonly TimeSpan CacheValidityDuration = TimeSpan.FromMinutes(1);
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Load credentials from available sources, in priority order
    /// </summary>
    public static CopilotOAuthCredentials Load()
    {
        lock (_cacheLock)
        {
            // Check cache first
            if (_cachedCredentials != null &&
                _cacheTimestamp.HasValue &&
                DateTime.UtcNow - _cacheTimestamp.Value < CacheValidityDuration)
            {
                return _cachedCredentials;
            }
        }

        Exception? lastError = null;

        // 1. Try environment variables first (highest priority, most explicit)
        try
        {
            var envToken = LoadFromEnvironment();
            if (!string.IsNullOrEmpty(envToken))
            {
                var creds = new CopilotOAuthCredentials
                {
                    AccessToken = envToken,
                    TokenType = "environment"
                };
                UpdateCache(creds);
                Log($"Loaded token from environment variable");
                return creds;
            }
        }
        catch (Exception ex)
        {
            lastError = ex;
            Log($"Environment variable check failed: {ex.Message}");
        }

        // 2. Try GitHub CLI hosts.yml (user's own authenticated session via 'gh auth login')
        try
        {
            var ghCliCreds = LoadFromGhCli();
            if (ghCliCreds != null)
            {
                UpdateCache(ghCliCreds);
                Log($"Loaded token from GitHub CLI config");
                return ghCliCreds;
            }
        }
        catch (Exception ex)
        {
            lastError = ex;
            Log($"GitHub CLI config failed: {ex.Message}");
        }

        throw new CopilotOAuthCredentialsException(
            "No GitHub Copilot credentials found. Please authenticate using 'gh auth login' in your terminal, or set the GITHUB_TOKEN environment variable.",
            lastError!);
    }

    /// <summary>
    /// Try to load credentials, returns null if not available
    /// </summary>
    public static CopilotOAuthCredentials? TryLoad()
    {
        try
        {
            return Load();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Invalidate the cached credentials
    /// </summary>
    public static void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedCredentials = null;
            _cacheTimestamp = null;
        }
    }

    private static void UpdateCache(CopilotOAuthCredentials creds)
    {
        lock (_cacheLock)
        {
            _cachedCredentials = creds;
            _cacheTimestamp = DateTime.UtcNow;
        }
    }

    private static string? LoadFromEnvironment()
    {
        // Try common GitHub token environment variables
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token)) return token;

        token = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrEmpty(token)) return token;

        token = Environment.GetEnvironmentVariable("COPILOT_TOKEN");
        if (!string.IsNullOrEmpty(token)) return token;

        return null;
    }

    private static CopilotOAuthCredentials? LoadFromGhCli()
    {
        if (!File.Exists(GhCliConfigPath))
        {
            Log($"GitHub CLI config not found: {GhCliConfigPath}");
            return null;
        }

        try
        {
            var yaml = File.ReadAllText(GhCliConfigPath);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var hosts = deserializer.Deserialize<Dictionary<string, GhCliHost>>(yaml);
            
            if (hosts != null && hosts.TryGetValue("github.com", out var host))
            {
                if (!string.IsNullOrEmpty(host.OauthToken))
                {
                    return new CopilotOAuthCredentials
                    {
                        AccessToken = host.OauthToken,
                        Username = host.User,
                        TokenType = "gh_cli"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to parse GitHub CLI config: {ex.Message}");
        }

        return null;
    }

    private static void Log(string message)
    {
        DebugLogger.Log("CopilotOAuthCredentialsStore", message);
    }

    private class GhCliHost
    {
        [YamlMember(Alias = "oauth_token")]
        public string? OauthToken { get; set; }

        [YamlMember(Alias = "user")]
        public string? User { get; set; }

        [YamlMember(Alias = "git_protocol")]
        public string? GitProtocol { get; set; }
    }
}
