using QuoteBar.Core.Models;
using QuoteBar.Core.Services;
using System.Diagnostics;
using System.IO;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuoteBar.Core.Providers.Droid;

public class DroidProviderDescriptor : ProviderDescriptor
{
    public override string Id => "droid";
    public override string DisplayName => "Droid";
    public override string IconGlyph => "\uE99A"; // Robot icon
    public override string PrimaryColor => "#10B981";
    public override string SecondaryColor => "#34D399";
    public override string PrimaryLabel => "Standard";
    public override string SecondaryLabel => "Premium";
    public override string? DashboardUrl => "https://app.factory.ai";

    public override bool SupportsOAuth => true;
    public override bool SupportsCLI => true;

    protected override void InitializeStrategies()
    {
        // Priority order:
        // 1. Cached data (if valid) - fastest, no network
        // 2. CLI auth file (~/.factory/auth.json) - best method if Droid CLI is installed
        // 3. Stored session (from previous WebView OAuth login)
        AddStrategy(new DroidCachedStrategy());
        AddStrategy(new DroidCLIAuthStrategy());
        AddStrategy(new DroidStoredSessionStrategy());
    }
}

#region Strategy 0: Cached Data (fastest)

/// <summary>
/// Strategy that returns cached data if still valid.
/// </summary>
public class DroidCachedStrategy : IProviderFetchStrategy
{
    public string StrategyName => "Cached";
    public int Priority => 0;
    public StrategyType Type => StrategyType.Cached;

    public Task<bool> CanExecuteAsync()
    {
        var isValid = DroidUsageCache.IsCacheValid();
        Log($"CanExecute: isValid={isValid}");
        return Task.FromResult(isValid);
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        Log("Returning cached data");
        return await DroidUsageCache.GetCachedAsync(cancellationToken);
    }

    private static void Log(string message) => DebugLogger.Log("DroidCachedStrategy", message);
}

#endregion

#region Strategy 1: CLI Auth File (recommended)

/// <summary>
/// Strategy that reads authentication from Droid CLI's auth.json file.
/// This is the RECOMMENDED method as it:
/// 1. Requires no additional login - reuses existing Droid CLI auth
/// 2. Is more secure than browser cookie extraction
/// 3. Automatically refreshes tokens via WorkOS
/// 
/// Location: ~/.factory/auth.json (same as Droid CLI)
/// </summary>
public class DroidCLIAuthStrategy : IProviderFetchStrategy
{
    public string StrategyName => "CLI Auth";
    public int Priority => 1;
    public StrategyType Type => StrategyType.CLI;

    public Task<bool> CanExecuteAsync()
    {
        var hasAuthFile = DroidCLIAuth.HasAuthFile();
        Log($"CanExecute: hasAuthFile={hasAuthFile}");
        return Task.FromResult(hasAuthFile);
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var auth = DroidCLIAuth.LoadAuth();
            if (auth == null)
            {
                return CreateError(DroidErrorMessages.NoCLIAuth);
            }

            Log("Using CLI auth file");

            // Check if token is expired
            if (auth.IsExpired)
            {
                Log("Token expired, attempting refresh");
                var refreshed = await DroidCLIAuth.RefreshTokenAsync(auth.RefreshToken, cancellationToken);
                if (refreshed == null)
                {
                    return CreateError(DroidErrorMessages.TokenExpired);
                }
                auth = refreshed;
            }

            var snapshot = await DroidUsageFetcher.FetchAsync(auth.AccessToken, Log, cancellationToken);
            DroidUsageCache.UpdateCache(snapshot);
            return snapshot;
        }
        catch (DroidFetchException ex) when (ex.ErrorType == DroidFetchError.NotLoggedIn)
        {
            Log("Token invalid, attempting refresh");
            try
            {
                var auth = DroidCLIAuth.LoadAuth();
                if (auth?.RefreshToken != null)
                {
                    var refreshed = await DroidCLIAuth.RefreshTokenAsync(auth.RefreshToken, cancellationToken);
                    if (refreshed != null)
                    {
                        var snapshot = await DroidUsageFetcher.FetchAsync(refreshed.AccessToken, Log, cancellationToken);
                        DroidUsageCache.UpdateCache(snapshot);
                        return snapshot;
                    }
                }
            }
            catch (Exception refreshEx)
            {
                Log($"Refresh failed: {refreshEx.Message}");
            }
            return CreateError(DroidErrorMessages.TokenExpired);
        }
        catch (DroidFetchException ex) when (ex.ErrorType == DroidFetchError.NetworkError)
        {
            return CreateError(DroidErrorMessages.NetworkError(ex.Message));
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            return CreateError(DroidErrorMessages.UnexpectedError(ex.Message));
        }
    }

    private static UsageSnapshot CreateError(string message) => new()
    {
        ProviderId = "droid",
        ErrorMessage = message,
        FetchedAt = DateTime.UtcNow,
        RequiresReauth = message.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                         message.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
                         message.Contains("re-authenticate", StringComparison.OrdinalIgnoreCase)
    };

    private static void Log(string message) => DebugLogger.Log("DroidCLIAuthStrategy", message);
}

