using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace QuoteBar.Core.Models;

/// <summary>
/// Represents a single API request/response pair with associated metadata.
/// Equivalent to RequestLog in Quotio's Swift implementation.
/// </summary>
public record RequestLog
{
    /// <summary>Unique identifier for this request</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>When the request was made</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>HTTP method (GET, POST, etc.)</summary>
    public string Method { get; init; } = string.Empty;

    /// <summary>Request endpoint path (e.g., "/v1/messages")</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>AI provider (e.g., "claude", "gemini", "openai", "cursor", "codex")</summary>
    public string? Provider { get; init; }

    /// <summary>Model used (e.g., "claude-sonnet-4", "gemini-2.0-flash")</summary>
    public string? Model { get; init; }

    /// <summary>Number of input tokens (from API response)</summary>
    public int? InputTokens { get; init; }

    /// <summary>Number of output tokens (from API response)</summary>
    public int? OutputTokens { get; init; }

    /// <summary>Total tokens (input + output)</summary>
    [JsonIgnore]
    public int? TotalTokens => (InputTokens, OutputTokens) switch
    {
        (int input, int output) => input + output,
        (int input, null) => input,
        (null, int output) => output,
        _ => null
    };

    /// <summary>Request duration in milliseconds</summary>
    public int DurationMs { get; init; }

    /// <summary>HTTP status code from response</summary>
    public int? StatusCode { get; init; }

    /// <summary>Request body size in bytes</summary>
    public int RequestSize { get; init; }

    /// <summary>Response body size in bytes</summary>
    public int ResponseSize { get; init; }

    /// <summary>Error message if request failed</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Whether the request was successful (2xx status)</summary>
    [JsonIgnore]
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>Whether this was a rate limit error (429)</summary>
    [JsonIgnore]
    public bool IsRateLimited => StatusCode == 429;

    /// <summary>Formatted duration for display</summary>
    [JsonIgnore]
    public string FormattedDuration => DurationMs < 1000
        ? $"{DurationMs}ms"
        : $"{DurationMs / 1000.0:F1}s";

    /// <summary>Formatted token count for display</summary>
    [JsonIgnore]
    public string? FormattedTokens => TotalTokens switch
    {
        >= 1_000_000 => $"{TotalTokens.Value / 1_000_000.0:F1}M",
        >= 1_000 => $"{TotalTokens.Value / 1_000.0:F1}K",
        int val => val.ToString(),
        null => null
    };

    /// <summary>Status badge text</summary>
    [JsonIgnore]
    public string StatusBadge => StatusCode?.ToString() ?? "?";

    /// <summary>Formatted timestamp (HH:mm:ss)</summary>
    [JsonIgnore]
    public string FormattedTime => Timestamp.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>Formatted date for grouping</summary>
    [JsonIgnore]
    public string FormattedDate => Timestamp.ToLocalTime().ToString("MMM dd, yyyy");
}

/// <summary>
/// Aggregated statistics for request history
/// </summary>
public record RequestStats
{
    /// <summary>Total number of requests</summary>
    public int TotalRequests { get; init; }

    /// <summary>Number of successful requests (2xx)</summary>
    public int SuccessfulRequests { get; init; }

    /// <summary>Number of failed requests</summary>
    public int FailedRequests { get; init; }

    /// <summary>Number of rate-limited requests (429)</summary>
    public int RateLimitedRequests { get; init; }

    /// <summary>Total input tokens across all requests</summary>
    public int TotalInputTokens { get; init; }

    /// <summary>Total output tokens across all requests</summary>
    public int TotalOutputTokens { get; init; }

    /// <summary>Total tokens (input + output)</summary>
    [JsonIgnore]
    public int TotalTokens => TotalInputTokens + TotalOutputTokens;

    /// <summary>Average request duration in milliseconds</summary>
    public int AverageDurationMs { get; init; }

    /// <summary>Statistics by provider</summary>
    public Dictionary<string, ProviderRequestStats> ByProvider { get; init; } = new();

    /// <summary>Statistics by model</summary>
    public Dictionary<string, ModelRequestStats> ByModel { get; init; } = new();

    /// <summary>Success rate as percentage (0-100)</summary>
    [JsonIgnore]
    public double SuccessRate => TotalRequests > 0
        ? (double)SuccessfulRequests / TotalRequests * 100
        : 0;

    /// <summary>Create empty stats</summary>
    public static RequestStats Empty => new();
}

