using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Shapes;
using NativeBar.WinUI.Core.Providers.Claude;
using NativeBar.WinUI.Core.Providers.Copilot;
using NativeBar.WinUI.Core.Providers.Cursor;
using NativeBar.WinUI.Core.Providers.Droid;
using NativeBar.WinUI.Core.Providers.Gemini;
using NativeBar.WinUI.Core.Providers.Zai;
using NativeBar.WinUI.Core.Providers.Augment;
using NativeBar.WinUI.Core.Services;
using NativeBar.WinUI.Settings.Controls;
using NativeBar.WinUI.Settings.Helpers;

namespace NativeBar.WinUI.Settings.Pages;

/// <summary>
/// Providers settings page - toggle providers, configure connections
/// </summary>
public class ProvidersSettingsPage : ISettingsPage
{
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;
    private ScrollViewer? _content;
    private DispatcherQueue? _dispatcherQueue;

    /// <summary>
    /// Event fired when a provider connection changes and page should refresh
    /// </summary>
    public event Action? RequestRefresh;

    public FrameworkElement Content => _content ??= CreateContent();

    public void SetDispatcherQueue(DispatcherQueue queue) => _dispatcherQueue = queue;

    private ScrollViewer CreateContent()
    {
        DebugLogger.Log("ProvidersSettingsPage", "CreateContent START");
        try
        {
            var scroll = new ScrollViewer
            {
                Padding = new Thickness(28, 20, 28, 24),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var stack = new StackPanel { Spacing = 12 };

            stack.Children.Add(SettingCard.CreateHeader("Providers"));
            stack.Children.Add(SettingCard.CreateSubheader("Choose which providers to show in the popup and configure their settings"));

            // Provider visibility section
            var visibilityCard = new Border
            {
                Background = new SolidColorBrush(_theme.CardColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 12),
                BorderBrush = new SolidColorBrush(_theme.BorderColor),
                BorderThickness = new Thickness(1)
            };

            var visibilityStack = new StackPanel { Spacing = 12 };
            visibilityStack.Children.Add(new TextBlock
            {
                Text = "Show in popup",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            // Provider toggles
            visibilityStack.Children.Add(CreateProviderToggle("Codex", "codex", "#7C3AED"));
            visibilityStack.Children.Add(CreateProviderToggle("Claude", "claude", "#D97757"));
            visibilityStack.Children.Add(CreateProviderToggle("Cursor", "cursor", "#007AFF"));
            visibilityStack.Children.Add(CreateProviderToggle("Gemini", "gemini", "#4285F4"));
            visibilityStack.Children.Add(CreateProviderToggle("Copilot", "copilot", "#24292F"));
            visibilityStack.Children.Add(CreateProviderToggle("Droid", "droid", "#EE6018"));
            visibilityStack.Children.Add(CreateProviderToggle("Antigravity", "antigravity", "#FF6B6B"));
            visibilityStack.Children.Add(CreateProviderToggle("z.ai", "zai", "#E85A6A"));
            visibilityStack.Children.Add(CreateProviderToggle("Augment", "augment", "#6366F1"));

            visibilityCard.Child = visibilityStack;
            stack.Children.Add(visibilityCard);

            // Provider Configuration section
            stack.Children.Add(new TextBlock
            {
                Text = "Provider Configuration",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 8)
            });

            // Provider cards
            stack.Children.Add(CreateProviderCardWithAutoDetect("Codex", "codex", "#7C3AED"));
            stack.Children.Add(CreateProviderCardWithAutoDetect("Claude", "claude", "#D97757"));
            stack.Children.Add(CreateProviderCardWithAutoDetect("Cursor", "cursor", "#007AFF"));
            stack.Children.Add(CreateProviderCardWithAutoDetect("Gemini", "gemini", "#4285F4"));
            stack.Children.Add(CreateProviderCardWithAutoDetect("Copilot", "copilot", "#24292F"));
            stack.Children.Add(CreateProviderCardWithAutoDetect("Droid", "droid", "#EE6018"));
            stack.Children.Add(CreateProviderCardWithAutoDetect("Antigravity", "antigravity", "#FF6B6B"));
            stack.Children.Add(CreateZaiProviderCard());
            stack.Children.Add(CreateProviderCardWithAutoDetect("Augment", "augment", "#6366F1"));

            scroll.Content = stack;
            DebugLogger.Log("ProvidersSettingsPage", "CreateContent DONE");
            return scroll;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProvidersSettingsPage", "CreateContent CRASHED", ex);
            var errorScroll = new ScrollViewer { Padding = new Thickness(24) };
            var errorStack = new StackPanel();
            errorStack.Children.Add(new TextBlock { Text = $"Error loading providers: {ex.Message}", Foreground = new SolidColorBrush(Colors.Red) });
            errorScroll.Content = errorStack;
            return errorScroll;
        }
    }

    private Grid CreateProviderToggle(string displayName, string providerId, string colorHex)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Set accessibility properties for the row
        AutomationProperties.SetName(grid, $"Show {displayName} in popup");

        // Provider icon
        FrameworkElement iconElement;
        var svgFileName = ProviderIconHelper.GetProviderSvgFileName(providerId);
        if (!string.IsNullOrEmpty(svgFileName))
        {
            var svgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", svgFileName);
            if (System.IO.File.Exists(svgPath))
            {
                iconElement = new Image
                {
                    Width = 18,
                    Height = 18,
                    Source = new SvgImageSource(new Uri(svgPath, UriKind.Absolute)),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                iconElement = CreateColorDot(colorHex);
            }
        }
        else
        {
            iconElement = CreateColorDot(colorHex);
        }
        Grid.SetColumn(iconElement, 0);

        // Name
        var nameText = new TextBlock
        {
            Text = displayName,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(nameText, 1);

        // Toggle with accessibility
        var toggle = new ToggleSwitch
        {
            IsOn = _settings.Settings.IsProviderEnabled(providerId),
            OnContent = "",
            OffContent = "",
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(toggle, $"Show {displayName} in popup");
        AutomationProperties.SetLabeledBy(toggle, nameText);

        toggle.Toggled += (s, e) =>
        {
            try
            {
                _settings.Settings.SetProviderEnabled(providerId, toggle.IsOn);
                _settings.Save();
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("ProvidersSettingsPage", $"Toggle error for {providerId}", ex);
            }
        };
        Grid.SetColumn(toggle, 2);

        grid.Children.Add(iconElement);
        grid.Children.Add(nameText);
        grid.Children.Add(toggle);

        return grid;
    }

    private Ellipse CreateColorDot(string colorHex)
    {
        return new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(ProviderIconHelper.ParseColor(colorHex)),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private Border CreateProviderCardWithAutoDetect(string name, string providerId, string colorHex)
    {
        try
        {
            var (isConnected, status) = GetProviderStatusFast(providerId);
            var card = CreateProviderCard(name, providerId, colorHex, isConnected, status);

            // If not connected, schedule async CLI detection
            if (!isConnected)
            {
                _ = DetectProviderCLIAsync(card, name, providerId, colorHex);
            }

            return card;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProvidersSettingsPage", $"CreateProviderCardWithAutoDetect({providerId}) CRASHED", ex);
            return new Border
            {
                Background = new SolidColorBrush(_theme.CardColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Child = new TextBlock { Text = $"Error loading {name}: {ex.Message}", Foreground = new SolidColorBrush(Colors.Red) }
            };
        }
    }

    private Border CreateProviderCard(string name, string providerId, string colorHex, bool isConnected, string status)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 6),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon
        var iconElement = CreateProviderCardIcon(providerId, name, colorHex);
        Grid.SetColumn(iconElement, 0);

        // Info
        var infoStack = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        infoStack.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var statusStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        statusStack.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(isConnected ? _theme.SuccessColor : _theme.SecondaryTextColor)
        });
        statusStack.Children.Add(new TextBlock
        {
            Text = status,
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor)
        });
        infoStack.Children.Add(statusStack);
        Grid.SetColumn(infoStack, 1);

        // Button
        FrameworkElement buttonElement = isConnected
            ? CreateOptionsButton(name, providerId)
            : CreateConnectButton(name, providerId);
        Grid.SetColumn(buttonElement, 2);

        grid.Children.Add(iconElement);
        grid.Children.Add(infoStack);
        grid.Children.Add(buttonElement);
        card.Child = grid;

        return card;
    }

    private Button CreateOptionsButton(string name, string providerId)
    {
        var menuFlyout = new MenuFlyout();

        var refreshItem = new MenuFlyoutItem { Text = "Refresh Data", Icon = new FontIcon { Glyph = "\uE72C" } };
        AutomationProperties.SetName(refreshItem, $"Refresh {name} data");
        refreshItem.Click += async (s, e) =>
        {
            try
            {
                var usageStore = App.Current?.Services?.GetService(typeof(UsageStore)) as UsageStore;
                if (usageStore != null)
                {
                    await usageStore.RefreshAsync(providerId);
                    RequestRefresh?.Invoke();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("ProvidersSettingsPage", $"Refresh failed for {providerId}", ex);
            }
        };
        menuFlyout.Items.Add(refreshItem);
        menuFlyout.Items.Add(new MenuFlyoutSeparator());

        var disconnectItem = new MenuFlyoutItem
        {
            Text = "Disconnect",
            Icon = new FontIcon { Glyph = "\uE7E8" }
        };
        AutomationProperties.SetName(disconnectItem, $"Disconnect {name}");
        disconnectItem.Click += async (s, e) =>
        {
            if (_content?.XamlRoot == null) return;

            var confirmDialog = new ContentDialog
            {
                Title = $"Disconnect {name}?",
                Content = $"This will clear stored credentials for {name}.",
                PrimaryButtonText = "Disconnect",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = _content.XamlRoot
            };

            var result = await confirmDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                DisconnectProvider(providerId);
                RequestRefresh?.Invoke();
            }
        };
        menuFlyout.Items.Add(disconnectItem);

        var button = new Button
        {
            Content = "Options",
            Padding = new Thickness(12, 6, 12, 6),
            Flyout = menuFlyout
        };
        AutomationProperties.SetName(button, $"{name} options menu");
        AutomationProperties.SetHelpText(button, $"Open options for {name} provider");

        return button;
    }

    private Button CreateConnectButton(string name, string providerId)
    {
        var button = new Button
        {
            Content = "Connect",
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(_theme.AccentColor),
            Foreground = new SolidColorBrush(Colors.White)
        };
        AutomationProperties.SetName(button, $"Connect {name}");
        AutomationProperties.SetHelpText(button, $"Set up connection to {name} provider");
        button.Click += async (s, e) => await ShowConnectDialogAsync(name, providerId);
        return button;
    }

    private async Task ShowConnectDialogAsync(string name, string providerId)
    {
        // Special handling for OAuth providers
        if (providerId.ToLower() == "copilot")
        {
            await LaunchCopilotLoginAsync();
            return;
        }
        if (providerId.ToLower() == "cursor")
        {
            await LaunchCursorLoginAsync();
            return;
        }
        if (providerId.ToLower() == "droid")
        {
            await LaunchDroidLoginAsync();
            return;
        }

        if (_content?.XamlRoot == null) return;

        string instructions = providerId.ToLower() switch
        {
            "gemini" => "To connect Gemini:\n\n1. Install the Gemini CLI\n2. Run: gemini auth login\n3. Complete OAuth in browser\n4. Click 'Retry Detection'",
            "codex" => "To connect Codex:\n\n1. Install the Codex CLI\n2. Run: codex auth login\n3. Click 'Retry Detection'",
            "claude" => "To connect Claude:\n\n1. Install the Claude CLI\n2. Run: claude auth login\n3. Complete OAuth in browser\n4. Click 'Retry Detection'",
            "antigravity" => "To connect Antigravity:\n\n1. Launch Antigravity IDE\n2. Make sure it's running and logged in\n3. Click 'Retry Detection'",
            "augment" => "To connect Augment:\n\n1. Install the Augment CLI\n2. Run: augment login\n3. Complete authentication in browser\n4. Click 'Retry Detection'\n\nThe session is stored at ~/.augment/session.json",
            _ => $"Configuration for {name} is not yet available."
        };

        var dialog = new ContentDialog
        {
            Title = $"Connect {name}",
            Content = new TextBlock { Text = instructions, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 },
            PrimaryButtonText = "Retry Detection",
            CloseButtonText = "Close",
            XamlRoot = _content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var usageStore = App.Current?.Services?.GetService(typeof(UsageStore)) as UsageStore;
            if (usageStore != null)
            {
                try { await usageStore.RefreshAsync(providerId); }
                catch { }
            }
            RequestRefresh?.Invoke();
        }
    }

    private async Task LaunchCopilotLoginAsync()
    {
        try
        {
            var result = await CopilotLoginHelper.LaunchLoginAsync();
            if (result.IsSuccess)
            {
                var usageStore = App.Current?.Services?.GetService(typeof(UsageStore)) as UsageStore;
                if (usageStore != null) await usageStore.RefreshAsync("copilot");
                await Task.Delay(500);
                RequestRefresh?.Invoke();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProvidersSettingsPage", "LaunchCopilotLoginAsync failed", ex);
        }
    }

    private async Task LaunchCursorLoginAsync()
    {
        try
        {
            var result = await CursorLoginHelper.LaunchLoginAsync();
            if (result.IsSuccess)
            {
                var usageStore = App.Current?.Services?.GetService(typeof(UsageStore)) as UsageStore;
                if (usageStore != null) await usageStore.RefreshAsync("cursor");
                await Task.Delay(500);
                RequestRefresh?.Invoke();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProvidersSettingsPage", "LaunchCursorLoginAsync failed", ex);
        }
    }

    private async Task LaunchDroidLoginAsync()
    {
        try
        {
            var result = await DroidLoginHelper.LaunchLoginAsync();
            if (result.IsSuccess)
            {
                var usageStore = App.Current?.Services?.GetService(typeof(UsageStore)) as UsageStore;
                if (usageStore != null) await usageStore.RefreshAsync("droid");
                await Task.Delay(500);
                RequestRefresh?.Invoke();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProvidersSettingsPage", "LaunchDroidLoginAsync failed", ex);
        }
    }

    private void DisconnectProvider(string providerId)
    {
        try
        {
            switch (providerId.ToLower())
            {
                case "cursor":
                    CursorSessionStore.ClearSession();
                    break;
                case "claude":
                    var claudePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "credentials.json");
                    if (System.IO.File.Exists(claudePath)) System.IO.File.Delete(claudePath);
                    break;
                case "gemini":
                    var geminiPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "credentials.json");
                    if (System.IO.File.Exists(geminiPath)) System.IO.File.Delete(geminiPath);
                    break;
                case "zai":
                    ZaiSettingsReader.DeleteApiToken();
                    break;
                case "augment":
                    AugmentCredentialStore.ClearCredentials();
                    AugmentSessionStore.InvalidateCache();
                    break;
            }
            DebugLogger.Log("ProvidersSettingsPage", $"Disconnected {providerId}");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProvidersSettingsPage", $"DisconnectProvider error ({providerId})", ex);
        }
    }

    private (bool isConnected, string status) GetProviderStatusFast(string providerId)
    {
        try
        {
            var usageStore = App.Current?.Services?.GetService(typeof(UsageStore)) as UsageStore;
            var snapshot = usageStore?.GetSnapshot(providerId);

            if (snapshot != null && snapshot.ErrorMessage == null && !snapshot.IsLoading && snapshot.Primary != null)
            {
                var planInfo = snapshot.Identity?.PlanType ?? "Connected";
                return (true, planInfo);
            }

            switch (providerId.ToLower())
            {
                case "claude":
                    var claudeCredentials = ClaudeOAuthCredentialsStore.TryLoad();
                    if (claudeCredentials != null && !claudeCredentials.IsExpired)
                        return (true, "OAuth connected");
                    break;
                case "cursor":
                    if (CursorSessionStore.HasSession())
                        return (true, "Session stored");
                    break;
                case "gemini":
                    if (GeminiOAuthCredentialsStore.HasValidCredentials())
                        return (true, "OAuth connected");
                    break;
                case "copilot":
                    var copilotCredentials = CopilotOAuthCredentialsStore.TryLoad();
                    if (copilotCredentials != null && !copilotCredentials.IsExpired)
                        return (true, "GitHub OAuth connected");
                    break;
                case "zai":
                    if (ZaiSettingsReader.HasApiToken())
                        return (true, "API token configured");
                    break;
                case "augment":
                    if (AugmentSessionStore.HasSession())
                        return (true, "CLI session detected");
                    if (AugmentCredentialStore.HasCredentials())
                        return (true, "Cookie configured");
                    break;
            }

            var needsCLICheck = providerId.ToLower() is "codex" or "claude" or "gemini" or "copilot" or "droid" or "augment";
            return (false, needsCLICheck ? "Checking..." : "Not configured");
        }
        catch
        {
            return (false, "Not configured");
        }
    }

    private async Task DetectProviderCLIAsync(Border card, string name, string providerId, string colorHex)
    {
        try
        {
            var (isConnected, status) = await Task.Run(() => GetProviderStatusWithCLI(providerId));
            if (isConnected && _dispatcherQueue != null)
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        if (card.Parent is not Panel panel) return;
                        var index = panel.Children.IndexOf(card);
                        if (index < 0) return;

                        var newCard = CreateProviderCard(name, providerId, colorHex, isConnected, status);
                        panel.Children.RemoveAt(index);
                        panel.Children.Insert(index, newCard);
                    }
                    catch { }
                });
            }
        }
        catch { }
    }

    private (bool isConnected, string status) GetProviderStatusWithCLI(string providerId)
    {
        try
        {
            switch (providerId.ToLower())
            {
                case "codex":
                    if (CanDetectCLI("codex", "--version")) return (true, "CLI detected");
                    break;
                case "claude":
                    if (CanDetectCLI("claude", "--version")) return (true, "CLI detected");
                    break;
                case "gemini":
                    if (CanDetectCLI("gemini", "--version")) return (true, "CLI detected");
                    break;
                case "copilot":
                    if (CanDetectCLI("gh", "auth status")) return (true, "GitHub CLI authenticated");
                    break;
                case "droid":
                    if (CanDetectCLI("droid", "--version")) return (true, "CLI detected");
                    break;
                case "augment":
                    // Check if session.json exists (created by 'augment login')
                    if (AugmentSessionStore.HasSession()) return (true, "CLI session detected");
                    if (AugmentCredentialStore.HasCredentials()) return (true, "Cookie configured");
                    break;
            }
            return (false, "Not configured");
        }
        catch
        {
            return (false, "Not configured");
        }
    }

    private bool CanDetectCLI(string command, string args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            var completed = process.WaitForExit(2000);
            if (!completed)
            {
                try { process.Kill(); } catch { }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private Border CreateZaiProviderCard()
    {
        var hasToken = ZaiSettingsReader.HasApiToken();

        var card = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 6),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1)
        };

        var mainStack = new StackPanel { Spacing = 12 };

        // Header row
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ProviderIconHelper.ParseColor("#E85A6A"))
        };
        var svgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "zai-white.svg");
        if (System.IO.File.Exists(svgPath))
        {
            iconBorder.Child = new Image
            {
                Width = 20,
                Height = 20,
                Source = new SvgImageSource(new Uri(svgPath, UriKind.Absolute)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        Grid.SetColumn(iconBorder, 0);

        var infoStack = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        infoStack.Children.Add(new TextBlock
        {
            Text = "z.ai",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var statusStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        statusStack.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(hasToken ? _theme.SuccessColor : _theme.SecondaryTextColor)
        });
        statusStack.Children.Add(new TextBlock
        {
            Text = hasToken ? "API token configured (secure)" : "Not configured",
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor)
        });
        infoStack.Children.Add(statusStack);
        Grid.SetColumn(infoStack, 1);

        headerGrid.Children.Add(iconBorder);
        headerGrid.Children.Add(infoStack);
        mainStack.Children.Add(headerGrid);

        // Token input
        var tokenSection = new StackPanel { Spacing = 10 };

        // Row 1: token box (full width)
        var tokenRow = new Grid();
        tokenRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tokenRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tokenBox = new PasswordBox
        {
            PlaceholderText = hasToken ? "Token saved - paste new to replace" : "Paste your z.ai API token",
            Height = 32,
            MinWidth = 360
        };
        Grid.SetColumn(tokenBox, 0);

        var toggleRevealButton = new Button
        {
            Content = "Show",
            Margin = new Thickness(8, 0, 0, 0),
            Height = 32
        };
        toggleRevealButton.Click += (s, e) =>
        {
            var isHidden = tokenBox.PasswordRevealMode != PasswordRevealMode.Visible;
            tokenBox.PasswordRevealMode = isHidden ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
            toggleRevealButton.Content = isHidden ? "Hide" : "Show";
        };
        Grid.SetColumn(toggleRevealButton, 1);

        tokenRow.Children.Add(tokenBox);
        tokenRow.Children.Add(toggleRevealButton);
        tokenSection.Children.Add(tokenRow);

        // Row 2: actions
        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        var pasteButton = new Button
        {
            Content = "Paste",
            Height = 30
        };
        pasteButton.Click += async (s, e) =>
        {
            try
            {
                var data = Clipboard.GetContent();
                if (data.Contains(StandardDataFormats.Text))
                {
                    var text = await data.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                        tokenBox.Password = text.Trim();
                }
            }
            catch
            {
                // Ignore clipboard failures
            }
        };

        var saveButton = new Button
        {
            Content = "Save",
            Padding = new Thickness(16, 4, 16, 4),
            Background = new SolidColorBrush(_theme.AccentColor),
            Foreground = new SolidColorBrush(Colors.White),
            Height = 30
        };
        saveButton.Click += async (s, e) =>
        {
            var token = string.IsNullOrWhiteSpace(tokenBox.Password) ? null : tokenBox.Password;

            // Show saving state
            saveButton.IsEnabled = false;
            saveButton.Content = "Saving...";

            try
            {
                var success = ZaiSettingsReader.StoreApiToken(token);

                if (_content?.XamlRoot != null)
                {
                    string title, message;
                    if (!success)
                    {
                        title = "Error";
                        message = "Failed to save token to Windows Credential Manager.";
                    }
                    else if (string.IsNullOrWhiteSpace(token))
                    {
                        title = "Token Cleared";
                        message = "API token removed from secure storage.";
                    }
                    else
                    {
                        title = "✓ Token Saved Successfully";
                        message = "Your z.ai API token has been saved securely.\n\nUsage data will be refreshed automatically.";
                    }

                    var dialog = new ContentDialog
                    {
                        Title = title,
                        Content = message,
                        CloseButtonText = "OK",
                        XamlRoot = _content.XamlRoot
                    };
                    await dialog.ShowAsync();
                }

                tokenBox.Password = "";
                tokenBox.PasswordRevealMode = PasswordRevealMode.Hidden;
                toggleRevealButton.Content = "Show";

                // Trigger data refresh
                var usageStore = App.Current?.Services?.GetService(typeof(UsageStore)) as UsageStore;
                if (usageStore != null && !string.IsNullOrWhiteSpace(token))
                {
                    await usageStore.RefreshAsync("zai");
                }

                RequestRefresh?.Invoke();
            }
            finally
            {
                saveButton.IsEnabled = true;
                saveButton.Content = "Save";
            }
        };

        var openLink = new HyperlinkButton
        {
            Content = "Get token",
            NavigateUri = new Uri("https://z.ai/manage-apikey/subscription"),
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor)
        };

        actionsRow.Children.Add(pasteButton);
        actionsRow.Children.Add(saveButton);
        actionsRow.Children.Add(openLink);
        tokenSection.Children.Add(actionsRow);

        tokenSection.Children.Add(new TextBlock
        {
            Text = "Token is stored securely in Windows Credential Manager",
            FontSize = 11,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7
        });

        mainStack.Children.Add(tokenSection);
        card.Child = mainStack;

        return card;
    }

    private FrameworkElement CreateProviderCardIcon(string providerId, string name, string colorHex)
    {
        var isDark = _theme.IsDarkMode;
        var providerLower = providerId.ToLower();

        switch (providerLower)
        {
            case "gemini":
                var geminiSvgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "gemini.svg");
                if (System.IO.File.Exists(geminiSvgPath))
                {
                    return new Image
                    {
                        Width = 36,
                        Height = 36,
                        Source = new SvgImageSource(new Uri(geminiSvgPath, UriKind.Absolute)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                break;

            case "cursor":
                // In dark mode: use black SVG on light background
                // In light mode: use white SVG on black background
                var cursorSvgFile = isDark ? "cursor.svg" : "cursor-white.svg";
                var cursorSvgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", cursorSvgFile);
                if (System.IO.File.Exists(cursorSvgPath))
                {
                    var cursorBgColor = isDark
                        ? Windows.UI.Color.FromArgb(255, 240, 240, 240) // Light gray for dark mode
                        : Colors.Black; // Black for light mode
                    var cursorBorder = new Border
                    {
                        Width = 36,
                        Height = 36,
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(cursorBgColor),
                        Padding = new Thickness(6)
                    };
                    cursorBorder.Child = new Image
                    {
                        Width = 24,
                        Height = 24,
                        Source = new SvgImageSource(new Uri(cursorSvgPath, UriKind.Absolute)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    return cursorBorder;
                }
                break;

            case "droid":
                var droidSvgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "droid-white.svg");
                if (System.IO.File.Exists(droidSvgPath))
                {
                    var droidBorder = new Border
                    {
                        Width = 36,
                        Height = 36,
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(ProviderIconHelper.ParseColor("#EE6018")),
                        Padding = new Thickness(6)
                    };
                    droidBorder.Child = new Image
                    {
                        Width = 24,
                        Height = 24,
                        Source = new SvgImageSource(new Uri(droidSvgPath, UriKind.Absolute)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    return droidBorder;
                }
                break;

            case "antigravity":
                var antigravityBgColor = isDark ? Colors.Black : Colors.White;
                var antigravitySvgFile = isDark ? "antigravity.svg" : "antigravity-black.svg";
                var antigravitySvgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", antigravitySvgFile);
                if (System.IO.File.Exists(antigravitySvgPath))
                {
                    var antigravityBorder = new Border
                    {
                        Width = 36,
                        Height = 36,
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(antigravityBgColor),
                        BorderBrush = new SolidColorBrush(isDark ? Windows.UI.Color.FromArgb(60, 255, 255, 255) : Windows.UI.Color.FromArgb(40, 0, 0, 0)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(6)
                    };
                    antigravityBorder.Child = new Image
                    {
                        Width = 24,
                        Height = 24,
                        Source = new SvgImageSource(new Uri(antigravitySvgPath, UriKind.Absolute)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    return antigravityBorder;
                }
                break;

            case "augment":
                var augmentSvgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "augment-white.svg");
                if (System.IO.File.Exists(augmentSvgPath))
                {
                    var augmentBorder = new Border
                    {
                        Width = 36,
                        Height = 36,
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(ProviderIconHelper.ParseColor("#6366F1")), // Indigo
                        Padding = new Thickness(6)
                    };
                    augmentBorder.Child = new Image
                    {
                        Width = 24,
                        Height = 24,
                        Source = new SvgImageSource(new Uri(augmentSvgPath, UriKind.Absolute)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    return augmentBorder;
                }
                break;

            default:
                var svgFileName = ProviderIconHelper.GetProviderSvgFileName(providerId, forIconWithBackground: true);
                if (!string.IsNullOrEmpty(svgFileName))
                {
                    var svgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", svgFileName);
                    if (System.IO.File.Exists(svgPath))
                    {
                        var iconBorder = new Border
                        {
                            Width = 36,
                            Height = 36,
                            CornerRadius = new CornerRadius(8),
                            Background = new SolidColorBrush(ProviderIconHelper.ParseColor(colorHex)),
                            Padding = new Thickness(6)
                        };
                        iconBorder.Child = new Image
                        {
                            Width = 24,
                            Height = 24,
                    Source = new SvgImageSource(new Uri(svgPath, UriKind.Absolute)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        return iconBorder;
                    }
                }
                break;
        }

        // Fallback
        return CreateProviderIconWithBackground(name, colorHex);
    }

    private Border CreateProviderIconWithBackground(string name, string colorHex)
    {
        var border = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ProviderIconHelper.ParseColor(colorHex))
        };
        border.Child = new TextBlock
        {
            Text = name.Length > 0 ? name[0].ToString().ToUpper() : "?",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return border;
    }

    public void OnThemeChanged()
    {
        _content = null; // Force recreation on next access
    }

    /// <summary>
    /// Force refresh the page content
    /// </summary>
    public void Refresh()
    {
        try
        {
            DebugLogger.Log("ProvidersSettingsPage", "Refresh START");
            _content = CreateContent();
            DebugLogger.Log("ProvidersSettingsPage", "Refresh DONE");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProvidersSettingsPage", "Refresh CRASHED", ex);
        }
    }

}
