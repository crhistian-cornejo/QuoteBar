using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Services;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NativeBar.WinUI.Core.Providers.Antigravity;

public class AntigravityProviderDescriptor : ProviderDescriptor
{
    public override string Id => "antigravity";
    public override string DisplayName => "Antigravity";
    public override string IconGlyph => "\uE81E"; // Repair icon
    public override string PrimaryColor => "#FFFFFF";
    public override string SecondaryColor => "#E0E0E0";
    public override string PrimaryLabel => "Claude";
    public override string SecondaryLabel => "Gemini Pro";
    public override string? TertiaryLabel => "Gemini Flash";

    public override UsageWindowType PrimaryWindowType => UsageWindowType.Daily;
    public override UsageWindowType SecondaryWindowType => UsageWindowType.Daily;

    // Antigravity is a local-only provider, no dashboard
    public override string? DashboardUrl => null;

    public override bool SupportsOAuth => false;

    protected override void InitializeStrategies()
    {
        AddStrategy(new AntigravityLocalProbeStrategy());
    }
}

/// <summary>
/// Local probe strategy for Antigravity language server.
/// Detects the running Antigravity process, extracts CSRF token and port,
/// then queries the local gRPC-Connect API for usage data.
/// </summary>
public class AntigravityLocalProbeStrategy : IProviderFetchStrategy
{
    public string StrategyName => "LocalProbe";
    public int Priority => 1;

    private const string ProcessName = "language_server_windows";
    private const string GetUserStatusPath = "/exa.language_server_pb.LanguageServerService/GetUserStatus";
    private const string GetCommandModelConfigPath = "/exa.language_server_pb.LanguageServerService/GetCommandModelConfigs";
    private const string GetUnleashDataPath = "/exa.language_server_pb.LanguageServerService/GetUnleashData";
    private const int DefaultTimeout = 8000;

