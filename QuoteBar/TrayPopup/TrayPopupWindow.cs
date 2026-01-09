using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QuoteBar.Controls;
using QuoteBar.Core.CostUsage;
using QuoteBar.Core.Models;
using QuoteBar.Core.Providers;
using QuoteBar.Core.Services;
using QuoteBar.ViewModels;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace QuoteBar.TrayPopup;

/// <summary>
/// CodexBar-style popup window with dark mode support and Mica backdrop
/// </summary>
public sealed class TrayPopupWindow : Window
{
    private readonly TrayPopupViewModel _viewModel;
    private readonly AppWindow _appWindow;
    private readonly UsageStore _usageStore;
    private bool _isPinned;
    private bool _isDarkMode;
    private bool _isInitialized;

    // Appearance settings (read from SettingsService)
    private bool IsCompactMode => SettingsService.Instance.Settings.CompactMode;
    private bool ShowProviderIcons => SettingsService.Instance.Settings.ShowProviderIcons;

    // Provider tabs
    private Grid _tabsGrid = null!;
    private readonly Dictionary<string, Border> _tabButtons = new();
    private string _selectedProviderId = "codex";
    
    // Resize grace period - suppress pointer exit events briefly after tab switch
    private bool _suppressPointerExit = false;
    private DispatcherTimer? _suppressPointerExitTimer;

    // UI Elements
    private Grid _rootGrid = null!;
    private Border _popupBorder = null!;
    private Border _logoContainer = null!;  // Container for logo (can hold Image or Path)
    private TextBlock _providerNameText = null!;
    private Controls.TierBadge _tierBadge = null!;
    private TextBlock _costText = null!;  // Cost display (e.g., "$5.20 this month")
    private FontIcon _pinIcon = null!;

    // Usage sections
    private StackPanel _usageSectionsPanel = null!;
    private TextBlock _lastUpdatedText = null!;

    // Usage history chart - disabled
    // private UsageHistoryChart? _usageChart;

    // Footer links
    private StackPanel _footerLinksPanel = null!;
    private Border? _dashboardLink;
    private Border? _statusPageLink;

    // Cost dashboard popup
    private CostDashboardPopup? _costDashboardPopup;

    // Request history popup
    private RequestHistoryPopup? _requestHistoryPopup;

    /// <summary>
    /// Returns true if the cost dashboard popup is currently visible
    /// Used to prevent the main popup from closing when showing the cost dashboard
    /// </summary>
    public bool IsCostDashboardOpen => _costDashboardPopup != null;

    /// <summary>
    /// Returns true if the request history popup is currently visible
    /// </summary>
    public bool IsRequestHistoryOpen => _requestHistoryPopup != null;

    public event Action? PointerEnteredPopup;
    public event Action? PointerExitedPopup;
    public event Action? PinToggled;
    public event Action? LightDismiss;
    public event Action<bool, string, int, string>? PinStateChanged; // isPinned, providerId, usagePercentage, colorHex

    public bool IsPinned => _isPinned;
    public string SelectedProviderId => _selectedProviderId;

    // Win32 constants
    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_CAPTION = 0x00C00000;
    private const uint WS_THICKFRAME = 0x00040000;

    // DWM constants for removing border
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;

    // UI Layout constants
    private const int PopupWidth = 340;
    private const int PopupMinHeight = 300;
    private const int PopupMaxHeight = 700;
    private const int PopupMargin = 12;
    private const int LogoSize = 36;
    private const int TabIconSize = 14;
    private const int HeaderLogoSize = 32;
    private const int PointerExitGracePeriodMs = 300;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    public TrayPopupWindow(IServiceProvider serviceProvider)
    {
        Title = "QuoteBar";

        _usageStore = serviceProvider.GetRequiredService<UsageStore>();
        _viewModel = new TrayPopupViewModel(_usageStore);

        // Use ThemeService for dark mode detection
        _isDarkMode = ThemeService.Instance.IsDarkMode;

        // Listen for theme changes
        ThemeService.Instance.ThemeChanged += OnThemeChanged;

        // Listen for settings changes (provider enable/disable)
        SettingsService.Instance.SettingsChanged += OnSettingsChanged;

        // Set Desktop Acrylic backdrop for more transparency
        try
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch
        {
            // Fallback to Mica
            try { SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt }; } catch { }
        }

        // Build UI
        BuildUI();
        Content = _rootGrid;

        // Get AppWindow
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        ConfigureWindow(hwnd);

        // Set initial provider
        if (_usageStore.CurrentProviderId != null)
        {
            _selectedProviderId = _usageStore.CurrentProviderId;
        }

        UpdateUI();

        Activated += OnWindowActivated;
        Closed += OnWindowClosed;

