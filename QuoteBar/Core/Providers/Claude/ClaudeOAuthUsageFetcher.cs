using System.Text.Json;
using System.Text.Json.Serialization;
using QuoteBar.Core.Models;

namespace QuoteBar.Core.Providers.Claude;

/// <summary>
/// Response from the Claude OAuth usage API
/// </summary>
public sealed class OAuthUsageResponse
{
    [JsonPropertyName("five_hour")]
    public OAuthUsageWindow? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public OAuthUsageWindow? SevenDay { get; set; }

    [JsonPropertyName("seven_day_oauth_apps")]
    public OAuthUsageWindow? SevenDayOAuthApps { get; set; }

    [JsonPropertyName("seven_day_opus")]
    public OAuthUsageWindow? SevenDayOpus { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public OAuthUsageWindow? SevenDaySonnet { get; set; }

    [JsonPropertyName("iguana_necktie")]
    public OAuthUsageWindow? IguanaNecktie { get; set; }

    [JsonPropertyName("extra_usage")]
    public OAuthExtraUsage? ExtraUsage { get; set; }
}

/// <summary>
/// Represents a usage window from the OAuth API
/// </summary>
public sealed class OAuthUsageWindow
{
    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }
}

/// <summary>
/// Extra usage/overage information from the OAuth API
/// </summary>
public sealed class OAuthExtraUsage
{
    [JsonPropertyName("is_enabled")]
    public bool? IsEnabled { get; set; }

    [JsonPropertyName("monthly_limit")]
    public double? MonthlyLimit { get; set; }

    [JsonPropertyName("used_credits")]
    public double? UsedCredits { get; set; }

    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}

/// <summary>
/// Fetches usage data from the Claude OAuth API
/// </summary>
public static class ClaudeOAuthUsageFetcher
{
    private const string BaseUrl = "https://api.anthropic.com";
    private const string UsagePath = "/api/oauth/usage";
    private const string BetaHeader = "oauth-2025-04-20";
    private const int TimeoutSeconds = 30;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Fetch usage data using the provided access token
    /// </summary>
    public static async Task<OAuthUsageResponse> FetchUsageAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{UsagePath}");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.Add("User-Agent", "QuoteBar");

        Core.Services.DebugLogger.Log("ClaudeOAuthUsageFetcher", $"Fetching from {BaseUrl}{UsagePath}");

