using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuoteBar.Core.Models;

namespace QuoteBar.Core.Services;

/// <summary>
/// Service for polling provider operational status from Statuspage.io and other sources
/// </summary>
public sealed class ProviderStatusService : IDisposable
{
    private static ProviderStatusService? _instance;
    public static ProviderStatusService Instance => _instance ??= new ProviderStatusService();

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, ProviderStatusSnapshot> _statusCache = new();
    private readonly Dictionary<string, string> _statusPageUrls = new()
    {
        // Statuspage.io API format: https://<page>.statuspage.io/api/v2/status.json
        ["claude"] = "https://status.anthropic.com/api/v2/status.json",
        ["codex"] = "https://status.openai.com/api/v2/status.json",
        ["cursor"] = "https://status.cursor.com/api/v2/status.json",
        ["copilot"] = "https://www.githubstatus.com/api/v2/status.json",
        ["droid"] = "https://status.factory.ai/api/v2/status.json",
        // Google Workspace incidents for Gemini/Antigravity
        ["gemini"] = "https://www.google.com/appsstatus/dashboard/incidents.json",
        ["antigravity"] = "https://www.google.com/appsstatus/dashboard/incidents.json",
        // Augment - check if they have a status page
        ["augment"] = "https://status.augmentcode.com/api/v2/status.json",
        // z.ai - no known status page yet
        ["zai"] = ""
    };

    private readonly Dictionary<string, string> _statusLinkUrls = new()
    {
        ["claude"] = "https://status.anthropic.com",
        ["codex"] = "https://status.openai.com",
        ["cursor"] = "https://status.cursor.com",
        ["copilot"] = "https://www.githubstatus.com",
        ["droid"] = "https://status.factory.ai",
        ["gemini"] = "https://www.google.com/appsstatus/dashboard",
        ["antigravity"] = "https://www.google.com/appsstatus/dashboard",
        ["augment"] = "https://status.augmentcode.com",
        ["zai"] = ""
    };

    private Timer? _pollingTimer;
    private bool _isEnabled = true;
    private int _pollingIntervalSeconds = 300; // 5 minutes default

    public event Action<string, ProviderStatusSnapshot>? StatusUpdated;