#endregion

#region Strategy 2: Stored Session (OAuth fallback)

/// <summary>
/// Strategy that uses a previously stored session from OAuth login.
/// This is the fallback when Droid CLI is not installed.
/// </summary>
public class DroidStoredSessionStrategy : IProviderFetchStrategy
{
    public string StrategyName => "Stored Session";
    public int Priority => 2;
    public StrategyType Type => StrategyType.OAuth;

    public Task<bool> CanExecuteAsync()
    {
        var hasSession = DroidSessionStore.HasSession();
        Log($"CanExecute: hasSession={hasSession}");
        return Task.FromResult(hasSession);
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var session = DroidSessionStore.GetSession();
        if (session == null)
        {
            return CreateError(DroidErrorMessages.NoStoredSession);
        }

        try
        {
            Log("Using stored OAuth session");

            // Check if token is expired
            if (session.IsExpired)
            {
                Log("Token expired, attempting refresh");
                if (!string.IsNullOrEmpty(session.RefreshToken))
                {
                    var refreshed = await DroidCLIAuth.RefreshTokenAsync(session.RefreshToken, cancellationToken);
                    if (refreshed != null)
                    {
                        DroidSessionStore.SetSession(new DroidStoredSession
                        {
                            AccessToken = refreshed.AccessToken,
                            RefreshToken = refreshed.RefreshToken,
                            ExpiresAt = refreshed.ExpiresAt,
                            SourceLabel = "OAuth (refreshed)",
                            AccountEmail = session.AccountEmail
                        });
                        session = DroidSessionStore.GetSession()!;
                    }
                    else
                    {
                        DroidSessionStore.ClearSession();
                        return CreateError(DroidErrorMessages.SessionExpired);
                    }
                }
                else
                {
                    DroidSessionStore.ClearSession();
                    return CreateError(DroidErrorMessages.SessionExpired);
                }
            }

            var snapshot = await DroidUsageFetcher.FetchAsync(session.AccessToken, Log, cancellationToken);
            DroidUsageCache.UpdateCache(snapshot);
            return snapshot;
        }
        catch (DroidFetchException ex) when (ex.ErrorType == DroidFetchError.NotLoggedIn)
        {
            DroidSessionStore.ClearSession();
            DroidUsageCache.Invalidate();
            Log("Stored session invalid, cleared");
            return CreateError(DroidErrorMessages.SessionExpired);
        }
        catch (DroidFetchException ex) when (ex.ErrorType == DroidFetchError.NetworkError)
        {
            return CreateError(DroidErrorMessages.NetworkError(ex.Message));
        }
        catch (Exception ex)
        {
            return CreateError(DroidErrorMessages.UnexpectedError(ex.Message));
        }
    }

