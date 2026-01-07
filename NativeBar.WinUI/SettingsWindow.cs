using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using NativeBar.WinUI.Core.Providers;
using NativeBar.WinUI.Core.Providers.Claude;
using NativeBar.WinUI.Core.Providers.Copilot;
using NativeBar.WinUI.Core.Providers.Cursor;
using NativeBar.WinUI.Core.Providers.Gemini;
using NativeBar.WinUI.Core.Providers.Zai;
using NativeBar.WinUI.Core.Services;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace NativeBar.WinUI;

/// <summary>
/// Settings window with native Windows 11 styling and custom titlebar
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly ThemeService _theme = ThemeService.Instance;
    private readonly SettingsService _settings = SettingsService.Instance;
    private Grid _rootGrid = null!;
    private Grid _contentGrid = null!;
    private StackPanel _menuPanel = null!;
    private ContentControl _contentArea = null!;
    private Border _contentBorder = null!;
    private Border _menuBorder = null!;
    private string _currentPage = "General";
    private string? _initialPage;

    // UI controls that need updating on theme change
    private ComboBox? _themeCombo;
    private ToggleSwitch? _accentToggle;
    private ToggleSwitch? _compactToggle;
    private ToggleSwitch? _iconsToggle;
    private ToggleSwitch? _alertsToggle;
    private Slider? _warningSlider;
    private Slider? _criticalSlider;
    private ToggleSwitch? _soundToggle;
    private ToggleSwitch? _startupToggle;
    private ComboBox? _intervalCombo;
    private Slider? _hoverSlider;
    private ToggleSwitch? _trayBadgeToggle;
    private StackPanel? _trayBadgeProvidersPanel;
    private ToggleSwitch? _hotkeyToggle;
    private ComboBox? _hotkeyCombo;

    public SettingsWindow(string? initialPage = null)
    {
        try
        {
            DebugLogger.Log("SettingsWindow", "Constructor start");

            _initialPage = initialPage;
            Title = "QuoteBar Settings";

            // Configure window
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            // Build UI
            BuildUI();

            // Configure window appearance
            ConfigureWindow();

            // Set backdrop
            try
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            }
            catch { }

            // Listen for theme changes
            _theme.ThemeChanged += OnThemeChanged;

            DebugLogger.Log("SettingsWindow", "Constructor complete");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("SettingsWindow", "Constructor error", ex);
            throw;
        }
    }

    /// <summary>
    /// Navigate to a specific settings page
    /// </summary>
    public void NavigateToPage(string pageName)
    {
        SelectMenuItem(pageName);
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        try
        {
            // Update root theme
            if (_rootGrid != null)
            {
                _rootGrid.RequestedTheme = theme;
            }
            
            // Update content grid theme
            if (_contentGrid != null)
            {
                _contentGrid.RequestedTheme = theme;
            }
            
            // Update content area theme
            if (_contentBorder != null)
            {
                _contentBorder.RequestedTheme = theme;
            }
            
            // Update content area control theme
            if (_contentArea != null)
            {
                _contentArea.RequestedTheme = theme;
            }
            
            // Update menu border theme and background
            if (_menuBorder != null)
            {
                _menuBorder.RequestedTheme = theme;
                _menuBorder.Background = new SolidColorBrush(_theme.SurfaceColor);
            }
            
            // Refresh current page to update colors
            ShowPage(_currentPage);
            
            // Update menu colors
            UpdateMenuColors();
        }
        catch { }
    }
    
    private void UpdateMenuColors()
    {
        foreach (var child in _menuPanel.Children)
        {
            if (child is Border border && border.Tag != null)
            {
                bool isSelected = border.Tag.ToString() == _currentPage;
                border.Background = new SolidColorBrush(isSelected ? _theme.SelectedColor : Microsoft.UI.Colors.Transparent);

                if (border.Child is StackPanel stack)
                {
                    if (stack.Children.Count > 0 && stack.Children[0] is FontIcon icon)
                    {
                        icon.Foreground = new SolidColorBrush(isSelected ? _theme.AccentColor : _theme.TextColor);
                    }
                    if (stack.Children.Count > 1 && stack.Children[1] is TextBlock text)
                    {
                        text.Foreground = new SolidColorBrush(_theme.TextColor);
                    }
                }
            }
        }
    }

    private void BuildUI()
    {
        _rootGrid = new Grid
        {
            RequestedTheme = _theme.CurrentTheme
        };

        // Rows: TitleBar, Content
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); // Standard titlebar height
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Custom titlebar
        var titleBar = CreateCustomTitleBar();
        Grid.SetRow(titleBar, 0);
        _rootGrid.Children.Add(titleBar);

        // Content area with two columns
        _contentGrid = new Grid
        {
            RequestedTheme = _theme.CurrentTheme
        };
        _contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        _contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_contentGrid, 1);

        // Left menu panel
        _menuBorder = new Border
        {
            Background = new SolidColorBrush(_theme.SurfaceColor),
            Padding = new Thickness(8),
            RequestedTheme = _theme.CurrentTheme
        };

        _menuPanel = new StackPanel { Spacing = 4 };

        // Menu items
        _menuPanel.Children.Add(CreateMenuItem("General", "\uE713", true));
        _menuPanel.Children.Add(CreateMenuItem("Providers", "\uE774", false));
        _menuPanel.Children.Add(CreateMenuItem("Appearance", "\uE790", false));
        _menuPanel.Children.Add(CreateMenuItem("Notifications", "\uEA8F", false));
        _menuPanel.Children.Add(CreateMenuItem("About", "\uE946", false));

        _menuBorder.Child = _menuPanel;
        Grid.SetColumn(_menuBorder, 0);

        // Right content area with background that respects theme
        _contentBorder = new Border
        {
            RequestedTheme = _theme.CurrentTheme
        };
        _contentArea = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            RequestedTheme = _theme.CurrentTheme
        };
        _contentBorder.Child = _contentArea;
        Grid.SetColumn(_contentBorder, 1);

        // Show initial page (use _initialPage if provided)
        var startPage = _initialPage ?? "General";
        _currentPage = startPage;
        ShowPage(startPage);
        UpdateMenuSelection(startPage);

        _contentGrid.Children.Add(_menuBorder);
        _contentGrid.Children.Add(_contentBorder);
        _rootGrid.Children.Add(_contentGrid);

        Content = _rootGrid;
    }

    private void UpdateMenuSelection(string pageName)
    {
        foreach (var child in _menuPanel.Children)
        {
            if (child is Border border && border.Tag != null)
            {
                bool isSelected = border.Tag.ToString() == pageName;
                border.Background = new SolidColorBrush(isSelected ? _theme.SelectedColor : Colors.Transparent);

                if (border.Child is StackPanel stack && stack.Children.Count > 0 && stack.Children[0] is FontIcon icon)
                {
                    icon.Foreground = new SolidColorBrush(isSelected ? _theme.AccentColor : _theme.TextColor);
                }
            }
        }
    }

    private Grid CreateCustomTitleBar()
    {
        var titleBarGrid = new Grid
        {
            Background = new SolidColorBrush(Colors.Transparent),
            Height = 32
        };

        titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon
        titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Title
        titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Drag area

        // App icon - try to load PNG, fallback to FontIcon
        FrameworkElement iconElement;
        try
        {
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-NATIVE.png");
            if (System.IO.File.Exists(logoPath))
            {
                var image = new Image
                {
                    Width = 16,
                    Height = 16,
                    Source = new BitmapImage(new Uri(logoPath)),
                    Margin = new Thickness(12, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconElement = image;
            }
            else
            {
                iconElement = new FontIcon
                {
                    Glyph = "\uE9D9",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(_theme.AccentColor),
                    Margin = new Thickness(12, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }
        catch
        {
            iconElement = new FontIcon
            {
                Glyph = "\uE9D9",
                FontSize = 14,
                Foreground = new SolidColorBrush(_theme.AccentColor),
                Margin = new Thickness(12, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        Grid.SetColumn(iconElement, 0);

        // Title text
        var titleText = new TextBlock
        {
            Text = "QuoteBar Settings",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0)
        };
        Grid.SetColumn(titleText, 1);

        // Drag region (rest of title bar)
        var dragRegion = new Border { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetColumn(dragRegion, 2);

        titleBarGrid.Children.Add(iconElement);
        titleBarGrid.Children.Add(titleText);
        titleBarGrid.Children.Add(dragRegion);

        return titleBarGrid;
    }

    private Border CreateMenuItem(string text, string glyph, bool isSelected)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(isSelected ? _theme.SelectedColor : Colors.Transparent),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(4, 0, 4, 0),
            Tag = text
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

        stack.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Foreground = new SolidColorBrush(isSelected ? _theme.AccentColor : _theme.TextColor)
        });

        stack.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        });

        border.Child = stack;

        border.PointerPressed += (s, e) => SelectMenuItem(text);
        border.PointerEntered += (s, e) =>
        {
            if (border.Tag?.ToString() != _currentPage)
                border.Background = new SolidColorBrush(_theme.HoverColor);
        };
        border.PointerExited += (s, e) =>
        {
            if (border.Tag?.ToString() != _currentPage)
                border.Background = new SolidColorBrush(Colors.Transparent);
        };

        return border;
    }

    private void SelectMenuItem(string pageName)
    {
        _currentPage = pageName;

        foreach (var child in _menuPanel.Children)
        {
            if (child is Border border && border.Tag != null)
            {
                bool isSelected = border.Tag.ToString() == pageName;
                border.Background = new SolidColorBrush(isSelected ? _theme.SelectedColor : Colors.Transparent);

                if (border.Child is StackPanel stack && stack.Children.Count > 0 && stack.Children[0] is FontIcon icon)
                {
                    icon.Foreground = new SolidColorBrush(isSelected ? _theme.AccentColor : _theme.TextColor);
                }
            }
        }

        ShowPage(pageName);
    }

    private void ShowPage(string pageName)
    {
        var content = pageName switch
        {
            "General" => CreateGeneralSettings(),
            "Providers" => CreateProvidersSettings(),
            "Appearance" => CreateAppearanceSettings(),
            "Notifications" => CreateNotificationsSettings(),
            "About" => CreateAboutSettings(),
            _ => CreateGeneralSettings()
        };

        // Ensure content respects theme
        content.RequestedTheme = _theme.CurrentTheme;
        _contentArea.Content = content;
    }

    private ScrollViewer CreateGeneralSettings()
    {
        var scroll = new ScrollViewer { Padding = new Thickness(24) };
        var stack = new StackPanel { Spacing = 16 };

        stack.Children.Add(CreateHeader("General Settings"));

        // Start at login - uses Windows Registry
        var isStartupEnabled = StartupService.IsStartupEnabled();
        _startupToggle = CreateToggleSwitch(isStartupEnabled);
        _startupToggle.Toggled += async (s, e) =>
        {
            var success = StartupService.SetStartupEnabled(_startupToggle.IsOn);
            _settings.Settings.StartAtLogin = _startupToggle.IsOn;
            _settings.Save();

            if (!success)
            {
                var dialog = new ContentDialog
                {
                    Title = "Error",
                    Content = "Failed to update startup setting. You may need to run as administrator.",
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot
                };
                await dialog.ShowAsync();
                // Revert toggle
                _startupToggle.IsOn = !_startupToggle.IsOn;
            }
        };
        stack.Children.Add(CreateSettingCard(
            "Start at login",
            "Automatically start QuoteBar when you log in to Windows",
            _startupToggle));

        // Refresh interval
        _intervalCombo = new ComboBox { Width = 150 };
        _intervalCombo.Items.Add("1 minute");
        _intervalCombo.Items.Add("5 minutes");
        _intervalCombo.Items.Add("15 minutes");
        _intervalCombo.Items.Add("30 minutes");
        _intervalCombo.SelectedIndex = _settings.Settings.RefreshIntervalMinutes switch
        {
            1 => 0, 5 => 1, 15 => 2, 30 => 3, _ => 1
        };
        _intervalCombo.SelectionChanged += (s, e) =>
        {
            _settings.Settings.RefreshIntervalMinutes = _intervalCombo.SelectedIndex switch
            {
                0 => 1, 1 => 5, 2 => 15, 3 => 30, _ => 5
            };
            _settings.Save();
        };
        stack.Children.Add(CreateSettingCard(
            "Refresh interval",
            "How often to check for usage updates",
            _intervalCombo));

        // Hover delay with value display
        var hoverPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        _hoverSlider = new Slider
        {
            Minimum = 100,
            Maximum = 1000,
            Value = _settings.Settings.HoverDelayMs,
            Width = 150,
            StepFrequency = 50
        };
        var hoverValueText = new TextBlock
        {
            Text = $"{_settings.Settings.HoverDelayMs}ms",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            MinWidth = 50
        };
        _hoverSlider.ValueChanged += (s, e) =>
        {
            var value = (int)_hoverSlider.Value;
            _settings.Settings.HoverDelayMs = value;
            hoverValueText.Text = $"{value}ms";
            _settings.Save();
        };
        hoverPanel.Children.Add(_hoverSlider);
        hoverPanel.Children.Add(hoverValueText);
        stack.Children.Add(CreateSettingCard(
            "Hover delay",
            "Delay before showing popup on hover",
            hoverPanel));

        // Keyboard shortcuts section
        stack.Children.Add(CreateHeader("Keyboard Shortcuts", topMargin: 24));

        // Global hotkey enable/disable
        _hotkeyToggle = CreateToggleSwitch(_settings.Settings.HotkeyEnabled);
        _hotkeyToggle.Toggled += (s, e) =>
        {
            _settings.Settings.HotkeyEnabled = _hotkeyToggle.IsOn;
            _settings.Save();
            // TODO: Notify HotkeyService to enable/disable
        };
        stack.Children.Add(CreateSettingCard(
            "Enable global hotkey",
            "Use a keyboard shortcut to toggle the popup from anywhere",
            _hotkeyToggle));

        // Hotkey selection
        _hotkeyCombo = new ComboBox { Width = 180 };
        _hotkeyCombo.Items.Add("Win + Shift + Q");
        _hotkeyCombo.Items.Add("Win + Alt + Q");
        _hotkeyCombo.Items.Add("Ctrl + Alt + Q");
        _hotkeyCombo.Items.Add("Win + Shift + U");
        _hotkeyCombo.Items.Add("Win + `");
        
        // Set current selection based on settings
        var currentHotkey = _settings.Settings.HotkeyDisplayString ?? "Win + Shift + Q";
        _hotkeyCombo.SelectedIndex = currentHotkey switch
        {
            "Win + Shift + Q" => 0,
            "Win + Alt + Q" => 1,
            "Ctrl + Alt + Q" => 2,
            "Win + Shift + U" => 3,
            "Win + `" => 4,
            _ => 0
        };
        
        _hotkeyCombo.SelectionChanged += (s, e) =>
        {
            var selected = _hotkeyCombo.SelectedItem?.ToString() ?? "Win + Shift + Q";
            _settings.Settings.HotkeyDisplayString = selected;
            
            // Parse and save individual components
            var (modifiers, key) = ParseHotkeyString(selected);
            _settings.Settings.HotkeyModifiers = modifiers;
            _settings.Settings.HotkeyKey = key;
            _settings.Save();
            // TODO: Notify HotkeyService to re-register with new binding
        };
        
        stack.Children.Add(CreateSettingCard(
            "Global hotkey",
            "Keyboard shortcut to toggle the popup",
            _hotkeyCombo));

        // Shortcuts reference
        var shortcutsInfo = new TextBlock
        {
            Text = "Popup shortcuts: 1-9 (switch provider), R (refresh), D (dashboard), S (settings), P (pin), ? (help)",
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(shortcutsInfo);

        scroll.Content = stack;
        return scroll;
    }

    private ScrollViewer CreateProvidersSettings()
    {
        var scroll = new ScrollViewer { Padding = new Thickness(24) };
        var stack = new StackPanel { Spacing = 16 };

        stack.Children.Add(CreateHeader("Providers"));
        stack.Children.Add(CreateSubheader("Choose which providers to show in the popup and configure their settings"));

        // Provider visibility section
        var visibilityCard = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16)
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
        visibilityStack.Children.Add(CreateProviderToggle("z.ai", "zai", "#E85A6A"));

        visibilityCard.Child = visibilityStack;
        stack.Children.Add(visibilityCard);

        // Separator
        stack.Children.Add(new TextBlock
        {
            Text = "Provider Configuration",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 8)
        });

        // Provider cards - using dynamic status detection
        stack.Children.Add(CreateProviderCardWithAutoDetect("Codex", "codex", "#7C3AED"));
        stack.Children.Add(CreateProviderCardWithAutoDetect("Claude", "claude", "#D97757"));
        stack.Children.Add(CreateProviderCardWithAutoDetect("Cursor", "cursor", "#007AFF"));
        stack.Children.Add(CreateProviderCardWithAutoDetect("Gemini", "gemini", "#4285F4"));
        stack.Children.Add(CreateProviderCardWithAutoDetect("Copilot", "copilot", "#24292F"));
        stack.Children.Add(CreateProviderCardWithAutoDetect("Droid", "droid", "#EE6018"));
        stack.Children.Add(CreateZaiProviderCard());

        scroll.Content = stack;
        return scroll;
    }

    private Grid CreateProviderToggle(string displayName, string providerId, string colorHex)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Provider icon (SVG without background)
        FrameworkElement iconElement;
        var svgFileName = GetProviderSvgFileName(providerId);
        if (!string.IsNullOrEmpty(svgFileName))
        {
            var svgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", svgFileName);
            if (System.IO.File.Exists(svgPath))
            {
                iconElement = new Image
                {
                    Width = 18,
                    Height = 18,
                    Source = new SvgImageSource(new Uri(svgPath)),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                // Fallback to color dot
                iconElement = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Fill = new SolidColorBrush(ParseColor(colorHex)),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }
        else
        {
            // No SVG available, use color dot
            iconElement = new Ellipse
            {
                Width = 12,
                Height = 12,
                Fill = new SolidColorBrush(ParseColor(colorHex)),
                VerticalAlignment = VerticalAlignment.Center
            };
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

        // Toggle
        var toggle = new ToggleSwitch
        {
            IsOn = _settings.Settings.IsProviderEnabled(providerId),
            OnContent = "",
            OffContent = "",
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Toggled += (s, e) =>
        {
            _settings.Settings.SetProviderEnabled(providerId, toggle.IsOn);
            _settings.Save();
        };
        Grid.SetColumn(toggle, 2);

        grid.Children.Add(iconElement);
        grid.Children.Add(nameText);
        grid.Children.Add(toggle);

        return grid;
    }

    private Border CreateProviderCard(string name, string providerId, string colorHex, bool isConnected, string status)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon - custom styling per provider
        FrameworkElement iconElement = CreateProviderCardIcon(providerId, name, colorHex);

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

        // Configure/Connect button with dropdown for connected providers
        FrameworkElement buttonElement;
        
        if (isConnected)
        {
            // Create a dropdown button for connected providers
            var menuFlyout = new MenuFlyout();
            
            // Add "View Details" option
            var viewItem = new MenuFlyoutItem { Text = "View Details" };
            viewItem.Click += async (s, e) =>
            {
                var detailsContent = GetProviderDetailsContent(providerId);
                var dialog = new ContentDialog
                {
                    Title = $"{name} - Connection Details",
                    Content = detailsContent,
                    CloseButtonText = "Close",
                    XamlRoot = Content.XamlRoot
                };
                await dialog.ShowAsync();
            };
            menuFlyout.Items.Add(viewItem);
            
            // Add "Refresh" option
            var refreshItem = new MenuFlyoutItem { Text = "Refresh Data" };
            refreshItem.Click += async (s, e) =>
            {
                var usageStore = App.Current?.Services?.GetService(typeof(UsageStore)) as UsageStore;
                if (usageStore != null)
                {
                    await usageStore.RefreshAsync(providerId);
                    ShowPage("Providers"); // Refresh the page
                }
            };
            menuFlyout.Items.Add(refreshItem);
            
            menuFlyout.Items.Add(new MenuFlyoutSeparator());
            
            // Add "Disconnect" option
            var disconnectItem = new MenuFlyoutItem 
            { 
                Text = "Disconnect",
                Icon = new FontIcon { Glyph = "\uE7E8" } // Unlink icon
            };
            disconnectItem.Click += async (s, e) =>
            {
                var confirmDialog = new ContentDialog
                {
                    Title = $"Disconnect {name}?",
                    Content = $"This will clear stored credentials for {name}. You'll need to reconnect to see usage data.",
                    PrimaryButtonText = "Disconnect",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot
                };
                
                var result = await confirmDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    DisconnectProvider(providerId);
                    ShowPage("Providers"); // Refresh the page
                }
            };
            menuFlyout.Items.Add(disconnectItem);
            
            var dropDownButton = new DropDownButton
            {
                Content = "Configure",
                Padding = new Thickness(16, 8, 16, 8),
                Flyout = menuFlyout
            };
            buttonElement = dropDownButton;
        }
        else
        {
            // Connect button for not connected providers
            var connectButton = new Button
            {
                Content = "Connect",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(_theme.AccentColor),
                Foreground = new SolidColorBrush(Colors.White)
            };
            
            connectButton.Click += async (s, e) =>
            {
                await ShowConnectDialogAsync(name, providerId);
            };
            buttonElement = connectButton;
        }

        Grid.SetColumn(buttonElement, 2);

        grid.Children.Add(iconElement);
        grid.Children.Add(infoStack);
        grid.Children.Add(buttonElement);
        card.Child = grid;

        return card;
    }

    /// <summary>
    /// Shows a connect dialog with provider-specific instructions
    /// </summary>
    private async Task ShowConnectDialogAsync(string name, string providerId)
    {
        string instructions = providerId.ToLower() switch
        {
            "cursor" => "To connect Cursor:\n\n" +
                        "1. Open your browser (Edge, Chrome, or Firefox)\n" +
                        "2. Go to cursor.com and log in\n" +
                        "3. Come back here and click 'Retry Detection'\n\n" +
                        "The app will automatically detect your session from the browser.",
            
            "gemini" => "To connect Gemini:\n\n" +
                        "1. Install the Gemini CLI: npm install -g @anthropic-ai/gemini-cli\n" +
                        "2. Run: gemini auth login\n" +
                        "3. Complete the OAuth flow in your browser\n" +
                        "4. Come back here and click 'Retry Detection'",
            
            "copilot" => "To connect GitHub Copilot:\n\n" +
                         "1. Install GitHub CLI: winget install GitHub.cli\n" +
                         "2. Run: gh auth login\n" +
                         "3. Select 'GitHub.com' and complete authentication\n" +
                         "4. Come back here and click 'Retry Detection'",
            
            "codex" => "To connect Codex:\n\n" +
                       "1. Install the Codex CLI\n" +
                       "2. Run: codex auth login\n" +
                       "3. Come back here and click 'Retry Detection'",
            
            "claude" => "To connect Claude:\n\n" +
                        "1. Install the Claude CLI: npm install -g @anthropic-ai/claude-cli\n" +
                        "2. Run: claude auth login\n" +
                        "3. Complete the OAuth flow in your browser\n" +
                        "4. Come back here and click 'Retry Detection'",
            
            "droid" => "To connect Droid:\n\n" +
                       "1. Install the Droid CLI\n" +
                       "2. Make sure 'droid --version' works in terminal\n" +
                       "3. Come back here and click 'Retry Detection'",
            
            _ => $"Configuration instructions for {name} are not yet available."
        };
        
        var dialog = new ContentDialog
        {
            Title = $"Connect {name}",
            Content = new TextBlock 
            { 
                Text = instructions, 
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 400
            },
            PrimaryButtonText = "Retry Detection",
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot
        };
        
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            // Refresh the page to re-detect
            ShowPage("Providers");
        }
    }

    /// <summary>
    /// Disconnects a provider by clearing its stored credentials
    /// </summary>
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
                    // Clear Claude OAuth credentials
                    var claudePath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".claude", "credentials.json");
                    if (File.Exists(claudePath))
                        File.Delete(claudePath);
                    break;
                    
                case "gemini":
                    // Clear Gemini OAuth credentials
                    var geminiPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".gemini", "credentials.json");
                    if (File.Exists(geminiPath))
                        File.Delete(geminiPath);
                    break;
                    
                case "copilot":
                    // Note: gh CLI credentials are managed by gh, just clear our cache
                    break;
                    
                case "zai":
                    ZaiSettingsReader.DeleteApiToken();
                    break;
            }
            
            DebugLogger.Log("SettingsWindow", $"DisconnectProvider: Disconnected {providerId}");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("SettingsWindow", $"DisconnectProvider error ({providerId})", ex);
        }
    }

    /// <summary>
    /// Gets details content for a connected provider
    /// </summary>
    private FrameworkElement GetProviderDetailsContent(string providerId)
    {
        var stack = new StackPanel { Spacing = 8 };
        
        var usageStore = App.Current?.Services?.GetService(typeof(UsageStore)) as UsageStore;
        var snapshot = usageStore?.GetSnapshot(providerId);
        
        if (snapshot != null && snapshot.ErrorMessage == null)
        {
            if (snapshot.Identity?.PlanType != null)
            {
                stack.Children.Add(new TextBlock 
                { 
                    Text = $"Plan: {snapshot.Identity.PlanType}",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                });
            }
            
            if (snapshot.Primary != null)
            {
                stack.Children.Add(new TextBlock 
                { 
                    Text = $"Primary Usage: {snapshot.Primary.UsedPercent:F1}%"
                });
            }
            
            if (snapshot.Secondary != null)
            {
                stack.Children.Add(new TextBlock 
                { 
                    Text = $"Secondary Usage: {snapshot.Secondary.UsedPercent:F1}%"
                });
            }
            
            stack.Children.Add(new TextBlock 
            { 
                Text = $"Last Updated: {snapshot.FetchedAt:g}",
                Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
                FontSize = 12
            });
        }
        else
        {
            stack.Children.Add(new TextBlock 
            { 
                Text = snapshot?.ErrorMessage ?? "No data available"
            });
        }
        
        return stack;
    }

    /// <summary>
    /// Creates a provider card with automatic status detection based on UsageStore data
    /// </summary>
    private Border CreateProviderCardWithAutoDetect(string name, string providerId, string colorHex)
    {
        var (isConnected, status) = GetProviderStatus(providerId);
        return CreateProviderCard(name, providerId, colorHex, isConnected, status);
    }

    /// <summary>
    /// Detects the current status of a provider by checking:
    /// 1. If we have cached usage data without errors -> Connected
    /// 2. If provider strategies report they can execute -> Ready to connect
    /// 3. Otherwise -> Not configured
    /// </summary>
    private (bool isConnected, string status) GetProviderStatus(string providerId)
    {
        try
        {
            // First check if we have successful usage data in the store
            var usageStore = App.Current?.Services?.GetService(typeof(UsageStore)) as UsageStore;
            var snapshot = usageStore?.GetSnapshot(providerId);
            
            if (snapshot != null && snapshot.ErrorMessage == null && !snapshot.IsLoading && snapshot.Primary != null)
            {
                // We have actual data - provider is connected
                var planInfo = snapshot.Identity?.PlanType ?? "Connected";
                return (true, planInfo);
            }

            // Check provider-specific detection methods
            switch (providerId.ToLower())
            {
                case "codex":
                    if (CanDetectCLI("codex", "--version"))
                        return (true, "CLI detected");
                    break;
                    
                case "claude":
                    // Check OAuth credentials or CLI
                    var claudeCredentials = ClaudeOAuthCredentialsStore.TryLoad();
                    if (claudeCredentials != null && !claudeCredentials.IsExpired)
                        return (true, "OAuth connected");
                    if (CanDetectCLI("claude", "--version"))
                        return (true, "CLI detected");
                    break;
                    
                case "cursor":
                    if (CursorSessionStore.HasSession())
                        return (true, "Session stored");
                    break;
                    
                case "gemini":
                    if (GeminiOAuthCredentialsStore.HasValidCredentials())
                        return (true, "OAuth connected");
                    if (CanDetectCLI("gemini", "--version"))
                        return (true, "CLI detected");
                    break;
                    
                case "copilot":
                    var copilotCredentials = CopilotOAuthCredentialsStore.TryLoad();
                    if (copilotCredentials != null && !copilotCredentials.IsExpired)
                        return (true, "GitHub OAuth connected");
                    if (CanDetectCLI("gh", "auth status"))
                        return (true, "GitHub CLI authenticated");
                    break;
                    
                case "droid":
                    if (CanDetectCLI("droid", "--version"))
                        return (true, "CLI detected");
                    break;
                    
                case "zai":
                    if (ZaiSettingsReader.HasApiToken())
                        return (true, "API token configured");
                    break;
            }
            
            return (false, "Not configured");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("SettingsWindow", $"GetProviderStatus({providerId}) error", ex);
            return (false, "Not configured");
        }
    }

    /// <summary>
    /// Quick check if a CLI tool is available (synchronous, with timeout)
    /// </summary>
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

            // Wait max 2 seconds
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
        // Use secure credential store instead of settings file
        var hasToken = ZaiSettingsReader.HasApiToken();

        var card = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var mainStack = new StackPanel { Spacing = 12 };

        // Header row with icon, name, status
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon with z.ai color background
        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ParseColor("#E85A6A"))
        };
        var svgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "zai-white.svg");
        if (System.IO.File.Exists(svgPath))
        {
            iconBorder.Child = new Image
            {
                Width = 20,
                Height = 20,
                Source = new SvgImageSource(new Uri(svgPath)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        Grid.SetColumn(iconBorder, 0);

        // Info
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

        // API Token input section
        var tokenSection = new StackPanel { Spacing = 8 };
        tokenSection.Children.Add(new TextBlock
        {
            Text = "API Token",
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor)
        });

        var tokenGrid = new Grid();
        tokenGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tokenGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tokenBox = new PasswordBox
        {
            PlaceholderText = hasToken ? "Token stored securely (enter new to replace)" : "Enter your z.ai API token",
            Password = "", // Never pre-fill with actual token for security
            Padding = new Thickness(12, 8, 12, 8),
            MinHeight = 36,
            MinWidth = 250,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = true,
            Background = new SolidColorBrush(_theme.IsDarkMode 
                ? Windows.UI.Color.FromArgb(255, 50, 50, 55) 
                : Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(_theme.IsDarkMode
                ? Windows.UI.Color.FromArgb(100, 255, 255, 255)
                : Windows.UI.Color.FromArgb(100, 0, 0, 0)),
            BorderThickness = new Thickness(1)
        };
        Grid.SetColumn(tokenBox, 0);

        var saveButton = new Button
        {
            Content = "Save",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(16, 8, 16, 8),
            Background = new SolidColorBrush(_theme.AccentColor),
            Foreground = new SolidColorBrush(Colors.White)
        };
        saveButton.Click += async (s, e) =>
        {
            // Store securely using Windows Credential Manager
            var token = string.IsNullOrWhiteSpace(tokenBox.Password) ? null : tokenBox.Password;
            var success = ZaiSettingsReader.StoreApiToken(token);

            var dialog = new ContentDialog
            {
                Title = success ? "z.ai Token Saved" : "Error",
                Content = success
                    ? (string.IsNullOrWhiteSpace(token)
                        ? "API token cleared from secure storage."
                        : "API token saved securely to Windows Credential Manager. Usage data will be fetched on next refresh.")
                    : "Failed to save token to Windows Credential Manager.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();

            // Clear password box after save
            tokenBox.Password = "";

            // Refresh the providers page to update status
            ShowPage("Providers");
        };
        Grid.SetColumn(saveButton, 1);

        tokenGrid.Children.Add(tokenBox);
        tokenGrid.Children.Add(saveButton);
        tokenSection.Children.Add(tokenGrid);

        // Help text with security info
        var helpStack = new StackPanel { Spacing = 2 };
        helpStack.Children.Add(new TextBlock
        {
            Text = "Get your API token from z.ai/manage-apikey/subscription",
            FontSize = 11,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            TextWrapping = TextWrapping.Wrap
        });
        helpStack.Children.Add(new TextBlock
        {
            Text = "Token is stored securely in Windows Credential Manager",
            FontSize = 10,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            FontStyle = Windows.UI.Text.FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap
        });
        tokenSection.Children.Add(helpStack);

        mainStack.Children.Add(tokenSection);
        card.Child = mainStack;

        return card;
    }

    private FrameworkElement CreateProviderCardIcon(string providerId, string name, string colorHex)
    {
        var isDark = _theme.IsDarkMode;
        var provideLower = providerId.ToLower();

        // Special handling per provider
        switch (provideLower)
        {
            case "gemini":
                // Gemini: No background, just the original colored logo
                var geminiSvgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "gemini.svg");
                if (System.IO.File.Exists(geminiSvgPath))
                {
                    return new Image
                    {
                        Width = 36,
                        Height = 36,
                        Source = new SvgImageSource(new Uri(geminiSvgPath)),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                break;

            case "cursor":
                // Cursor: White background (dark mode) / Black background (light mode), white logo
                var cursorBgColor = isDark ? Colors.White : Colors.Black;
                var cursorSvgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons",
                    isDark ? "cursor.svg" : "cursor-white.svg");  // Inverted: dark bg needs white logo
                if (System.IO.File.Exists(cursorSvgPath))
                {
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
                        Source = new SvgImageSource(new Uri(cursorSvgPath)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    return cursorBorder;
                }
                break;

            case "droid":
                // Droid: Always #EE6018 background
                var droidBgColor = ParseColor("#EE6018");
                var droidSvgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", "droid-white.svg");
                if (System.IO.File.Exists(droidSvgPath))
                {
                    var droidBorder = new Border
                    {
                        Width = 36,
                        Height = 36,
                        CornerRadius = new CornerRadius(8),
                        Background = new SolidColorBrush(droidBgColor),
                        Padding = new Thickness(6)
                    };
                    droidBorder.Child = new Image
                    {
                        Width = 24,
                        Height = 24,
                        Source = new SvgImageSource(new Uri(droidSvgPath)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    return droidBorder;
                }
                break;

            default:
                // Other providers: Colored background with white SVG icon
                var svgFileName = GetProviderSvgFileName(providerId, forIconWithBackground: true);
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
                            Background = new SolidColorBrush(ParseColor(colorHex)),
                            Padding = new Thickness(6)
                        };
                        iconBorder.Child = new Image
                        {
                            Width = 24,
                            Height = 24,
                            Source = new SvgImageSource(new Uri(svgPath)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        return iconBorder;
                    }
                }
                break;
        }

        // Fallback to colored background with initial letter
        return CreateProviderIconWithBackground(name, colorHex);
    }

    private Border CreateProviderIconWithBackground(string name, string colorHex)
    {
        var border = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(ParseColor(colorHex))
        };
        border.Child = new TextBlock
        {
            Text = name.Substring(0, 1).ToUpper(),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        return border;
    }

    private ScrollViewer CreateAppearanceSettings()
    {
        var scroll = new ScrollViewer { Padding = new Thickness(24) };
        var stack = new StackPanel { Spacing = 16 };

        stack.Children.Add(CreateHeader("Appearance"));

        // Theme selection
        _themeCombo = new ComboBox { Width = 180 };
        _themeCombo.Items.Add("System default");
        _themeCombo.Items.Add("Light");
        _themeCombo.Items.Add("Dark");
        _themeCombo.SelectedIndex = (int)_settings.Settings.Theme;
        _themeCombo.SelectionChanged += (s, e) =>
        {
            var newTheme = (ThemeMode)_themeCombo.SelectedIndex;
            _theme.SetTheme(newTheme);

            // Apply theme immediately to this window
            if (_rootGrid != null)
            {
                _rootGrid.RequestedTheme = _theme.GetEffectiveTheme();
            }
        };
        stack.Children.Add(CreateSettingCard(
            "Theme",
            "Choose your preferred color scheme",
            _themeCombo));

        // Use system accent color
        _accentToggle = CreateToggleSwitch(_settings.Settings.UseSystemAccentColor);
        _accentToggle.Toggled += (s, e) =>
        {
            _settings.Settings.UseSystemAccentColor = _accentToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(CreateSettingCard(
            "Use system accent color",
            "Match the Windows accent color for highlights",
            _accentToggle));

        // Compact mode
        _compactToggle = CreateToggleSwitch(_settings.Settings.CompactMode);
        _compactToggle.Toggled += (s, e) =>
        {
            _settings.Settings.CompactMode = _compactToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(CreateSettingCard(
            "Compact mode",
            "Use a smaller popup with less information",
            _compactToggle));

        // Show provider icons
        _iconsToggle = CreateToggleSwitch(_settings.Settings.ShowProviderIcons);
        _iconsToggle.Toggled += (s, e) =>
        {
            _settings.Settings.ShowProviderIcons = _iconsToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(CreateSettingCard(
            "Show provider icons",
            "Display icons next to provider names in tabs",
            _iconsToggle));

        // Separator for Tray Badge section
        stack.Children.Add(new TextBlock
        {
            Text = "System Tray Badge",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 16, 0, 8)
        });

        // Tray Badge toggle
        _trayBadgeToggle = CreateToggleSwitch(_settings.Settings.TrayBadgeEnabled);
        _trayBadgeToggle.Toggled += (s, e) =>
        {
            _settings.Settings.TrayBadgeEnabled = _trayBadgeToggle.IsOn;
            _settings.Save();
            UpdateTrayBadgeProvidersVisibility();
        };
        stack.Children.Add(CreateSettingCard(
            "Show usage badges in tray",
            "Replace tray icon with usage percentages (remaining %)",
            _trayBadgeToggle));

        // Tray Badge providers selection
        _trayBadgeProvidersPanel = CreateTrayBadgeProvidersPanel();
        stack.Children.Add(_trayBadgeProvidersPanel);
        UpdateTrayBadgeProvidersVisibility();

        scroll.Content = stack;
        return scroll;
    }

    private ScrollViewer CreateNotificationsSettings()
    {
        var scroll = new ScrollViewer { Padding = new Thickness(24) };
        var stack = new StackPanel { Spacing = 16 };

        stack.Children.Add(CreateHeader("Notifications"));

        // Usage alerts
        _alertsToggle = CreateToggleSwitch(_settings.Settings.UsageAlertsEnabled);
        _alertsToggle.Toggled += (s, e) =>
        {
            _settings.Settings.UsageAlertsEnabled = _alertsToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(CreateSettingCard(
            "Usage alerts",
            "Show notifications when usage reaches certain thresholds",
            _alertsToggle));

        // Warning threshold
        _warningSlider = new Slider
        {
            Minimum = 50,
            Maximum = 95,
            Value = _settings.Settings.WarningThreshold,
            Width = 150,
            StepFrequency = 5
        };
        _warningSlider.ValueChanged += (s, e) =>
        {
            _settings.Settings.WarningThreshold = (int)_warningSlider.Value;
            _settings.Save();
        };
        stack.Children.Add(CreateSettingCard(
            $"Warning threshold ({_settings.Settings.WarningThreshold}%)",
            "Show warning when usage exceeds this percentage",
            _warningSlider));

        // Critical threshold
        _criticalSlider = new Slider
        {
            Minimum = 70,
            Maximum = 100,
            Value = _settings.Settings.CriticalThreshold,
            Width = 150,
            StepFrequency = 5
        };
        _criticalSlider.ValueChanged += (s, e) =>
        {
            _settings.Settings.CriticalThreshold = (int)_criticalSlider.Value;
            _settings.Save();
        };
        stack.Children.Add(CreateSettingCard(
            $"Critical threshold ({_settings.Settings.CriticalThreshold}%)",
            "Show critical alert when usage exceeds this percentage",
            _criticalSlider));

        // Sound
        _soundToggle = CreateToggleSwitch(_settings.Settings.PlaySound);
        _soundToggle.Toggled += (s, e) =>
        {
            _settings.Settings.PlaySound = _soundToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(CreateSettingCard(
            "Play sound",
            "Play a sound when showing notifications",
            _soundToggle));

        scroll.Content = stack;
        return scroll;
    }

    private ScrollViewer CreateAboutSettings()
    {
        var scroll = new ScrollViewer { Padding = new Thickness(24) };
        var stack = new StackPanel { Spacing = 16 };

        stack.Children.Add(CreateHeader("About QuoteBar"));

        // App info card
        var infoCard = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24)
        };

        var infoStack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };

        // Logo
        FrameworkElement logoElement;
        try
        {
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-NATIVE.png");
            if (System.IO.File.Exists(logoPath))
            {
                var image = new Image
                {
                    Width = 80,
                    Height = 80,
                    Source = new BitmapImage(new Uri(logoPath)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                var imageBorder = new Border
                {
                    CornerRadius = new CornerRadius(16),
                    Child = image
                };
                logoElement = imageBorder;
            }
            else
            {
                throw new FileNotFoundException();
            }
        }
        catch
        {
            var logoBorder = new Border
            {
                Width = 80,
                Height = 80,
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(_theme.AccentColor),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            logoBorder.Child = new FontIcon
            {
                Glyph = "\uE9D9",
                FontSize = 36,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            logoElement = logoBorder;
        }
        infoStack.Children.Add(logoElement);

        infoStack.Children.Add(new TextBlock
        {
            Text = "QuoteBar",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        infoStack.Children.Add(new TextBlock
        {
            Text = "Version 1.0.0",
            FontSize = 14,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        infoStack.Children.Add(new TextBlock
        {
            Text = "AI Usage Monitor for Windows",
            FontSize = 14,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        });

        // Links
        var linksStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0)
        };

        linksStack.Children.Add(CreateLinkButton("GitHub", "https://github.com"));
        linksStack.Children.Add(CreateLinkButton("Website", "https://nativebar.app"));
        linksStack.Children.Add(CreateLinkButton("Report Issue", "https://github.com/issues"));

        infoStack.Children.Add(linksStack);
        infoCard.Child = infoStack;
        stack.Children.Add(infoCard);

        // Check for updates
        var updateButton = new Button
        {
            Content = "Check for updates",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(16, 8, 16, 8)
        };
        updateButton.Click += async (s, e) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Check for Updates",
                Content = "You are running the latest version of NativeBar!",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        };
        stack.Children.Add(updateButton);

        scroll.Content = stack;
        return scroll;
    }

    private HyperlinkButton CreateLinkButton(string text, string url)
    {
        var button = new HyperlinkButton
        {
            Content = text,
            NavigateUri = new Uri(url),
            Foreground = new SolidColorBrush(_theme.AccentColor),
            Padding = new Thickness(8, 4, 8, 4)
        };
        return button;
    }

    private TextBlock CreateHeader(string text, double topMargin = 0)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, topMargin, 0, 8)
        };
    }

    private TextBlock CreateSubheader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            Margin = new Thickness(0, 0, 0, 16),
            TextWrapping = TextWrapping.Wrap
        };
    }

    private Border CreateSettingCard(string title, string description, FrameworkElement control)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14
        });
        textStack.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(textStack, 0);

        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.Margin = new Thickness(16, 0, 0, 0);

        grid.Children.Add(textStack);
        grid.Children.Add(control);
        card.Child = grid;

        return card;
    }

    private ToggleSwitch CreateToggleSwitch(bool isOn)
    {
        return new ToggleSwitch
        {
            IsOn = isOn,
            OnContent = "",
            OffContent = ""
        };
    }

    private StackPanel CreateTrayBadgeProvidersPanel()
    {
        var panel = new StackPanel { Spacing = 8 };

        var card = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16)
        };

        var innerStack = new StackPanel { Spacing = 12 };

        innerStack.Children.Add(new TextBlock
        {
            Text = "Select providers to show in tray (max 3)",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        innerStack.Children.Add(new TextBlock
        {
            Text = "Providers will show remaining % (100 - used)",
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Provider checkboxes
        var providers = new[] {
            ("Claude", "claude", "#D97757"),
            ("Gemini", "gemini", "#4285F4"),
            ("Copilot", "copilot", "#24292F"),
            ("Cursor", "cursor", "#007AFF"),
            ("Codex", "codex", "#7C3AED"),
            ("Droid", "droid", "#EE6018"),
            ("z.ai", "zai", "#E85A6A")
        };

        foreach (var (displayName, id, color) in providers)
        {
            var isSelected = _settings.Settings.TrayBadgeProviders.Contains(id);
            innerStack.Children.Add(CreateTrayBadgeProviderCheckbox(displayName, id, color, isSelected));
        }

        card.Child = innerStack;
        panel.Children.Add(card);
        return panel;
    }

    private Grid CreateTrayBadgeProviderCheckbox(string displayName, string providerId, string colorHex, bool isChecked)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Checkbox
        var checkbox = new CheckBox
        {
            IsChecked = isChecked,
            Tag = providerId,
            VerticalAlignment = VerticalAlignment.Center
        };
        checkbox.Checked += OnTrayBadgeProviderChanged;
        checkbox.Unchecked += OnTrayBadgeProviderChanged;
        Grid.SetColumn(checkbox, 0);

        // Color dot
        var colorDot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(ParseColor(colorHex)),
            Margin = new Thickness(4, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(colorDot, 1);

        // Name
        var nameText = new TextBlock
        {
            Text = displayName,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(nameText, 2);

        grid.Children.Add(checkbox);
        grid.Children.Add(colorDot);
        grid.Children.Add(nameText);

        return grid;
    }

    private void OnTrayBadgeProviderChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox checkbox) return;
        var providerId = checkbox.Tag?.ToString();
        if (string.IsNullOrEmpty(providerId)) return;

        var isChecked = checkbox.IsChecked == true;
        var providers = _settings.Settings.TrayBadgeProviders;

        if (isChecked)
        {
            // Check if already at max (3)
            if (providers.Count >= 3 && !providers.Contains(providerId))
            {
                // Uncheck and show message
                checkbox.IsChecked = false;
                return;
            }
            if (!providers.Contains(providerId))
            {
                providers.Add(providerId);
            }
        }
        else
        {
            providers.Remove(providerId);
        }

        _settings.Save();
    }

    private void UpdateTrayBadgeProvidersVisibility()
    {
        if (_trayBadgeProvidersPanel != null)
        {
            _trayBadgeProvidersPanel.Visibility = _settings.Settings.TrayBadgeEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private string? GetProviderSvgFileName(string providerId, bool forIconWithBackground = false)
    {
        // When icon is shown on colored background, always use white version
        if (forIconWithBackground)
        {
            return providerId.ToLower() switch
            {
                "claude" => "claude-white.svg",
                "codex" => "openai-white.svg",
                "gemini" => "gemini-white.svg",
                "copilot" => "github-copilot-white.svg",
                "cursor" => "cursor-white.svg",
                "droid" => "droid-white.svg",
                "antigravity" => "antigravity.svg",
                "zai" => "zai-white.svg",
                _ => null
            };
        }

        // Use white versions for dark icons in dark mode (when shown without background)
        var isDark = _theme.IsDarkMode;
        return providerId.ToLower() switch
        {
            "claude" => "claude.svg",  // Orange - visible in both modes
            "codex" => isDark ? "openai-white.svg" : "openai.svg",
            "gemini" => "gemini.svg",  // Blue - visible in both modes
            "copilot" => isDark ? "github-copilot-white.svg" : "github-copilot.svg",
            "cursor" => isDark ? "cursor-white.svg" : "cursor.svg",
            "droid" => isDark ? "droid-white.svg" : "droid.svg",
            "antigravity" => "antigravity.svg",  // Red - visible in both modes
            "zai" => isDark ? "zai-white.svg" : "zai.svg",  // Black/white based on theme
            _ => null
        };
    }

    private Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }

    private (List<string> Modifiers, string Key) ParseHotkeyString(string hotkeyString)
    {
        // Parse "Win + Shift + Q" into modifiers list and key
        var parts = hotkeyString.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var modifiers = new List<string>();
        var key = "Q";

        foreach (var part in parts)
        {
            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Shift", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Alt", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase))
            {
                modifiers.Add(part);
            }
            else
            {
                key = part;
            }
        }

        return (modifiers, key);
    }

    private TextBlock CreateProviderInitial(string name)
    {
        return new TextBlock
        {
            Text = name.Substring(0, 1).ToUpper(),
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void ConfigureWindow()
    {
        // Set window size - compact size that fits content
        _appWindow.Resize(new SizeInt32(800, 520));

        // Center window
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var x = (workArea.Width - 800) / 2 + workArea.X;
        var y = (workArea.Height - 520) / 2 + workArea.Y;
        _appWindow.Move(new PointInt32(x, y));

        // Set custom icon
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-NATIVE.ico");
            if (!System.IO.File.Exists(iconPath))
            {
                // Try PNG as fallback (won't work but at least won't crash)
                iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-NATIVE.png");
            }
            if (System.IO.File.Exists(iconPath))
            {
                _appWindow.SetIcon(iconPath);
            }
        }
        catch { }

        // Configure custom titlebar
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = _appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;

            // Use standard height for a more compact look
            titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

            // Set titlebar colors based on theme
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            if (_theme.IsDarkMode)
            {
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 255, 255, 255);
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(50, 255, 255, 255);
            }
            else
            {
                titleBar.ButtonForegroundColor = Colors.Black;
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 0, 0, 0);
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(50, 0, 0, 0);
            }
        }
    }
}