    private ProviderStatusService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "QuoteBar/1.0");
    }

    /// <summary>
    /// Start automatic status polling for all providers
    /// </summary>
    public void StartPolling(int intervalSeconds = 300)
    {
        _pollingIntervalSeconds = intervalSeconds;
        _pollingTimer?.Dispose();
        _pollingTimer = new Timer(
            async _ => await PollAllProvidersAsync(),
            null,
            TimeSpan.Zero, // Start immediately
            TimeSpan.FromSeconds(intervalSeconds)
        );
        DebugLogger.Log("ProviderStatusService", $"Started polling every {intervalSeconds}s");
    }

    /// <summary>
    /// Stop automatic status polling
    /// </summary>
    public void StopPolling()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        DebugLogger.Log("ProviderStatusService", "Stopped polling");
    }

    /// <summary>
    /// Enable or disable status checking
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (!value)
            {
                StopPolling();
            }
        }
    }

    /// <summary>
    /// Get cached status for a provider
    /// </summary>
    public ProviderStatusSnapshot? GetStatus(string providerId)
    {
        return _statusCache.TryGetValue(providerId, out var status) ? status : null;
    }

    /// <summary>
    /// Get the status page URL for a provider
    /// </summary>
    public string? GetStatusPageUrl(string providerId)
    {
        return _statusLinkUrls.TryGetValue(providerId, out var url) && !string.IsNullOrEmpty(url) ? url : null;
    }

    /// <summary>
    /// Poll status for all configured providers
    /// </summary>
    public async Task PollAllProvidersAsync()
    {
        if (!_isEnabled) return;

        var tasks = _statusPageUrls
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .Select(kvp => FetchStatusAsync(kvp.Key));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Fetch status for a specific provider
    /// </summary>
    public async Task<ProviderStatusSnapshot?> FetchStatusAsync(string providerId)
    {
        if (!_statusPageUrls.TryGetValue(providerId, out var url) || string.IsNullOrEmpty(url))
        {
            return null;
        }

        try
        {
            DebugLogger.Log("ProviderStatusService", $"Fetching status for {providerId} from {url}");

            // Handle different API formats
            if (url.Contains("google.com/appsstatus"))
            {
                return await FetchGoogleWorkspaceStatusAsync(providerId, url);
            }
            else
            {
                return await FetchStatuspageStatusAsync(providerId, url);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProviderStatusService", $"Failed to fetch status for {providerId}", ex);
            
            var unknownStatus = new ProviderStatusSnapshot
            {
                Level = ProviderStatusLevel.Unknown,
                Description = "Unable to fetch status",
                FetchedAt = DateTime.UtcNow,
                StatusPageUrl = _statusLinkUrls.GetValueOrDefault(providerId)
            };
            
            _statusCache[providerId] = unknownStatus;
            return unknownStatus;
        }
    }

    /// <summary>
    /// Fetch status from Statuspage.io API
    /// </summary>
    private async Task<ProviderStatusSnapshot> FetchStatuspageStatusAsync(string providerId, string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var statusResponse = JsonSerializer.Deserialize<StatuspageResponse>(json);

        var level = MapStatuspageIndicator(statusResponse?.Status?.Indicator ?? "none");
        var description = statusResponse?.Status?.Description ?? "Unknown";

        // Also fetch incidents for more detail
        var incidentCount = 0;
        string? incidentSummary = null;
        
        try
        {
            var incidentsUrl = url.Replace("/status.json", "/incidents/unresolved.json");
            var incidentsResponse = await _httpClient.GetAsync(incidentsUrl);
            if (incidentsResponse.IsSuccessStatusCode)
            {
                var incidentsJson = await incidentsResponse.Content.ReadAsStringAsync();
                var incidents = JsonSerializer.Deserialize<StatuspageIncidentsResponse>(incidentsJson);
                incidentCount = incidents?.Incidents?.Count ?? 0;
                if (incidentCount > 0)
                {
                    incidentSummary = string.Join("; ", incidents!.Incidents!.Take(3).Select(i => i.Name));
                }
            }
        }
        catch
        {
            // Ignore incident fetch errors
        }

        var snapshot = new ProviderStatusSnapshot
        {
            Level = level,
            Description = description,
            IncidentSummary = incidentSummary,
            FetchedAt = DateTime.UtcNow,
            StatusPageUrl = _statusLinkUrls.GetValueOrDefault(providerId),
            ActiveIncidents = incidentCount
        };

        _statusCache[providerId] = snapshot;
        StatusUpdated?.Invoke(providerId, snapshot);
        
        DebugLogger.Log("ProviderStatusService", $"{providerId}: {level} - {description}");
        return snapshot;
    }

    /// <summary>
    /// Fetch status from Google Workspace incidents feed (for Gemini/Antigravity)
    /// </summary>
    private async Task<ProviderStatusSnapshot> FetchGoogleWorkspaceStatusAsync(string providerId, string url)
    {
        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var incidents = JsonSerializer.Deserialize<List<GoogleWorkspaceIncident>>(json) ?? new List<GoogleWorkspaceIncident>();

        // Filter for Gemini-related incidents (product ID varies)
        // Common product names: "Gemini", "Google Gemini", "AI Studio"
        var geminiIncidents = incidents
            .Where(i => i.AffectedProducts?.Any(p => 
                p.Title?.Contains("Gemini", StringComparison.OrdinalIgnoreCase) == true ||
                p.Title?.Contains("AI Studio", StringComparison.OrdinalIgnoreCase) == true) == true)
            .Where(i => i.End == null) // Only active incidents
            .ToList();

        var level = ProviderStatusLevel.Operational;
        string? summary = null;
        
        if (geminiIncidents.Count > 0)
        {
            var mostSevere = geminiIncidents.MaxBy(i => MapGoogleSeverity(i.Severity));
            level = MapGoogleSeverity(mostSevere?.Severity);
            summary = mostSevere?.MostRecentUpdate?.Text;
        }

        var snapshot = new ProviderStatusSnapshot
        {
            Level = level,
            Description = level == ProviderStatusLevel.Operational ? "All Systems Operational" : "Service Disruption",
            IncidentSummary = summary,
            FetchedAt = DateTime.UtcNow,
            StatusPageUrl = _statusLinkUrls.GetValueOrDefault(providerId),
            ActiveIncidents = geminiIncidents.Count
        };

        _statusCache[providerId] = snapshot;
        StatusUpdated?.Invoke(providerId, snapshot);
        
        return snapshot;
    }

    private static ProviderStatusLevel MapStatuspageIndicator(string indicator)
    {
        return indicator.ToLowerInvariant() switch
        {
            "none" => ProviderStatusLevel.Operational,
            "minor" => ProviderStatusLevel.Degraded,
            "major" => ProviderStatusLevel.PartialOutage,
            "critical" => ProviderStatusLevel.MajorOutage,
            "maintenance" => ProviderStatusLevel.Maintenance,
            _ => ProviderStatusLevel.Unknown
        };
    }

    private static ProviderStatusLevel MapGoogleSeverity(string? severity)
    {
        return severity?.ToLowerInvariant() switch
        {
            "low" => ProviderStatusLevel.Degraded,
            "medium" => ProviderStatusLevel.PartialOutage,
            "high" => ProviderStatusLevel.MajorOutage,
            _ => ProviderStatusLevel.Operational
        };
    }

    public void Dispose()
    {
        _pollingTimer?.Dispose();
        _httpClient.Dispose();
    }
}

#region Statuspage.io Models

internal class StatuspageResponse
{
    [JsonPropertyName("status")]
    public StatuspageStatus? Status { get; set; }
    
    [JsonPropertyName("page")]
    public StatuspagePage? Page { get; set; }
}

internal class StatuspageStatus
{
    [JsonPropertyName("indicator")]
    public string? Indicator { get; set; }
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

internal class StatuspagePage
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

internal class StatuspageIncidentsResponse
{
    [JsonPropertyName("incidents")]
    public List<StatuspageIncident>? Incidents { get; set; }
}

internal class StatuspageIncident
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("status")]
    public string? Status { get; set; }
    
    [JsonPropertyName("impact")]
    public string? Impact { get; set; }
    
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }
}

#endregion

#region Google Workspace Models

internal class GoogleWorkspaceIncident
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
    
    [JsonPropertyName("severity")]
    public string? Severity { get; set; }
    
    [JsonPropertyName("begin")]
    public string? Begin { get; set; }
    
    [JsonPropertyName("end")]
    public string? End { get; set; }
    
    [JsonPropertyName("affected_products")]
    public List<GoogleWorkspaceProduct>? AffectedProducts { get; set; }
    
    [JsonPropertyName("most_recent_update")]
    public GoogleWorkspaceUpdate? MostRecentUpdate { get; set; }
}

internal class GoogleWorkspaceProduct
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }
    
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

internal class GoogleWorkspaceUpdate
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }
    
    [JsonPropertyName("when")]
    public string? When { get; set; }
}

#endregion