    private static UsageSnapshot CreateError(string message) => new()
    {
        ProviderId = "droid",
        ErrorMessage = message,
        FetchedAt = DateTime.UtcNow,
        RequiresReauth = message.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                         message.Contains("sign in", StringComparison.OrdinalIgnoreCase) ||
                         message.Contains("re-authenticate", StringComparison.OrdinalIgnoreCase)
    };

    private static void Log(string message) => DebugLogger.Log("DroidStoredStrategy", message);
}

#endregion

#region CLI Auth File Reader

/// <summary>
/// Reads authentication from Droid CLI's auth.json file.
/// Location: ~/.factory/auth.json
/// </summary>
public static class DroidCLIAuth
{
    private static readonly string AuthFilePath;
    private static readonly HttpClient _httpClient = new();

    // WorkOS client IDs used by Factory/Droid
    private static readonly string[] WorkOSClientIDs = [
        "client_01HXRMBQ9BJ3E7QSTQ9X2PHVB7",
        "client_01HNM792M5G5G1A2THWPXKFMXB"
    ];

    static DroidCLIAuth()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AuthFilePath = Path.Combine(home, ".factory", "auth.json");
    }

    public static bool HasAuthFile()
    {
        return File.Exists(AuthFilePath);
    }

    public static DroidAuthInfo? LoadAuth()
    {
        try
        {
            if (!File.Exists(AuthFilePath))
            {
                Log("Auth file not found");
                return null;
            }

            var json = File.ReadAllText(AuthFilePath);
            var auth = JsonSerializer.Deserialize<DroidAuthFileContent>(json);

            if (auth == null || string.IsNullOrEmpty(auth.AccessToken))
            {
                Log("Auth file is empty or invalid");
                return null;
            }

            // Parse JWT to extract expiration and user info
            var claims = ParseJwtClaims(auth.AccessToken);

            return new DroidAuthInfo
            {
                AccessToken = auth.AccessToken,
                RefreshToken = auth.RefreshToken ?? string.Empty,
                ExpiresAt = claims.ExpiresAt,
                Email = claims.Email,
                OrgId = claims.OrgId,
                UserId = claims.UserId
            };
        }
        catch (Exception ex)
        {
            Log($"Failed to load auth: {ex.Message}");
            return null;
        }
    }

    public static async Task<DroidAuthInfo?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            Log("No refresh token available");
            return null;
        }

        foreach (var clientId in WorkOSClientIDs)
        {
            try
            {
                var result = await RefreshWithClientIdAsync(refreshToken, clientId, cancellationToken);
                if (result != null)
                {
                    // Save refreshed tokens back to auth file
                    SaveAuth(result.AccessToken, result.RefreshToken);
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log($"Refresh failed with client {clientId}: {ex.Message}");
            }
        }

        Log("All refresh attempts failed");
        return null;
    }

    private static async Task<DroidAuthInfo?> RefreshWithClientIdAsync(
        string refreshToken,
        string clientId,
        CancellationToken cancellationToken)
    {
        var url = "https://api.workos.com/user_management/authenticate";

        var body = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            Log($"WorkOS refresh failed: {response.StatusCode} - {error}");
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<WorkOSAuthResponse>(json);

        if (result == null || string.IsNullOrEmpty(result.AccessToken))
        {
            Log("WorkOS returned empty token");
            return null;
        }

        var claims = ParseJwtClaims(result.AccessToken);

        Log("Token refreshed successfully");
        return new DroidAuthInfo
        {
            AccessToken = result.AccessToken,
            RefreshToken = result.RefreshToken ?? refreshToken,
            ExpiresAt = claims.ExpiresAt,
            Email = claims.Email,
            OrgId = claims.OrgId,
            UserId = claims.UserId
        };
    }

    private static void SaveAuth(string accessToken, string refreshToken)
    {
        try
        {
            var dir = Path.GetDirectoryName(AuthFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var auth = new DroidAuthFileContent
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken
            };

            var json = JsonSerializer.Serialize(auth, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AuthFilePath, json);
            Log("Auth file updated");
        }
        catch (Exception ex)
        {
            Log($"Failed to save auth: {ex.Message}");
        }
    }

    private static JwtClaims ParseJwtClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return new JwtClaims();

            var payload = parts[1];
            // Add padding if needed
            payload += new string('=', (4 - payload.Length % 4) % 4);
            // Replace URL-safe characters
            payload = payload.Replace('-', '+').Replace('_', '/');

            var bytes = Convert.FromBase64String(payload);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            if (claims == null)
                return new JwtClaims();

            var result = new JwtClaims();

            if (claims.TryGetValue("exp", out var exp) && exp.ValueKind == JsonValueKind.Number)
            {
                result.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()).UtcDateTime;
            }

            if (claims.TryGetValue("email", out var email) && email.ValueKind == JsonValueKind.String)
            {
                result.Email = email.GetString();
            }

            if (claims.TryGetValue("org_id", out var orgId) && orgId.ValueKind == JsonValueKind.String)
            {
                result.OrgId = orgId.GetString();
            }

            if (claims.TryGetValue("sub", out var sub) && sub.ValueKind == JsonValueKind.String)
            {
                result.UserId = sub.GetString();
            }

            return result;
        }
        catch
        {
            return new JwtClaims();
        }
    }

    private static void Log(string message) => DebugLogger.Log("DroidCLIAuth", message);

    private class JwtClaims
    {
        public DateTime? ExpiresAt { get; set; }
        public string? Email { get; set; }
        public string? OrgId { get; set; }
        public string? UserId { get; set; }
    }
}

