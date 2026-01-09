using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuoteBar.Core.CostUsage;
using QuoteBar.Core.Models;
using QuoteBar.Core.Services;

namespace QuoteBar.Core.Providers.Copilot;

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
    
    /// <summary>
    /// Estimated cost in USD based on model usage
    /// </summary>
    public double EstimatedCostUSD { get; set; }
}

/// <summary>
/// Response from GitHub's internal Copilot API (copilot_internal/user)
/// This API returns quota snapshots directly with percent_remaining
/// </summary>
public sealed class CopilotInternalUserResponse
{
    [JsonPropertyName("quota_snapshots")]
    public CopilotQuotaSnapshots? QuotaSnapshots { get; set; }

    [JsonPropertyName("copilot_plan")]
    public string? CopilotPlan { get; set; }

    [JsonPropertyName("assigned_date")]
    public string? AssignedDate { get; set; }

    [JsonPropertyName("quota_reset_date")]
    public string? QuotaResetDate { get; set; }
}

public sealed class CopilotQuotaSnapshots
{
    [JsonPropertyName("premium_interactions")]
    public CopilotQuotaSnapshot? PremiumInteractions { get; set; }

    [JsonPropertyName("chat")]
    public CopilotQuotaSnapshot? Chat { get; set; }
}

public sealed class CopilotQuotaSnapshot
{
    [JsonPropertyName("entitlement")]
    public double Entitlement { get; set; }

    [JsonPropertyName("remaining")]
    public double Remaining { get; set; }

    [JsonPropertyName("percent_remaining")]
    public double PercentRemaining { get; set; }

    [JsonPropertyName("quota_id")]
    public string? QuotaId { get; set; }
}

