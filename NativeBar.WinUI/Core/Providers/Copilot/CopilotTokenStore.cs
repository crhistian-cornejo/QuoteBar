using System.IO;
using System.Text.Json;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Core.Providers.Copilot;

/// <summary>
/// Stores and retrieves GitHub OAuth tokens for Copilot.
/// Tokens are stored in the user's app data folder as a JSON file.
/// 
/// Priority order for loading tokens:
/// 1. Stored token from OAuth Device Flow (this app)
/// 2. GitHub CLI hosts.yml
/// 3. Environment variables (GITHUB_TOKEN, GH_TOKEN)
/// </summary>
public static class CopilotTokenStore
{
    private static readonly string TokenFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QuoteBar", "copilot_token.json");

    private static readonly string GhCliConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GitHub CLI", "hosts.yml");

    private static StoredToken? _cachedToken;

    /// <summary>
    /// Get the current access token, or null if not logged in.
    /// </summary>
    public static string? GetToken()
    {
        // 1. Try stored token first (our OAuth)
        var stored = LoadStoredToken();
        if (!string.IsNullOrEmpty(stored?.AccessToken))
        {
            Log("Using stored OAuth token");
            return stored.AccessToken;
        }

        // 2. Try GitHub CLI
        var ghToken = LoadFromGitHubCLI();
        if (!string.IsNullOrEmpty(ghToken))
        {
            Log("Using GitHub CLI token");
            return ghToken;
        }

        // 3. Try environment variables
        var envToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                    ?? Environment.GetEnvironmentVariable("GH_TOKEN")
                    ?? Environment.GetEnvironmentVariable("COPILOT_TOKEN");
        if (!string.IsNullOrEmpty(envToken))
        {
            Log("Using environment variable token");
            return envToken;
        }

        Log("No token found");
        return null;
    }

    /// <summary>
    /// Check if we have a valid token
    /// </summary>
    public static bool HasToken()
    {
        return !string.IsNullOrEmpty(GetToken());
    }

    /// <summary>
    /// Save an OAuth token from the Device Flow
    /// </summary>
    public static void SaveToken(string token)
    {
        try
        {
            var dir = Path.GetDirectoryName(TokenFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var stored = new StoredToken
            {
                AccessToken = token,
                CreatedAt = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(TokenFilePath, json);
            _cachedToken = stored;

            Log("Token saved successfully");
        }
        catch (Exception ex)
        {
            Log($"Failed to save token: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear the stored token (logout)
    /// </summary>
    public static void ClearToken()
    {
        try
        {
            if (File.Exists(TokenFilePath))
            {
                File.Delete(TokenFilePath);
            }
            _cachedToken = null;
            Log("Token cleared");
        }
        catch (Exception ex)
        {
            Log($"Failed to clear token: {ex.Message}");
        }
    }

    private static StoredToken? LoadStoredToken()
    {
        if (_cachedToken != null)
            return _cachedToken;

        try
        {
            if (!File.Exists(TokenFilePath))
                return null;

            var json = File.ReadAllText(TokenFilePath);
            _cachedToken = JsonSerializer.Deserialize<StoredToken>(json);
            return _cachedToken;
        }
        catch (Exception ex)
        {
            Log($"Failed to load stored token: {ex.Message}");
            return null;
        }
    }

    private static string? LoadFromGitHubCLI()
    {
        try
        {
            if (!File.Exists(GhCliConfigPath))
                return null;

            var yaml = File.ReadAllText(GhCliConfigPath);
            
            // Simple YAML parsing for github.com oauth_token
            // Format:
            // github.com:
            //     oauth_token: ghp_xxxxx
            //     user: username
            
            var lines = yaml.Split('\n');
            bool inGitHubSection = false;
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                if (trimmed.StartsWith("github.com:"))
                {
                    inGitHubSection = true;
                    continue;
                }
                
                if (inGitHubSection && trimmed.StartsWith("oauth_token:"))
                {
                    var token = trimmed.Substring("oauth_token:".Length).Trim();
                    if (!string.IsNullOrEmpty(token))
                    {
                        return token;
                    }
                }
                
                // Check if we've moved to a different host
                if (inGitHubSection && !line.StartsWith(" ") && !line.StartsWith("\t") && trimmed.Contains(":"))
                {
                    break;
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            Log($"Failed to load from GitHub CLI: {ex.Message}");
            return null;
        }
    }

    private static void Log(string message)
    {
        DebugLogger.Log("CopilotTokenStore", message);
    }

    private class StoredToken
    {
        public string? AccessToken { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