public class DroidAuthInfo
{
    public required string AccessToken { get; init; }
    public required string RefreshToken { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? Email { get; init; }
    public string? OrgId { get; init; }
    public string? UserId { get; init; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value.AddMinutes(-5);
}

public class DroidAuthFileContent
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
}

public class WorkOSAuthResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("organization_id")]
    public string? OrganizationId { get; set; }
}

#endregion

#region Usage Fetcher

public enum DroidFetchError
{
    None,
    NotLoggedIn,
    NetworkError,
    ParseError,
    Unknown
}

public class DroidFetchException : Exception
{
    public DroidFetchError ErrorType { get; }

    public DroidFetchException(DroidFetchError errorType, string message) : base(message)
    {
        ErrorType = errorType;
    }
}

/// <summary>
/// Fetches usage data from Factory API.
/// </summary>
public static class DroidUsageFetcher
{
    private const string ApiBaseUrl = "https://api.factory.ai";

    public static async Task<UsageSnapshot> FetchAsync(
        string accessToken,
        Action<string>? logger = null,
        CancellationToken cancellationToken = default)
    {
        var log = logger ?? (_ => { });

        try
        {
            log("Fetching usage from Factory API");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/api/organization/subscription/usage")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { useCache = true }),
                    System.Text.Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("x-factory-client", "web-app");

            // Use SharedHttpClient to avoid socket exhaustion
            var response = await Core.Services.SharedHttpClient.Default.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                log($"Not logged in: {response.StatusCode}");
                throw new DroidFetchException(DroidFetchError.NotLoggedIn, "Not logged in to Factory");
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                log($"API error: {response.StatusCode} - {error}");
                throw new DroidFetchException(DroidFetchError.NetworkError, $"HTTP {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            log($"Response: {json[..Math.Min(200, json.Length)]}...");

            var usageResponse = JsonSerializer.Deserialize<FactoryUsageResponse>(json);
            if (usageResponse?.Usage == null)
            {
                throw new DroidFetchException(DroidFetchError.ParseError, "Could not parse usage data");
            }

            return BuildSnapshot(usageResponse);
        }
        catch (DroidFetchException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            log($"Network error: {ex.Message}");
            throw new DroidFetchException(DroidFetchError.NetworkError, ex.Message);
        }
        catch (Exception ex)
        {
            log($"Unexpected error: {ex.Message}");
            throw new DroidFetchException(DroidFetchError.Unknown, ex.Message);
        }
    }

    private static UsageSnapshot BuildSnapshot(FactoryUsageResponse response)
    {
        var usage = response.Usage!;
        var standard = usage.Standard;
        var premium = usage.Premium;

        // Calculate percentages
        double standardPercent = 0;
        if (standard != null && standard.TotalAllowance > 0)
        {
            // Use org total tokens or user tokens
            var used = standard.OrgTotalTokensUsed ?? standard.UserTokens ?? 0;
            standardPercent = (double)used / standard.TotalAllowance.Value * 100;
        }
        else if (standard?.UsedRatio != null)
        {
            standardPercent = standard.UsedRatio.Value * 100;
        }

        double premiumPercent = 0;
        if (premium != null && premium.TotalAllowance > 0)
        {
            var used = premium.OrgTotalTokensUsed ?? premium.UserTokens ?? 0;
            premiumPercent = (double)used / premium.TotalAllowance.Value * 100;
        }
        else if (premium?.UsedRatio != null)
        {
            premiumPercent = premium.UsedRatio.Value * 100;
        }

        // Parse reset date from endDate (milliseconds since epoch)
        DateTime? resetsAt = null;
        string? resetDescription = null;
        if (usage.EndDate.HasValue)
        {
            resetsAt = DateTimeOffset.FromUnixTimeMilliseconds(usage.EndDate.Value).UtcDateTime;
            resetDescription = FormatResetDate(resetsAt.Value);
        }

        return new UsageSnapshot
        {
            ProviderId = "droid",
            Primary = new RateWindow
            {
                UsedPercent = standardPercent,
                WindowMinutes = null,
                ResetsAt = resetsAt,
                ResetDescription = resetDescription
            },
            Secondary = premiumPercent > 0 || (premium?.TotalAllowance ?? 0) > 0 ? new RateWindow
            {
                UsedPercent = premiumPercent,
                WindowMinutes = null,
                ResetsAt = resetsAt,
                ResetDescription = resetDescription
            } : null,
            Identity = new ProviderIdentity
            {
                PlanType = "Factory"
            },
            FetchedAt = DateTime.UtcNow
        };
    }

    private static string FormatResetDate(DateTime date)
    {
        var local = date.ToLocalTime();
        return $"Resets {local:MMM d} at {local:h:mmtt}".ToLower().Replace("am", "AM").Replace("pm", "PM");
    }
}

#region Factory API Models

public class FactoryUsageResponse
{
    [JsonPropertyName("usage")]
    public FactoryUsageData? Usage { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }
}

public class FactoryUsageData
{
    [JsonPropertyName("startDate")]
    public long? StartDate { get; set; }

