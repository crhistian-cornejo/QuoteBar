using QuoteBar.Core.Models;
using QuoteBar.Core.Services;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace QuoteBar.Core.Providers.Claude;

public class ClaudeProviderDescriptor : ProviderDescriptor
{
    public override string Id => "claude";
    public override string DisplayName => "Claude";
    public override string IconGlyph => "\uE8F1"; // PersonVoice icon
    public override string PrimaryColor => "#D97757";
    public override string SecondaryColor => "#F4A582";
    public override string PrimaryLabel => "Session";
    public override string SecondaryLabel => "Weekly";
    public override string? TertiaryLabel => "Sonnet";

    // Session resets every 5h, notify each time; Weekly notify only once
    public override UsageWindowType PrimaryWindowType => UsageWindowType.Session;
    public override UsageWindowType SecondaryWindowType => UsageWindowType.Weekly;

    public override string? DashboardUrl => "https://claude.ai/settings/usage";

    public override bool SupportsOAuth => true;
    public override bool SupportsCLI => true;

    protected override void InitializeStrategies()
    {
        // OAuth has higher priority (2) - try first
        AddStrategy(new ClaudeOAuthStrategy());
        // CLI is fallback (1)
        AddStrategy(new ClaudeCLIStrategy());
    }
}

/// <summary>
/// OAuth strategy for Claude using the official OAuth API
/// This is the preferred method when credentials are available
/// </summary>
public class ClaudeOAuthStrategy : IProviderFetchStrategy
{
    public string StrategyName => "OAuth";
    public int Priority => 2; // Higher priority - try first
    public StrategyType Type => StrategyType.OAuth;

    private ClaudeOAuthCredentials? _cachedCredentials;

    public Task<bool> CanExecuteAsync()
    {
        try
        {
            _cachedCredentials = ClaudeOAuthCredentialsStore.TryLoad();
            if (_cachedCredentials == null)
            {
                DebugLogger.Log("ClaudeOAuthStrategy", "CanExecute: No credentials found");
                return Task.FromResult(false);
            }

            if (_cachedCredentials.IsExpired)
            {
                DebugLogger.Log("ClaudeOAuthStrategy", $"CanExecute: Credentials expired at {_cachedCredentials.ExpiresAt}");
                // TODO: Implement token refresh
                return Task.FromResult(false);
            }

            DebugLogger.Log("ClaudeOAuthStrategy", $"CanExecute: Found valid credentials, tier={_cachedCredentials.RateLimitTier}, scopes=[{string.Join(", ", _cachedCredentials.Scopes)}]");

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ClaudeOAuthStrategy", "CanExecute error", ex);
            return Task.FromResult(false);
        }
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Use cached credentials or reload
            var credentials = _cachedCredentials ?? ClaudeOAuthCredentialsStore.TryLoad();
            if (credentials == null)
            {
                return new UsageSnapshot
                {
                    ProviderId = "claude",
                    ErrorMessage = "No OAuth credentials available. Run `claude` to authenticate.",
                    RequiresReauth = true,
                    FetchedAt = DateTime.UtcNow
                };
            }

            if (credentials.IsExpired)
            {
                return new UsageSnapshot
                {
                    ProviderId = "claude",
                    ErrorMessage = "OAuth token expired. Run `claude` to re-authenticate.",
                    RequiresReauth = true,
                    FetchedAt = DateTime.UtcNow
                };
            }

            // Fetch usage from OAuth API
            var response = await ClaudeOAuthUsageFetcher.FetchUsageAsync(
                credentials.AccessToken,
                cancellationToken);

            // Convert to UsageSnapshot
            return ClaudeOAuthUsageFetcher.ToUsageSnapshot(response, credentials);
        }
        catch (ClaudeOAuthFetchException ex)
        {
            DebugLogger.LogError("ClaudeOAuthStrategy", $"Fetch error: {ex.ErrorType}", ex);

            // Set RequiresReauth for authentication errors
            var requiresReauth = ex.ErrorType == ClaudeOAuthFetchError.Unauthorized;

            return new UsageSnapshot
            {
                ProviderId = "claude",
                ErrorMessage = ex.Message,
                RequiresReauth = requiresReauth,
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ClaudeOAuthStrategy", "Fetch error", ex);

            return new UsageSnapshot
            {
                ProviderId = "claude",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }
}

/// <summary>
/// CLI strategy for Claude Code using the claude command with PTY
/// Fallback method when OAuth is not available
/// </summary>
public class ClaudeCLIStrategy : IProviderFetchStrategy
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
                FileName = "claude",
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

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Use PTY-like approach: run claude in print mode to get usage
            var usageOutput = await RunClaudeWithPtyAsync(cancellationToken);

            if (string.IsNullOrEmpty(usageOutput))
            {
                return new UsageSnapshot
                {
                    ProviderId = "claude",
                    ErrorMessage = "Failed to get usage from claude CLI",
                    FetchedAt = DateTime.UtcNow
                };
            }

            return ParseClaudeOutput(usageOutput);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ClaudeCLIStrategy", "Fetch error", ex);

            return new UsageSnapshot
            {
                ProviderId = "claude",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Run claude CLI with PTY-like approach using print mode
    /// This mimics running the CLI interactively and typing /usage
    /// </summary>
    private async Task<string> RunClaudeWithPtyAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Method 1: Try using --print flag with /usage command
            var startInfo = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = "--print \"/usage\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                Environment =
                {
                    // Force non-interactive mode
                    ["NO_COLOR"] = "1",
                    ["TERM"] = "dumb"
                }
            };

            DebugLogger.Log("ClaudeCLIStrategy", "Running claude --print \"/usage\"");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                // Fall back to direct usage command
                return await RunClaudeCommandAsync("usage", cancellationToken);
            }

