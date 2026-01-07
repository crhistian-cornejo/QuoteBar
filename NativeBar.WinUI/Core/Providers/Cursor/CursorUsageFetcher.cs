using System.Text.Json;
using System.Text.Json.Serialization;
using NativeBar.WinUI.Core.Models;

namespace NativeBar.WinUI.Core.Providers.Cursor;

#region API Models

/// <summary>
/// Response from GET https://cursor.com/api/usage-summary
/// </summary>
public sealed class CursorUsageSummary
{
    [JsonPropertyName("billingCycleStart")]
    public string? BillingCycleStart { get; set; }

    [JsonPropertyName("billingCycleEnd")]
    public string? BillingCycleEnd { get; set; }

    [JsonPropertyName("membershipType")]
    public string? MembershipType { get; set; }

    [JsonPropertyName("limitType")]
    public string? LimitType { get; set; }

    [JsonPropertyName("isUnlimited")]
    public bool? IsUnlimited { get; set; }

    [JsonPropertyName("autoModelSelectedDisplayMessage")]
    public string? AutoModelSelectedDisplayMessage { get; set; }

    [JsonPropertyName("namedModelSelectedDisplayMessage")]
    public string? NamedModelSelectedDisplayMessage { get; set; }

    [JsonPropertyName("individualUsage")]
    public CursorIndividualUsage? IndividualUsage { get; set; }

    [JsonPropertyName("teamUsage")]
    public CursorTeamUsage? TeamUsage { get; set; }
}

public sealed class CursorIndividualUsage
{
    [JsonPropertyName("plan")]
    public CursorPlanUsage? Plan { get; set; }

    [JsonPropertyName("onDemand")]
    public CursorOnDemandUsage? OnDemand { get; set; }
}

public sealed class CursorPlanUsage
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Usage in cents (e.g., 2000 = $20.00)</summary>
    [JsonPropertyName("used")]
    public int? Used { get; set; }

    /// <summary>Limit in cents (e.g., 2000 = $20.00)</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    /// <summary>Remaining in cents</summary>
    [JsonPropertyName("remaining")]
    public int? Remaining { get; set; }

    [JsonPropertyName("breakdown")]
    public CursorPlanBreakdown? Breakdown { get; set; }

    [JsonPropertyName("autoPercentUsed")]
    public double? AutoPercentUsed { get; set; }

    [JsonPropertyName("apiPercentUsed")]
    public double? ApiPercentUsed { get; set; }

    [JsonPropertyName("totalPercentUsed")]
    public double? TotalPercentUsed { get; set; }
}

public sealed class CursorPlanBreakdown
{
    [JsonPropertyName("included")]
    public int? Included { get; set; }

    [JsonPropertyName("bonus")]
    public int? Bonus { get; set; }

    [JsonPropertyName("total")]
    public int? Total { get; set; }
}

public sealed class CursorOnDemandUsage
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>Usage in cents</summary>
    [JsonPropertyName("used")]
    public int? Used { get; set; }

    /// <summary>Limit in cents (null if unlimited)</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    /// <summary>Remaining in cents (null if unlimited)</summary>
    [JsonPropertyName("remaining")]
    public int? Remaining { get; set; }
}

public sealed class CursorTeamUsage
{
    [JsonPropertyName("onDemand")]
    public CursorOnDemandUsage? OnDemand { get; set; }
}

/// <summary>
/// Response from GET https://cursor.com/api/auth/me
/// </summary>
public sealed class CursorUserInfo
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("email_verified")]
    public bool? EmailVerified { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("sub")]
    public string? Sub { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }
}

/// <summary>
/// Response from GET https://cursor.com/api/usage?user=ID (legacy request-based plans)
/// </summary>
public sealed class CursorUsageResponse
{
    [JsonPropertyName("gpt-4")]
    public CursorModelUsage? Gpt4 { get; set; }

    [JsonPropertyName("startOfMonth")]
    public string? StartOfMonth { get; set; }
}

public sealed class CursorModelUsage
{
    [JsonPropertyName("numRequests")]
    public int? NumRequests { get; set; }

    [JsonPropertyName("numRequestsTotal")]
    public int? NumRequestsTotal { get; set; }