    [JsonPropertyName("endDate")]
    public long? EndDate { get; set; }

    [JsonPropertyName("standard")]
    public FactoryTokenUsage? Standard { get; set; }

    [JsonPropertyName("premium")]
    public FactoryTokenUsage? Premium { get; set; }
}

public class FactoryTokenUsage
{
    [JsonPropertyName("userTokens")]
    public long? UserTokens { get; set; }

    [JsonPropertyName("orgTotalTokensUsed")]
    public long? OrgTotalTokensUsed { get; set; }

    [JsonPropertyName("totalAllowance")]
    public long? TotalAllowance { get; set; }

    [JsonPropertyName("usedRatio")]
    public double? UsedRatio { get; set; }

    [JsonPropertyName("orgOverageUsed")]
    public long? OrgOverageUsed { get; set; }

    [JsonPropertyName("basicAllowance")]
    public long? BasicAllowance { get; set; }

    [JsonPropertyName("orgOverageLimit")]
    public long? OrgOverageLimit { get; set; }
}

#endregion

#endregion

#region Session Store (OAuth fallback)

/// <summary>
/// Stored session from OAuth login (fallback when CLI not installed)
/// </summary>
public class DroidStoredSession
{
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public required string SourceLabel { get; init; }
    public DateTime StoredAt { get; init; } = DateTime.UtcNow;
    public string? AccountEmail { get; init; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow >= ExpiresAt.Value.AddMinutes(-5);
}

/// <summary>
/// Manages stored OAuth sessions for Droid (when CLI is not available).
/// Uses Windows Credential Manager for secure storage.
/// </summary>
public static class DroidSessionStore
{
    private const string CredentialKey = "droid-session";
    private static readonly string MetadataFilePath;
    private static readonly object _lock = new();