            // Set timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var error = await process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            DebugLogger.Log("ClaudeCLIStrategy", $"--print output: {output.Substring(0, Math.Min(200, output.Length))}...");

            // Strip ANSI codes
            output = StripAnsiCodes(output);

            if (!string.IsNullOrEmpty(output) && output.Contains("%"))
            {
                return output;
            }

            // Fallback: try direct command
            return await RunClaudeCommandAsync("usage", cancellationToken);
        }
        catch (Exception ex)
        {
            DebugLogger.Log("ClaudeCLIStrategy", $"PTY failed: {ex.Message}, falling back to direct");
            return await RunClaudeCommandAsync("usage", cancellationToken);
        }
    }

    private async Task<string> RunClaudeCommandAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                Environment =
                {
                    ["NO_COLOR"] = "1",
                    ["TERM"] = "dumb"
                }
            };

            using var process = Process.Start(startInfo);
            if (process == null) return string.Empty;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            var output = await process.StandardOutput.ReadToEndAsync(cts.Token);
            var error = await process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            DebugLogger.Log("ClaudeCLIStrategy", $"claude {command} output: {output.Substring(0, Math.Min(200, output.Length))}...");

            return StripAnsiCodes(output);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Strip ANSI escape codes from output
    /// </summary>
    private static string StripAnsiCodes(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Remove ANSI escape sequences
        return Regex.Replace(text, @"\x1B\[[0-9;]*[a-zA-Z]|\x1B\].*?\x07|\x1B[PX^_].*?\x1B\\", "");
    }

    private UsageSnapshot ParseClaudeOutput(string output)
    {
        // Try JSON parsing first
        try
        {
            if (output.TrimStart().StartsWith("{") || output.TrimStart().StartsWith("["))
            {
                return ParseJsonOutput(output);
            }
        }
        catch { }

        // Parse plain text output
        return ParsePlainTextOutput(output);
    }

    private UsageSnapshot ParseJsonOutput(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        RateWindow? primary = null;
        RateWindow? secondary = null;
        RateWindow? tertiary = null;
        ProviderIdentity? identity = null;

        // Parse session/5-hour window
        if (root.TryGetProperty("session", out var session) ||
            root.TryGetProperty("5hour", out session) ||
            root.TryGetProperty("primary", out session) ||
            root.TryGetProperty("five_hour", out session))
        {
            primary = ParseRateWindowFromJson(session, 300);
        }

        // Parse weekly limit
        if (root.TryGetProperty("weekly", out var weekly) ||
            root.TryGetProperty("secondary", out weekly) ||
            root.TryGetProperty("seven_day", out weekly))
        {
            secondary = ParseRateWindowFromJson(weekly, 10080);
        }

        // Parse Sonnet/tertiary
        if (root.TryGetProperty("sonnet", out var sonnet) ||
            root.TryGetProperty("tertiary", out sonnet) ||
            root.TryGetProperty("seven_day_sonnet", out sonnet))
        {
            tertiary = ParseRateWindowFromJson(sonnet, null);
        }

        // Parse identity
        if (root.TryGetProperty("plan", out var plan))
        {
            identity = new ProviderIdentity
            {
                PlanType = plan.GetString() ?? "Max"
            };
        }

        return new UsageSnapshot
        {
            ProviderId = "claude",
            Primary = primary ?? new RateWindow { UsedPercent = 0, WindowMinutes = 300 },
            Secondary = secondary,
            Tertiary = tertiary,
            Identity = identity ?? new ProviderIdentity { PlanType = "Max" },
            FetchedAt = DateTime.UtcNow
        };
    }

    private RateWindow ParseRateWindowFromJson(JsonElement element, int? defaultWindowMinutes)
    {
        double usedPercent = 0;
        double? used = null;
        double? limit = null;
        DateTime? resetsAt = null;
        string? resetDescription = null;

        if (element.TryGetProperty("usedPercent", out var usedPercentEl) ||
            element.TryGetProperty("percent", out usedPercentEl) ||
            element.TryGetProperty("utilization", out usedPercentEl))
        {
            var val = usedPercentEl.GetDouble();
            // Check if it's a fraction (0-1) or percentage (0-100)
            usedPercent = val <= 1.0 ? val * 100 : val;
        }

        if (element.TryGetProperty("used", out var usedEl))
        {
            used = usedEl.GetDouble();
        }

        if (element.TryGetProperty("limit", out var limitEl))
        {
            limit = limitEl.GetDouble();
        }

        if (element.TryGetProperty("resetsIn", out var resetsInEl) ||
            element.TryGetProperty("resets_at", out resetsInEl))
        {
            var resetsStr = resetsInEl.GetString();
            if (!string.IsNullOrEmpty(resetsStr))
            {
                resetsAt = ClaudeOAuthUsageFetcher.ParseISO8601Date(resetsStr) ?? ParseResetsIn(resetsStr);
                resetDescription = FormatResetDescription(resetsAt);
            }
        }

        if (usedPercent == 0 && used.HasValue && limit.HasValue && limit.Value > 0)
        {
            usedPercent = (used.Value / limit.Value) * 100;
        }

        return new RateWindow
        {
            UsedPercent = usedPercent,
            Used = used,
            Limit = limit,
            WindowMinutes = defaultWindowMinutes,
            ResetsAt = resetsAt,
            ResetDescription = resetDescription
        };
    }

    private UsageSnapshot ParsePlainTextOutput(string output)
    {
        RateWindow? primary = null;
        RateWindow? secondary = null;
        RateWindow? tertiary = null;
        string? planType = null;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Parse plan type
            if (trimmed.Contains("Plan:") || trimmed.Contains("plan:") || trimmed.Contains("Max"))
            {
                var match = Regex.Match(trimmed, @"[Pp]lan:\s*(.+)");
                if (match.Success)
                    planType = match.Groups[1].Value.Trim();
                else if (trimmed.Contains("Max"))
                    planType = "Max";
                continue;
            }

            // Parse session/5-hour
            if (trimmed.StartsWith("Session", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("5-hour", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("5 hour", StringComparison.OrdinalIgnoreCase))
            {
                primary = ParseRateWindowFromText(trimmed, 300);
            }
            // Parse weekly
            else if (trimmed.StartsWith("Weekly", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Contains("7-day", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Contains("7 day", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Contains("Sonnet", StringComparison.OrdinalIgnoreCase))
                {
                    tertiary = ParseRateWindowFromText(trimmed, 10080);
                }
                else
                {
                    secondary = ParseRateWindowFromText(trimmed, 10080);
                }
            }
            // Parse Sonnet specifically
            else if (trimmed.StartsWith("Sonnet", StringComparison.OrdinalIgnoreCase))
            {
                tertiary = ParseRateWindowFromText(trimmed, null);
            }
        }

        return new UsageSnapshot
        {
            ProviderId = "claude",
            Primary = primary ?? new RateWindow { UsedPercent = 0, WindowMinutes = 300 },
            Secondary = secondary,
            Tertiary = tertiary,
            Identity = new ProviderIdentity { PlanType = planType ?? "Max" },
            FetchedAt = DateTime.UtcNow
        };
    }

    private RateWindow ParseRateWindowFromText(string text, int? windowMinutes)
    {
        double usedPercent = 0;
        string? resetDescription = null;
        DateTime? resetsAt = null;

        var percentMatch = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*%");
        if (percentMatch.Success)
        {
            usedPercent = double.Parse(percentMatch.Groups[1].Value);
        }

        var resetMatch = Regex.Match(text, @"[Rr]esets?\s+in\s+(.+?)(?:\)|$)");
        if (resetMatch.Success)
        {
            var resetTimeStr = resetMatch.Groups[1].Value.Trim();
            resetsAt = ParseResetsIn(resetTimeStr);
            resetDescription = $"in {resetTimeStr}";
        }

        return new RateWindow
        {
            UsedPercent = usedPercent,
            WindowMinutes = windowMinutes,
            ResetsAt = resetsAt,
            ResetDescription = resetDescription
        };
    }

    private DateTime? ParseResetsIn(string resetsIn)
    {
        int totalMinutes = 0;

        var daysMatch = Regex.Match(resetsIn, @"(\d+)\s*d");
        var hoursMatch = Regex.Match(resetsIn, @"(\d+)\s*h");
        var minutesMatch = Regex.Match(resetsIn, @"(\d+)\s*m");

        if (daysMatch.Success)
            totalMinutes += int.Parse(daysMatch.Groups[1].Value) * 24 * 60;
        if (hoursMatch.Success)
            totalMinutes += int.Parse(hoursMatch.Groups[1].Value) * 60;
        if (minutesMatch.Success)
            totalMinutes += int.Parse(minutesMatch.Groups[1].Value);

        if (totalMinutes > 0)
            return DateTime.UtcNow.AddMinutes(totalMinutes);

        return null;
    }

    private string? FormatResetDescription(DateTime? resetsAt)
    {
        if (!resetsAt.HasValue)
            return null;

        var diff = resetsAt.Value - DateTime.UtcNow;
        if (diff.TotalMinutes <= 0)
            return "now";

        if (diff.TotalDays >= 1)
        {
            int days = (int)diff.TotalDays;
            int hours = diff.Hours;
            if (hours > 0)
                return $"in {days}d {hours}h";
            return $"in {days}d";
        }

        if (diff.TotalHours >= 1)
        {
            int hours = (int)diff.TotalHours;
            int minutes = diff.Minutes;
            if (minutes > 0)
                return $"in {hours}h {minutes}m";
            return $"in {hours}h";
        }

        return $"in {(int)diff.TotalMinutes}m";
    }
}
