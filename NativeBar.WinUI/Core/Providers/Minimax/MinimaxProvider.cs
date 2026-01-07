using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Services;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NativeBar.WinUI.Core.Providers.Minimax;

public class MinimaxProviderDescriptor : ProviderDescriptor
{
    public override string Id => "minimax";
    public override string DisplayName => "Minimax";
    public override string IconGlyph => "\uE945"; // Placeholder - will use SVG
    public override string PrimaryColor => "#E2167E"; // From gradient start
    public override string SecondaryColor => "#FE603C"; // From gradient end
    public override string PrimaryLabel => "API Status";
    public override string SecondaryLabel => "Models";
    public override string? DashboardUrl => "https://platform.minimax.io";

    public override bool SupportsOAuth => false;
    public override bool SupportsCLI => false;

    protected override void InitializeStrategies()
    {
        AddStrategy(new MinimaxAPIStrategy());
    }
}

/// <summary>
/// API Key strategy for Minimax
/// Minimax doesn't have a public usage/quota API, so we validate the key
/// and show connection status with available models.
/// </summary>
public class MinimaxAPIStrategy : IProviderFetchStrategy
{
    public string StrategyName => "API";
    public int Priority => 1;

    public Task<bool> CanExecuteAsync()
    {
        var hasCredentials = MinimaxCredentialsStore.HasCredentials();
        Log($"CanExecute: hasCredentials={hasCredentials}");
        return Task.FromResult(hasCredentials);
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = MinimaxCredentialsStore.GetApiKey();
            var groupId = MinimaxCredentialsStore.GetGroupId();

            if (string.IsNullOrEmpty(apiKey))
            {
                return new UsageSnapshot
                {
                    ProviderId = "minimax",
                    ErrorMessage = "Minimax API key not configured. Click 'Connect' to set up.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            // Validate API key and get model info
            var result = await MinimaxUsageFetcher.ValidateAndFetchInfoAsync(apiKey, groupId, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            Log($"Fetch ERROR: {ex.Message}\n{ex.StackTrace}");
            return new UsageSnapshot
            {
                ProviderId = "minimax",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private void Log(string message)
    {
        DebugLogger.Log("MinimaxAPIStrategy", message);
    }
}

/// <summary>
/// Securely stores Minimax API credentials using Windows Credential Manager
/// </summary>
public static class MinimaxCredentialsStore
{
    public const string ApiKeyEnvVar = "MINIMAX_API_KEY";
    public const string GroupIdEnvVar = "MINIMAX_GROUP_ID";

    /// <summary>
    /// Get API key from secure storage or environment variable
    /// </summary>
    public static string? GetApiKey()
    {
        // First check secure credential store
        var key = SecureCredentialStore.GetCredential(CredentialKeys.MinimaxApiKey);
        if (!string.IsNullOrWhiteSpace(key))
            return CleanValue(key);

        // Then check environment variable
        key = Environment.GetEnvironmentVariable(ApiKeyEnvVar);
        if (!string.IsNullOrWhiteSpace(key))
            return CleanValue(key);

        return null;
    }

    /// <summary>
    /// Get Group ID from secure storage or environment variable
    /// </summary>
    public static string? GetGroupId()
    {
        var id = SecureCredentialStore.GetCredential(CredentialKeys.MinimaxGroupId);
        if (!string.IsNullOrWhiteSpace(id))
            return CleanValue(id);

        id = Environment.GetEnvironmentVariable(GroupIdEnvVar);
        if (!string.IsNullOrWhiteSpace(id))
            return CleanValue(id);

        return null;
    }

    /// <summary>
    /// Store API key securely
    /// </summary>
    public static bool StoreApiKey(string? key)
    {
        var cleaned = CleanValue(key);
        return SecureCredentialStore.StoreCredential(CredentialKeys.MinimaxApiKey, cleaned);
    }

    /// <summary>
    /// Store Group ID securely
    /// </summary>
    public static bool StoreGroupId(string? groupId)
    {
        var cleaned = CleanValue(groupId);
        return SecureCredentialStore.StoreCredential(CredentialKeys.MinimaxGroupId, cleaned);
    }

    /// <summary>
    /// Check if credentials are configured
    /// </summary>
    public static bool HasCredentials()
    {
        return !string.IsNullOrWhiteSpace(GetApiKey());
    }

    /// <summary>
    /// Delete all Minimax credentials
    /// </summary>
    public static bool DeleteCredentials()
    {
        var keyDeleted = SecureCredentialStore.DeleteCredential(CredentialKeys.MinimaxApiKey);
        var groupDeleted = SecureCredentialStore.DeleteCredential(CredentialKeys.MinimaxGroupId);
        return keyDeleted || groupDeleted;
    }

    private static string? CleanValue(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();
        if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
            (value.StartsWith("'") && value.EndsWith("'")))
        {
            value = value.Substring(1, value.Length - 2);
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

/// <summary>
/// Validates Minimax API key and fetches available info
/// Note: Minimax doesn't have a public usage/quota API, so we just validate the key
/// </summary>
public static class MinimaxUsageFetcher
{
    // International endpoint (use api.minimaxi.com for China)
    private const string BaseUrl = "https://api.minimax.io";
    private const int TimeoutSeconds = 30;

    /// <summary>
    /// Validate API key and fetch basic info
    /// </summary>
    public static async Task<UsageSnapshot> ValidateAndFetchInfoAsync(
        string apiKey,
        string? groupId,
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Try to list models to validate the API key
        var url = $"{BaseUrl}/v1/models";
        if (!string.IsNullOrEmpty(groupId))
        {
            url += $"?GroupId={groupId}";
        }

        Log($"Validating API key: GET {url}");

        try
        {
            var response = await client.GetAsync(url, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            Log($"Response: {response.StatusCode} - {content.Substring(0, Math.Min(500, content.Length))}");

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return new UsageSnapshot
                {
                    ProviderId = "minimax",
                    ErrorMessage = "Invalid API key. Please check your Minimax API key.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UsageSnapshot
                {
                    ProviderId = "minimax",
                    ErrorMessage = $"Minimax API error: {response.StatusCode}",
                    FetchedAt = DateTime.UtcNow
                };
            }

            // Parse models response
            return ParseModelsResponse(content);
        }
        catch (HttpRequestException ex)
        {
            Log($"Network error: {ex.Message}");
            return new UsageSnapshot
            {
                ProviderId = "minimax",
                ErrorMessage = "Network error connecting to Minimax API",
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private static UsageSnapshot ParseModelsResponse(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var modelCount = 0;
            var topModels = new List<string>();

            // OpenAI-compatible format: { "data": [...] }
            if (root.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in dataArray.EnumerateArray())
                {
                    modelCount++;
                    if (model.TryGetProperty("id", out var idProp) && topModels.Count < 3)
                    {
                        var modelId = idProp.GetString();
                        if (!string.IsNullOrEmpty(modelId))
                        {
                            topModels.Add(FormatModelName(modelId));
                        }
                    }
                }
            }

            // Build snapshot - since no usage API exists, show connection status
            var primary = new RateWindow
            {
                UsedPercent = 0, // No usage data available
                Used = modelCount,
                Limit = null,
                Unit = "models available"
            };

            RateWindow? secondary = null;
            if (topModels.Count > 0)
            {
                secondary = new RateWindow
                {
                    UsedPercent = 0,
                    Used = null,
                    Limit = null,
                    Unit = string.Join(", ", topModels.Take(2))
                };
            }

            return new UsageSnapshot
            {
                ProviderId = "minimax",
                Primary = primary,
                Secondary = secondary,
                Identity = new ProviderIdentity
                {
                    PlanType = "Minimax API"
                },
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Log($"Parse error: {ex.Message}");

            // API key is valid even if we can't parse the response
            return new UsageSnapshot
            {
                ProviderId = "minimax",
                Primary = new RateWindow
                {
                    UsedPercent = 0,
                    Unit = "Connected"
                },
                Identity = new ProviderIdentity
                {
                    PlanType = "Minimax API"
                },
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private static string FormatModelName(string modelId)
    {
        // Shorten common model names
        return modelId switch
        {
            "abab5.5-chat" => "abab5.5",
            "abab5.5s-chat" => "abab5.5s",
            "abab6-chat" => "abab6",
            "abab6.5-chat" => "abab6.5",
            "abab6.5s-chat" => "abab6.5s",
            "speech-01" => "TTS",
            "speech-02" => "TTS-2",
            "video-01" => "Video",
            "music-01" => "Music",
            _ => modelId.Length > 12 ? modelId.Substring(0, 12) : modelId
        };
    }

    private static void Log(string message)
    {
        DebugLogger.Log("MinimaxUsageFetcher", message);
    }
}

public class MinimaxException : Exception
{
    public MinimaxException(string message) : base(message) { }
}
