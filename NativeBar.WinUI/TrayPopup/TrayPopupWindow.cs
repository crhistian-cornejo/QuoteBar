using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NativeBar.WinUI.Controls;
using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Providers;
using NativeBar.WinUI.Core.Services;
using NativeBar.WinUI.ViewModels;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace NativeBar.WinUI.TrayPopup;

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
    private TextBlock _planTypeText = null!;
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

        DebugLogger.Log("TrayPopup", $"TrayPopupWindow created (CodexBar style, DarkMode={_isDarkMode})");
    }

    private void OnSettingsChanged()
    {
        // Rebuild UI on settings change (provider toggles, etc.)
        DispatcherQueue?.TryEnqueue(() =>
        {
            DebugLogger.Log("TrayPopup", "Settings changed - rebuilding UI");
            BuildUI();
            Content = _rootGrid;
            UpdateUI();
        });
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        // Update theme on UI thread
        DispatcherQueue?.TryEnqueue(() =>
        {
            _isDarkMode = theme == ElementTheme.Dark;
            // Rebuild UI with new theme colors
            BuildUI();
            Content = _rootGrid;
            UpdateUI();
        });
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
    /// Create a status indicator (InfoBadge-style) for provider tabs
    /// </summary>
    private FrameworkElement CreateStatusIndicator(Windows.UI.Color statusColor, UsageSnapshot? snapshot)
    {
        // Use InfoBadge when available, otherwise fall back to styled dot
        try
        {
            var badge = new InfoBadge
            {
                Width = 8,
                Height = 8,
                Margin = new Thickness(2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Determine badge style based on status
            if (snapshot == null || snapshot.ErrorMessage != null)
            {
                // Gray - not configured or error
                badge.Style = Application.Current.Resources["InformationalIconInfoBadgeStyle"] as Style;
            }
            else if (snapshot.IsLoading)
            {
                // Yellow-ish - loading
                badge.Style = Application.Current.Resources["CautionIconInfoBadgeStyle"] as Style;
            }
            else
            {
                // Green - connected
                badge.Style = Application.Current.Resources["SuccessIconInfoBadgeStyle"] as Style;
            }

            return badge;
        }
        catch
        {
            // Fallback to simple colored dot if InfoBadge styles not available
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
                    Width = 14,
                    Height = 14,
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
            FontSize = 14,
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
    /// </summary>
    private string? GetBaseSvgFileName(string providerId)
    {
        return providerId.ToLower() switch
        {
            "claude" => "claude.svg",
            "codex" => "openai.svg",
            "gemini" => "gemini.svg",
            "copilot" => "github-copilot.svg",
            "cursor" => "cursor.svg",
            "droid" => "droid.svg",
            "antigravity" => "antigravity.svg",
            "zai" => "zai.svg",
            "minimax" => "minimax-color.svg",
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
        
        // Create timer to re-enable pointer exit after 300ms
        _suppressPointerExitTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
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
    /// </summary>
    private void ResizePopupToFit()
    {
        try
        {
            var newHeight = CalculatePopupHeight();
            var currentSize = _appWindow.Size;
            
            // Only resize if height changed significantly (avoid flicker)
            if (Math.Abs(currentSize.Height - newHeight) > 20)
            {
                // Keep current X position, adjust Y to grow upward
                var currentPos = _appWindow.Position;
                var heightDiff = newHeight - currentSize.Height;
                var newY = currentPos.Y - heightDiff;
                
                _appWindow.MoveAndResize(new RectInt32(currentPos.X, newY, currentSize.Width, newHeight));
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayPopup", "ResizePopupToFit error", ex);
        }
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
        _logoContainer = new Border { Width = 36, Height = 36 };
        LoadProviderLogo();
        Grid.SetColumn(_logoContainer, 0);

        // Provider info
        var headerTextStack = new StackPanel
        {
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        _providerNameText = new TextBlock
        {
            Text = "Codex",
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(PrimaryTextColor)
        };

        _planTypeText = new TextBlock
        {
            Text = "Not configured",
            FontSize = 11,
            Foreground = new SolidColorBrush(SecondaryTextColor)
        };

        headerTextStack.Children.Add(_providerNameText);
        headerTextStack.Children.Add(_planTypeText);
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
                        Width = 32,
                        Height = 32,
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
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-NATIVE.png");
            if (System.IO.File.Exists(logoPath))
            {
                var logoImage = new Image
                {
                    Width = 36,
                    Height = 36,
                    Source = new BitmapImage(new Uri(logoPath))
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
            _ => null
        };
    }

    private string? GetStatusPageUrl(string providerId)
    {
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
        const int popupWidth = 340;
        const int margin = 12;

        // Calculate dynamic height based on number of provider rows
        int popupHeight = CalculatePopupHeight();

        int x, y;

        if (taskbarAtBottom)
        {
            x = iconX + (iconWidth / 2) - (popupWidth / 2);
            y = iconY - popupHeight - margin;
        }
        else
        {
            x = iconX + (iconWidth / 2) - (popupWidth / 2);
            y = iconY + iconHeight + margin;
        }

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        x = Math.Max(workArea.X + 8, Math.Min(x, workArea.X + workArea.Width - popupWidth - 8));
        y = Math.Max(workArea.Y + 8, Math.Min(y, workArea.Y + workArea.Height - popupHeight - 8));

        _appWindow.MoveAndResize(new RectInt32(x, y, popupWidth, popupHeight));
    }

    /// <summary>
    /// Calculate popup height based on number of enabled providers and actual usage sections
    /// Height adapts dynamically to the current provider's data
    /// </summary>
    private int CalculatePopupHeight()
    {
        // Minimal base height: tabs, header, footer (without usage sections)
        int baseHeight = IsCompactMode ? 380 : 460;
        int tabRowHeight = IsCompactMode ? 28 : 36;
        int usageSectionHeight = IsCompactMode ? 55 : 70;
        const int baseTabRows = 2;

        // Get enabled provider count for tab rows
        var allProviders = ProviderRegistry.Instance.GetAllProviders().ToList();
        var enabledCount = allProviders.Count(p => SettingsService.Instance.Settings.IsProviderEnabled(p.Id));
        
        if (enabledCount == 0)
            enabledCount = allProviders.Count;

        // Calculate extra tab rows
        int rowCount = (int)Math.Ceiling((double)enabledCount / MaxTabsPerRow);
        int extraTabRows = Math.Max(0, rowCount - baseTabRows);

        // Get actual usage section count from current provider's snapshot
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

        // Update cost display
        if (snapshot?.Cost != null && snapshot.Cost.TotalCostUSD > 0)
        {
            _costText.Text = $"${snapshot.Cost.TotalCostUSD:F2}";
            _costText.Visibility = Visibility.Visible;
        }
        else
        {
            _costText.Text = "";
            _costText.Visibility = Visibility.Collapsed;
        }

        // Update identity info
        _planTypeText.Text = snapshot?.Identity?.PlanType ?? "Not configured";

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

        // Progress bar - apply CompactMode sizing
        var progressHeight = IsCompactMode ? 5.0 : 6.0;
        var progressWidth = IsCompactMode ? 280.0 : 308.0;
        var progressRadius = progressHeight / 2;

        var progressTrack = new Border
        {
            Height = progressHeight,
            CornerRadius = new CornerRadius(progressRadius),
            Background = new SolidColorBrush(ProgressTrackColor)
        };

        var progressFill = new Border
        {
            Height = progressHeight,
            CornerRadius = new CornerRadius(progressRadius),
            Background = new SolidColorBrush(GetProgressColor(percent)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(0, Math.Min(progressWidth * percent / 100.0, progressWidth))
        };

        var progressContainer = new Grid();
        progressContainer.Children.Add(progressFill);
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

    private void AddCostSection(ProviderCost cost)
    {
        var section = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };

        // Header
        var headerRow = new Grid();
        var headerText = new TextBlock
        {
            Text = "Cost",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = new SolidColorBrush(PrimaryTextColor)
        };
        var arrowIcon = new FontIcon
        {
            Glyph = "\uE76C",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = new SolidColorBrush(SecondaryTextColor)
        };
        headerRow.Children.Add(headerText);
        headerRow.Children.Add(arrowIcon);
        section.Children.Add(headerRow);

        // Today's cost
        AddCostRow(section, "Today:", $"$ {cost.TotalCostUSD:F2}", "0 tokens");

        // Last 30 days (placeholder)
        AddCostRow(section, "Last 30 days:", $"$ {cost.TotalCostUSD:F2}", "0 tokens");

        _usageSectionsPanel.Children.Add(section);
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

            // Tab - Next provider
            case VirtualKey.Tab:
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
