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

    public override bool SupportsOAuth => true;
    public override bool SupportsCLI => true;

    protected override void InitializeStrategies()
    {
        AddStrategy(new CodexCLIStrategy());
        AddStrategy(new CodexRPCStrategy());
    }
}

/// <summary>
/// CLI strategy parsing codex usage output - Primary strategy
/// Based on actual codex CLI output format
/// </summary>
public class CodexCLIStrategy : IProviderFetchStrategy
{
    public string StrategyName => "CLI";
    public int Priority => 1; // Higher priority than RPC
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

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get usage info from codex CLI
            var usageOutput = await RunCodexCommandAsync("usage", cancellationToken);

            if (string.IsNullOrEmpty(usageOutput))
            {
                // Fallback: try status command
                usageOutput = await RunCodexCommandAsync("status", cancellationToken);
            }

            if (string.IsNullOrEmpty(usageOutput))
            {
                return new UsageSnapshot
                {
                    ProviderId = "codex",
                    ErrorMessage = "Failed to get usage from codex CLI",
                    FetchedAt = DateTime.UtcNow
                };
            }

            return ParseCodexOutput(usageOutput);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CodexCLIStrategy", $"ERROR: {ex.Message}", ex);

            return new UsageSnapshot
            {
                ProviderId = "codex",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private async Task<string> RunCodexCommandAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "codex",
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process == null) return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            DebugLogger.Log("CodexCLIStrategy", $"codex {command} output:\n{output}\nstderr: {error}");

            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    private UsageSnapshot ParseCodexOutput(string output)
    {
        // Parse various formats of codex output
        // Format 1: JSON output from "codex usage --json"
        // Format 2: Plain text output from "codex usage" or "codex status"

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
            root.TryGetProperty("primary", out session))
        {
            primary = ParseRateWindowFromJson(session, 300); // 5 hours = 300 minutes
        }

        // Parse weekly limit
        if (root.TryGetProperty("weekly", out var weekly) ||
            root.TryGetProperty("secondary", out weekly))
        {
            secondary = ParseRateWindowFromJson(weekly, 10080); // 7 days = 10080 minutes
        }

        // Parse additional limits (like Sonnet)
        if (root.TryGetProperty("sonnet", out var sonnet) ||
            root.TryGetProperty("tertiary", out sonnet))
        {
            tertiary = ParseRateWindowFromJson(sonnet, null);
        }

        // Parse identity
        if (root.TryGetProperty("plan", out var plan) ||
            root.TryGetProperty("planType", out plan))
        {
            identity = new ProviderIdentity
            {
                PlanType = plan.GetString() ?? "Max"
            };
        }

        return new UsageSnapshot
        {
            ProviderId = "codex",
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
            element.TryGetProperty("used_percent", out usedPercentEl))
        {
            usedPercent = usedPercentEl.GetDouble();
        }

        if (element.TryGetProperty("used", out var usedEl))
        {
            used = usedEl.GetDouble();
        }

        if (element.TryGetProperty("limit", out var limitEl))
        {
            limit = limitEl.GetDouble();
        }

        if (element.TryGetProperty("resetsAt", out var resetsAtEl) ||
            element.TryGetProperty("resets_at", out resetsAtEl))
        {
            if (resetsAtEl.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(resetsAtEl.GetString(), out var dt))
                    resetsAt = dt;
            }
        }

        if (element.TryGetProperty("resetsIn", out var resetsInEl) ||
            element.TryGetProperty("resets_in", out resetsInEl))
        {
            resetDescription = resetsInEl.GetString();

            // Try to parse resets_in to DateTime
            if (!string.IsNullOrEmpty(resetDescription))
            {
                resetsAt = ParseResetsIn(resetDescription);
            }
        }

        // Calculate percent from used/limit if not provided
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
        // Parse text output like:
        // "Session: 2% used (Resets in 3h 53m)"
        // "Weekly: 3% used (Resets in 3d 20h)"
        // "Sonnet: 0% used"

        RateWindow? primary = null;
        RateWindow? secondary = null;
        RateWindow? tertiary = null;
        string? planType = null;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Parse plan type
            if (trimmed.Contains("Plan:") || trimmed.Contains("plan:"))
            {
                var match = Regex.Match(trimmed, @"[Pp]lan:\s*(.+)");
                if (match.Success)
                    planType = match.Groups[1].Value.Trim();
                continue;
            }

            // Parse session/5-hour window
            if (trimmed.StartsWith("Session", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("5-hour", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("5 hour", StringComparison.OrdinalIgnoreCase))
            {
                primary = ParseRateWindowFromText(trimmed, 300);
            }
            // Parse weekly limit
            else if (trimmed.StartsWith("Weekly", StringComparison.OrdinalIgnoreCase))
            {
                secondary = ParseRateWindowFromText(trimmed, 10080);
            }
            // Parse Sonnet or other tertiary limits
            else if (trimmed.StartsWith("Sonnet", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("Extra", StringComparison.OrdinalIgnoreCase))
            {
                tertiary = ParseRateWindowFromText(trimmed, null);
            }
        }

        return new UsageSnapshot
        {
            ProviderId = "codex",
            Primary = primary ?? new RateWindow
            {
                UsedPercent = 0,
                WindowMinutes = 300,
                ResetDescription = "in 5 hours"
            },
            Secondary = secondary,
            Tertiary = tertiary,
            Identity = new ProviderIdentity { PlanType = planType ?? "Max" },
            FetchedAt = DateTime.UtcNow
        };
    }

    private RateWindow ParseRateWindowFromText(string text, int? windowMinutes)
    {
        // Parse patterns like:
        // "Session: 2% used (Resets in 3h 53m)"
        // "Weekly: 3% used (Resets in 3d 20h)"
        // "Sonnet: 0% used"
        // "5-hour window: 15% | Resets in 2h 30m"

        double usedPercent = 0;
        string? resetDescription = null;
        DateTime? resetsAt = null;

        // Extract percentage
        var percentMatch = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*%");
        if (percentMatch.Success)
        {
            usedPercent = double.Parse(percentMatch.Groups[1].Value);
        }

        // Extract reset time
        var resetMatch = Regex.Match(text, @"[Rr]esets?\s+in\s+(.+?)(?:\)|$)");
        if (resetMatch.Success)
        {
            resetDescription = resetMatch.Groups[1].Value.Trim();
            resetsAt = ParseResetsIn(resetDescription);
        }

        return new RateWindow
        {
            UsedPercent = usedPercent,
            WindowMinutes = windowMinutes,
            ResetsAt = resetsAt,
            ResetDescription = resetDescription != null ? $"in {resetDescription}" : null
        };
    }

    private DateTime? ParseResetsIn(string resetsIn)
    {
        // Parse strings like "3h 53m", "3d 20h", "2h", "45m"
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
}

/// <summary>
/// RPC strategy communicating with Codex daemon via JSON-RPC
/// </summary>
public class CodexRPCStrategy : IProviderFetchStrategy
{
    public string StrategyName => "RPC";
    public int Priority => 2;
    public StrategyType Type => StrategyType.AutoDetect;

    public async Task<bool> CanExecuteAsync()
    {
        // Check if codex daemon is running
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
