using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
/// Manages loading GitHub Copilot OAuth credentials from various sources:
/// 1. Windows Credential Manager (GitHub CLI stores tokens here)
/// 2. GitHub CLI hosts.yml config file
/// 3. Environment variables (GITHUB_TOKEN, GH_TOKEN)
/// 4. VS Code Copilot extension settings
/// </summary>
public static class CopilotOAuthCredentialsStore
{
    // GitHub CLI config paths
    private static readonly string GhCliConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GitHub CLI", "hosts.yml");

    // VS Code Copilot credential targets in Windows Credential Manager
    private static readonly string[] CredentialManagerTargets = new[]
    {
        "github.com/github.copilot", // VS Code Copilot extension
        "git:https://github.com",     // Git credential manager
        "github.com",                  // General GitHub credential
    };

    // Cache to avoid repeated credential manager access
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

        // 1. Try environment variables first (highest priority)
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

        // 2. Try Windows Credential Manager
        try
        {
            var credManagerToken = LoadFromCredentialManager();
            if (credManagerToken != null)
            {
                UpdateCache(credManagerToken);
                Log($"Loaded token from Windows Credential Manager");
                return credManagerToken;
            }
        }
        catch (Exception ex)
        {
            lastError = ex;
            Log($"Credential Manager failed: {ex.Message}");
        }

        // 3. Try GitHub CLI hosts.yml
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
            "No GitHub Copilot credentials found. Please authenticate using 'gh auth login' or sign in to the Copilot extension in VS Code.",
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

    private static CopilotOAuthCredentials? LoadFromCredentialManager()
    {
        foreach (var target in CredentialManagerTargets)
        {
            try
            {
                var credential = CopilotCredentialManager.ReadCredential(target);
                if (!string.IsNullOrEmpty(credential?.Password))
                {
                    return new CopilotOAuthCredentials
                    {
                        AccessToken = credential.Password,
                        Username = credential.Username,
                        TokenType = "credential_manager"
                    };
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to read credential '{target}': {ex.Message}");
            }
        }

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
        try
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] CopilotOAuthCredentialsStore: {message}\n");
        }
        catch { }
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

/// <summary>
/// Credential result from Windows Credential Manager
/// </summary>
internal class CredentialResult
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

/// <summary>
/// Windows Credential Manager P/Invoke wrapper for Copilot
/// </summary>
internal static class CopilotCredentialManager
{
    public static CredentialResult? ReadCredential(string targetName)
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
                // Don't throw, just return null for other errors
                return null;
            }

            var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            
            string? username = cred.UserName != IntPtr.Zero 
                ? Marshal.PtrToStringUni(cred.UserName) 
                : null;

            string? password = null;
            if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != IntPtr.Zero)
            {
                byte[] passwordBytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, passwordBytes, 0, (int)cred.CredentialBlobSize);

                // Try UTF-16 first (Windows default), then UTF-8
                try
                {
                    password = Encoding.Unicode.GetString(passwordBytes);
                    // Validate - if it contains too many null chars, try UTF-8
                    if (password.Count(c => c == '\0') > password.Length / 4)
                    {
                        password = Encoding.UTF8.GetString(passwordBytes).TrimEnd('\0');
                    }
                }
                catch
                {
                    password = Encoding.UTF8.GetString(passwordBytes).TrimEnd('\0');
                }
            }

            return new CredentialResult
            {
                Username = username,
                Password = password
            };
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
