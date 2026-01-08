using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Core.CostUsage;

/// <summary>
/// Supported providers for local cost usage scanning
/// </summary>
public enum CostUsageProvider
{
    Codex,
    Claude,
    Copilot
}

/// <summary>
/// Fetches and computes cost usage from local JSONL logs
/// Based on CodexBar's CostUsageFetcher.swift
/// </summary>
public class CostUsageFetcher
{
    private static CostUsageFetcher? _instance;
    public static CostUsageFetcher Instance => _instance ??= new CostUsageFetcher();

    private readonly object _lock = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Load token cost snapshot for a provider
    /// </summary>
    public async Task<CostUsageTokenSnapshot> LoadTokenSnapshotAsync(
        CostUsageProvider provider,
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => LoadTokenSnapshotSync(provider, forceRefresh), cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private CostUsageTokenSnapshot LoadTokenSnapshotSync(CostUsageProvider provider, bool forceRefresh)
    {
        var now = DateTime.UtcNow;
        var until = now;
        var since = now.AddDays(-29); // Rolling 30-day window (inclusive)
        var range = new CostUsageDayRange(since, until);

        var providerId = provider.ToString().ToLowerInvariant();
        
        // Copilot doesn't use local logs - it uses API data
        if (provider == CostUsageProvider.Copilot)
        {
            return LoadCopilotSnapshot(now);
        }
        
        var cache = CostUsageCacheIO.Load(providerId);
        var nowMs = (long)(now.Subtract(DateTime.UnixEpoch).TotalMilliseconds);

        const int refreshMinIntervalMs = 60_000; // 1 minute
        var shouldRefresh = forceRefresh ||
                            cache.LastScanUnixMs == 0 ||
                            nowMs - cache.LastScanUnixMs > refreshMinIntervalMs;

        var report = provider switch
        {
            CostUsageProvider.Codex => LoadCodexDaily(cache, range, shouldRefresh, forceRefresh, providerId, nowMs),
            CostUsageProvider.Claude => LoadClaudeDaily(cache, range, shouldRefresh, forceRefresh, providerId, nowMs),
            _ => new CostUsageDailyReport()
        };

        return ToTokenSnapshot(report, now);
    }

    /// <summary>
    /// Load Copilot usage from cached API data
    /// </summary>
    private CostUsageTokenSnapshot LoadCopilotSnapshot(DateTime now)
    {
        try
        {
            // Load cached Copilot data
            var cache = CostUsageCacheIO.Load("copilot");
            
            if (cache.Days.Count == 0)
            {
                DebugLogger.Log("CostUsageFetcher", "No cached Copilot data found");
                return CostUsageTokenSnapshot.Empty;
            }

            var entries = new List<CostUsageDailyEntry>();
            var today = now.Date;
            var last30Days = Enumerable.Range(0, 30)
                .Select(i => CostUsageDayRange.DayKey(today.AddDays(-i)))
                .ToHashSet();

            double totalCost = 0;
            int totalRequests = 0;
            var modelTotals = new Dictionary<string, (double requests, double cost)>(StringComparer.OrdinalIgnoreCase);

            foreach (var (dayKey, models) in cache.Days)
            {
                if (!last30Days.Contains(dayKey))
                    continue;

                double dayCost = 0;
                int dayRequests = 0;
                var dayBreakdowns = new List<ModelBreakdown>();

                foreach (var (model, packed) in models)
                {
                    var requests = packed.Length > 0 ? packed[0] : 0;
                    var costNanos = packed.Length > 1 ? packed[1] : 0;
                    var cost = costNanos / 1_000_000_000.0;
                    
                    if (cost == 0 && requests > 0)
                    {
                        cost = CostUsagePricing.CopilotCostUSD(model, requests) ?? 0;
                    }

                    dayRequests += requests;
                    dayCost += cost;
                    
                    dayBreakdowns.Add(new ModelBreakdown
                    {
                        ModelName = model,
                        CostUSD = cost,
                        InputTokens = requests, // Use requests as "tokens" for display
                        OutputTokens = 0
                    });

                    if (modelTotals.TryGetValue(model, out var existing))
                    {
                        modelTotals[model] = (existing.requests + requests, existing.cost + cost);
                    }
                    else
                    {
                        modelTotals[model] = (requests, cost);
                    }
                }

                if (dayRequests > 0 || dayCost > 0)
                {
                    entries.Add(new CostUsageDailyEntry
                    {
                        Date = dayKey,
                        TotalTokens = dayRequests, // Premium requests
                        CostUSD = dayCost,
                        ModelsUsed = dayBreakdowns.Select(b => b.ModelName).ToList(),
                        ModelBreakdowns = dayBreakdowns.OrderByDescending(b => b.CostUSD ?? 0).Take(5).ToList()
                    });

                    totalRequests += dayRequests;
                    totalCost += dayCost;
                }
            }

            // Sort entries by date descending
            entries = entries.OrderByDescending(e => e.Date).ToList();

            var currentDay = entries.FirstOrDefault();

            // Build top models breakdown
            var topModels = modelTotals
                .OrderByDescending(m => m.Value.cost)
                .Take(5)
                .Select(m => new ModelBreakdown
                {
                    ModelName = m.Key,
                    CostUSD = m.Value.cost,
                    InputTokens = (int)m.Value.requests
                })
                .ToList();

            return new CostUsageTokenSnapshot
            {
                SessionTokens = currentDay?.TotalTokens,
                SessionCostUSD = currentDay?.CostUSD,
                Last30DaysTokens = totalRequests,
                Last30DaysCostUSD = totalCost > 0 ? totalCost : null,
                Daily = entries,
                UpdatedAt = now
            };
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostUsageFetcher", "Failed to load Copilot snapshot", ex);
            return CostUsageTokenSnapshot.Empty;
        }
    }

    /// <summary>
    /// Save Copilot usage data from API response
    /// </summary>
    public void SaveCopilotUsageData(
        DateTime date,
        Dictionary<string, double> modelUsage,
        Dictionary<string, double>? modelCosts = null)
    {
        try
        {
            var cache = CostUsageCacheIO.Load("copilot");
            var dayKey = CostUsageDayRange.DayKey(date);
            
            var models = new Dictionary<string, int[]>();
            const double costScale = 1_000_000_000.0;

            foreach (var (model, requests) in modelUsage)
            {
                if (requests <= 0) continue;
                
                var cost = modelCosts?.GetValueOrDefault(model) ?? 
                          CostUsagePricing.CopilotCostUSD(model, requests) ?? 0;
                var costNanos = (int)(cost * costScale);
                
                models[model] = new[] { (int)Math.Round(requests), costNanos };
            }

            if (models.Count > 0)
            {
                cache.Days[dayKey] = models;
                cache.LastScanUnixMs = (long)(DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalMilliseconds);
                
                // Prune old data (keep last 30 days)
                var cutoffDate = date.AddDays(-30);
                var cutoffKey = CostUsageDayRange.DayKey(cutoffDate);
                var keysToRemove = cache.Days.Keys.Where(k => string.CompareOrdinal(k, cutoffKey) < 0).ToList();
                foreach (var key in keysToRemove)
                {
                    cache.Days.Remove(key);
                }
                
                CostUsageCacheIO.Save("copilot", cache);
                DebugLogger.Log("CostUsageFetcher", $"Saved Copilot usage: {modelUsage.Count} models, {modelUsage.Values.Sum()} requests");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("CostUsageFetcher", "Failed to save Copilot usage data", ex);
        }
    }

    private CostUsageDailyReport LoadCodexDaily(
        CostUsageCache cache,
        CostUsageDayRange range,
        bool shouldRefresh,
        bool forceRescan,
        string providerId,
        long nowMs)
    {
        if (!shouldRefresh)
            return BuildCodexReportFromCache(cache, range);

        if (forceRescan)
            cache = new CostUsageCache();

        var root = CostUsageScanner.GetCodexSessionsRoot();
        if (!Directory.Exists(root))
        {
            DebugLogger.Log("CostUsageFetcher", $"Codex sessions root not found: {root}");
            return new CostUsageDailyReport();
        }

        var files = CostUsageScanner.ListCodexSessionFiles(root, range);
        var filePathsInScan = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

        DebugLogger.Log("CostUsageFetcher", $"Scanning {files.Count} Codex files from {root}");

        foreach (var filePath in files)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var mtimeMs = (long)(fileInfo.LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalMilliseconds);
                var size = fileInfo.Length;

                // Check if file is unchanged
                if (cache.Files.TryGetValue(filePath, out var cached) &&
                    cached.MtimeUnixMs == mtimeMs &&
                    cached.Size == size)
                {
                    continue;
                }

                // Try incremental parsing
                if (cached != null && 
                    size > cached.Size && 
                    cached.ParsedBytes > 0 && 
                    cached.ParsedBytes <= size &&
                    cached.LastTotals != null)
                {
                    var delta = CostUsageScanner.ParseCodexFile(
                        filePath, range, cached.ParsedBytes.Value, 
                        cached.LastModel, cached.LastTotals);

                    if (delta.Days.Count > 0)
                        CostUsageScanner.ApplyFileDays(cache, delta.Days, 1);

                    CostUsageScanner.MergeFileDays(cached.Days, delta.Days);

                    cache.Files[filePath] = new CostUsageFileUsage
                    {
                        MtimeUnixMs = mtimeMs,
                        Size = size,
                        Days = cached.Days,
                        ParsedBytes = delta.ParsedBytes,
                        LastModel = delta.LastModel,
                        LastTotals = delta.LastTotals
                    };
                    continue;
                }

                // Full parse required - remove old data first
                if (cached != null)
                    CostUsageScanner.ApplyFileDays(cache, cached.Days, -1);

                var parsed = CostUsageScanner.ParseCodexFile(filePath, range);

                cache.Files[filePath] = new CostUsageFileUsage
                {
                    MtimeUnixMs = mtimeMs,
                    Size = size,
                    Days = parsed.Days,
                    ParsedBytes = parsed.ParsedBytes,
                    LastModel = parsed.LastModel,
                    LastTotals = parsed.LastTotals
                };

                CostUsageScanner.ApplyFileDays(cache, parsed.Days, 1);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("CostUsageFetcher", $"Error processing Codex file {filePath}", ex);
            }
        }

        // Remove stale file entries
        var staleFiles = cache.Files.Keys
            .Where(k => !filePathsInScan.Contains(k))
            .ToList();
        foreach (var path in staleFiles)
        {
            if (cache.Files.TryGetValue(path, out var old))
                CostUsageScanner.ApplyFileDays(cache, old.Days, -1);
            cache.Files.Remove(path);
        }

        CostUsageScanner.PruneDays(cache, range);
        cache.LastScanUnixMs = nowMs;
        CostUsageCacheIO.Save(providerId, cache);

        return BuildCodexReportFromCache(cache, range);
    }

