using System;
using System.Collections.Generic;

namespace NativeBar.WinUI.Core.CostUsage;

/// <summary>
/// Complete token cost snapshot for a provider - includes session, 30-day, and daily breakdown
/// </summary>
public record CostUsageTokenSnapshot
{
    public int? SessionTokens { get; init; }
    public double? SessionCostUSD { get; init; }
    public int? Last30DaysTokens { get; init; }
    public double? Last30DaysCostUSD { get; init; }
    public IReadOnlyList<CostUsageDailyEntry> Daily { get; init; } = Array.Empty<CostUsageDailyEntry>();
    public DateTime UpdatedAt { get; init; }

    public static CostUsageTokenSnapshot Empty => new()
    {
        SessionTokens = null,
        SessionCostUSD = null,
        Last30DaysTokens = null,
        Last30DaysCostUSD = null,
        Daily = Array.Empty<CostUsageDailyEntry>(),
        UpdatedAt = DateTime.UtcNow
    };
}

/// <summary>
/// Model-specific cost breakdown for a day
/// </summary>
public record ModelBreakdown
{
    public string ModelName { get; init; } = string.Empty;
    public double? CostUSD { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? CacheReadTokens { get; init; }
    public int? CacheCreationTokens { get; init; }
}

/// <summary>
/// Daily cost/usage entry
/// </summary>
public record CostUsageDailyEntry
{
    public string Date { get; init; } = string.Empty; // yyyy-MM-dd format
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? CacheReadTokens { get; init; }
    public int? CacheCreationTokens { get; init; }
    public int? TotalTokens { get; init; }
    public double? CostUSD { get; init; }
    public IReadOnlyList<string>? ModelsUsed { get; init; }
    public IReadOnlyList<ModelBreakdown>? ModelBreakdowns { get; init; }
}

/// <summary>
/// Summary of cost usage over a period
/// </summary>
public record CostUsageSummary
{
    public int? TotalInputTokens { get; init; }
    public int? TotalOutputTokens { get; init; }
    public int? TotalCacheReadTokens { get; init; }
    public int? TotalCacheCreationTokens { get; init; }
    public int? TotalTokens { get; init; }
    public double? TotalCostUSD { get; init; }
}

/// <summary>
/// Daily report containing entries and optional summary
/// </summary>
public record CostUsageDailyReport
{
    public IReadOnlyList<CostUsageDailyEntry> Data { get; init; } = Array.Empty<CostUsageDailyEntry>();
    public CostUsageSummary? Summary { get; init; }
}

/// <summary>
/// Range of days for scanning
/// </summary>
public readonly struct CostUsageDayRange
{
    public string SinceKey { get; }
    public string UntilKey { get; }
    public string ScanSinceKey { get; }
    public string ScanUntilKey { get; }

    public CostUsageDayRange(DateTime since, DateTime until)
    {
        SinceKey = DayKey(since);
        UntilKey = DayKey(until);
        ScanSinceKey = DayKey(since.AddDays(-1));
        ScanUntilKey = DayKey(until.AddDays(1));
    }

    public static string DayKey(DateTime date)
    {
        return date.ToString("yyyy-MM-dd");
    }

    public static bool IsInRange(string dayKey, string since, string until)
    {
        return string.CompareOrdinal(dayKey, since) >= 0 && 
               string.CompareOrdinal(dayKey, until) <= 0;
    }

    public static DateTime? ParseDayKey(string key)
    {
        if (DateTime.TryParseExact(key, "yyyy-MM-dd", null, 
            System.Globalization.DateTimeStyles.None, out var result))
        {
            return result;
        }
        return null;
    }
}
