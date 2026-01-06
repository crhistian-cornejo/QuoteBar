using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Core.Providers.Gemini;

/// <summary>
/// Response from retrieveUserQuota endpoint
/// </summary>
public sealed class GeminiQuotaResponse
{
    [JsonPropertyName("buckets")]
    public List<GeminiQuotaBucket>? Buckets { get; set; }
}

/// <summary>
/// Individual quota bucket for a model
/// </summary>
public sealed class GeminiQuotaBucket
{
    [JsonPropertyName("modelId")]
    public string? ModelId { get; set; }

    [JsonPropertyName("remainingFraction")]
    public double? RemainingFraction { get; set; }

    [JsonPropertyName("resetTime")]
    public string? ResetTime { get; set; }

    [JsonPropertyName("tokenType")]
    public string? TokenType { get; set; }

    /// <summary>
    /// Parsed reset time
    /// </summary>
    [JsonIgnore]
    public DateTime? ResetsAt
    {
        get
        {
            if (string.IsNullOrEmpty(ResetTime)) return null;
            if (DateTime.TryParse(ResetTime, out var dt))
                return dt.ToUniversalTime();
            return null;
        }
    }

    /// <summary>
    /// Percentage used (100 - remaining%)
    /// </summary>
    [JsonIgnore]
    public double UsedPercent => RemainingFraction.HasValue
        ? Math.Max(0, (1 - RemainingFraction.Value) * 100)
        : 0;

    /// <summary>
    /// Percentage remaining
    /// </summary>
    [JsonIgnore]
    public double RemainingPercent => RemainingFraction.HasValue
        ? Math.Max(0, RemainingFraction.Value * 100)
        : 100;
}

/// <summary>
/// Response from loadCodeAssist endpoint for tier detection
/// </summary>
public sealed class GeminiLoadCodeAssistResponse
{
    [JsonPropertyName("currentTier")]
    public GeminiTierInfo? CurrentTier { get; set; }
}

public sealed class GeminiTierInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
}

/// <summary>
/// Response from Google OAuth2 token refresh
/// </summary>
public sealed class GoogleOAuth2TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("id_token")]
    public string? IdToken { get; set; }

    [JsonPropertyName("expires_in")]
    public int? ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }
}

/// <summary>
/// Combined Gemini usage data
/// </summary>
public sealed class GeminiUsageData
{
    public List<GeminiQuotaBucket> Buckets { get; set; } = new();
    public string? TierId { get; set; }
    public string? TierDisplayName { get; set; }
    public string? Email { get; set; }
    public string? ProjectId { get; set; }
    public DateTime? ResetsAt { get; set; }

    // Calculated fields
    public GeminiQuotaBucket? MostConstrainedProModel { get; set; }
    public GeminiQuotaBucket? MostConstrainedFlashModel { get; set; }

    /// <summary>
    /// Detected plan type based on tier
    /// </summary>
    public string PlanType
    {
        get
        {
            return TierId?.ToLowerInvariant() switch
            {
                "free-tier" => "Free",
                "standard-tier" => "Paid",
                "legacy-tier" => "Legacy",
                _ => "Unknown"
            };
        }
    }
}

/// <summary>
/// Fetches usage data from Google Cloud Code PA API (Gemini internal API)
/// </summary>
public static class GeminiUsageFetcher
{
    private const string QuotaApiUrl = "https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota";
    private const string TierApiUrl = "https://cloudcode-pa.googleapis.com/v1internal:loadCodeAssist";
    private const string TokenRefreshUrl = "https://oauth2.googleapis.com/token";
    private const int TimeoutSeconds = 30;

    // Google OAuth2 client credentials - extracted from Gemini CLI installation
    private static string? _cachedClientId;
    private static string? _cachedClientSecret;
    private static readonly object _credentialsLock = new();

