using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Core.Providers.Augment;

/// <summary>
/// Reads Augment CLI session from ~/.augment/session.json
/// This is the preferred authentication method as it uses the user's own CLI session.
///
/// Priority order:
/// 1. Environment variable AUGMENT_SESSION_AUTH (JSON string)
/// 2. Session file at ~/.augment/session.json (created by 'augment login')
///
/// The session.json format:
/// {
///   "accessToken": "...",
///   "tenantURL": "https://...",
///   "scopes": ["read", "write"]
/// }
/// </summary>
public static class AugmentSessionStore
{
    private static readonly string SessionFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".augment", "session.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Cache to avoid repeated file access
    private static AugmentSession? _cachedSession;
    private static DateTime? _cacheTimestamp;
    private static readonly TimeSpan CacheValidityDuration = TimeSpan.FromMinutes(1);
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Try to load session from CLI or environment variable
    /// </summary>
    public static AugmentSession? TryLoad()
    {
        lock (_cacheLock)
        {
            // Check cache first
            if (_cachedSession != null &&
                _cacheTimestamp.HasValue &&
                DateTime.UtcNow - _cacheTimestamp.Value < CacheValidityDuration)
            {
                return _cachedSession;
            }
        }

        // 1. Try environment variable first
        try
        {
            var envSession = LoadFromEnvironment();
            if (envSession != null)
            {
                UpdateCache(envSession);
                Log("Loaded session from AUGMENT_SESSION_AUTH environment variable");
                return envSession;
            }
        }
        catch (Exception ex)
        {
            Log($"Environment variable check failed: {ex.Message}");
        }

        // 2. Try session file
        try
        {
            var fileSession = LoadFromFile();
            if (fileSession != null)
            {
                UpdateCache(fileSession);
                Log($"Loaded session from {SessionFilePath}");
                return fileSession;
            }
        }
        catch (Exception ex)
        {
            Log($"Session file check failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Check if CLI session is available
    /// </summary>
    public static bool HasSession()
    {
        return TryLoad() != null;
    }

    /// <summary>
    /// Get the access token from CLI session
    /// </summary>
    public static string? GetAccessToken()
    {
        return TryLoad()?.AccessToken;
    }

    /// <summary>
    /// Get the tenant URL from CLI session
    /// </summary>
    public static string? GetTenantUrl()
    {
        return TryLoad()?.TenantUrl;
    }

    /// <summary>
    /// Invalidate the cached session
    /// </summary>
    public static void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedSession = null;
            _cacheTimestamp = null;
        }
    }

    private static void UpdateCache(AugmentSession session)
    {
        lock (_cacheLock)
        {
            _cachedSession = session;
            _cacheTimestamp = DateTime.UtcNow;
        }
    }

    private static AugmentSession? LoadFromEnvironment()
    {
        var envValue = Environment.GetEnvironmentVariable("AUGMENT_SESSION_AUTH");
        if (string.IsNullOrEmpty(envValue))
            return null;

        try
        {
            var session = JsonSerializer.Deserialize<AugmentSession>(envValue, JsonOptions);
            if (session != null && !string.IsNullOrEmpty(session.AccessToken))
            {
                session.Source = "environment";
                return session;
            }
        }
        catch (JsonException ex)
        {
            Log($"Failed to parse AUGMENT_SESSION_AUTH: {ex.Message}");
        }

        return null;
    }

    private static AugmentSession? LoadFromFile()
    {
        if (!File.Exists(SessionFilePath))
        {
            Log($"Session file not found: {SessionFilePath}");
            return null;
        }

        try
        {
            var json = File.ReadAllText(SessionFilePath);
            var session = JsonSerializer.Deserialize<AugmentSession>(json, JsonOptions);

            if (session != null && !string.IsNullOrEmpty(session.AccessToken))
            {
                session.Source = "cli";
                return session;
            }

            Log("Session file exists but has no valid accessToken");
        }
        catch (JsonException ex)
        {
            Log($"Failed to parse session file: {ex.Message}");
        }
        catch (IOException ex)
        {
            Log($"Failed to read session file: {ex.Message}");
        }

        return null;
    }

    private static void Log(string message)
    {
        DebugLogger.Log("AugmentSessionStore", message);
    }
}

/// <summary>
/// Represents Augment CLI session data from session.json
/// </summary>
public sealed class AugmentSession
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("tenantURL")]
    public string? TenantUrl { get; set; }

    [JsonPropertyName("scopes")]
    public string[]? Scopes { get; set; }

    /// <summary>
    /// Source of the session (cli, environment)
    /// </summary>
    [JsonIgnore]
    public string Source { get; set; } = "unknown";

    /// <summary>
    /// Check if the session has valid credentials
    /// </summary>
    [JsonIgnore]
    public bool IsValid => !string.IsNullOrEmpty(AccessToken);
}
