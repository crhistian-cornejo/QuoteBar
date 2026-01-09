using QuoteBar.Core.Models;
using QuoteBar.Core.Services;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace QuoteBar.Core.Providers.Codex;

public class CodexProviderDescriptor : ProviderDescriptor
{
    public override string Id => "codex";
    public override string DisplayName => "Codex";
    public override string IconGlyph => "\uE943"; // Code icon
    public override string PrimaryColor => "#7C3AED";
    public override string SecondaryColor => "#A78BFA";
    public override string PrimaryLabel => "Session";
    public override string SecondaryLabel => "Weekly";
    public override string? TertiaryLabel => "Sonnet";
    public override string? DashboardUrl => "https://chatgpt.com/codex/settings/usage";

    // Session resets every 5h, notify each time; Weekly notify only once
    public override UsageWindowType PrimaryWindowType => UsageWindowType.Session;
    public override UsageWindowType SecondaryWindowType => UsageWindowType.Weekly;

    public override bool SupportsOAuth => true;
    public override bool SupportsCLI => true;

    protected override void InitializeStrategies()
    {
        // OAuth/Credentials has higher priority (2) - try first
        AddStrategy(new CodexOAuthStrategy());
        // CLI is fallback (1)
        AddStrategy(new CodexCLIStrategy());
        // RPC is last resort (0)
        AddStrategy(new CodexRPCStrategy());
    }
}

/// <summary>
/// OAuth strategy for Codex using stored credentials
/// This is the preferred method when credentials are available
/// </summary>
public class CodexOAuthStrategy : IProviderFetchStrategy
{
    public string StrategyName => "OAuth";
    public int Priority => 3; // Highest priority - try first
    public StrategyType Type => StrategyType.OAuth;

    private CodexOAuthCredentials? _cachedCredentials;

