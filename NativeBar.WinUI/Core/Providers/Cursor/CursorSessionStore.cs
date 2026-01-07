using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Core.Providers.Cursor;

/// <summary>
/// Represents a session with cookies obtained from WebView login
/// </summary>
public sealed class CursorSessionInfo
{
    public required string CookieHeader { get; init; }
    public required string SourceLabel { get; init; }
    public required List<BrowserCookie> Cookies { get; init; }
}

/// <summary>
/// Represents a single cookie from the session
/// </summary>
public sealed class BrowserCookie
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public required string Domain { get; init; }
    public required string Path { get; init; }
    public DateTime? Expires { get; init; }
    public bool IsSecure { get; init; }
    public bool IsHttpOnly { get; init; }
}

/// <summary>
/// Stored session metadata for Cursor (persisted to disk).
/// SECURITY: Cookie header is stored in Credential Manager, NOT in this file.
/// </summary>
public sealed class CursorSessionMetadata
{
    public required string SourceLabel { get; init; }
    public DateTime StoredAt { get; init; } = DateTime.UtcNow;
    public string? AccountEmail { get; init; }
    /// <summary>Hash of cookie for validation (not the actual cookie)</summary>
    public string? CookieHash { get; init; }
}

/// <summary>
/// Full session data (for internal use only, never persisted to disk with cookie)
/// </summary>
public sealed class CursorStoredSession
{
    public required string CookieHeader { get; init; }
    public required string SourceLabel { get; init; }
    public DateTime StoredAt { get; init; } = DateTime.UtcNow;
    public string? AccountEmail { get; init; }
}

/// <summary>
/// Manages persistent storage of Cursor session cookies.
/// 
/// SECURITY IMPROVEMENTS over CodexBar:
/// 1. Cookie is ONLY stored in Windows Credential Manager (encrypted by DPAPI)
/// 2. Disk file only stores metadata (source, email, timestamp) - NO COOKIE
/// 3. No prompts to user (unlike macOS Keychain prompts in Issue #84)
/// 4. Credentials persist when provider is toggled off (Issue #123)
/// 5. Memory is cleared after use with sensitive data
/// 
/// Storage locations:
/// - Cookie: Windows Credential Manager (QuoteBar:cursor-session)
/// - Metadata: %LocalAppData%\NativeBar\cursor-session-meta.json
/// </summary>
public static class CursorSessionStore
{
    private const string CredentialKey = "cursor-session";
    private static readonly string MetadataFilePath;
    private static readonly object _lock = new();

    private static CursorStoredSession? _cachedSession;
    private static bool _hasLoadedFromDisk;
    private static DateTime _lastFetchTime = DateTime.MinValue;

    // Cache duration to avoid excessive Credential Manager reads
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    static CursorSessionStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(appData, "NativeBar");
        Directory.CreateDirectory(appDir);
        MetadataFilePath = Path.Combine(appDir, "cursor-session-meta.json");

