using System.Collections.Concurrent;
using NativeBar.WinUI.Core.Models;

namespace NativeBar.WinUI.Core.Providers.Cursor;

/// <summary>
/// Intelligent caching and throttling for Cursor API requests.
/// 
/// Addresses Issue #139 (high power consumption) by:
/// 1. Caching API responses for a configurable duration
/// 2. Preventing concurrent requests to the same endpoint
/// 3. Exponential backoff on failures
/// 4. Smart refresh based on billing cycle proximity
/// </summary>
public static class CursorUsageCache
{
    private static readonly object _lock = new();
    private static CursorStatusSnapshot? _cachedSnapshot;
    private static UsageSnapshot? _cachedUsageSnapshot;
    private static DateTime _lastFetchTime = DateTime.MinValue;
    private static DateTime _lastSuccessfulFetchTime = DateTime.MinValue;
    private static int _consecutiveFailures = 0;
    private static bool _isFetching = false;

    // Cache configuration
    private static readonly TimeSpan MinCacheDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NearResetCacheDuration = TimeSpan.FromMinutes(1);
    private static readonly int MaxConsecutiveFailures = 5;

    /// <summary>
    /// Get cached usage snapshot if valid, otherwise fetch new data.
    /// </summary>
    public static async Task<UsageSnapshot> GetUsageAsync(
        string? manualCookieHeader = null,
        bool forceRefresh = false,
        Action<string>? logger = null,
        CancellationToken cancellationToken = default)
    {
        var log = (string msg) => logger?.Invoke($"[cache] {msg}");

        lock (_lock)
        {
            // Return cached data if valid and not forcing refresh
            if (!forceRefresh && _cachedUsageSnapshot != null && IsCacheValid())
            {
                log($"Using cached data (age: {DateTime.UtcNow - _lastSuccessfulFetchTime:mm\\:ss})");
                return _cachedUsageSnapshot;
            }

            // Prevent concurrent fetches
            if (_isFetching)
            {
                log("Fetch already in progress, returning cached data");
                return _cachedUsageSnapshot ?? CreateLoadingSnapshot();
            }

            _isFetching = true;
        }

        try
        {
            log("Fetching fresh data from Cursor API");
            
            // Get cookie from manual header or stored session
            var cookieHeader = manualCookieHeader ?? CursorSessionStore.GetCookieHeader();
            
            if (string.IsNullOrEmpty(cookieHeader))
            {
                // Try browser import
                log("No stored session, trying browser import");
                var session = CursorCookieImporter.ImportSession(logger: logger);
                if (session != null)
                {
                    cookieHeader = session.CookieHeader;
                    CursorSessionStore.SetSession(session);
                    log($"Imported session from {session.SourceLabel}");
                }
            }
            
            if (string.IsNullOrEmpty(cookieHeader))
            {
                log("No cookie available");
                return new UsageSnapshot
                {
                    ProviderId = "cursor",
                    ErrorMessage = CursorErrorMessages.NoBrowserSession,
                    FetchedAt = DateTime.UtcNow
                };
            }
            
            // Fetch directly (not through CursorUsageProbe to avoid recursion)
            var statusSnapshot = await CursorUsageFetcher.FetchAsync(cookieHeader, logger, cancellationToken);
            var snapshot = statusSnapshot.ToUsageSnapshot();

            lock (_lock)
            {
                _lastFetchTime = DateTime.UtcNow;

                if (snapshot.ErrorMessage == null)
                {
                    _cachedUsageSnapshot = snapshot;
                    _lastSuccessfulFetchTime = DateTime.UtcNow;
                    _consecutiveFailures = 0;
                    log("Cache updated successfully");
                }
                else
                {
                    _consecutiveFailures++;
                    log($"Fetch failed ({_consecutiveFailures} consecutive failures): {snapshot.ErrorMessage}");

                    // Return stale cache on failure if available
                    if (_cachedUsageSnapshot != null)
                    {
                        log("Returning stale cached data due to error");
                        return _cachedUsageSnapshot with
                        {
                            ErrorMessage = $"Using cached data. Last error: {snapshot.ErrorMessage}"
                        };
                    }
                }

                return snapshot;
            }
        }
        finally
        {
            lock (_lock)
            {
                _isFetching = false;
            }
        }
    }

