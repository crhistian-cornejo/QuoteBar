using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Services;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NativeBar.WinUI.Core.Providers.MiniMax;

/// <summary>
/// Fetches usage data from MiniMax Coding Plan API
/// Based on CodexBar's MiniMaxUsageFetcher.swift implementation
/// </summary>
public static class MiniMaxUsageFetcher
{
    private const string CodingPlanUrl = "https://platform.minimax.io/user-center/payment/coding-plan?cycle_type=3";
    private const string CodingPlanRefererUrl = "https://platform.minimax.io/user-center/payment/coding-plan";
    private const string CodingPlanRemainsUrl = "https://platform.minimax.io/v1/api/openplatform/coding_plan/remains";

    private static readonly string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36";

    public static async Task<UsageSnapshot> FetchUsageAsync(
        string cookieHeader,
        string? authorizationToken = null,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        // First try the HTML page, then fall back to the remains API
        try
        {
            return await FetchCodingPlanHtmlAsync(cookieHeader, authorizationToken, cancellationToken);
        }
        catch (MiniMaxUsageException ex) when (ex.ErrorType == MiniMaxErrorType.ParseFailed)
        {
            Log("Coding plan HTML parse failed, trying remains API");
            return await FetchCodingPlanRemainsAsync(cookieHeader, authorizationToken, groupId, cancellationToken);
        }
    }

    private static async Task<UsageSnapshot> FetchCodingPlanHtmlAsync(
        string cookie,
        string? authorizationToken,
        CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient(cookie, authorizationToken);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.Referrer = new Uri(CodingPlanRefererUrl);

        var response = await client.GetAsync(CodingPlanUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            Log($"API error: HTTP {(int)response.StatusCode}: {errorBody}");

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new MiniMaxUsageException("MiniMax session cookie is invalid or expired.", MiniMaxErrorType.InvalidCredentials);
            }

            throw new MiniMaxUsageException($"HTTP {(int)response.StatusCode}", MiniMaxErrorType.ApiError);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // If response is JSON, parse it as coding plan remains
        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return MiniMaxUsageParser.ParseCodingPlanRemains(body);
        }

        // Check if it looks like a signed-out page
        if (LooksSignedOut(body))
        {
            throw new MiniMaxUsageException("MiniMax session cookie is invalid or expired.", MiniMaxErrorType.InvalidCredentials);
        }

        // Parse HTML
        return MiniMaxUsageParser.ParseHtml(body);
    }

    private static async Task<UsageSnapshot> FetchCodingPlanRemainsAsync(
        string cookie,
        string? authorizationToken,
        string? groupId,
        CancellationToken cancellationToken)
    {
        var url = CodingPlanRemainsUrl;
        if (!string.IsNullOrEmpty(groupId))
        {
            url = $"{CodingPlanRemainsUrl}?GroupId={Uri.EscapeDataString(groupId)}";
        }

        using var client = CreateHttpClient(cookie, authorizationToken);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
        client.DefaultRequestHeaders.Add("x-requested-with", "XMLHttpRequest");
        client.DefaultRequestHeaders.Referrer = new Uri(CodingPlanRefererUrl);

        var response = await client.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            Log($"API error: HTTP {(int)response.StatusCode}: {errorBody}");

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new MiniMaxUsageException("MiniMax session cookie is invalid or expired.", MiniMaxErrorType.InvalidCredentials);
            }

            throw new MiniMaxUsageException($"HTTP {(int)response.StatusCode}", MiniMaxErrorType.ApiError);
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // If response is JSON, parse it
        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return MiniMaxUsageParser.ParseCodingPlanRemains(body);
        }

        // Check if it looks like a signed-out page
        if (LooksSignedOut(body))
        {
            throw new MiniMaxUsageException("MiniMax session cookie is invalid or expired.", MiniMaxErrorType.InvalidCredentials);
        }

        // Try parsing as HTML
        return MiniMaxUsageParser.ParseHtml(body);
    }

    private static HttpClient CreateHttpClient(string cookie, string? authorizationToken)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.Add("Origin", "https://platform.minimax.io");

        if (!string.IsNullOrEmpty(authorizationToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authorizationToken);
        }

        return client;
    }

    private static bool LooksSignedOut(string html)
    {
        var lower = html.ToLowerInvariant();
        return lower.Contains("sign in") || lower.Contains("log in") ||
               lower.Contains("登录") || lower.Contains("登入");
    }

    private static void Log(string message)
    {
        DebugLogger.Log("MiniMaxUsageFetcher", message);
    }
}

