using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using QuoteBar.Core.Models;

namespace QuoteBar.Core.Services;

/// <summary>
/// Service for tracking API request history with persistence.
/// Port of Quotio's RequestTracker.swift to C# for Windows.
/// 
/// Features:
/// - Tracks all API requests (method, endpoint, provider, model, tokens, duration, status)
/// - Persists history to JSON file in %LocalAppData%\QuoteBar\
/// - Calculates aggregate statistics by provider and model
/// - Auto-trims to MaxEntries to limit memory usage
/// - Thread-safe singleton pattern
/// </summary>
public sealed class RequestTracker
{
    // Singleton
    private static readonly Lazy<RequestTracker> _instance = new(() => new RequestTracker());
    public static RequestTracker Instance => _instance.Value;

    // Storage
    private RequestHistoryStore _store = RequestHistoryStore.Empty;
    private readonly object _lock = new();
    private readonly string _storagePath;

    // Public properties
    public IReadOnlyList<RequestLog> RequestHistory
    {
        get
        {
            lock (_lock)
            {
                return _store.Entries.ToList().AsReadOnly();
            }
        }
    }

    public RequestStats Stats
    {
        get
        {
            lock (_lock)
            {
                return _store.CalculateStats();
            }
        }
    }

    public bool IsActive { get; private set; }
    public string? LastError { get; private set; }

    // Events
    public event EventHandler<RequestLog>? RequestAdded;
    public event EventHandler? StatsUpdated;

    private RequestTracker()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuoteBar");
        Directory.CreateDirectory(appDataPath);
        _storagePath = Path.Combine(appDataPath, "request-history.json");

        LoadFromDisk();
    }

    /// <summary>
    /// Start tracking (called when app starts or proxy starts)
    /// </summary>
    public void Start()
    {
        IsActive = true;
        DebugLogger.Log("RequestTracker", "Started tracking");
    }

    /// <summary>
    /// Stop tracking (called when app stops or proxy stops)
    /// </summary>
    public void Stop()
    {
        IsActive = false;
        DebugLogger.Log("RequestTracker", "Stopped tracking");
    }

    /// <summary>
    /// Add a request entry directly
    /// </summary>
    public void AddEntry(RequestLog entry)
    {
        lock (_lock)
        {
            _store.AddEntry(entry);
        }

        // Fire events outside lock
        RequestAdded?.Invoke(this, entry);
        StatsUpdated?.Invoke(this, EventArgs.Empty);

        // Save asynchronously
        ThreadPool.QueueUserWorkItem(_ => SaveToDisk());
    }

    /// <summary>
    /// Record a request with individual parameters (convenience method)
    /// </summary>
    public void RecordRequest(
        string method,
        string endpoint,
        string? provider = null,
        string? model = null,
        int? inputTokens = null,
        int? outputTokens = null,
        int durationMs = 0,
        int? statusCode = null,
        int requestSize = 0,
        int responseSize = 0,
        string? errorMessage = null)
    {
        var entry = new RequestLog
        {
            Timestamp = DateTime.UtcNow,
            Method = method,
            Endpoint = endpoint,
            Provider = provider,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            DurationMs = durationMs,
            StatusCode = statusCode,
            RequestSize = requestSize,
            ResponseSize = responseSize,
            ErrorMessage = errorMessage
        };

        AddEntry(entry);
    }

    /// <summary>
    /// Clear all history
    /// </summary>
    public void ClearHistory()
    {
        lock (_lock)
        {
            _store = RequestHistoryStore.Empty;
        }

        StatsUpdated?.Invoke(this, EventArgs.Empty);
        ThreadPool.QueueUserWorkItem(_ => SaveToDisk());

        DebugLogger.Log("RequestTracker", "History cleared");
    }

    /// <summary>
    /// Get requests filtered by provider
    /// </summary>
    public IReadOnlyList<RequestLog> GetRequestsForProvider(string provider)
    {
        lock (_lock)
        {
            return _store.Entries
                .Where(e => string.Equals(e.Provider, provider, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Get requests from last N minutes
    /// </summary>
    public IReadOnlyList<RequestLog> GetRecentRequests(int minutes)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-minutes);
        lock (_lock)
        {
            return _store.Entries
                .Where(e => e.Timestamp >= cutoff)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Get requests filtered by model
    /// </summary>
    public IReadOnlyList<RequestLog> GetRequestsForModel(string model)
    {
        lock (_lock)
        {
            return _store.Entries
                .Where(e => string.Equals(e.Model, model, StringComparison.OrdinalIgnoreCase))
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Get count of rate-limited requests in last N minutes
    /// </summary>
    public int GetRateLimitedCount(int minutes = 60)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-minutes);
        lock (_lock)
        {
            return _store.Entries
                .Count(e => e.Timestamp >= cutoff && e.IsRateLimited);
        }
    }

    /// <summary>
    /// Trim history for background/memory pressure
    /// </summary>
    public void TrimHistoryForBackground()
    {
        const int reducedLimit = 10;

        bool needsSave = false;
        lock (_lock)
        {
            if (_store.Entries.Count > reducedLimit)
            {
                _store.TrimTo(reducedLimit);
                needsSave = true;
            }
        }

        if (needsSave)
        {
            StatsUpdated?.Invoke(this, EventArgs.Empty);
            ThreadPool.QueueUserWorkItem(_ => SaveToDisk());
            DebugLogger.Log("RequestTracker", $"Trimmed to {reducedLimit} entries for background");
        }
    }

    /// <summary>
    /// Get summary text for display
    /// </summary>
    public string GetSummary()
    {
        var stats = Stats;
        if (stats.TotalRequests == 0)
            return "No requests tracked";

        return $"{stats.TotalRequests} requests, {stats.SuccessRate:F0}% success, " +
               $"{stats.TotalTokens.FormatAsTokenCount()} tokens";
    }

    // Persistence

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                DebugLogger.Log("RequestTracker", "No history file found, starting fresh");
                return;
            }

            var json = File.ReadAllText(_storagePath);
            var loaded = JsonSerializer.Deserialize<RequestHistoryStore>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (loaded != null)
            {
                lock (_lock)
                {
                    _store = loaded;
                }
                DebugLogger.Log("RequestTracker", $"Loaded {_store.Entries.Count} entries from disk");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("RequestTracker", "Failed to load history", ex);
            LastError = ex.Message;

            // Remove corrupt file and start fresh
            try
            {
                File.Delete(_storagePath);
                DebugLogger.Log("RequestTracker", "Removed corrupt history file, starting fresh");
            }
            catch
            {
                // Ignore delete errors
            }
        }
    }

    private void SaveToDisk()
    {
        try
        {
            RequestHistoryStore snapshot;
            lock (_lock)
            {
                // Create a snapshot to avoid holding the lock during IO
                snapshot = new RequestHistoryStore
                {
                    Version = _store.Version,
                    Entries = _store.Entries.ToList()
                };
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_storagePath, json);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("RequestTracker", "Failed to save history", ex);
            LastError = ex.Message;
        }
    }
}
