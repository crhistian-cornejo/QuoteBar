using QuoteBar.Core.Models;

namespace QuoteBar.Core.Providers;

/// <summary>
/// Defines the type of usage window for notification behavior
/// </summary>
public enum UsageWindowType
{
    /// <summary>Session-based (5h): notify each time limit is reached</summary>
    Session,
    /// <summary>Weekly: notify only once per reset period</summary>
    Weekly,
    /// <summary>Daily: notify only once per day</summary>
    Daily,
    /// <summary>Monthly: notify only once per month</summary>
    Monthly
}

/// <summary>
/// Defines the type of authentication strategy for categorization
/// </summary>
public enum StrategyType
{
    /// <summary>Cached data (no network, highest priority)</summary>
    Cached,
    /// <summary>CLI-based authentication (e.g., claude, codex commands)</summary>
    CLI,
    /// <summary>OAuth/Web-based authentication (WebView login)</summary>
    OAuth,
    /// <summary>Manually provided cookie/token</summary>
    Manual,
    /// <summary>Auto-detected from local files or browser</summary>
    AutoDetect
}

/// <summary>
/// Defines the user's preferred authentication strategy per provider
/// </summary>
public enum AuthenticationStrategy
{
    /// <summary>Try all strategies in priority order (default behavior)</summary>
    Auto,
    /// <summary>Only use CLI-based strategies</summary>
    CLI,
    /// <summary>Only use OAuth/Web-based strategies</summary>
    OAuth,
    /// <summary>Only use manually configured cookies/tokens</summary>
    Manual
}

/// <summary>
/// Base interface for all provider fetch strategies
/// </summary>
public interface IProviderFetchStrategy
{
    string StrategyName { get; }
    int Priority { get; }
    /// <summary>
    /// The type of this strategy for filtering by user preference
    /// </summary>
    StrategyType Type { get; }
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

    // Window types for notification behavior
    UsageWindowType PrimaryWindowType { get; }
    UsageWindowType SecondaryWindowType { get; }

    // Dashboard URL for "View Details" action
    string? DashboardUrl { get; }

    // Fetch strategies in priority order
    IReadOnlyList<IProviderFetchStrategy> FetchStrategies { get; }

    /// <summary>
    /// Get the list of available authentication strategies for this provider
    /// </summary>
    IReadOnlyList<AuthenticationStrategy> AvailableStrategies { get; }

    /// <summary>
    /// Get strategies filtered by user's preferred authentication method
    /// </summary>
    IEnumerable<IProviderFetchStrategy> GetStrategiesForPreference(AuthenticationStrategy preference);

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

    // Window types - default to Session for primary (notify each time) and Weekly for secondary (notify once)
    public virtual UsageWindowType PrimaryWindowType => UsageWindowType.Session;
    public virtual UsageWindowType SecondaryWindowType => UsageWindowType.Weekly;

    // Dashboard URL - providers should override this
    public virtual string? DashboardUrl => null;

    public virtual bool SupportsOAuth => false;
    public virtual bool SupportsWebScraping => false;
    public virtual bool SupportsCLI => false;

    private readonly List<IProviderFetchStrategy> _strategies = new();
    public IReadOnlyList<IProviderFetchStrategy> FetchStrategies => _strategies.AsReadOnly();

    /// <summary>
    /// Get available authentication strategies based on the strategies this provider supports
    /// Always includes Auto as the first option
    /// </summary>
    public IReadOnlyList<AuthenticationStrategy> AvailableStrategies
    {
        get
        {
            var available = new List<AuthenticationStrategy> { AuthenticationStrategy.Auto };
            
            // Check what strategy types are available
            var types = _strategies.Select(s => s.Type).Distinct().ToHashSet();
            
            if (types.Contains(StrategyType.CLI))
                available.Add(AuthenticationStrategy.CLI);
            if (types.Contains(StrategyType.OAuth))
                available.Add(AuthenticationStrategy.OAuth);
            if (types.Contains(StrategyType.Manual))
                available.Add(AuthenticationStrategy.Manual);
            
            return available.AsReadOnly();
        }
    }

    /// <summary>
    /// Get strategies filtered by user's preferred authentication method
    /// </summary>
    public IEnumerable<IProviderFetchStrategy> GetStrategiesForPreference(AuthenticationStrategy preference)
    {
        if (preference == AuthenticationStrategy.Auto)
        {
            // Return all strategies in priority order
            return _strategies;
        }

        // Map AuthenticationStrategy to StrategyType(s)
        var allowedTypes = preference switch
        {
            AuthenticationStrategy.CLI => new[] { StrategyType.Cached, StrategyType.CLI },
            AuthenticationStrategy.OAuth => new[] { StrategyType.Cached, StrategyType.OAuth },
            AuthenticationStrategy.Manual => new[] { StrategyType.Cached, StrategyType.Manual },
            _ => Array.Empty<StrategyType>()
        };

        // Filter strategies by allowed types (always include Cached for performance)
        var filtered = _strategies.Where(s => allowedTypes.Contains(s.Type)).ToList();
        
        // If no strategies match, fall back to all strategies
        return filtered.Count > 0 ? filtered : _strategies;
    }

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