    /// <summary>
    /// Get Google OAuth client credentials from Gemini CLI installation
    /// </summary>
    private static (string? clientId, string? clientSecret) GetGoogleClientCredentials()
    {
        lock (_credentialsLock)
        {
            if (_cachedClientId != null && _cachedClientSecret != null)
            {
                return (_cachedClientId, _cachedClientSecret);
            }

            // Try to extract from Gemini CLI installation
            var creds = ExtractOAuthClientCredentialsFromCLI();
            if (creds.HasValue)
            {
                _cachedClientId = creds.Value.clientId;
                _cachedClientSecret = creds.Value.clientSecret;
                Log($"Extracted OAuth credentials from Gemini CLI");
                return creds.Value;
            }

            // Fallback to environment variables
            var envId = Environment.GetEnvironmentVariable("GEMINI_CLIENT_ID");
            var envSecret = Environment.GetEnvironmentVariable("GEMINI_CLIENT_SECRET");
            if (!string.IsNullOrEmpty(envId) && !string.IsNullOrEmpty(envSecret))
            {
                _cachedClientId = envId;
                _cachedClientSecret = envSecret;
                Log("Using OAuth credentials from environment variables");
                return (envId, envSecret);
            }

            Log("No OAuth client credentials found");
            return (null, null);
        }
    }

    /// <summary>
    /// Extract OAuth client credentials from Gemini CLI oauth2.js file
    /// </summary>
    private static (string clientId, string clientSecret)? ExtractOAuthClientCredentialsFromCLI()
    {
        try
        {
            // Find Gemini CLI installation path
            var geminiPath = FindGeminiCliPath();
            if (string.IsNullOrEmpty(geminiPath))
            {
                Log("Gemini CLI not found in PATH");
                return null;
            }

            Log($"Found Gemini CLI at: {geminiPath}");

            // Get the real path (resolve symlinks if any)
            var realPath = geminiPath;
            try
            {
                var fileInfo = new FileInfo(geminiPath);
                if (fileInfo.LinkTarget != null)
                {
                    realPath = fileInfo.LinkTarget;
                    if (!Path.IsPathRooted(realPath))
                    {
                        realPath = Path.Combine(Path.GetDirectoryName(geminiPath) ?? "", realPath);
                    }
                }
            }
            catch { }

            // Navigate to find oauth2.js file
            var binDir = Path.GetDirectoryName(realPath) ?? "";
            var baseDir = Path.GetDirectoryName(binDir) ?? "";

            // Possible paths where oauth2.js might be located
            var possiblePaths = new[]
            {
                // npm global installation
                Path.Combine(baseDir, "node_modules", "@google", "gemini-cli-core", "dist", "src", "code_assist", "oauth2.js"),
                Path.Combine(baseDir, "lib", "node_modules", "@google", "gemini-cli-core", "dist", "src", "code_assist", "oauth2.js"),
                // npm nested inside gemini-cli
                Path.Combine(baseDir, "node_modules", "@google", "gemini-cli", "node_modules", "@google", "gemini-cli-core", "dist", "src", "code_assist", "oauth2.js"),
                // Homebrew/bun structure
                Path.Combine(baseDir, "libexec", "lib", "node_modules", "@google", "gemini-cli", "node_modules", "@google", "gemini-cli-core", "dist", "src", "code_assist", "oauth2.js"),
                // Windows AppData npm global
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@google", "gemini-cli-core", "dist", "src", "code_assist", "oauth2.js"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@google", "gemini-cli", "node_modules", "@google", "gemini-cli-core", "dist", "src", "code_assist", "oauth2.js"),
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    var content = File.ReadAllText(path);
                    var creds = ParseOAuthCredentialsFromJs(content);
                    if (creds.HasValue)
                    {
                        Log($"Found OAuth credentials in: {path}");
                        return creds;
                    }
                }
            }

