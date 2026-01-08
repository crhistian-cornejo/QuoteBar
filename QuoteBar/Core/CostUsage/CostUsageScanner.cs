using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using QuoteBar.Core.Services;

namespace QuoteBar.Core.CostUsage;

/// <summary>
/// JSONL file scanner for extracting cost/usage data from local logs
/// Based on CodexBar's CostUsageScanner.swift
/// </summary>
public static class CostUsageScanner
{
    public record struct ScanOptions
    {
        public string? CodexSessionsRoot { get; init; }
        public string? ClaudeConfigDir { get; init; }
        public int RefreshMinIntervalSeconds { get; init; }
        public bool ForceRescan { get; init; }
    }

    // MARK: - Codex Scanner

    /// <summary>
    /// Find the Codex sessions root directory
    /// </summary>
    public static string GetCodexSessionsRoot(ScanOptions options = default)
    {
        if (!string.IsNullOrEmpty(options.CodexSessionsRoot))
            return options.CodexSessionsRoot;

        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
            return Path.Combine(codexHome, "sessions");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex", "sessions");
    }

    /// <summary>
    /// List Codex session JSONL files within a date range
    /// </summary>
    public static List<string> ListCodexSessionFiles(string root, CostUsageDayRange range)
    {
        var files = new List<string>();
        var currentDate = CostUsageDayRange.ParseDayKey(range.ScanSinceKey) ?? DateTime.Today;
        var untilDate = CostUsageDayRange.ParseDayKey(range.ScanUntilKey) ?? DateTime.Today;

        while (currentDate <= untilDate)
        {
            var dayDir = Path.Combine(
                root,
                currentDate.Year.ToString("D4"),
                currentDate.Month.ToString("D2"),
                currentDate.Day.ToString("D2"));

            if (Directory.Exists(dayDir))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(dayDir, "*.jsonl"))
                    {
                        files.Add(file);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("CostUsageScanner", $"Error listing files in {dayDir}", ex);
                }
            }

