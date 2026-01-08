using NativeBar.WinUI.Core.Models;

namespace NativeBar.WinUI.Core.Providers.Cursor;

public class CursorProviderDescriptor : ProviderDescriptor
{
    public override string Id => "cursor";
    public override string DisplayName => "Cursor";
    public override string IconGlyph => "\uE8A5"; // Code/Edit icon
    public override string PrimaryColor => "#007AFF";
    public override string SecondaryColor => "#5AC8FA";
    public override string PrimaryLabel => "Plan usage";
    public override string SecondaryLabel => "On-demand";
public override string? DashboardUrl => "https://cursor.com/settings";

    public override bool SupportsOAuth => true;
    public override bool SupportsCLI => false;

    protected override void InitializeStrategies()
    {
        // Priority order:
        // 1. Cached data (if valid) - fastest, no network
        // 2. Manual cookie header (user explicitly provided)
        // 3. Stored session (from previous WebView login)
        // 
        // NOTE: Browser cookie import was REMOVED because it triggered antivirus
        // detections (SOPHOS Creds_6a / MITRE T1555.003 - Credentials from Web Browsers).
        // The secure alternative is WebView2 login which requires user interaction.
        AddStrategy(new CursorCachedStrategy());
        AddStrategy(new CursorManualCookieStrategy());
        AddStrategy(new CursorStoredSessionStrategy());
    }
}

#region Strategy 0: Cached Data (fastest)

/// <summary>
/// Strategy that returns cached data if still valid.
/// This prevents excessive API calls and reduces power consumption (Issue #139).
/// </summary>
public class CursorCachedStrategy : IProviderFetchStrategy
{
    public string StrategyName => "Cached";
    public int Priority => 0; // Highest priority - no network needed
    public StrategyType Type => StrategyType.Cached;

    public Task<bool> CanExecuteAsync()
    {
        var (lastFetch, lastSuccess, failures, isValid) = CursorUsageCache.GetStats();
        Log($"CanExecute: isValid={isValid}, lastSuccess={lastSuccess}, failures={failures}");
        return Task.FromResult(isValid);
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        Log("FetchAsync: returning cached data");
        return await CursorUsageCache.GetUsageAsync(
            forceRefresh: false,
            logger: Log,
            cancellationToken: cancellationToken);
    }

    private static void Log(string message)
    {
        Core.Services.DebugLogger.Log("CursorCachedStrategy", message);
    }
}

#endregion

#region Strategy 1: Manual Cookie Header

/// <summary>
/// Strategy that uses a manually provided cookie header.
/// Users can paste the Cookie header from browser DevTools.
/// </summary>
public class CursorManualCookieStrategy : IProviderFetchStrategy
{
    public string StrategyName => "Manual Cookie";
    public int Priority => 1;
    public StrategyType Type => StrategyType.Manual;

    public Task<bool> CanExecuteAsync()
    {
        var session = CursorSessionStore.GetSession();
        var canExecute = session?.SourceLabel == "Manual" && !string.IsNullOrEmpty(session.CookieHeader);
        Log($"CanExecute: hasSession={session != null}, sourceLabel={session?.SourceLabel ?? "null"}, canExecute={canExecute}");
        return Task.FromResult(canExecute);
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var session = CursorSessionStore.GetSession();

        if (session?.SourceLabel != "Manual" || string.IsNullOrEmpty(session.CookieHeader))
        {
            return CreateError(CursorErrorMessages.NoManualCookie);
        }

        // Check rate limit
        if (!CursorRateLimiter.IsRequestAllowed("manual"))
        {
            var waitTime = CursorRateLimiter.GetWaitTime("manual");
            Log($"Rate limited, wait {waitTime.TotalSeconds:F0}s");
            return await CursorUsageCache.GetUsageAsync(forceRefresh: false, cancellationToken: cancellationToken);
        }

        try
        {
            Log("Using manual cookie header");
            var snapshot = await CursorUsageFetcher.FetchAsync(
                session.CookieHeader,
                Log,
                cancellationToken);

            // Update stored session with account email
            if (!string.IsNullOrEmpty(snapshot.AccountEmail) && snapshot.AccountEmail != session.AccountEmail)
            {
                CursorSessionStore.SetSession(new CursorStoredSession
                {
                    CookieHeader = session.CookieHeader,
                    SourceLabel = "Manual",
                    StoredAt = session.StoredAt,
                    AccountEmail = snapshot.AccountEmail
                });
            }

            // Update cache
            var usageSnapshot = snapshot.ToUsageSnapshot();
            return usageSnapshot;
        }
        catch (CursorFetchException ex) when (ex.ErrorType == CursorFetchError.NotLoggedIn)
        {
            CursorSessionStore.ClearSession();
            CursorUsageCache.Invalidate();
            return CreateError(CursorErrorMessages.CookieExpired);
        }
        catch (CursorFetchException ex) when (ex.ErrorType == CursorFetchError.NetworkError)
        {
            return CreateError(CursorErrorMessages.NetworkError(ex.Message));
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            return CreateError(CursorErrorMessages.UnexpectedError(ex.Message));
        }
    }

    private static UsageSnapshot CreateError(string message) => new()
    {
        ProviderId = "cursor",
        ErrorMessage = message,
        FetchedAt = DateTime.UtcNow
    };

    private static void Log(string message)
    {
        Core.Services.DebugLogger.Log("CursorManualStrategy", message);
    }
}

#endregion

#region Strategy 2: Stored Session

