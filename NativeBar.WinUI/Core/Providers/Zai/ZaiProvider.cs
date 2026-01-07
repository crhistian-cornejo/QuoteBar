using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Services;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NativeBar.WinUI.Core.Providers.Zai;

public class ZaiProviderDescriptor : ProviderDescriptor
{
    public override string Id => "zai";
    public override string DisplayName => "z.ai";
    public override string IconGlyph => "\uE950"; // Star icon
    public override string PrimaryColor => "#E85A6A"; // Pink/red color from CodexBar
    public override string SecondaryColor => "#D44A5A";
    public override string PrimaryLabel => "Tokens";
    public override string SecondaryLabel => "MCP";
public override string? DashboardUrl => "https://z.ai/account";

    public override bool SupportsOAuth => false;
    public override bool SupportsCLI => false;

    protected override void InitializeStrategies()
    {
        AddStrategy(new ZaiAPIStrategy());
    }
}

/// <summary>
/// API Token strategy for z.ai
/// </summary>
public class ZaiAPIStrategy : IProviderFetchStrategy
{
    public string StrategyName => "API";
    public int Priority => 1;

    public Task<bool> CanExecuteAsync()
    {
        var token = ZaiSettingsReader.GetApiToken();
        var hasToken = !string.IsNullOrEmpty(token);
        Log($"CanExecute: hasToken={hasToken}");
        return Task.FromResult(hasToken);
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var token = ZaiSettingsReader.GetApiToken();
            if (string.IsNullOrEmpty(token))
            {
                return new UsageSnapshot
                {
                    ProviderId = "zai",
                    ErrorMessage = "z.ai API token not configured. Set it in Settings → Providers → z.ai.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            var usage = await ZaiUsageFetcher.FetchUsageAsync(token, cancellationToken);
            return usage;
        }
        catch (Exception ex)
        {
            Log($"Fetch ERROR: {ex.Message}\n{ex.StackTrace}");
            return new UsageSnapshot
            {
                ProviderId = "zai",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private void Log(string message)
    {
        Core.Services.DebugLogger.Log("ZaiAPIStrategy", message);
    }
}

/// <summary>
/// Reads and stores z.ai API token securely
/// Uses Windows Credential Manager (similar to macOS Keychain in CodexBar)
/// </summary>
public static class ZaiSettingsReader
{
    public const string EnvVarKey = "Z_AI_API_KEY";

    /// <summary>
    /// Get API token from secure storage or environment variable
    /// Priority: 1. Windows Credential Manager, 2. Environment variable
    /// </summary>
    public static string? GetApiToken()
    {
        // First check secure credential store (Windows Credential Manager)
        var token = SecureCredentialStore.GetCredential(CredentialKeys.ZaiApiToken);
        if (!string.IsNullOrWhiteSpace(token))
            return CleanToken(token);

        // Then check environment variable (for CLI/automation scenarios)
        token = Environment.GetEnvironmentVariable(EnvVarKey);
        if (!string.IsNullOrWhiteSpace(token))
            return CleanToken(token);

        return null;
    }

    /// <summary>
    /// Store API token securely in Windows Credential Manager
    /// </summary>
    public static bool StoreApiToken(string? token)
    {
        var cleaned = CleanToken(token);
        return SecureCredentialStore.StoreCredential(CredentialKeys.ZaiApiToken, cleaned);
    }

    /// <summary>
    /// Check if API token is configured
    /// </summary>
    public static bool HasApiToken()
    {
        return !string.IsNullOrWhiteSpace(GetApiToken());
    }

    /// <summary>
    /// Delete API token from secure storage
    /// </summary>
    public static bool DeleteApiToken()
    {
        return SecureCredentialStore.DeleteCredential(CredentialKeys.ZaiApiToken);
    }

    private static string? CleanToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();

        // Remove surrounding quotes if present
        if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
            (value.StartsWith("'") && value.EndsWith("'")))
        {
            value = value.Substring(1, value.Length - 2);
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

/// <summary>
/// Fetches usage data from z.ai API
/// </summary>
public static class ZaiUsageFetcher
{
    private const string QuotaApiUrl = "https://api.z.ai/api/monitor/usage/quota/limit";

    public static async Task<UsageSnapshot> FetchUsageAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.GetAsync(QuotaApiUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            Log($"API error: HTTP {(int)response.StatusCode}: {errorBody}");
            throw new ZaiUsageException($"z.ai API error: HTTP {(int)response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        Log($"API response: {json}");

        return ParseResponse(json);
    }

    private static UsageSnapshot ParseResponse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Check success
        if (!root.TryGetProperty("success", out var successProp) || !successProp.GetBoolean())
        {
            var msg = root.TryGetProperty("msg", out var msgProp) ? msgProp.GetString() : "Unknown error";
            throw new ZaiUsageException($"z.ai API failed: {msg}");
        }

        // Parse data
        if (!root.TryGetProperty("data", out var data))
        {
            throw new ZaiUsageException("z.ai API response missing 'data' field");
        }

        ZaiLimitEntry? tokenLimit = null;
        ZaiLimitEntry? timeLimit = null;
        string? planName = null;

        // Parse plan name (try multiple field names)
        if (data.TryGetProperty("planName", out var planProp))
            planName = planProp.GetString();
        else if (data.TryGetProperty("plan", out planProp))
            planName = planProp.GetString();
        else if (data.TryGetProperty("plan_type", out planProp))
            planName = planProp.GetString();
        else if (data.TryGetProperty("packageName", out planProp))
            planName = planProp.GetString();

        // Parse limits
        if (data.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var limitEl in limits.EnumerateArray())
            {
                var entry = ParseLimitEntry(limitEl);
                if (entry != null)
                {
                    if (entry.Type == ZaiLimitType.TokensLimit)
                        tokenLimit = entry;
                    else if (entry.Type == ZaiLimitType.TimeLimit)
                        timeLimit = entry;
                }
            }
        }

        return ToUsageSnapshot(tokenLimit, timeLimit, planName);
    }

    private static ZaiLimitEntry? ParseLimitEntry(JsonElement el)
    {
        if (!el.TryGetProperty("type", out var typeProp))
            return null;

        var typeStr = typeProp.GetString();
        ZaiLimitType? limitType = typeStr switch
        {
            "TOKENS_LIMIT" => ZaiLimitType.TokensLimit,
            "TIME_LIMIT" => ZaiLimitType.TimeLimit,
            _ => null
        };

        if (limitType == null)
            return null;

        var unit = el.TryGetProperty("unit", out var unitProp) ? unitProp.GetInt32() : 0;
        var number = el.TryGetProperty("number", out var numberProp) ? numberProp.GetInt32() : 0;
        var usage = el.TryGetProperty("usage", out var usageProp) ? usageProp.GetInt32() : 0;
        var currentValue = el.TryGetProperty("currentValue", out var cvProp) ? cvProp.GetInt32() : 0;
        var remaining = el.TryGetProperty("remaining", out var remProp) ? remProp.GetInt32() : 0;
        var percentage = el.TryGetProperty("percentage", out var pctProp) ? pctProp.GetInt32() : 0;
        var nextResetTime = el.TryGetProperty("nextResetTime", out var resetProp) && resetProp.ValueKind == JsonValueKind.Number
            ? resetProp.GetInt64()
            : (long?)null;

        return new ZaiLimitEntry
        {
            Type = limitType.Value,
            Unit = (ZaiLimitUnit)unit,
            Number = number,
            Usage = usage,
            CurrentValue = currentValue,
            Remaining = remaining,
            Percentage = percentage,
            NextResetTime = nextResetTime.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(nextResetTime.Value).UtcDateTime
                : null
        };
    }

    private static UsageSnapshot ToUsageSnapshot(ZaiLimitEntry? tokenLimit, ZaiLimitEntry? timeLimit, string? planName)
    {
        var primaryLimit = tokenLimit ?? timeLimit;
        var secondaryLimit = (tokenLimit != null && timeLimit != null) ? timeLimit : null;

        RateWindow? primary = null;
        RateWindow? secondary = null;

        if (primaryLimit != null)
        {
            primary = new RateWindow
            {
                UsedPercent = primaryLimit.ComputedUsedPercent,
                Used = primaryLimit.CurrentValue,
                Limit = primaryLimit.Usage,
                WindowMinutes = primaryLimit.WindowMinutes,
                ResetsAt = primaryLimit.NextResetTime,
                ResetDescription = primaryLimit.WindowLabel ?? (primaryLimit.Type == ZaiLimitType.TimeLimit ? "Monthly" : null),
                Unit = primaryLimit.Type == ZaiLimitType.TokensLimit ? "tokens" : "time"
            };
        }

        if (secondaryLimit != null)
        {
            secondary = new RateWindow
            {
                UsedPercent = secondaryLimit.ComputedUsedPercent,
                Used = secondaryLimit.CurrentValue,
                Limit = secondaryLimit.Usage,
                WindowMinutes = secondaryLimit.WindowMinutes,
                ResetsAt = secondaryLimit.NextResetTime,
                ResetDescription = secondaryLimit.WindowLabel ?? "Monthly",
                Unit = "time"
            };
        }

        return new UsageSnapshot
        {
            ProviderId = "zai",
            Primary = primary ?? new RateWindow { UsedPercent = 0, ResetDescription = "No data" },
            Secondary = secondary,
            Identity = new ProviderIdentity
            {
                PlanType = string.IsNullOrWhiteSpace(planName) ? "z.ai" : planName.Trim()
            },
            FetchedAt = DateTime.UtcNow
        };
    }

    private static void Log(string message)
    {
        Core.Services.DebugLogger.Log("ZaiUsageFetcher", message);
    }
}

public enum ZaiLimitType
{
    TokensLimit,
    TimeLimit
}

public enum ZaiLimitUnit
{
    Unknown = 0,
    Days = 1,
    Hours = 3,
    Minutes = 5
}

public class ZaiLimitEntry
{
    public ZaiLimitType Type { get; set; }
    public ZaiLimitUnit Unit { get; set; }
    public int Number { get; set; }
    public int Usage { get; set; }
    public int CurrentValue { get; set; }
    public int Remaining { get; set; }
    public int Percentage { get; set; }
    public DateTime? NextResetTime { get; set; }

    public double ComputedUsedPercent
    {
        get
        {
            if (Usage <= 0) return Percentage;

            var limit = Math.Max(0, Usage);
            if (limit <= 0) return Percentage;

            var usedFromRemaining = limit - Remaining;
            var used = Math.Max(0, Math.Min(limit, Math.Max(usedFromRemaining, CurrentValue)));
            var percent = ((double)used / limit) * 100;
            return Math.Min(100, Math.Max(0, percent));
        }
    }

    public int? WindowMinutes
    {
        get
        {
            if (Number <= 0) return null;
            return Unit switch
            {
                ZaiLimitUnit.Minutes => Number,
                ZaiLimitUnit.Hours => Number * 60,
                ZaiLimitUnit.Days => Number * 24 * 60,
                _ => null
            };
        }
    }

    public string? WindowDescription
    {
        get
        {
            if (Number <= 0) return null;
            var unitLabel = Unit switch
            {
                ZaiLimitUnit.Minutes => "minute",
                ZaiLimitUnit.Hours => "hour",
                ZaiLimitUnit.Days => "day",
                _ => null
            };
            if (unitLabel == null) return null;
            var suffix = Number == 1 ? unitLabel : $"{unitLabel}s";
            return $"{Number} {suffix}";
        }
    }

    public string? WindowLabel
    {
        get
        {
            var desc = WindowDescription;
            return desc != null ? $"{desc} window" : null;
        }
    }
}

public class ZaiUsageException : Exception
{
    public ZaiUsageException(string message) : base(message) { }
}
