using System.Text.Json;
using System.Text.Json.Serialization;
using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Core.Providers.Augment;

/// <summary>
/// Augment provider descriptor for the Augment Code AI assistant
/// https://www.augmentcode.com/
/// </summary>
public sealed class AugmentProviderDescriptor : ProviderDescriptor
{
    public override string Id => "augment";
    public override string DisplayName => "Augment";
    public override string IconGlyph => "\uE950"; // Code icon
    public override string PrimaryColor => "#6366F1"; // Indigo
    public override string SecondaryColor => "#8B5CF6"; // Purple
    public override string PrimaryLabel => "Credits";
    public override string SecondaryLabel => "Usage";
    public override string? TertiaryLabel => null;
    public override UsageWindowType PrimaryWindowType => UsageWindowType.Monthly;
    public override UsageWindowType SecondaryWindowType => UsageWindowType.Monthly;
    public override string? DashboardUrl => "https://app.augmentcode.com/account/subscription";
    public override bool SupportsOAuth => false;
    public override bool SupportsWebScraping => true;
    public override bool SupportsCLI => false;

    protected override void InitializeStrategies()
    {
        AddStrategy(new AugmentCookieFetchStrategy());
    }
}

/// <summary>
/// Fetch strategy using browser cookies for Augment
/// </summary>
public sealed class AugmentCookieFetchStrategy : IProviderFetchStrategy
{
    public string StrategyName => "Cookie";
    public int Priority => 1;

    public Task<bool> CanExecuteAsync()
    {
        // Check if we have stored cookie credentials
        var (cookieHeader, _) = AugmentCredentialStore.GetCookieHeader();
        return Task.FromResult(!string.IsNullOrEmpty(cookieHeader));
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var (cookieHeader, error) = AugmentCredentialStore.GetCookieHeader();
        
        if (string.IsNullOrEmpty(cookieHeader))
        {
            return new UsageSnapshot
            {
                ProviderId = "augment",
                ErrorMessage = error ?? "No Augment session. Please log in at app.augmentcode.com.",
                FetchedAt = DateTime.UtcNow
            };
        }

        var fetcher = new AugmentUsageFetcher();
        return await fetcher.FetchAsync(cookieHeader, cancellationToken);
    }
}

/// <summary>
/// Credential store for Augment session cookies
/// Uses Windows Credential Manager for secure storage
/// </summary>
public static class AugmentCredentialStore
{
    /// <summary>
    /// Store cookie header securely
    /// </summary>
    public static void StoreCookieHeader(string cookieHeader)
    {
        SecureCredentialStore.StoreCredential(CredentialKeys.AugmentCookie, cookieHeader);
        DebugLogger.Log("AugmentCredentialStore", "Cookie header stored");
    }

    /// <summary>
    /// Get stored cookie header
    /// </summary>
    public static (string? CookieHeader, string? Error) GetCookieHeader()
    {
        try
        {
            var cookie = SecureCredentialStore.GetCredential(CredentialKeys.AugmentCookie);
            if (string.IsNullOrEmpty(cookie))
            {
                return (null, "No Augment session found. Log in at app.augmentcode.com and configure cookie in Settings.");
            }
            return (cookie, null);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("AugmentCredentialStore", "Failed to get cookie", ex);
            return (null, "Failed to retrieve Augment credentials");
        }
    }

    /// <summary>
    /// Clear stored credentials
    /// </summary>
    public static void ClearCredentials()
    {
        try
        {
            SecureCredentialStore.DeleteCredential(CredentialKeys.AugmentCookie);
            DebugLogger.Log("AugmentCredentialStore", "Credentials cleared");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("AugmentCredentialStore", "Failed to clear credentials", ex);
        }
    }

    /// <summary>
    /// Check if credentials are stored
    /// </summary>
    public static bool HasCredentials()
    {
        var (cookie, _) = GetCookieHeader();
        return !string.IsNullOrEmpty(cookie);
    }
}

