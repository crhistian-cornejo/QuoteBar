using System.Collections.Concurrent;
using QuoteBar.Core.Models;
using QuoteBar.Core.Providers.Antigravity;
using QuoteBar.Core.Providers.Codex;
using QuoteBar.Core.Providers.Claude;
using QuoteBar.Core.Providers.Cursor;
using QuoteBar.Core.Providers.Droid;
using QuoteBar.Core.Providers.Gemini;
using QuoteBar.Core.Providers.Copilot;
using QuoteBar.Core.Providers.Zai;
using QuoteBar.Core.Providers.Augment;
using QuoteBar.Core.Providers.MiniMax;
using QuoteBar.Core.Services;

namespace QuoteBar.Core.Providers;

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
        var settings = Core.Services.SettingsService.Instance;

        if (settings.Settings.ProviderOrder.Count > 0)
        {
            var ordered = new List<IProviderDescriptor>();

            foreach (var providerId in settings.Settings.ProviderOrder)
            {
                if (_providers.TryGetValue(providerId, out var provider))
                {
                    ordered.Add(provider);
                }
            }

            foreach (var provider in _providers.Values)
            {
                if (!settings.Settings.ProviderOrder.Contains(provider.Id))
                {
                    ordered.Add(provider);
                }
            }

            return ordered.AsReadOnly();
        }

        return _providers.Values
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    public void SaveProviderOrder(List<string> providerIds)
    {
        var settings = Core.Services.SettingsService.Instance;
        settings.Settings.ProviderOrder = providerIds;
        settings.Save();
    }

    private void RegisterDefaultProviders()
    {
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
        // Get user's preferred strategy from settings
        var config = Core.Services.SettingsService.Instance.Settings.GetProviderConfig(_descriptor.Id);
        var preferredStrategy = ParseAuthenticationStrategy(config.PreferredStrategy);
        
        // Filter strategies based on user preference
        var strategies = _descriptor.GetStrategiesForPreference(preferredStrategy).ToList();

        Log($"[{_descriptor.Id}] FetchAsync: {strategies.Count} strategies available (preference: {preferredStrategy})");

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
                Log($"[{_descriptor.Id}] Checking strategy: {strategy.StrategyName} (type: {strategy.Type})");

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
            errorMessage = GetNoCredentialsMessage(_descriptor.Id, preferredStrategy);
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
    /// Parse the string preference to enum
    /// </summary>
    private static AuthenticationStrategy ParseAuthenticationStrategy(string? preference)
    {
        if (string.IsNullOrEmpty(preference))
            return AuthenticationStrategy.Auto;

        return preference.ToLowerInvariant() switch
        {
            "cli" => AuthenticationStrategy.CLI,
            "oauth" => AuthenticationStrategy.OAuth,
            "manual" => AuthenticationStrategy.Manual,
            _ => AuthenticationStrategy.Auto
        };
    }

    /// <summary>
    /// Get a user-friendly message when no credentials are available
    /// </summary>
    private static string GetNoCredentialsMessage(string providerId, AuthenticationStrategy preference)
    {
        // Add context about the selected strategy
        var strategyHint = preference switch
        {
            AuthenticationStrategy.CLI => " (CLI mode selected)",
            AuthenticationStrategy.OAuth => " (OAuth mode selected)",
            AuthenticationStrategy.Manual => " (Manual mode selected)",
            _ => ""
        };

        var baseMessage = providerId.ToLower() switch
        {
            "claude" => preference switch
            {
                AuthenticationStrategy.CLI => "CLI not available. Run 'claude' to login.",
                AuthenticationStrategy.OAuth => "Not authenticated. Run 'claude' to get OAuth credentials.",
                _ => "Not authenticated. Run 'claude' CLI to login."
            },
            "codex" => "Not authenticated. Run 'codex auth login' to login.",
            "gemini" => "Not authenticated. Run 'gemini auth login' to login.",
            "copilot" => preference switch
            {
                AuthenticationStrategy.OAuth => "Not authenticated. Click 'Connect' to sign in with GitHub.",
                _ => "Not authenticated. Click 'Connect' to sign in with GitHub."
            },
            "cursor" => preference switch
            {
                AuthenticationStrategy.OAuth => "Not authenticated. Click 'Connect' to sign in.",
                AuthenticationStrategy.Manual => "No cookie configured. Paste your cookie header in Settings.",
                _ => "Not authenticated. Click 'Connect' to sign in."
            },
            "droid" => "Not authenticated. Click 'Connect' to sign in.",
            "antigravity" => "Not detected. Launch Antigravity IDE and sign in.",
            "zai" => "No API token. Enter your z.ai API token in Settings.",
            "minimax" => "No cookie configured. Paste your MiniMax cookie header in Settings.",
            "augment" => preference switch
            {
                AuthenticationStrategy.Manual => "No cookie configured. Paste your Augment cookie in Settings.",
                AuthenticationStrategy.OAuth => "Not authenticated. Click 'Connect' to sign in.",
                _ => "Not authenticated. Configure cookie in Settings or log in at app.augmentcode.com."
            },
            _ => "Not configured. Set up this provider in Settings."
        };

        return preference == AuthenticationStrategy.Auto ? baseMessage : baseMessage + strategyHint;
    }
    
    private static void Log(string message)
    {
        Core.Services.DebugLogger.Log("UsageFetcher", message);
    }
}