    private static DroidStoredSession? _cachedSession;
    private static bool _hasLoaded;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static DateTime _lastLoadTime = DateTime.MinValue;

    static DroidSessionStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appDir = Path.Combine(appData, "NativeBar");
        Directory.CreateDirectory(appDir);
        MetadataFilePath = Path.Combine(appDir, "droid-session-meta.json");
    }

    public static void SetSession(DroidStoredSession session)
    {
        lock (_lock)
        {
            _cachedSession = session;
            _hasLoaded = true;
            _lastLoadTime = DateTime.UtcNow;

            // Store tokens securely
            var tokenData = JsonSerializer.Serialize(new
            {
                session.AccessToken,
                session.RefreshToken,
                ExpiresAt = session.ExpiresAt?.ToString("O")
            });

            SecureCredentialStore.StoreCredential(CredentialKey, tokenData);

            // Save metadata (no tokens)
            SaveMetadata(new DroidSessionMetadata
            {
                SourceLabel = session.SourceLabel,
                StoredAt = session.StoredAt,
                AccountEmail = session.AccountEmail
            });

            Log("Session stored securely");
        }
    }

    public static DroidStoredSession? GetSession()
    {
        lock (_lock)
        {
            if (_cachedSession != null && DateTime.UtcNow - _lastLoadTime < CacheDuration)
            {
                return _cachedSession;
            }

            LoadFromCredentialManager();
            return _cachedSession;
        }
    }

    public static bool HasSession()
    {
        return GetSession() != null;
    }

    public static void ClearSession()
    {
        lock (_lock)
        {
            _cachedSession = null;
            _hasLoaded = true;
            _lastLoadTime = DateTime.MinValue;

            SecureCredentialStore.DeleteCredential(CredentialKey);

            try
            {
                if (File.Exists(MetadataFilePath))
                    File.Delete(MetadataFilePath);
            }
            catch { }

            Log("Session cleared");
        }
    }

    private static void LoadFromCredentialManager()
    {
        if (_hasLoaded && DateTime.UtcNow - _lastLoadTime < CacheDuration)
            return;

        _hasLoaded = true;
        _lastLoadTime = DateTime.UtcNow;

        var tokenData = SecureCredentialStore.GetCredential(CredentialKey);
        if (string.IsNullOrEmpty(tokenData))
        {
            _cachedSession = null;
            return;
        }

        try
        {
            var tokens = JsonSerializer.Deserialize<JsonElement>(tokenData);
            var metadata = LoadMetadata();

            DateTime? expiresAt = null;
            if (tokens.TryGetProperty("ExpiresAt", out var exp) && exp.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(exp.GetString(), out var dt))
                    expiresAt = dt;
            }

            _cachedSession = new DroidStoredSession
            {
                AccessToken = tokens.GetProperty("AccessToken").GetString() ?? "",
                RefreshToken = tokens.TryGetProperty("RefreshToken", out var rt) ? rt.GetString() : null,
                ExpiresAt = expiresAt,
                SourceLabel = metadata?.SourceLabel ?? "OAuth",
                StoredAt = metadata?.StoredAt ?? DateTime.UtcNow,
                AccountEmail = metadata?.AccountEmail
            };

            Log("Session loaded from Credential Manager");
        }
        catch (Exception ex)
        {
            Log($"Failed to load session: {ex.Message}");
            _cachedSession = null;
        }
    }

    private static void SaveMetadata(DroidSessionMetadata metadata)
    {
        try
        {
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(MetadataFilePath, json);
        }
        catch { }
    }

