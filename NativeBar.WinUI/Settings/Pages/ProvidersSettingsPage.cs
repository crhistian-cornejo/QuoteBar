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
using NativeBar.WinUI.Core.Providers;
using NativeBar.WinUI.Core.Providers.Claude;
using NativeBar.WinUI.Core.Providers.Copilot;
using NativeBar.WinUI.Core.Providers.Cursor;
using NativeBar.WinUI.Core.Providers.Droid;
using NativeBar.WinUI.Core.Providers.Gemini;
using NativeBar.WinUI.Core.Providers.Zai;
using NativeBar.WinUI.Core.Providers.Augment;
using NativeBar.WinUI.Core.Providers.MiniMax;
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

            // Provider toggles (alphabetical order)
            visibilityStack.Children.Add(CreateProviderToggle("Antigravity", "antigravity", "#FF6B6B"));
            visibilityStack.Children.Add(CreateProviderToggle("Augment", "augment", "#3C3C3C"));
            visibilityStack.Children.Add(CreateProviderToggle("Claude", "claude", "#D97757"));
            visibilityStack.Children.Add(CreateProviderToggle("Codex", "codex", "#7C3AED"));
            visibilityStack.Children.Add(CreateProviderToggle("Copilot", "copilot", "#24292F"));
            visibilityStack.Children.Add(CreateProviderToggle("Cursor", "cursor", "#007AFF"));
            visibilityStack.Children.Add(CreateProviderToggle("Droid", "droid", "#EE6018"));
            visibilityStack.Children.Add(CreateProviderToggle("Gemini", "gemini", "#4285F4"));
            visibilityStack.Children.Add(CreateProviderToggle("MiniMax", "minimax", "#E2167E")); // TEST: only toggle
            visibilityStack.Children.Add(CreateProviderToggle("z.ai", "zai", "#E85A6A"));

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

            // Provider cards (alphabetical order)
            DebugLogger.Log("ProvidersSettingsPage", "Creating Antigravity card...");
            stack.Children.Add(CreateProviderCardWithAutoDetect("Antigravity", "antigravity", "#FF6B6B"));
            DebugLogger.Log("ProvidersSettingsPage", "Creating Augment card...");
            stack.Children.Add(CreateProviderCardWithAutoDetect("Augment", "augment", "#3C3C3C"));
            DebugLogger.Log("ProvidersSettingsPage", "Creating Claude card...");
            stack.Children.Add(CreateProviderCardWithAutoDetect("Claude", "claude", "#D97757"));
            DebugLogger.Log("ProvidersSettingsPage", "Creating Codex card...");
            stack.Children.Add(CreateProviderCardWithAutoDetect("Codex", "codex", "#7C3AED"));
            DebugLogger.Log("ProvidersSettingsPage", "Creating Copilot card...");
            stack.Children.Add(CreateProviderCardWithAutoDetect("Copilot", "copilot", "#24292F"));
            DebugLogger.Log("ProvidersSettingsPage", "Creating Cursor card...");
            stack.Children.Add(CreateProviderCardWithAutoDetect("Cursor", "cursor", "#007AFF"));
            DebugLogger.Log("ProvidersSettingsPage", "Creating Droid card...");
            stack.Children.Add(CreateProviderCardWithAutoDetect("Droid", "droid", "#EE6018"));
            DebugLogger.Log("ProvidersSettingsPage", "Creating Gemini card...");
            stack.Children.Add(CreateProviderCardWithAutoDetect("Gemini", "gemini", "#4285F4"));
            DebugLogger.Log("ProvidersSettingsPage", "Creating z.ai card...");
            stack.Children.Add(CreateZaiProviderCard());
            DebugLogger.Log("ProvidersSettingsPage", "Creating MiniMax card...");
            stack.Children.Add(CreateMiniMaxProviderCard());
            DebugLogger.Log("ProvidersSettingsPage", "All cards created successfully");

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

        var mainStack = new StackPanel { Spacing = 10 };

        // Header row (icon, info, button)
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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

        headerGrid.Children.Add(iconElement);
        headerGrid.Children.Add(infoStack);
        headerGrid.Children.Add(buttonElement);
        mainStack.Children.Add(headerGrid);

        // Strategy selector (only for providers with multiple strategies)
        var strategySelector = CreateStrategySelector(providerId);
        if (strategySelector != null)
        {
            mainStack.Children.Add(strategySelector);
        }

        card.Child = mainStack;

        return card;
    }

    /// <summary>
    /// Creates a strategy selector (ComboBox) for providers with multiple authentication strategies
    /// TEMPORARILY DISABLED to debug crash
    /// </summary>
    private FrameworkElement? CreateStrategySelector(string providerId)
    {
        // TEMPORARILY DISABLED to isolate crash source
        // The ComboBox was causing WinUI rendering crashes
        DebugLogger.Log("ProvidersSettingsPage", $"CreateStrategySelector DISABLED for {providerId}");
        return null;

        /*
        try
        {
            DebugLogger.Log("ProvidersSettingsPage", $"CreateStrategySelector START for {providerId}");
            var provider = ProviderRegistry.Instance.GetProvider(providerId);
            if (provider == null)
            {
                DebugLogger.Log("ProvidersSettingsPage", $"CreateStrategySelector: provider {providerId} is null");
                return null;
            }

            var availableStrategies = provider.AvailableStrategies;
            DebugLogger.Log("ProvidersSettingsPage", $"CreateStrategySelector: {providerId} has {availableStrategies.Count} strategies");

            // Only show selector if there are multiple options (more than just Auto)
            if (availableStrategies.Count <= 1)
            {
                DebugLogger.Log("ProvidersSettingsPage", $"CreateStrategySelector: {providerId} skipped (only 1 strategy)");
                return null;
            }

            var grid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var label = new TextBlock
            {
                Text = "Auth method:",
                FontSize = 12,
                Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(label, 0);

            var combo = new ComboBox
            {
                MinWidth = 120,
                MaxWidth = 200,
                Height = 28,
                FontSize = 12,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(combo, $"Authentication method for {providerId}");

            // Get current preference
            var config = _settings.Settings.GetProviderConfig(providerId);
            var currentPreference = config.PreferredStrategy ?? "Auto";

            // Add items
            foreach (var strategy in availableStrategies)
            {
                var displayName = GetStrategyDisplayName(strategy);
                combo.Items.Add(displayName);

                if (strategy.ToString().Equals(currentPreference, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = combo.Items.Count - 1;
                }
            }

            // Select first if none matched
            if (combo.SelectedIndex < 0 && combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }

            combo.SelectionChanged += (s, e) =>
            {
                try
                {
                    if (combo.SelectedIndex >= 0 && combo.SelectedIndex < availableStrategies.Count)
                    {
                        var selectedStrategy = availableStrategies[combo.SelectedIndex];
                        var providerConfig = _settings.Settings.GetProviderConfig(providerId);
                        providerConfig.PreferredStrategy = selectedStrategy.ToString();
                        _settings.Save();

                        DebugLogger.Log("ProvidersSettingsPage",
                            $"Changed auth strategy for {providerId} to {selectedStrategy}");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("ProvidersSettingsPage", $"Strategy selection error for {providerId}", ex);
                }
            };

            Grid.SetColumn(combo, 1);

            grid.Children.Add(label);
            grid.Children.Add(combo);

            DebugLogger.Log("ProvidersSettingsPage", $"CreateStrategySelector DONE for {providerId}");
            return grid;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProvidersSettingsPage", $"CreateStrategySelector({providerId}) failed", ex);
            return null;
        }
        */
    }

    /// <summary>
    /// Get user-friendly display name for authentication strategy
    /// </summary>
    private static string GetStrategyDisplayName(AuthenticationStrategy strategy)
    {
        return strategy switch
        {
            AuthenticationStrategy.Auto => "Auto (recommended)",
            AuthenticationStrategy.CLI => "CLI only",
            AuthenticationStrategy.OAuth => "OAuth/Web only",
            AuthenticationStrategy.Manual => "Manual cookie only",
            _ => strategy.ToString()
        };
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
                var usageStore = (Application.Current as NativeBar.WinUI.App)?.Services?.GetService(typeof(UsageStore)) as UsageStore;
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
        DebugLogger.Log("ProvidersSettingsPage", $"ShowConnectDialogAsync called for {providerId}");

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
        if (providerId.ToLower() == "augment")
        {
            await LaunchAugmentLoginAsync();
            return;
        }
        // Special handling for cookie/token-based providers
        if (providerId.ToLower() == "minimax")
        {
            await ShowMiniMaxConnectDialogAsync();
            return;
        }
        if (providerId.ToLower() == "zai")
        {
            await ShowZaiConnectDialogAsync();
            return;
        }

        if (_content?.XamlRoot == null) return;

        string instructions = providerId.ToLower() switch
        {
            "gemini" => "To connect Gemini:\n\n1. Install the Gemini CLI\n2. Run: gemini auth login\n3. Complete OAuth in browser\n4. Click 'Retry Detection'",
            "codex" => "To connect Codex:\n\n1. Install the Codex CLI\n2. Run: codex auth login\n3. Click 'Retry Detection'",
            "claude" => "To connect Claude:\n\n1. Install the Claude CLI\n2. Run: claude auth login\n3. Complete OAuth in browser\n4. Click 'Retry Detection'",
            "antigravity" => "To connect Antigravity:\n\n1. Launch Antigravity IDE\n2. Make sure it's running and logged in\n3. Click 'Retry Detection'",
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
            var usageStore = (Application.Current as NativeBar.WinUI.App)?.Services?.GetService(typeof(UsageStore)) as UsageStore;
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
                var usageStore = (Application.Current as NativeBar.WinUI.App)?.Services?.GetService(typeof(UsageStore)) as UsageStore;
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
                var usageStore = (Application.Current as NativeBar.WinUI.App)?.Services?.GetService(typeof(UsageStore)) as UsageStore;
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
                var usageStore = (Application.Current as NativeBar.WinUI.App)?.Services?.GetService(typeof(UsageStore)) as UsageStore;
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

    private async Task LaunchAugmentLoginAsync()
    {
        try
        {
            var result = await AugmentLoginHelper.LaunchLoginAsync();
            if (result.IsSuccess)
            {
                var usageStore = (Application.Current as NativeBar.WinUI.App)?.Services?.GetService(typeof(UsageStore)) as UsageStore;
                if (usageStore != null) await usageStore.RefreshAsync("augment");
                await Task.Delay(500);
                RequestRefresh?.Invoke();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProvidersSettingsPage", "LaunchAugmentLoginAsync failed", ex);
        }
    }

    private async Task ShowMiniMaxConnectDialogAsync()
    {
        DebugLogger.Log("ProvidersSettingsPage", "ShowMiniMaxConnectDialogAsync called");
        if (_content?.XamlRoot == null)
        {
            DebugLogger.Log("ProvidersSettingsPage", "ShowMiniMaxConnectDialogAsync: _content or XamlRoot is null, aborting");
            return;
        }

        // Create dialog content with input field
        var contentStack = new StackPanel { Spacing = 12, MinWidth = 400 };

        contentStack.Children.Add(new TextBlock
        {
            Text = "To connect MiniMax:\n\n1. Go to platform.minimax.io and log in\n2. Open DevTools (F12) → Network tab\n3. Make any request and copy the 'Cookie' header\n4. Paste it below",
            TextWrapping = TextWrapping.Wrap
        });

        var inputBox = new TextBox
        {
            PlaceholderText = "Paste your MiniMax cookie header here...",
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            Height = 32
        };
        contentStack.Children.Add(inputBox);

        // Paste from clipboard button
        var pasteButton = new Button { Content = "Paste from Clipboard", Margin = new Thickness(0, 4, 0, 0) };
        pasteButton.Click += async (s, e) =>
        {
            try
            {
                var data = Clipboard.GetContent();
                if (data.Contains(StandardDataFormats.Text))
                {
                    var text = await data.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                        inputBox.Text = text.Trim();
                }
            }
            catch { }
        };
        contentStack.Children.Add(pasteButton);

        contentStack.Children.Add(new TextBlock
        {
            Text = "Cookie is stored securely in Windows Credential Manager.",
            FontSize = 11,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            Opacity = 0.7
        });

        var dialog = new ContentDialog
        {
            Title = "Connect MiniMax",
            Content = contentStack,
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Open MiniMax",
            CloseButtonText = "Cancel",
            XamlRoot = _content.XamlRoot
        };

        dialog.SecondaryButtonClick += (s, e) =>
        {
            try { Process.Start(new ProcessStartInfo("https://platform.minimax.io/user-center/payment/coding-plan?cycle_type=3") { UseShellExecute = true }); }
            catch { }
            e.Cancel = true; // Don't close the dialog
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text))
        {
            var success = MiniMaxSettingsReader.StoreCookieHeader(inputBox.Text.Trim());
            if (success)
            {
                var usageStore = (Application.Current as NativeBar.WinUI.App)?.Services?.GetService(typeof(UsageStore)) as UsageStore;
                if (usageStore != null)
                {
                    try { await usageStore.RefreshAsync("minimax"); }
                    catch { }
                }
                RequestRefresh?.Invoke();
            }
        }
    }

    private async Task ShowZaiConnectDialogAsync()
    {
        DebugLogger.Log("ProvidersSettingsPage", "ShowZaiConnectDialogAsync called");
        if (_content?.XamlRoot == null)
        {
            DebugLogger.Log("ProvidersSettingsPage", "ShowZaiConnectDialogAsync: _content or XamlRoot is null, aborting");
            return;
        }

        // Create dialog content with input field
        var contentStack = new StackPanel { Spacing = 12, MinWidth = 400 };

        contentStack.Children.Add(new TextBlock
        {
            Text = "To connect z.ai:\n\n1. Go to z.ai/manage-apikey/subscription\n2. Copy your API token\n3. Paste it below",
            TextWrapping = TextWrapping.Wrap
        });

        var inputBox = new TextBox
        {
            PlaceholderText = "Paste your z.ai API token here...",
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            Height = 32
        };
        contentStack.Children.Add(inputBox);

        // Paste from clipboard button
        var pasteButton = new Button { Content = "Paste from Clipboard", Margin = new Thickness(0, 4, 0, 0) };
        pasteButton.Click += async (s, e) =>
        {
            try
            {
                var data = Clipboard.GetContent();
                if (data.Contains(StandardDataFormats.Text))
                {
                    var text = await data.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                        inputBox.Text = text.Trim();
                }
            }
            catch { }
        };
        contentStack.Children.Add(pasteButton);

        contentStack.Children.Add(new TextBlock
        {
            Text = "Token is stored securely in Windows Credential Manager.",
            FontSize = 11,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            Opacity = 0.7
        });

        var dialog = new ContentDialog
        {
            Title = "Connect z.ai",
            Content = contentStack,
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Get Token",
            CloseButtonText = "Cancel",
            XamlRoot = _content.XamlRoot
        };

        dialog.SecondaryButtonClick += (s, e) =>
        {
            try { Process.Start(new ProcessStartInfo("https://z.ai/manage-apikey/subscription") { UseShellExecute = true }); }
            catch { }
            e.Cancel = true; // Don't close the dialog
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(inputBox.Text))
        {
            var success = ZaiSettingsReader.StoreApiToken(inputBox.Text.Trim());
            if (success)
            {
                var usageStore = (Application.Current as NativeBar.WinUI.App)?.Services?.GetService(typeof(UsageStore)) as UsageStore;
                if (usageStore != null)
                {
                    try { await usageStore.RefreshAsync("zai"); }
                    catch { }
                }
                RequestRefresh?.Invoke();
            }
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
                case "minimax":
                    MiniMaxSettingsReader.DeleteCookieHeader();
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
            var usageStore = (Application.Current as NativeBar.WinUI.App)?.Services?.GetService(typeof(UsageStore)) as UsageStore;
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
                case "minimax":
                    if (MiniMaxSettingsReader.HasCookieHeader())
                        return (true, "Cookie configured");
                    break;
                case "augment":
                    // Only cookies work for Augment web API - CLI session doesn't work
                    if (AugmentCredentialStore.HasCredentials())
                        return (true, "Session stored");
                    break;
            }

            var needsCLICheck = providerId.ToLower() is "codex" or "claude" or "gemini" or "copilot" or "droid";
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
                    // Only cookies work for Augment web API - CLI session doesn't work
                    if (AugmentCredentialStore.HasCredentials()) return (true, "Session stored");
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

    private Border CreateMiniMaxProviderCard()
    {
        // SIMPLIFIED VERSION to debug WinUI crash
        // Using same pattern as CreateProviderCardWithAutoDetect
        return CreateProviderCardWithAutoDetect("MiniMax", "minimax", "#E2167E");
    }

    private Border CreateMiniMaxProviderCardOld()
    {
        try
        {
            return CreateMiniMaxProviderCardInternal();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProvidersSettingsPage", "CreateMiniMaxProviderCard CRASHED", ex);
            return new Border
            {
                Background = new SolidColorBrush(_theme.CardColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 6),
                BorderBrush = new SolidColorBrush(_theme.BorderColor),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = $"Error loading MiniMax: {ex.Message}",
                    Foreground = new SolidColorBrush(Colors.Red),
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }
    }

    private Border CreateMiniMaxProviderCardInternal()
    {
        bool hasCookie;
        try
        {
            hasCookie = MiniMaxSettingsReader.HasCookieHeader();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("ProvidersSettingsPage", "HasCookieHeader check failed", ex);
            hasCookie = false;
        }

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

        // Icon - always use letter fallback to avoid SVG issues
        var iconElement = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ProviderIconHelper.ParseColor("#E2167E")),
            Child = new TextBlock
            {
                Text = "M",
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(iconElement, 0);

        var infoStack = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        infoStack.Children.Add(new TextBlock
        {
            Text = "MiniMax",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var statusStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        statusStack.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(hasCookie ? _theme.SuccessColor : _theme.SecondaryTextColor)
        });
        statusStack.Children.Add(new TextBlock
        {
            Text = hasCookie ? "Cookie configured (secure)" : "Not configured",
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor)
        });
        infoStack.Children.Add(statusStack);
        Grid.SetColumn(infoStack, 1);

        headerGrid.Children.Add(iconElement);
        headerGrid.Children.Add(infoStack);
        mainStack.Children.Add(headerGrid);

        // Cookie input section
        var cookieSection = new StackPanel { Spacing = 10 };

        // Simple TextBox for cookie input (PasswordBox causes WinUI crashes)
        var cookieBox = new TextBox
        {
            PlaceholderText = hasCookie ? "Cookie saved - paste new to replace" : "Paste your MiniMax cookie header",
            Height = 32,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap
        };
        AutomationProperties.SetName(cookieBox, "MiniMax cookie header");
        cookieSection.Children.Add(cookieBox);

        // Row 2: actions
        var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        var pasteButton = new Button
        {
            Content = "Paste",
            Height = 30
        };
        AutomationProperties.SetName(pasteButton, "Paste cookie from clipboard");
        pasteButton.Click += async (s, e) =>
        {
            try
            {
                var data = Clipboard.GetContent();
                if (data.Contains(StandardDataFormats.Text))
                {
                    var text = await data.GetTextAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                        cookieBox.Text = text.Trim();
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
        AutomationProperties.SetName(saveButton, "Save MiniMax cookie");
        saveButton.Click += async (s, e) =>
        {
            var cookie = string.IsNullOrWhiteSpace(cookieBox.Text) ? null : cookieBox.Text?.Trim();

            // Show saving state
            saveButton.IsEnabled = false;
            saveButton.Content = "Saving...";

            try
            {
                var success = MiniMaxSettingsReader.StoreCookieHeader(cookie);

                if (_content?.XamlRoot != null)
                {
                    string title, message;
                    if (!success)
                    {
                        title = "Error";
                        message = "Failed to save cookie to Windows Credential Manager.";
                    }
                    else if (string.IsNullOrWhiteSpace(cookie))
                    {
                        title = "Cookie Cleared";
                        message = "MiniMax cookie removed from secure storage.";
                    }
                    else
                    {
                        title = "Cookie Saved Successfully";
                        message = "Your MiniMax cookie has been saved securely.\n\nUsage data will be refreshed automatically.";
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

                cookieBox.Text = "";

                // Trigger data refresh
                var usageStore = (Application.Current as NativeBar.WinUI.App)?.Services?.GetService(typeof(UsageStore)) as UsageStore;
                if (usageStore != null && !string.IsNullOrWhiteSpace(cookie))
                {
                    await usageStore.RefreshAsync("minimax");
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
            Content = "Open Coding Plan",
            NavigateUri = new Uri("https://platform.minimax.io/user-center/payment/coding-plan?cycle_type=3"),
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor)
        };
        AutomationProperties.SetName(openLink, "Open MiniMax Coding Plan page");

        actionsRow.Children.Add(pasteButton);
        actionsRow.Children.Add(saveButton);
        actionsRow.Children.Add(openLink);
        cookieSection.Children.Add(actionsRow);

        // Help text
        cookieSection.Children.Add(new TextBlock
        {
            Text = "Cookie is stored securely in Windows Credential Manager.\nGet cookie from DevTools: Network tab → copy 'Cookie' header from any API request.",
            FontSize = 11,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7
        });

        mainStack.Children.Add(cookieSection);
        card.Child = mainStack;

        return card;
    }

    private Border CreateZaiProviderCard()
    {
        // SIMPLIFIED VERSION to debug WinUI crash
        // Using same pattern as CreateProviderCardWithAutoDetect
        return CreateProviderCardWithAutoDetect("z.ai", "zai", "#E85A6A");
    }

    private Border CreateZaiProviderCardFull()
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
        // Using TextBox instead of PasswordBox to avoid WinUI rendering crashes
        var tokenBox = new TextBox
        {
            PlaceholderText = hasToken ? "Token saved - paste new to replace" : "Paste your z.ai API token",
            Height = 32,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap
        };
        AutomationProperties.SetName(tokenBox, "z.ai API token");
        tokenSection.Children.Add(tokenBox);

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
                        tokenBox.Text = text.Trim();
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
            var token = string.IsNullOrWhiteSpace(tokenBox.Text) ? null : tokenBox.Text?.Trim();

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

                tokenBox.Text = "";

                // Trigger data refresh
                var usageStore = (Application.Current as NativeBar.WinUI.App)?.Services?.GetService(typeof(UsageStore)) as UsageStore;
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
                        Background = new SolidColorBrush(ProviderIconHelper.ParseColor("#3C3C3C")), // Dark gray (neutral)
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
