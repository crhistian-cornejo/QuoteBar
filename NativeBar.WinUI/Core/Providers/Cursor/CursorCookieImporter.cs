using System.Data.SQLite;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NativeBar.WinUI.Core.Providers.Cursor;

/// <summary>
/// Represents a session with cookies from a browser source
/// </summary>
public sealed class CursorSessionInfo
{
    public required string CookieHeader { get; init; }
    public required string SourceLabel { get; init; }
    public required List<BrowserCookie> Cookies { get; init; }
}

/// <summary>
/// Represents a single browser cookie
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
/// Browser types supported for cookie import
/// </summary>
public enum BrowserType
{
    Edge,
    Chrome,
    Firefox,
    Brave
}

/// <summary>
/// Imports Cursor session cookies from installed browsers on Windows
/// </summary>
public static class CursorCookieImporter
{
    private static readonly string[] CursorDomains = { "cursor.com", ".cursor.com", "cursor.sh", ".cursor.sh" };

    private static readonly HashSet<string> SessionCookieNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WorkosCursorSessionToken",
        "__Secure-next-auth.session-token",
        "next-auth.session-token"
    };

    /// <summary>
    /// Default browser import order for Windows (Edge first since it's most common)
    /// </summary>
    public static readonly BrowserType[] DefaultImportOrder =
    {
        BrowserType.Edge,
        BrowserType.Chrome,
        BrowserType.Brave,
        BrowserType.Firefox
    };

    /// <summary>
    /// Try to import Cursor session cookies from browsers in priority order
    /// </summary>
    public static CursorSessionInfo? ImportSession(
        BrowserType[]? importOrder = null,
        Action<string>? logger = null)
    {
        var log = (string msg) => logger?.Invoke($"[cookie-import] {msg}");
        var browsers = importOrder ?? DefaultImportOrder;

        foreach (var browser in browsers)
        {
            try
            {
                log($"Trying {browser}...");
                var cookies = ImportFromBrowser(browser, log);

                if (cookies.Count > 0 && cookies.Any(c => SessionCookieNames.Contains(c.Name)))
                {
                    var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                    log($"Found {cookies.Count} Cursor cookies in {browser}");

                    return new CursorSessionInfo
                    {
                        CookieHeader = cookieHeader,
                        SourceLabel = browser.ToString(),
                        Cookies = cookies
                    };
                }
                else if (cookies.Count > 0)
                {
                    log($"{browser} has cookies but no session token");
                }
            }
            catch (Exception ex)
            {
                log($"{browser} failed: {ex.Message}");
            }
        }

        log("No valid session found in any browser");
        return null;
    }

    /// <summary>
    /// Check if a valid Cursor session exists in any browser
    /// </summary>
    public static bool HasSession(Action<string>? logger = null)
    {
        return ImportSession(logger: logger) != null;
    }

    private static List<BrowserCookie> ImportFromBrowser(BrowserType browser, Action<string>? log)
    {
        return browser switch
        {
            BrowserType.Chrome => ImportFromChromium(GetChromeCookiePath(), GetChromeLocalStatePath(), log),
            BrowserType.Edge => ImportFromChromium(GetEdgeCookiePath(), GetEdgeLocalStatePath(), log),
            BrowserType.Brave => ImportFromChromium(GetBraveCookiePath(), GetBraveLocalStatePath(), log),
            BrowserType.Firefox => ImportFromFirefox(log),
            _ => new List<BrowserCookie>()
        };
    }

    #region Chromium-based browsers (Chrome, Edge, Brave)

    private static List<BrowserCookie> ImportFromChromium(string? cookiePath, string? localStatePath, Action<string>? log)
    {
        var cookies = new List<BrowserCookie>();

        if (string.IsNullOrEmpty(cookiePath) || !File.Exists(cookiePath))
        {
            log?.Invoke($"Cookie file not found: {cookiePath}");
            return cookies;
        }

        // Copy the database to a temp file (browser may have it locked)
        var tempPath = Path.Combine(Path.GetTempPath(), $"cursor_cookies_{Guid.NewGuid()}.db");

        try
        {
            File.Copy(cookiePath, tempPath, true);

            // Get the decryption key
            byte[]? masterKey = null;
            if (!string.IsNullOrEmpty(localStatePath) && File.Exists(localStatePath))
            {
                masterKey = GetChromiumMasterKey(localStatePath, log);
            }

            using var connection = new SQLiteConnection($"Data Source={tempPath};Read Only=True;");
            connection.Open();

            // Build domain filter for SQL
            var domainFilter = string.Join(" OR ", CursorDomains.Select((d, i) => $"host_key LIKE @domain{i}"));
            var sql = $"SELECT host_key, name, encrypted_value, path, expires_utc, is_secure, is_httponly FROM cookies WHERE {domainFilter}";

            using var command = new SQLiteCommand(sql, connection);
            for (int i = 0; i < CursorDomains.Length; i++)
            {
                command.Parameters.AddWithValue($"@domain{i}", $"%{CursorDomains[i]}%");
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                var encryptedValue = (byte[])reader["encrypted_value"];
                var value = DecryptChromiumValue(encryptedValue, masterKey, log);

                if (string.IsNullOrEmpty(value))
                    continue;

                cookies.Add(new BrowserCookie
                {
                    Name = name,
                    Value = value,
                    Domain = reader.GetString(0),
                    Path = reader.GetString(3),
                    Expires = ChromiumTimestampToDateTime(reader.GetInt64(4)),
                    IsSecure = reader.GetInt32(5) == 1,
                    IsHttpOnly = reader.GetInt32(6) == 1
                });
            }

            log?.Invoke($"Found {cookies.Count} Cursor cookies");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }

        return cookies;
    }

    private static byte[]? GetChromiumMasterKey(string localStatePath, Action<string>? log)
    {
        try
        {
            var json = File.ReadAllText(localStatePath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt) ||
                !osCrypt.TryGetProperty("encrypted_key", out var encryptedKeyElement))
            {
                log?.Invoke("No encrypted_key in Local State");
                return null;
            }

            var encryptedKeyBase64 = encryptedKeyElement.GetString();
            if (string.IsNullOrEmpty(encryptedKeyBase64))
                return null;

            var encryptedKey = Convert.FromBase64String(encryptedKeyBase64);

            // Remove "DPAPI" prefix (5 bytes)
            if (encryptedKey.Length > 5 && Encoding.ASCII.GetString(encryptedKey, 0, 5) == "DPAPI")
            {
                encryptedKey = encryptedKey[5..];
            }

            // Decrypt using Windows DPAPI
            var decryptedKey = ProtectedData.Unprotect(encryptedKey, null, DataProtectionScope.CurrentUser);
            log?.Invoke("Got master key from DPAPI");
            return decryptedKey;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Failed to get master key: {ex.Message}");
            return null;
        }
    }

    private static string? DecryptChromiumValue(byte[] encryptedValue, byte[]? masterKey, Action<string>? log)
    {
        if (encryptedValue == null || encryptedValue.Length == 0)
            return null;

        try
        {
            // Check for v10/v11 encryption (AES-GCM with prefix)
            if (encryptedValue.Length > 15 &&
                Encoding.ASCII.GetString(encryptedValue, 0, 3) == "v10" ||
                Encoding.ASCII.GetString(encryptedValue, 0, 3) == "v11")
            {
                if (masterKey == null)
                {
                    log?.Invoke("v10/v11 encrypted but no master key");
                    return null;
                }

                // v10/v11 format: "v10" (3 bytes) + nonce (12 bytes) + ciphertext + tag (16 bytes)
                var nonce = encryptedValue[3..15];
                var ciphertext = encryptedValue[15..];

                // AES-GCM decryption
                using var aesGcm = new AesGcm(masterKey, 16);
                var plaintext = new byte[ciphertext.Length - 16];
                var tag = ciphertext[^16..];
                var actualCiphertext = ciphertext[..^16];

                aesGcm.Decrypt(nonce, actualCiphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }

            // Fallback: Old DPAPI encryption (no prefix)
            var decrypted = ProtectedData.Unprotect(encryptedValue, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Decrypt failed: {ex.Message}");
            return null;
        }
    }

    private static DateTime? ChromiumTimestampToDateTime(long timestamp)
    {
        if (timestamp == 0)
            return null;

        // Chromium uses microseconds since 1601-01-01
        try
        {
            var epoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddTicks(timestamp * 10);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Firefox

    private static List<BrowserCookie> ImportFromFirefox(Action<string>? log)
    {
        var cookies = new List<BrowserCookie>();
        var profilePath = GetFirefoxDefaultProfile();

        if (string.IsNullOrEmpty(profilePath))
        {
            log?.Invoke("Firefox profile not found");
            return cookies;
        }

        var cookiePath = Path.Combine(profilePath, "cookies.sqlite");
        if (!File.Exists(cookiePath))
        {
            log?.Invoke("Firefox cookies.sqlite not found");
            return cookies;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"cursor_firefox_cookies_{Guid.NewGuid()}.db");

        try
        {
            File.Copy(cookiePath, tempPath, true);

            using var connection = new SQLiteConnection($"Data Source={tempPath};Read Only=True;");
            connection.Open();

            // Firefox stores cookies in plain text
            var domainFilter = string.Join(" OR ", CursorDomains.Select((d, i) => $"host LIKE @domain{i}"));
            var sql = $"SELECT host, name, value, path, expiry, isSecure, isHttpOnly FROM moz_cookies WHERE {domainFilter}";

            using var command = new SQLiteCommand(sql, connection);
            for (int i = 0; i < CursorDomains.Length; i++)
            {
                command.Parameters.AddWithValue($"@domain{i}", $"%{CursorDomains[i]}%");
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                var value = reader.GetString(2);

                if (string.IsNullOrEmpty(value))
                    continue;

                cookies.Add(new BrowserCookie
                {
                    Name = name,
                    Value = value,
                    Domain = reader.GetString(0),
                    Path = reader.GetString(3),
                    Expires = FirefoxTimestampToDateTime(reader.GetInt64(4)),
                    IsSecure = reader.GetInt32(5) == 1,
                    IsHttpOnly = reader.GetInt32(6) == 1
                });
            }

            log?.Invoke($"Found {cookies.Count} Firefox Cursor cookies");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }

        return cookies;
    }

    private static string? GetFirefoxDefaultProfile()
    {
        var firefoxPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Mozilla", "Firefox", "Profiles");

        if (!Directory.Exists(firefoxPath))
            return null;

        // Look for default profile (ends with .default or .default-release)
        var profiles = Directory.GetDirectories(firefoxPath);
        var defaultProfile = profiles.FirstOrDefault(p =>
            p.EndsWith(".default-release", StringComparison.OrdinalIgnoreCase) ||
            p.EndsWith(".default", StringComparison.OrdinalIgnoreCase));

        return defaultProfile ?? profiles.FirstOrDefault();
    }

    private static DateTime? FirefoxTimestampToDateTime(long timestamp)
    {
        if (timestamp == 0)
            return null;

        // Firefox uses seconds since Unix epoch
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Browser Paths

    private static string? GetChromeCookiePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Network", "Cookies");
        if (File.Exists(path)) return path;

        // Fallback to old location
        path = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cookies");
        return File.Exists(path) ? path : null;
    }

    private static string? GetChromeLocalStatePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(localAppData, "Google", "Chrome", "User Data", "Local State");
        return File.Exists(path) ? path : null;
    }

    private static string? GetEdgeCookiePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Network", "Cookies");
        if (File.Exists(path)) return path;

        path = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cookies");
        return File.Exists(path) ? path : null;
    }

    private static string? GetEdgeLocalStatePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Local State");
        return File.Exists(path) ? path : null;
    }

    private static string? GetBraveCookiePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Network", "Cookies");
        if (File.Exists(path)) return path;

        path = Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cookies");
        return File.Exists(path) ? path : null;
    }

    private static string? GetBraveLocalStatePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Local State");
        return File.Exists(path) ? path : null;
    }

    #endregion
}
