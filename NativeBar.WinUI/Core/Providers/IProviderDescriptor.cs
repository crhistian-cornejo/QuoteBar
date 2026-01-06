using NativeBar.WinUI.Core.Models;

namespace NativeBar.WinUI.Core.Providers;

/// <summary>
/// Base interface for all provider fetch strategies
/// </summary>
public interface IProviderFetchStrategy
{
    string StrategyName { get; }
    int Priority { get; }
    Task<bool> CanExecuteAsync();
    Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider descriptor defines metadata and capabilities
/// </summary>
public interface IProviderDescriptor
{
    string Id { get; }
    string DisplayName { get; }
    string IconGlyph { get; }
    string PrimaryColor { get; }
    string SecondaryColor { get; }
    
    // Labels for usage windows
    string PrimaryLabel { get; }
    string SecondaryLabel { get; }
    string? TertiaryLabel { get; }
    
    // Fetch strategies in priority order
    IReadOnlyList<IProviderFetchStrategy> FetchStrategies { get; }
    
    // Provider-specific configuration
    bool SupportsOAuth { get; }
    bool SupportsWebScraping { get; }
    bool SupportsCLI { get; }
}

/// <summary>
/// Base class for provider descriptors
/// </summary>
public abstract class ProviderDescriptor : IProviderDescriptor
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string IconGlyph { get; }
    public abstract string PrimaryColor { get; }
    public abstract string SecondaryColor { get; }
    public abstract string PrimaryLabel { get; }
    public abstract string SecondaryLabel { get; }
    public virtual string? TertiaryLabel => null;
    
    public virtual bool SupportsOAuth => false;
    public virtual bool SupportsWebScraping => false;
    public virtual bool SupportsCLI => false;
    
    private readonly List<IProviderFetchStrategy> _strategies = new();
    public IReadOnlyList<IProviderFetchStrategy> FetchStrategies => _strategies.AsReadOnly();
    
    protected void AddStrategy(IProviderFetchStrategy strategy)
    {
        _strategies.Add(strategy);
    }
    
    protected ProviderDescriptor()
    {
        InitializeStrategies();
        _strategies.Sort((a, b) => a.Priority.CompareTo(b.Priority));
    }
    
    protected abstract void InitializeStrategies();
}
