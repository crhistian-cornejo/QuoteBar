using Microsoft.UI.Xaml;
using NativeBar.WinUI.Views;
using System;
using System.Threading.Tasks;

namespace NativeBar.WinUI.Core.Providers.Cursor;

/// <summary>
/// Helper class to launch Cursor WebView login from anywhere in the app.
/// 
/// This replaces the old browser cookie import which was flagged by antivirus
/// as credential stealing (SOPHOS Creds_6a / MITRE T1555.003).
/// 
/// The WebView approach is safe because:
/// 1. User explicitly initiates and sees the login process
/// 2. Cookies are obtained through official WebView2 Cookie Manager API
/// 3. No access to other applications' data
/// 4. No SQLite database access, no DPAPI decryption of browser data
/// </summary>
public static class CursorLoginHelper
{
    private static CursorLoginWindow? _currentWindow;
    private static readonly object _lock = new();

    /// <summary>
    /// Launch the Cursor login window and wait for completion.
    /// Returns true if login was successful, false otherwise.
    /// </summary>
    /// <remarks>
    /// This method must be called from the UI thread.
    /// Only one login window can be open at a time.
    /// </remarks>
    public static async Task<CursorLoginResult> LaunchLoginAsync()
    {
        lock (_lock)
        {
            if (_currentWindow != null)
            {
                Log("Login window already open");
                // Return cancelled since we can't show another window
                return CursorLoginResult.Cancelled();
            }
        }

        try
        {
            Log("Launching Cursor login window");
            
            var window = new CursorLoginWindow();
            
            lock (_lock)
            {
                _currentWindow = window;
            }

            var result = await window.ShowLoginAsync();

            Log($"Login completed: Success={result.IsSuccess}, Cancelled={result.IsCancelled}");
            
            return result;
        }
        catch (Exception ex)
        {
            Log($"Login failed with exception: {ex.Message}");
            return CursorLoginResult.Failed(ex.Message);
        }
        finally
        {
            lock (_lock)
            {
                _currentWindow = null;
            }
        }
    }

    /// <summary>
    /// Check if the login window is currently open
    /// </summary>
    public static bool IsLoginWindowOpen
    {
        get
        {
            lock (_lock)
            {
                return _currentWindow != null;
            }
        }
    }

    /// <summary>
    /// Sign out from Cursor (clear stored session)
    /// </summary>
    public static void SignOut()
    {
        Log("Signing out from Cursor");
        CursorSessionStore.ClearSession();
        CursorUsageCache.Invalidate();
    }

    /// <summary>
    /// Check if user is currently signed in
    /// </summary>
    public static bool IsSignedIn => CursorSessionStore.HasSession();

    /// <summary>
    /// Get the current session info (email, source, etc.) if signed in
    /// </summary>
    public static CursorStoredSession? GetCurrentSession() => CursorSessionStore.GetSession();

    private static void Log(string message)
    {
        Core.Services.DebugLogger.Log("CursorLoginHelper", message);
    }
}
