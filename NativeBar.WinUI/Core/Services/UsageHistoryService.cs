using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using NativeBar.WinUI.Core.Models;

namespace NativeBar.WinUI.Core.Services;

/// <summary>
/// Service to persist and manage usage history for charts
/// </summary>
public class UsageHistoryService
{
    private static UsageHistoryService? _instance;
    public static UsageHistoryService Instance => _instance ??= new UsageHistoryService();

    private readonly ConcurrentDictionary<string, ProviderUsageHistory> _histories = new();
    private readonly string _historyFilePath;
    private readonly object _saveLock = new();

    private UsageHistoryService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuoteBar");
        Directory.CreateDirectory(appDataPath);
        _historyFilePath = Path.Combine(appDataPath, "usage_history.json");

        LoadHistory();
    }

    /// <summary>
    /// Record a usage snapshot for a provider
    /// </summary>
    public void RecordSnapshot(string providerId, UsageSnapshot snapshot)
    {
        if (snapshot.ErrorMessage != null || snapshot.IsLoading)
            return;

        var entry = new UsageHistoryEntry
        {
            Date = DateTime.UtcNow,
            ProviderId = providerId,
            PrimaryPercent = snapshot.Primary?.UsedPercent ?? 0,
            SecondaryPercent = snapshot.Secondary?.UsedPercent,
            CostUSD = snapshot.Cost?.TotalCostUSD,
            Used = snapshot.Primary?.Used,
            Limit = snapshot.Primary?.Limit
        };

        var history = _histories.GetOrAdd(providerId, _ => new ProviderUsageHistory { ProviderId = providerId });
        history.AddOrUpdate(entry);

        // Save asynchronously
        _ = Task.Run(() => SaveHistoryAsync());
    }

    /// <summary>
    /// Get usage history for a provider
    /// </summary>
    public ProviderUsageHistory GetHistory(string providerId)
    {
        return _histories.GetOrAdd(providerId, _ => new ProviderUsageHistory { ProviderId = providerId });
    }

    /// <summary>
    /// Get all provider histories
    /// </summary>
    public IReadOnlyDictionary<string, ProviderUsageHistory> GetAllHistories()
    {
        return _histories;
    }

    /// <summary>
    /// Get daily cost summary for the last N days
    /// </summary>
    public Dictionary<DateTime, double> GetDailyCostSummary(int days = 30)
    {
        var summary = new Dictionary<DateTime, double>();
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);

        foreach (var history in _histories.Values)
        {
            foreach (var entry in history.GetLastDays(days))
            {
                if (entry.CostUSD.HasValue && entry.CostUSD.Value > 0)
                {
                    var date = entry.Date.Date;
                    if (date >= cutoff)
                    {
                        if (summary.ContainsKey(date))
                        {
                            summary[date] += entry.CostUSD.Value;
                        }
                        else
                        {
                            summary[date] = entry.CostUSD.Value;
                        }
                    }
                }
            }
        }

        return summary;
    }

    /// <summary>
    /// Get total cost for all providers in the last N days
    /// </summary>
    public double GetTotalCost(int days = 30)
    {
        return GetDailyCostSummary(days).Values.Sum();
    }

    /// <summary>
    /// Get cost breakdown by provider for the last N days
    /// </summary>
    public Dictionary<string, double> GetProviderCostBreakdown(int days = 30)
    {
        var breakdown = new Dictionary<string, double>();
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);

        foreach (var (providerId, history) in _histories)
        {
            double total = 0;
            foreach (var entry in history.GetLastDays(days))
            {
                if (entry.CostUSD.HasValue)
                {
                    total += entry.CostUSD.Value;
                }
            }
            if (total > 0)
            {
                breakdown[providerId] = total;
            }
        }

        return breakdown;
    }

    /// <summary>
    /// Get daily cost data for chart visualization (dates sorted ascending)
    /// </summary>
    public List<(DateTime Date, double Cost)> GetDailyCostChartData(int days = 30)
    {
        var summary = GetDailyCostSummary(days);
        return summary
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();
    }

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(_historyFilePath))
                return;

            var json = File.ReadAllText(_historyFilePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, ProviderUsageHistory>>(json);

            if (data != null)
            {
                foreach (var (providerId, history) in data)
                {
                    _histories[providerId] = history;
                }
            }

            DebugLogger.Log("UsageHistory", $"Loaded history: {_histories.Count} providers");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("UsageHistory", "Failed to load history", ex);
        }
    }

    private void SaveHistoryAsync()
    {
        lock (_saveLock)
        {
            try
            {
                var data = _histories.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("UsageHistory", "Failed to save history", ex);
            }
        }
    }
}
