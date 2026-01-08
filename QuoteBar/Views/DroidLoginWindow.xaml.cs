using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using QuoteBar.Core.Providers.Droid;
using QuoteBar.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace QuoteBar.Views;

/// <summary>
/// WebView2-based login window for Droid/Factory authentication.
/// 
/// SECURITY: This approach is safe because:
/// 1. User explicitly initiates login through a visible browser window
/// 2. Tokens are obtained through official WebView2 Cookie Manager API
/// 3. No browser profile/database access
/// 4. Transparent user consent
/// 
/// NOTE: This is a FALLBACK method. The recommended approach is:
/// 1. Install Droid CLI
/// 2. Run 'droid' once to authenticate
/// 3. NativeBar will automatically read ~/.factory/auth.json
/// 
/// Flow:
/// 1. Open WebView2 to app.factory.ai
/// 2. User logs in through Factory/WorkOS auth flow
/// 3. After successful login, we extract the access token from cookies/localStorage
/// 4. Store tokens securely in Windows Credential Manager
/// </summary>
public sealed partial class DroidLoginWindow : Window
{
    private const string FactoryLoginUrl = "https://app.factory.ai";
    private const string FactoryDashboardPattern = "app.factory.ai";

    private static readonly string[] FactoryDomains = { 
        ".factory.ai", 
        "factory.ai", 
        "app.factory.ai", 
        "auth.factory.ai",
        ".workos.com",
        "workos.com"
    };

