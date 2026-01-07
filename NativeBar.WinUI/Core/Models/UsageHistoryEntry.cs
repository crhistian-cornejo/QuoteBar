namespace NativeBar.WinUI.Core.Models;

/// <summary>
/// Represents a single historical usage data point
/// </summary>
public record UsageHistoryEntry
{
    public DateTime Date { get; init; }
    public string ProviderId { get; init; } = string.Empty;
    public double PrimaryPercent { get; init; }
    public double? SecondaryPercent { get; init; }
    public double? CostUSD { get; init; }

    /// <summary>
    /// Used count (e.g., requests, tokens)
    /// </summary>
    public double? Used { get; init; }

    /// <summary>
    /// Limit count
    /// </summary>
    public double? Limit { get; init; }
}

/// <summary>
/// Usage history for a single provider
/// </summary>
public class ProviderUsageHistory
{
    public string ProviderId { get; set; } = string.Empty;
    public List<UsageHistoryEntry> Entries { get; set; } = new();

    /// <summary>
    /// Get entries for the last N days
    /// </summary>
    public IEnumerable<UsageHistoryEntry> GetLastDays(int days)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);
        return Entries
            .Where(e => e.Date.Date >= cutoff)
            .OrderBy(e => e.Date);
    }

    /// <summary>
    /// Get the most recent entry for a specific date
    /// </summary>
    public UsageHistoryEntry? GetForDate(DateTime date)
    {
        return Entries
            .Where(e => e.Date.Date == date.Date)
            .OrderByDescending(e => e.Date)
            .FirstOrDefault();
    }

    /// <summary>
    /// Add or update entry for today
    /// </summary>
    public void AddOrUpdate(UsageHistoryEntry entry)
    {
        // Remove any existing entry for the same date
        Entries.RemoveAll(e => e.Date.Date == entry.Date.Date);
        Entries.Add(entry);

        // Keep only last 90 days
        var cutoff = DateTime.UtcNow.Date.AddDays(-90);
        Entries.RemoveAll(e => e.Date.Date < cutoff);
    }
}