    [JsonPropertyName("numTokens")]
    public int? NumTokens { get; set; }

    [JsonPropertyName("maxRequestUsage")]
    public int? MaxRequestUsage { get; set; }

    [JsonPropertyName("maxTokenUsage")]
    public int? MaxTokenUsage { get; set; }
}

#endregion

#region Status Snapshot

/// <summary>
/// Cursor-specific status snapshot with all usage details
/// </summary>
public sealed class CursorStatusSnapshot
{
    /// <summary>Percentage of included plan usage (0-100)</summary>
    public double PlanPercentUsed { get; init; }

    /// <summary>Auto model usage percentage (0-100) - for auto-selected models</summary>
    public double? AutoPercentUsed { get; init; }

    /// <summary>API usage percentage (0-100) - for named/specific models</summary>
    public double? ApiPercentUsed { get; init; }

    /// <summary>Included plan usage in USD</summary>
    public double PlanUsedUSD { get; init; }

    /// <summary>Included plan limit in USD</summary>
    public double PlanLimitUSD { get; init; }

    /// <summary>On-demand usage in USD</summary>
    public double OnDemandUsedUSD { get; init; }

    /// <summary>On-demand limit in USD (null if unlimited)</summary>
    public double? OnDemandLimitUSD { get; init; }

    /// <summary>Team on-demand usage in USD (for team plans)</summary>
    public double? TeamOnDemandUsedUSD { get; init; }

    /// <summary>Team on-demand limit in USD</summary>
    public double? TeamOnDemandLimitUSD { get; init; }

    /// <summary>Billing cycle reset date</summary>
    public DateTime? BillingCycleEnd { get; init; }

    /// <summary>Membership type (e.g., "enterprise", "pro", "hobby")</summary>
    public string? MembershipType { get; init; }

    /// <summary>User email</summary>
    public string? AccountEmail { get; init; }

    /// <summary>User name</summary>
    public string? AccountName { get; init; }

    /// <summary>Raw API response for debugging</summary>
    public string? RawJSON { get; init; }

    // Legacy Plan (Request-Based) Fields
    public int? RequestsUsed { get; init; }
    public int? RequestsLimit { get; init; }

    /// <summary>Whether this is a legacy request-based plan (vs token-based)</summary>
    public bool IsLegacyRequestPlan => RequestsLimit != null;

    /// <summary>Convert to UsageSnapshot for the common provider interface</summary>
    public UsageSnapshot ToUsageSnapshot()
    {
        // PRIMARY: Total plan usage (totalPercentUsed) 
        // This is the overall usage combining Auto + API
        double primaryUsedPercent;
        if (IsLegacyRequestPlan && RequestsUsed.HasValue && RequestsLimit.HasValue && RequestsLimit.Value > 0)
        {
            primaryUsedPercent = (double)RequestsUsed.Value / RequestsLimit.Value * 100;
        }
        else
        {
            primaryUsedPercent = PlanPercentUsed; // totalPercentUsed from API
        }

        var primary = new RateWindow
        {
            UsedPercent = primaryUsedPercent,
            WindowMinutes = null,
            ResetsAt = BillingCycleEnd,
            ResetDescription = BillingCycleEnd.HasValue ? FormatResetDate(BillingCycleEnd.Value) : null,
            Label = "Total"
        };

        // SECONDARY: Auto model usage (autoPercentUsed)
        // Usage when Cursor automatically selects the model
        RateWindow? secondary = null;
        if (AutoPercentUsed.HasValue)
        {
            secondary = new RateWindow
            {
                UsedPercent = AutoPercentUsed.Value,
                WindowMinutes = null,
                ResetsAt = BillingCycleEnd,
                ResetDescription = BillingCycleEnd.HasValue ? FormatResetDate(BillingCycleEnd.Value) : null,
                Label = "Auto"
            };
        }

        // TERTIARY: API/Named model usage (apiPercentUsed)
        // Usage when user selects a specific model (claude, gpt, etc.)
        RateWindow? tertiary = null;
        if (ApiPercentUsed.HasValue)
        {
            tertiary = new RateWindow
            {
                UsedPercent = ApiPercentUsed.Value,
                WindowMinutes = null,
                ResetsAt = BillingCycleEnd,
                ResetDescription = BillingCycleEnd.HasValue ? FormatResetDate(BillingCycleEnd.Value) : null,
                Label = "API"
            };
        }

        // Provider cost snapshot for on-demand usage
        ProviderCost? cost = null;
        if (OnDemandUsedUSD > 0)
        {
            cost = new ProviderCost
            {
                TotalCostUSD = OnDemandUsedUSD,
                StartDate = DateTime.UtcNow.Date.AddDays(-DateTime.UtcNow.Day + 1),
                EndDate = DateTime.UtcNow
            };
        }

        return new UsageSnapshot
        {
            ProviderId = "cursor",
            Primary = primary,
            Secondary = secondary,
            Tertiary = tertiary,
            Cost = cost,
            Identity = new ProviderIdentity
            {
                Email = AccountEmail,
                PlanType = MembershipType != null ? FormatMembershipType(MembershipType) : null
            },
            FetchedAt = DateTime.UtcNow
        };
    }

