using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using NativeBar.WinUI.Core.Models;

namespace NativeBar.WinUI.Core.Providers.Copilot;

/// <summary>
/// Response from GitHub API /user endpoint for identity info
/// </summary>
public sealed class GitHubUserResponse
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("plan")]
    public GitHubPlanInfo? Plan { get; set; }
}

public sealed class GitHubPlanInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("space")]
    public long? Space { get; set; }

    [JsonPropertyName("private_repos")]
    public int? PrivateRepos { get; set; }
}

/// <summary>
/// Response from GitHub Billing API /users/{username}/settings/billing/premium_request/usage
/// This is the actual premium request usage for Copilot
/// </summary>
public sealed class CopilotPremiumRequestUsageResponse
{
    [JsonPropertyName("timePeriod")]
    public BillingTimePeriod? TimePeriod { get; set; }

    [JsonPropertyName("user")]
    public string? User { get; set; }

    [JsonPropertyName("usageItems")]
    public List<PremiumRequestUsageItem>? UsageItems { get; set; }
}

public sealed class BillingTimePeriod
{
    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("month")]
    public int? Month { get; set; }
}

public sealed class PremiumRequestUsageItem
{
    [JsonPropertyName("product")]
    public string? Product { get; set; }

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("unitType")]
    public string? UnitType { get; set; }

    [JsonPropertyName("pricePerUnit")]
    public double? PricePerUnit { get; set; }

    [JsonPropertyName("grossQuantity")]
    public double? GrossQuantity { get; set; }

    [JsonPropertyName("grossAmount")]
    public double? GrossAmount { get; set; }

    [JsonPropertyName("discountQuantity")]
    public double? DiscountQuantity { get; set; }

    [JsonPropertyName("discountAmount")]
    public double? DiscountAmount { get; set; }

    [JsonPropertyName("netQuantity")]
    public double? NetQuantity { get; set; }

    [JsonPropertyName("netAmount")]
    public double? NetAmount { get; set; }
}

/// <summary>
/// Response from GitHub Billing API /users/{username}/settings/billing/usage/summary
/// </summary>
public sealed class CopilotUsageSummaryResponse
{
    [JsonPropertyName("timePeriod")]
    public BillingTimePeriod? TimePeriod { get; set; }

    [JsonPropertyName("user")]
    public string? User { get; set; }

    [JsonPropertyName("usageItems")]
    public List<UsageSummaryItem>? UsageItems { get; set; }
}

public sealed class UsageSummaryItem
{
    [JsonPropertyName("product")]
    public string? Product { get; set; }

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("unitType")]
    public string? UnitType { get; set; }

    [JsonPropertyName("pricePerUnit")]
    public double? PricePerUnit { get; set; }

    [JsonPropertyName("grossQuantity")]
    public double? GrossQuantity { get; set; }

    [JsonPropertyName("grossAmount")]
    public double? GrossAmount { get; set; }

    [JsonPropertyName("discountQuantity")]
    public double? DiscountQuantity { get; set; }

    [JsonPropertyName("discountAmount")]
    public double? DiscountAmount { get; set; }

    [JsonPropertyName("netQuantity")]
    public double? NetQuantity { get; set; }

    [JsonPropertyName("netAmount")]
    public double? NetAmount { get; set; }
}

/// <summary>
/// Combined Copilot usage data from various API endpoints
/// </summary>
public sealed class CopilotUsageData
{
    public GitHubUserResponse? User { get; set; }
    public CopilotPremiumRequestUsageResponse? PremiumRequestUsage { get; set; }
    public CopilotUsageSummaryResponse? UsageSummary { get; set; }
    public double TotalPremiumRequestsUsed { get; set; }
    public double TotalBilledAmount { get; set; }
    public double TotalIncludedAmount { get; set; }
    public string? DetectedPlanType { get; set; }
    public int? IncludedRequestsLimit { get; set; }
    public DateTime? ResetsAt { get; set; }
    
    /// <summary>
    /// Model usage breakdown - model name -> requests used
    /// </summary>
    public Dictionary<string, double> ModelUsage { get; set; } = new();
    
    /// <summary>
    /// The model with the highest usage
    /// </summary>
    public string? TopModel { get; set; }
    
    /// <summary>
    /// Usage amount for the top model
    /// </summary>
    public double TopModelUsage { get; set; }
}

/// <summary>
/// Fetches usage data from the GitHub Copilot API using the billing endpoints
/// </summary>
public static class CopilotUsageFetcher
{
    private const string BaseUrl = "https://api.github.com";
    private const int TimeoutSeconds = 30;

