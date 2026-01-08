using CommunityToolkit.Mvvm.ComponentModel;
using QuoteBar.Core.CostUsage;
using QuoteBar.Core.Models;
using QuoteBar.Core.Providers;
using System.Collections.ObjectModel;

namespace QuoteBar.Core.Services;

/// <summary>
/// Central store for managing provider usage data
/// </summary>
public partial class UsageStore : ObservableObject, IDisposable
{
    private bool _disposed;
    [ObservableProperty]
    private string? _currentProviderId;
    
    [ObservableProperty]
    private ObservableCollection<string> _activeProviderIds = new();
    
    private readonly Dictionary<string, UsageSnapshot> _snapshots = new();
    private readonly Dictionary<string, UsageFetcher> _fetchers = new();
    private Timer? _refreshTimer;
    private readonly SettingsService _settings = SettingsService.Instance;
    private readonly CostUsageFetcher _costFetcher = CostUsageFetcher.Instance;

    /// <summary>
    /// Event fired after all providers are refreshed (for tray badge updates)
    /// </summary>
    public event Action? AllProvidersRefreshed;

    public UsageStore()
    {
        InitializeProviders();

        if (_settings.Settings.AutoRefreshEnabled)
        {
            StartRefreshTimer();
        }

        _settings.SettingsChanged += OnSettingsChanged;
    }

    private void StartRefreshTimer()
    {
        _refreshTimer?.Dispose();
        var interval = TimeSpan.FromMinutes(_settings.Settings.RefreshIntervalMinutes);
        _refreshTimer = new Timer(OnRefreshTimer, null, TimeSpan.Zero, interval);
        DebugLogger.Log("UsageStore", $"Refresh timer started with {interval.TotalMinutes} min interval");
    }

    private void StopRefreshTimer()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
        DebugLogger.Log("UsageStore", "Refresh timer stopped (manual mode)");
    }

    private void OnSettingsChanged()
    {
        if (_settings.Settings.AutoRefreshEnabled)
        {
            if (_refreshTimer == null)
            {
                StartRefreshTimer();
            }
            else
            {
                StopRefreshTimer();
                StartRefreshTimer();
            }
        }
        else
        {
            StopRefreshTimer();
        }
    }
    
    private void InitializeProviders()
    {
        var registry = ProviderRegistry.Instance;
        var providers = registry.GetAllProviders();
        
        foreach (var provider in providers)
        {
            _fetchers[provider.Id] = new UsageFetcher(provider);
            ActiveProviderIds.Add(provider.Id);
        }
        
        if (ActiveProviderIds.Count > 0)
        {
            CurrentProviderId = ActiveProviderIds[0];
        }
    }
    
    public UsageSnapshot? GetSnapshot(string providerId)
    {
        _snapshots.TryGetValue(providerId, out var snapshot);
        return snapshot;
    }
    
    public UsageSnapshot? GetCurrentSnapshot()
    {
        if (CurrentProviderId == null) return null;
        return GetSnapshot(CurrentProviderId);
    }
    
    public IProviderDescriptor? GetCurrentProvider()
    {
        if (CurrentProviderId == null) return null;
        return ProviderRegistry.Instance.GetProvider(CurrentProviderId);
    }
    
    public async Task RefreshAsync(string providerId)
    {
        if (!_fetchers.TryGetValue(providerId, out var fetcher))
        {
            return;
        }
        
        // Set loading state
        _snapshots[providerId] = new UsageSnapshot
        {
            ProviderId = providerId,
            IsLoading = true,
            FetchedAt = DateTime.UtcNow
        };
        
        OnPropertyChanged(nameof(GetCurrentSnapshot));
        
        try
        {
            var snapshot = await fetcher.FetchAsync();
            
            // Enrich with local cost data for supported providers
            snapshot = await EnrichWithLocalCostDataAsync(providerId, snapshot);
            
            _snapshots[providerId] = snapshot;

            // Check for usage alerts after successful fetch
            var provider = ProviderRegistry.Instance.GetProvider(providerId);
            if (provider != null)
            {
                NotificationService.Instance.CheckAndNotifyUsage(providerId, snapshot);
            }

            // Record history for charts
            UsageHistoryService.Instance.RecordSnapshot(providerId, snapshot);
        }
        catch (Exception ex)
        {
            _snapshots[providerId] = new UsageSnapshot
            {
                ProviderId = providerId,
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
        
        OnPropertyChanged(nameof(GetCurrentSnapshot));
    }

    /// <summary>
    /// Enrich a snapshot with local cost data from CLI logs (Codex/Claude)
    /// </summary>
    private async Task<UsageSnapshot> EnrichWithLocalCostDataAsync(string providerId, UsageSnapshot snapshot)
    {
        try
        {
            CostUsageTokenSnapshot? costSnapshot = null;

            // Get local cost data for supported providers
            if (providerId.Equals("codex", StringComparison.OrdinalIgnoreCase))
            {
                costSnapshot = await _costFetcher.LoadTokenSnapshotAsync(CostUsageProvider.Codex);
            }
            else if (providerId.Equals("claude", StringComparison.OrdinalIgnoreCase))
            {
                costSnapshot = await _costFetcher.LoadTokenSnapshotAsync(CostUsageProvider.Claude);
            }

            if (costSnapshot != null && (costSnapshot.Last30DaysCostUSD > 0 || costSnapshot.Last30DaysTokens > 0))
            {
                // Create or update cost info
                var cost = new ProviderCost
                {
                    SessionCostUSD = costSnapshot.SessionCostUSD,
                    SessionTokens = costSnapshot.SessionTokens,
                    TotalCostUSD = costSnapshot.Last30DaysCostUSD ?? 0,
                    TotalTokens = costSnapshot.Last30DaysTokens,
                    StartDate = DateTime.UtcNow.AddDays(-29),
                    EndDate = DateTime.UtcNow
                };

                return snapshot with { Cost = cost };
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("UsageStore", $"Failed to enrich with local cost data for {providerId}", ex);
        }

        return snapshot;
    }
    
    /// <summary>
    /// Clear a provider's snapshot (used when disconnecting)
    /// Sets the snapshot to show as not configured
    /// </summary>
    public void ClearSnapshot(string providerId)
    {
        _snapshots[providerId] = new UsageSnapshot
        {
            ProviderId = providerId,
            ErrorMessage = "Not configured",
            FetchedAt = DateTime.UtcNow
        };

        OnPropertyChanged(nameof(GetCurrentSnapshot));
        DebugLogger.Log("UsageStore", $"Cleared snapshot for {providerId}");
    }

    public async Task RefreshAllAsync()
    {
        var tasks = ActiveProviderIds.Select(id => RefreshAsync(id));
        await Task.WhenAll(tasks);

        // Notify listeners that all providers have been refreshed
        AllProvidersRefreshed?.Invoke();
    }

    /// <summary>
    /// Get all current snapshots (for badge generation)
    /// </summary>
    public Dictionary<string, UsageSnapshot?> GetAllSnapshots()
    {
        return ActiveProviderIds.ToDictionary(id => id, id => GetSnapshot(id));
    }
    
    private async void OnRefreshTimer(object? state)
    {
        await RefreshAllAsync();
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            // Unsubscribe from events to prevent memory leaks
            _settings.SettingsChanged -= OnSettingsChanged;

            // Dispose timer
            _refreshTimer?.Dispose();
            _refreshTimer = null;

            DebugLogger.Log("UsageStore", "Disposed");
        }

        _disposed = true;
    }
}