/// <summary>
/// Fetches usage data from the GitHub Copilot API using both the internal API and billing endpoints
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
    /// Fetch usage data using both APIs:
    /// - Internal API for accurate quota percentages
    /// - Billing API for model breakdown
    /// Falls back to billing API only if internal API fails.
    /// </summary>
    public static async Task<UsageSnapshot> FetchUsageSnapshotAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        UsageSnapshot? internalResult = null;
        CopilotUsageData? billingData = null;

        // Try internal API first (more reliable for quota percentages)
        try
        {
            internalResult = await FetchFromInternalApiAsync(accessToken, cancellationToken);
            if (internalResult != null)
            {
                Log("Successfully fetched from internal API");
            }
        }
        catch (Exception ex)
        {
            Log($"Internal API failed: {ex.Message}");
        }

        // Always try billing API to get model breakdown
        try
        {
            billingData = await FetchUsageAsync(accessToken, cancellationToken);
            Log($"Billing API: TopModel={billingData.TopModel}, Models={billingData.ModelUsage.Count}");
        }
        catch (Exception ex)
        {
            Log($"Billing API failed: {ex.Message}");
        }

        // If we have internal result, merge in model data from billing
        if (internalResult != null && billingData != null && billingData.ModelUsage.Count > 0)
        {
            Log($"Merging internal API with billing data ({billingData.ModelUsage.Count} models)");
            return MergeInternalWithBillingData(internalResult, billingData);
        }

        // If only internal result, clear the chat-based secondary that's not useful without model data
        if (internalResult != null)
        {
            Log("Using internal API result only (no model breakdown available)");
            // Clear secondary if it's the "chat" quota - it's not useful without model breakdown
            // and shows confusing "0 / 0 chat" in the UI
            if (internalResult.Secondary?.Unit == "chat")
            {
                return new UsageSnapshot
                {
                    ProviderId = internalResult.ProviderId,
                    Primary = internalResult.Primary,
                    Secondary = null, // Clear the confusing chat secondary
                    Tertiary = null,
                    Identity = internalResult.Identity,
                    FetchedAt = internalResult.FetchedAt
                };
            }
            return internalResult;
        }

        // Fall back to billing API only
        if (billingData != null)
        {
            Log("Using billing API result only");
            return ToUsageSnapshot(billingData, null);
        }

        // Both failed
        Log("Both APIs failed - returning error");
        return new UsageSnapshot
        {
            ProviderId = "copilot",
            ErrorMessage = "Failed to fetch Copilot usage data",
            FetchedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Merge internal API result with billing API model data
    /// </summary>
    private static UsageSnapshot MergeInternalWithBillingData(UsageSnapshot internalResult, CopilotUsageData billingData)
    {
        RateWindow? primary = internalResult.Primary;
        RateWindow? secondary = null;
        RateWindow? tertiary = null;

        // Get total usage from billing API (has decimals)
        double totalUsed = billingData.TotalPremiumRequestsUsed;

        // Update Primary with decimal values from billing API if available
        if (totalUsed > 0 && primary != null)
        {
            var limit = primary.Limit ?? billingData.IncludedRequestsLimit;
            var usedPercent = limit > 0 ? (totalUsed / limit.Value) * 100 : primary.UsedPercent;

            primary = new RateWindow
            {
                UsedPercent = usedPercent,
                Used = totalUsed,  // Use decimal value from billing API
                Limit = limit,
                WindowMinutes = primary.WindowMinutes,
                ResetsAt = primary.ResetsAt,
                ResetDescription = primary.ResetDescription,
                Unit = primary.Unit
            };
        }

        // Secondary: Top model
        if (!string.IsNullOrEmpty(billingData.TopModel) && billingData.TopModelUsage > 0)
        {
            double topModelPercent = totalUsed > 0 ? (billingData.TopModelUsage / totalUsed) * 100 : 0;

            secondary = new RateWindow
            {
                UsedPercent = topModelPercent,
                Used = billingData.TopModelUsage,
                Limit = null,
                Unit = FormatModelName(billingData.TopModel)
            };
        }

        // Tertiary: Second model or billed amount
        if (billingData.TotalBilledAmount > 0)
        {
            tertiary = new RateWindow
            {
                UsedPercent = 0,
                Used = billingData.TotalBilledAmount,
                Limit = null,
                Unit = "USD billed"
            };
        }
        else if (billingData.ModelUsage.Count >= 2)
        {
            var secondModel = billingData.ModelUsage.OrderByDescending(m => m.Value).Skip(1).First();
            double secondPercent = totalUsed > 0 ? (secondModel.Value / totalUsed) * 100 : 0;

            tertiary = new RateWindow
            {
                UsedPercent = secondPercent,
                Used = secondModel.Value,
                Limit = null,
                Unit = FormatModelName(secondModel.Key)
            };
        }

        // Build cost info from billing data
        ProviderCost? cost = null;
        var estimatedCost = billingData.EstimatedCostUSD;
        var billedCost = billingData.TotalBilledAmount;
        
        if (estimatedCost > 0 || billedCost > 0 || totalUsed > 0)
        {
            cost = new ProviderCost
            {
                SessionCostUSD = null,
                SessionTokens = null,
                TotalCostUSD = estimatedCost > 0 ? estimatedCost : billedCost,
                TotalTokens = (int)totalUsed,
                StartDate = DateTime.UtcNow.Date.AddDays(-DateTime.UtcNow.Day + 1),
                EndDate = DateTime.UtcNow,
                CostBreakdown = new Dictionary<string, double>
                {
                    ["Estimated"] = estimatedCost,
                    ["Billed (overage)"] = billedCost
                }
            };
        }

        return new UsageSnapshot
        {
            ProviderId = internalResult.ProviderId,
            Primary = primary,
            Secondary = secondary ?? internalResult.Secondary,
            Tertiary = tertiary ?? internalResult.Tertiary,
            Cost = cost,
            Identity = internalResult.Identity,
            FetchedAt = internalResult.FetchedAt
        };
    }

    /// <summary>
    /// Fetch from the internal Copilot API (copilot_internal/user)
    /// This is the same API that VS Code and CodexBar use.
    /// </summary>
    private static async Task<UsageSnapshot?> FetchFromInternalApiAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
        
        // Use the same headers that VS Code uses
        client.DefaultRequestHeaders.Add("Authorization", $"token {accessToken}");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.Add("Editor-Version", "vscode/1.96.2");
        client.DefaultRequestHeaders.Add("Editor-Plugin-Version", "copilot-chat/0.26.7");
        client.DefaultRequestHeaders.Add("User-Agent", "GitHubCopilotChat/0.26.7");
        client.DefaultRequestHeaders.Add("X-Github-Api-Version", "2025-04-01");

        var url = "https://api.github.com/copilot_internal/user";
        Log($"Fetching from internal API: {url}");

        var response = await client.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        Log($"Internal API response: Status={response.StatusCode}");

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
            response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            Log($"Internal API auth failed: {content}");
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            Log($"Internal API error: {content}");
            return null;
        }

        Log($"Internal API body: {content.Substring(0, Math.Min(500, content.Length))}");

        var internalData = JsonSerializer.Deserialize<CopilotInternalUserResponse>(content, JsonOptions);
        if (internalData == null)
        {
            Log("Failed to parse internal API response");
            return null;
        }

        return ConvertInternalResponseToSnapshot(internalData);
    }

    /// <summary>
    /// Convert internal API response to UsageSnapshot
    /// </summary>
    private static UsageSnapshot ConvertInternalResponseToSnapshot(CopilotInternalUserResponse data)
    {
        RateWindow? primary = null;

        // Primary: Premium Interactions
        var premium = data.QuotaSnapshots?.PremiumInteractions;
        if (premium != null)
        {
            // percent_remaining is 0-100, we need used percent
            var usedPercent = Math.Max(0, 100 - premium.PercentRemaining);
            var used = premium.Entitlement - premium.Remaining;

            primary = new RateWindow
            {
                UsedPercent = usedPercent,
                Used = used,
                Limit = (int)premium.Entitlement,
                WindowMinutes = null,
                ResetsAt = ParseResetDate(data.QuotaResetDate),
                ResetDescription = FormatResetTime(ParseResetDate(data.QuotaResetDate)),
                Unit = "premium"
            };
        }

        // Note: Secondary/Tertiary will be populated by billing API if available (model breakdown)
        // The internal API doesn't provide useful secondary data, so we leave them null

        // Determine plan type based on entitlement (quota limit) - this is the most reliable indicator
        // Pro+ = 1500, Pro = 300, Free = 50
        var entitlement = premium?.Entitlement ?? 0;
        var planLabel = entitlement switch
        {
            >= 1000 => "Copilot Pro+",      // 1500 requests = Pro+
            >= 200 => "Copilot Pro",         // 300 requests = Pro
            >= 30 => "Copilot Free",         // 50 requests = Free
            _ => DetectPlanFromString(data.CopilotPlan) // Fallback to string parsing
        };

        var identity = new ProviderIdentity
        {
            PlanType = planLabel
        };

        return new UsageSnapshot
        {
            ProviderId = "copilot",
            Primary = primary ?? new RateWindow { UsedPercent = 0, Used = 0, Limit = 0 },
            Secondary = null, // Will be filled by billing API if available
            Tertiary = null,
            Identity = identity,
            FetchedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Fallback plan detection from copilot_plan string when entitlement is not available
    /// </summary>
    private static string DetectPlanFromString(string? copilotPlan)
    {
        return copilotPlan?.ToLowerInvariant() switch
        {
            "individual_plus" or "pro_plus" or "individual_pro_plus" or "pro+" => "Copilot Pro+",
            "individual" or "pro" or "individual_pro" => "Copilot Pro",
            "free" or "individual_free" => "Copilot Free",
            "business" => "Copilot Business",
            "enterprise" => "Copilot Enterprise",
            _ => "Copilot" // Unknown plan
        };
    }

    private static DateTime? ParseResetDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr))
            return null;

        if (DateTime.TryParse(dateStr, out var date))
            return date;

        return null;
    }

    /// <summary>
    /// Fetch complete usage data including premium requests from billing API
    /// </summary>
    public static async Task<CopilotUsageData> FetchUsageAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        // Create HttpClient with BaseAddress for relative URLs used by FetchUserInfoAsync, etc.
        using var client = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.Add("User-Agent", "QuoteBar");
        
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
            Log($"Processing {usageData.PremiumRequestUsage.UsageItems.Count} usage items from billing API");
            
            foreach (var item in usageData.PremiumRequestUsage.UsageItems)
            {
                Log($"  Item: Product={item.Product}, Model={item.Model}, Quantity={item.GrossQuantity}, SKU={item.Sku}");
                
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
                        Log($"    -> Added model: {modelName} = {quantity}");
                    }
                    else if (quantity > 0)
                    {
                        Log($"    -> Item has no model name, quantity={quantity}");
                    }
                }
            }
            
            Log($"Total models tracked: {usageData.ModelUsage.Count}");
            
            // Find top model and calculate estimated cost
            if (usageData.ModelUsage.Count > 0)
            {
                var topModel = usageData.ModelUsage.OrderByDescending(m => m.Value).First();
                usageData.TopModel = topModel.Key;
                usageData.TopModelUsage = topModel.Value;
                Log($"Top model selected: {usageData.TopModel} with {usageData.TopModelUsage} requests");
                
                // Calculate estimated cost based on model-specific pricing
                double estimatedCost = 0;
                foreach (var (model, requests) in usageData.ModelUsage)
                {
                    var modelCost = CostUsage.CostUsagePricing.CopilotCostUSD(model, requests) ?? 0;
                    estimatedCost += modelCost;
                    Log($"    -> Model cost: {model} = {requests} requests * pricing = ${modelCost:F2}");
                }
                usageData.EstimatedCostUSD = estimatedCost;
                Log($"Total estimated cost: ${estimatedCost:F2}");
            }
            else
            {
                Log("No models found in billing data (Model field is empty in all items)");
            }
        }
        else
        {
            Log("No usage items available from billing API");
        }

        // Detect plan type and set limits
        DetectPlanTypeAndLimits(usageData);

        // Calculate reset date (first of next month)
        usageData.ResetsAt = CalculateNextResetDate();

        Log($"Usage data: Used={usageData.TotalPremiumRequestsUsed}, Limit={usageData.IncludedRequestsLimit}, Plan={usageData.DetectedPlanType}, Resets={usageData.ResetsAt}");

        // Save to cost tracking cache for Cost Tracking page
        if (usageData.ModelUsage.Count > 0)
        {
            try
            {
                CostUsage.CostUsageFetcher.Instance.SaveCopilotUsageData(DateTime.UtcNow, usageData.ModelUsage);
            }
            catch (Exception ex)
            {
                Log($"Failed to save cost tracking data: {ex.Message}");
            }
        }

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
        // First, check if user has manually configured their plan
        var settings = QuoteBar.Core.Services.SettingsService.Instance.Settings;
        var configuredPlan = settings.CopilotPlanType?.ToLowerInvariant() ?? "auto";
        
        if (configuredPlan != "auto")
        {
            // User has explicitly set their plan type
            switch (configuredPlan)
            {
                case "pro_plus":
                    usageData.DetectedPlanType = "Copilot Pro+";
                    usageData.IncludedRequestsLimit = 1500;
                    Log($"Using configured plan: Pro+ (1500 limit)");
                    return;
                case "pro":
                    usageData.DetectedPlanType = "Copilot Pro";
                    usageData.IncludedRequestsLimit = 300;
                    Log($"Using configured plan: Pro (300 limit)");
                    return;
                case "free":
                    usageData.DetectedPlanType = "Copilot Free";
                    usageData.IncludedRequestsLimit = 50;
                    Log($"Using configured plan: Free (50 limit)");
                    return;
            }
        }
        
        // Auto-detection based on usage patterns
        double used = usageData.TotalPremiumRequestsUsed;
        double billed = usageData.TotalBilledAmount;
        bool hasBilledRequests = billed > 0;
        
        // Check for Pro+ indicators in usage items
        bool hasProPlusIndicators = false;
        if (usageData.PremiumRequestUsage?.UsageItems != null)
        {
            var copilotItems = usageData.PremiumRequestUsage.UsageItems
                .Where(i => i.Product?.Equals("Copilot", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            // Pro+ indicators:
            // 1. Use of premium models (Claude Opus, o1, o3, GPT-5)
            // 2. Higher price per unit items (Pro+ has access to more expensive models)
            // 3. Usage of multiple premium model types
            hasProPlusIndicators = copilotItems.Any(i => 
                i.Model?.Contains("Opus", StringComparison.OrdinalIgnoreCase) == true ||
                i.Model?.Contains("GPT-5", StringComparison.OrdinalIgnoreCase) == true ||
                i.Model?.Contains("o1", StringComparison.OrdinalIgnoreCase) == true ||
                i.Model?.Contains("o3", StringComparison.OrdinalIgnoreCase) == true ||
                i.Model?.Contains("Gemini 2.5", StringComparison.OrdinalIgnoreCase) == true ||
                (i.PricePerUnit ?? 0) > 0.01); // Premium models have higher price per request
                
            // Also check if there are many different model types (Pro+ users tend to use variety)
            var distinctModels = copilotItems
                .Where(i => !string.IsNullOrEmpty(i.Model))
                .Select(i => i.Model)
                .Distinct()
                .Count();
            
            if (distinctModels >= 3)
            {
                hasProPlusIndicators = true;
            }
            
            Log($"Pro+ detection: hasIndicators={hasProPlusIndicators}, distinctModels={distinctModels}, hasBilled={hasBilledRequests}");
        }
        
        // Determine plan based on indicators
        if (hasProPlusIndicators)
        {
            usageData.DetectedPlanType = "Copilot Pro+";
            usageData.IncludedRequestsLimit = 1500;
        }
        else if (used > 300 && !hasBilledRequests)
        {
            // Used more than 300 without being billed = definitely Pro+
            usageData.DetectedPlanType = "Copilot Pro+";
            usageData.IncludedRequestsLimit = 1500;
        }
        else if (used > 50 && !hasBilledRequests)
        {
            // Used more than 50 without being billed = at least Pro
            // But if we don't have Pro+ indicators, assume Pro
            usageData.DetectedPlanType = "Copilot Pro";
            usageData.IncludedRequestsLimit = 300;
        }
        else if (used <= 50)
        {
            // Low usage - check GitHub plan for hints
            var githubPlan = usageData.User?.Plan?.Name?.ToLowerInvariant();
            
            if (githubPlan == "pro" || githubPlan == "team")
            {
                // GitHub Pro users likely have Copilot Pro
                usageData.DetectedPlanType = "Copilot Pro";
                usageData.IncludedRequestsLimit = 300;
            }
            else
            {
                // Default to Pro for paying users (most common scenario)
                // Users can override in settings if they have Pro+
                usageData.DetectedPlanType = "Copilot Pro";
                usageData.IncludedRequestsLimit = 300;
            }
        }
        else
        {
            // User is being billed for excess usage
            if (hasBilledRequests && used > 300)
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
        
        Log($"Auto-detected plan: {usageData.DetectedPlanType} (limit={usageData.IncludedRequestsLimit})");
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

        // Build cost info - show estimated cost based on model usage
        ProviderCost? cost = null;
        var estimatedCost = usageData.EstimatedCostUSD;
        var billedCost = usageData.TotalBilledAmount;
        
        if (estimatedCost > 0 || billedCost > 0 || usageData.TotalPremiumRequestsUsed > 0)
        {
            cost = new ProviderCost
            {
                SessionCostUSD = null, // No session granularity for Copilot
                SessionTokens = null,
                TotalCostUSD = estimatedCost > 0 ? estimatedCost : billedCost, // Show estimated cost, fallback to billed
                TotalTokens = (int)usageData.TotalPremiumRequestsUsed,
                StartDate = DateTime.UtcNow.Date.AddDays(-DateTime.UtcNow.Day + 1),
                EndDate = DateTime.UtcNow,
                CostBreakdown = new Dictionary<string, double>
                {
                    ["Estimated"] = estimatedCost,
                    ["Billed (overage)"] = billedCost
                }
            };
        }

        return new UsageSnapshot
        {
            ProviderId = "copilot",
            Primary = primary,
            Secondary = secondary,
            Tertiary = tertiary,
            Cost = cost,
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

    // Removed unused CreateHttpClient method - use SharedHttpClient.Default with per-request headers instead

    private static void Log(string message)
    {
        DebugLogger.Log("CopilotUsageFetcher", message);
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