    /// <summary>
    /// Get the raw Cursor status snapshot (with full details)
    /// </summary>
    public static async Task<CursorStatusSnapshot?> GetStatusSnapshotAsync(
        string? manualCookieHeader = null,
        bool forceRefresh = false,
        Action<string>? logger = null,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!forceRefresh && _cachedSnapshot != null && IsCacheValid())
            {
                return _cachedSnapshot;
            }
        }

        // Fetch and update cache
        var cookieHeader = manualCookieHeader ?? CursorSessionStore.GetCookieHeader();
        if (string.IsNullOrEmpty(cookieHeader))
            return null;

        try
        {
            var snapshot = await CursorUsageFetcher.FetchAsync(cookieHeader, logger, cancellationToken);

            lock (_lock)
            {
                _cachedSnapshot = snapshot;
                _cachedUsageSnapshot = snapshot.ToUsageSnapshot();
                _lastSuccessfulFetchTime = DateTime.UtcNow;
                _lastFetchTime = DateTime.UtcNow;
                _consecutiveFailures = 0;
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            logger?.Invoke($"[cache] Failed to fetch: {ex.Message}");
            lock (_lock)
            {
                _consecutiveFailures++;
            }
            return _cachedSnapshot;
        }
    }

    /// <summary>
    /// Invalidate the cache (call after logout or settings change)
    /// </summary>
    public static void Invalidate()
    {
        lock (_lock)
        {
            _cachedSnapshot = null;
            _cachedUsageSnapshot = null;
            _lastFetchTime = DateTime.MinValue;
            _lastSuccessfulFetchTime = DateTime.MinValue;
            _consecutiveFailures = 0;
        }
    }

    /// <summary>
    /// Get cache statistics for debugging
    /// </summary>
    public static (DateTime lastFetch, DateTime lastSuccess, int failures, bool isCacheValid) GetStats()
    {
        lock (_lock)
        {
            return (_lastFetchTime, _lastSuccessfulFetchTime, _consecutiveFailures, IsCacheValid());
        }
    }

    #region Private Methods

    private static bool IsCacheValid()
    {
        if (_cachedUsageSnapshot == null)
            return false;

        var age = DateTime.UtcNow - _lastSuccessfulFetchTime;
        var cacheDuration = GetDynamicCacheDuration();

        return age < cacheDuration;
    }

    /// <summary>
    /// Calculate cache duration based on current state:
    /// - Near billing cycle reset: shorter cache (1 min)
    /// - After failures: longer cache with exponential backoff
    /// - Normal: default duration (5 min)
    /// </summary>
    private static TimeSpan GetDynamicCacheDuration()
    {
        // Apply exponential backoff on failures
        if (_consecutiveFailures > 0)
        {
            var backoffMinutes = Math.Min(
                DefaultCacheDuration.TotalMinutes * Math.Pow(2, _consecutiveFailures - 1),
                MaxCacheDuration.TotalMinutes);
            return TimeSpan.FromMinutes(backoffMinutes);
        }

        // Check if near billing cycle reset (within 1 hour)
        if (_cachedSnapshot?.BillingCycleEnd != null)
        {
            var timeToReset = _cachedSnapshot.BillingCycleEnd.Value - DateTime.UtcNow;
            if (timeToReset > TimeSpan.Zero && timeToReset < TimeSpan.FromHours(1))
            {
                return NearResetCacheDuration;
            }
        }

        return DefaultCacheDuration;
    }

    private static UsageSnapshot CreateLoadingSnapshot()
    {
        return new UsageSnapshot
        {
            ProviderId = "cursor",
            IsLoading = true,
            FetchedAt = DateTime.UtcNow
        };
    }

    #endregion
}

/// <summary>
/// Rate limiter for API requests to prevent abuse and reduce power consumption.
/// </summary>
public static class CursorRateLimiter
{
    private static readonly ConcurrentDictionary<string, DateTime> _lastRequestTimes = new();
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Check if a request is allowed (not rate limited)
    /// </summary>
    public static bool IsRequestAllowed(string endpoint = "default")
    {
        var now = DateTime.UtcNow;

        if (_lastRequestTimes.TryGetValue(endpoint, out var lastRequest))
        {
            if (now - lastRequest < MinRequestInterval)
            {
                return false;
            }
        }

        _lastRequestTimes[endpoint] = now;
        return true;
    }

    /// <summary>
    /// Get time until next request is allowed
    /// </summary>
    public static TimeSpan GetWaitTime(string endpoint = "default")
    {
        if (_lastRequestTimes.TryGetValue(endpoint, out var lastRequest))
        {
            var elapsed = DateTime.UtcNow - lastRequest;
            if (elapsed < MinRequestInterval)
            {
                return MinRequestInterval - elapsed;
            }
        }
        return TimeSpan.Zero;
    }

    /// <summary>
    /// Reset rate limiter (e.g., after user action)
    /// </summary>
    public static void Reset(string? endpoint = null)
    {
        if (endpoint == null)
        {
            _lastRequestTimes.Clear();
        }
        else
        {
            _lastRequestTimes.TryRemove(endpoint, out _);
        }
    }
}