    public async Task<bool> CanExecuteAsync()
    {
        try
        {
            var processInfo = await DetectAntigravityProcess();
            return processInfo != null;
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
            // Step 1: Detect Antigravity process and extract CSRF token
            var processInfo = await DetectAntigravityProcess();
            if (processInfo == null)
            {
                return new UsageSnapshot
                {
                    ProviderId = "antigravity",
                    ErrorMessage = "Antigravity not running. Launch Antigravity and retry.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            DebugLogger.Log("Antigravity", $"Found process PID={processInfo.Pid}, CSRF token present, port={processInfo.ExtensionPort}");

            // Step 2: Find listening ports
            var ports = await GetListeningPorts(processInfo.Pid);
            if (ports.Count == 0)
            {
                return new UsageSnapshot
                {
                    ProviderId = "antigravity",
                    ErrorMessage = "Antigravity is running but no ports detected. Try again.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            DebugLogger.Log("Antigravity", $"Found {ports.Count} ports: {string.Join(", ", ports)}");

            // Step 3: Find working API port
            int? workingPort = null;
            foreach (var port in ports)
            {
                if (await TestPortConnectivity(port, processInfo.CsrfToken))
                {
                    workingPort = port;
                    break;
                }
            }

            if (workingPort == null)
            {
                return new UsageSnapshot
                {
                    ProviderId = "antigravity",
                    ErrorMessage = "Could not connect to Antigravity API. Restart Antigravity and retry.",
                    FetchedAt = DateTime.UtcNow
                };
            }

            DebugLogger.Log("Antigravity", $"Using port {workingPort}");

            // Step 4: Fetch usage data
            try
            {
                var response = await MakeApiRequest(workingPort.Value, GetUserStatusPath, processInfo.CsrfToken, cancellationToken);
                return ParseUserStatusResponse(response);
            }
            catch
            {
                // Fallback to GetCommandModelConfigs
                var response = await MakeApiRequest(workingPort.Value, GetCommandModelConfigPath, processInfo.CsrfToken, cancellationToken);
                return ParseCommandModelResponse(response);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("Antigravity", "Fetch error", ex);
            return new UsageSnapshot
            {
                ProviderId = "antigravity",
                ErrorMessage = ex.Message,
                FetchedAt = DateTime.UtcNow
            };
        }
    }

    private record ProcessInfo(int Pid, string CsrfToken, int? ExtensionPort);

    private async Task<ProcessInfo?> DetectAntigravityProcess()
    {
        try
        {
            // Use PowerShell Get-CimInstance (modern replacement for deprecated wmic)
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -Command \"Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*language_server*' } | Select-Object ProcessId, CommandLine | ConvertTo-Json\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (string.IsNullOrWhiteSpace(output)) return null;

            // Parse JSON output
            using var doc = JsonDocument.Parse(output);

            // Handle both single object and array
            var processes = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray()
                : new[] { doc.RootElement }.AsEnumerable();

            foreach (var proc in processes)
            {
                if (!proc.TryGetProperty("ProcessId", out var pidEl) ||
                    !proc.TryGetProperty("CommandLine", out var cmdEl))
                    continue;

                var pid = pidEl.GetInt32();
                var commandLine = cmdEl.GetString();

                if (string.IsNullOrEmpty(commandLine)) continue;

                // Check if this is Antigravity
                var lower = commandLine.ToLowerInvariant();
                if (!lower.Contains("language_server") || !IsAntigravityCommandLine(lower))
                    continue;

                // Extract CSRF token
                var csrfToken = ExtractFlag("--csrf_token", commandLine);
                if (string.IsNullOrEmpty(csrfToken)) continue;

                // Extract extension port (optional)
                var extensionPort = ExtractPort("--extension_server_port", commandLine);

                return new ProcessInfo(pid, csrfToken, extensionPort);
            }

            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("Antigravity", "DetectAntigravityProcess error", ex);
            return null;
        }
    }

    private static bool IsAntigravityCommandLine(string command)
    {
        if (command.Contains("--app_data_dir") && command.Contains("antigravity")) return true;
        if (command.Contains("\\antigravity\\") || command.Contains("/antigravity/")) return true;
        return false;
    }

    private static string? ExtractFlag(string flag, string command)
    {
        var pattern = $@"{Regex.Escape(flag)}[=\s]+([^\s]+)";
        var match = Regex.Match(command, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static int? ExtractPort(string flag, string command)
    {
        var raw = ExtractFlag(flag, command);
        return raw != null && int.TryParse(raw, out var port) ? port : null;
    }

    private async Task<List<int>> GetListeningPorts(int pid)
    {
        var ports = new HashSet<int>();

        try
        {
            // Use netstat to find listening ports for the process
            var startInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return ports.ToList();

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Parse netstat output
            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains("LISTENING")) continue;

                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;

                // Check if the PID matches
                if (!int.TryParse(parts[^1], out var linePid) || linePid != pid)
                    continue;

                // Extract port from local address (e.g., "127.0.0.1:12345" or "[::]:12345")
                var localAddr = parts[1];
                var colonIndex = localAddr.LastIndexOf(':');
                if (colonIndex > 0 && int.TryParse(localAddr.Substring(colonIndex + 1), out var port))
                {
                    ports.Add(port);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("Antigravity", "GetListeningPorts error", ex);
        }

        return ports.OrderBy(p => p).ToList();
    }

    private async Task<bool> TestPortConnectivity(int port, string csrfToken)
    {
        try
        {
            await MakeApiRequest(port, GetUnleashDataPath, csrfToken, CancellationToken.None, isProbe: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> MakeApiRequest(int port, string path, string csrfToken, CancellationToken cancellationToken, bool isProbe = false)
    {
        var body = isProbe ? GetUnleashRequestBody() : GetDefaultRequestBody();
        var json = JsonSerializer.Serialize(body);

        // Try HTTPS first, then HTTP
        foreach (var scheme in new[] { "https", "http" })
        {
            try
            {
                var url = $"{scheme}://127.0.0.1:{port}{path}";

                using var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true // Accept self-signed certs
                };

                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(DefaultTimeout) };

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Add("X-Codeium-Csrf-Token", csrfToken);
                request.Headers.Add("Connect-Protocol-Version", "1");

                var response = await client.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync(cancellationToken);
                }
            }
            catch
            {
                // Try next scheme
            }
        }

        throw new Exception($"Failed to connect to Antigravity API on port {port}");
    }

    private static Dictionary<string, object> GetDefaultRequestBody() => new()
    {
        ["metadata"] = new Dictionary<string, string>
        {
            ["ideName"] = "antigravity",
            ["extensionName"] = "antigravity",
            ["ideVersion"] = "unknown",
            ["locale"] = "en"
        }
    };

    private static Dictionary<string, object> GetUnleashRequestBody() => new()
    {
        ["context"] = new Dictionary<string, object>
        {
            ["properties"] = new Dictionary<string, string>
            {
                ["devMode"] = "false",
                ["extensionVersion"] = "unknown",
                ["hasAnthropicModelAccess"] = "true",
                ["ide"] = "antigravity",
                ["ideVersion"] = "unknown",
                ["installationId"] = "quotebar",
                ["language"] = "UNSPECIFIED",
                ["os"] = "windows",
                ["requestedModelId"] = "MODEL_UNSPECIFIED"
            }
        }
    };

    private UsageSnapshot ParseUserStatusResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Check for error code
        if (root.TryGetProperty("code", out var code) && !IsOkCode(code))
        {
            return new UsageSnapshot
            {
                ProviderId = "antigravity",
                ErrorMessage = $"Antigravity API error: {code}",
                FetchedAt = DateTime.UtcNow
            };
        }

        if (!root.TryGetProperty("userStatus", out var userStatus))
        {
            return new UsageSnapshot
            {
                ProviderId = "antigravity",
                ErrorMessage = "Missing userStatus in response",
                FetchedAt = DateTime.UtcNow
            };
        }

        var quotas = ExtractModelQuotas(userStatus);
        var selected = SelectModels(quotas);

        string? email = null;
        string? planName = null;

        if (userStatus.TryGetProperty("email", out var emailEl))
            email = emailEl.GetString();

        if (userStatus.TryGetProperty("planStatus", out var planStatus) &&
            planStatus.TryGetProperty("planInfo", out var planInfo))
        {
            planName = planInfo.TryGetProperty("planDisplayName", out var pn) ? pn.GetString() :
                       planInfo.TryGetProperty("displayName", out pn) ? pn.GetString() :
                       planInfo.TryGetProperty("planName", out pn) ? pn.GetString() : null;
        }

        return new UsageSnapshot
        {
            ProviderId = "antigravity",
            Primary = selected.Count > 0 ? ToRateWindow(selected[0]) : null,
            Secondary = selected.Count > 1 ? ToRateWindow(selected[1]) : null,
            Tertiary = selected.Count > 2 ? ToRateWindow(selected[2]) : null,
            Identity = new ProviderIdentity { Email = email, PlanType = planName },
            FetchedAt = DateTime.UtcNow
        };
    }

    private UsageSnapshot ParseCommandModelResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("code", out var code) && !IsOkCode(code))
        {
            return new UsageSnapshot
            {
                ProviderId = "antigravity",
                ErrorMessage = $"Antigravity API error: {code}",
                FetchedAt = DateTime.UtcNow
            };
        }

        var quotas = new List<ModelQuota>();
        if (root.TryGetProperty("clientModelConfigs", out var configs) && configs.ValueKind == JsonValueKind.Array)
        {
            quotas = ExtractQuotasFromConfigs(configs);
        }

        var selected = SelectModels(quotas);

        return new UsageSnapshot
        {
            ProviderId = "antigravity",
            Primary = selected.Count > 0 ? ToRateWindow(selected[0]) : null,
            Secondary = selected.Count > 1 ? ToRateWindow(selected[1]) : null,
            Tertiary = selected.Count > 2 ? ToRateWindow(selected[2]) : null,
            FetchedAt = DateTime.UtcNow
        };
    }

    private record ModelQuota(string Label, string ModelId, double? RemainingFraction, DateTime? ResetTime);

    private List<ModelQuota> ExtractModelQuotas(JsonElement userStatus)
    {
        var quotas = new List<ModelQuota>();

        if (!userStatus.TryGetProperty("cascadeModelConfigData", out var configData) ||
            !configData.TryGetProperty("clientModelConfigs", out var configs) ||
            configs.ValueKind != JsonValueKind.Array)
        {
            return quotas;
        }

        return ExtractQuotasFromConfigs(configs);
    }

    private List<ModelQuota> ExtractQuotasFromConfigs(JsonElement configs)
    {
        var quotas = new List<ModelQuota>();

        foreach (var config in configs.EnumerateArray())
        {
            if (!config.TryGetProperty("label", out var labelEl) ||
                !config.TryGetProperty("modelOrAlias", out var modelOrAlias) ||
                !modelOrAlias.TryGetProperty("model", out var modelEl))
            {
                continue;
            }

            var label = labelEl.GetString() ?? "";
            var model = modelEl.GetString() ?? "";

            double? remainingFraction = null;
            DateTime? resetTime = null;

            if (config.TryGetProperty("quotaInfo", out var quota))
            {
                if (quota.TryGetProperty("remainingFraction", out var rf))
                    remainingFraction = rf.GetDouble();

                if (quota.TryGetProperty("resetTime", out var rt))
                    resetTime = ParseResetTime(rt.GetString());
            }

            quotas.Add(new ModelQuota(label, model, remainingFraction, resetTime));
        }

        return quotas;
    }

    private List<ModelQuota> SelectModels(List<ModelQuota> models)
    {
        var selected = new List<ModelQuota>();

        // Priority 1: Claude (without "thinking")
        var claude = models.FirstOrDefault(m =>
            m.Label.Contains("claude", StringComparison.OrdinalIgnoreCase) &&
            !m.Label.Contains("thinking", StringComparison.OrdinalIgnoreCase));
        if (claude != null) selected.Add(claude);

        // Priority 2: Gemini Pro Low
        var proPow = models.FirstOrDefault(m =>
            m.Label.Contains("pro", StringComparison.OrdinalIgnoreCase) &&
            m.Label.Contains("low", StringComparison.OrdinalIgnoreCase) &&
            !selected.Any(s => s.Label == m.Label));
        if (proPow != null) selected.Add(proPow);

        // Priority 3: Gemini Flash
        var flash = models.FirstOrDefault(m =>
            m.Label.Contains("gemini", StringComparison.OrdinalIgnoreCase) &&
            m.Label.Contains("flash", StringComparison.OrdinalIgnoreCase) &&
            !selected.Any(s => s.Label == m.Label));
        if (flash != null) selected.Add(flash);

        // Fallback: sort by remaining percent (lowest first = most used)
        if (selected.Count == 0)
        {
            selected.AddRange(models
                .Where(m => m.RemainingFraction.HasValue)
                .OrderBy(m => m.RemainingFraction)
                .Take(3));
        }

        return selected;
    }

    private RateWindow ToRateWindow(ModelQuota quota)
    {
        var usedPercent = quota.RemainingFraction.HasValue
            ? 100 - (quota.RemainingFraction.Value * 100)
            : 0;

        return new RateWindow
        {
            UsedPercent = Math.Max(0, Math.Min(100, usedPercent)),
            ResetsAt = quota.ResetTime,
            ResetDescription = quota.ResetTime.HasValue ? FormatResetTime(quota.ResetTime.Value) : null
        };
    }

    private static bool IsOkCode(JsonElement code)
    {
        if (code.ValueKind == JsonValueKind.Number)
            return code.GetInt32() == 0;
        if (code.ValueKind == JsonValueKind.String)
        {
            var s = code.GetString()?.ToLowerInvariant();
            return s == "ok" || s == "success" || s == "0";
        }
        return false;
    }

    private static DateTime? ParseResetTime(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        if (DateTime.TryParse(value, out var dt))
            return dt;

        if (double.TryParse(value, out var epoch))
            return DateTimeOffset.FromUnixTimeSeconds((long)epoch).UtcDateTime;

        return null;
    }

    private static string FormatResetTime(DateTime resetUtc)
    {
        var diff = resetUtc - DateTime.UtcNow;

        // If reset time is in the past, show "Resets now" or the date
        if (diff.TotalSeconds <= 0)
        {
            return "Resets now";
        }

        if (diff.TotalMinutes < 60) return $"in {diff.TotalMinutes:F0}m";
        if (diff.TotalHours < 24) return $"in {diff.TotalHours:F1}h";
        return resetUtc.ToLocalTime().ToString("MMM d, h:mm tt");
    }
}
