using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Core.CostUsage;

/// <summary>
/// Cache structure for persisting scanned cost data
/// </summary>
public class CostUsageCache
{
    [JsonPropertyName("lastScanUnixMs")]
    public long LastScanUnixMs { get; set; }

    [JsonPropertyName("files")]
    public Dictionary<string, CostUsageFileUsage> Files { get; set; } = new();

    [JsonPropertyName("days")]
    public Dictionary<string, Dictionary<string, int[]>> Days { get; set; } = new();

    [JsonPropertyName("roots")]
    public Dictionary<string, long>? Roots { get; set; }
}

/// <summary>
/// Per-file usage tracking with incremental scanning support
/// </summary>
public class CostUsageFileUsage
{
    [JsonPropertyName("mtimeUnixMs")]
    public long MtimeUnixMs { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("parsedBytes")]
    public long? ParsedBytes { get; set; }

    [JsonPropertyName("days")]
    public Dictionary<string, Dictionary<string, int[]>> Days { get; set; } = new();

    // Codex-specific: track the last model and cumulative totals for incremental parsing
    [JsonPropertyName("lastModel")]
    public string? LastModel { get; set; }

    [JsonPropertyName("lastTotals")]
    public CodexTotals? LastTotals { get; set; }
}

/// <summary>
/// Cumulative token totals for Codex incremental parsing
/// </summary>
public class CodexTotals
{
    [JsonPropertyName("input")]
    public int Input { get; set; }

    [JsonPropertyName("cached")]
    public int Cached { get; set; }

    [JsonPropertyName("output")]
    public int Output { get; set; }
}

/// <summary>
/// I/O operations for the cost usage cache
/// </summary>
public static class CostUsageCacheIO
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static string GetCacheDirectory()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuoteBar",
            "cost-usage");
        Directory.CreateDirectory(appDataPath);
        return appDataPath;
    }

    private static string GetCacheFilePath(string providerId)
    {
        return Path.Combine(GetCacheDirectory(), $"{providerId.ToLowerInvariant()}-cache.json");
    }

    /// <summary>
    /// Load cache for a provider, returns empty cache if not found
    /// </summary>
    public static CostUsageCache Load(string providerId)
    {
        try
        {
            var path = GetCacheFilePath(providerId);
            if (!File.Exists(path))
                return new CostUsageCache();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CostUsageCache>(json, _jsonOptions) ?? new CostUsageCache();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostUsageCacheIO", $"Failed to load cache for {providerId}", ex);
            return new CostUsageCache();
        }
    }

    /// <summary>
    /// Save cache for a provider
    /// </summary>
    public static void Save(string providerId, CostUsageCache cache)
    {
        try
        {
            var path = GetCacheFilePath(providerId);
            var json = JsonSerializer.Serialize(cache, _jsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostUsageCacheIO", $"Failed to save cache for {providerId}", ex);
        }
    }

    /// <summary>
    /// Clear cache for a provider
    /// </summary>
    public static void Clear(string providerId)
    {
        try
        {
            var path = GetCacheFilePath(providerId);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostUsageCacheIO", $"Failed to clear cache for {providerId}", ex);
        }
    }

    /// <summary>
    /// Clear all cost usage caches
    /// </summary>
    public static void ClearAll()
    {
        try
        {
            var dir = GetCacheDirectory();
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "*-cache.json"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostUsageCacheIO", "Failed to clear all caches", ex);
        }
    }
}
