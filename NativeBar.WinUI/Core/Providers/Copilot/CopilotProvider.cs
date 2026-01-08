using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Services;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NativeBar.WinUI.Core.Providers.Copilot;

public class CopilotProviderDescriptor : ProviderDescriptor
{
    public override string Id => "copilot";
    public override string DisplayName => "Copilot";
    public override string IconGlyph => "\uE99A"; // Robot icon
    public override string PrimaryColor => "#24292F";
    public override string SecondaryColor => "#57606A";
    public override string PrimaryLabel => "Premium Requests";
    public override string SecondaryLabel => "Top Model";
    public override string? TertiaryLabel => "2nd Model";
public override string? DashboardUrl => "https://github.com/settings/copilot";

    public override bool SupportsOAuth => true;
    public override bool SupportsCLI => true;

    protected override void InitializeStrategies()
    {
        // OAuth has higher priority (2) - try first
        AddStrategy(new CopilotOAuthStrategy());
        // CLI is fallback (1) - uses gh CLI
        AddStrategy(new CopilotCLIStrategy());
    }
}

/// <summary>
/// OAuth strategy for GitHub Copilot using GitHub's OAuth tokens
/// This is the preferred method - uses our Device Flow OAuth or GitHub CLI token
/// </summary>
public class CopilotOAuthStrategy : IProviderFetchStrategy
{
    public string StrategyName => "OAuth";
    public int Priority => 2; // Higher priority - try first
    public StrategyType Type => StrategyType.OAuth;