            Log("oauth2.js not found in any expected location");
            return null;
        }
        catch (Exception ex)
        {
            Log($"Error extracting OAuth credentials: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Find Gemini CLI path from PATH environment variable
    /// </summary>
    private static string? FindGeminiCliPath()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(Path.PathSeparator);

        var geminiNames = new[] { "gemini.cmd", "gemini.exe", "gemini.ps1", "gemini" };

        foreach (var dir in paths)
        {
            foreach (var name in geminiNames)
            {
                var fullPath = Path.Combine(dir, name);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        // Also check common Windows npm global paths
        var npmGlobalPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"),
        };

        foreach (var dir in npmGlobalPaths)
        {
            foreach (var name in geminiNames)
            {
                var fullPath = Path.Combine(dir, name);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Parse OAuth client ID and secret from oauth2.js content
    /// </summary>
    private static (string clientId, string clientSecret)? ParseOAuthCredentialsFromJs(string content)
    {
        try
        {
            // Match: const OAUTH_CLIENT_ID = '...';
            // or: OAUTH_CLIENT_ID: '...'
            var clientIdPatterns = new[]
            {
                @"OAUTH_CLIENT_ID\s*[=:]\s*['""]([^'""]+)['""]",
                @"clientId\s*[=:]\s*['""]([^'""]+)['""]",
            };

            var secretPatterns = new[]
            {
                @"OAUTH_CLIENT_SECRET\s*[=:]\s*['""]([^'""]+)['""]",
                @"clientSecret\s*[=:]\s*['""]([^'""]+)['""]",
            };

            string? clientId = null;
            string? clientSecret = null;

            foreach (var pattern in clientIdPatterns)
            {
                var match = Regex.Match(content, pattern);
                if (match.Success)
                {
                    clientId = match.Groups[1].Value;
                    break;
                }
            }

            foreach (var pattern in secretPatterns)
            {
                var match = Regex.Match(content, pattern);
                if (match.Success)
                {
                    clientSecret = match.Groups[1].Value;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
            {
                return (clientId, clientSecret);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    // Model tier classification
    private static readonly HashSet<string> ProModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "gemini-2.0-pro",
        "gemini-1.5-pro",
        "gemini-pro",
        "gemini-3-pro",
        "gemini-ultra"
    };

    private static readonly HashSet<string> FlashModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "gemini-2.0-flash",
        "gemini-1.5-flash",
        "gemini-flash",
        "gemini-3-flash",
        "gemini-2.5-flash"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Fetch complete usage data from Gemini API
    /// </summary>
    public static async Task<GeminiUsageData> FetchUsageAsync(
        GeminiOAuthCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        var usageData = new GeminiUsageData
        {
            Email = credentials.Email
        };

        // Check if token is expired and refresh if needed
        var accessToken = credentials.AccessToken;
        if (credentials.IsExpired && !string.IsNullOrEmpty(credentials.RefreshToken))
        {
            Log("Token expired, refreshing...");
            var refreshedCreds = await RefreshTokenAsync(credentials, cancellationToken);
            if (refreshedCreds != null)
            {
                accessToken = refreshedCreds.AccessToken;
                // Save refreshed credentials
                GeminiOAuthCredentialsStore.SaveCredentials(refreshedCreds);
            }
            else
            {
                Log("Token refresh failed, using expired token");
            }
        }

        using var client = CreateHttpClient(accessToken);

        // Fetch quota and tier info in parallel
        var quotaTask = FetchQuotaAsync(client, cancellationToken);
        var tierTask = FetchTierAsync(client, cancellationToken);

        try
        {
            await Task.WhenAll(quotaTask, tierTask);
        }
        catch (Exception ex)
        {
            Log($"Error fetching Gemini data: {ex.Message}");
        }

        // Process quota data
        var quotaResponse = quotaTask.IsCompletedSuccessfully ? quotaTask.Result : null;
        if (quotaResponse?.Buckets != null)
        {
            usageData.Buckets = quotaResponse.Buckets;

            // Find most constrained Pro and Flash models
            usageData.MostConstrainedProModel = quotaResponse.Buckets
                .Where(b => IsProModel(b.ModelId))
                .OrderBy(b => b.RemainingFraction ?? 1)
                .FirstOrDefault();

            usageData.MostConstrainedFlashModel = quotaResponse.Buckets
                .Where(b => IsFlashModel(b.ModelId))
                .OrderBy(b => b.RemainingFraction ?? 1)
                .FirstOrDefault();

            // Get reset time from first bucket
            usageData.ResetsAt = quotaResponse.Buckets
                .Select(b => b.ResetsAt)
                .FirstOrDefault(d => d.HasValue);
        }

        // Process tier data
        var tierResponse = tierTask.IsCompletedSuccessfully ? tierTask.Result : null;
        if (tierResponse?.CurrentTier != null)
        {
            usageData.TierId = tierResponse.CurrentTier.Id;
            usageData.TierDisplayName = tierResponse.CurrentTier.DisplayName;
        }

        Log($"Usage data: Pro={usageData.MostConstrainedProModel?.UsedPercent:F1}%, " +
            $"Flash={usageData.MostConstrainedFlashModel?.UsedPercent:F1}%, " +
            $"Tier={usageData.TierId}");

        return usageData;
    }

    private static async Task<GeminiQuotaResponse?> FetchQuotaAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            // POST request with optional project ID
            var requestBody = new { project = (string?)null };
            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync(QuotaApiUrl, content, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            Log($"POST retrieveUserQuota: Status={response.StatusCode}, Body={responseContent.Substring(0, Math.Min(500, responseContent.Length))}");

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<GeminiQuotaResponse>(responseContent, JsonOptions);
            }

            return null;
        }
        catch (Exception ex)
        {
            Log($"FetchQuota error: {ex.Message}");
            return null;
        }
    }

    private static async Task<GeminiLoadCodeAssistResponse?> FetchTierAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestBody = new
            {
                metadata = new
                {
                    ideType = "GEMINI_CLI",
                    pluginType = "GEMINI"
                }
            };
            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync(TierApiUrl, content, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            Log($"POST loadCodeAssist: Status={response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<GeminiLoadCodeAssistResponse>(responseContent, JsonOptions);
            }

            return null;
        }
        catch (Exception ex)
        {
            Log($"FetchTier error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Refresh an expired access token using the refresh token
    /// </summary>
    public static async Task<GeminiOAuthCredentials?> RefreshTokenAsync(
        GeminiOAuthCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(credentials.RefreshToken))
        {
            Log("No refresh token available");
            return null;
        }

        // Get OAuth client credentials from Gemini CLI
        var (clientId, clientSecret) = GetGoogleClientCredentials();
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
        {
            Log("Cannot refresh token: OAuth client credentials not available. " +
                "Ensure Gemini CLI is installed or set GEMINI_CLIENT_ID and GEMINI_CLIENT_SECRET environment variables.");
            return null;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };

            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("refresh_token", credentials.RefreshToken),
                new KeyValuePair<string, string>("grant_type", "refresh_token")
            });

            var response = await client.PostAsync(TokenRefreshUrl, formData, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            Log($"Token refresh: Status={response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var tokenResponse = JsonSerializer.Deserialize<GoogleOAuth2TokenResponse>(content, JsonOptions);
                if (tokenResponse?.AccessToken != null)
                {
                    // Calculate new expiry time
                    var expiresIn = tokenResponse.ExpiresIn ?? 3600;
                    var expiryDateMs = DateTimeOffset.UtcNow.AddSeconds(expiresIn).ToUnixTimeMilliseconds();

                    var newCreds = new GeminiOAuthCredentials
                    {
                        AccessToken = tokenResponse.AccessToken,
                        IdToken = tokenResponse.IdToken ?? credentials.IdToken,
                        RefreshToken = credentials.RefreshToken, // Keep existing refresh token
                        ExpiryDateMs = expiryDateMs,
                        Email = credentials.Email
                    };

                    Log($"Token refreshed, expires at: {newCreds.ExpiresAt}");
                    return newCreds;
                }
            }

            Log($"Token refresh failed: {content}");
            return null;
        }
        catch (Exception ex)
        {
            Log($"RefreshToken error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Convert usage data to UsageSnapshot
    /// </summary>
    public static UsageSnapshot ToUsageSnapshot(GeminiUsageData usageData)
    {
        RateWindow? primary = null;
        RateWindow? secondary = null;
        ProviderIdentity? identity = null;

        // Primary: Pro models (most constrained)
        if (usageData.MostConstrainedProModel != null)
        {
            var bucket = usageData.MostConstrainedProModel;
            primary = new RateWindow
            {
                UsedPercent = bucket.UsedPercent,
                Used = bucket.UsedPercent,
                Limit = 100,
                ResetsAt = bucket.ResetsAt,
                ResetDescription = FormatResetTime(bucket.ResetsAt),
                Unit = FormatModelName(bucket.ModelId)
            };
        }
        else
        {
            // No Pro model data - show as 0%
            primary = new RateWindow
            {
                UsedPercent = 0,
                Used = 0,
                Limit = 100,
                Unit = "Pro"
            };
        }

        // Secondary: Flash models (most constrained)
        if (usageData.MostConstrainedFlashModel != null)
        {
            var bucket = usageData.MostConstrainedFlashModel;
            secondary = new RateWindow
            {
                UsedPercent = bucket.UsedPercent,
                Used = bucket.UsedPercent,
                Limit = 100,
                ResetsAt = bucket.ResetsAt,
                ResetDescription = FormatResetTime(bucket.ResetsAt),
                Unit = FormatModelName(bucket.ModelId)
            };
        }
        else
        {
            // No Flash model data - show as 0%
            secondary = new RateWindow
            {
                UsedPercent = 0,
                Used = 0,
                Limit = 100,
                Unit = "Flash"
            };
        }

        // Identity
        identity = new ProviderIdentity
        {
            Email = usageData.Email,
            PlanType = usageData.PlanType,
            AccountId = usageData.Email
        };

        return new UsageSnapshot
        {
            ProviderId = "gemini",
            Primary = primary,
            Secondary = secondary,
            Identity = identity,
            FetchedAt = DateTime.UtcNow
        };
    }

    private static bool IsProModel(string? modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return false;

        // Check exact match first
        if (ProModels.Contains(modelId)) return true;

        // Check if model name contains "pro"
        return modelId.Contains("pro", StringComparison.OrdinalIgnoreCase) &&
               !modelId.Contains("flash", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFlashModel(string? modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return false;

        // Check exact match first
        if (FlashModels.Contains(modelId)) return true;

        // Check if model name contains "flash"
        return modelId.Contains("flash", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FormatModelName(string? modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return null;

        // Shorten model names for display
        return modelId
            .Replace("gemini-", "")
            .Replace("-", " ")
            .ToUpperInvariant() switch
        {
            var s when s.Contains("2.0 PRO") => "2.0 Pro",
            var s when s.Contains("2.0 FLASH") => "2.0 Flash",
            var s when s.Contains("2.5 FLASH") => "2.5 Flash",
            var s when s.Contains("1.5 PRO") => "1.5 Pro",
            var s when s.Contains("1.5 FLASH") => "1.5 Flash",
            var s when s.Contains("3 PRO") => "3 Pro",
            var s when s.Contains("3 FLASH") => "3 Flash",
            _ => modelId.Replace("gemini-", "")
        };
    }

    private static string? FormatResetTime(DateTime? resetsAt)
    {
        if (!resetsAt.HasValue) return null;

        var diff = resetsAt.Value - DateTime.UtcNow;
        if (diff.TotalMinutes <= 0) return "now";

        if (diff.TotalDays >= 1)
        {
            int days = (int)diff.TotalDays;
            return $"Resets in {days}d";
        }

        if (diff.TotalHours >= 1)
        {
            int hours = (int)diff.TotalHours;
            return $"Resets in {hours}h";
        }

        return $"Resets in {(int)diff.TotalMinutes}m";
    }

    private static HttpClient CreateHttpClient(string accessToken)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("User-Agent", "NativeBar");

        return client;
    }

    private static void Log(string message)
    {
        DebugLogger.Log("GeminiUsageFetcher", message);
    }
}

public enum GeminiFetchError
{
    None,
    Unauthorized,
    InvalidResponse,
    ServerError,
    NetworkError,
    NotConfigured,
    AuthTypeNotSupported
}

public class GeminiFetchException : Exception
{
    public GeminiFetchError ErrorType { get; }

    public GeminiFetchException(string message, GeminiFetchError errorType)
        : base(message)
    {
        ErrorType = errorType;
    }
}