    private CostUsageDailyReport LoadClaudeDaily(
        CostUsageCache cache,
        CostUsageDayRange range,
        bool shouldRefresh,
        bool forceRescan,
        string providerId,
        long nowMs)
    {
        if (!shouldRefresh)
            return BuildClaudeReportFromCache(cache, range);

        // Always do a full rescan to avoid incremental parsing bugs
        // The cost of a full scan is minimal for Claude logs
        cache = new CostUsageCache();

        var roots = CostUsageScanner.GetClaudeProjectsRoots();
        var allFiles = new List<string>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            DebugLogger.Log("CostUsageFetcher", $"Scanning Claude files from {root}");
            var files = CostUsageScanner.ListClaudeJsonlFiles(root);
            allFiles.AddRange(files);
        }

        DebugLogger.Log("CostUsageFetcher", $"Found {allFiles.Count} Claude log files");

        foreach (var filePath in allFiles)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var mtimeMs = (long)(fileInfo.LastWriteTimeUtc.Subtract(DateTime.UnixEpoch).TotalMilliseconds);
                var size = fileInfo.Length;

                // Always do full parse
                var parsed = CostUsageScanner.ParseClaudeFile(filePath, range);

                if (parsed.Days.Count > 0)
                {
                    cache.Files[filePath] = new CostUsageFileUsage
                    {
                        MtimeUnixMs = mtimeMs,
                        Size = size,
                        Days = parsed.Days,
                        ParsedBytes = parsed.ParsedBytes
                    };

                    CostUsageScanner.ApplyFileDays(cache, parsed.Days, 1);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("CostUsageFetcher", $"Error processing Claude file {filePath}", ex);
            }
        }

        CostUsageScanner.PruneDays(cache, range);
        cache.LastScanUnixMs = nowMs;
        CostUsageCacheIO.Save(providerId, cache);

        DebugLogger.Log("CostUsageFetcher", $"Claude scan complete: {cache.Days.Count} days with data");

        return BuildClaudeReportFromCache(cache, range);
    }


    private CostUsageDailyReport BuildCodexReportFromCache(CostUsageCache cache, CostUsageDayRange range)
    {
        var entries = new List<CostUsageDailyEntry>();
        int totalInput = 0, totalOutput = 0, totalTokens = 0;
        double totalCost = 0;
        bool costSeen = false;

        var dayKeys = cache.Days.Keys
            .Where(k => CostUsageDayRange.IsInRange(k, range.SinceKey, range.UntilKey))
            .OrderBy(k => k)
            .ToList();

        foreach (var day in dayKeys)
        {
            if (!cache.Days.TryGetValue(day, out var models))
                continue;

            var modelNames = models.Keys.OrderBy(m => m).ToList();
            int dayInput = 0, dayOutput = 0;
            double dayCost = 0;
            bool dayCostSeen = false;
            var breakdown = new List<ModelBreakdown>();

            foreach (var model in modelNames)
            {
                var packed = models[model];
                var input = packed.Length > 0 ? packed[0] : 0;
                var cached = packed.Length > 1 ? packed[1] : 0;
                var output = packed.Length > 2 ? packed[2] : 0;

                dayInput += input;
                dayOutput += output;

                var cost = CostUsagePricing.CodexCostUSD(model, input, cached, output);
                breakdown.Add(new ModelBreakdown
                {
                    ModelName = model,
                    CostUSD = cost,
                    InputTokens = input,
                    OutputTokens = output,
                    CacheReadTokens = cached
                });

                if (cost.HasValue)
                {
                    dayCost += cost.Value;
                    dayCostSeen = true;
                }
            }

            var dayTotal = dayInput + dayOutput;
            entries.Add(new CostUsageDailyEntry
            {
                Date = day,
                InputTokens = dayInput,
                OutputTokens = dayOutput,
                TotalTokens = dayTotal,
                CostUSD = dayCostSeen ? dayCost : null,
                ModelsUsed = modelNames,
                ModelBreakdowns = breakdown.OrderByDescending(b => b.CostUSD ?? 0).Take(3).ToList()
            });

            totalInput += dayInput;
            totalOutput += dayOutput;
            totalTokens += dayTotal;
            if (dayCostSeen)
            {
                totalCost += dayCost;
                costSeen = true;
            }
        }

        return new CostUsageDailyReport
        {
            Data = entries,
            Summary = entries.Count > 0 ? new CostUsageSummary
            {
                TotalInputTokens = totalInput,
                TotalOutputTokens = totalOutput,
                TotalTokens = totalTokens,
                TotalCostUSD = costSeen ? totalCost : null
            } : null
        };
    }

    private CostUsageDailyReport BuildClaudeReportFromCache(CostUsageCache cache, CostUsageDayRange range)
    {
        var entries = new List<CostUsageDailyEntry>();
        int totalInput = 0, totalOutput = 0, totalCacheRead = 0, totalCacheCreate = 0, totalTokens = 0;
        double totalCost = 0;
        bool costSeen = false;
        const double costScale = 1_000_000_000.0;

        var dayKeys = cache.Days.Keys
            .Where(k => CostUsageDayRange.IsInRange(k, range.SinceKey, range.UntilKey))
            .OrderBy(k => k)
            .ToList();

        foreach (var day in dayKeys)
        {
            if (!cache.Days.TryGetValue(day, out var models))
                continue;

            var modelNames = models.Keys.OrderBy(m => m).ToList();
            int dayInput = 0, dayOutput = 0, dayCacheRead = 0, dayCacheCreate = 0;
            double dayCost = 0;
            bool dayCostSeen = false;
            var breakdown = new List<ModelBreakdown>();

            foreach (var model in modelNames)
            {
                var packed = models[model];
                var input = packed.Length > 0 ? packed[0] : 0;
                var cacheRead = packed.Length > 1 ? packed[1] : 0;
                var cacheCreate = packed.Length > 2 ? packed[2] : 0;
                var output = packed.Length > 3 ? packed[3] : 0;
                var cachedCost = packed.Length > 4 ? packed[4] : 0;

                dayInput += input;
                dayCacheRead += cacheRead;
                dayCacheCreate += cacheCreate;
                dayOutput += output;

                double? cost = cachedCost > 0
                    ? cachedCost / costScale
                    : CostUsagePricing.ClaudeCostUSD(model, input, cacheRead, cacheCreate, output);

                breakdown.Add(new ModelBreakdown
                {
                    ModelName = model,
                    CostUSD = cost,
                    InputTokens = input,
                    OutputTokens = output,
                    CacheReadTokens = cacheRead,
                    CacheCreationTokens = cacheCreate
                });

                if (cost.HasValue)
                {
                    dayCost += cost.Value;
                    dayCostSeen = true;
                }
            }

            var dayTotal = dayInput + dayCacheRead + dayCacheCreate + dayOutput;
            entries.Add(new CostUsageDailyEntry
            {
                Date = day,
                InputTokens = dayInput,
                OutputTokens = dayOutput,
                CacheReadTokens = dayCacheRead,
                CacheCreationTokens = dayCacheCreate,
                TotalTokens = dayTotal,
                CostUSD = dayCostSeen ? dayCost : null,
                ModelsUsed = modelNames,
                ModelBreakdowns = breakdown.OrderByDescending(b => b.CostUSD ?? 0).Take(3).ToList()
            });

            totalInput += dayInput;
            totalOutput += dayOutput;
            totalCacheRead += dayCacheRead;
            totalCacheCreate += dayCacheCreate;
            totalTokens += dayTotal;
            if (dayCostSeen)
            {
                totalCost += dayCost;
                costSeen = true;
            }
        }

        return new CostUsageDailyReport
        {
            Data = entries,
            Summary = entries.Count > 0 ? new CostUsageSummary
            {
                TotalInputTokens = totalInput,
                TotalOutputTokens = totalOutput,
                TotalCacheReadTokens = totalCacheRead,
                TotalCacheCreationTokens = totalCacheCreate,
                TotalTokens = totalTokens,
                TotalCostUSD = costSeen ? totalCost : null
            } : null
        };
    }

    private static CostUsageTokenSnapshot ToTokenSnapshot(CostUsageDailyReport report, DateTime now)
    {
        if (report.Data.Count == 0)
            return CostUsageTokenSnapshot.Empty;

        // Find the most recent day (session)
        var currentDay = report.Data
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.CostUSD ?? 0)
            .ThenByDescending(e => e.TotalTokens ?? 0)
            .FirstOrDefault();

        // Prefer summary totals; fall back to summing entries
        var totalCost = report.Summary?.TotalCostUSD ??
                        report.Data.Where(e => e.CostUSD.HasValue).Sum(e => e.CostUSD!.Value);
        var totalTokens = report.Summary?.TotalTokens ??
                          report.Data.Where(e => e.TotalTokens.HasValue).Sum(e => e.TotalTokens!.Value);

        return new CostUsageTokenSnapshot
        {
            SessionTokens = currentDay?.TotalTokens,
            SessionCostUSD = currentDay?.CostUSD,
            Last30DaysTokens = totalTokens > 0 ? totalTokens : null,
            Last30DaysCostUSD = totalCost > 0 ? totalCost : null,
            Daily = report.Data,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Get cost snapshot for a specific provider (for display in popup)
    /// </summary>
    public async Task<CostUsageTokenSnapshot?> GetCostSnapshotForProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        return providerId.ToLowerInvariant() switch
        {
            "codex" => await LoadTokenSnapshotAsync(CostUsageProvider.Codex, false, cancellationToken),
            "claude" => await LoadTokenSnapshotAsync(CostUsageProvider.Claude, false, cancellationToken),
            "copilot" => await LoadTokenSnapshotAsync(CostUsageProvider.Copilot, false, cancellationToken),
            _ => null
        };
    }

    /// <summary>
    /// Force refresh cost data for a provider
    /// </summary>
    public async Task<CostUsageTokenSnapshot> RefreshCostSnapshotAsync(
        CostUsageProvider provider,
        CancellationToken cancellationToken = default)
    {
        return await LoadTokenSnapshotAsync(provider, forceRefresh: true, cancellationToken);
    }
}