        _isInitialized = true;
        DebugLogger.Log("TrayPopup", $"TrayPopupWindow created (CodexBar style, DarkMode={_isDarkMode})");
    }

    /// <summary>
    /// Cleanup event subscriptions to prevent memory leaks
    /// </summary>
    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _isInitialized = false;
        
        // Unsubscribe from service events
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
        SettingsService.Instance.SettingsChanged -= OnSettingsChanged;
        
        // Stop any running timers
        _suppressPointerExitTimer?.Stop();
        _suppressPointerExitTimer = null;
        
        // Close secondary popups
        CloseCostDashboard();
        CloseRequestHistoryPopup();
        
        DebugLogger.Log("TrayPopup", "TrayPopupWindow closed and cleaned up");
    }

    private void OnSettingsChanged()
    {
        // Guard: ensure popup is initialized
        if (!_isInitialized) return;

        // Rebuild UI on settings change (provider toggles, etc.)
        try
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isInitialized) return;
                try
                {
                    DebugLogger.Log("TrayPopup", "Settings changed - rebuilding UI");
                    BuildUI();
                    Content = _rootGrid;
                    UpdateUI();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("TrayPopup", "OnSettingsChanged UI rebuild failed", ex);
                }
            });
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayPopup", "OnSettingsChanged dispatch failed", ex);
        }
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        // Guard: ensure popup is initialized
        if (!_isInitialized) return;

        // Update theme on UI thread
        try
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isInitialized) return;
                try
                {
                    _isDarkMode = theme == ElementTheme.Dark;
                    // Rebuild UI with new theme colors
                    BuildUI();
                    Content = _rootGrid;
                    UpdateUI();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("TrayPopup", "OnThemeChanged UI rebuild failed", ex);
                }
            });
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayPopup", "OnThemeChanged dispatch failed", ex);
        }
    }

    // Theme colors
    private Windows.UI.Color BackgroundColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(245, 30, 30, 35)
        : Windows.UI.Color.FromArgb(245, 251, 251, 253);

    private Windows.UI.Color SurfaceColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 45, 45, 50)
        : Windows.UI.Color.FromArgb(255, 245, 245, 248);

    private Windows.UI.Color BorderColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(40, 255, 255, 255)
        : Windows.UI.Color.FromArgb(30, 0, 0, 0);

    private Windows.UI.Color PrimaryTextColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
        : Windows.UI.Color.FromArgb(255, 30, 30, 30);

    // Improved contrast ratios for WCAG 2.1 AA compliance
    // Dark mode: minimum 4.5:1 contrast against #1E1E23 background
    // Light mode: minimum 4.5:1 contrast against #FBFBFD background
    private Windows.UI.Color SecondaryTextColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 200, 200, 200)  // Improved: #C8C8C8 vs #1E1E23 = 10.4:1
        : Windows.UI.Color.FromArgb(255, 90, 90, 90);    // Improved: #5A5A5A vs #FBFBFD = 7.2:1

    private Windows.UI.Color TertiaryTextColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 160, 160, 160)  // Improved: #A0A0A0 vs #1E1E23 = 6.8:1
        : Windows.UI.Color.FromArgb(255, 110, 110, 110); // Improved: #6E6E6E vs #FBFBFD = 5.5:1

    private Windows.UI.Color DividerColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(30, 255, 255, 255)
        : Windows.UI.Color.FromArgb(20, 0, 0, 0);

    private Windows.UI.Color ProgressTrackColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(60, 255, 255, 255)
        : Windows.UI.Color.FromArgb(40, 0, 0, 0);

    private void BuildUI()
    {
        _rootGrid = new Grid
        {
            Background = new SolidColorBrush(Colors.Transparent)
        };
        _rootGrid.PointerEntered += OnPointerEntered;
        _rootGrid.PointerExited += OnPointerExited;
        _rootGrid.KeyDown += OnKeyDown;

        // Main border - NO visible border, just subtle shadow via Mica
        _popupBorder = new Border
        {
            Background = new SolidColorBrush(BackgroundColor),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0)
        };

        var mainStack = new StackPanel();

        // === Provider Tabs (CodexBar style) ===
        BuildProviderTabs(mainStack);

        // === Separator ===
        mainStack.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(DividerColor),
            Margin = new Thickness(0)
        });

        // === Scrollable content area === (Apply CompactMode padding)
        var contentPadding = IsCompactMode ? 10.0 : 16.0;
        var contentSpacing = IsCompactMode ? 8.0 : 12.0;
        var contentPanel = new StackPanel
        {
            Padding = new Thickness(contentPadding, contentPadding - 4, contentPadding, contentPadding),
            Spacing = contentSpacing
        };

        // Header with provider info
        BuildHeader(contentPanel);

        // Usage sections (dynamic)
        _usageSectionsPanel = new StackPanel { Spacing = 16 };
        contentPanel.Children.Add(_usageSectionsPanel);

        // Separator before footer
        contentPanel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(DividerColor),
            Margin = new Thickness(0, 4, 0, 4)
        });

        // Footer with links
        BuildFooter(contentPanel);

        // Wrap content in ScrollViewer for overflow handling
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = contentPanel
        };
        mainStack.Children.Add(scrollViewer);
        _popupBorder.Child = mainStack;
        _rootGrid.Children.Add(_popupBorder);
    }

    private const int MaxTabsPerRow = 3;

    private void BuildProviderTabs(StackPanel parent)
    {
        var tabsContainer = new Border
        {
            Padding = new Thickness(12, 12, 12, 8)
        };

        _tabButtons.Clear();

        // Filter providers based on settings
        var allProviders = ProviderRegistry.Instance.GetAllProviders().ToList();
        var enabledProviders = allProviders
            .Where(p => SettingsService.Instance.Settings.IsProviderEnabled(p.Id))
            .ToList();

        // If no providers enabled, show all
        if (enabledProviders.Count == 0)
        {
            enabledProviders = allProviders;
        }

        // Calculate number of rows needed
        int rowCount = (int)Math.Ceiling((double)enabledProviders.Count / MaxTabsPerRow);

        // Create a StackPanel to hold rows
        var rowsPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6
        };

        for (int row = 0; row < rowCount; row++)
        {
            var rowGrid = new Grid();

            // Get providers for this row
            int startIndex = row * MaxTabsPerRow;
            int endIndex = Math.Min(startIndex + MaxTabsPerRow, enabledProviders.Count);
            int itemsInRow = endIndex - startIndex;

            // Add columns for this row
            for (int col = 0; col < itemsInRow; col++)
            {
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            // Add tabs for this row
            for (int col = 0; col < itemsInRow; col++)
            {
                int providerIndex = startIndex + col;
                var provider = enabledProviders[providerIndex];
                var tabButton = CreateProviderTab(provider);
                Grid.SetColumn(tabButton, col);
                rowGrid.Children.Add(tabButton);
                _tabButtons[provider.Id] = tabButton;
            }

            rowsPanel.Children.Add(rowGrid);
        }

        // Store reference to first row grid for compatibility (or create empty grid if none)
        _tabsGrid = rowsPanel.Children.Count > 0 ? (Grid)rowsPanel.Children[0] : new Grid();

        // Select first enabled provider if current selection is disabled
        if (!enabledProviders.Any(p => p.Id == _selectedProviderId) && enabledProviders.Count > 0)
        {
            _selectedProviderId = enabledProviders[0].Id;
        }

        tabsContainer.Child = rowsPanel;
        parent.Children.Add(tabsContainer);
    }

    private Border CreateProviderTab(IProviderDescriptor provider)
    {
        var isSelected = provider.Id == _selectedProviderId;
        var snapshot = _usageStore.GetSnapshot(provider.Id);
        var statusText = GetProviderStatusText(provider.Id, snapshot);

        // Use a consistent selection color (similar to hover, but slightly stronger)
        var selectedBgColor = _isDarkMode
            ? Windows.UI.Color.FromArgb(60, 255, 255, 255)
            : Windows.UI.Color.FromArgb(50, 0, 0, 0);

        // Apply CompactMode padding
        var tabPadding = IsCompactMode ? new Thickness(8, 6, 8, 6) : new Thickness(12, 8, 12, 8);
        var tabMargin = IsCompactMode ? new Thickness(0, 0, 6, 0) : new Thickness(0, 0, 8, 0);

        var tab = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = tabPadding,
            Margin = tabMargin,
            Background = new SolidColorBrush(isSelected ? selectedBgColor : Colors.Transparent),
            Tag = provider.Id,
            // Enable keyboard focus for Tab navigation
            IsTabStop = true,
            TabIndex = _tabButtons.Count,
            // Focus visual styling
            FocusVisualMargin = new Thickness(-2),
            FocusVisualPrimaryThickness = new Thickness(2),
            FocusVisualSecondaryThickness = new Thickness(1)
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = ShowProviderIcons ? 6 : 4
        };

        // Provider icon - only add if ShowProviderIcons is enabled
        if (ShowProviderIcons)
        {
            FrameworkElement iconElement = CreateProviderIcon(provider, isSelected);
            content.Children.Add(iconElement);
        }

        // Provider name (adjust font size for compact mode)
        var nameFontSize = IsCompactMode ? 11.0 : 12.0;
        var name = new TextBlock
        {
            Text = provider.DisplayName,
            FontSize = nameFontSize,
            FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = new SolidColorBrush(isSelected ? PrimaryTextColor : SecondaryTextColor),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Status indicator - use InfoBadge for native WinUI 3 look
        var statusColor = GetProviderStatusColor(provider.Id);
        var statusIndicator = CreateStatusIndicator(statusColor, snapshot);

        content.Children.Add(name);
        content.Children.Add(statusIndicator);
        tab.Child = content;

        // Hover color (slightly lighter than selected)
        var hoverBgColor = _isDarkMode
            ? Windows.UI.Color.FromArgb(40, 255, 255, 255)
            : Windows.UI.Color.FromArgb(30, 0, 0, 0);

        // Pressed color (same as selected)
        var pressedBgColor = selectedBgColor;

        // Focus color (slightly highlighted)
        var focusBgColor = _isDarkMode
            ? Windows.UI.Color.FromArgb(35, 255, 255, 255)
            : Windows.UI.Color.FromArgb(25, 0, 0, 0);

        tab.PointerPressed += (s, e) =>
        {
            // Show pressed state immediately
            tab.Background = new SolidColorBrush(pressedBgColor);
            OnProviderTabClick(provider.Id);
        };
        tab.PointerEntered += (s, e) =>
        {
            if (provider.Id != _selectedProviderId)
                tab.Background = new SolidColorBrush(hoverBgColor);
        };
        tab.PointerExited += (s, e) =>
        {
            if (provider.Id != _selectedProviderId)
                tab.Background = new SolidColorBrush(Colors.Transparent);
        };

        // Keyboard support for Tab navigation
        tab.GotFocus += (s, e) =>
        {
            if (provider.Id != _selectedProviderId)
                tab.Background = new SolidColorBrush(focusBgColor);
        };
        tab.LostFocus += (s, e) =>
        {
            if (provider.Id != _selectedProviderId)
                tab.Background = new SolidColorBrush(Colors.Transparent);
        };
        tab.KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space)
            {
                tab.Background = new SolidColorBrush(pressedBgColor);
                OnProviderTabClick(provider.Id);
                e.Handled = true;
            }
        };

        return tab;
    }

    /// <summary>
    /// Create a status indicator (colored dot) for provider tabs
    /// </summary>
    private FrameworkElement CreateStatusIndicator(Windows.UI.Color statusColor, UsageSnapshot? snapshot)
    {
        // Simple colored dot indicator
        // Note: InfoBadge styles (SuccessIconInfoBadgeStyle, etc.) are not available by default in WinUI 3
        return new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(statusColor),
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>
    /// Get status text for accessibility
    /// </summary>
    private string GetProviderStatusText(string providerId, UsageSnapshot? snapshot)
    {
        if (snapshot == null || snapshot.ErrorMessage != null)
            return "Not configured";
        if (snapshot.IsLoading)
            return "Loading";
        if (snapshot.Primary != null)
            return $"{snapshot.Primary.UsedPercent:F0}% used";
        return "Connected";
    }

    private Windows.UI.Color GetProviderColor(IProviderDescriptor provider) => GetProviderColorById(provider.Id);

    private Windows.UI.Color GetProviderColorById(string providerId)
    {
        return providerId switch
        {
            "claude" => Windows.UI.Color.FromArgb(255, 217, 119, 87),   // #D97757
            "codex" => Windows.UI.Color.FromArgb(255, 124, 58, 237),    // #7C3AED
            "github" => Windows.UI.Color.FromArgb(255, 35, 134, 54),    // #238636
            "antigravity" => Windows.UI.Color.FromArgb(255, 255, 107, 107), // #FF6B6B
            "cursor" => Windows.UI.Color.FromArgb(255, 0, 122, 255),    // #007AFF
            "gemini" => Windows.UI.Color.FromArgb(255, 66, 133, 244),   // #4285F4
            "copilot" => Windows.UI.Color.FromArgb(255, 36, 41, 47),    // #24292F
            "droid" => Windows.UI.Color.FromArgb(255, 238, 96, 24),     // #EE6018
            "zai" => Windows.UI.Color.FromArgb(255, 232, 90, 106),      // #E85A6A
            "minimax" => Windows.UI.Color.FromArgb(255, 226, 22, 126),   // #E2167E
            "augment" => Windows.UI.Color.FromArgb(255, 60, 60, 60),   // Dark gray (neutral)
            _ => Windows.UI.Color.FromArgb(255, 100, 100, 100)
        };
    }

    private string GetProviderColorHex(string providerId)
    {
        return providerId switch
        {
            "claude" => "#D97757",
            "codex" => "#7C3AED",
            "github" => "#238636",
            "antigravity" => "#FF6B6B",
            "cursor" => "#007AFF",
            "gemini" => "#4285F4",
            "copilot" => "#24292F",
            "droid" => "#EE6018",
            "zai" => "#E85A6A",
            "minimax" => "#E2167E",
            "augment" => "#3C3C3C",
            _ => "#646464"
        };
    }

    /// <summary>
    /// Get current usage percentage for the selected provider
    /// </summary>
    public int GetCurrentUsagePercentage()
    {
        var snapshot = _usageStore.GetSnapshot(_selectedProviderId);
        if (snapshot?.Primary != null)
        {
            return (int)Math.Round(snapshot.Primary.UsedPercent);
        }
        return 0;
    }

    private Windows.UI.Color GetProviderStatusColor(string providerId)
    {
        // First check for real incidents from ProviderStatusService
        var statusSnapshot = ProviderStatusService.Instance.GetStatus(providerId);
        if (statusSnapshot != null)
        {
            return statusSnapshot.Level switch
            {
                ProviderStatusLevel.MajorOutage => Windows.UI.Color.FromArgb(255, 220, 53, 69),   // Red - major outage
                ProviderStatusLevel.PartialOutage => Windows.UI.Color.FromArgb(255, 255, 87, 34), // Orange - partial outage
                ProviderStatusLevel.Degraded => Windows.UI.Color.FromArgb(255, 255, 193, 7),      // Yellow - degraded performance
                ProviderStatusLevel.Maintenance => Windows.UI.Color.FromArgb(255, 66, 165, 245),  // Blue - maintenance
                ProviderStatusLevel.Operational => GetUsageBasedStatusColor(providerId),           // Use usage-based color
                _ => GetUsageBasedStatusColor(providerId)
            };
        }
        
        // Fallback to usage-based status
        return GetUsageBasedStatusColor(providerId);
    }

    private Windows.UI.Color GetUsageBasedStatusColor(string providerId)
    {
        var snapshot = _usageStore.GetSnapshot(providerId);
        if (snapshot == null || snapshot.ErrorMessage != null)
            return Windows.UI.Color.FromArgb(255, 150, 150, 150); // Gray - not configured
        if (snapshot.IsLoading)
            return Windows.UI.Color.FromArgb(255, 255, 193, 7); // Yellow - loading
        return Windows.UI.Color.FromArgb(255, 76, 175, 80); // Green - connected
    }

    // Cache for parsed SVG path data
    private static readonly Dictionary<string, string> _svgPathCache = new();
    private static readonly object _svgCacheLock = new();

    private FrameworkElement CreateProviderIcon(IProviderDescriptor provider, bool isSelected)
    {
        // Strategy: Parse SVG file once, cache path data, render with dynamic color
        var pathData = GetOrParseSvgPath(provider.Id);
        if (!string.IsNullOrEmpty(pathData))
        {
            try
            {
                var iconColor = GetProviderIconColor(provider.Id);
                var geometry = (Microsoft.UI.Xaml.Media.Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                    typeof(Microsoft.UI.Xaml.Media.Geometry), pathData);
                
                return new Microsoft.UI.Xaml.Shapes.Path
                {
                    Data = geometry,
                    Fill = new SolidColorBrush(iconColor),
                    Width = TabIconSize,
                    Height = TabIconSize,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("TrayPopup", $"Failed to parse path for {provider.Id}", ex);
            }
        }

        // Final fallback to FontIcon (always works)
        return new FontIcon
        {
            Glyph = provider.IconGlyph,
            FontSize = TabIconSize,
            Foreground = new SolidColorBrush(GetProviderIconColor(provider.Id))
        };
    }

    /// <summary>
    /// Get or parse SVG path data from file. Caches result for performance.
    /// </summary>
    private string? GetOrParseSvgPath(string providerId)
    {
        var cacheKey = providerId.ToLower();
        
        lock (_svgCacheLock)
        {
            if (_svgPathCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        // Get base SVG file (always use non-white version, we control color via Fill)
        var svgFileName = GetBaseSvgFileName(providerId);
        if (string.IsNullOrEmpty(svgFileName))
            return null;

        var svgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", svgFileName);
        if (!System.IO.File.Exists(svgPath))
            return null;

        try
        {
            var svgContent = System.IO.File.ReadAllText(svgPath);
            var pathData = ExtractPathFromSvg(svgContent);
            
            if (!string.IsNullOrEmpty(pathData))
            {
                lock (_svgCacheLock)
                {
                    _svgPathCache[cacheKey] = pathData;
                }
                return pathData;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayPopup", $"Failed to read SVG {svgFileName}", ex);
        }

        return null;
    }

    /// <summary>
    /// Extract path data from SVG content using regex
    /// Combines multiple paths into a single path string
    /// </summary>
    private string? ExtractPathFromSvg(string svgContent)
    {
        // Match all <path ... d="..." /> elements - extract the d attribute
        var matches = System.Text.RegularExpressions.Regex.Matches(
            svgContent,
            @"<path[^>]*\sd=""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        if (matches.Count == 0)
            return null;

        if (matches.Count == 1)
            return matches[0].Groups[1].Value;

        // Combine multiple paths into single path string
        var combinedPath = new System.Text.StringBuilder();
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Success && match.Groups.Count > 1)
            {
                if (combinedPath.Length > 0)
                    combinedPath.Append(' ');
                combinedPath.Append(match.Groups[1].Value);
            }
        }

        return combinedPath.Length > 0 ? combinedPath.ToString() : null;
    }

    /// <summary>
    /// Get base SVG file name (non-themed version - we control color programmatically)
    /// For Cursor, use white version in dark mode
    /// </summary>
    private string? GetBaseSvgFileName(string providerId)
    {
        var providerLower = providerId.ToLower();
        
        // Special case: Cursor uses white SVG in dark mode
        if (providerLower == "cursor")
        {
            return _isDarkMode ? "cursor-white.svg" : "cursor.svg";
        }
        
        return providerLower switch
        {
            "claude" => "claude.svg",
            "codex" => "openai.svg",
            "gemini" => "gemini.svg",
            "copilot" => "github-copilot.svg",
            "droid" => "droid.svg",
            "antigravity" => "antigravity.svg",
            "zai" => "zai.svg",
            "minimax" => "minimax-color.svg",
            "augment" => "augment.svg",
            _ => null
        };
    }

    /// <summary>
    /// Get the appropriate icon color based on theme.
    /// Brand colors stay consistent, dark icons switch to white in dark mode.
    /// </summary>
    private Windows.UI.Color GetProviderIconColor(string providerId)
    {
        return providerId.ToLower() switch
        {
            // Brand colors - always use brand color (visible in both modes)
            "claude" => Windows.UI.Color.FromArgb(255, 217, 119, 87),       // #D97757 Orange
            "gemini" => Windows.UI.Color.FromArgb(255, 66, 133, 244),       // #4285F4 Blue
            "droid" => Windows.UI.Color.FromArgb(255, 238, 96, 24),         // #EE6018 Orange
            
            // Dark icons - switch to white in dark mode (original SVG is white/black)
            "antigravity" => _isDarkMode ? Colors.White : Windows.UI.Color.FromArgb(255, 0, 0, 0), // White/Black
            "cursor" => _isDarkMode ? Colors.White : Windows.UI.Color.FromArgb(255, 0, 0, 0),
            "codex" => _isDarkMode ? Colors.White : Windows.UI.Color.FromArgb(255, 0, 0, 0),
            "copilot" => _isDarkMode ? Colors.White : Windows.UI.Color.FromArgb(255, 36, 41, 47),
            "zai" => _isDarkMode ? Colors.White : Windows.UI.Color.FromArgb(255, 0, 0, 0),
            "augment" => _isDarkMode ? Colors.White : Windows.UI.Color.FromArgb(255, 0, 0, 0), // White/Black

            // Brand colors that work in both modes
            "minimax" => Windows.UI.Color.FromArgb(255, 226, 22, 126),        // #E2167E Pink (gradient start)

            // Default - use theme-appropriate color
            _ => _isDarkMode ? Colors.White : Windows.UI.Color.FromArgb(255, 60, 60, 60)
        };
    }

    private void OnProviderTabClick(string providerId)
    {
        _selectedProviderId = providerId;
        _usageStore.CurrentProviderId = providerId;
        
        // Start grace period to prevent popup from closing during resize
        StartPointerExitGracePeriod();
        
        UpdateTabStyles();
        UpdateUI();
        UpdateFooterLinksVisibility(); // Show/hide dashboard and status links
        ResizePopupToFit(); // Adjust height based on new provider's data
    }
    
    /// <summary>
    /// Temporarily suppress pointer exit events to prevent popup from closing
    /// during tab switch and resize operations.
    /// </summary>
    private void StartPointerExitGracePeriod()
    {
        _suppressPointerExit = true;
        
        // Cancel any existing timer
        _suppressPointerExitTimer?.Stop();
        
        // Create timer to re-enable pointer exit after grace period
        _suppressPointerExitTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PointerExitGracePeriodMs)
        };
        _suppressPointerExitTimer.Tick += (s, e) =>
        {
            _suppressPointerExitTimer?.Stop();
            _suppressPointerExit = false;
        };
        _suppressPointerExitTimer.Start();
    }

    /// <summary>
    /// Resize popup to fit current content (based on provider's usage sections)
    /// Defers measurement to ensure layout is complete
    /// </summary>
    private void ResizePopupToFit()
    {
        // Defer to next frame to ensure content is fully laid out before measuring
        DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try
            {
                // Force layout update before measuring
                _rootGrid.UpdateLayout();

                var newHeight = CalculatePopupHeight();
                var currentSize = _appWindow.Size;

                // Only resize if height changed significantly (avoid flicker)
                if (Math.Abs(currentSize.Height - newHeight) > 10)
                {
                    // Keep current X position, adjust Y to grow upward (popup anchored at bottom)
                    var currentPos = _appWindow.Position;
                    var heightDiff = newHeight - currentSize.Height;
                    var newY = currentPos.Y - heightDiff;

                    // Ensure we don't go above the screen
                    var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
                    var workArea = displayArea.WorkArea;
                    newY = Math.Max(workArea.Y + 8, newY);

                    _appWindow.MoveAndResize(new RectInt32(currentPos.X, newY, currentSize.Width, newHeight));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("TrayPopup", "ResizePopupToFit error", ex);
            }
        });
    }

    private void UpdateTabStyles()
    {
        // Consistent selection color (same as in CreateProviderTab)
        var selectedBgColor = _isDarkMode
            ? Windows.UI.Color.FromArgb(60, 255, 255, 255)
            : Windows.UI.Color.FromArgb(50, 0, 0, 0);

        foreach (var (id, tab) in _tabButtons)
        {
            var provider = ProviderRegistry.Instance.GetProvider(id);
            if (provider == null) continue;

            var isSelected = id == _selectedProviderId;
            tab.Background = new SolidColorBrush(isSelected ? selectedBgColor : Colors.Transparent);

            if (tab.Child is StackPanel content)
            {
                foreach (var child in content.Children)
                {
                    if (child is FontIcon icon)
                    {
                        icon.Foreground = new SolidColorBrush(isSelected ? PrimaryTextColor : SecondaryTextColor);
                    }
                    else if (child is TextBlock text && text.Text != "")
                    {
                        text.Foreground = new SolidColorBrush(isSelected ? PrimaryTextColor : SecondaryTextColor);
                        text.FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                    }
                }
            }
        }
    }

    private void BuildHeader(StackPanel parent)
    {
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Logo container
        _logoContainer = new Border { Width = LogoSize, Height = LogoSize };
        LoadProviderLogo();
        Grid.SetColumn(_logoContainer, 0);

        // Provider info - name row with badge
        var headerTextStack = new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };

        // First row: Provider name + Tier badge
        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        _providerNameText = new TextBlock
        {
            Text = "Codex",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(PrimaryTextColor),
            VerticalAlignment = VerticalAlignment.Center
        };

        _tierBadge = new Controls.TierBadge();
        _tierBadge.VerticalAlignment = VerticalAlignment.Center;

        nameRow.Children.Add(_providerNameText);
        nameRow.Children.Add(_tierBadge);

        headerTextStack.Children.Add(nameRow);
        Grid.SetColumn(headerTextStack, 1);

        // Right side: Cost display + subtle pin icon
        var rightStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        _costText = new TextBlock
        {
            Text = "",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80)), // Green
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        // Subtle pin icon (functional, more visible when pinned or on hover)
        _pinIcon = new FontIcon
        {
            Glyph = "\uE718",
            FontSize = 12,
            Foreground = new SolidColorBrush(TertiaryTextColor),
            Opacity = 0.4,
            Margin = new Thickness(8, 0, 0, 0)
        };

        var pinContainer = new Border
        {
            Child = _pinIcon,
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Center
        };
        pinContainer.PointerPressed += (s, e) => OnPinClick();
        pinContainer.PointerEntered += (s, e) => _pinIcon.Opacity = 1.0;
        pinContainer.PointerExited += (s, e) => _pinIcon.Opacity = _isPinned ? 1.0 : 0.4;

        rightStack.Children.Add(_costText);
        rightStack.Children.Add(pinContainer);
        Grid.SetColumn(rightStack, 2);

        headerGrid.Children.Add(_logoContainer);
        headerGrid.Children.Add(headerTextStack);
        headerGrid.Children.Add(rightStack);
        parent.Children.Add(headerGrid);
    }

    private void LoadProviderLogo()
    {
        try
        {
            // Use same SVG parsing system as tab icons for consistency
            var pathData = GetOrParseSvgPath(_selectedProviderId);
            if (!string.IsNullOrEmpty(pathData))
            {
                try
                {
                    var iconColor = GetProviderIconColor(_selectedProviderId);
                    var geometry = (Microsoft.UI.Xaml.Media.Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                        typeof(Microsoft.UI.Xaml.Media.Geometry), pathData);
                    
                    var pathIcon = new Microsoft.UI.Xaml.Shapes.Path
                    {
                        Data = geometry,
                        Fill = new SolidColorBrush(iconColor),
                        Width = HeaderLogoSize,
                        Height = HeaderLogoSize,
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    _logoContainer.Child = pathIcon;
                    return;
                }
                catch { }
            }

            // Final fallback to app logo
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-64.png");
            if (System.IO.File.Exists(logoPath))
            {
                var logoImage = new Image
                {
                    Width = LogoSize,
                    Height = LogoSize,
                    Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute))
                };
                _logoContainer.Child = logoImage;
            }
        }
        catch { }
    }

    // Chart section disabled - uncomment when UsageHistoryChart is ready
    // private void BuildChartSection(StackPanel parent)
    // {
    //     var section = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
    //     var headerRow = new Grid();
    //     var headerText = new TextBlock
    //     {
    //         Text = "Usage History (14 days)",
    //         FontSize = 12,
    //         FontWeight = Microsoft.UI.Text.FontWeights.Medium,
    //         Foreground = new SolidColorBrush(SecondaryTextColor)
    //     };
    //     headerRow.Children.Add(headerText);
    //     section.Children.Add(headerRow);
    //     _usageChart = new UsageHistoryChart
    //     {
    //         Height = 100,
    //         IsDarkMode = _isDarkMode,
    //         ProviderId = _selectedProviderId,
    //         ProviderColor = GetProviderColorById(_selectedProviderId),
    //         Days = 14
    //     };
    //     section.Children.Add(_usageChart);
    //     parent.Children.Add(section);
    // }

    private void BuildFooter(StackPanel parent)
    {
        // Last updated row
        var updateRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };

        _lastUpdatedText = new TextBlock
        {
            Text = "Updated: just now",
            FontSize = 11,
            Foreground = new SolidColorBrush(TertiaryTextColor),
            VerticalAlignment = VerticalAlignment.Center
        };

        var refreshButton = CreateIconButton("\uE72C", out _);
        refreshButton.HorizontalAlignment = HorizontalAlignment.Right;
        refreshButton.PointerPressed += async (s, e) => await OnRefreshClick();

        updateRow.Children.Add(_lastUpdatedText);
        updateRow.Children.Add(refreshButton);
        parent.Children.Add(updateRow);

        // Footer links (CodexBar style)
        _footerLinksPanel = new StackPanel { Spacing = 4 };

        AddFooterLink("\uE713", "Add Account...", null, OnAddAccountClick);
        _dashboardLink = AddFooterLink("\uE9D9", "Usage Dashboard", null, OnUsageDashboardClick);
        _statusPageLink = AddFooterLink("\uE946", "Status Page", null, OnStatusPageClick);
        AddFooterLink("\uE81C", "Request History", null, OnRequestHistoryClick);
        
        // Update visibility based on current provider
        UpdateFooterLinksVisibility();

        parent.Children.Add(_footerLinksPanel);

        // Settings row
        var settingsRow = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        AddFooterLink("\uE713", "Settings...", settingsRow, OnSettingsClick);
        AddFooterLink("\uE946", "About QuoteBar", settingsRow, OnAboutClick);
        AddFooterLink("\uE7E8", "Quit", settingsRow, OnQuitClick);
        parent.Children.Add(settingsRow);

        // Keyboard shortcuts hint
        var shortcutsHint = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        
        var hintText = new TextBlock
        {
            Text = "Press ? for shortcuts",
            FontSize = 10,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 
                _isDarkMode ? (byte)255 : (byte)0, 
                _isDarkMode ? (byte)255 : (byte)0, 
                _isDarkMode ? (byte)255 : (byte)0)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        
        // Make the hint clickable
        hintText.PointerPressed += (s, e) => ShowShortcutsHelp();
        hintText.PointerEntered += (s, e) => 
        {
            hintText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(180, 
                _isDarkMode ? (byte)255 : (byte)0, 
                _isDarkMode ? (byte)255 : (byte)0, 
                _isDarkMode ? (byte)255 : (byte)0));
        };
        hintText.PointerExited += (s, e) => 
        {
            hintText.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(100, 
                _isDarkMode ? (byte)255 : (byte)0, 
                _isDarkMode ? (byte)255 : (byte)0, 
                _isDarkMode ? (byte)255 : (byte)0));
        };
        
        shortcutsHint.Children.Add(hintText);
        parent.Children.Add(shortcutsHint);
    }

    public event Action? SettingsRequested;
    public event Action<string>? SettingsPageRequested;
    public event Action? QuitRequested;

    private void OnAddAccountClick() => SettingsPageRequested?.Invoke("Providers");
    
    private void OnUsageDashboardClick()
    {
        var url = GetDashboardUrl(_selectedProviderId);
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }
    
    private void OnStatusPageClick()
    {
        var url = GetStatusPageUrl(_selectedProviderId);
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }
    }

    private void OnRequestHistoryClick()
    {
        // Close existing popup if open
        CloseRequestHistoryPopup();

        try
        {
            _requestHistoryPopup = new RequestHistoryPopup(_isDarkMode);
            _requestHistoryPopup.Closed += () =>
            {
                _requestHistoryPopup = null;
            };

            // Position to the left of main popup
            var mainPos = _appWindow.Position;
            var mainSize = _appWindow.Size;
            var popupX = mainPos.X - 350; // 340 width + 10 margin
            var popupY = mainPos.Y;

            // Ensure it stays on screen
            var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            if (popupX < workArea.X + 8)
            {
                // If no room on left, position to the right
                popupX = mainPos.X + mainSize.Width + 10;
            }

            _requestHistoryPopup.PositionAt(popupX, popupY);
            _requestHistoryPopup.Show();
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayPopup", "Failed to open request history popup", ex);
        }
    }

    private void CloseRequestHistoryPopup()
    {
        try
        {
            _requestHistoryPopup?.Close();
        }
        catch { }
        _requestHistoryPopup = null;
    }
    
    private string? GetDashboardUrl(string providerId)
    {
        return providerId.ToLower() switch
        {
            "claude" => "https://console.anthropic.com/settings/usage",
            "codex" => "https://platform.openai.com/usage",
            "gemini" => "https://aistudio.google.com/apikey",
            "copilot" => "https://github.com/settings/copilot",
            "cursor" => "https://cursor.com/settings",
            "droid" => "https://app.factory.ai/settings/usage",
            "antigravity" => null, // Local-only provider, no web dashboard
            "zai" => "https://z.ai/manage-apikey/subscription",
            "minimax" => "https://platform.minimax.io",
            "augment" => "https://app.augmentcode.com/account/subscription",
            _ => null
        };
    }

    private string? GetStatusPageUrl(string providerId)
    {
        // First try to get from ProviderStatusService (has live polling)
        var statusService = ProviderStatusService.Instance;
        var statusUrl = statusService.GetStatusPageUrl(providerId);
        if (!string.IsNullOrEmpty(statusUrl))
            return statusUrl;
            
        // Fallback to static mapping
        return providerId.ToLower() switch
        {
            "claude" => "https://status.anthropic.com/",
            "codex" => "https://status.openai.com/",
            "gemini" => "https://status.cloud.google.com/",
            "copilot" => "https://www.githubstatus.com/",
            "cursor" => "https://status.cursor.com/",
            "droid" => "https://status.factory.ai/",
            "antigravity" => null, // Local-only provider, no status page
            "zai" => null,
            "minimax" => null,
            "augment" => "https://status.augmentcode.com/",
            _ => null
        };
    }
    
    /// <summary>
    /// Update visibility of Dashboard and Status Page links based on selected provider.
    /// Some providers (like Antigravity) are local-only and don't have web dashboards.
    /// </summary>
    private void UpdateFooterLinksVisibility()
    {
        if (_dashboardLink != null)
        {
            var hasDashboard = GetDashboardUrl(_selectedProviderId) != null;
            _dashboardLink.Visibility = hasDashboard ? Visibility.Visible : Visibility.Collapsed;
        }
        
        if (_statusPageLink != null)
        {
            var hasStatusPage = GetStatusPageUrl(_selectedProviderId) != null;
            _statusPageLink.Visibility = hasStatusPage ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    
    private void OnSettingsClick() => SettingsRequested?.Invoke();
    private void OnAboutClick() => SettingsPageRequested?.Invoke("About");
    private void OnQuitClick() => QuitRequested?.Invoke();

    private Border AddFooterLink(string glyph, string text, StackPanel? container = null, Action? onClick = null)
    {
        var link = new Border
        {
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Colors.Transparent)
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        content.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 12,
            Foreground = new SolidColorBrush(SecondaryTextColor)
        });

        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = new SolidColorBrush(SecondaryTextColor),
            VerticalAlignment = VerticalAlignment.Center
        });

        link.Child = content;

        link.PointerEntered += (s, e) => link.Background = new SolidColorBrush(_isDarkMode
            ? Windows.UI.Color.FromArgb(30, 255, 255, 255)
            : Windows.UI.Color.FromArgb(20, 0, 0, 0));
        link.PointerExited += (s, e) => link.Background = new SolidColorBrush(Colors.Transparent);

        if (onClick != null)
        {
            link.PointerPressed += (s, e) => onClick();
        }

        (container ?? _footerLinksPanel).Children.Add(link);
        
        return link;
    }

    private Border CreateIconButton(string glyph, out FontIcon icon)
    {
        var button = new Border
        {
            Background = new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Top
        };

        icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 12,
            Foreground = new SolidColorBrush(SecondaryTextColor)
        };

        button.Child = icon;
        button.PointerEntered += (s, e) => button.Background = new SolidColorBrush(_isDarkMode
            ? Windows.UI.Color.FromArgb(40, 255, 255, 255)
            : Windows.UI.Color.FromArgb(30, 0, 0, 0));
        button.PointerExited += (s, e) => button.Background = new SolidColorBrush(Colors.Transparent);

        return button;
    }

    private void ConfigureWindow(IntPtr hwnd)
    {
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        // Set window styles
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW | WS_EX_TOPMOST);

        // Remove window caption and thick frame to eliminate border
        var style = (uint)GetWindowLong(hwnd, GWL_STYLE);
        style &= ~WS_CAPTION;
        style &= ~WS_THICKFRAME;
        SetWindowLong(hwnd, GWL_STYLE, (int)style);

        // Use DWM to set rounded corners (Windows 11)
        int cornerPreference = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

        // Set border color to transparent (COLORREF 0xFFFFFFFF = no border)
        int borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));

        // Extend frame into client area for seamless look
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && !_isPinned)
        {
            LightDismiss?.Invoke();
        }
    }

    public void PositionNear(int iconX, int iconY, int iconWidth, int iconHeight, bool taskbarAtBottom = true)
    {
        // Calculate dynamic height based on number of provider rows
        int popupHeight = CalculatePopupHeight();

        int x, y;

        if (taskbarAtBottom)
        {
            x = iconX + (iconWidth / 2) - (PopupWidth / 2);
            y = iconY - popupHeight - PopupMargin;
        }
        else
        {
            x = iconX + (iconWidth / 2) - (PopupWidth / 2);
            y = iconY + iconHeight + PopupMargin;
        }

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        x = Math.Max(workArea.X + 8, Math.Min(x, workArea.X + workArea.Width - PopupWidth - 8));
        y = Math.Max(workArea.Y + 8, Math.Min(y, workArea.Y + workArea.Height - popupHeight - 8));

        _appWindow.MoveAndResize(new RectInt32(x, y, PopupWidth, popupHeight));
    }

    /// <summary>
    /// Calculate popup height based on actual rendered content
    /// Uses WinUI Measure() to get the true desired height
    /// </summary>
    private int CalculatePopupHeight()
    {
        try
        {
            // Measure the actual content to get desired height
            _rootGrid.Measure(new Windows.Foundation.Size(PopupWidth, double.PositiveInfinity));
            var desiredHeight = (int)Math.Ceiling(_rootGrid.DesiredSize.Height);

            // Clamp to reasonable bounds
            return Math.Max(PopupMinHeight, Math.Min(PopupMaxHeight, desiredHeight));
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayPopup", "CalculatePopupHeight measure error", ex);
            // Fallback to estimate-based calculation
            return CalculatePopupHeightFallback();
        }
    }

    /// <summary>
    /// Fallback height calculation using estimates (when Measure fails)
    /// </summary>
    private int CalculatePopupHeightFallback()
    {
        int baseHeight = IsCompactMode ? 380 : 460;
        int tabRowHeight = IsCompactMode ? 28 : 36;
        int usageSectionHeight = IsCompactMode ? 55 : 70;
        const int baseTabRows = 2;

        var allProviders = ProviderRegistry.Instance.GetAllProviders().ToList();
        var enabledCount = allProviders.Count(p => SettingsService.Instance.Settings.IsProviderEnabled(p.Id));

        if (enabledCount == 0)
            enabledCount = allProviders.Count;

        int rowCount = (int)Math.Ceiling((double)enabledCount / MaxTabsPerRow);
        int extraTabRows = Math.Max(0, rowCount - baseTabRows);
        int usageSectionCount = GetCurrentProviderUsageSectionCount();

        return baseHeight + (extraTabRows * tabRowHeight) + (usageSectionCount * usageSectionHeight);
    }

    /// <summary>
    /// Count actual usage sections for the currently selected provider
    /// </summary>
    private int GetCurrentProviderUsageSectionCount()
    {
        var snapshot = _usageStore.GetSnapshot(_selectedProviderId);
        if (snapshot == null || snapshot.ErrorMessage != null)
            return 1; // Show at least 1 section for error/no data

        int count = 0;
        if (snapshot.Primary != null) count++;
        if (snapshot.Secondary != null) count++;
        if (snapshot.Tertiary != null) count++;
        if (snapshot.Cost != null) count++;

        return Math.Max(1, count); // At least 1 section
    }

    public void ShowPopup()
    {
        // Re-detect dark mode on show using ThemeService
        var newDarkMode = ThemeService.Instance.IsDarkMode;
        if (newDarkMode != _isDarkMode)
        {
            _isDarkMode = newDarkMode;
            RefreshTheme();
        }

        _viewModel.RefreshData();
        UpdateUI();
        Activate();
    }

    public void HidePopup()
    {
        // Don't hide if cost dashboard or request history is open - the user is interacting with it
        if (IsCostDashboardOpen || IsRequestHistoryOpen)
        {
            DebugLogger.Log("TrayPopupWindow", "HidePopup blocked - secondary popup is open");
            return;
        }

        // Close secondary popups if open (safety check)
        CloseCostDashboard();
        CloseRequestHistoryPopup();

        var hwnd = WindowNative.GetWindowHandle(this);
        ShowWindow(hwnd, 0);
    }

    private void RefreshTheme()
    {
        _popupBorder.Background = new SolidColorBrush(BackgroundColor);
        // Full rebuild would be needed for complete theme refresh
    }

    private void UpdateUI()
    {
        var provider = _usageStore.GetCurrentProvider();
        var snapshot = _usageStore.GetCurrentSnapshot();

        // Update header
        if (provider != null)
        {
            _providerNameText.Text = provider.DisplayName;
            // Update provider logo
            LoadProviderLogo();
        }

        // Update cost display (use locale-aware formatting)
        if (snapshot?.Cost != null && snapshot.Cost.TotalCostUSD > 0)
        {
            _costText.Text = FormatCost(snapshot.Cost.TotalCostUSD);
            _costText.Visibility = Visibility.Visible;
        }
        else
        {
            _costText.Text = "";
            _costText.Visibility = Visibility.Collapsed;
        }

        // Update identity info with tier badge
        _tierBadge.SetTier(snapshot?.Identity?.PlanType);

        // Clear and rebuild usage sections
        _usageSectionsPanel.Children.Clear();
        BuildUsageSections(snapshot, provider);

        // Update chart - disabled
        // try
        // {
        //     if (_usageChart != null)
        //     {
        //         _usageChart.ProviderId = _selectedProviderId;
        //         _usageChart.ProviderColor = GetProviderColorById(_selectedProviderId);
        //         _usageChart.IsDarkMode = _isDarkMode;
        //         _usageChart.Refresh();
        //     }
        // }
        // catch (Exception ex)
        // {
        //     DebugLogger.LogError("TrayPopup", "Chart update failed", ex);
        // }

        // Update footer
        _lastUpdatedText.Text = $"Updated: {_viewModel.LastUpdatedText}";

        // Pin icon
        _pinIcon.Glyph = _isPinned ? "\uE840" : "\uE718";

        // Update tab status indicators
        foreach (var (id, tab) in _tabButtons)
        {
            if (tab.Child is StackPanel content)
            {
                var statusDot = content.Children.OfType<Border>().FirstOrDefault(b => b.Width == 6);
                if (statusDot != null)
                {
                    statusDot.Background = new SolidColorBrush(GetProviderStatusColor(id));
                }
            }
        }
    }

    private void BuildUsageSections(UsageSnapshot? snapshot, IProviderDescriptor? provider)
    {
        if (snapshot == null || provider == null)
        {
            AddUsageSection("5-hour window", 0, null, "No data");
            return;
        }

        // Handle upgrade required case with special UI
        if (snapshot.RequiresUpgrade && !string.IsNullOrEmpty(snapshot.UpgradeUrl))
        {
            AddUpgradeSection(snapshot.ErrorMessage ?? "Upgrade required", snapshot.UpgradeUrl, snapshot.Identity);
            return;
        }

        // Handle session expired / requires re-authentication
        if (snapshot.RequiresReauth)
        {
            AddReauthSection(snapshot.ProviderId, snapshot.ErrorMessage ?? "Session expired. Please log in again.");
            return;
        }

        if (snapshot.ErrorMessage != null)
        {
            AddUsageSection(provider.PrimaryLabel, 0, null, snapshot.ErrorMessage);
            return;
        }

        // Primary usage
        if (snapshot.Primary != null)
        {
            AddUsageSection(
                snapshot.Primary.Label ?? provider.PrimaryLabel,
                snapshot.Primary.UsedPercent,
                snapshot.Primary,
                _viewModel.FormatUsage(snapshot.Primary),
                _viewModel.FormatResetTime(snapshot.Primary)
            );
        }

        // Secondary usage
        if (snapshot.Secondary != null)
        {
            AddUsageSection(
                snapshot.Secondary.Label ?? provider.SecondaryLabel,
                snapshot.Secondary.UsedPercent,
                snapshot.Secondary,
                _viewModel.FormatUsage(snapshot.Secondary),
                _viewModel.FormatResetTime(snapshot.Secondary),
                true
            );
        }

        // Tertiary/Extra usage
        if (snapshot.Tertiary != null)
        {
            AddUsageSection(
                snapshot.Tertiary.Label ?? provider.TertiaryLabel ?? "Extra",
                snapshot.Tertiary.UsedPercent,
                snapshot.Tertiary,
                _viewModel.FormatUsage(snapshot.Tertiary),
                _viewModel.FormatResetTime(snapshot.Tertiary)
            );
        }

        // Cost section (if available)
        if (snapshot.Cost != null)
        {
            AddCostSection(snapshot.Cost);
        }

        // Available models section (if available, e.g., for Copilot)
        if (snapshot.AvailableModels != null && snapshot.AvailableModels.Count > 0)
        {
            AddAvailableModelsSection(snapshot.AvailableModels);
        }
    }

    private void AddUsageSection(string label, double percent, RateWindow? window, string valueText, string? resetText = null, bool showPace = false)
    {
        // Apply CompactMode spacing
        var sectionSpacing = IsCompactMode ? 4.0 : 6.0;
        var section = new StackPanel { Spacing = sectionSpacing };

        // Label row - apply CompactMode font sizes
        var labelFontSize = IsCompactMode ? 12.0 : 13.0;
        var labelRow = new Grid();

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = labelFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = new SolidColorBrush(PrimaryTextColor)
        };

        var valueTextBlock = new TextBlock
        {
            Text = valueText,
            FontSize = labelFontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = new SolidColorBrush(PrimaryTextColor)
        };

        labelRow.Children.Add(labelText);
        labelRow.Children.Add(valueTextBlock);
        section.Children.Add(labelRow);

        // Progress bar - apply CompactMode sizing, stretch to align with text
        var progressHeight = IsCompactMode ? 5.0 : 6.0;
        var progressRadius = progressHeight / 2;

        var progressTrack = new Border
        {
            Height = progressHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(progressRadius),
            Background = new SolidColorBrush(ProgressTrackColor)
        };

        // Use percentage-based width for the fill via a Grid
        var progressContainer = new Grid();

        var progressFill = new Border
        {
            Height = progressHeight,
            CornerRadius = new CornerRadius(progressRadius),
            Background = new SolidColorBrush(GetProgressColor(percent)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // Create a two-column grid: fill column (percent%) and empty column (remaining%)
        var clampedPercent = Math.Max(0, Math.Min(percent, 100));
        if (clampedPercent > 0)
        {
            progressContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(clampedPercent, GridUnitType.Star) });
            progressContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - clampedPercent, GridUnitType.Star) });
            Grid.SetColumn(progressFill, 0);
            progressContainer.Children.Add(progressFill);
        }

        progressTrack.Child = progressContainer;
        section.Children.Add(progressTrack);

        // Reset time always shown; pace only in non-compact mode
        bool hasResetText = !string.IsNullOrEmpty(resetText);
        bool showPaceInfo = !IsCompactMode && showPace && window != null;
        
        if (hasResetText || showPaceInfo)
        {
            var infoFontSize = IsCompactMode ? 9.0 : 10.0;
            var infoRow = new Grid();

            if (showPaceInfo)
            {
                // Calculate pace
                var pacePercent = CalculatePace(window!);
                var paceText = new TextBlock
                {
                    Text = $"Pace: {(pacePercent >= 0 ? "Ahead" : "Behind")} ({pacePercent:+0;-0}%) · Lasts to reset",
                    FontSize = infoFontSize,
                    Foreground = new SolidColorBrush(TertiaryTextColor)
                };
                infoRow.Children.Add(paceText);
            }

            if (hasResetText)
            {
                var resetTextBlock = new TextBlock
                {
                    Text = resetText,
                    FontSize = infoFontSize,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Foreground = new SolidColorBrush(TertiaryTextColor)
                };
                infoRow.Children.Add(resetTextBlock);
            }

            section.Children.Add(infoRow);
        }

        _usageSectionsPanel.Children.Add(section);
    }

    /// <summary>
    /// Adds a special upgrade section when the current plan doesn't support the provider.
    /// Shows a warning icon, message, plan info, and an upgrade button.
    /// </summary>
    private void AddUpgradeSection(string message, string upgradeUrl, ProviderIdentity? identity)
    {
        var section = new StackPanel 
        { 
            Spacing = 10,
            Padding = new Thickness(0, 4, 0, 4)
        };

        // Warning icon with message
        var warningRow = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var warningIcon = new FontIcon
        {
            Glyph = "\uE7BA", // Warning triangle
            FontSize = 16,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 180, 0)) // Orange/Yellow warning color
        };

        var warningText = new TextBlock
        {
            Text = message,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(SecondaryTextColor),
            MaxWidth = 260
        };

        warningRow.Children.Add(warningIcon);
        warningRow.Children.Add(warningText);
        section.Children.Add(warningRow);

        // Current plan info with tier badge (if available)
        if (identity != null && !string.IsNullOrEmpty(identity.PlanType))
        {
            var planRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 6
            };
            
            var planLabel = new TextBlock
            {
                Text = "Current plan:",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(TertiaryTextColor)
            };
            
            var planBadge = Controls.TierBadge.Create(identity.PlanType);
            
            planRow.Children.Add(planLabel);
            planRow.Children.Add(planBadge);
            section.Children.Add(planRow);
        }

        // Upgrade button
        var upgradeButton = new Button
        {
            Content = "Upgrade Plan",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 8, 16, 8),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 124, 58, 237)), // Purple (Codex color)
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0)
        };

        upgradeButton.Click += (s, e) =>
        {
            try
            {
                DebugLogger.Log("TrayPopupWindow", $"Opening upgrade URL: {upgradeUrl}");
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = upgradeUrl,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("TrayPopupWindow", "Failed to open upgrade URL", ex);
            }
        };

        section.Children.Add(upgradeButton);
        _usageSectionsPanel.Children.Add(section);
    }

    /// <summary>
    /// Adds a re-authentication section when the session has expired.
    /// Shows a warning icon, message, and a "Sign In" button.
    /// </summary>
    private void AddReauthSection(string providerId, string message)
    {
        var section = new StackPanel 
        { 
            Spacing = 10,
            Padding = new Thickness(0, 4, 0, 4)
        };

        // Warning icon with message
        var warningRow = new StackPanel 
        { 
            Orientation = Orientation.Horizontal, 
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var warningIcon = new FontIcon
        {
            Glyph = "\uE72E", // Lock icon - session locked out
            FontSize = 16,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100)) // Red-ish warning color
        };

        var warningText = new TextBlock
        {
            Text = "Session expired",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100))
        };

        warningRow.Children.Add(warningIcon);
        warningRow.Children.Add(warningText);
        section.Children.Add(warningRow);

        // Detailed message
        var detailText = new TextBlock
        {
            Text = "Please sign in again to continue tracking usage.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            HorizontalTextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(SecondaryTextColor),
            MaxWidth = 260
        };
        section.Children.Add(detailText);

        // Sign In button
        var signInButton = new Button
        {
            Content = "Sign In",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 10, 16, 10),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 130, 246)), // Blue
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0)
        };

        signInButton.Click += async (s, e) =>
        {
            try
            {
                DebugLogger.Log("TrayPopupWindow", $"Re-login requested for provider: {providerId}");
                await LaunchProviderLoginAsync(providerId);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("TrayPopupWindow", "Failed to launch provider login", ex);
            }
        };

        section.Children.Add(signInButton);

        // Settings link
        var settingsLink = new HyperlinkButton
        {
            Content = "Or configure in Settings",
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 11,
            Padding = new Thickness(0)
        };
        settingsLink.Click += (s, e) => SettingsPageRequested?.Invoke("Providers");
        section.Children.Add(settingsLink);

        _usageSectionsPanel.Children.Add(section);
    }

    /// <summary>
    /// Launches the login flow for a specific provider
    /// </summary>
    private async Task LaunchProviderLoginAsync(string providerId)
    {
        try
        {
            switch (providerId.ToLower())
            {
                case "augment":
                    var augmentResult = await Core.Providers.Augment.AugmentLoginHelper.LaunchLoginAsync();
                    if (augmentResult.IsSuccess)
                    {
                        DebugLogger.Log("TrayPopupWindow", "Augment login successful, refreshing...");
                        _viewModel.RefreshCommand.Execute(null);
                    }
                    break;
                    
                case "cursor":
                    var cursorWindow = new Views.CursorLoginWindow();
                    cursorWindow.Activate();
                    break;
                    
                case "droid":
                    var droidWindow = new Views.DroidLoginWindow();
                    droidWindow.Activate();
                    break;
                    
                case "gemini":
                    // Gemini uses OAuth - open settings
                    SettingsPageRequested?.Invoke("Providers");
                    break;
                    
                case "copilot":
                    // Copilot uses OAuth device flow - open settings
                    SettingsPageRequested?.Invoke("Providers");
                    break;
                    
                case "claude":
                    // Claude Code needs terminal login - open terminal with claude command
                    LaunchClaudeLogin();
                    break;
                    
                default:
                    // For other providers, open settings
                    SettingsPageRequested?.Invoke("Providers");
                    break;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayPopupWindow", $"LaunchProviderLoginAsync failed for {providerId}", ex);
            // Fallback to settings
            SettingsPageRequested?.Invoke("Providers");
        }
    }

    /// <summary>
    /// Launch Claude Code login by opening a terminal with the claude command.
    /// The user needs to run 'claude' to trigger the OAuth flow.
    /// </summary>
    private void LaunchClaudeLogin()
    {
        try
        {
            // Open Windows Terminal or cmd with claude command
            // This will trigger the OAuth flow in Claude Code CLI
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k claude",
                UseShellExecute = true,
                CreateNoWindow = false
            };
            
            Process.Start(startInfo);
            DebugLogger.Log("TrayPopupWindow", "Launched terminal for Claude login");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayPopupWindow", "Failed to launch Claude login terminal", ex);
            // Fallback: open settings
            SettingsPageRequested?.Invoke("Providers");
        }
    }

    private void AddCostSection(ProviderCost cost)
    {
        // Wrap in a Border for rounded corners and better hover styling
        var hoverColor = _isDarkMode
            ? Windows.UI.Color.FromArgb(30, 255, 255, 255)  // Light overlay in dark mode
            : Windows.UI.Color.FromArgb(20, 0, 0, 0);       // Dark overlay in light mode

        var container = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(-10, 8, -10, 0),  // Negative margin to extend to edges
            Background = new SolidColorBrush(Colors.Transparent)
        };

        var section = new StackPanel { Spacing = 4 };

        // Header - clickable to show cost dashboard flyout
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerText = new TextBlock
        {
            Text = "Cost",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = new SolidColorBrush(PrimaryTextColor)
        };
        Grid.SetColumn(headerText, 0);

        var arrowIcon = new FontIcon
        {
            Glyph = "\uE76C",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = new SolidColorBrush(SecondaryTextColor)
        };
        Grid.SetColumn(arrowIcon, 1);

        headerRow.Children.Add(headerText);
        headerRow.Children.Add(arrowIcon);
        section.Children.Add(headerRow);

        // Today's cost (session)
        var sessionCost = cost.SessionCostUSD ?? 0;
        var sessionTokens = cost.SessionTokens ?? 0;
        AddCostRow(section, "Today:",
            FormatCost(sessionCost),
            FormatTokenCount(sessionTokens));

        // Last 30 days
        AddCostRow(section, "Last 30 days:",
            FormatCost(cost.TotalCostUSD),
            FormatTokenCount(cost.TotalTokens ?? 0));

        container.Child = section;

        // Attach click handler to show cost dashboard popup
        container.Tapped += async (s, e) =>
        {
            DebugLogger.Log("TrayPopupWindow", "Cost section tapped");
            e.Handled = true;
            await ShowCostDashboardPopupAsync();
        };

        // Hover effect - works in both dark and light mode
        container.PointerEntered += (s, e) =>
        {
            container.Background = new SolidColorBrush(hoverColor);
        };
        container.PointerExited += (s, e) =>
        {
            container.Background = new SolidColorBrush(Colors.Transparent);
        };

        _usageSectionsPanel.Children.Add(container);
    }

    /// <summary>
    /// Adds a section displaying available models for providers like Copilot
    /// Shows top models as chips with vendor info
    /// </summary>
    private void AddAvailableModelsSection(List<AvailableModel> models)
    {
        var section = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };

        // Header
        var headerRow = new Grid();
        var headerText = new TextBlock
        {
            Text = "Available Models",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = new SolidColorBrush(SecondaryTextColor)
        };

        var countText = new TextBlock
        {
            Text = $"{models.Count} models",
            FontSize = 10,
            Foreground = new SolidColorBrush(TertiaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        headerRow.Children.Add(headerText);
        headerRow.Children.Add(countText);
        section.Children.Add(headerRow);

        // Model chips - show top 6 models in a wrap panel style (2 rows of 3)
        var modelsToShow = models.Take(6).ToList();
        
        // Use a grid for consistent chip sizing (2 columns)
        var chipsGrid = new Grid { RowSpacing = 4, ColumnSpacing = 4 };
        chipsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        chipsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int rowCount = (int)Math.Ceiling(modelsToShow.Count / 2.0);
        for (int i = 0; i < rowCount; i++)
        {
            chipsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int i = 0; i < modelsToShow.Count; i++)
        {
            var model = modelsToShow[i];
            var chip = CreateModelChip(model);
            Grid.SetRow(chip, i / 2);
            Grid.SetColumn(chip, i % 2);
            chipsGrid.Children.Add(chip);
        }

        section.Children.Add(chipsGrid);

        // Show "and X more" if there are more models
        if (models.Count > 6)
        {
            var moreText = new TextBlock
            {
                Text = $"+ {models.Count - 6} more models available",
                FontSize = 10,
                Foreground = new SolidColorBrush(TertiaryTextColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            section.Children.Add(moreText);
        }

        _usageSectionsPanel.Children.Add(section);
    }

    /// <summary>
    /// Creates a model chip for display in the Available Models section
    /// </summary>
    private Border CreateModelChip(AvailableModel model)
    {
        var chipColor = GetModelVendorColor(model.Vendor);
        var chipBgColor = Windows.UI.Color.FromArgb(30, chipColor.R, chipColor.G, chipColor.B);
        
        var chip = new Border
        {
            Background = new SolidColorBrush(chipBgColor),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(50, chipColor.R, chipColor.G, chipColor.B)),
            BorderThickness = new Thickness(1)
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        // Model name
        var displayName = FormatModelDisplayName(model.Name ?? model.Id);
        var nameText = new TextBlock
        {
            Text = displayName,
            FontSize = 10,
            Foreground = new SolidColorBrush(PrimaryTextColor),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 100
        };
        content.Children.Add(nameText);

        // Preview badge if applicable
        if (model.IsPreview)
        {
            var previewBadge = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 255, 180, 0)),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(3, 1, 3, 1)
            };
            previewBadge.Child = new TextBlock
            {
                Text = "β",
                FontSize = 8,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 140, 0))
            };
            content.Children.Add(previewBadge);
        }

        chip.Child = content;

        // Tooltip with full info
        var tooltipText = $"{model.Name ?? model.Id}";
        if (!string.IsNullOrEmpty(model.Vendor))
            tooltipText += $"\nVendor: {model.Vendor}";
        if (!string.IsNullOrEmpty(model.Category))
            tooltipText += $"\nCategory: {model.Category}";
        if (model.IsPreview)
            tooltipText += "\n(Preview)";
        ToolTipService.SetToolTip(chip, tooltipText);

        return chip;
    }

    /// <summary>
    /// Get color for model vendor
    /// </summary>
    private Windows.UI.Color GetModelVendorColor(string? vendor)
    {
        return vendor?.ToLowerInvariant() switch
        {
            "anthropic" => Windows.UI.Color.FromArgb(255, 217, 119, 87),  // Claude orange
            "openai" => Windows.UI.Color.FromArgb(255, 16, 163, 127),     // OpenAI green
            "google" => Windows.UI.Color.FromArgb(255, 66, 133, 244),     // Google blue
            "xai" => Windows.UI.Color.FromArgb(255, 100, 100, 100),       // Gray
            _ => Windows.UI.Color.FromArgb(255, 124, 58, 237)             // Default purple
        };
    }

    /// <summary>
    /// Format model name for compact display
    /// </summary>
    private string FormatModelDisplayName(string name)
    {
        // Shorten common model names
        return name switch
        {
            var n when n.Contains("claude-sonnet-4", StringComparison.OrdinalIgnoreCase) => "Sonnet 4",
            var n when n.Contains("claude-3.5-sonnet", StringComparison.OrdinalIgnoreCase) => "Sonnet 3.5",
            var n when n.Contains("claude-3-opus", StringComparison.OrdinalIgnoreCase) => "Opus 3",
            var n when n.Contains("gpt-4o", StringComparison.OrdinalIgnoreCase) => "GPT-4o",
            var n when n.Contains("gpt-4", StringComparison.OrdinalIgnoreCase) && !n.Contains("4o") => "GPT-4",
            var n when n.Contains("o3-mini", StringComparison.OrdinalIgnoreCase) => "o3-mini",
            var n when n.Contains("o3", StringComparison.OrdinalIgnoreCase) => "o3",
            var n when n.Contains("o1-mini", StringComparison.OrdinalIgnoreCase) => "o1-mini",
            var n when n.Contains("o1", StringComparison.OrdinalIgnoreCase) => "o1",
            var n when n.Contains("gemini-2.0-flash", StringComparison.OrdinalIgnoreCase) => "Gemini 2 Flash",
            var n when n.Contains("gemini-2.5", StringComparison.OrdinalIgnoreCase) => "Gemini 2.5",
            var n when n.Contains("gemini", StringComparison.OrdinalIgnoreCase) => "Gemini",
            _ => name.Length > 16 ? name.Substring(0, 14) + "..." : name
        };
    }

    private async Task ShowCostDashboardPopupAsync()
    {
        try
        {
            DebugLogger.Log("TrayPopupWindow", "ShowCostDashboardPopupAsync called");

            // Close existing popup if any
            _costDashboardPopup?.Close();

            // Get current popup position to place cost dashboard next to it
            var popupPos = _appWindow.Position;
            var popupSize = _appWindow.Size;

            // Determine if we should show on left or right
            // Get screen work area
            var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;

            // Calculate available space on each side
            var spaceOnRight = workArea.Width - (popupPos.X + popupSize.Width);
            var spaceOnLeft = popupPos.X - workArea.X;

            const int dashboardWidth = 300;
            const int gap = 8;

            int dashboardX;
            if (spaceOnRight >= dashboardWidth + gap)
            {
                // Show on right
                dashboardX = popupPos.X + popupSize.Width + gap;
            }
            else if (spaceOnLeft >= dashboardWidth + gap)
            {
                // Show on left
                dashboardX = popupPos.X - dashboardWidth - gap;
            }
            else
            {
                // Not enough space, show on right anyway (will overlap taskbar)
                dashboardX = popupPos.X + popupSize.Width + gap;
            }

            // Align top with popup
            int dashboardY = popupPos.Y;

            // Create and show cost dashboard popup
            _costDashboardPopup = new CostDashboardPopup(
                _selectedProviderId,
                _isDarkMode,
                () => SettingsPageRequested?.Invoke("cost"));

            // Subscribe to closed event to clean up reference
            _costDashboardPopup.Closed += () =>
            {
                DebugLogger.Log("TrayPopupWindow", "CostDashboard closed");
                _costDashboardPopup = null;
            };

            _costDashboardPopup.PositionAt(dashboardX, dashboardY);
            DebugLogger.Log("TrayPopupWindow", $"CostDashboard positioned at ({dashboardX}, {dashboardY})");
            _costDashboardPopup.Show();
            DebugLogger.Log("TrayPopupWindow", "CostDashboard Show() called");

            // Load data
            await _costDashboardPopup.LoadDataAsync();
            DebugLogger.Log("TrayPopupWindow", "CostDashboard LoadDataAsync completed");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayPopupWindow", "ShowCostDashboardPopupAsync failed", ex);
        }
    }

    /// <summary>
    /// Close the cost dashboard popup (called when main popup hides)
    /// </summary>
    public void CloseCostDashboard()
    {
        _costDashboardPopup?.Close();
        _costDashboardPopup = null;
    }

    private async Task LoadCostDashboardAsync(StackPanel container)
    {
        try
        {
            // Determine provider based on selected tab
            var providerId = _selectedProviderId?.ToLowerInvariant();
            CostUsageProvider? costProvider = providerId switch
            {
                "codex" => CostUsageProvider.Codex,
                "claude" => CostUsageProvider.Claude,
                _ => null
            };

            if (costProvider == null)
            {
                // Provider doesn't support cost tracking
                container.Children.Clear();
                container.Children.Add(new TextBlock
                {
                    Text = "Cost tracking not available for this provider",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(SecondaryTextColor),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            // Load cost data
            var snapshot = await CostUsageFetcher.Instance.LoadTokenSnapshotAsync(costProvider.Value);

            // Clear loading indicator
            container.Children.Clear();

            // Header
            var header = new TextBlock
            {
                Text = $"{_selectedProviderId} Cost",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(PrimaryTextColor)
            };
            container.Children.Add(header);

            // Summary card
            var summaryCard = CreateCostSummaryCard(snapshot);
            container.Children.Add(summaryCard);

            // Daily chart (if we have data)
            if (snapshot.Daily.Count > 0)
            {
                var chartLabel = new TextBlock
                {
                    Text = "Last 14 Days",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(SecondaryTextColor),
                    Margin = new Thickness(0, 8, 0, 4)
                };
                container.Children.Add(chartLabel);

                var chart = CreateMiniDailyChart(snapshot.Daily.ToList());
                container.Children.Add(chart);
            }
            else
            {
                container.Children.Add(new TextBlock
                {
                    Text = "No usage data available",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TertiaryTextColor),
                    Margin = new Thickness(0, 8, 0, 0)
                });
            }

            // Footer link to full dashboard
            var footerLink = new HyperlinkButton
            {
                Content = "View full dashboard in Settings →",
                FontSize = 10,
                Foreground = new SolidColorBrush(DefaultAccentColor),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 8, 0, 0)
            };
            footerLink.Click += (s, e) =>
            {
                SettingsPageRequested?.Invoke("cost");
            };
            container.Children.Add(footerLink);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayPopupWindow", "LoadCostDashboardAsync failed", ex);
            container.Children.Clear();
            container.Children.Add(new TextBlock
            {
                Text = "Failed to load cost data",
                FontSize = 11,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 100, 100))
            });
        }
    }

    // Default accent color for cost dashboard (purple)
    private static Windows.UI.Color DefaultAccentColor => Windows.UI.Color.FromArgb(255, 124, 58, 237);

    private Border CreateCostSummaryCard(CostUsageTokenSnapshot snapshot)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(30, 255, 255, 255)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Today column
        var todayStack = new StackPanel { Spacing = 2 };
        todayStack.Children.Add(new TextBlock
        {
            Text = "Today",
            FontSize = 10,
            Foreground = new SolidColorBrush(TertiaryTextColor)
        });
        todayStack.Children.Add(new TextBlock
        {
            Text = FormatCost(snapshot.SessionCostUSD ?? 0),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 76, 175, 80)) // Green
        });
        todayStack.Children.Add(new TextBlock
        {
            Text = FormatTokenCount(snapshot.SessionTokens ?? 0),
            FontSize = 10,
            Foreground = new SolidColorBrush(SecondaryTextColor)
        });
        Grid.SetColumn(todayStack, 0);
        grid.Children.Add(todayStack);

        // 30-day column
        var monthStack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
        monthStack.Children.Add(new TextBlock
        {
            Text = "Last 30 days",
            FontSize = 10,
            Foreground = new SolidColorBrush(TertiaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        monthStack.Children.Add(new TextBlock
        {
            Text = FormatCost(snapshot.Last30DaysCostUSD ?? 0),
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(PrimaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        monthStack.Children.Add(new TextBlock
        {
            Text = FormatTokenCount(snapshot.Last30DaysTokens ?? 0),
            FontSize = 10,
            Foreground = new SolidColorBrush(SecondaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        Grid.SetColumn(monthStack, 1);
        grid.Children.Add(monthStack);

        card.Child = grid;
        return card;
    }

    private Grid CreateMiniDailyChart(List<CostUsageDailyEntry> daily)
    {
        var grid = new Grid { Height = 80 };

        // Get provider color
        var provider = ProviderRegistry.Instance.GetProvider(_selectedProviderId);
        var barColor = DefaultAccentColor;
        if (provider != null && !string.IsNullOrEmpty(provider.PrimaryColor))
        {
            try
            {
                var hex = provider.PrimaryColor.TrimStart('#');
                barColor = Windows.UI.Color.FromArgb(255,
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16));
            }
            catch { }
        }

        // Generate 14 days
        var today = DateTime.Today;
        var last14Days = Enumerable.Range(0, 14)
            .Select(i => today.AddDays(-13 + i))
            .ToList();

        // Create lookup
        var dataByDate = daily.ToDictionary(
            d => d.Date,
            d => d,
            StringComparer.OrdinalIgnoreCase);

        // Find max cost for scaling
        var maxCost = daily.Count > 0 ? daily.Max(d => d.CostUSD ?? 0) : 0;
        if (maxCost <= 0) maxCost = 1;

        var barsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };

        foreach (var date in last14Days)
        {
            var dateKey = CostUsageDayRange.DayKey(date);
            var hasData = dataByDate.TryGetValue(dateKey, out var entry);
            var cost = entry?.CostUSD ?? 0;
            var tokens = entry?.TotalTokens ?? 0;

            var heightRatio = maxCost > 0 ? cost / maxCost : 0;
            var barHeight = cost > 0 ? Math.Max(heightRatio * 50, 3) : 2;

            var barContainer = new StackPanel { Width = 16, Spacing = 2 };

            var bar = new Border
            {
                Width = 12,
                Height = barHeight,
                Background = new SolidColorBrush(cost > 0 ? barColor :
                    Windows.UI.Color.FromArgb(40, barColor.R, barColor.G, barColor.B)),
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 50 - barHeight, 0, 0)
            };

            // Day label (only show for some days to avoid clutter)
            var showLabel = date.Day == 1 || date == last14Days[0] || date == last14Days[^1];
            var dayLabel = new TextBlock
            {
                Text = showLabel ? date.Day.ToString() : "",
                FontSize = 7,
                Foreground = new SolidColorBrush(TertiaryTextColor),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            barContainer.Children.Add(bar);
            barContainer.Children.Add(dayLabel);

            // Tooltip
            var tooltipText = cost > 0
                ? $"{date:MMM d}\n{FormatCost(cost)}\n{FormatTokenCount(tokens)}"
                : $"{date:MMM d}\nNo usage";
            ToolTipService.SetToolTip(barContainer, tooltipText);

            barsPanel.Children.Add(barContainer);
        }

        // Legend
        var chartStack = new StackPanel { Spacing = 4 };
        chartStack.Children.Add(barsPanel);

        var legend = new TextBlock
        {
            Text = $"Peak: {FormatCost(maxCost)}",
            FontSize = 9,
            Foreground = new SolidColorBrush(TertiaryTextColor),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        chartStack.Children.Add(legend);

        grid.Children.Add(chartStack);
        return grid;
    }

    private static string FormatCost(double amount)
    {
        // Use locale-aware currency formatting
        return CurrencyFormatter.FormatSmart(amount);
    }

    private static string FormatTokenCount(int tokens)
    {
        if (tokens >= 1_000_000)
            return $"{tokens / 1_000_000.0:F1}M tokens";
        if (tokens >= 1_000)
            return $"{tokens / 1_000.0:F1}K tokens";
        return $"{tokens:N0} tokens";
    }


    private void AddCostRow(StackPanel parent, string label, string cost, string tokens)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(TertiaryTextColor)
        });

        var valuePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        valuePanel.Children.Add(new TextBlock
        {
            Text = cost,
            FontSize = 11,
            Foreground = new SolidColorBrush(SecondaryTextColor)
        });

        valuePanel.Children.Add(new TextBlock
        {
            Text = "·",
            FontSize = 11,
            Foreground = new SolidColorBrush(TertiaryTextColor)
        });

        valuePanel.Children.Add(new TextBlock
        {
            Text = tokens,
            FontSize = 11,
            Foreground = new SolidColorBrush(TertiaryTextColor)
        });

        row.Children.Add(valuePanel);
        parent.Children.Add(row);
    }

    private double CalculatePace(RateWindow window)
    {
        if (window.ResetsAt == null || window.WindowMinutes == null) return 0;

        var totalMinutes = window.WindowMinutes.Value;
        var remaining = (window.ResetsAt.Value - DateTime.UtcNow).TotalMinutes;
        var elapsed = totalMinutes - remaining;

        if (elapsed <= 0) return 0;

        var expectedPercent = (elapsed / totalMinutes) * 100;
        return expectedPercent - window.UsedPercent;
    }

    private Windows.UI.Color GetProgressColor(double percent)
    {
        if (percent >= 90)
            return Windows.UI.Color.FromArgb(255, 220, 53, 69);  // Red
        if (percent >= 70)
            return Windows.UI.Color.FromArgb(255, 255, 152, 0);  // Orange
        return Windows.UI.Color.FromArgb(255, 76, 175, 80);      // Green
    }

    #region Keyboard Navigation

    // Keyboard shortcut state
    private bool _showingShortcutsHelp;
    private Border? _shortcutsHelpPanel;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            // Escape - Close popup (if not pinned) or hide shortcuts help
            case VirtualKey.Escape:
                if (_showingShortcutsHelp)
                {
                    HideShortcutsHelp();
                }
                else if (!_isPinned)
                {
                    LightDismiss?.Invoke();
                }
                e.Handled = true;
                break;

            // R - Refresh
            case VirtualKey.R:
                _ = OnRefreshClick();
                e.Handled = true;
                break;

            // S - Open Settings
            case VirtualKey.S:
                OnSettingsClick();
                e.Handled = true;
                break;

            // D - Open Dashboard in browser
            case VirtualKey.D:
                OnUsageDashboardClick();
                e.Handled = true;
                break;

            // P - Toggle Pin
            case VirtualKey.P:
                OnPinClick();
                e.Handled = true;
                break;

            // ? or / - Show keyboard shortcuts help (using F1 as alternative since / key is complex)
            case VirtualKey.F1:
            case (VirtualKey)191: // Forward slash / question mark key on US keyboard
                ToggleShortcutsHelp();
                e.Handled = true;
                break;

            // 1-9 - Switch to provider N
            case VirtualKey.Number1:
            case VirtualKey.Number2:
            case VirtualKey.Number3:
            case VirtualKey.Number4:
            case VirtualKey.Number5:
            case VirtualKey.Number6:
            case VirtualKey.Number7:
            case VirtualKey.Number8:
            case VirtualKey.Number9:
                var index = (int)e.Key - (int)VirtualKey.Number1;
                SwitchToProviderByIndex(index);
                e.Handled = true;
                break;

            // Tab - Next provider (Shift+Tab = previous)
            case VirtualKey.Tab:
                var shiftPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                NavigateToNextProvider(forward: !shiftPressed);
                e.Handled = true;
                break;

            // Arrow keys - Navigate between providers
            case VirtualKey.Left:
                NavigateToNextProvider(forward: false);
                e.Handled = true;
                break;

            case VirtualKey.Right:
                NavigateToNextProvider(forward: true);
                e.Handled = true;
                break;
        }
    }

    private void SwitchToProviderByIndex(int index)
    {
        var enabledProviders = ProviderRegistry.Instance.GetAllProviders()
            .Where(p => SettingsService.Instance.Settings.IsProviderEnabled(p.Id))
            .ToList();

        if (enabledProviders.Count == 0)
        {
            enabledProviders = ProviderRegistry.Instance.GetAllProviders().ToList();
        }

        if (index >= 0 && index < enabledProviders.Count)
        {
            var provider = enabledProviders[index];
            OnProviderTabClick(provider.Id);
        }
    }

    private void NavigateToNextProvider(bool forward)
    {
        var enabledProviders = ProviderRegistry.Instance.GetAllProviders()
            .Where(p => SettingsService.Instance.Settings.IsProviderEnabled(p.Id))
            .ToList();

        if (enabledProviders.Count == 0)
        {
            enabledProviders = ProviderRegistry.Instance.GetAllProviders().ToList();
        }

        var currentIndex = enabledProviders.FindIndex(p => p.Id == _selectedProviderId);
        int nextIndex;

        if (forward)
        {
            nextIndex = (currentIndex + 1) % enabledProviders.Count;
        }
        else
        {
            nextIndex = currentIndex - 1;
            if (nextIndex < 0) nextIndex = enabledProviders.Count - 1;
        }

        if (nextIndex >= 0 && nextIndex < enabledProviders.Count)
        {
            OnProviderTabClick(enabledProviders[nextIndex].Id);
        }
    }

    private void ToggleShortcutsHelp()
    {
        if (_showingShortcutsHelp)
            HideShortcutsHelp();
        else
            ShowShortcutsHelp();
    }

    private void ShowShortcutsHelp()
    {
        if (_showingShortcutsHelp || _shortcutsHelpPanel != null) return;

        _showingShortcutsHelp = true;

        // Create overlay panel
        _shortcutsHelpPanel = new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(240, 30, 30, 35)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1)
        };

        var content = new StackPanel { Spacing = 8 };

        // Title
        content.Children.Add(new TextBlock
        {
            Text = "Keyboard Shortcuts",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Shortcuts
        AddShortcutHelpRow(content, "1-9", "Switch provider");
        AddShortcutHelpRow(content, "Tab", "Next provider");
        AddShortcutHelpRow(content, "R", "Refresh");
        AddShortcutHelpRow(content, "D", "Open dashboard");
        AddShortcutHelpRow(content, "S", "Settings");
        AddShortcutHelpRow(content, "P", "Pin/Unpin");
        AddShortcutHelpRow(content, "Esc", "Close");
        AddShortcutHelpRow(content, "?", "Toggle this help");

        // Global hotkey hint
        var globalHint = SettingsService.Instance.Settings.HotkeyDisplayString ?? "Win + Shift + Q";
        content.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255)),
            Margin = new Thickness(0, 8, 0, 8)
        });
        AddShortcutHelpRow(content, globalHint, "Toggle popup (global)");

        _shortcutsHelpPanel.Child = content;
        _rootGrid.Children.Add(_shortcutsHelpPanel);
    }

    private void AddShortcutHelpRow(StackPanel parent, string shortcut, string description)
    {
        var row = new ShortcutHintRow
        {
            Shortcut = shortcut,
            Description = description,
            IsDarkMode = true,
            Margin = new Thickness(0, 2, 0, 2)
        };
        parent.Children.Add(row);
    }

    private void HideShortcutsHelp()
    {
        if (_shortcutsHelpPanel != null)
        {
            _rootGrid.Children.Remove(_shortcutsHelpPanel);
            _shortcutsHelpPanel = null;
        }
        _showingShortcutsHelp = false;
    }

    #endregion

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => PointerEnteredPopup?.Invoke();
    
    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Ignore pointer exit during grace period (tab switch / resize)
        if (_suppressPointerExit) return;

        // Don't close when cost dashboard is open
        if (IsCostDashboardOpen) return;

        PointerExitedPopup?.Invoke();
    }

    private void OnPinClick()
    {
        _isPinned = !_isPinned;
        _pinIcon.Glyph = _isPinned ? "\uE840" : "\uE718";
        _pinIcon.Foreground = new SolidColorBrush(_isPinned
            ? Windows.UI.Color.FromArgb(255, 0, 120, 212)
            : SecondaryTextColor);
        PinToggled?.Invoke();

        // Fire pin state changed event for taskbar overlay
        var usagePercentage = GetCurrentUsagePercentage();
        var colorHex = GetProviderColorHex(_selectedProviderId);
        PinStateChanged?.Invoke(_isPinned, _selectedProviderId, usagePercentage, colorHex);
    }

    private async Task OnRefreshClick()
    {
        await _viewModel.RefreshCommand.ExecuteAsync(null);
        UpdateUI();
    }
}