    private static DroidSessionMetadata? LoadMetadata()
    {
        try
        {
            if (!File.Exists(MetadataFilePath))
                return null;

            var json = File.ReadAllText(MetadataFilePath);
            return JsonSerializer.Deserialize<DroidSessionMetadata>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void Log(string message) => DebugLogger.Log("DroidSessionStore", message);
}

public class DroidSessionMetadata
{
    public required string SourceLabel { get; init; }
    public DateTime StoredAt { get; init; }
    public string? AccountEmail { get; init; }
}

#endregion

#region Usage Cache

/// <summary>
/// Cache for Droid usage data to minimize API calls.
/// </summary>
public static class DroidUsageCache
{
    private static UsageSnapshot? _cachedSnapshot;
    private static DateTime _lastFetch = DateTime.MinValue;
    private static readonly object _lock = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public static bool IsCacheValid()
    {
        lock (_lock)
        {
            return _cachedSnapshot != null &&
                   string.IsNullOrEmpty(_cachedSnapshot.ErrorMessage) &&
                   DateTime.UtcNow - _lastFetch < CacheDuration;
        }
    }

    public static Task<UsageSnapshot> GetCachedAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_cachedSnapshot != null)
            {
                return Task.FromResult(_cachedSnapshot);
            }

            return Task.FromResult(new UsageSnapshot
            {
                ProviderId = "droid",
                ErrorMessage = "No cached data",
                FetchedAt = DateTime.UtcNow
            });
        }
    }

    public static void UpdateCache(UsageSnapshot snapshot)
    {
        lock (_lock)
        {
            _cachedSnapshot = snapshot;
            _lastFetch = DateTime.UtcNow;
        }
    }

    public static void Invalidate()
    {
        lock (_lock)
        {
            _cachedSnapshot = null;
            _lastFetch = DateTime.MinValue;
        }
    }
}

#endregion

#region Error Messages

/// <summary>
/// User-friendly error messages for Droid provider.
/// </summary>
public static class DroidErrorMessages
{
    public const string NoCLIAuth =
        "Droid CLI not authenticated. Run 'droid' in terminal to login, or use OAuth login in Settings.";

    public const string NoStoredSession =
        "Not signed in to Droid. Go to Settings > Droid to sign in.";

    public const string TokenExpired =
        "Your Droid session has expired. Run 'droid' in terminal to re-authenticate, or sign in via Settings.";

    public const string SessionExpired =
        "Your Droid session has expired. Please sign in again via Settings > Droid.";

    public static string NetworkError(string details) =>
        $"Network error connecting to Factory. Check your internet connection. ({details})";

    public static string UnexpectedError(string details) =>
        $"An unexpected error occurred. ({details})";

    /// <summary>
    /// Get installation instructions for Droid CLI
    /// </summary>
    public static string GetCLIInstallHint() =>
        "Tip: Install Droid CLI for secure, automatic authentication. Visit https://docs.factory.ai/factory-cli/getting-started/overview";

    /// <summary>
    /// Get a hint message for how to fix the error
    /// </summary>
    public static string GetHint(string errorMessage)
    {
        if (errorMessage.Contains("CLI", StringComparison.OrdinalIgnoreCase))
        {
            return GetCLIInstallHint();
        }
        if (errorMessage.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            return "Tip: Run 'droid' in terminal to refresh your session, or sign in via Settings > Droid.";
        }
        if (errorMessage.Contains("network", StringComparison.OrdinalIgnoreCase))
        {
            return "Tip: Check your internet connection and firewall settings.";
        }
        return "Tip: Go to Settings > Droid to configure authentication.";
    }
}

#endregion

#region Login Helper

/// <summary>
/// Helper class to launch Droid OAuth login.
/// Similar to CursorLoginHelper but for Factory/Droid.
/// 
/// AUTHENTICATION PRIORITY:
/// 1. Droid CLI (~/.factory/auth.json) - RECOMMENDED, most secure
/// 2. OAuth via WebView (fallback if CLI not installed)
/// </summary>
public static class DroidLoginHelper
{
    private static QuoteBar.Views.DroidLoginWindow? _currentWindow;
    private static readonly object _lock = new();

    /// <summary>
    /// Check if user is signed in (via CLI or OAuth)
    /// </summary>
    public static bool IsSignedIn => DroidCLIAuth.HasAuthFile() || DroidSessionStore.HasSession();

