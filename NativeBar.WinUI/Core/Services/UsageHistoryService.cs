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
