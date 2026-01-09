using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuoteBar.Core.Services;

namespace QuoteBar.Core.Providers.Codex;

/// <summary>
/// Represents OAuth credentials for Codex CLI
/// </summary>
public sealed class CodexOAuthCredentials
{
    public string AccessToken { get; init; } = string.Empty;
    public string? IdToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? OrganizationId { get; init; }
    public string? UserId { get; init; }
    public string? Email { get; init; }
    public string? PlanType { get; init; }
    public string? AccountId { get; init; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value;

    public TimeSpan? ExpiresIn => ExpiresAt.HasValue
        ? ExpiresAt.Value - DateTime.UtcNow
        : null;

    public bool IsValid => !string.IsNullOrEmpty(AccessToken) && !IsExpired;
    
    /// <summary>
    /// Get display-friendly plan name
    /// </summary>
    public string DisplayPlanType => PlanType?.ToLowerInvariant() switch
    {
        "free" => "Free",
        "plus" => "Plus",
        "pro" => "Pro",
        "team" => "Team",
        "enterprise" => "Enterprise",
        _ => PlanType ?? "Unknown"
    };
}

public class CodexCredentialsException : Exception
{
    public CodexCredentialsException(string message) : base(message) { }
    public CodexCredentialsException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Manages loading Codex OAuth credentials from the Codex CLI credentials file.
/// 
/// The Codex CLI stores credentials in various formats depending on version:
/// - ~/.codex/auth.json (newer versions)
/// - ~/.codex/config.toml or config.json (older versions)
/// - Environment variable OPENAI_API_KEY
/// 
/// This class reads from these locations to detect authentication status.
/// </summary>
public static class CodexCredentialsStore
{
    private static readonly string CodexDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex");