    /// <summary>
    /// Check if Droid CLI is installed and authenticated
    /// </summary>
    public static bool HasCLIAuth => DroidCLIAuth.HasAuthFile();

    /// <summary>
    /// Check if the login window is currently open
    /// </summary>
    public static bool IsLoginWindowOpen
    {
        get
        {
            lock (_lock)
            {
                return _currentWindow != null;
            }
        }
    }

    /// <summary>
    /// Get the current auth source
    /// </summary>
    public static string GetAuthSource()
    {
        if (DroidCLIAuth.HasAuthFile())
            return "Droid CLI";

        var session = DroidSessionStore.GetSession();
        return session?.SourceLabel ?? "Not signed in";
    }

    /// <summary>
    /// Get the current account email
    /// </summary>
    public static string? GetAccountEmail()
    {
        var auth = DroidCLIAuth.LoadAuth();
        if (auth?.Email != null)
            return auth.Email;

        return DroidSessionStore.GetSession()?.AccountEmail;
    }

    /// <summary>
    /// Get the current session info
    /// </summary>
    public static DroidStoredSession? GetCurrentSession() => DroidSessionStore.GetSession();

    /// <summary>
    /// Launch the OAuth login window.
    /// Returns the result of the login attempt.
    /// 
    /// NOTE: If Droid CLI is installed, users should use 'droid' command instead
    /// for a more secure authentication flow.
    /// </summary>
    public static async Task<QuoteBar.Views.DroidLoginResult> LaunchLoginAsync()
    {
        lock (_lock)
        {
            if (_currentWindow != null)
            {
                Log("Login window already open");
                return QuoteBar.Views.DroidLoginResult.Cancelled();
            }
        }

        try
        {
            Log("Launching Droid OAuth login window");

            var window = new QuoteBar.Views.DroidLoginWindow();

            lock (_lock)
            {
                _currentWindow = window;
            }

            var result = await window.ShowLoginAsync();

            Log($"Login completed: Success={result.IsSuccess}, Cancelled={result.IsCancelled}");

            return result;
        }
        catch (Exception ex)
        {
            Log($"Login failed with exception: {ex.Message}");
            return QuoteBar.Views.DroidLoginResult.Failed(ex.Message);
        }
        finally
        {
            lock (_lock)
            {
                _currentWindow = null;
            }
        }
    }

    /// <summary>
    /// Sign out (clear OAuth session only - CLI auth is managed by CLI)
    /// </summary>
    public static void SignOut()
    {
        DroidSessionStore.ClearSession();
        DroidUsageCache.Invalidate();
        Log("Signed out from Droid OAuth");
    }

    /// <summary>
    /// Force refresh the current session
    /// </summary>
    public static async Task<bool> RefreshSessionAsync(CancellationToken cancellationToken = default)
    {
        // Try CLI auth first
        var auth = DroidCLIAuth.LoadAuth();
        if (auth != null && !string.IsNullOrEmpty(auth.RefreshToken))
        {
            var refreshed = await DroidCLIAuth.RefreshTokenAsync(auth.RefreshToken, cancellationToken);
            return refreshed != null;
        }

        // Try stored session
        var session = DroidSessionStore.GetSession();
        if (session != null && !string.IsNullOrEmpty(session.RefreshToken))
        {
            var refreshed = await DroidCLIAuth.RefreshTokenAsync(session.RefreshToken, cancellationToken);
            if (refreshed != null)
            {
                DroidSessionStore.SetSession(new DroidStoredSession
                {
                    AccessToken = refreshed.AccessToken,
                    RefreshToken = refreshed.RefreshToken,
                    ExpiresAt = refreshed.ExpiresAt,
                    SourceLabel = "OAuth (refreshed)",
                    AccountEmail = session.AccountEmail
                });
                return true;
            }
        }

        return false;
    }

    private static void Log(string message) => DebugLogger.Log("DroidLoginHelper", message);
}

#endregion