/// <summary>
/// Parses MiniMax usage data from HTML or JSON responses
/// </summary>
public static class MiniMaxUsageParser
{
    public static UsageSnapshot ParseCodingPlanRemains(string json)
    {
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Check base_resp for errors
        if (root.TryGetProperty("base_resp", out var baseResp))
        {
            var statusCode = GetIntValue(baseResp, "status_code");
            if (statusCode.HasValue && statusCode.Value != 0)
            {
                var statusMsg = GetStringValue(baseResp, "status_msg") ?? $"status_code {statusCode}";
                var lower = statusMsg.ToLowerInvariant();
                
                if (statusCode == 1004 || lower.Contains("cookie") || lower.Contains("log in") || lower.Contains("login"))
                {
                    throw new MiniMaxUsageException("MiniMax session cookie is invalid or expired.", MiniMaxErrorType.InvalidCredentials);
                }
                
                throw new MiniMaxUsageException(statusMsg, MiniMaxErrorType.ApiError);
            }
        }

        // Get data root
        var dataRoot = root.TryGetProperty("data", out var dataProp) ? dataProp : root;

        // Parse model_remains array
        if (!dataRoot.TryGetProperty("model_remains", out var modelRemains) || 
            modelRemains.ValueKind != JsonValueKind.Array ||
            modelRemains.GetArrayLength() == 0)
        {
            throw new MiniMaxUsageException("Missing coding plan data.", MiniMaxErrorType.ParseFailed);
        }

        var first = modelRemains[0];

        var total = GetIntValue(first, "current_interval_total_count");
        var remaining = GetIntValue(first, "current_interval_usage_count");
        var usedPercent = ComputeUsedPercent(total, remaining);

        var startTime = GetDateFromEpoch(first, "start_time");
        var endTime = GetDateFromEpoch(first, "end_time");
        var remainsTime = GetIntValue(first, "remains_time");

        var windowMinutes = ComputeWindowMinutes(startTime, endTime);
        var resetsAt = ComputeResetsAt(endTime, remainsTime, DateTime.UtcNow);

        var planName = ParsePlanName(dataRoot);

        if (planName == null && total == null && usedPercent == null)
        {
            throw new MiniMaxUsageException("Missing coding plan data.", MiniMaxErrorType.ParseFailed);
        }

        return ToUsageSnapshot(planName, total, windowMinutes, usedPercent, resetsAt);
    }

    public static UsageSnapshot ParseHtml(string html)
    {
        // Try to parse __NEXT_DATA__ first
        var nextDataSnapshot = ParseNextData(html);
        if (nextDataSnapshot != null)
        {
            return nextDataSnapshot;
        }

        // Strip HTML tags for text parsing
        var text = StripHtml(html);

        var planName = ParsePlanNameFromHtml(html, text);
        var available = ParseAvailableUsage(text);
        var usedPercent = ParseUsedPercent(text);
        var resetsAt = ParseResetsAt(text, DateTime.UtcNow);

        if (planName == null && available == null && usedPercent == null)
        {
            throw new MiniMaxUsageException("Missing coding plan data.", MiniMaxErrorType.ParseFailed);
        }

        return ToUsageSnapshot(planName, available?.prompts, available?.windowMinutes, usedPercent, resetsAt);
    }