    private static readonly string[] CredentialsPaths = new[]
    {
        Path.Combine(CodexDir, "auth.json"),
        Path.Combine(CodexDir, "credentials.json"),
        Path.Combine(CodexDir, "config.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "codex", "auth.json"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "codex", "auth.json"),
    };

    // Cache to avoid repeated file access
    private static CodexOAuthCredentials? _cachedCredentials;
    private static DateTime? _cacheTimestamp;
    private static readonly TimeSpan CacheValidityDuration = TimeSpan.FromMinutes(1);
    private static readonly object _cacheLock = new();

    /// <summary>
    /// Check if credentials exist (from file or environment)
    /// </summary>
    public static bool HasCredentials()
    {
        try
        {
            // Check environment variable first
            var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(envKey))
            {
                DebugLogger.Log("CodexCredentialsStore", "Found OPENAI_API_KEY in environment");
                return true;
            }

            // Check credential files
            foreach (var path in CredentialsPaths)
            {
                if (File.Exists(path))
                {
                    DebugLogger.Log("CodexCredentialsStore", $"Found credentials file: {path}");
                    return true;
                }
            }

            // Check if codex directory exists with any auth-related files
            if (Directory.Exists(CodexDir))
            {
                var files = Directory.GetFiles(CodexDir, "*auth*", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(CodexDir, "*cred*", SearchOption.TopDirectoryOnly))
                    .Concat(Directory.GetFiles(CodexDir, "*token*", SearchOption.TopDirectoryOnly))
                    .ToArray();

                if (files.Length > 0)
                {
                    DebugLogger.Log("CodexCredentialsStore", $"Found auth files in .codex: {string.Join(", ", files.Select(Path.GetFileName))}");
                    return true;
                }

                // Check for sessions directory (indicates CLI has been used)
                var sessionsDir = Path.Combine(CodexDir, "sessions");
                if (Directory.Exists(sessionsDir))
                {
                    DebugLogger.Log("CodexCredentialsStore", "Found sessions directory - CLI has been used");
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CodexCredentialsStore", "HasCredentials check failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Try to load credentials, returns null if not available
    /// </summary>
    public static CodexOAuthCredentials? TryLoad()
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
    /// Load credentials from Codex CLI credentials file
    /// </summary>
    public static CodexOAuthCredentials Load()
    {
        lock (_cacheLock)
        {
            // Check cache first
            if (_cachedCredentials != null &&
                _cacheTimestamp.HasValue &&
                DateTime.UtcNow - _cacheTimestamp.Value < CacheValidityDuration &&
                _cachedCredentials.IsValid)
            {
                DebugLogger.Log("CodexCredentialsStore", $"Load: Returning cached credentials (plan={_cachedCredentials.PlanType ?? "NULL"}, email={_cachedCredentials.Email ?? "NULL"})");
                return _cachedCredentials;
            }
            else
            {
                DebugLogger.Log("CodexCredentialsStore", $"Load: Cache miss or invalid. cached={_cachedCredentials != null}, timestamp={_cacheTimestamp}, valid={_cachedCredentials?.IsValid}");
            }
        }

        // Try environment variable for access token
        var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        
        // Try to get plan/identity info from auth.json even if we have OPENAI_API_KEY
        // The env var is just the access token, but auth.json has JWT with plan info
        CodexOAuthCredentials? authJsonCreds = null;
        foreach (var path in CredentialsPaths)
        {
            if (!File.Exists(path)) continue;

            try
            {
                var json = File.ReadAllText(path);
                var creds = ParseCredentials(json, path);
                if (creds != null)
                {
                    authJsonCreds = creds;
                    DebugLogger.Log("CodexCredentialsStore", $"Load: Found auth.json credentials at {path}, plan={creds.PlanType ?? "NULL"}");
                    break;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("CodexCredentialsStore", $"Failed to parse {path}: {ex.Message}");
            }
        }
        
        // If we have an environment API key, use it but merge with auth.json info
        if (!string.IsNullOrEmpty(envKey))
        {
            DebugLogger.Log("CodexCredentialsStore", $"Load: Using OPENAI_API_KEY from environment, merging with auth.json plan={authJsonCreds?.PlanType ?? "NULL"}");
            
            var envCreds = new CodexOAuthCredentials
            {
                AccessToken = envKey,
                // Use info from auth.json if available
                IdToken = authJsonCreds?.IdToken,
                RefreshToken = authJsonCreds?.RefreshToken,
                Email = authJsonCreds?.Email,
                PlanType = authJsonCreds?.PlanType,
                UserId = authJsonCreds?.UserId,
                AccountId = authJsonCreds?.AccountId,
                OrganizationId = authJsonCreds?.OrganizationId,
                // Environment tokens don't have explicit expiry
                ExpiresAt = authJsonCreds?.ExpiresAt ?? DateTime.UtcNow.AddYears(1)
            };
            UpdateCache(envCreds);
            return envCreds;
        }

        // If we found valid auth.json credentials (without env var), use them
        if (authJsonCreds != null && authJsonCreds.IsValid)
        {
            UpdateCache(authJsonCreds);
            return authJsonCreds;
        }

        // Try credential files (fallback for other locations)
        foreach (var path in CredentialsPaths)
        {
            if (!File.Exists(path)) continue;

            try
            {
                var json = File.ReadAllText(path);
                var creds = ParseCredentials(json, path);
                if (creds != null && creds.IsValid)
                {
                    UpdateCache(creds);
                    return creds;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log("CodexCredentialsStore", $"Failed to parse {path}: {ex.Message}");
            }
        }

        // Try to find any auth file in .codex directory
        if (Directory.Exists(CodexDir))
        {
            var authFiles = Directory.GetFiles(CodexDir, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var file in authFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var creds = ParseCredentials(json, file);
                    if (creds != null && creds.IsValid)
                    {
                        UpdateCache(creds);
                        return creds;
                    }
                }
                catch { }
            }
        }

        throw new CodexCredentialsException(
            "Codex credentials not found. Please run 'codex auth login' in your terminal to authenticate.");
    }

    private static CodexOAuthCredentials? ParseCredentials(string json, string sourcePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? accessToken = null;
            string? idToken = null;
            string? refreshToken = null;
            DateTime? expiresAt = null;
            string? orgId = null;
            string? userId = null;
            string? email = null;
            string? planType = null;
            string? accountId = null;

            // Format for Codex CLI auth.json: { "tokens": { "access_token": "...", "id_token": "...", "refresh_token": "..." }, "last_refresh": "..." }
            if (root.TryGetProperty("tokens", out var tokensProp))
            {
                if (tokensProp.TryGetProperty("access_token", out var at))
                    accessToken = at.GetString();
                if (tokensProp.TryGetProperty("id_token", out var it))
                    idToken = it.GetString();
                if (tokensProp.TryGetProperty("refresh_token", out var rt))
                    refreshToken = rt.GetString();
                if (tokensProp.TryGetProperty("account_id", out var aid))
                    accountId = aid.GetString();
                    
                // Extract plan and email from JWT tokens
                var jwtInfo = ExtractInfoFromJwt(idToken) ?? ExtractInfoFromJwt(accessToken);
                if (jwtInfo != null)
                {
                    planType = jwtInfo.PlanType;
                    email = jwtInfo.Email;
                    userId = jwtInfo.UserId;
                    expiresAt = jwtInfo.ExpiresAt;
                }
                
                DebugLogger.Log("CodexCredentialsStore", $"Parsed tokens structure, plan: {planType ?? "unknown"}");
            }
            else
            {
                // Fallback: Try different JSON structures for older formats
                
                // Format 1: { "access_token": "...", "expires_at": ... }
                if (root.TryGetProperty("access_token", out var atProp))
                    accessToken = atProp.GetString();
                else if (root.TryGetProperty("accessToken", out atProp))
                    accessToken = atProp.GetString();
                else if (root.TryGetProperty("token", out atProp))
                    accessToken = atProp.GetString();
                else if (root.TryGetProperty("api_key", out atProp))
                    accessToken = atProp.GetString();
                else if (root.TryGetProperty("apiKey", out atProp))
                    accessToken = atProp.GetString();

                // Try nested structure: { "auth": { "access_token": "..." } }
                if (string.IsNullOrEmpty(accessToken) && root.TryGetProperty("auth", out var authProp))
                {
                    if (authProp.TryGetProperty("access_token", out atProp))
                        accessToken = atProp.GetString();
                    else if (authProp.TryGetProperty("accessToken", out atProp))
                        accessToken = atProp.GetString();
                    else if (authProp.TryGetProperty("token", out atProp))
                        accessToken = atProp.GetString();
                }

                // Try openai nested: { "openai": { "api_key": "..." } }
                if (string.IsNullOrEmpty(accessToken) && root.TryGetProperty("openai", out var openaiProp))
                {
                    if (openaiProp.TryGetProperty("api_key", out atProp))
                        accessToken = atProp.GetString();
                    else if (openaiProp.TryGetProperty("apiKey", out atProp))
                        accessToken = atProp.GetString();
                }

                // Refresh token
                if (root.TryGetProperty("refresh_token", out var rtProp))
                    refreshToken = rtProp.GetString();
                else if (root.TryGetProperty("refreshToken", out rtProp))
                    refreshToken = rtProp.GetString();

                // Expiry
                if (root.TryGetProperty("expires_at", out var expProp) || root.TryGetProperty("expiresAt", out expProp))
                {
                    if (expProp.ValueKind == JsonValueKind.Number)
                    {
                        var expVal = expProp.GetInt64();
                        // Check if milliseconds or seconds
                        if (expVal > 1000000000000) // milliseconds
                            expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(expVal).UtcDateTime;
                        else // seconds
                            expiresAt = DateTimeOffset.FromUnixTimeSeconds(expVal).UtcDateTime;
                    }
                    else if (expProp.ValueKind == JsonValueKind.String)
                    {
                        if (DateTime.TryParse(expProp.GetString(), out var dt))
                            expiresAt = dt.ToUniversalTime();
                    }
                }

                // Organization/User info
                if (root.TryGetProperty("organization_id", out var orgProp))
                    orgId = orgProp.GetString();
                else if (root.TryGetProperty("org_id", out orgProp))
                    orgId = orgProp.GetString();

                if (root.TryGetProperty("user_id", out var userProp))
                    userId = userProp.GetString();
                
                if (root.TryGetProperty("email", out var emailProp))
                    email = emailProp.GetString();
                    
                // Try to extract plan from JWT if we have an access token
                if (!string.IsNullOrEmpty(accessToken))
                {
                    var jwtInfo = ExtractInfoFromJwt(accessToken);
                    if (jwtInfo != null)
                    {
                        planType ??= jwtInfo.PlanType;
                        email ??= jwtInfo.Email;
                        userId ??= jwtInfo.UserId;
                        expiresAt ??= jwtInfo.ExpiresAt;
                    }
                }
            }

            if (string.IsNullOrEmpty(accessToken))
            {
                DebugLogger.Log("CodexCredentialsStore", $"No access token found in {Path.GetFileName(sourcePath)}");
                return null;
            }

            DebugLogger.Log("CodexCredentialsStore", $"ParseCredentials: Creating credentials object - accessToken={!string.IsNullOrEmpty(accessToken)}, planType='{planType ?? "NULL"}', email='{email ?? "NULL"}'");

            return new CodexOAuthCredentials
            {
                AccessToken = accessToken,
                IdToken = idToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddYears(1), // Default to long expiry if not specified
                OrganizationId = orgId,
                UserId = userId,
                Email = email,
                PlanType = planType,
                AccountId = accountId
            };
        }
        catch (JsonException ex)
        {
            DebugLogger.Log("CodexCredentialsStore", $"JSON parse error for {Path.GetFileName(sourcePath)}: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Information extracted from JWT token
    /// </summary>
    private sealed class JwtInfo
    {
        public string? PlanType { get; init; }
        public string? Email { get; init; }
        public string? UserId { get; init; }
        public DateTime? ExpiresAt { get; init; }
    }
    
    /// <summary>
    /// Extract plan type and other info from JWT token without external dependencies.
    /// JWT format: header.payload.signature (base64url encoded)
    /// </summary>
    private static JwtInfo? ExtractInfoFromJwt(string? jwt)
    {
        if (string.IsNullOrEmpty(jwt))
        {
            DebugLogger.Log("CodexCredentialsStore", "ExtractInfoFromJwt: JWT is null or empty");
            return null;
        }
            
        try
        {
            DebugLogger.Log("CodexCredentialsStore", $"ExtractInfoFromJwt: JWT length={jwt.Length}, starts with: {jwt.Substring(0, Math.Min(50, jwt.Length))}...");
            
            var parts = jwt.Split('.');
            if (parts.Length != 3)
            {
                DebugLogger.Log("CodexCredentialsStore", $"ExtractInfoFromJwt: Invalid JWT format, parts count={parts.Length}");
                return null;
            }
            
            DebugLogger.Log("CodexCredentialsStore", $"ExtractInfoFromJwt: JWT payload part length={parts[1].Length}");
                
            // Decode the payload (middle part)
            var payload = DecodeBase64Url(parts[1]);
            if (string.IsNullOrEmpty(payload))
            {
                DebugLogger.Log("CodexCredentialsStore", "ExtractInfoFromJwt: Failed to decode base64url payload");
                return null;
            }
            
            DebugLogger.Log("CodexCredentialsStore", $"ExtractInfoFromJwt: Decoded payload length={payload.Length}");
            
            // Log first 200 chars of payload for debugging (careful with sensitive data in prod)
            var payloadPreview = payload.Length > 200 ? payload.Substring(0, 200) + "..." : payload;
            DebugLogger.Log("CodexCredentialsStore", $"ExtractInfoFromJwt: Payload preview: {payloadPreview}");
                
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            
            string? planType = null;
            string? email = null;
            string? userId = null;
            DateTime? expiresAt = null;
            
            // Extract expiry
            if (root.TryGetProperty("exp", out var expProp) && expProp.ValueKind == JsonValueKind.Number)
            {
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(expProp.GetInt64()).UtcDateTime;
                DebugLogger.Log("CodexCredentialsStore", $"ExtractInfoFromJwt: Found exp={expiresAt}");
            }
            
            // OpenAI JWT structure: { "https://api.openai.com/auth": { "chatgpt_plan_type": "free", ... } }
            if (root.TryGetProperty("https://api.openai.com/auth", out var authClaim))
            {
                DebugLogger.Log("CodexCredentialsStore", "ExtractInfoFromJwt: Found 'https://api.openai.com/auth' claim");
                
                if (authClaim.TryGetProperty("chatgpt_plan_type", out var pt))
                {
                    planType = pt.GetString();
                    DebugLogger.Log("CodexCredentialsStore", $"ExtractInfoFromJwt: Found chatgpt_plan_type='{planType}'");
                }
                else
                {
                    DebugLogger.Log("CodexCredentialsStore", "ExtractInfoFromJwt: 'chatgpt_plan_type' NOT found in auth claim");
                }
                
                if (authClaim.TryGetProperty("chatgpt_user_id", out var uid))
                    userId = uid.GetString();
            }
            else
            {
                DebugLogger.Log("CodexCredentialsStore", "ExtractInfoFromJwt: 'https://api.openai.com/auth' claim NOT found in JWT");
                
                // Log all top-level properties for debugging
                var props = new List<string>();
                foreach (var prop in root.EnumerateObject())
                {
                    props.Add(prop.Name);
                }
                DebugLogger.Log("CodexCredentialsStore", $"ExtractInfoFromJwt: Available claims: {string.Join(", ", props)}");
            }
            
            // Try profile claim for email
            if (root.TryGetProperty("https://api.openai.com/profile", out var profileClaim))
            {
                if (profileClaim.TryGetProperty("email", out var em))
                    email = em.GetString();
            }
            
            // Fallback: direct email claim
            if (string.IsNullOrEmpty(email) && root.TryGetProperty("email", out var emailProp))
                email = emailProp.GetString();
                
            // Fallback: sub claim for user id
            if (string.IsNullOrEmpty(userId) && root.TryGetProperty("sub", out var subProp))
                userId = subProp.GetString();
            
            DebugLogger.Log("CodexCredentialsStore", $"ExtractInfoFromJwt: FINAL RESULT - plan='{planType ?? "NULL"}', email='{email ?? "NULL"}'");
            
            return new JwtInfo
            {
                PlanType = planType,
                Email = email,
                UserId = userId,
                ExpiresAt = expiresAt
            };
        }
        catch (Exception ex)
        {
            DebugLogger.Log("CodexCredentialsStore", $"ExtractInfoFromJwt: EXCEPTION - {ex.GetType().Name}: {ex.Message}");
            DebugLogger.Log("CodexCredentialsStore", $"ExtractInfoFromJwt: Stack trace: {ex.StackTrace}");
            return null;
        }
    }
    
    /// <summary>
    /// Decode base64url string (JWT uses base64url, not standard base64)
    /// </summary>
    private static string? DecodeBase64Url(string base64Url)
    {
        try
        {
            // Convert base64url to standard base64
            var base64 = base64Url
                .Replace('-', '+')
                .Replace('_', '/');
                
            // Add padding if needed
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
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

    private static void UpdateCache(CodexOAuthCredentials creds)
    {
        lock (_cacheLock)
        {
            _cachedCredentials = creds;
            _cacheTimestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Get the path to the .codex directory
    /// </summary>
    public static string GetCodexDirectory() => CodexDir;

    /// <summary>
    /// Check if the .codex directory exists
    /// </summary>
    public static bool CodexDirectoryExists() => Directory.Exists(CodexDir);
}