    // Copilot plan limits based on GitHub documentation
    private static readonly Dictionary<string, int> PlanLimits = new()
    {
        { "free", 50 },           // Copilot Free
        { "pro", 300 },           // Copilot Pro ($10/month)
        { "pro_plus", 1500 },     // Copilot Pro+ ($39/month) - soft limit
        { "individual", 300 },    // Legacy individual plan
        { "business", 500 },      // Copilot Business (varies)
        { "enterprise", 1000 }    // Copilot Enterprise (varies)
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Fetch complete usage data including premium requests from billing API
    /// </summary>
    public static async Task<CopilotUsageData> FetchUsageAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var client = CreateHttpClient(accessToken);
        var usageData = new CopilotUsageData();

        // First, get user info to know the username
        var userResponse = await FetchUserInfoAsync(client, cancellationToken);
        usageData.User = userResponse;

        if (userResponse?.Login == null)
        {
            Log("Failed to get user login, cannot fetch billing data");
            return usageData;
        }

        var username = userResponse.Login;

        // Fetch premium request usage and summary in parallel
        var premiumTask = FetchPremiumRequestUsageAsync(client, username, cancellationToken);
        var summaryTask = FetchUsageSummaryAsync(client, username, cancellationToken);

        try
        {
            await Task.WhenAll(premiumTask, summaryTask);
        }
        catch (Exception ex)
        {
            Log($"Error fetching billing data: {ex.Message}");
        }

        usageData.PremiumRequestUsage = premiumTask.IsCompletedSuccessfully ? premiumTask.Result : null;
        usageData.UsageSummary = summaryTask.IsCompletedSuccessfully ? summaryTask.Result : null;

        // Calculate totals from premium request usage and build model breakdown
        if (usageData.PremiumRequestUsage?.UsageItems != null)
        {
            foreach (var item in usageData.PremiumRequestUsage.UsageItems)
            {
                if (item.Product?.Equals("Copilot", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var quantity = item.GrossQuantity ?? 0;
                    usageData.TotalPremiumRequestsUsed += quantity;
                    usageData.TotalBilledAmount += item.NetAmount ?? 0;
                    // Included = gross - net (if net is 0, all are included)
                    usageData.TotalIncludedAmount += (item.GrossAmount ?? 0) - (item.NetAmount ?? 0);
                    
                    // Track model usage
                    if (!string.IsNullOrEmpty(item.Model) && quantity > 0)
                    {
                        var modelName = item.Model;
                        if (usageData.ModelUsage.ContainsKey(modelName))
                        {
                            usageData.ModelUsage[modelName] += quantity;
                        }
                        else
                        {
                            usageData.ModelUsage[modelName] = quantity;
                        }
                    }
                }
            }
            
            // Find top model
            if (usageData.ModelUsage.Count > 0)
            {
                var topModel = usageData.ModelUsage.OrderByDescending(m => m.Value).First();
                usageData.TopModel = topModel.Key;
                usageData.TopModelUsage = topModel.Value;
            }
        }

        // Detect plan type and set limits
        DetectPlanTypeAndLimits(usageData);

        // Calculate reset date (first of next month)
        usageData.ResetsAt = CalculateNextResetDate();

        Log($"Usage data: Used={usageData.TotalPremiumRequestsUsed}, Limit={usageData.IncludedRequestsLimit}, Plan={usageData.DetectedPlanType}, Resets={usageData.ResetsAt}");

        return usageData;
    }

    private static async Task<GitHubUserResponse?> FetchUserInfoAsync(HttpClient client, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetAsync("/user", cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            Log($"GET /user: Status={response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<GitHubUserResponse>(content, JsonOptions);
            }

            return null;
        }
        catch (Exception ex)
        {
            Log($"FetchUserInfo error: {ex.Message}");
            return null;
        }
    }

    private static async Task<CopilotPremiumRequestUsageResponse?> FetchPremiumRequestUsageAsync(
        HttpClient client, string username, CancellationToken cancellationToken)
    {
        try
        {
            // GET /users/{username}/settings/billing/premium_request/usage
            var url = $"/users/{username}/settings/billing/premium_request/usage";
            Log($"Fetching: {url}");

            var response = await client.GetAsync(url, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            Log($"GET premium_request/usage: Status={response.StatusCode}, Body={content.Substring(0, Math.Min(500, content.Length))}");

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<CopilotPremiumRequestUsageResponse>(content, JsonOptions);
            }

            // Log error response for debugging
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                Log("403 Forbidden - Token may not have 'Plan' user permission (read)");
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Log("404 Not Found - User may not have Copilot subscription");
            }

            return null;
        }
        catch (Exception ex)
        {
            Log($"FetchPremiumRequestUsage error: {ex.Message}");
            return null;
        }
    }