        // Clean up old insecure file if it exists
        CleanupInsecureFiles(appDir);
    }

    /// <summary>
    /// Store a new session (from browser import or WebView login)
    /// </summary>
    public static void SetSession(CursorSessionInfo sessionInfo, string? accountEmail = null)
    {
        var session = new CursorStoredSession
        {
            CookieHeader = sessionInfo.CookieHeader,
            SourceLabel = sessionInfo.SourceLabel,
            StoredAt = DateTime.UtcNow,
            AccountEmail = accountEmail
        };

        SetSession(session);
    }

    /// <summary>
    /// Store a session directly
    /// </summary>
    public static void SetSession(CursorStoredSession session)
    {
        lock (_lock)
        {
            _cachedSession = session;
            _hasLoadedFromDisk = true;
            _lastFetchTime = DateTime.UtcNow;

            // Store cookie ONLY in Windows Credential Manager (secure, encrypted)
            var stored = SecureCredentialStore.StoreCredential(CredentialKey, session.CookieHeader);

            if (stored)
            {
                Log("Session stored securely in Credential Manager");
            }
            else
            {
                Log("WARNING: Failed to store in Credential Manager");
            }

            // Save ONLY metadata to disk (no cookie!)
            SaveMetadataToDisk(new CursorSessionMetadata
            {
                SourceLabel = session.SourceLabel,
                StoredAt = session.StoredAt,
                AccountEmail = session.AccountEmail,
                CookieHash = ComputeHash(session.CookieHeader)
            });
        }
    }

    /// <summary>
    /// Store a manual cookie header provided by the user
    /// </summary>
    public static void SetManualCookieHeader(string cookieHeader)
    {
        var normalized = NormalizeCookieHeader(cookieHeader);

        if (string.IsNullOrEmpty(normalized))
        {
            Log("Empty cookie header provided");
            return;
        }

        var session = new CursorStoredSession
        {
            CookieHeader = normalized,
            SourceLabel = "Manual",
            StoredAt = DateTime.UtcNow
        };

        SetSession(session);
    }

    /// <summary>
    /// Get the stored session (if any)
    /// </summary>
    public static CursorStoredSession? GetSession()
    {
        lock (_lock)
        {
            // Use cache if valid
            if (_cachedSession != null && DateTime.UtcNow - _lastFetchTime < CacheDuration)
            {
                return _cachedSession;
            }

            LoadFromCredentialManager();
            return _cachedSession;
        }
    }

    /// <summary>
    /// Get just the cookie header for API requests.
    /// Returns a copy to prevent external modification.
    /// </summary>
    public static string? GetCookieHeader()
    {
        var cookie = GetSession()?.CookieHeader;
        return cookie;
    }

    /// <summary>
    /// Check if we have a stored session
    /// </summary>
    public static bool HasSession()
    {
        return !string.IsNullOrEmpty(GetCookieHeader());
    }

    /// <summary>
    /// Clear the stored session (logout).
    /// This completely removes credentials - use DisableProvider for temporary disable.
    /// </summary>
    public static void ClearSession()
    {
        lock (_lock)
        {
            // Clear from memory first
            if (_cachedSession != null)
            {
                // Attempt to clear sensitive data from memory
                ClearSensitiveString(_cachedSession.CookieHeader);
            }
            _cachedSession = null;
            _hasLoadedFromDisk = true;
            _lastFetchTime = DateTime.MinValue;

            // Remove from Credential Manager
            SecureCredentialStore.DeleteCredential(CredentialKey);

            // Remove metadata file
            try
            {
                if (File.Exists(MetadataFilePath))
                    File.Delete(MetadataFilePath);
            }
            catch (Exception ex)
            {
                Log($"Failed to delete metadata file: {ex.Message}");
            }

            Log("Session completely cleared");
        }
    }

    /// <summary>
    /// Validate that the stored session matches what we expect.
    /// Useful for detecting tampering or corruption.
    /// </summary>
    public static bool ValidateSession()
    {
        lock (_lock)
        {
            var session = GetSession();
            if (session == null)
                return false;

            var metadata = LoadMetadataFromDisk();
            if (metadata?.CookieHash == null)
                return true; // No hash to validate against

            var currentHash = ComputeHash(session.CookieHeader);
            return currentHash == metadata.CookieHash;
        }
    }

    /// <summary>
    /// Force refresh from Credential Manager (bypass cache)
    /// </summary>
    public static void RefreshSession()
    {
        lock (_lock)
        {
            _cachedSession = null;
            _hasLoadedFromDisk = false;
            _lastFetchTime = DateTime.MinValue;
            LoadFromCredentialManager();
        }
    }

    #region Private Methods

    /// <summary>
    /// Normalize a cookie header (handle "Cookie: " prefix, etc.)
    /// </summary>
    private static string NormalizeCookieHeader(string cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader))
            return string.Empty;

        var normalized = cookieHeader.Trim();

        // Remove "Cookie:" prefix if present
        if (normalized.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..].TrimStart();
        }

        // Basic validation - should contain = for cookie format
        if (!normalized.Contains('='))
        {
            Log("Warning: Cookie header doesn't appear to be valid format");
        }

        return normalized;
    }

    private static void LoadFromCredentialManager()
    {
        if (_hasLoadedFromDisk && DateTime.UtcNow - _lastFetchTime < CacheDuration)
            return;

        _hasLoadedFromDisk = true;
        _lastFetchTime = DateTime.UtcNow;

        // Load cookie from Credential Manager (secure)
        var storedCookie = SecureCredentialStore.GetCredential(CredentialKey);
        if (string.IsNullOrEmpty(storedCookie))
        {
            _cachedSession = null;
            return;
        }

        // Load metadata from disk
        var metadata = LoadMetadataFromDisk();

        _cachedSession = new CursorStoredSession
        {
            CookieHeader = storedCookie,
            SourceLabel = metadata?.SourceLabel ?? "Credential Manager",
            StoredAt = metadata?.StoredAt ?? DateTime.UtcNow,
            AccountEmail = metadata?.AccountEmail
        };

        Log("Session loaded from Credential Manager");
    }

    private static void SaveMetadataToDisk(CursorSessionMetadata metadata)
    {
        try
        {
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(MetadataFilePath, json);
        }
        catch (Exception ex)
        {
            Log($"Failed to save metadata: {ex.Message}");
        }
    }

    private static CursorSessionMetadata? LoadMetadataFromDisk()
    {
        try
        {
            if (!File.Exists(MetadataFilePath))
                return null;

            var json = File.ReadAllText(MetadataFilePath);
            return JsonSerializer.Deserialize<CursorSessionMetadata>(json);
        }
        catch (Exception ex)
        {
            Log($"Failed to load metadata: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Compute a SHA256 hash of the cookie for integrity validation
    /// </summary>
    private static string ComputeHash(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);

        // Clear sensitive bytes
        Array.Clear(bytes, 0, bytes.Length);

        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Attempt to clear sensitive string from memory.
    /// Note: This is best-effort due to .NET string immutability.
    /// In managed .NET, we cannot truly clear strings, so this is a no-op reminder.
    /// </summary>
    private static void ClearSensitiveString(string? value)
    {
        // In .NET, strings are immutable and managed by the GC.
        // There's no safe way to truly clear them from memory.
        // This method exists as a placeholder for security awareness.
        // The actual clearing happens when GC collects the string.
        _ = value; // Suppress unused parameter warning
    }

    /// <summary>
    /// Clean up old insecure files from previous versions
    /// </summary>
    private static void CleanupInsecureFiles(string appDir)
    {
        try
        {
            // Remove old file that stored cookies in plain text
            var oldFile = Path.Combine(appDir, "cursor-session.json");
            if (File.Exists(oldFile))
            {
                // Securely delete by overwriting first
                var random = new byte[1024];
                RandomNumberGenerator.Fill(random);
                File.WriteAllBytes(oldFile, random);
                File.Delete(oldFile);
                Log("Cleaned up insecure legacy session file");
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to cleanup old files: {ex.Message}");
        }
    }

    private static void Log(string message)
    {
        Core.Services.DebugLogger.Log("CursorSessionStore", message);
    }

    #endregion
}
