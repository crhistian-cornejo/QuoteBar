using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using QuoteBar.Core.Providers.Augment;
using QuoteBar.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuoteBar.Views;

/// <summary>
/// WebView2-based login window for Augment authentication.
///
/// SECURITY: This approach is safe and will NOT trigger antivirus detections because:
/// 1. User explicitly initiates login through a visible browser window
/// 2. Cookies are obtained through official WebView2 Cookie Manager API
/// 3. No browser profile/database access (no SQLite, no DPAPI decryption)
/// 4. No access to other applications' data
/// 5. Transparent user consent - they see exactly what they're logging into
///
/// Flow:
/// 1. Open WebView2 to app.augmentcode.com
/// 2. User logs in through normal Augment auth flow
/// 3. After successful login, we capture session cookies via WebView2 API
/// 4. Store cookies securely in Windows Credential Manager
/// </summary>
public sealed partial class AugmentLoginWindow : Window
{
    private const string AugmentDashboardUrl = "https://app.augmentcode.com/account/subscription";
    private const string AugmentLoginSuccessPattern = "app.augmentcode.com";

    private static readonly string[] AugmentDomains = { ".augmentcode.com", "augmentcode.com", "app.augmentcode.com" };

    // Cookie names that indicate a valid session
    private static readonly HashSet<string> SessionCookieNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "session",
        "_session",
        "web_rpc_proxy_session",
        "__Secure-next-auth.session-token",
        "next-auth.session-token",
        "__Host-next-auth.csrf-token",
        "auth_session",
        "augment_session"
    };

    private Microsoft.UI.Xaml.Controls.WebView2? _webView;
    private TaskCompletionSource<AugmentLoginResult>? _loginCompletionSource;
    private bool _isLoggedIn = false;

    public AugmentLoginWindow()
    {
        InitializeComponent();

        // Set window size for comfortable login experience
        AppWindow.Resize(new Windows.Graphics.SizeInt32(500, 700));

        // Initialize WebView2 asynchronously
        _ = InitializeWebViewAsync();
    }

    /// <summary>
    /// Show the login window and wait for the user to complete login.
    /// Returns the result of the login attempt.
    /// </summary>
    public Task<AugmentLoginResult> ShowLoginAsync()
    {
        _loginCompletionSource = new TaskCompletionSource<AugmentLoginResult>();

        // Handle window closing
        this.Closed += OnWindowClosed;

        // Activate the window
        this.Activate();

        return _loginCompletionSource.Task;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            UpdateStatus("Initializing browser...", StatusType.Loading);

            // Create WebView2 control
            _webView = new Microsoft.UI.Xaml.Controls.WebView2();

            // Add to container
            if (WebViewContainer.Child == null)
            {
                WebViewContainer.Child = _webView;
            }

            // Initialize WebView2
            await _webView.EnsureCoreWebView2Async();

            if (_webView.CoreWebView2 == null)
            {
                UpdateStatus("Failed to initialize browser", StatusType.Error);
                return;
            }

            // Configure WebView2 settings for security
            var settings = _webView.CoreWebView2.Settings;
            settings.IsStatusBarEnabled = false;
            settings.AreDevToolsEnabled = false; // Disable dev tools in production
            settings.IsZoomControlEnabled = true;
            settings.AreDefaultContextMenusEnabled = true;

            // Subscribe to navigation events
            _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webView.CoreWebView2.SourceChanged += OnSourceChanged;

            // Navigate to Augment dashboard (will redirect to login if not authenticated)
            UpdateStatus("Loading Augment login...", StatusType.Loading);
            _webView.CoreWebView2.Navigate(AugmentDashboardUrl);
        }
        catch (Exception ex)
        {
            Log($"WebView2 initialization failed: {ex.Message}");
            UpdateStatus($"Browser error: {ex.Message}", StatusType.Error);
            CompleteLogin(AugmentLoginResult.Failed($"WebView2 initialization failed: {ex.Message}"));
        }
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        var uri = args.Uri;
        Log($"Navigation starting: {uri}");
        UpdateStatus($"Loading: {GetDomainFromUrl(uri)}", StatusType.Loading);
    }

    private async void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        var url = sender.Source;
        Log($"Navigation completed: {url}, Success: {args.IsSuccess}");

        // Hide loading overlay
        LoadingOverlay.Visibility = Visibility.Collapsed;

        if (!args.IsSuccess)
        {
            UpdateStatus("Page failed to load", StatusType.Error);
            return;
        }

        // Check if we're on the dashboard/account page (meaning login succeeded)
        if ((url.Contains("app.augmentcode.com/account", StringComparison.OrdinalIgnoreCase) ||
             url.Contains("app.augmentcode.com/dashboard", StringComparison.OrdinalIgnoreCase)) && !_isLoggedIn)
        {
            Log("Detected successful login, extracting cookies...");
            UpdateStatus("Login detected, saving session...", StatusType.Success);

            await ExtractAndSaveCookiesAsync();
        }
        else if (url.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("signin", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("auth", StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatus("Please sign in with your Augment account", StatusType.Info);
        }
        else
        {
            UpdateStatus("Waiting for login...", StatusType.Info);
        }
    }

    private void OnSourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
    {
        Log($"Source changed to: {sender.Source}");
    }

    private async Task ExtractAndSaveCookiesAsync()
    {
        if (_webView?.CoreWebView2 == null)
        {
            CompleteLogin(AugmentLoginResult.Failed("WebView not initialized"));
            return;
        }

        try
        {
            _isLoggedIn = true;

            var cookieManager = _webView.CoreWebView2.CookieManager;
            var allCookies = new List<(string Name, string Value, string Domain)>();

            // Get cookies from all Augment domains
            foreach (var domain in AugmentDomains)
            {
                var cookieUri = $"https://{domain.TrimStart('.')}";
                var cookies = await cookieManager.GetCookiesAsync(cookieUri);

                foreach (var cookie in cookies)
                {
                    Log($"Found cookie: {cookie.Name} from {cookie.Domain}");
                    allCookies.Add((cookie.Name, cookie.Value, cookie.Domain));
                }
            }

            if (allCookies.Count == 0)
            {
                Log("No cookies found after login");
                UpdateStatus("Login incomplete - no session found", StatusType.Error);
                _isLoggedIn = false; // Reset to allow retry
                return;
            }

            // Filter for session cookies only - skip analytics cookies to keep size small
            // Windows Credential Manager has a size limit
            var sessionCookieNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "session", "_session", "web_rpc_proxy_session",
                "__Secure-next-auth.session-token", "next-auth.session-token",
                "auth_session", "augment_session", "__Host-next-auth.csrf-token"
            };

            var sessionCookies = allCookies
                .Where(c => sessionCookieNames.Contains(c.Name))
                .DistinctBy(c => c.Name)
                .ToList();

            if (sessionCookies.Count == 0)
            {
                // Fallback: if no known session cookies, try cookies from app.augmentcode.com
                sessionCookies = allCookies
                    .Where(c => c.Domain.Contains("app.augmentcode.com"))
                    .Where(c => !c.Name.StartsWith("_g") && !c.Name.StartsWith("_fb") && !c.Name.StartsWith("ajs_") && !c.Name.StartsWith("ph_"))
                    .DistinctBy(c => c.Name)
                    .ToList();
            }

            if (sessionCookies.Count == 0)
            {
                Log("No session cookies found after login");
                UpdateStatus("Login incomplete - no session cookies found", StatusType.Error);
                _isLoggedIn = false;
                return;
            }

            var cookieHeader = string.Join("; ", sessionCookies.Select(c => $"{c.Name}={c.Value}"));
            Log($"Built cookie header with {sessionCookies.Count} session cookies (filtered from {allCookies.Count} total)");

            // Store in Windows Credential Manager (secure storage)
            AugmentCredentialStore.StoreCookieHeader(cookieHeader);

            // Invalidate session cache
            AugmentSessionStore.InvalidateCache();

            Log("Session stored successfully");
            UpdateStatus("Login successful!", StatusType.Success);

            // Small delay to show success message
            await Task.Delay(500);

            CompleteLogin(AugmentLoginResult.Success(cookieHeader));
        }
        catch (Exception ex)
        {
            Log($"Failed to extract cookies: {ex.Message}");
            UpdateStatus($"Failed to save session: {ex.Message}", StatusType.Error);
            CompleteLogin(AugmentLoginResult.Failed($"Cookie extraction failed: {ex.Message}"));
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Log("User cancelled login");
        CompleteLogin(AugmentLoginResult.Cancelled());
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // If login wasn't completed, treat as cancelled
        if (_loginCompletionSource?.Task.IsCompleted == false)
        {
            Log("Window closed without completing login");
            _loginCompletionSource.TrySetResult(AugmentLoginResult.Cancelled());
        }

        // Clean up WebView2
        CleanupWebView();
    }

    private void CompleteLogin(AugmentLoginResult result)
    {
        if (_loginCompletionSource?.Task.IsCompleted == false)
        {
            _loginCompletionSource.TrySetResult(result);
        }

        // Show success message for a moment before closing
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (result.IsSuccess)
                {
                    // Let user see the success message
                    await Task.Delay(1500);
                }
                this.Close();
            }
            catch { }
        });
    }

    private void CleanupWebView()
    {
        if (_webView != null)
        {
            try
            {
                _webView.CoreWebView2?.Stop();
                _webView.Close();
            }
            catch { }
            _webView = null;
        }
    }

    private enum StatusType { Loading, Info, Success, Error }

    private void UpdateStatus(string message, StatusType type)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = message;

            // Update icon and color based on status type
            (StatusIcon.Glyph, StatusIcon.Foreground) = type switch
            {
                StatusType.Loading => ("\uE72E", new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray)),
                StatusType.Info => ("\uE946", new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DodgerBlue)),
                StatusType.Success => ("\uE73E", new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen)),
                StatusType.Error => ("\uEA39", new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed)),
                _ => ("\uE72E", new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray))
            };
        });
    }

    private static string GetDomainFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return url;
        }
    }

    private static void Log(string message)
    {
        DebugLogger.Log("AugmentLoginWindow", message);
    }
}

/// <summary>
/// Result of an Augment login attempt
/// </summary>
public sealed class AugmentLoginResult
{
    public bool IsSuccess { get; private init; }
    public bool IsCancelled { get; private init; }
    public string? ErrorMessage { get; private init; }
    public string? CookieHeader { get; private init; }

    private AugmentLoginResult() { }

    public static AugmentLoginResult Success(string cookieHeader) => new()
    {
        IsSuccess = true,
        CookieHeader = cookieHeader
    };

    public static AugmentLoginResult Cancelled() => new()
    {
        IsCancelled = true
    };

    public static AugmentLoginResult Failed(string error) => new()
    {
        ErrorMessage = error
    };
}
