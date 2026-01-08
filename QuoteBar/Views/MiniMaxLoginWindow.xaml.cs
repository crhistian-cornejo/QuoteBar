using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using QuoteBar.Core.Providers.MiniMax;
using QuoteBar.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QuoteBar.Views;

/// <summary>
/// WebView2-based login window for MiniMax authentication.
///
/// Flow:
/// 1. Open WebView2 to platform.minimax.io login
/// 2. User logs in through normal MiniMax auth flow
/// 3. After successful login, navigate to Coding Plan page
/// 4. Capture session cookies via WebView2 API
/// 5. Store cookies securely in Windows Credential Manager
/// </summary>
public sealed partial class MiniMaxLoginWindow : Window
{
    private const string MiniMaxLoginUrl = "https://platform.minimax.io/login";
    private const string MiniMaxCodingPlanUrl = "https://platform.minimax.io/user-center/payment/coding-plan?cycle_type=3";

    // Patterns that indicate the user is logged in
    private static readonly string[] LoggedInPatterns = {
        "platform.minimax.io/user-center",
        "platform.minimax.io/home",
        "platform.minimax.io/document"
    };

    private static readonly string[] MiniMaxDomains = { ".minimax.io", "minimax.io", "platform.minimax.io" };

    private Microsoft.UI.Xaml.Controls.WebView2? _webView;
    private TaskCompletionSource<MiniMaxLoginResult>? _loginCompletionSource;
    private bool _isLoggedIn = false;
    private bool _hasNavigatedToCodingPlan = false;