    private static async Task<CopilotUsageSummaryResponse?> FetchUsageSummaryAsync(
        HttpClient client, string username, CancellationToken cancellationToken)
    {
        try
        {
            // GET /users/{username}/settings/billing/usage/summary
            var url = $"/users/{username}/settings/billing/usage/summary";
            Log($"Fetching: {url}");

            var response = await client.GetAsync(url, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            Log($"GET usage/summary: Status={response.StatusCode}, Body={content.Substring(0, Math.Min(500, content.Length))}");

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<CopilotUsageSummaryResponse>(content, JsonOptions);
            }

            return null;
        }
        catch (Exception ex)
        {
            Log($"FetchUsageSummary error: {ex.Message}");
            return null;
        }
    }

    private static void DetectPlanTypeAndLimits(CopilotUsageData usageData)
    {
        // Detect plan based on usage patterns and limits
        // If user has >300 included requests without being billed, they're on Pro+ (1500)
        // If user has >50 included requests without being billed, they're on Pro (300)
        // Otherwise they're on Free (50)

        double used = usageData.TotalPremiumRequestsUsed;
        double billed = usageData.TotalBilledAmount;

        // Check if any items have netAmount > 0 (meaning they went over included limit)
        bool hasBilledRequests = billed > 0;

        // Determine plan based on usage
        if (used > 300 && !hasBilledRequests)
        {
            // Used more than 300 without being billed = Pro+ (1500 limit)
            usageData.DetectedPlanType = "Copilot Pro+";
            usageData.IncludedRequestsLimit = 1500;
        }
        else if (used > 50 && !hasBilledRequests)
        {
            // Used more than 50 without being billed = Pro (300 limit)
            usageData.DetectedPlanType = "Copilot Pro";
            usageData.IncludedRequestsLimit = 300;
        }
        else if (used <= 50)
        {
            // Could be any plan, but if usage is low, assume based on GitHub plan
            var githubPlan = usageData.User?.Plan?.Name?.ToLowerInvariant();
            
            if (githubPlan == "pro" || githubPlan == "team")
            {
                usageData.DetectedPlanType = "Copilot Pro";
                usageData.IncludedRequestsLimit = 300;
            }
            else
            {
                // Default to Pro if we can't determine (most likely scenario for active users)
                usageData.DetectedPlanType = "Copilot Pro";
                usageData.IncludedRequestsLimit = 300;
            }
        }
        else
        {
            // User is being billed for excess usage
            // Try to determine original plan
            if (hasBilledRequests && used > 1500)
            {
                usageData.DetectedPlanType = "Copilot Pro+";
                usageData.IncludedRequestsLimit = 1500;
            }
            else if (hasBilledRequests && used > 300)
            {
                usageData.DetectedPlanType = "Copilot Pro+";
                usageData.IncludedRequestsLimit = 1500;
            }
            else
            {
                usageData.DetectedPlanType = "Copilot Pro";
                usageData.IncludedRequestsLimit = 300;
            }
        }

        // Check from usage items if there's copilot-specific info
        if (usageData.PremiumRequestUsage?.UsageItems != null)
        {
            var copilotItems = usageData.PremiumRequestUsage.UsageItems
                .Where(i => i.Product?.Equals("Copilot", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // If we have items with models like Claude Opus, GPT-5, etc., it's likely Pro+
            var hasAdvancedModels = copilotItems.Any(i => 
                i.Model?.Contains("Opus", StringComparison.OrdinalIgnoreCase) == true ||
                i.Model?.Contains("GPT-5", StringComparison.OrdinalIgnoreCase) == true ||
                i.Model?.Contains("o1", StringComparison.OrdinalIgnoreCase) == true);

            if (hasAdvancedModels && usageData.IncludedRequestsLimit < 1500)
            {
                usageData.DetectedPlanType = "Copilot Pro+";
                usageData.IncludedRequestsLimit = 1500;
            }
        }
    }

    private static DateTime CalculateNextResetDate()
    {
        var now = DateTime.UtcNow;
        // Premium requests reset on the 1st of each month
        var nextMonth = now.AddMonths(1);
        return new DateTime(nextMonth.Year, nextMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Convert API responses to UsageSnapshot
    /// </summary>
    public static UsageSnapshot ToUsageSnapshot(CopilotUsageData usageData, CopilotOAuthCredentials? credentials = null)
    {
        RateWindow? primary = null;
        RateWindow? secondary = null;
        RateWindow? tertiary = null;
        ProviderIdentity? identity = null;

        // Primary: Premium Requests (monthly)
        double used = usageData.TotalPremiumRequestsUsed;
        int limit = usageData.IncludedRequestsLimit ?? 1500;
        double usedPercent = limit > 0 ? (used / limit) * 100 : 0;

        // Cap at 100% for display purposes (can go over)
        if (usedPercent > 100)
        {
            usedPercent = 100;
        }

        primary = new RateWindow
        {
            UsedPercent = usedPercent,
            Used = used,
            Limit = limit,
            WindowMinutes = null, // Monthly, not hourly
            ResetsAt = usageData.ResetsAt,
            ResetDescription = FormatResetTime(usageData.ResetsAt),
            Unit = "included"
        };

        // Secondary: Show top model usage
        if (!string.IsNullOrEmpty(usageData.TopModel) && usageData.TopModelUsage > 0)
        {
            // Calculate percentage of top model usage relative to total
            double topModelPercent = used > 0 ? (usageData.TopModelUsage / used) * 100 : 0;
            
            secondary = new RateWindow
            {
                UsedPercent = topModelPercent,
                Used = usageData.TopModelUsage,
                Limit = null,
                Unit = FormatModelName(usageData.TopModel)
            };
        }

        // Tertiary: Show second top model if exists, or billed amount
        if (usageData.TotalBilledAmount > 0)
        {
            tertiary = new RateWindow
            {
                UsedPercent = 0,
                Used = usageData.TotalBilledAmount,
                Limit = null,
                Unit = "USD billed"
            };
        }
        else if (usageData.ModelUsage.Count >= 2)
        {
            // Show second most used model
            var secondModel = usageData.ModelUsage.OrderByDescending(m => m.Value).Skip(1).First();
            double secondPercent = used > 0 ? (secondModel.Value / used) * 100 : 0;
            
            tertiary = new RateWindow
            {
                UsedPercent = secondPercent,
                Used = secondModel.Value,
                Limit = null,
                Unit = FormatModelName(secondModel.Key)
            };
        }

        // Build identity with detected plan
        identity = new ProviderIdentity
        {
            Email = usageData.User?.Email,
            PlanType = usageData.DetectedPlanType ?? "Copilot",
            AccountId = usageData.User?.Login ?? credentials?.Username
        };

        return new UsageSnapshot
        {
            ProviderId = "copilot",
            Primary = primary,
            Secondary = secondary,
            Tertiary = tertiary,
            Identity = identity,
            FetchedAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Format model name for display (shorten long names)
    /// </summary>
    private static string FormatModelName(string modelName)
    {
        // Common model name mappings for cleaner display
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Claude Opus 4.5", "Opus 4.5" },
            { "Claude Sonnet 4.5", "Sonnet 4.5" },
            { "Claude Sonnet 3.5", "Sonnet 3.5" },
            { "Claude 3.5 Sonnet", "Sonnet 3.5" },
            { "Claude 3 Opus", "Opus 3" },
            { "Gemini 3 Flash", "Gemini Flash" },
            { "Gemini 2.0 Flash", "Gemini Flash" },
            { "GPT-4o", "GPT-4o" },
            { "GPT-4", "GPT-4" },
            { "GPT-5", "GPT-5" },
            { "o1-preview", "o1" },
            { "o1-mini", "o1-mini" },
            { "o1", "o1" },
            { "o3", "o3" },
            { "o3-mini", "o3-mini" },
            { "Gemini 3 Pro", "Gemini Pro" }
        };
        
        if (mappings.TryGetValue(modelName, out var shortName))
        {
            return shortName;
        }
        
        // If model name is too long, try to shorten it
        if (modelName.Length > 15)
        {
            // Remove common prefixes
            var shortened = modelName
                .Replace("Claude ", "")
                .Replace("Gemini ", "Gem ")
                .Replace(" Flash", " Fl")
                .Replace(" Preview", "")
                .Replace("-preview", "");
            return shortened.Length > 15 ? shortened.Substring(0, 15) : shortened;
        }
        
        return modelName;
    }

    private static string? FormatResetTime(DateTime? resetsAt)
    {
        if (!resetsAt.HasValue)
            return null;

        var diff = resetsAt.Value - DateTime.UtcNow;
        if (diff.TotalMinutes <= 0)
            return "now";

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
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.Add("User-Agent", "QuoteBar");

        return client;
    }

    private static void Log(string message)
    {
        try
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] CopilotUsageFetcher: {message}\n");
        }
        catch { }
    }
}

public enum CopilotFetchError
{
    None,
    Unauthorized,
    InvalidResponse,
    ServerError,
    NetworkError,
    NotConfigured
}

public class CopilotFetchException : Exception
{
    public CopilotFetchError ErrorType { get; }

    public CopilotFetchException(string message, CopilotFetchError errorType)
        : base(message)
    {
        ErrorType = errorType;
    }
}