    // Cookies that indicate a valid session
    private static readonly HashSet<string> SessionCookieNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "wos-session",
        "__Secure-next-auth.session-token",
        "next-auth.session-token",
        "__Secure-authjs.session-token",
        "authjs.session-token",
        "session",
        "access-token"
    };

    private Microsoft.UI.Xaml.Controls.WebView2? _webView;
    private TaskCompletionSource<DroidLoginResult>? _loginCompletionSource;
    private bool _isLoggedIn = false;

    public DroidLoginWindow()
    {
        InitializeComponent();

        // Set window size
        AppWindow.Resize(new Windows.Graphics.SizeInt32(500, 750));

        // Hide CLI banner if CLI is already installed
        if (DroidCLIAuth.HasAuthFile())
        {
            CLIBanner.Visibility = Visibility.Collapsed;
        }

        // Initialize WebView2 asynchronously
        _ = InitializeWebViewAsync();
    }

    /// <summary>
    /// Show the login window and wait for the user to complete login.
    /// </summary>
    public Task<DroidLoginResult> ShowLoginAsync()
    {
        _loginCompletionSource = new TaskCompletionSource<DroidLoginResult>();

        this.Closed += OnWindowClosed;
        this.Activate();

        return _loginCompletionSource.Task;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            UpdateStatus("Initializing browser...", StatusType.Loading);

            _webView = new Microsoft.UI.Xaml.Controls.WebView2();

            if (WebViewContainer.Child == null)
            {
                WebViewContainer.Child = _webView;
            }

            await _webView.EnsureCoreWebView2Async();

            if (_webView.CoreWebView2 == null)
            {
                UpdateStatus("Failed to initialize browser", StatusType.Error);
                return;
            }

            // Configure WebView2
            var settings = _webView.CoreWebView2.Settings;
            settings.IsStatusBarEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsZoomControlEnabled = true;
            settings.AreDefaultContextMenusEnabled = true;

            // Subscribe to events
            _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

            // Navigate to Factory login
            UpdateStatus("Loading Factory login...", StatusType.Loading);
            _webView.CoreWebView2.Navigate(FactoryLoginUrl);
        }
        catch (Exception ex)
        {
            Log($"WebView2 initialization failed: {ex.Message}");
            UpdateStatus($"Browser error: {ex.Message}", StatusType.Error);
            CompleteLogin(DroidLoginResult.Failed($"WebView2 initialization failed: {ex.Message}"));
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

        LoadingOverlay.Visibility = Visibility.Collapsed;

        if (!args.IsSuccess)
        {
            UpdateStatus("Page failed to load", StatusType.Error);
            return;
        }

        // Check if we're on the Factory dashboard (login succeeded)
        if (url.Contains(FactoryDashboardPattern, StringComparison.OrdinalIgnoreCase) && !_isLoggedIn)
        {
            // Wait a moment for cookies to be set
            await Task.Delay(1000);

            Log("Detected Factory dashboard, checking for session...");
            UpdateStatus("Login detected, extracting tokens...", StatusType.Success);

            await ExtractAndSaveTokensAsync();
        }
        else if (url.Contains("auth.factory.ai", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("workos.com", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("signin", StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatus("Please sign in with your Factory account", StatusType.Info);
        }
        else
        {
            UpdateStatus("Waiting for login...", StatusType.Info);
        }
    }

    private async Task ExtractAndSaveTokensAsync()
    {
        if (_webView?.CoreWebView2 == null)
        {
            CompleteLogin(DroidLoginResult.Failed("WebView not initialized"));
            return;
        }

        try
        {
            _isLoggedIn = true;

            // First try to get tokens from localStorage (WorkOS stores tokens there)
            var tokensFromStorage = await TryExtractFromLocalStorageAsync();
            if (tokensFromStorage != null)
            {
                Log("Tokens extracted from localStorage");
                await SaveTokensAndComplete(tokensFromStorage.Value.accessToken, tokensFromStorage.Value.refreshToken);
                return;
            }

            // Fallback: try to get from cookies
            var tokensFromCookies = await TryExtractFromCookiesAsync();
            if (tokensFromCookies != null)
            {
                Log("Tokens extracted from cookies");
                await SaveTokensAndComplete(tokensFromCookies.Value.accessToken, tokensFromCookies.Value.refreshToken);
                return;
            }

            Log("No tokens found after login");
            UpdateStatus("Login incomplete - no session found. Please try again.", StatusType.Error);
            _isLoggedIn = false;
        }
        catch (Exception ex)
        {
            Log($"Failed to extract tokens: {ex.Message}");
            UpdateStatus($"Failed to save session: {ex.Message}", StatusType.Error);
            CompleteLogin(DroidLoginResult.Failed($"Token extraction failed: {ex.Message}"));
        }
    }

    private async Task<(string accessToken, string? refreshToken)?> TryExtractFromLocalStorageAsync()
    {
        try
        {
            // Try to read WorkOS tokens from localStorage
            var script = @"
                (function() {
                    try {
                        var accessToken = localStorage.getItem('workos:access-token');
                        var refreshToken = localStorage.getItem('workos:refresh-token');
                        return JSON.stringify({ accessToken: accessToken, refreshToken: refreshToken });
                    } catch (e) {
                        return JSON.stringify({ error: e.message });
                    }
                })();
            ";

            var result = await _webView!.CoreWebView2.ExecuteScriptAsync(script);

            // Result comes as a JSON-encoded string
            if (string.IsNullOrEmpty(result) || result == "null")
                return null;

            // Parse the outer JSON string
            var jsonString = JsonSerializer.Deserialize<string>(result);
            if (string.IsNullOrEmpty(jsonString))
                return null;

            var tokens = JsonSerializer.Deserialize<JsonElement>(jsonString);

            if (tokens.TryGetProperty("error", out _))
            {
                Log($"localStorage access error");
                return null;
            }

            string? accessToken = null;
            string? refreshToken = null;

            if (tokens.TryGetProperty("accessToken", out var at) && at.ValueKind == JsonValueKind.String)
                accessToken = at.GetString();

            if (tokens.TryGetProperty("refreshToken", out var rt) && rt.ValueKind == JsonValueKind.String)
                refreshToken = rt.GetString();

            if (!string.IsNullOrEmpty(accessToken))
            {
                return (accessToken, refreshToken);
            }

            return null;
        }
        catch (Exception ex)
        {
            Log($"localStorage extraction failed: {ex.Message}");
            return null;
        }
    }

    private async Task<(string accessToken, string? refreshToken)?> TryExtractFromCookiesAsync()
    {
        try
        {
            var cookieManager = _webView!.CoreWebView2.CookieManager;
            string? accessToken = null;

            foreach (var domain in FactoryDomains)
            {
                var cookieUri = $"https://{domain.TrimStart('.')}";
                var cookies = await cookieManager.GetCookiesAsync(cookieUri);

                foreach (var cookie in cookies)
                {
                    Log($"Found cookie: {cookie.Name} from {cookie.Domain}");

                    if (cookie.Name == "access-token" && !string.IsNullOrEmpty(cookie.Value))
                    {
                        accessToken = cookie.Value;
                    }
                }
            }

            if (!string.IsNullOrEmpty(accessToken))
            {
                return (accessToken, null);
            }

            return null;
        }
        catch (Exception ex)
        {
            Log($"Cookie extraction failed: {ex.Message}");
            return null;
        }
    }

    private async Task SaveTokensAndComplete(string accessToken, string? refreshToken)
    {
        try
        {
            // Parse JWT to get user info and expiration
            var claims = ParseJwtClaims(accessToken);

            // Store in our session store
            var session = new DroidStoredSession
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = claims.ExpiresAt,
                SourceLabel = "WebView OAuth",
                AccountEmail = claims.Email
            };

            DroidSessionStore.SetSession(session);
            DroidUsageCache.Invalidate();

            Log($"Session stored for {claims.Email ?? "unknown user"}");
            UpdateStatus($"Signed in as {claims.Email ?? "Factory user"}", StatusType.Success);

            await Task.Delay(1000);

            CompleteLogin(DroidLoginResult.Success(session));
        }
        catch (Exception ex)
        {
            Log($"Failed to save session: {ex.Message}");
            CompleteLogin(DroidLoginResult.Failed($"Failed to save session: {ex.Message}"));
        }
    }

    private JwtClaims ParseJwtClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return new JwtClaims();

            var payload = parts[1];
            payload += new string('=', (4 - payload.Length % 4) % 4);
            payload = payload.Replace('-', '+').Replace('_', '/');

            var bytes = Convert.FromBase64String(payload);
            var json = System.Text.Encoding.UTF8.GetString(bytes);
            var claims = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

            if (claims == null)
                return new JwtClaims();

            var result = new JwtClaims();

            if (claims.TryGetValue("exp", out var exp) && exp.ValueKind == JsonValueKind.Number)
            {
                result.ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()).UtcDateTime;
            }

            if (claims.TryGetValue("email", out var email) && email.ValueKind == JsonValueKind.String)
            {
                result.Email = email.GetString();
            }

            return result;
        }
        catch
        {
            return new JwtClaims();
        }
    }

    private class JwtClaims
    {
        public DateTime? ExpiresAt { get; set; }
        public string? Email { get; set; }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Log("User cancelled login");
        CompleteLogin(DroidLoginResult.Cancelled());
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_loginCompletionSource?.Task.IsCompleted == false)
        {
            Log("Window closed without completing login");
            _loginCompletionSource.TrySetResult(DroidLoginResult.Cancelled());
        }

        CleanupWebView();
    }

    private void CompleteLogin(DroidLoginResult result)
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
        DebugLogger.Log("DroidLoginWindow", message);
    }
}

/// <summary>
/// Result of a Droid login attempt
/// </summary>
public sealed class DroidLoginResult
{
    public bool IsSuccess { get; private init; }
    public bool IsCancelled { get; private init; }
    public string? ErrorMessage { get; private init; }
    public DroidStoredSession? Session { get; private init; }

    private DroidLoginResult() { }

    public static DroidLoginResult Success(DroidStoredSession session) => new()
    {
        IsSuccess = true,
        Session = session
    };

    public static DroidLoginResult Cancelled() => new()
    {
        IsCancelled = true
    };

    public static DroidLoginResult Failed(string error) => new()
    {
        ErrorMessage = error
    };
}
