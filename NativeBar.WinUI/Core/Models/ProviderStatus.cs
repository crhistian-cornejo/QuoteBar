namespace NativeBar.WinUI.Core.Models;

/// <summary>
/// Represents the operational status of a provider from Statuspage.io or similar
/// </summary>
public enum ProviderStatusLevel
{
    /// <summary>All Systems Operational</summary>
    Operational,
    /// <summary>Degraded Performance</summary>
    Degraded,
    /// <summary>Partial Outage</summary>
    PartialOutage,
    /// <summary>Major Outage</summary>
    MajorOutage,
    /// <summary>Under Maintenance</summary>
    Maintenance,
    /// <summary>Unable to fetch status</summary>
    Unknown
}

/// <summary>
/// Provider operational status snapshot
/// </summary>
public record ProviderStatusSnapshot
{
    /// <summary>Current operational status level</summary>
    public ProviderStatusLevel Level { get; init; }
    
    /// <summary>Human-readable status description</summary>
    public string? Description { get; init; }
    
    /// <summary>Active incident summary (if any)</summary>
    public string? IncidentSummary { get; init; }
    
    /// <summary>When the status was last fetched</summary>
    public DateTime FetchedAt { get; init; }
    
    /// <summary>URL to the status page</summary>
    public string? StatusPageUrl { get; init; }
    
    /// <summary>Number of active incidents</summary>
    public int ActiveIncidents { get; init; }
}