/// <summary>
/// Strategy that uses a previously stored session.
/// This is the primary method after WebView login is completed.
/// </summary>
public class CursorStoredSessionStrategy : IProviderFetchStrategy
{
    public string StrategyName => "Stored Session";
    public int Priority => 2;
    public StrategyType Type => StrategyType.OAuth;

    public Task<bool> CanExecuteAsync()
    {
        return Task.FromResult(CursorSessionStore.HasSession());
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var cookieHeader = CursorSessionStore.GetCookieHeader();

        if (string.IsNullOrEmpty(cookieHeader))
        {
            return CreateError(CursorErrorMessages.NoStoredSession);
        }

        // Check rate limit
        if (!CursorRateLimiter.IsRequestAllowed("stored"))
        {
            var waitTime = CursorRateLimiter.GetWaitTime("stored");
            Log($"Rate limited, wait {waitTime.TotalSeconds:F0}s");
            return await CursorUsageCache.GetUsageAsync(forceRefresh: false, cancellationToken: cancellationToken);
        }

        try
        {
            Log("Using stored session");
            var snapshot = await CursorUsageFetcher.FetchAsync(
                cookieHeader,
                Log,
                cancellationToken);

            return snapshot.ToUsageSnapshot();
        }
        catch (CursorFetchException ex) when (ex.ErrorType == CursorFetchError.NotLoggedIn)
        {
            CursorSessionStore.ClearSession();
            CursorUsageCache.Invalidate();
            Log("Stored session invalid, cleared");
            return CreateError(CursorErrorMessages.SessionExpired);
        }
        catch (CursorFetchException ex) when (ex.ErrorType == CursorFetchError.NetworkError)
        {
            return CreateError(CursorErrorMessages.NetworkError(ex.Message));
        }
        catch (Exception ex)
        {
            return CreateError(CursorErrorMessages.UnexpectedError(ex.Message));
        }
    }

    private static UsageSnapshot CreateError(string message) => new()
    {
        ProviderId = "cursor",
        ErrorMessage = message,
        FetchedAt = DateTime.UtcNow
    };

    private static void Log(string message)
    {
        Core.Services.DebugLogger.Log("CursorStoredStrategy", message);
    }
}

#endregion

#region Static Helper for Quick Access

/// <summary>
/// Static helper for fetching Cursor usage with automatic fallback and caching.
/// Tries all methods in order: Cache → Manual → Stored Session
/// </summary>
public static class CursorUsageProbe
{
    /// <summary>
    /// Fetch Cursor usage using the best available method.
    /// Uses caching to minimize API calls and power consumption.
    /// </summary>
    public static async Task<UsageSnapshot> FetchAsync(
        string? manualCookieHeader = null,
        Action<string>? logger = null,
        CancellationToken cancellationToken = default)
    {
        // Use cache with smart refresh
        return await CursorUsageCache.GetUsageAsync(
            manualCookieHeader,
            forceRefresh: false,
            logger,
            cancellationToken);
    }

    /// <summary>
    /// Force refresh from API (bypass cache)
    /// </summary>
    public static async Task<UsageSnapshot> ForceRefreshAsync(
        Action<string>? logger = null,
        CancellationToken cancellationToken = default)
    {
        CursorRateLimiter.Reset();
        return await CursorUsageCache.GetUsageAsync(
            forceRefresh: true,
            logger: logger,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Check if any authentication method is available
    /// </summary>
    public static bool HasSession(Action<string>? logger = null)
    {
        // Check stored session (from WebView login or manual cookie)
        return CursorSessionStore.HasSession();
    }

    /// <summary>
    /// Clear all stored sessions (logout)
    /// </summary>
    public static void ClearSession()
    {
        CursorSessionStore.ClearSession();
        CursorUsageCache.Invalidate();
    }

    /// <summary>
    /// Get cache statistics for debugging
    /// </summary>
    public static (DateTime lastFetch, DateTime lastSuccess, int failures, bool isCacheValid) GetCacheStats()
    {
        return CursorUsageCache.GetStats();
    }
}

#endregion

#region Error Messages

/// <summary>
/// User-friendly error messages for Cursor provider.
/// Centralized to ensure consistent messaging.
/// </summary>
public static class CursorErrorMessages
{
    public const string NoManualCookie =
        "No manual cookie configured. Go to Settings > Cursor to paste your cookie header.";

    public const string CookieExpired =
        "Your Cursor cookie has expired. Please sign in again via Settings > Cursor.";

    public const string NoStoredSession =
        "Not signed in to Cursor. Go to Settings > Cursor to sign in.";

    public const string SessionExpired =
        "Your Cursor session has expired. Please sign in again via Settings > Cursor.";

    public static string NetworkError(string details) =>
        $"Network error connecting to Cursor. Check your internet connection. ({details})";

    public static string UnexpectedError(string details) =>
        $"An unexpected error occurred. ({details})";

    /// <summary>
    /// Get a hint message for how to fix the error
    /// </summary>
    public static string GetHint(string errorMessage)
    {
        if (errorMessage.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            return "Tip: Go to Settings > Cursor and click 'Sign In' to refresh your session.";
        }
        if (errorMessage.Contains("network", StringComparison.OrdinalIgnoreCase))
        {
            return "Tip: Check your internet connection and firewall settings.";
        }
        if (errorMessage.Contains("sign in", StringComparison.OrdinalIgnoreCase))
        {
            return "Tip: Go to Settings > Cursor to sign in with your Cursor account.";
        }
        return "Tip: Go to Settings > Cursor to configure authentication.";
    }
}

#endregion