    public Task<bool> CanExecuteAsync()
    {
        try
        {
            // Check if we have a token from any source
            var hasToken = CopilotTokenStore.HasToken();
            Log($"CanExecute: HasToken={hasToken}");
            return Task.FromResult(hasToken);
        }
        catch (Exception ex)
        {
            Log($"CanExecute ERROR: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var token = CopilotTokenStore.GetToken();
            if (string.IsNullOrEmpty(token))
            {
                return new UsageSnapshot
                {
                    ProviderId = "copilot",
                    ErrorMessage = "No GitHub credentials available. Click 'Connect' to sign in.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            // Use the new FetchUsageSnapshotAsync which tries internal API first
            return await CopilotUsageFetcher.FetchUsageSnapshotAsync(token, cancellationToken);
        }
        catch (CopilotFetchException ex)
        {
            Log($"Fetch ERROR: {ex.ErrorType} - {ex.Message}");

            return new UsageSnapshot
            {
                ProviderId = "copilot",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            Log($"Fetch ERROR: {ex.Message}\n{ex.StackTrace}");

            return new UsageSnapshot
            {
                ProviderId = "copilot",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private void Log(string message)
    {
        DebugLogger.Log("CopilotOAuthStrategy", message);
    }
}

/// <summary>
/// CLI strategy for GitHub Copilot using the gh command
/// Fallback method when OAuth credentials are not available
/// </summary>
public class CopilotCLIStrategy : IProviderFetchStrategy
{
    public string StrategyName => "CLI";
    public int Priority => 1; // Lower priority - fallback
    public StrategyType Type => StrategyType.CLI;

    public async Task<bool> CanExecuteAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = "auth status",
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

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Get username first
            var userOutput = await RunGhCommandAsync("api user -q .login", cancellationToken);
            var username = userOutput?.Trim();

            if (string.IsNullOrEmpty(username))
            {
                return new UsageSnapshot
                {
                    ProviderId = "copilot",
                    ErrorMessage = "Failed to get username from gh CLI. Run 'gh auth login' to authenticate.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            // Try to get premium request usage via gh api
            var premiumUsageJson = await RunGhCommandAsync(
                $"api /users/{username}/settings/billing/premium_request/usage", 
                cancellationToken);

            return ParsePremiumUsage(premiumUsageJson, username);
        }
        catch (Exception ex)
        {
            Log($"ERROR: {ex.Message}\n{ex.StackTrace}");

            return new UsageSnapshot
            {
                ProviderId = "copilot",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private async Task<string> RunGhCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            Log($"Running: gh {arguments}");

            using var process = Process.Start(startInfo);
            if (process == null) return string.Empty;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var error = await process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            Log($"gh {arguments} output:\n{output}\nstderr: {error}");

            // gh auth status outputs to stderr, so combine them
            return string.IsNullOrEmpty(output) ? error : output;
        }
        catch (Exception ex)
        {
            Log($"RunGhCommand error: {ex.Message}");
            return string.Empty;
        }
    }

    private UsageSnapshot ParsePremiumUsage(string jsonOutput, string username)
    {
        RateWindow? primary = null;
        ProviderIdentity? identity = null;
        double totalUsed = 0;
        double totalBilled = 0;

        if (!string.IsNullOrEmpty(jsonOutput))
        {
            try
            {
                var doc = JsonDocument.Parse(jsonOutput);
                var root = doc.RootElement;

                if (root.TryGetProperty("usageItems", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.TryGetProperty("product", out var product) &&
                            product.GetString()?.Equals("Copilot", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            if (item.TryGetProperty("grossQuantity", out var qty))
                            {
                                totalUsed += qty.GetDouble();
                            }
                            if (item.TryGetProperty("netAmount", out var amt))
                            {
                                totalBilled += amt.GetDouble();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Failed to parse premium usage JSON: {ex.Message}");
            }
        }

        // Detect plan and limit
        int limit = 1500; // Default to Pro+ limit
        string planType = "Copilot Pro+";

        if (totalUsed <= 50 && totalBilled == 0)
        {
            planType = "Copilot Pro";
            limit = 300;
        }
        else if (totalUsed <= 300 && totalBilled == 0)
        {
            planType = "Copilot Pro";
            limit = 300;
        }
        else if (totalUsed > 300 && totalBilled == 0)
        {
            planType = "Copilot Pro+";
            limit = 1500;
        }

        double usedPercent = limit > 0 ? (totalUsed / limit) * 100 : 0;
        if (usedPercent > 100) usedPercent = 100;

        // Calculate reset date
        var now = DateTime.UtcNow;
        var nextMonth = now.AddMonths(1);
        var resetsAt = new DateTime(nextMonth.Year, nextMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var daysUntilReset = (int)(resetsAt - now).TotalDays;

        primary = new RateWindow
        {
            UsedPercent = usedPercent,
            Used = totalUsed,
            Limit = limit,
            WindowMinutes = null,
            ResetsAt = resetsAt,
            ResetDescription = $"Resets in {daysUntilReset}d",
            Unit = "premium requests"
        };

        identity = new ProviderIdentity
        {
            PlanType = planType,
            AccountId = username
        };

        return new UsageSnapshot
        {
            ProviderId = "copilot",
            Primary = primary,
            Identity = identity,
            FetchedAt = DateTime.UtcNow
        };
    }

    private void Log(string message)
    {
        DebugLogger.Log("CopilotCLIStrategy", message);
    }
}

/// <summary>
/// Helper class to launch Copilot OAuth login via Device Flow.
/// </summary>
public static class CopilotLoginHelper
{
    private static NativeBar.WinUI.Views.CopilotLoginWindow? _currentWindow;
    private static readonly object _lock = new();

    /// <summary>
    /// Check if user is signed in
    /// </summary>
    public static bool IsSignedIn => CopilotTokenStore.HasToken();

    /// <summary>
    /// Check if the login window is currently open
    /// </summary>
    public static bool IsLoginWindowOpen
    {
        get
        {
            lock (_lock)
            {
                return _currentWindow != null;
            }
        }
    }

    /// <summary>
    /// Launch the OAuth Device Flow login window.
    /// Returns the result of the login attempt.
    /// </summary>
    public static async Task<NativeBar.WinUI.Views.CopilotLoginResult> LaunchLoginAsync()
    {
        lock (_lock)
        {
            if (_currentWindow != null)
            {
                Log("Login window already open");
                return NativeBar.WinUI.Views.CopilotLoginResult.Cancelled();
            }
        }

        try
        {
            Log("Launching Copilot OAuth Device Flow login window");

            var window = new NativeBar.WinUI.Views.CopilotLoginWindow();

            lock (_lock)
            {
                _currentWindow = window;
            }

            var result = await window.ShowLoginAsync();

            Log($"Login completed: Success={result.IsSuccess}, Cancelled={result.IsCancelled}");

            return result;
        }
        finally
        {
            lock (_lock)
            {
                _currentWindow = null;
            }
        }
    }

    /// <summary>
    /// Sign out by clearing the stored token
    /// </summary>
    public static void SignOut()
    {
        CopilotTokenStore.ClearToken();
        Log("Signed out");
    }

    private static void Log(string message)
    {
        DebugLogger.Log("CopilotLoginHelper", message);
    }
}
