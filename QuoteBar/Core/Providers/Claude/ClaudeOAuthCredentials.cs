using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuoteBar.Core.Providers.Claude;

/// <summary>
/// Represents OAuth credentials for Claude API
/// </summary>
public sealed class ClaudeOAuthCredentials
{
    public string AccessToken { get; init; } = string.Empty;
    public string? RefreshToken { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string[] Scopes { get; init; } = Array.Empty<string>();
    public string? RateLimitTier { get; init; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;

    public TimeSpan? ExpiresIn => ExpiresAt.HasValue 
        ? ExpiresAt.Value - DateTime.UtcNow 
        : null;

    public bool HasScope(string scope) => Scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);

    public static ClaudeOAuthCredentials Parse(string json)
    {
        var root = JsonSerializer.Deserialize<CredentialsRoot>(json, JsonOptions);
        if (root?.ClaudeAiOauth == null)
            throw new ClaudeOAuthCredentialsException("Missing claudeAiOauth in credentials");

        var oauth = root.ClaudeAiOauth;
        var accessToken = oauth.AccessToken?.Trim() ?? string.Empty;
        
        if (string.IsNullOrEmpty(accessToken))
            throw new ClaudeOAuthCredentialsException("Missing access token in credentials");

        DateTime? expiresAt = null;
        if (oauth.ExpiresAt.HasValue)
        {
            // ExpiresAt is in milliseconds since epoch
            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds((long)oauth.ExpiresAt.Value).UtcDateTime;
        }

        return new ClaudeOAuthCredentials
        {
            AccessToken = accessToken,
            RefreshToken = oauth.RefreshToken,
            ExpiresAt = expiresAt,
            Scopes = oauth.Scopes ?? Array.Empty<string>(),
            RateLimitTier = oauth.RateLimitTier
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class CredentialsRoot
    {
        [JsonPropertyName("claudeAiOauth")]
        public OAuthData? ClaudeAiOauth { get; set; }
    }

    private sealed class OAuthData
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expiresAt")]
        public double? ExpiresAt { get; set; }

        [JsonPropertyName("scopes")]
        public string[]? Scopes { get; set; }

        [JsonPropertyName("rateLimitTier")]
        public string? RateLimitTier { get; set; }
    }
}

public class ClaudeOAuthCredentialsException : Exception
{
    public ClaudeOAuthCredentialsException(string message) : base(message) { }
    public ClaudeOAuthCredentialsException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Manages loading Claude OAuth credentials from the Claude CLI credentials file.
/// 
/// SECURITY NOTE: This class intentionally does NOT use:
/// - Windows Credential Manager P/Invoke (advapi32.dll CredRead)
/// - Any third-party application's stored credentials
/// 
/// It only reads the Claude CLI's own credentials file (~/.claude/.credentials.json)
/// which the user explicitly created via 'claude login' command.
/// 
/// This avoids antivirus/EDR detections for credential theft (MITRE T1555.003/T1555.004)
/// </summary>
public static class ClaudeOAuthCredentialsStore
{
    private static readonly string CredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    // Cache to avoid repeated file access
    private static ClaudeOAuthCredentials? _cachedCredentials;
    private static DateTime? _cacheTimestamp;
    private static readonly TimeSpan CacheValidityDuration = TimeSpan.FromMinutes(1);
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Load credentials from Claude CLI credentials file
    /// </summary>
    public static ClaudeOAuthCredentials Load()
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

        // Load from file
        try
        {
            var fileData = LoadFromFile();
            var creds = ClaudeOAuthCredentials.Parse(fileData);
            UpdateCache(creds);
            return creds;
        }
        catch (FileNotFoundException)
        {
            throw new ClaudeOAuthCredentialsException(
                $"Claude credentials not found. Please run 'claude login' in your terminal to authenticate.");
        }
        catch (Exception ex) when (ex is not ClaudeOAuthCredentialsException)
        {
            throw new ClaudeOAuthCredentialsException($"Failed to load credentials: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Try to load credentials, returns null if not available
    /// </summary>
    public static ClaudeOAuthCredentials? TryLoad()
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

    private static void UpdateCache(ClaudeOAuthCredentials creds)
    {
        lock (_cacheLock)
        {
            _cachedCredentials = creds;
            _cacheTimestamp = DateTime.UtcNow;
        }
    }

    private static string LoadFromFile()
    {
        if (!File.Exists(CredentialsPath))
            throw new FileNotFoundException($"Credentials file not found", CredentialsPath);

        return File.ReadAllText(CredentialsPath);
    }
}