    private static string FormatResetDate(DateTime date)
    {
        return $"Resets {date:MMM d 'at' h:mmtt}";
    }

    private static string FormatMembershipType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "enterprise" => "Cursor Enterprise",
            "pro" => "Cursor Pro",
            "hobby" => "Cursor Hobby",
            "team" => "Cursor Team",
            _ => $"Cursor {char.ToUpper(type[0]) + type[1..].ToLower()}"
        };
    }
}

#endregion

#region Errors

public enum CursorFetchError
{
    None,
    NotLoggedIn,
    NetworkError,
    ParseFailed,
    NoSessionCookie
}

public class CursorFetchException : Exception
{
    public CursorFetchError ErrorType { get; }

    public CursorFetchException(string message, CursorFetchError errorType)
        : base(message)
    {
        ErrorType = errorType;
    }
}

#endregion

#region Usage Fetcher

/// <summary>
/// Fetches usage data from Cursor API endpoints using cookie-based authentication
/// </summary>
public static class CursorUsageFetcher
{
    private const string BaseUrl = "https://cursor.com";
    private const int TimeoutSeconds = 15;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Fetch Cursor usage with the provided cookie header
    /// </summary>
    public static async Task<CursorStatusSnapshot> FetchAsync(
        string cookieHeader,
        Action<string>? logger = null,
        CancellationToken cancellationToken = default)
    {
        var log = (string msg) => logger?.Invoke($"[cursor-fetch] {msg}");

        log("Starting fetch with cookie header");

        // Fetch usage summary and user info in parallel
        var usageSummaryTask = FetchUsageSummaryAsync(cookieHeader, cancellationToken);
        var userInfoTask = FetchUserInfoAsync(cookieHeader, cancellationToken);

        var (usageSummary, rawJson) = await usageSummaryTask;
        var userInfo = await userInfoTask.ContinueWith(t => t.IsFaulted ? null : t.Result, cancellationToken);

        log($"Got usage summary, user={userInfo?.Email ?? "unknown"}");

        // Fetch legacy request usage if user has a sub ID
        CursorUsageResponse? requestUsage = null;
        if (userInfo?.Sub != null)
        {
            try
            {
                requestUsage = await FetchRequestUsageAsync(userInfo.Sub, cookieHeader, cancellationToken);
                log("Got legacy request usage");
            }
            catch (Exception ex)
            {
                log($"Legacy request usage failed (ok to ignore): {ex.Message}");
            }
        }

        return ParseUsageSummary(usageSummary, userInfo, rawJson, requestUsage);
    }

    private static async Task<(CursorUsageSummary, string)> FetchUsageSummaryAsync(
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/usage-summary");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Cookie", cookieHeader);

