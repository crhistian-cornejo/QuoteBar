using System;
using System.Collections.Generic;

namespace NativeBar.WinUI.Core.Models;

/// <summary>
/// Represents a time-based usage window with reset information
/// </summary>
public record RateWindow
{
    public double UsedPercent { get; init; }
    public int? WindowMinutes { get; init; }
    public DateTime? ResetsAt { get; init; }
    public string? ResetDescription { get; init; }
    public double? Used { get; init; }
    public double? Limit { get; init; }
    public string? Unit { get; init; }
    /// <summary>Display label for this usage window (e.g., "Auto", "API", "On-Demand")</summary>
    public string? Label { get; init; }
}

/// <summary>
/// Provider identity information
/// </summary>
public record ProviderIdentity
{
    public string? Email { get; init; }
    public string? PlanType { get; init; }
    public string? AccountId { get; init; }
}

/// <summary>
/// Cost tracking snapshot - CodexBar style with session and 30-day data
/// </summary>
public record ProviderCost
{
    // Current session (today)
    public double? SessionCostUSD { get; init; }
    public int? SessionTokens { get; init; }
    
    // 30-day rolling window
    public double TotalCostUSD { get; init; }
    public int? TotalTokens { get; init; }
    
    // Period info
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    
    // Model breakdown (model name -> cost)
    public Dictionary<string, double>? CostBreakdown { get; init; }
}

/// <summary>
/// Complete usage snapshot for a provider
/// </summary>
public record UsageSnapshot
{
    public string ProviderId { get; init; } = string.Empty;
    public RateWindow? Primary { get; init; }
    public RateWindow? Secondary { get; init; }
    public RateWindow? Tertiary { get; init; }
    public ProviderCost? Cost { get; init; }
    public ProviderIdentity? Identity { get; init; }
    public DateTime FetchedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsLoading { get; init; }
}