/// <summary>
/// Statistics for a specific provider
/// </summary>
public record ProviderRequestStats
{
    public string Provider { get; init; } = string.Empty;
    public int RequestCount { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int AverageDurationMs { get; init; }
    public int RateLimitedCount { get; init; }

    [JsonIgnore]
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// Statistics for a specific model
/// </summary>
public record ModelRequestStats
{
    public string Model { get; init; } = string.Empty;
    public string? Provider { get; init; }
    public int RequestCount { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int AverageDurationMs { get; init; }

    [JsonIgnore]
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// Container for persisted request history.
/// Handles storage, trimming, and statistics calculation.
/// </summary>
public class RequestHistoryStore
{
    /// <summary>Version for migration support</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Request log entries (newest first)</summary>
    public List<RequestLog> Entries { get; set; } = new();

    /// <summary>Maximum entries to keep (memory-optimized)</summary>
    public const int MaxEntries = 50;

    /// <summary>Current storage version</summary>
    public const int CurrentVersion = 1;

    /// <summary>Create empty store</summary>
    public static RequestHistoryStore Empty => new();

    /// <summary>Add entry and trim if needed</summary>
    public void AddEntry(RequestLog entry)
    {
        Entries.Insert(0, entry);

        // Trim oldest entries if exceeding max
        if (Entries.Count > MaxEntries)
        {
            Entries = Entries.Take(MaxEntries).ToList();
        }
    }

    /// <summary>Trim to reduced count (for background/memory pressure)</summary>
    public void TrimTo(int count)
    {
        if (Entries.Count > count)
        {
            Entries = Entries.Take(count).ToList();
        }
    }

    /// <summary>Calculate aggregate statistics from entries</summary>
    public RequestStats CalculateStats()
    {
        if (Entries.Count == 0)
            return RequestStats.Empty;

        var totalInput = 0;
        var totalOutput = 0;
        var totalDuration = 0;
        var successCount = 0;
        var rateLimitedCount = 0;

        var providerData = new Dictionary<string, (int count, int input, int output, int duration, int rateLimited)>();
        var modelData = new Dictionary<string, (string? provider, int count, int input, int output, int duration)>();

        foreach (var entry in Entries)
        {
            totalInput += entry.InputTokens ?? 0;
            totalOutput += entry.OutputTokens ?? 0;
            totalDuration += entry.DurationMs;

            if (entry.IsSuccess)
                successCount++;

            if (entry.IsRateLimited)
                rateLimitedCount++;

            // Aggregate by provider
            if (!string.IsNullOrEmpty(entry.Provider))
            {
                if (!providerData.TryGetValue(entry.Provider, out var pd))
                    pd = (0, 0, 0, 0, 0);

                providerData[entry.Provider] = (
                    pd.count + 1,
                    pd.input + (entry.InputTokens ?? 0),
                    pd.output + (entry.OutputTokens ?? 0),
                    pd.duration + entry.DurationMs,
                    pd.rateLimited + (entry.IsRateLimited ? 1 : 0)
                );
            }

            // Aggregate by model
            if (!string.IsNullOrEmpty(entry.Model))
            {
                if (!modelData.TryGetValue(entry.Model, out var md))
                    md = (entry.Provider, 0, 0, 0, 0);

                modelData[entry.Model] = (
                    md.provider,
                    md.count + 1,
                    md.input + (entry.InputTokens ?? 0),
                    md.output + (entry.OutputTokens ?? 0),
                    md.duration + entry.DurationMs
                );
            }
        }

        var byProvider = providerData.ToDictionary(
            kvp => kvp.Key,
            kvp => new ProviderRequestStats
            {
                Provider = kvp.Key,
                RequestCount = kvp.Value.count,
                InputTokens = kvp.Value.input,
                OutputTokens = kvp.Value.output,
                AverageDurationMs = kvp.Value.count > 0 ? kvp.Value.duration / kvp.Value.count : 0,
                RateLimitedCount = kvp.Value.rateLimited
            });

        var byModel = modelData.ToDictionary(
            kvp => kvp.Key,
            kvp => new ModelRequestStats
            {
                Model = kvp.Key,
                Provider = kvp.Value.provider,
                RequestCount = kvp.Value.count,
                InputTokens = kvp.Value.input,
                OutputTokens = kvp.Value.output,
                AverageDurationMs = kvp.Value.count > 0 ? kvp.Value.duration / kvp.Value.count : 0
            });

        return new RequestStats
        {
            TotalRequests = Entries.Count,
            SuccessfulRequests = successCount,
            FailedRequests = Entries.Count - successCount,
            RateLimitedRequests = rateLimitedCount,
            TotalInputTokens = totalInput,
            TotalOutputTokens = totalOutput,
            AverageDurationMs = Entries.Count > 0 ? totalDuration / Entries.Count : 0,
            ByProvider = byProvider,
            ByModel = byModel
        };
    }
}

/// <summary>
/// Extension methods for token formatting
/// </summary>
public static class TokenFormatExtensions
{
    /// <summary>Format large numbers with K/M suffix</summary>
    public static string FormatAsTokenCount(this int value) => value switch
    {
        >= 1_000_000 => $"{value / 1_000_000.0:F1}M",
        >= 1_000 => $"{value / 1_000.0:F1}K",
        _ => value.ToString()
    };
}