    private static UsageSnapshot? ParseNextData(string html)
    {
        // Look for __NEXT_DATA__ script tag
        var match = Regex.Match(html, @"<script\s+id=""__NEXT_DATA__""[^>]*>([^<]+)</script>", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        try
        {
            var jsonContent = match.Groups[1].Value.Trim();
            var doc = JsonDocument.Parse(jsonContent);

            // Recursively find model_remains
            var payload = FindCodingPlanPayload(doc.RootElement);
            if (payload == null)
                return null;

            // Re-serialize and parse
            return ParseCodingPlanRemains(payload.Value.GetRawText());
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement? FindCodingPlanPayload(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("model_remains", out _))
            {
                return element;
            }

            foreach (var prop in element.EnumerateObject())
            {
                var result = FindCodingPlanPayload(prop.Value);
                if (result != null)
                    return result;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var result = FindCodingPlanPayload(item);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    private static string? ParsePlanName(JsonElement root)
    {
        string[] keys = { "current_subscribe_title", "plan_name", "combo_title", "current_plan_title" };

        foreach (var key in keys)
        {
            var value = GetStringValue(root, key);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        if (root.TryGetProperty("current_combo_card", out var card))
        {
            var title = GetStringValue(card, "title");
            if (!string.IsNullOrWhiteSpace(title))
                return title.Trim();
        }

        return null;
    }

    private static string? ParsePlanNameFromHtml(string html, string text)
    {
        string[] patterns = {
            @"(?i)""planName""\s*:\s*""([^""]+)""",
            @"(?i)""plan""\s*:\s*""([^""]+)""",
            @"(?i)""packageName""\s*:\s*""([^""]+)""",
            @"(?i)Coding\s*Plan\s*([A-Za-z0-9][A-Za-z0-9\s._-]{0,32})"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(pattern.Contains("Coding") ? text : html, pattern);
            if (match.Success && match.Groups.Count > 1)
            {
                var value = match.Groups[1].Value.Trim();
                // Clean up the plan name
                value = Regex.Replace(value, @"(?i)\s+available\s+usage.*$", "").Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    private static (int prompts, int windowMinutes)? ParseAvailableUsage(string text)
    {
        var pattern = @"(?i)available\s+usage[:\s]*([0-9][0-9,]*)\s*prompts?\s*/\s*([0-9]+(?:\.[0-9]+)?)\s*(hours?|hrs?|h|minutes?|mins?|m|days?|d)";
        var match = Regex.Match(text, pattern);

        if (!match.Success || match.Groups.Count < 4)
            return null;

        var promptsRaw = match.Groups[1].Value.Replace(",", "");
        var durationRaw = match.Groups[2].Value;
        var unitRaw = match.Groups[3].Value;

        if (!int.TryParse(promptsRaw, out var prompts) || prompts <= 0)
            return null;

        if (!double.TryParse(durationRaw, out var duration))
            return null;

        var windowMinutes = ComputeMinutes(duration, unitRaw);
        if (windowMinutes <= 0)
            return null;

        return (prompts, windowMinutes);
    }

    private static double? ParseUsedPercent(string text)
    {
        string[] patterns = {
            @"(?i)([0-9]{1,3}(?:\.[0-9]+)?)\s*%\s*used",
            @"(?i)used\s*([0-9]{1,3}(?:\.[0-9]+)?)\s*%"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, pattern);
            if (match.Success && match.Groups.Count > 1)
            {
                if (double.TryParse(match.Groups[1].Value, out var value) && value >= 0 && value <= 100)
                    return value;
            }
        }

        return null;
    }

    private static DateTime? ParseResetsAt(string text, DateTime now)
    {
        // Try "resets in X units"
        var match = Regex.Match(text, @"(?i)resets?\s+in\s+([0-9]+)\s*(seconds?|secs?|s|minutes?|mins?|m|hours?|hrs?|h|days?|d)");
        if (match.Success && match.Groups.Count >= 3)
        {
            if (double.TryParse(match.Groups[1].Value, out var value))
            {
                var unit = match.Groups[2].Value;
                var seconds = ComputeSeconds(value, unit);
                return now.AddSeconds(seconds);
            }
        }

        // Try "resets at HH:mm"
        match = Regex.Match(text, @"(?i)resets?\s+at\s+([0-9]{1,2}:[0-9]{2})(?:\s*\(([^)]+)\))?");
        if (match.Success && match.Groups.Count >= 2)
        {
            var timeText = match.Groups[1].Value;
            // Parse time and compute next occurrence
            if (TimeSpan.TryParse(timeText, out var time))
            {
                var candidate = now.Date.Add(time);
                if (candidate < now)
                    candidate = candidate.AddDays(1);
                return candidate;
            }
        }

        return null;
    }

    private static double? ComputeUsedPercent(int? total, int? remaining)
    {
        if (total == null || total <= 0 || remaining == null)
            return null;

        int used;
        if (remaining > total)
            used = Math.Min(remaining.Value, total.Value);
        else
            used = Math.Max(0, total.Value - remaining.Value);

        var percent = (double)used / total.Value * 100;
        return Math.Min(100, Math.Max(0, percent));
    }

    private static int? ComputeWindowMinutes(DateTime? start, DateTime? end)
    {
        if (start == null || end == null)
            return null;

        var minutes = (int)(end.Value - start.Value).TotalMinutes;
        return minutes > 0 ? minutes : null;
    }

    private static DateTime? ComputeResetsAt(DateTime? end, int? remainsTime, DateTime now)
    {
        if (end.HasValue && end.Value > now)
            return end;

        if (remainsTime == null || remainsTime <= 0)
            return null;

        // remainsTime might be in milliseconds or seconds
        var seconds = remainsTime > 1_000_000 ? remainsTime.Value / 1000.0 : remainsTime.Value;
        return now.AddSeconds(seconds);
    }

    private static int ComputeMinutes(double value, string unit)
    {
        var lower = unit.ToLowerInvariant();
        if (lower.StartsWith("d")) return (int)(value * 24 * 60);
        if (lower.StartsWith("h")) return (int)(value * 60);
        if (lower.StartsWith("m")) return (int)value;
        if (lower.StartsWith("s")) return Math.Max(1, (int)(value / 60));
        return 0;
    }

    private static double ComputeSeconds(double value, string unit)
    {
        var lower = unit.ToLowerInvariant();
        if (lower.StartsWith("d")) return value * 24 * 60 * 60;
        if (lower.StartsWith("h")) return value * 60 * 60;
        if (lower.StartsWith("m")) return value * 60;
        return value;
    }

    private static string StripHtml(string html)
    {
        var text = Regex.Replace(html, "<[^>]+>", " ");
        text = text.Replace("&nbsp;", " ")
                   .Replace("&amp;", "&")
                   .Replace("&lt;", "<")
                   .Replace("&gt;", ">");
        text = Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    private static int? GetIntValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return null;

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.TryGetInt32(out var i) ? i : (prop.TryGetInt64(out var l) ? (int)l : null),
            JsonValueKind.String => int.TryParse(prop.GetString()?.Trim(), out var parsed) ? parsed : null,
            _ => null
        };
    }

    private static string? GetStringValue(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
            return null;

        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }

    private static DateTime? GetDateFromEpoch(JsonElement element, string propertyName)
    {
        var raw = GetIntValue(element, propertyName);
        if (raw == null)
            return null;

        // Handle both seconds and milliseconds
        if (raw > 1_000_000_000_000)
            return DateTimeOffset.FromUnixTimeMilliseconds(raw.Value).UtcDateTime;
        if (raw > 1_000_000_000)
            return DateTimeOffset.FromUnixTimeSeconds(raw.Value).UtcDateTime;

        return null;
    }

    private static UsageSnapshot ToUsageSnapshot(
        string? planName,
        int? availablePrompts,
        int? windowMinutes,
        double? usedPercent,
        DateTime? resetsAt)
    {
        var used = Math.Max(0, Math.Min(100, usedPercent ?? 0));
        var resetDescription = GetLimitDescription(availablePrompts, windowMinutes);

        var primary = new RateWindow
        {
            UsedPercent = used,
            WindowMinutes = windowMinutes,
            ResetsAt = resetsAt,
            ResetDescription = resetDescription
        };

        var identity = new ProviderIdentity
        {
            PlanType = string.IsNullOrWhiteSpace(planName) ? null : planName.Trim()
        };

        return new UsageSnapshot
        {
            ProviderId = "minimax",
            Primary = primary,
            Secondary = null,
            Tertiary = null,
            Identity = identity,
            FetchedAt = DateTime.UtcNow
        };
    }

    private static string? GetLimitDescription(int? availablePrompts, int? windowMinutes)
    {
        if (availablePrompts == null || availablePrompts <= 0)
            return GetWindowDescription(windowMinutes);

        var windowDesc = GetWindowDescription(windowMinutes);
        if (windowDesc != null)
            return $"{availablePrompts} prompts / {windowDesc}";

        return $"{availablePrompts} prompts";
    }

    private static string? GetWindowDescription(int? windowMinutes)
    {
        if (windowMinutes == null || windowMinutes <= 0)
            return null;

        if (windowMinutes % (24 * 60) == 0)
        {
            var days = windowMinutes / (24 * 60);
            return $"{days} {(days == 1 ? "day" : "days")}";
        }

        if (windowMinutes % 60 == 0)
        {
            var hours = windowMinutes / 60;
            return $"{hours} {(hours == 1 ? "hour" : "hours")}";
        }

        return $"{windowMinutes} {(windowMinutes == 1 ? "minute" : "minutes")}";
    }
}

public enum MiniMaxErrorType
{
    InvalidCredentials,
    NetworkError,
    ApiError,
    ParseFailed
}

public class MiniMaxUsageException : Exception
{
    public MiniMaxErrorType ErrorType { get; }

    public MiniMaxUsageException(string message, MiniMaxErrorType errorType = MiniMaxErrorType.ApiError)
        : base(message)
    {
        ErrorType = errorType;
    }
}
