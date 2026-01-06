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

    public override bool SupportsOAuth => true;
    public override bool SupportsCLI => false;

    protected override void InitializeStrategies()
    {
        // Priority order:
        // 1. Cached data (if valid) - fastest, no network
        // 2. Manual cookie header (user explicitly provided)
        // 3. Browser cookie import (automatic from Edge/Chrome/Firefox)
        // 4. Stored session (from previous successful import or WebView login)
        AddStrategy(new CursorCachedStrategy());
        AddStrategy(new CursorManualCookieStrategy());
        AddStrategy(new CursorBrowserCookieStrategy());
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
        try
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] CursorCachedStrategy: {message}\n");
        }
        catch { }
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
        try
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] CursorManualStrategy: {message}\n");
        }
        catch { }
    }
}

#endregion

#region Strategy 2: Browser Cookie Import

/// <summary>
/// Strategy that imports cookies directly from installed browsers.
/// Automatically detects Edge, Chrome, Firefox, and Brave.
/// </summary>
public class CursorBrowserCookieStrategy : IProviderFetchStrategy
{
    public string StrategyName => "Browser Import";
    public int Priority => 2;

    public Task<bool> CanExecuteAsync()
    {
        // Always available as an option
        Log("CanExecute: true (always available)");
        return Task.FromResult(true);
    }

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        Log("FetchAsync: Starting browser cookie import");
        // Check rate limit
        if (!CursorRateLimiter.IsRequestAllowed("browser"))
        {
            var waitTime = CursorRateLimiter.GetWaitTime("browser");
            Log($"Rate limited, wait {waitTime.TotalSeconds:F0}s");
            return await CursorUsageCache.GetUsageAsync(forceRefresh: false, cancellationToken: cancellationToken);
        }

        try
        {
            Log("Attempting browser cookie import");

            var session = CursorCookieImporter.ImportSession(logger: Log);

            if (session == null)
            {
                return CreateError(CursorErrorMessages.NoBrowserSession);
            }

            Log($"Found session in {session.SourceLabel}");

            var snapshot = await CursorUsageFetcher.FetchAsync(
                session.CookieHeader,
                Log,
                cancellationToken);

            // Save successful session for future use
            CursorSessionStore.SetSession(session, snapshot.AccountEmail);
            Log($"Session cached for {snapshot.AccountEmail ?? "unknown user"}");

            return snapshot.ToUsageSnapshot();
        }
        catch (CursorFetchException ex) when (ex.ErrorType == CursorFetchError.NotLoggedIn)
        {
            return CreateError(CursorErrorMessages.BrowserCookiesExpired);
        }
        catch (CursorFetchException ex) when (ex.ErrorType == CursorFetchError.NetworkError)
        {
            return CreateError(CursorErrorMessages.NetworkError(ex.Message));
        }
        catch (Exception ex)
        {
            Log($"Browser import failed: {ex.Message}");
            return CreateError(CursorErrorMessages.BrowserImportFailed(ex.Message));
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
        try
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] CursorBrowserStrategy: {message}\n");
        }
        catch { }
    }
}

#endregion

#region Strategy 3: Stored Session

/// <summary>
/// Strategy that uses a previously stored session.
/// Fallback when browser import fails (e.g., browser is running and locks cookies).
/// </summary>
public class CursorStoredSessionStrategy : IProviderFetchStrategy
{
    public string StrategyName => "Stored Session";
    public int Priority => 3;

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
        try
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
                $"[{DateTime.Now}] CursorStoredStrategy: {message}\n");
        }
        catch { }
    }
}

#endregion

#region Static Helper for Quick Access

/// <summary>
/// Static helper for fetching Cursor usage with automatic fallback and caching.
/// Tries all methods in order: Cache → Manual → Browser Import → Stored Session
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
        // Check stored session first (fastest)
        if (CursorSessionStore.HasSession())
            return true;

        // Check browser cookies
        return CursorCookieImporter.HasSession(logger);
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
        "Your Cursor cookie has expired. Please get a fresh cookie from cursor.com.";

    public const string NoBrowserSession =
        "No Cursor session found in your browsers. Please log in to cursor.com in Edge, Chrome, or Firefox.";

    public const string BrowserCookiesExpired =
        "Browser cookies are expired or invalid. Please log in to cursor.com again.";

    public const string NoStoredSession =
        "No saved Cursor session. Please log in to cursor.com in your browser.";

    public const string SessionExpired =
        "Your saved session has expired. Please log in to cursor.com in your browser.";

    public static string NetworkError(string details) =>
        $"Network error connecting to Cursor. Check your internet connection. ({details})";

    public static string BrowserImportFailed(string details) =>
        $"Could not import cookies from browser. The browser may be locking the cookie file. Try closing it. ({details})";

    public static string UnexpectedError(string details) =>
        $"An unexpected error occurred. ({details})";

    /// <summary>
    /// Get a hint message for how to fix the error
    /// </summary>
    public static string GetHint(string errorMessage)
    {
        if (errorMessage.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            return "Tip: Log in to cursor.com in your browser, then refresh.";
        }
        if (errorMessage.Contains("network", StringComparison.OrdinalIgnoreCase))
        {
            return "Tip: Check your internet connection and firewall settings.";
        }
        if (errorMessage.Contains("browser", StringComparison.OrdinalIgnoreCase))
        {
            return "Tip: Close your browser completely and try again.";
        }
        return "Tip: Go to Settings > Cursor to configure authentication.";
    }
}

#endregion
