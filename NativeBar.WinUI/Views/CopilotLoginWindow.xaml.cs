using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using NativeBar.WinUI.Core.Providers.Copilot;
using NativeBar.WinUI.Core.Services;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NativeBar.WinUI.Views;

/// <summary>
/// GitHub OAuth Device Flow login window for Copilot.
/// 
/// Flow:
/// 1. Request device code from GitHub
/// 2. Display user code to user
/// 3. User opens GitHub URL and enters code
/// 4. Poll for access token
/// 5. Store token and complete
/// </summary>
public sealed partial class CopilotLoginWindow : Window
{
    private readonly CopilotDeviceFlow _deviceFlow;
    private TaskCompletionSource<CopilotLoginResult>? _loginCompletionSource;
    private CancellationTokenSource? _pollCancellation;
    private string? _verificationUri;

    public CopilotLoginWindow()
    {
        InitializeComponent();
        _deviceFlow = new CopilotDeviceFlow();

        // Set window size
        AppWindow.Resize(new Windows.Graphics.SizeInt32(450, 600));

        // Start the device flow
        _ = StartDeviceFlowAsync();
    }

    /// <summary>
    /// Show the login window and wait for the user to complete login.
    /// </summary>
    public Task<CopilotLoginResult> ShowLoginAsync()
    {
        _loginCompletionSource = new TaskCompletionSource<CopilotLoginResult>();

        this.Closed += OnWindowClosed;
        this.Activate();

        return _loginCompletionSource.Task;
    }

    private async Task StartDeviceFlowAsync()
    {
        try
        {
            ShowLoading("Requesting device code from GitHub...");

            var deviceCode = await _deviceFlow.RequestDeviceCodeAsync();

            Log($"Got device code, user code: {deviceCode.UserCode}");

            // Show the user code
            _verificationUri = deviceCode.VerificationUri;
            UserCodeText.Text = deviceCode.UserCode;
            
            ShowCodePanel();

            // Start polling for token
            _pollCancellation = new CancellationTokenSource();
            
            // Set timeout based on expires_in
            _pollCancellation.CancelAfter(TimeSpan.FromSeconds(deviceCode.ExpiresIn));

            ShowWaiting();

            var token = await _deviceFlow.PollForTokenAsync(
                deviceCode.DeviceCode, 
                deviceCode.Interval, 
                _pollCancellation.Token);

            Log("Got access token!");

            // Store the token
            CopilotTokenStore.SaveToken(token);

            ShowSuccess();

            await Task.Delay(1500);

            CompleteLogin(CopilotLoginResult.Success(token));
        }
        catch (OperationCanceledException)
        {
            Log("Device flow cancelled or timed out");
            ShowError("Authorization timed out. Please try again.");
        }
        catch (TimeoutException ex)
        {
            Log($"Device flow timeout: {ex.Message}");
            ShowError(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log($"Access denied: {ex.Message}");
            ShowError("Access denied. Please try again.");
        }
        catch (Exception ex)
        {
            Log($"Device flow error: {ex.Message}");
            ShowError($"Error: {ex.Message}");
        }
    }

    private void ShowLoading(string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LoadingPanel.Visibility = Visibility.Visible;
            LoadingText.Text = message;
            CodePanel.Visibility = Visibility.Collapsed;
            WaitingPanel.Visibility = Visibility.Collapsed;
            SuccessPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
        });
    }

    private void ShowCodePanel()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            CodePanel.Visibility = Visibility.Visible;
            WaitingPanel.Visibility = Visibility.Collapsed;
            SuccessPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
        });
    }

    private void ShowWaiting()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            WaitingPanel.Visibility = Visibility.Visible;
        });
    }

    private void ShowSuccess()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            CodePanel.Visibility = Visibility.Collapsed;
            WaitingPanel.Visibility = Visibility.Collapsed;
            SuccessPanel.Visibility = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
        });
    }

    private void ShowError(string message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            CodePanel.Visibility = Visibility.Collapsed;
            WaitingPanel.Visibility = Visibility.Collapsed;
            SuccessPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorText.Text = message;
        });
    }

    private void OnCopyCodeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(UserCodeText.Text);
            Clipboard.SetContent(dataPackage);

            CopyCodeButton.Content = "Copied!";
            _ = ResetCopyButtonAsync();
        }
        catch (Exception ex)
        {
            Log($"Failed to copy: {ex.Message}");
        }
    }

    private async Task ResetCopyButtonAsync()
    {
        await Task.Delay(2000);
        DispatcherQueue.TryEnqueue(() =>
        {
            CopyCodeButton.Content = "Copy";
        });
    }

    private void OnVerifyLinkClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var uri = _verificationUri ?? "https://github.com/login/device";
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"Failed to open browser: {ex.Message}");
        }
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        _pollCancellation?.Cancel();
        _ = StartDeviceFlowAsync();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Log("User cancelled login");
        _pollCancellation?.Cancel();
        CompleteLogin(CopilotLoginResult.Cancelled());
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _pollCancellation?.Cancel();

        if (_loginCompletionSource?.Task.IsCompleted == false)
        {
            Log("Window closed without completing login");
            _loginCompletionSource.TrySetResult(CopilotLoginResult.Cancelled());
        }
    }

    private void CompleteLogin(CopilotLoginResult result)
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

    private static void Log(string message)
    {
        DebugLogger.Log("CopilotLoginWindow", message);
    }
}

/// <summary>
/// Result of a Copilot login attempt
/// </summary>
public sealed class CopilotLoginResult
{
    public bool IsSuccess { get; private init; }
    public bool IsCancelled { get; private init; }
    public string? ErrorMessage { get; private init; }
    public string? AccessToken { get; private init; }

    private CopilotLoginResult() { }

    public static CopilotLoginResult Success(string token) => new()
    {
        IsSuccess = true,
        AccessToken = token
    };

    public static CopilotLoginResult Cancelled() => new()
    {
        IsCancelled = true
    };

    public static CopilotLoginResult Failed(string error) => new()
    {
        ErrorMessage = error
    };
}
