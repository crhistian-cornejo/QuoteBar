using System.Collections.Concurrent;
using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Providers.Antigravity;
using NativeBar.WinUI.Core.Providers.Codex;
using NativeBar.WinUI.Core.Providers.Claude;
using NativeBar.WinUI.Core.Providers.Cursor;
using NativeBar.WinUI.Core.Providers.Droid;
using NativeBar.WinUI.Core.Providers.Gemini;
using NativeBar.WinUI.Core.Providers.Copilot;
using NativeBar.WinUI.Core.Providers.Zai;

namespace NativeBar.WinUI.Core.Providers;

/// <summary>
/// Central registry for all provider descriptors
/// </summary>
public class ProviderRegistry
{
    private static readonly Lazy<ProviderRegistry> _instance = new(() => new ProviderRegistry());
    public static ProviderRegistry Instance => _instance.Value;
    
    private readonly ConcurrentDictionary<string, IProviderDescriptor> _providers = new();
    
    private ProviderRegistry()
    {
        RegisterDefaultProviders();
    }
    
    public void Register(IProviderDescriptor descriptor)
    {
        _providers[descriptor.Id] = descriptor;
    }
    
    public IProviderDescriptor? GetProvider(string id)
    {
        _providers.TryGetValue(id, out var provider);
        return provider;
    }
    
    public IReadOnlyList<IProviderDescriptor> GetAllProviders()
    {
        return _providers.Values.ToList().AsReadOnly();
    }
    
    private void RegisterDefaultProviders()
    {
        // Register built-in providers (order matters for tab display)
        Register(new CodexProviderDescriptor());
        Register(new ClaudeProviderDescriptor());
        Register(new CursorProviderDescriptor());
        Register(new AntigravityProviderDescriptor());
        Register(new DroidProviderDescriptor());
        Register(new GeminiProviderDescriptor());
        Register(new CopilotProviderDescriptor());
        Register(new ZaiProviderDescriptor());
    }
}

/// <summary>
/// Fetches usage data from providers with fallback support
/// </summary>
public class UsageFetcher
{
    private readonly IProviderDescriptor _descriptor;
    
    public UsageFetcher(IProviderDescriptor descriptor)
    {
        _descriptor = descriptor;
    }
    
    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var strategies = _descriptor.FetchStrategies;
        
        Log($"[{_descriptor.Id}] FetchAsync: {strategies.Count} strategies available");
        
        if (strategies.Count == 0)
        {
            return new UsageSnapshot
            {
                ProviderId = _descriptor.Id,
                ErrorMessage = "No fetch strategies available",
                FetchedAt = DateTime.UtcNow
            };
        }
        
        Exception? lastException = null;
        
        foreach (var strategy in strategies)
        {
            try
            {
                Log($"[{_descriptor.Id}] Checking strategy: {strategy.StrategyName}");
                
                if (!await strategy.CanExecuteAsync())
                {
                    Log($"[{_descriptor.Id}] Strategy {strategy.StrategyName}: CanExecute=false, skipping");
                    continue;
                }
                
                Log($"[{_descriptor.Id}] Strategy {strategy.StrategyName}: Executing...");
                var snapshot = await strategy.FetchAsync(cancellationToken);
                
                if (snapshot.ErrorMessage == null)
                {
                    Log($"[{_descriptor.Id}] Strategy {strategy.StrategyName}: SUCCESS");
                    return snapshot;
                }
                
                Log($"[{_descriptor.Id}] Strategy {strategy.StrategyName}: returned error: {snapshot.ErrorMessage}");
                lastException = new Exception(snapshot.ErrorMessage);
            }
            catch (Exception ex)
            {
                Log($"[{_descriptor.Id}] Strategy {strategy.StrategyName}: EXCEPTION: {ex.Message}");
                lastException = ex;
                // Continue to next strategy
            }
        }
        
        Log($"[{_descriptor.Id}] All strategies failed, last error: {lastException?.Message}");
        
        return new UsageSnapshot
        {
            ProviderId = _descriptor.Id,
            ErrorMessage = lastException?.Message ?? "All fetch strategies failed",
            FetchedAt = DateTime.UtcNow
        };
    }
    
    private static void Log(string message)
    {
        Core.Services.DebugLogger.Log("UsageFetcher", message);
    }
}