    public Task<bool> CanExecuteAsync()
    {
        try
        {
            // First check if we have stored credentials
            if (CodexCredentialsStore.HasCredentials())
            {
                _cachedCredentials = CodexCredentialsStore.TryLoad();
                if (_cachedCredentials != null && _cachedCredentials.IsValid)
                {
                    DebugLogger.Log("CodexOAuthStrategy", $"CanExecute: Found valid credentials");
                    return Task.FromResult(true);
                }
            }

            // If no stored credentials, check if CLI is authenticated by running a quick command
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CodexOAuthStrategy", "CanExecute error", ex);
            return Task.FromResult(false);
        }
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Use cached credentials or reload
            var credentials = _cachedCredentials ?? CodexCredentialsStore.TryLoad();
            
            if (credentials == null || !credentials.IsValid)
            {
                // If we have credentials but they're expired
                if (credentials != null && credentials.IsExpired)
                {
                    return new UsageSnapshot
                    {
                        ProviderId = "codex",
                        ErrorMessage = "Codex credentials expired. Run `codex auth login` to re-authenticate.",
                        FetchedAt = DateTime.UtcNow
                    };
                }

                // Fallback to CLI strategy
                var cliStrategy = new CodexCLIStrategy();
                if (await cliStrategy.CanExecuteAsync())
                {
                    return await cliStrategy.FetchAsync(cancellationToken);
                }

                return new UsageSnapshot
                {
                    ProviderId = "codex",
                    ErrorMessage = "No Codex credentials available. Run `codex auth login` to authenticate.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            // We have valid credentials - use CLI to fetch usage since Codex doesn't have a direct API
            var strategy = new CodexCLIStrategy();
            if (await strategy.CanExecuteAsync())
            {
                return await strategy.FetchAsync(cancellationToken);
            }

            // CLI not available - check plan and return appropriate response
            var planType = credentials.PlanType?.ToLowerInvariant();
            var isFreePlan = planType == "free" || string.IsNullOrEmpty(planType);
            
            if (isFreePlan)
            {
                return new UsageSnapshot
                {
                    ProviderId = "codex",
                    ErrorMessage = "Codex requires ChatGPT Plus or Pro. Upgrade your plan to use Codex.",
                    RequiresUpgrade = true,
                    UpgradeUrl = "https://openai.com/chatgpt/pricing",
                    Identity = new ProviderIdentity
                    {
                        PlanType = credentials.DisplayPlanType,
                        Email = credentials.Email
                    },
                    FetchedAt = DateTime.UtcNow
                };
            }

            // Return a basic snapshot showing we're authenticated with a supported plan
            return new UsageSnapshot
            {
                ProviderId = "codex",
                Primary = new RateWindow
                {
                    UsedPercent = 0,
                    WindowMinutes = 300,
                    ResetDescription = "in 5 hours"
                },
                Identity = new ProviderIdentity
                {
                    PlanType = credentials.DisplayPlanType,
                    Email = credentials.Email
                },
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (CodexCredentialsException ex)
        {
            DebugLogger.LogError("CodexOAuthStrategy", $"Credentials error", ex);

            return new UsageSnapshot
            {
                ProviderId = "codex",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CodexOAuthStrategy", "Fetch error", ex);

            return new UsageSnapshot
            {
                ProviderId = "codex",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }
}

/// <summary>
/// CLI strategy for Codex - verifies CLI is installed and uses credentials from auth.json
/// Note: Codex CLI does not have a 'usage' or 'status' command, so we rely on the JWT in auth.json
/// </summary>
public class CodexCLIStrategy : IProviderFetchStrategy
{
    public string StrategyName => "CLI";
    public int Priority => 2; // Medium priority - after OAuth, before RPC
    public StrategyType Type => StrategyType.CLI;

    public async Task<bool> CanExecuteAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "codex",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Codex CLI doesn't have a usage/status command
            // We read the plan info from the JWT stored in ~/.codex/auth.json
            var credentials = CodexCredentialsStore.TryLoad();
            
            if (credentials == null)
            {
                return Task.FromResult(new UsageSnapshot
                {
                    ProviderId = "codex",
                    ErrorMessage = "Codex credentials not found. Run 'codex login' to authenticate.",
                    FetchedAt = DateTime.UtcNow
                });
            }
            
            if (!credentials.IsValid)
            {
                return Task.FromResult(new UsageSnapshot
                {
                    ProviderId = "codex",
                    ErrorMessage = credentials.IsExpired 
                        ? "Codex credentials expired. Run 'codex login' to re-authenticate."
                        : "Invalid Codex credentials. Run 'codex login' to authenticate.",
                    FetchedAt = DateTime.UtcNow
                });
            }
            
            // Check if plan supports Codex - Free plan does NOT have Codex access
            var planType = credentials.PlanType?.ToLowerInvariant();
            var supportedPlans = new[] { "plus", "pro", "team", "enterprise" };
            var isFreePlan = planType == "free" || string.IsNullOrEmpty(planType);
            
            if (isFreePlan)
            {
                DebugLogger.Log("CodexCLIStrategy", $"Plan '{planType ?? "unknown"}' does not support Codex, showing upgrade message");
                
                return Task.FromResult(new UsageSnapshot
                {
                    ProviderId = "codex",
                    ErrorMessage = "Codex requires ChatGPT Plus or Pro. Upgrade your plan to use Codex.",
                    RequiresUpgrade = true,
                    UpgradeUrl = "https://openai.com/chatgpt/pricing",
                    Identity = new ProviderIdentity
                    {
                        PlanType = credentials.DisplayPlanType,
                        Email = credentials.Email
                    },
                    FetchedAt = DateTime.UtcNow
                });
            }
            
            // Plan supports Codex - show usage info
            // Note: Codex doesn't expose usage/quota API, so we can only show the plan type
            // The usage percentages would need to come from intercepting actual API calls
            return Task.FromResult(new UsageSnapshot
            {
                ProviderId = "codex",
                Primary = new RateWindow
                {
                    UsedPercent = 0, // Unknown - Codex doesn't expose this
                    WindowMinutes = 300, // 5-hour session window
                    ResetDescription = "Usage tracking via requests"
                },
                Identity = new ProviderIdentity
                {
                    PlanType = credentials.DisplayPlanType,
                    Email = credentials.Email
                },
                FetchedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CodexCLIStrategy", $"ERROR: {ex.Message}", ex);

            return Task.FromResult(new UsageSnapshot
            {
                ProviderId = "codex",
                ErrorMessage = $"Codex error: {ex.Message}",
                FetchedAt = DateTime.UtcNow
            });
        }
    }
}

/// <summary>
/// RPC strategy communicating with Codex daemon via JSON-RPC
/// </summary>
public class CodexRPCStrategy : IProviderFetchStrategy
{
    public string StrategyName => "RPC";
    public int Priority => 1; // Lowest priority - fallback only
    public StrategyType Type => StrategyType.AutoDetect;

    public async Task<bool> CanExecuteAsync()
    {
        // RPC strategy should only be used if CLI strategy is not available
        // Check if CLI strategy can execute first
        var cliStrategy = new CodexCLIStrategy();
        if (await cliStrategy.CanExecuteAsync())
        {
            // CLI is available, don't use RPC
            return false;
        }

        // Check if codex daemon is running (only if CLI is not available)
        try
        {
            var processes = Process.GetProcessesByName("codex");
            if (processes.Length == 0)
            {
                // Try codex-cli or codex-daemon
                processes = Process.GetProcessesByName("codex-cli");
            }
            return await Task.FromResult(processes.Length > 0);
        }
        catch
        {
            return false;
        }
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to connect to codex RPC endpoint
            // The codex CLI typically listens on a local socket or HTTP endpoint

            // Method 1: Try HTTP API if available
            var httpResult = await TryHttpApiAsync(cancellationToken);
            if (httpResult != null) return httpResult;

            // Method 2: Spawn codex app-server for RPC
            var rpcResult = await TryRpcAsync(cancellationToken);
            if (rpcResult != null) return rpcResult;

            return new UsageSnapshot
            {
                ProviderId = "codex",
                ErrorMessage = "Failed to connect to codex RPC",
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new UsageSnapshot
            {
                ProviderId = "codex",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private async Task<UsageSnapshot?> TryHttpApiAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

            // Try common codex API endpoints
            var endpoints = new[]
            {
                "http://localhost:8080/api/usage",
                "http://localhost:3000/api/usage",
                "http://127.0.0.1:8080/api/usage"
            };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    var response = await httpClient.GetAsync(endpoint, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync(cancellationToken);
                        return ParseJsonResponse(json);
                    }
                }
                catch
                {
                    // Try next endpoint
                }
            }
        }
        catch { }

        return null;
    }

    private async Task<UsageSnapshot?> TryRpcAsync(CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "codex",
                Arguments = "app-server",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            // Send JSON-RPC request
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "get_usage",
                @params = new { },
                id = 1
            });

            await process.StandardInput.WriteLineAsync(request);
            await process.StandardInput.FlushAsync();

            // Wait for response with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await process.StandardOutput.ReadLineAsync(cts.Token);

            process.Kill();

            if (!string.IsNullOrEmpty(response))
            {
                return ParseJsonResponse(response);
            }
        }
        catch { }

        return null;
    }

    private UsageSnapshot ParseJsonResponse(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Handle JSON-RPC response wrapper
        if (root.TryGetProperty("result", out var result))
        {
            root = result;
        }

        RateWindow? primary = null;
        RateWindow? secondary = null;

        if (root.TryGetProperty("session", out var session))
        {
            primary = new RateWindow
            {
                UsedPercent = session.TryGetProperty("percent", out var p) ? p.GetDouble() : 0,
                WindowMinutes = 300
            };
        }

        if (root.TryGetProperty("weekly", out var weekly))
        {
            secondary = new RateWindow
            {
                UsedPercent = weekly.TryGetProperty("percent", out var p) ? p.GetDouble() : 0,
                WindowMinutes = 10080
            };
        }

        return new UsageSnapshot
        {
            ProviderId = "codex",
            Primary = primary ?? new RateWindow { UsedPercent = 0, WindowMinutes = 300 },
            Secondary = secondary,
            Identity = new ProviderIdentity { PlanType = "Max" },
            FetchedAt = DateTime.UtcNow
        };
    }
}
