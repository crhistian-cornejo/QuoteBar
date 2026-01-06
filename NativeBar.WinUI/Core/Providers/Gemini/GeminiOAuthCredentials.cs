using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NativeBar.WinUI.Core.Providers.Gemini;

/// <summary>
/// Represents OAuth credentials for Google Gemini CLI
/// Stored in ~/.gemini/oauth_creds.json
/// </summary>
public sealed class GeminiOAuthCredentials
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; init; } = string.Empty;

    [JsonPropertyName("id_token")]
    public string? IdToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("expiry_date")]
    public long? ExpiryDateMs { get; init; }

    /// <summary>
    /// Expiry date as DateTime (converted from milliseconds since epoch)
    /// </summary>
    [JsonIgnore]
    public DateTime? ExpiresAt => ExpiryDateMs.HasValue
        ? DateTimeOffset.FromUnixTimeMilliseconds(ExpiryDateMs.Value).UtcDateTime
        : null;

    [JsonIgnore]
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;

    [JsonIgnore]
    public TimeSpan? ExpiresIn => ExpiresAt.HasValue
        ? ExpiresAt.Value - DateTime.UtcNow
        : null;

    /// <summary>
    /// Email extracted from id_token JWT
    /// </summary>
    [JsonIgnore]
    public string? Email { get; set; }
}

/// <summary>
/// Gemini settings from ~/.gemini/settings.json
/// </summary>
public sealed class GeminiSettings
{
    [JsonPropertyName("security")]
    public GeminiSecuritySettings? Security { get; set; }
}

public sealed class GeminiSecuritySettings
{
    [JsonPropertyName("auth")]
    public GeminiAuthSettings? Auth { get; set; }
}

public sealed class GeminiAuthSettings
{
    [JsonPropertyName("selectedType")]
    public string? SelectedType { get; set; }
}

public enum GeminiAuthType
{
    Unknown,
    OAuthPersonal,
    ApiKey,
    VertexAI
}

public class GeminiOAuthCredentialsException : Exception
{
    public GeminiOAuthCredentialsException(string message) : base(message) { }
    public GeminiOAuthCredentialsException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Manages loading Gemini OAuth credentials from ~/.gemini/oauth_creds.json
/// Compatible with Gemini CLI authentication
/// </summary>
public static class GeminiOAuthCredentialsStore
{
    // Gemini CLI config paths
    private static readonly string GeminiDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gemini");

    private static readonly string OAuthCredsPath = Path.Combine(GeminiDir, "oauth_creds.json");
    private static readonly string SettingsPath = Path.Combine(GeminiDir, "settings.json");

    // Cache to avoid repeated file reads
    private static GeminiOAuthCredentials? _cachedCredentials;
    private static DateTime? _cacheTimestamp;
    private static readonly TimeSpan CacheValidityDuration = TimeSpan.FromMinutes(1);
    private static readonly object _cacheLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Load credentials from ~/.gemini/oauth_creds.json
    /// </summary>
    public static GeminiOAuthCredentials Load()
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

        // Check auth type from settings
        var authType = GetAuthType();
        if (authType != GeminiAuthType.OAuthPersonal)
        {
            throw new GeminiOAuthCredentialsException(
                $"Gemini auth type is '{authType}'. Only OAuth personal authentication is supported. " +
                "Please authenticate using 'gemini auth login'.");
        }

        // Load OAuth credentials
        if (!File.Exists(OAuthCredsPath))
        {
            throw new GeminiOAuthCredentialsException(
                $"Gemini credentials not found at {OAuthCredsPath}. " +
                "Please authenticate using 'gemini auth login'.");
        }

        try
        {
            var json = File.ReadAllText(OAuthCredsPath);
            var creds = JsonSerializer.Deserialize<GeminiOAuthCredentials>(json, JsonOptions);

            if (creds == null || string.IsNullOrEmpty(creds.AccessToken))
            {
                throw new GeminiOAuthCredentialsException(
                    "Invalid Gemini credentials file: missing access_token");
            }

            // Extract email from id_token if present
            if (!string.IsNullOrEmpty(creds.IdToken))
            {
                creds.Email = ExtractEmailFromIdToken(creds.IdToken);
            }

            UpdateCache(creds);
            Log($"Loaded credentials, expires at: {creds.ExpiresAt}, email: {creds.Email}");

            return creds;
        }
        catch (JsonException ex)
        {
            throw new GeminiOAuthCredentialsException(
                $"Failed to parse Gemini credentials: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Try to load credentials, returns null if not available
    /// </summary>
    public static GeminiOAuthCredentials? TryLoad()
    {
        try
        {
            return Load();
        }
        catch (Exception ex)
        {
            Log($"TryLoad failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Check if credentials exist and are valid
    /// </summary>
    public static bool HasValidCredentials()
    {
        var creds = TryLoad();
        return creds != null && !string.IsNullOrEmpty(creds.AccessToken);
    }

    /// <summary>
    /// Get the current auth type from settings.json
    /// </summary>
    public static GeminiAuthType GetAuthType()
    {
        if (!File.Exists(SettingsPath))
        {
            // No settings file - check if oauth_creds.json exists
            if (File.Exists(OAuthCredsPath))
            {
                return GeminiAuthType.OAuthPersonal;
            }
            return GeminiAuthType.Unknown;
        }

        try
        {
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<GeminiSettings>(json, JsonOptions);

            var authType = settings?.Security?.Auth?.SelectedType?.ToLowerInvariant();

            return authType switch
            {
                "oauth-personal" => GeminiAuthType.OAuthPersonal,
                "api-key" => GeminiAuthType.ApiKey,
                "vertex-ai" => GeminiAuthType.VertexAI,
                _ => GeminiAuthType.Unknown
            };
        }
        catch (Exception ex)
        {
            Log($"Failed to read settings: {ex.Message}");
            return GeminiAuthType.Unknown;
        }
    }

    /// <summary>
    /// Update credentials file with new tokens after refresh
    /// </summary>
    public static void SaveCredentials(GeminiOAuthCredentials credentials)
    {
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(GeminiDir);

            var json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(OAuthCredsPath, json);

            // Update cache
            UpdateCache(credentials);
            Log("Saved updated credentials");
        }
        catch (Exception ex)
        {
            Log($"Failed to save credentials: {ex.Message}");
            throw new GeminiOAuthCredentialsException($"Failed to save credentials: {ex.Message}", ex);
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

    private static void UpdateCache(GeminiOAuthCredentials creds)
    {
        lock (_cacheLock)
        {
            _cachedCredentials = creds;
            _cacheTimestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Extract email from JWT id_token
    /// </summary>
    private static string? ExtractEmailFromIdToken(string idToken)
    {
        try
        {
            // JWT format: header.payload.signature
            var parts = idToken.Split('.');
            if (parts.Length != 3)
            {
                Log("Invalid JWT format");
                return null;
            }

            // Decode payload (middle part)
            var payload = parts[1];

            // Add padding if needed for Base64
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            // Replace URL-safe characters
            payload = payload.Replace('-', '+').Replace('_', '/');

            var bytes = Convert.FromBase64String(payload);
            var json = System.Text.Encoding.UTF8.GetString(bytes);

            // Parse JSON to extract email
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("email", out var emailElement))
            {
                return emailElement.GetString();
            }

            return null;
        }
        catch (Exception ex)
        {
            Log($"Failed to extract email from id_token: {ex.Message}");
            return null;
        }
    }

    private static void Log(string message)
    {
        try
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] GeminiOAuthCredentialsStore: {message}\n");
        }
        catch { }
    }
}