        try
        {
            var response = await client.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            Core.Services.DebugLogger.Log("ClaudeOAuthUsageFetcher", $"Status={response.StatusCode}, Body={content.Substring(0, Math.Min(200, content.Length))}...");

            switch ((int)response.StatusCode)
            {
                case 200:
                    return DecodeUsageResponse(content);
                case 401:
                case 403:
                    throw new ClaudeOAuthFetchException("Unauthorized. Run `claude` to re-authenticate.", ClaudeOAuthFetchError.Unauthorized);
                default:
                    throw new ClaudeOAuthFetchException(
                        $"Server error: HTTP {(int)response.StatusCode} - {content}",
                        ClaudeOAuthFetchError.ServerError);
            }
        }
        catch (ClaudeOAuthFetchException)
        {
            throw;
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken != cancellationToken)
        {
            throw new ClaudeOAuthFetchException("Request timed out", ClaudeOAuthFetchError.NetworkError);
        }
        catch (Exception ex)
        {
            throw new ClaudeOAuthFetchException($"Network error: {ex.Message}", ClaudeOAuthFetchError.NetworkError);
        }
    }

    private static OAuthUsageResponse DecodeUsageResponse(string json)
    {
        try
        {
            var response = JsonSerializer.Deserialize<OAuthUsageResponse>(json, JsonOptions);
            return response ?? throw new ClaudeOAuthFetchException("Invalid response format", ClaudeOAuthFetchError.InvalidResponse);
        }
        catch (JsonException ex)
        {
            throw new ClaudeOAuthFetchException($"Failed to parse response: {ex.Message}", ClaudeOAuthFetchError.InvalidResponse);
        }
    }

    /// <summary>
    /// Parse an ISO8601 date string to DateTime
    /// </summary>
    public static DateTime? ParseISO8601Date(string? dateString)
    {
        if (string.IsNullOrEmpty(dateString))
            return null;

        // Try parsing with various formats
        if (DateTime.TryParse(dateString, null, System.Globalization.DateTimeStyles.RoundtripKind, out var result))
            return result.ToUniversalTime();

        return null;
    }

    /// <summary>
    /// Normalize utilization value to percentage (0-100)
    /// Claude API may return utilization as decimal (0.57) or percentage (57)
    /// </summary>
    private static double NormalizeUtilization(double? utilization)
    {
        if (!utilization.HasValue) return 0;
        var value = utilization.Value;
        // If value > 1, it's already a percentage; otherwise multiply by 100
        return value > 1 ? value : value * 100;
    }

    /// <summary>
    /// Convert OAuth API response to UsageSnapshot
    /// </summary>
    public static UsageSnapshot ToUsageSnapshot(OAuthUsageResponse response, ClaudeOAuthCredentials? credentials = null)
    {
        RateWindow? primary = null;
        RateWindow? secondary = null;
        RateWindow? tertiary = null;
        ProviderCost? cost = null;

        // Primary: 5-hour window (Session)
        if (response.FiveHour != null)
        {
            primary = new RateWindow
            {
                UsedPercent = NormalizeUtilization(response.FiveHour.Utilization),
                WindowMinutes = 300, // 5 hours
                ResetsAt = ParseISO8601Date(response.FiveHour.ResetsAt),
                ResetDescription = FormatResetTime(response.FiveHour.ResetsAt)
            };
        }

        // Secondary: 7-day window (Weekly)
        if (response.SevenDay != null)
        {
            secondary = new RateWindow
            {
                UsedPercent = NormalizeUtilization(response.SevenDay.Utilization),
                WindowMinutes = 10080, // 7 days
                ResetsAt = ParseISO8601Date(response.SevenDay.ResetsAt),
                ResetDescription = FormatResetTime(response.SevenDay.ResetsAt)
            };
        }

        // Tertiary: Sonnet 7-day window
        if (response.SevenDaySonnet != null)
        {
            tertiary = new RateWindow
            {
                UsedPercent = NormalizeUtilization(response.SevenDaySonnet.Utilization),
                WindowMinutes = 10080, // 7 days
                ResetsAt = ParseISO8601Date(response.SevenDaySonnet.ResetsAt),
                ResetDescription = FormatResetTime(response.SevenDaySonnet.ResetsAt)
            };
        }

        // Extra usage as cost (API returns cents, convert to dollars)
        if (response.ExtraUsage?.IsEnabled == true && response.ExtraUsage.UsedCredits.HasValue)
        {
            cost = new ProviderCost
            {
                TotalCostUSD = response.ExtraUsage.UsedCredits.Value / 100.0,
                StartDate = DateTime.UtcNow.Date.AddDays(-DateTime.UtcNow.Day + 1), // First of month
                EndDate = DateTime.UtcNow
            };
        }

        // Determine plan type from subscription type or rate limit tier
        var planType = credentials?.SubscriptionType ?? credentials?.RateLimitTier ?? "Max";
        
        // Normalize plan type display
        if (planType.Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            // Check rate limit tier for level info
            var tier = credentials?.RateLimitTier ?? "";
            if (tier.Contains("5", StringComparison.OrdinalIgnoreCase))
                planType = "Max (Level 5)";
            else if (tier.Contains("4", StringComparison.OrdinalIgnoreCase))
                planType = "Max (Level 4)";
            else
                planType = "Max";
        }
        else if (planType.Equals("pro", StringComparison.OrdinalIgnoreCase))
            planType = "Pro";
        else if (planType.Equals("free", StringComparison.OrdinalIgnoreCase))
            planType = "Free";

        return new UsageSnapshot
        {
            ProviderId = "claude",
            Primary = primary ?? new RateWindow { UsedPercent = 0, WindowMinutes = 300 },
            Secondary = secondary,
            Tertiary = tertiary,
            Cost = cost,
            Identity = new ProviderIdentity { PlanType = planType },
            FetchedAt = DateTime.UtcNow
        };
    }

    private static string? FormatResetTime(string? isoDate)
    {
        var date = ParseISO8601Date(isoDate);
        if (!date.HasValue)
            return null;

        var diff = date.Value - DateTime.UtcNow;
        if (diff.TotalMinutes <= 0)
            return "now";

        if (diff.TotalDays >= 1)
        {
            int days = (int)diff.TotalDays;
            int hours = diff.Hours;
            if (hours > 0)
                return $"in {days}d {hours}h";
            return $"in {days}d";
        }

        if (diff.TotalHours >= 1)
        {
            int hours = (int)diff.TotalHours;
            int minutes = diff.Minutes;
            if (minutes > 0)
                return $"in {hours}h {minutes}m";
            return $"in {hours}h";
        }

        return $"in {(int)diff.TotalMinutes}m";
    }
}

public enum ClaudeOAuthFetchError
{
    None,
    Unauthorized,
    InvalidResponse,
    ServerError,
    NetworkError
}

public class ClaudeOAuthFetchException : Exception
{
    public ClaudeOAuthFetchError ErrorType { get; }

    public ClaudeOAuthFetchException(string message, ClaudeOAuthFetchError errorType)
        : base(message)
    {
        ErrorType = errorType;
    }
}
