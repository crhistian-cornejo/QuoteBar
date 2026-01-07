using NativeBar.WinUI.Views;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Core.Providers.Augment;

/// <summary>
/// Helper class to launch Augment WebView login from anywhere in the app.
///
/// This uses WebView2 to let users log into Augment through the official website,
/// then captures the session cookies through the WebView2 Cookie Manager API.
///
/// The WebView approach is safe because:
/// 1. User explicitly initiates and sees the login process
/// 2. Cookies are obtained through official WebView2 Cookie Manager API
/// 3. No access to other applications' data
/// 4. No SQLite database access, no DPAPI decryption of browser data
/// </summary>
public static class AugmentLoginHelper
{
    private static AugmentLoginWindow? _currentWindow;
    private static readonly object _lock = new();

    /// <summary>
    /// Launch the Augment login window and wait for completion.
    /// Returns true if login was successful, false otherwise.
    /// </summary>
    /// <remarks>
    /// This method must be called from the UI thread.
    /// Only one login window can be open at a time.
    /// </remarks>
    public static async Task<AugmentLoginResult> LaunchLoginAsync()
    {
        lock (_lock)
        {
            if (_currentWindow != null)
            {
                Log("Login window already open");
                // Return cancelled since we can't show another window
                return AugmentLoginResult.Cancelled();
            }
        }

        try
        {
            Log("Launching Augment login window");

            var window = new AugmentLoginWindow();

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
            return AugmentLoginResult.Failed(ex.Message);
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
    /// Sign out from Augment (clear stored session)
    /// </summary>
    public static void SignOut()
    {
        Log("Signing out from Augment");
        AugmentCredentialStore.ClearCredentials();
        AugmentSessionStore.InvalidateCache();
    }

    /// <summary>
    /// Check if user is currently signed in (has cookie stored)
    /// </summary>
    public static bool IsSignedIn => AugmentCredentialStore.HasCredentials();

    private static void Log(string message)
    {
        DebugLogger.Log("AugmentLoginHelper", message);
    }
}
