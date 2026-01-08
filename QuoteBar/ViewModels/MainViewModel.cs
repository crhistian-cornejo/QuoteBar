using CommunityToolkit.Mvvm.ComponentModel;
using QuoteBar.Core.Services;
using QuoteBar.Core.Models;
using QuoteBar.Core.Providers;

namespace QuoteBar.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly UsageStore _usageStore;
    
    [ObservableProperty]
    private UsageSnapshot? _currentSnapshot;
    
    [ObservableProperty]
    private IProviderDescriptor? _currentProvider;
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private string? _errorMessage;
    
    public MainViewModel(UsageStore usageStore)
    {
        _usageStore = usageStore;
        _usageStore.PropertyChanged += OnUsageStorePropertyChanged;
        
        LoadCurrentData();
    }
    
    private void OnUsageStorePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UsageStore.CurrentProviderId))
        {
            LoadCurrentData();
        }
    }
    
    private void LoadCurrentData()
    {
        CurrentSnapshot = _usageStore.GetCurrentSnapshot();
        CurrentProvider = _usageStore.GetCurrentProvider();
        IsLoading = CurrentSnapshot?.IsLoading ?? false;
        ErrorMessage = CurrentSnapshot?.ErrorMessage;
    }
    
    public async Task RefreshAsync()
    {
        if (_usageStore.CurrentProviderId != null)
        {
            IsLoading = true;
            await _usageStore.RefreshAsync(_usageStore.CurrentProviderId);
            LoadCurrentData();
            IsLoading = false;
        }
    }
    
    public void SwitchProvider(string providerId)
    {
        _usageStore.CurrentProviderId = providerId;
    }
    
    public IReadOnlyList<string> GetActiveProviders()
    {
        return _usageStore.ActiveProviderIds.ToList();
    }
}
