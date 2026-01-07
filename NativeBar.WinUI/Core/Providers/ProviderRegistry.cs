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
using NativeBar.WinUI.Core.Providers.Augment;
using NativeBar.WinUI.Core.Providers.MiniMax;

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
        // Return providers sorted alphabetically by display name
        return _providers.Values
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }
    
    private void RegisterDefaultProviders()
    {
        // Register built-in providers (alphabetical order)
        Register(new AntigravityProviderDescriptor());
        Register(new AugmentProviderDescriptor());
        Register(new ClaudeProviderDescriptor());
        Register(new CodexProviderDescriptor());
        Register(new CopilotProviderDescriptor());
        Register(new CursorProviderDescriptor());
        Register(new DroidProviderDescriptor());
        Register(new GeminiProviderDescriptor());
        Register(new MiniMaxProviderDescriptor());
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
        var allStrategiesSkipped = true;
        var skippedStrategies = new List<string>();

        foreach (var strategy in strategies)
        {
            try
            {
                Log($"[{_descriptor.Id}] Checking strategy: {strategy.StrategyName}");

                if (!await strategy.CanExecuteAsync())
                {
                    Log($"[{_descriptor.Id}] Strategy {strategy.StrategyName}: CanExecute=false, skipping");
                    skippedStrategies.Add(strategy.StrategyName);
                    continue;
                }

                allStrategiesSkipped = false;
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
                allStrategiesSkipped = false;
                Log($"[{_descriptor.Id}] Strategy {strategy.StrategyName}: EXCEPTION: {ex.Message}");
                lastException = ex;
                // Continue to next strategy
            }
        }

        Log($"[{_descriptor.Id}] All strategies failed, last error: {lastException?.Message}");

        // If all strategies were skipped (none could execute), provide helpful message
        string errorMessage;
        if (allStrategiesSkipped)
        {
            errorMessage = GetNoCredentialsMessage(_descriptor.Id);
        }
        else
        {
            errorMessage = lastException?.Message ?? "All fetch strategies failed";
        }

        return new UsageSnapshot
        {
            ProviderId = _descriptor.Id,
            ErrorMessage = errorMessage,
            FetchedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Get a user-friendly message when no credentials are available
    /// </summary>
    private static string GetNoCredentialsMessage(string providerId)
    {
        return providerId.ToLower() switch
        {
            "claude" => "Not authenticated. Run 'claude' CLI to login.",
            "codex" => "Not authenticated. Run 'codex auth login' to login.",
            "gemini" => "Not authenticated. Run 'gemini auth login' to login.",
            "copilot" => "Not authenticated. Click 'Connect' to sign in with GitHub.",
            "cursor" => "Not authenticated. Click 'Connect' to sign in.",
            "droid" => "Not authenticated. Click 'Connect' to sign in.",
            "antigravity" => "Not detected. Launch Antigravity IDE and sign in.",
            "zai" => "No API token. Enter your z.ai API token in Settings.",
            "minimax" => "No cookie configured. Paste your MiniMax cookie header in Settings.",
            "augment" => "Not authenticated. Configure cookie in Settings or log in at app.augmentcode.com.",
            _ => "Not configured. Set up this provider in Settings."
        };
    }
    
    private static void Log(string message)
    {
        Core.Services.DebugLogger.Log("UsageFetcher", message);
    }
}