        var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new CursorFetchException("Not logged in to Cursor", CursorFetchError.NotLoggedIn);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new CursorFetchException($"HTTP {(int)response.StatusCode}", CursorFetchError.NetworkError);
        }

        try
        {
            var summary = JsonSerializer.Deserialize<CursorUsageSummary>(content, JsonOptions);
            if (summary == null)
                throw new CursorFetchException("Empty response", CursorFetchError.ParseFailed);
            return (summary, content);
        }
        catch (JsonException ex)
        {
            throw new CursorFetchException($"JSON parse failed: {ex.Message}", CursorFetchError.ParseFailed);
        }
    }

    private static async Task<CursorUserInfo?> FetchUserInfoAsync(
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/auth/me");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Cookie", cookieHeader);

        var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<CursorUserInfo>(content, JsonOptions);
    }

    private static async Task<CursorUsageResponse?> FetchRequestUsageAsync(
        string userId,
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/usage?user={Uri.EscapeDataString(userId)}");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Cookie", cookieHeader);

        var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<CursorUsageResponse>(content, JsonOptions);
    }

    private static CursorStatusSnapshot ParseUsageSummary(
        CursorUsageSummary summary,
        CursorUserInfo? userInfo,
        string? rawJson,
        CursorUsageResponse? requestUsage)
    {
        // Parse billing cycle end date
        DateTime? billingCycleEnd = null;
        if (!string.IsNullOrEmpty(summary.BillingCycleEnd))
        {
            if (DateTime.TryParse(summary.BillingCycleEnd, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            {
                billingCycleEnd = parsed.ToUniversalTime();
            }
        }

        // Convert cents to USD
        double planUsedRaw = summary.IndividualUsage?.Plan?.Used ?? 0;
        double planLimitRaw = summary.IndividualUsage?.Plan?.Breakdown?.Total
            ?? summary.IndividualUsage?.Plan?.Limit ?? 0;

        double planUsed = planUsedRaw / 100.0;
        double planLimit = planLimitRaw / 100.0;

        // PRIORITY: Use totalPercentUsed from API first (matches Cursor's display message)
        // Fallback to calculated value only if API doesn't provide it
        double planPercentUsed;
        if (summary.IndividualUsage?.Plan?.TotalPercentUsed.HasValue == true)
        {
            // Use the exact percentage Cursor provides (e.g., 25.49%)
            var pct = summary.IndividualUsage.Plan.TotalPercentUsed.Value;
            planPercentUsed = pct <= 1 ? pct * 100 : pct;
        }
        else if (planLimitRaw > 0)
        {
            // Fallback: calculate from used/total
            planPercentUsed = (planUsedRaw / planLimitRaw) * 100;
        }
        else
        {
            planPercentUsed = 0;
        }

        double onDemandUsed = (summary.IndividualUsage?.OnDemand?.Used ?? 0) / 100.0;
        double? onDemandLimit = summary.IndividualUsage?.OnDemand?.Limit.HasValue == true
            ? summary.IndividualUsage.OnDemand.Limit.Value / 100.0
            : null;

        double? teamOnDemandUsed = summary.TeamUsage?.OnDemand?.Used.HasValue == true
            ? summary.TeamUsage.OnDemand.Used.Value / 100.0
            : null;
        double? teamOnDemandLimit = summary.TeamUsage?.OnDemand?.Limit.HasValue == true
            ? summary.TeamUsage.OnDemand.Limit.Value / 100.0
            : null;

        // Legacy request-based plan
        int? requestsUsed = requestUsage?.Gpt4?.NumRequestsTotal ?? requestUsage?.Gpt4?.NumRequests;
        int? requestsLimit = requestUsage?.Gpt4?.MaxRequestUsage;

        // Extract Auto and API percentages directly from API
        double? autoPercentUsed = summary.IndividualUsage?.Plan?.AutoPercentUsed;
        double? apiPercentUsed = summary.IndividualUsage?.Plan?.ApiPercentUsed;

        return new CursorStatusSnapshot
        {
            PlanPercentUsed = planPercentUsed,
            AutoPercentUsed = autoPercentUsed,
            ApiPercentUsed = apiPercentUsed,
            PlanUsedUSD = planUsed,
            PlanLimitUSD = planLimit,
            OnDemandUsedUSD = onDemandUsed,
            OnDemandLimitUSD = onDemandLimit,
            TeamOnDemandUsedUSD = teamOnDemandUsed,
            TeamOnDemandLimitUSD = teamOnDemandLimit,
            BillingCycleEnd = billingCycleEnd,
            MembershipType = summary.MembershipType,
            AccountEmail = userInfo?.Email,
            AccountName = userInfo?.Name,
            RawJSON = rawJson,
            RequestsUsed = requestsUsed,
            RequestsLimit = requestsLimit
        };
    }
}

#endregion