/// <summary>
/// Fetcher for Augment API usage data
/// </summary>
public sealed class AugmentUsageFetcher
{
    private const string BaseUrl = "https://app.augmentcode.com";
    private const string CreditsPath = "/api/credits";
    private const string SubscriptionPath = "/api/subscription";
    private const int TimeoutSeconds = 15;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Fetch usage data from Augment APIs
    /// </summary>
    public async Task<UsageSnapshot> FetchAsync(string cookieHeader, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
        
        try
        {
            DebugLogger.Log("AugmentUsageFetcher", "Fetching credits and subscription...");

            // Fetch credits (required)
            var creditsResult = await FetchCreditsAsync(client, cookieHeader, cancellationToken);
            
            // Fetch subscription (optional - provides plan name and billing cycle)
            AugmentSubscriptionResponse? subscription = null;
            try
            {
                subscription = await FetchSubscriptionAsync(client, cookieHeader, cancellationToken);
            }
            catch (Exception ex)
            {
                DebugLogger.Log("AugmentUsageFetcher", $"Subscription fetch failed (optional): {ex.Message}");
            }

            return BuildSnapshot(creditsResult, subscription);
        }
        catch (AugmentFetchException ex)
        {
            DebugLogger.LogError("AugmentUsageFetcher", ex.Message, ex);
            return new UsageSnapshot
            {
                ProviderId = "augment",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("AugmentUsageFetcher", "Unexpected error", ex);
            return new UsageSnapshot
            {
                ProviderId = "augment",
                ErrorMessage = $"Error: {ex.Message}",
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private async Task<AugmentCreditsResponse> FetchCreditsAsync(
        HttpClient client, string cookieHeader, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{CreditsPath}");
        request.Headers.Add("Cookie", cookieHeader);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", "QuoteBar");

        var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        DebugLogger.Log("AugmentUsageFetcher", $"Credits API: Status={response.StatusCode}, Body={content[..Math.Min(200, content.Length)]}");

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // Clear invalid credentials
            AugmentCredentialStore.ClearCredentials();
            throw new AugmentFetchException("Session expired. Please log in again at app.augmentcode.com.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new AugmentFetchException($"API error: HTTP {(int)response.StatusCode}");
        }

        var credits = JsonSerializer.Deserialize<AugmentCreditsResponse>(content, JsonOptions);
        return credits ?? throw new AugmentFetchException("Invalid credits response");
    }

    private async Task<AugmentSubscriptionResponse> FetchSubscriptionAsync(
        HttpClient client, string cookieHeader, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{SubscriptionPath}");
        request.Headers.Add("Cookie", cookieHeader);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", "QuoteBar");

        var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        DebugLogger.Log("AugmentUsageFetcher", $"Subscription API: Status={response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            throw new AugmentFetchException($"Subscription API error: HTTP {(int)response.StatusCode}");
        }

        var subscription = JsonSerializer.Deserialize<AugmentSubscriptionResponse>(content, JsonOptions);
        return subscription ?? throw new AugmentFetchException("Invalid subscription response");
    }

    private static UsageSnapshot BuildSnapshot(AugmentCreditsResponse credits, AugmentSubscriptionResponse? subscription)
    {
        // Calculate usage percentage
        double usedPercent = 0;
        if (credits.CreditsUsed.HasValue && credits.CreditsLimit.HasValue && credits.CreditsLimit.Value > 0)
        {
            usedPercent = (credits.CreditsUsed.Value / credits.CreditsLimit.Value) * 100.0;
        }
        else if (credits.CreditsRemaining.HasValue && credits.CreditsLimit.HasValue && credits.CreditsLimit.Value > 0)
        {
            usedPercent = ((credits.CreditsLimit.Value - credits.CreditsRemaining.Value) / credits.CreditsLimit.Value) * 100.0;
        }

        // Parse billing cycle end
        DateTime? billingEnd = null;
        if (!string.IsNullOrEmpty(subscription?.BillingPeriodEnd))
        {
            if (DateTime.TryParse(subscription.BillingPeriodEnd, out var parsed))
            {
                billingEnd = parsed.ToUniversalTime();
            }
        }

        // Format reset description
        string? resetDescription = null;
        if (billingEnd.HasValue)
        {
            var remaining = billingEnd.Value - DateTime.UtcNow;
            if (remaining.TotalDays >= 1)
            {
                resetDescription = $"Resets in {(int)remaining.TotalDays}d";
            }
            else if (remaining.TotalHours >= 1)
            {
                resetDescription = $"Resets in {(int)remaining.TotalHours}h";
            }
            else
            {
                resetDescription = "Resets soon";
            }
        }

        // Build primary rate window (Credits)
        var primary = new RateWindow
        {
            UsedPercent = usedPercent,
            Used = credits.CreditsUsed,
            Limit = credits.CreditsLimit,
            Unit = "credits",
            ResetsAt = billingEnd,
            ResetDescription = resetDescription,
            Label = "Credits"
        };

        // Build identity
        var identity = new ProviderIdentity
        {
            Email = subscription?.Email,
            PlanType = subscription?.PlanName ?? "Augment"
        };

        DebugLogger.Log("AugmentUsageFetcher", 
            $"Built snapshot: {usedPercent:F1}% used, Plan={subscription?.PlanName}");

        return new UsageSnapshot
        {
            ProviderId = "augment",
            Primary = primary,
            Secondary = null,
            Tertiary = null,
            Cost = null, // Augment doesn't expose cost data currently
            Identity = identity,
            FetchedAt = DateTime.UtcNow
        };
    }
}

#region API Response Models

/// <summary>
/// Response from Augment /api/credits endpoint
/// </summary>
public sealed class AugmentCreditsResponse
{
    [JsonPropertyName("usageUnitsRemaining")]
    public double? UsageUnitsRemaining { get; set; }

    [JsonPropertyName("usageUnitsConsumedThisBillingCycle")]
    public double? UsageUnitsConsumedThisBillingCycle { get; set; }

    [JsonPropertyName("usageUnitsAvailable")]
    public double? UsageUnitsAvailable { get; set; }

    [JsonPropertyName("usageBalanceStatus")]
    public string? UsageBalanceStatus { get; set; }

    // Computed properties for compatibility
    public double? CreditsRemaining => UsageUnitsRemaining;
    public double? CreditsUsed => UsageUnitsConsumedThisBillingCycle;
    public double? CreditsLimit => 
        UsageUnitsRemaining.HasValue && UsageUnitsConsumedThisBillingCycle.HasValue
            ? UsageUnitsRemaining.Value + UsageUnitsConsumedThisBillingCycle.Value
            : UsageUnitsAvailable;
}

/// <summary>
/// Response from Augment /api/subscription endpoint
/// </summary>
public sealed class AugmentSubscriptionResponse
{
    [JsonPropertyName("planName")]
    public string? PlanName { get; set; }

    [JsonPropertyName("billingPeriodEnd")]
    public string? BillingPeriodEnd { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("organization")]
    public string? Organization { get; set; }
}

#endregion

#region Exceptions

public sealed class AugmentFetchException : Exception
{
    public AugmentFetchException(string message) : base(message) { }
    public AugmentFetchException(string message, Exception inner) : base(message, inner) { }
}

#endregion