            currentDate = currentDate.AddDays(1);
        }

        return files;
    }

    /// <summary>
    /// Parse a Codex JSONL file and extract token usage
    /// </summary>
    public static (Dictionary<string, Dictionary<string, int[]>> Days, long ParsedBytes, string? LastModel, CodexTotals? LastTotals)
        ParseCodexFile(
            string filePath,
            CostUsageDayRange range,
            long startOffset = 0,
            string? initialModel = null,
            CodexTotals? initialTotals = null)
    {
        var days = new Dictionary<string, Dictionary<string, int[]>>();
        var currentModel = initialModel ?? "gpt-5";
        var previousTotals = initialTotals;
        long parsedBytes = startOffset;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (startOffset > 0)
                stream.Position = startOffset;

            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                parsedBytes = stream.Position;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    // Quick check for relevant lines
                    if (!line.Contains("\"type\":\"event_msg\"") && !line.Contains("\"type\":\"turn_context\""))
                        continue;

                    if (line.Contains("\"type\":\"event_msg\"") && !line.Contains("\"token_count\""))
                        continue;

                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("type", out var typeEl))
                        continue;

                    var type = typeEl.GetString();
                    if (type == null)
                        continue;

                    if (!root.TryGetProperty("timestamp", out var tsEl))
                        continue;

                    var tsText = tsEl.GetString();
                    var dayKey = ParseDayKeyFromTimestamp(tsText);
                    if (dayKey == null)
                        continue;

                    if (type == "turn_context")
                    {
                        if (root.TryGetProperty("payload", out var payload))
                        {
                            if (payload.TryGetProperty("model", out var modelEl))
                                currentModel = modelEl.GetString() ?? currentModel;
                            else if (payload.TryGetProperty("info", out var turnInfo) &&
                                     turnInfo.TryGetProperty("model", out modelEl))
                                currentModel = modelEl.GetString() ?? currentModel;
                        }
                        continue;
                    }

                    if (type != "event_msg")
                        continue;

                    if (!root.TryGetProperty("payload", out var payloadEl))
                        continue;

                    if (!payloadEl.TryGetProperty("type", out var payloadType) ||
                        payloadType.GetString() != "token_count")
                        continue;

                    var info = payloadEl.TryGetProperty("info", out var infoEl) ? infoEl : payloadEl;
                    var model = TryGetString(info, "model") ??
                                TryGetString(info, "model_name") ??
                                TryGetString(payloadEl, "model") ??
                                TryGetString(root, "model") ??
                                currentModel;

                    int deltaInput = 0, deltaCached = 0, deltaOutput = 0;

                    if (info.TryGetProperty("total_token_usage", out var totalUsage))
                    {
                        var input = TryGetInt(totalUsage, "input_tokens");
                        var cached = TryGetInt(totalUsage, "cached_input_tokens") ??
                                     TryGetInt(totalUsage, "cache_read_input_tokens");
                        var output = TryGetInt(totalUsage, "output_tokens");

                        deltaInput = Math.Max(0, (input ?? 0) - (previousTotals?.Input ?? 0));
                        deltaCached = Math.Max(0, (cached ?? 0) - (previousTotals?.Cached ?? 0));
                        deltaOutput = Math.Max(0, (output ?? 0) - (previousTotals?.Output ?? 0));

                        previousTotals = new CodexTotals
                        {
                            Input = input ?? 0,
                            Cached = cached ?? 0,
                            Output = output ?? 0
                        };
                    }
                    else if (info.TryGetProperty("last_token_usage", out var lastUsage))
                    {
                        deltaInput = Math.Max(0, TryGetInt(lastUsage, "input_tokens") ?? 0);
                        deltaCached = Math.Max(0, TryGetInt(lastUsage, "cached_input_tokens") ??
                                                   TryGetInt(lastUsage, "cache_read_input_tokens") ?? 0);
                        deltaOutput = Math.Max(0, TryGetInt(lastUsage, "output_tokens") ?? 0);
                    }
                    else
                    {
                        continue;
                    }

                    if (deltaInput == 0 && deltaCached == 0 && deltaOutput == 0)
                        continue;

                    // Clamp cached to input
                    deltaCached = Math.Min(deltaCached, deltaInput);

                    AddToDay(days, dayKey, CostUsagePricing.NormalizeCodexModel(model),
                        deltaInput, deltaCached, deltaOutput, range);
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostUsageScanner", $"Error parsing Codex file {filePath}", ex);
        }

        return (days, parsedBytes, currentModel, previousTotals);
    }

    // MARK: - Claude Scanner

    /// <summary>
    /// Find Claude projects root directories
    /// </summary>
    public static List<string> GetClaudeProjectsRoots(ScanOptions options = default)
    {
        var roots = new List<string>();

        var configDir = options.ClaudeConfigDir ?? Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(configDir))
        {
            foreach (var part in configDir.Split(','))
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed))
                    continue;

                if (trimmed.EndsWith("projects", StringComparison.OrdinalIgnoreCase))
                    roots.Add(trimmed);
                else
                    roots.Add(Path.Combine(trimmed, "projects"));
            }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            roots.Add(Path.Combine(home, ".config", "claude", "projects"));
            roots.Add(Path.Combine(home, ".claude", "projects"));
        }

        return roots;
    }

    /// <summary>
    /// Parse a Claude JSONL file and extract token usage
    /// </summary>
    public static (Dictionary<string, Dictionary<string, int[]>> Days, long ParsedBytes)
        ParseClaudeFile(string filePath, CostUsageDayRange range, long startOffset = 0)
    {
        var days = new Dictionary<string, Dictionary<string, int[]>>();
        var seenKeys = new HashSet<string>();
        long parsedBytes = startOffset;
        const double costScale = 1_000_000_000.0;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (startOffset > 0)
                stream.Position = startOffset;

            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                parsedBytes = stream.Position;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    // Quick check for relevant lines
                    if (!line.Contains("\"type\":\"assistant\""))
                        continue;
                    if (!line.Contains("\"usage\""))
                        continue;

                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("type", out var typeEl) ||
                        typeEl.GetString() != "assistant")
                        continue;

                    if (!root.TryGetProperty("timestamp", out var tsEl))
                        continue;

                    var tsText = tsEl.GetString();
                    var dayKey = ParseDayKeyFromTimestamp(tsText);
                    if (dayKey == null)
                        continue;

                    if (!root.TryGetProperty("message", out var message))
                        continue;

                    var model = TryGetString(message, "model");
                    if (string.IsNullOrEmpty(model))
                        continue;

                    if (!message.TryGetProperty("usage", out var usage))
                        continue;

                    // Deduplicate by message ID + request ID
                    var messageId = TryGetString(message, "id");
                    var requestId = TryGetString(root, "requestId");
                    if (!string.IsNullOrEmpty(messageId) && !string.IsNullOrEmpty(requestId))
                    {
                        var dedupeKey = $"{messageId}:{requestId}";
                        if (seenKeys.Contains(dedupeKey))
                            continue;
                        seenKeys.Add(dedupeKey);
                    }

                    var input = Math.Max(0, TryGetInt(usage, "input_tokens") ?? 0);
                    var cacheRead = Math.Max(0, TryGetInt(usage, "cache_read_input_tokens") ?? 0);
                    var cacheCreate = Math.Max(0, TryGetInt(usage, "cache_creation_input_tokens") ?? 0);
                    var output = Math.Max(0, TryGetInt(usage, "output_tokens") ?? 0);

                    if (input == 0 && cacheRead == 0 && cacheCreate == 0 && output == 0)
                        continue;

                    var cost = CostUsagePricing.ClaudeCostUSD(model, input, cacheRead, cacheCreate, output);
                    var costNanos = cost.HasValue ? (int)(cost.Value * costScale) : 0;

                    AddToClaudeDay(days, dayKey, CostUsagePricing.NormalizeClaudeModel(model),
                        input, cacheRead, cacheCreate, output, costNanos, range);
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostUsageScanner", $"Error parsing Claude file {filePath}", ex);
        }

        return (days, parsedBytes);
    }

    /// <summary>
    /// Scan all Claude JSONL files in a root directory
    /// </summary>
    public static List<string> ListClaudeJsonlFiles(string root)
    {
        var files = new List<string>();

        if (!Directory.Exists(root))
            return files;

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
            {
                files.Add(file);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostUsageScanner", $"Error listing Claude files in {root}", ex);
        }

        return files;
    }

    // MARK: - Helper Methods

    private static string? TryGetString(JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static int? TryGetInt(JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value))
                return value;
        }
        return null;
    }

    private static string? ParseDayKeyFromTimestamp(string? timestamp)
    {
        if (string.IsNullOrEmpty(timestamp))
            return null;

        // Try ISO8601 format first
        if (DateTime.TryParse(timestamp, out var dt))
            return CostUsageDayRange.DayKey(dt);

        // Try extracting date portion from ISO format
        if (timestamp.Length >= 10)
        {
            var datePart = timestamp[..10];
            if (DateTime.TryParseExact(datePart, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out dt))
                return CostUsageDayRange.DayKey(dt);
        }

        return null;
    }

    private static void AddToDay(
        Dictionary<string, Dictionary<string, int[]>> days,
        string dayKey,
        string model,
        int input, int cached, int output,
        CostUsageDayRange range)
    {
        if (!CostUsageDayRange.IsInRange(dayKey, range.ScanSinceKey, range.ScanUntilKey))
            return;

        if (!days.TryGetValue(dayKey, out var dayModels))
        {
            dayModels = new Dictionary<string, int[]>();
            days[dayKey] = dayModels;
        }

        if (!dayModels.TryGetValue(model, out var packed))
        {
            packed = new int[3];
            dayModels[model] = packed;
        }

        packed[0] += input;
        packed[1] += cached;
        packed[2] += output;
    }

    private static void AddToClaudeDay(
        Dictionary<string, Dictionary<string, int[]>> days,
        string dayKey,
        string model,
        int input, int cacheRead, int cacheCreate, int output, int costNanos,
        CostUsageDayRange range)
    {
        if (!CostUsageDayRange.IsInRange(dayKey, range.ScanSinceKey, range.ScanUntilKey))
            return;

        if (!days.TryGetValue(dayKey, out var dayModels))
        {
            dayModels = new Dictionary<string, int[]>();
            days[dayKey] = dayModels;
        }

        if (!dayModels.TryGetValue(model, out var packed))
        {
            packed = new int[5];
            dayModels[model] = packed;
        }

        packed[0] += input;
        packed[1] += cacheRead;
        packed[2] += cacheCreate;
        packed[3] += output;
        packed[4] += costNanos;
    }

    /// <summary>
    /// Apply file days to cache (add or subtract)
    /// </summary>
    public static void ApplyFileDays(
        CostUsageCache cache,
        Dictionary<string, Dictionary<string, int[]>> fileDays,
        int sign)
    {
        foreach (var (day, models) in fileDays)
        {
            if (!cache.Days.TryGetValue(day, out var dayModels))
            {
                if (sign > 0)
                {
                    dayModels = new Dictionary<string, int[]>();
                    cache.Days[day] = dayModels;
                }
                else
                {
                    continue;
                }
            }

            foreach (var (model, packed) in models)
            {
                if (!dayModels.TryGetValue(model, out var existing))
                {
                    if (sign > 0)
                    {
                        dayModels[model] = (int[])packed.Clone();
                    }
                    continue;
                }

                for (int i = 0; i < Math.Max(existing.Length, packed.Length); i++)
                {
                    if (i < existing.Length && i < packed.Length)
                    {
                        existing[i] = Math.Max(0, existing[i] + sign * packed[i]);
                    }
                    else if (sign > 0 && i < packed.Length)
                    {
                        // Expand existing array
                        var newArr = new int[packed.Length];
                        Array.Copy(existing, newArr, existing.Length);
                        newArr[i] = packed[i];
                        dayModels[model] = newArr;
                        existing = newArr;
                    }
                }

                // Remove if all zeros
                if (Array.TrueForAll(existing, v => v == 0))
                    dayModels.Remove(model);
            }

            if (dayModels.Count == 0)
                cache.Days.Remove(day);
        }
    }

    /// <summary>
    /// Merge delta days into existing file days
    /// </summary>
    public static void MergeFileDays(
        Dictionary<string, Dictionary<string, int[]>> existing,
        Dictionary<string, Dictionary<string, int[]>> delta)
    {
        foreach (var (day, models) in delta)
        {
            if (!existing.TryGetValue(day, out var dayModels))
            {
                dayModels = new Dictionary<string, int[]>();
                existing[day] = dayModels;
            }

            foreach (var (model, packed) in models)
            {
                if (!dayModels.TryGetValue(model, out var existingPacked))
                {
                    dayModels[model] = (int[])packed.Clone();
                    continue;
                }

                var merged = new int[Math.Max(existingPacked.Length, packed.Length)];
                for (int i = 0; i < merged.Length; i++)
                {
                    var a = i < existingPacked.Length ? existingPacked[i] : 0;
                    var b = i < packed.Length ? packed[i] : 0;
                    merged[i] = a + b;
                }
                dayModels[model] = merged;
            }
        }
    }

    /// <summary>
    /// Prune days outside the scan range
    /// </summary>
    public static void PruneDays(CostUsageCache cache, CostUsageDayRange range)
    {
        var toRemove = new List<string>();
        foreach (var key in cache.Days.Keys)
        {
            if (!CostUsageDayRange.IsInRange(key, range.ScanSinceKey, range.ScanUntilKey))
                toRemove.Add(key);
        }
        foreach (var key in toRemove)
            cache.Days.Remove(key);
    }
}
