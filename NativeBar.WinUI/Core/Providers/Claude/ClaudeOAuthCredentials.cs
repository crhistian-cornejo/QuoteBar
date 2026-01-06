using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NativeBar.WinUI.Core.Providers.Claude;

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
/// Manages loading Claude OAuth credentials from Windows Credential Manager or file
/// </summary>
public static class ClaudeOAuthCredentialsStore
{
    private static readonly string CredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    private const string CredentialManagerTarget = "Claude Code-credentials";

    // Cache to avoid repeated credential manager access
    private static ClaudeOAuthCredentials? _cachedCredentials;
    private static DateTime? _cacheTimestamp;
    private static readonly TimeSpan CacheValidityDuration = TimeSpan.FromMinutes(1);
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Load credentials, preferring Windows Credential Manager, falling back to file
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

        Exception? lastError = null;

        // Try Windows Credential Manager first
        try
        {
            var credManagerData = LoadFromCredentialManager();
            if (!string.IsNullOrEmpty(credManagerData))
            {
                var creds = ClaudeOAuthCredentials.Parse(credManagerData);
                UpdateCache(creds);
                return creds;
            }
        }
        catch (Exception ex)
        {
            lastError = ex;
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] ClaudeOAuthCredentialsStore: Credential Manager failed: {ex.Message}\n");
        }

        // Fall back to file
        try
        {
            var fileData = LoadFromFile();
            var creds = ClaudeOAuthCredentials.Parse(fileData);
            UpdateCache(creds);
            return creds;
        }
        catch (Exception ex)
        {
            if (lastError != null)
                throw new ClaudeOAuthCredentialsException($"Failed to load credentials: {lastError.Message}", lastError);
            throw new ClaudeOAuthCredentialsException($"Failed to load credentials from file: {ex.Message}", ex);
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
            throw new ClaudeOAuthCredentialsException($"Credentials file not found: {CredentialsPath}");

        return File.ReadAllText(CredentialsPath);
    }

    private static string? LoadFromCredentialManager()
    {
        try
        {
            var credential = CredentialManager.ReadCredential(CredentialManagerTarget);
            return credential;
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] CredentialManager.ReadCredential failed: {ex.Message}\n");
            return null;
        }
    }
}

/// <summary>
/// Windows Credential Manager P/Invoke wrapper
/// </summary>
internal static class CredentialManager
{
    public static string? ReadCredential(string targetName)
    {
        IntPtr credPtr = IntPtr.Zero;
        try
        {
            bool success = CredRead(targetName, CRED_TYPE_GENERIC, 0, out credPtr);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_NOT_FOUND)
                    return null;
                throw new ClaudeOAuthCredentialsException($"CredRead failed with error {error}");
            }

            var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero)
                return null;

            byte[] passwordBytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, passwordBytes, 0, (int)cred.CredentialBlobSize);

            // Try UTF-16 first (Windows default), then UTF-8
            string password;
            try
            {
                password = Encoding.Unicode.GetString(passwordBytes);
                // Check if it looks like valid JSON
                if (!password.TrimStart().StartsWith("{"))
                {
                    password = Encoding.UTF8.GetString(passwordBytes);
                }
            }
            catch
            {
                password = Encoding.UTF8.GetString(passwordBytes);
            }

            return password;
        }
        finally
        {
            if (credPtr != IntPtr.Zero)
                CredFree(credPtr);
        }
    }

    private const int CRED_TYPE_GENERIC = 1;
    private const int ERROR_NOT_FOUND = 1168;

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
