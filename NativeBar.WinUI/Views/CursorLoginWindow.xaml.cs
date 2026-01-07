using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using NativeBar.WinUI.Core.Providers.Cursor;
using NativeBar.WinUI.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NativeBar.WinUI.Views;

/// <summary>
/// WebView2-based login window for Cursor authentication.
/// 
/// SECURITY: This approach is safe and will NOT trigger antivirus detections because:
/// 1. User explicitly initiates login through a visible browser window
/// 2. Cookies are obtained through official WebView2 Cookie Manager API
/// 3. No browser profile/database access (no SQLite, no DPAPI decryption)
/// 4. No access to other applications' data
/// 5. Transparent user consent - they see exactly what they're logging into
/// 
/// Flow:
/// 1. Open WebView2 to cursor.com/dashboard
/// 2. User logs in through normal Cursor/WorkOS auth flow
/// 3. After successful login, we capture session cookies via WebView2 API
/// 4. Store cookies securely in Windows Credential Manager
/// </summary>
public sealed partial class CursorLoginWindow : Window
{
    private const string CursorDashboardUrl = "https://cursor.com/dashboard";
    private const string CursorLoginSuccessPattern = "cursor.com/dashboard";
    
    private static readonly string[] CursorDomains = { ".cursor.com", "cursor.com", ".cursor.sh", "cursor.sh" };
    private static readonly HashSet<string> SessionCookieNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WorkosCursorSessionToken",
        "__Secure-next-auth.session-token",
        "next-auth.session-token"
    };

    private Microsoft.UI.Xaml.Controls.WebView2? _webView;
    private TaskCompletionSource<CursorLoginResult>? _loginCompletionSource;
    private bool _isLoggedIn = false;

    public CursorLoginWindow()
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
    public Task<CursorLoginResult> ShowLoginAsync()
    {
        _loginCompletionSource = new TaskCompletionSource<CursorLoginResult>();
        
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
            
            // Navigate to Cursor dashboard (will redirect to login if not authenticated)
            UpdateStatus("Loading Cursor login...", StatusType.Loading);
            _webView.CoreWebView2.Navigate(CursorDashboardUrl);
        }
        catch (Exception ex)
        {
            Log($"WebView2 initialization failed: {ex.Message}");
            UpdateStatus($"Browser error: {ex.Message}", StatusType.Error);
            CompleteLogin(CursorLoginResult.Failed($"WebView2 initialization failed: {ex.Message}"));
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
        
        // Check if we're on the dashboard (meaning login succeeded)
        if (url.Contains(CursorLoginSuccessPattern, StringComparison.OrdinalIgnoreCase) && !_isLoggedIn)
        {
            Log("Detected successful login, extracting cookies...");
            UpdateStatus("Login detected, saving session...", StatusType.Success);
            
            await ExtractAndSaveCookiesAsync();
        }
        else if (url.Contains("authenticator.cursor.sh", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("signin", StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatus("Please sign in with your Cursor account", StatusType.Info);
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
            CompleteLogin(CursorLoginResult.Failed("WebView not initialized"));
            return;
        }

        try
        {
            _isLoggedIn = true;
            
            var cookieManager = _webView.CoreWebView2.CookieManager;
            var allCookies = new List<(string Name, string Value, string Domain)>();
            
            // Get cookies from all Cursor domains
            foreach (var domain in CursorDomains)
            {
                var cookieUri = $"https://{domain.TrimStart('.')}";
                var cookies = await cookieManager.GetCookiesAsync(cookieUri);
                
                foreach (var cookie in cookies)
                {
                    Log($"Found cookie: {cookie.Name} from {cookie.Domain}");
                    allCookies.Add((cookie.Name, cookie.Value, cookie.Domain));
                }
            }
            
            // Filter for session cookies
            var sessionCookies = allCookies
                .Where(c => SessionCookieNames.Contains(c.Name))
                .DistinctBy(c => c.Name)
                .ToList();
            
            if (sessionCookies.Count == 0)
            {
                Log("No session cookies found after login");
                UpdateStatus("Login incomplete - no session found", StatusType.Error);
                _isLoggedIn = false; // Reset to allow retry
                return;
            }
            
            // Build cookie header
            var cookieHeader = string.Join("; ", sessionCookies.Select(c => $"{c.Name}={c.Value}"));
            Log($"Built cookie header with {sessionCookies.Count} cookies");
            
            // Create session info
            var sessionInfo = new CursorSessionInfo
            {
                CookieHeader = cookieHeader,
                SourceLabel = "WebView Login",
                Cookies = sessionCookies.Select(c => new BrowserCookie
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain,
                    Path = "/",
                    IsSecure = true,
                    IsHttpOnly = true
                }).ToList()
            };
            
            // Store in Credential Manager (secure storage)
            CursorSessionStore.SetSession(sessionInfo);
            
            // Invalidate cache to force fresh fetch with new credentials
            CursorUsageCache.Invalidate();
            
            Log("Session stored successfully");
            UpdateStatus("Login successful!", StatusType.Success);
            
            // Small delay to show success message
            await Task.Delay(500);
            
            CompleteLogin(CursorLoginResult.Success(sessionInfo));
        }
        catch (Exception ex)
        {
            Log($"Failed to extract cookies: {ex.Message}");
            UpdateStatus($"Failed to save session: {ex.Message}", StatusType.Error);
            CompleteLogin(CursorLoginResult.Failed($"Cookie extraction failed: {ex.Message}"));
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Log("User cancelled login");
        CompleteLogin(CursorLoginResult.Cancelled());
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // If login wasn't completed, treat as cancelled
        if (_loginCompletionSource?.Task.IsCompleted == false)
        {
            Log("Window closed without completing login");
            _loginCompletionSource.TrySetResult(CursorLoginResult.Cancelled());
        }
        
        // Clean up WebView2
        CleanupWebView();
    }

    private void CompleteLogin(CursorLoginResult result)
    {
        if (_loginCompletionSource?.Task.IsCompleted == false)
        {
            _loginCompletionSource.TrySetResult(result);
        }
        
        // Close window on success or failure (not cancellation, already handled)
        DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
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
        DebugLogger.Log("CursorLoginWindow", message);
    }
}

/// <summary>
/// Result of a Cursor login attempt
/// </summary>
public sealed class CursorLoginResult
{
    public bool IsSuccess { get; private init; }
    public bool IsCancelled { get; private init; }
    public string? ErrorMessage { get; private init; }
    public CursorSessionInfo? Session { get; private init; }

    private CursorLoginResult() { }

    public static CursorLoginResult Success(CursorSessionInfo session) => new()
    {
        IsSuccess = true,
        Session = session
    };

    public static CursorLoginResult Cancelled() => new()
    {
        IsCancelled = true
    };

    public static CursorLoginResult Failed(string error) => new()
    {
        ErrorMessage = error
    };
}
