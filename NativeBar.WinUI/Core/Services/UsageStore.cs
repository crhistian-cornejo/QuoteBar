using CommunityToolkit.Mvvm.ComponentModel;
using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Providers;
using System.Collections.ObjectModel;

namespace NativeBar.WinUI.Core.Services;

/// <summary>
/// Central store for managing provider usage data
/// </summary>
public partial class UsageStore : ObservableObject
{
    [ObservableProperty]
    private string? _currentProviderId;
    
    [ObservableProperty]
    private ObservableCollection<string> _activeProviderIds = new();
    
    private readonly Dictionary<string, UsageSnapshot> _snapshots = new();
    private readonly Dictionary<string, UsageFetcher> _fetchers = new();
    private readonly Timer _refreshTimer;

    /// <summary>
    /// Event fired after all providers are refreshed (for tray badge updates)
    /// </summary>
    public event Action? AllProvidersRefreshed;
    
    public UsageStore()
    {
        InitializeProviders();
        _refreshTimer = new Timer(OnRefreshTimer, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
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
            _snapshots[providerId] = snapshot;
            
            // Check for usage alerts after successful fetch
            var provider = ProviderRegistry.Instance.GetProvider(providerId);
            if (provider != null)
            {
                NotificationService.Instance.CheckAndNotifyUsage(providerId, provider.DisplayName, snapshot);
            }
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
        _refreshTimer?.Dispose();
    }
}