    public MiniMaxLoginWindow()
    {
        InitializeComponent();

        // Set window size for comfortable login experience
        AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 800));

        // Initialize WebView2 asynchronously
        _ = InitializeWebViewAsync();
    }

    /// <summary>
    /// Show the login window and wait for the user to complete login.
    /// Returns the result of the login attempt.
    /// </summary>
    public Task<MiniMaxLoginResult> ShowLoginAsync()
    {
        _loginCompletionSource = new TaskCompletionSource<MiniMaxLoginResult>();

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

            // Initialize WebView2 with default profile first
            await _webView.EnsureCoreWebView2Async();

            // Then clear ALL cookies to force fresh login (including from other sites to fully isolate)
            Log("Clearing all cookies for fresh login...");
            _webView.CoreWebView2.CookieManager.DeleteAllCookies();

            // Also clear browsing data to ensure complete isolation
            await _webView.CoreWebView2.Profile.ClearBrowsingDataAsync();
            Log("All browsing data cleared");

            if (_webView.CoreWebView2 == null)
            {
                UpdateStatus("Failed to initialize browser", StatusType.Error);
                return;
            }

            Log("WebView2 initialized with isolated profile");

            // Configure WebView2 settings for security
            var settings = _webView.CoreWebView2.Settings;
            settings.IsStatusBarEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsZoomControlEnabled = true;
            settings.AreDefaultContextMenusEnabled = true;

            // Subscribe to navigation events
            _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webView.CoreWebView2.SourceChanged += OnSourceChanged;

            // Navigate to MiniMax login page
            UpdateStatus("Loading MiniMax login...", StatusType.Loading);
            _webView.CoreWebView2.Navigate(MiniMaxLoginUrl);
        }
        catch (Exception ex)
        {
            Log($"WebView2 initialization failed: {ex.Message}");
            UpdateStatus($"Browser error: {ex.Message}", StatusType.Error);
            CompleteLogin(MiniMaxLoginResult.Failed($"WebView2 initialization failed: {ex.Message}"));
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

        // Check if we're on a logged-in page
        var isLoggedInPage = LoggedInPatterns.Any(p => url.Contains(p, StringComparison.OrdinalIgnoreCase));

        if (isLoggedInPage && !_isLoggedIn)
        {
            Log("Detected successful login - user is on authenticated page");
            _isLoggedIn = true;

            // If we're already on the coding-plan page, extract cookies directly
            if (url.Contains("coding-plan", StringComparison.OrdinalIgnoreCase))
            {
                Log("Already on Coding Plan page, extracting cookies...");
                UpdateStatus("Saving session...", StatusType.Success);
                await ExtractAndSaveCookiesAsync();
                return;
            }

            // Otherwise navigate to coding plan page to ensure we have all necessary cookies
            if (!_hasNavigatedToCodingPlan)
            {
                Log("Navigating to Coding Plan page...");
                UpdateStatus("Login detected, loading Coding Plan...", StatusType.Success);
                _hasNavigatedToCodingPlan = true;
                _webView?.CoreWebView2?.Navigate(MiniMaxCodingPlanUrl);
                return;
            }
        }
        else if (_isLoggedIn && _hasNavigatedToCodingPlan)
        {
            // We've navigated after login, extract cookies now
            Log("Post-login navigation complete, extracting cookies...");
            UpdateStatus("Saving session...", StatusType.Success);
            await ExtractAndSaveCookiesAsync();
        }
        else if (url.Contains("login", StringComparison.OrdinalIgnoreCase) && !isLoggedInPage)
        {
            UpdateStatus("Please sign in with your MiniMax account", StatusType.Info);
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
            CompleteLogin(MiniMaxLoginResult.Failed("WebView not initialized"));
            return;
        }

        try
        {
            var cookieManager = _webView.CoreWebView2.CookieManager;
            var allCookies = new List<(string Name, string Value, string Domain)>();

            // Get cookies from all MiniMax domains
            foreach (var domain in MiniMaxDomains)
            {
                var cookieUri = $"https://{domain.TrimStart('.')}";
                Log($"Getting cookies for: {cookieUri}");
                var cookies = await cookieManager.GetCookiesAsync(cookieUri);
                Log($"Found {cookies.Count} cookies for {cookieUri}");

                foreach (var cookie in cookies)
                {
                    Log($"Cookie: {cookie.Name} = {cookie.Value.Substring(0, Math.Min(20, cookie.Value.Length))}... (domain: {cookie.Domain})");
                    allCookies.Add((cookie.Name, cookie.Value, cookie.Domain));
                }
            }

            Log($"Total cookies collected: {allCookies.Count}");

            if (allCookies.Count == 0)
            {
                Log("No cookies found after login");
                UpdateStatus("Login incomplete - no session found", StatusType.Error);
                _isLoggedIn = false;
                _hasNavigatedToCodingPlan = false;
                return;
            }

            // Build cookie header with all cookies (MiniMax needs various cookies for session)
            var uniqueCookies = allCookies.DistinctBy(c => c.Name).ToList();
            var cookieHeader = string.Join("; ", uniqueCookies.Select(c => $"{c.Name}={c.Value}"));
            Log($"Built cookie header with {uniqueCookies.Count} unique cookies, length: {cookieHeader.Length}");

            // Store in Windows Credential Manager
            var stored = MiniMaxSettingsReader.StoreCookieHeader(cookieHeader);
            Log($"Cookie storage result: {stored}");

            // Verify storage
            var verifyRead = MiniMaxSettingsReader.GetCookieHeader();
            Log($"Verification read: {(verifyRead != null ? $"{verifyRead.Length} chars" : "NULL")}");

            if (stored && !string.IsNullOrEmpty(verifyRead))
            {
                Log("Session stored and verified successfully");
                UpdateStatus("Login successful!", StatusType.Success);

                // Small delay to show success message
                await Task.Delay(500);

                CompleteLogin(MiniMaxLoginResult.Success(cookieHeader));
            }
            else
            {
                Log($"Failed to store session - stored={stored}, verify={verifyRead != null}");
                UpdateStatus("Failed to save session", StatusType.Error);
                CompleteLogin(MiniMaxLoginResult.Failed("Failed to store cookies in Credential Manager"));
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to extract cookies: {ex.Message}\n{ex.StackTrace}");
            UpdateStatus($"Failed to save session: {ex.Message}", StatusType.Error);
            CompleteLogin(MiniMaxLoginResult.Failed($"Cookie extraction failed: {ex.Message}"));
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Log("User cancelled login");
        CompleteLogin(MiniMaxLoginResult.Cancelled());
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // If login wasn't completed, treat as cancelled
        if (_loginCompletionSource?.Task.IsCompleted == false)
        {
            Log("Window closed without completing login");
            _loginCompletionSource.TrySetResult(MiniMaxLoginResult.Cancelled());
        }

        // Clean up WebView2
        CleanupWebView();
    }

    private void CompleteLogin(MiniMaxLoginResult result)
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
        DebugLogger.Log("MiniMaxLoginWindow", message);
    }
}

/// <summary>
/// Result of a MiniMax login attempt
/// </summary>
public sealed class MiniMaxLoginResult
{
    public bool IsSuccess { get; private init; }
    public bool IsCancelled { get; private init; }
    public string? ErrorMessage { get; private init; }
    public string? CookieHeader { get; private init; }

    private MiniMaxLoginResult() { }

    public static MiniMaxLoginResult Success(string cookieHeader) => new()
    {
        IsSuccess = true,
        CookieHeader = cookieHeader
    };

    public static MiniMaxLoginResult Cancelled() => new()
    {
        IsCancelled = true
    };

    public static MiniMaxLoginResult Failed(string error) => new()
    {
        ErrorMessage = error
    };
}
