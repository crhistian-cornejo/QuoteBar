using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NativeBar.WinUI.Core.Models;
using NativeBar.WinUI.Core.Providers;
using NativeBar.WinUI.Core.Services;
using NativeBar.WinUI.ViewModels;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
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

    // Provider tabs
    private Grid _tabsGrid = null!;
    private readonly Dictionary<string, Border> _tabButtons = new();
    private string _selectedProviderId = "codex";

    // UI Elements
    private Grid _rootGrid = null!;
    private Border _popupBorder = null!;
    private Image _logoImage = null!;
    private TextBlock _providerNameText = null!;
    private TextBlock _planTypeText = null!;
    private FontIcon _pinIcon = null!;

    // Usage sections
    private StackPanel _usageSectionsPanel = null!;
    private TextBlock _lastUpdatedText = null!;

    // Footer links
    private StackPanel _footerLinksPanel = null!;

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
        Title = "NativeBar";

        _usageStore = serviceProvider.GetRequiredService<UsageStore>();
        _viewModel = new TrayPopupViewModel(_usageStore);

        // Use ThemeService for dark mode detection
        _isDarkMode = ThemeService.Instance.IsDarkMode;

        // Listen for theme changes
        ThemeService.Instance.ThemeChanged += OnThemeChanged;

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

        System.IO.File.AppendAllText("D:\\NativeBar\\debug.log",
            $"[{DateTime.Now}] TrayPopupWindow created (CodexBar style, DarkMode={_isDarkMode})\n");
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

    private Windows.UI.Color SecondaryTextColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 180, 180, 180)
        : Windows.UI.Color.FromArgb(255, 100, 100, 100);

    private Windows.UI.Color TertiaryTextColor => _isDarkMode
        ? Windows.UI.Color.FromArgb(255, 140, 140, 140)
        : Windows.UI.Color.FromArgb(255, 140, 140, 140);

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

        // === Scrollable content area ===
        var contentPanel = new StackPanel
        {
            Padding = new Thickness(16, 12, 16, 16),
            Spacing = 12
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
        
        // Use a consistent selection color (similar to hover, but slightly stronger)
        var selectedBgColor = _isDarkMode
            ? Windows.UI.Color.FromArgb(60, 255, 255, 255)
            : Windows.UI.Color.FromArgb(50, 0, 0, 0);

        var tab = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(isSelected ? selectedBgColor : Colors.Transparent),
            Tag = provider.Id
        };

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };

        // Provider icon - try SVG first, then fallback to FontIcon
        FrameworkElement iconElement = CreateProviderIcon(provider, isSelected);

        // Provider name
        var name = new TextBlock
        {
            Text = provider.DisplayName,
            FontSize = 12,
            FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = new SolidColorBrush(isSelected ? PrimaryTextColor : SecondaryTextColor),
            VerticalAlignment = VerticalAlignment.Center
        };

        // Status indicator dot (uses provider color)
        var statusDot = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(GetProviderStatusColor(provider.Id)),
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        content.Children.Add(iconElement);
        content.Children.Add(name);
        content.Children.Add(statusDot);
        tab.Child = content;

        // Hover color (slightly lighter than selected)
        var hoverBgColor = _isDarkMode
            ? Windows.UI.Color.FromArgb(40, 255, 255, 255)
            : Windows.UI.Color.FromArgb(30, 0, 0, 0);
        
        // Pressed color (same as selected)
        var pressedBgColor = selectedBgColor;

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

        return tab;
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

    private FrameworkElement CreateProviderIcon(IProviderDescriptor provider, bool isSelected)
    {
        // Map provider ID to SVG file name - use white version for dark icons in dark mode
        var svgFileName = GetProviderSvgFileName(provider.Id);

        if (!string.IsNullOrEmpty(svgFileName))
        {
            var svgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", svgFileName);
            if (System.IO.File.Exists(svgPath))
            {
                return new Image
                {
                    Width = 14,
                    Height = 14,
                    Source = new SvgImageSource(new Uri(svgPath)),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
        }

        // Fallback to FontIcon
        return new FontIcon
        {
            Glyph = provider.IconGlyph,
            FontSize = 14,
            Foreground = new SolidColorBrush(isSelected ? Colors.White : SecondaryTextColor)
        };
    }

    private void OnProviderTabClick(string providerId)
    {
        _selectedProviderId = providerId;
        _usageStore.CurrentProviderId = providerId;
        UpdateTabStyles();
        UpdateUI();
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

        // Logo
        _logoImage = new Image { Width = 36, Height = 36 };
        LoadProviderLogo();
        Grid.SetColumn(_logoImage, 0);

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

        // Pin button
        var pinButton = CreateIconButton("\uE718", out _pinIcon);
        pinButton.PointerPressed += (s, e) => OnPinClick();
        Grid.SetColumn(pinButton, 2);

        headerGrid.Children.Add(_logoImage);
        headerGrid.Children.Add(headerTextStack);
        headerGrid.Children.Add(pinButton);
        parent.Children.Add(headerGrid);
    }

    private void LoadProviderLogo()
    {
        try
        {
            // Get SVG file name for the selected provider
            var svgFileName = GetProviderSvgFileName(_selectedProviderId);

            if (!string.IsNullOrEmpty(svgFileName))
            {
                var svgPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", svgFileName);
                if (System.IO.File.Exists(svgPath))
                {
                    _logoImage.Source = new SvgImageSource(new Uri(svgPath));
                    return;
                }
            }

            // Fallback to app logo if no provider SVG
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-NATIVE.png");
            if (System.IO.File.Exists(logoPath))
            {
                _logoImage.Source = new BitmapImage(new Uri(logoPath));
            }
        }
        catch { }
    }

    private string? GetProviderSvgFileName(string providerId)
    {
        // Use white versions for dark icons in dark mode
        // Icons with dark fills need white versions in dark mode
        return providerId.ToLower() switch
        {
            "claude" => "claude.svg",  // Orange icon - visible in both modes
            "codex" => _isDarkMode ? "openai-white.svg" : "openai.svg",
            "gemini" => "gemini.svg",  // Blue icon - visible in both modes
            "copilot" => _isDarkMode ? "github-copilot-white.svg" : "github-copilot.svg",
            "cursor" => _isDarkMode ? "cursor-white.svg" : "cursor.svg",
            "droid" => _isDarkMode ? "droid-white.svg" : "droid.svg",
            "antigravity" => "antigravity.svg",  // Red icon - visible in both modes
            "zai" => _isDarkMode ? "zai-white.svg" : "zai.svg",  // Black/white based on theme
            _ => null
        };
    }

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
        AddFooterLink("\uE9D9", "Usage Dashboard", null, OnUsageDashboardClick);
        AddFooterLink("\uE946", "Status Page", null, OnStatusPageClick);

        parent.Children.Add(_footerLinksPanel);

        // Settings row
        var settingsRow = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        AddFooterLink("\uE713", "Settings...", settingsRow, OnSettingsClick);
        AddFooterLink("\uE946", "About NativeBar", settingsRow, OnAboutClick);
        AddFooterLink("\uE7E8", "Quit", settingsRow, OnQuitClick);
        parent.Children.Add(settingsRow);
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
            "antigravity" => "https://antigravity.ai/dashboard",
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
            "antigravity" => "https://status.antigravity.ai/",
            _ => null
        };
    }
    
    private void OnSettingsClick() => SettingsRequested?.Invoke();
    private void OnAboutClick() => SettingsPageRequested?.Invoke("About");
    private void OnQuitClick() => QuitRequested?.Invoke();

    private void AddFooterLink(string glyph, string text, StackPanel? container = null, Action? onClick = null)
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
        const int popupHeight = 580;
        const int margin = 12;

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

        // Update identity info
        _planTypeText.Text = snapshot?.Identity?.PlanType ?? "Not configured";

        // Clear and rebuild usage sections
        _usageSectionsPanel.Children.Clear();
        BuildUsageSections(snapshot, provider);

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
                provider.PrimaryLabel,
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
                provider.SecondaryLabel,
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
                provider.TertiaryLabel ?? "Extra",
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
        var section = new StackPanel { Spacing = 6 };

        // Label row
        var labelRow = new Grid();

        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            Foreground = new SolidColorBrush(PrimaryTextColor)
        };

        var valueTextBlock = new TextBlock
        {
            Text = valueText,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = new SolidColorBrush(PrimaryTextColor)
        };

        labelRow.Children.Add(labelText);
        labelRow.Children.Add(valueTextBlock);
        section.Children.Add(labelRow);

        // Progress bar
        var progressTrack = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(ProgressTrackColor)
        };

        var progressFill = new Border
        {
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(GetProgressColor(percent)),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(0, Math.Min(308 * percent / 100.0, 308))
        };

        var progressContainer = new Grid();
        progressContainer.Children.Add(progressFill);
        progressTrack.Child = progressContainer;
        section.Children.Add(progressTrack);

        // Reset time and pace
        if (!string.IsNullOrEmpty(resetText) || showPace)
        {
            var infoRow = new Grid();

            if (showPace && window != null)
            {
                // Calculate pace
                var pacePercent = CalculatePace(window);
                var paceText = new TextBlock
                {
                    Text = $"Pace: {(pacePercent >= 0 ? "Ahead" : "Behind")} ({pacePercent:+0;-0}%) · Lasts to reset",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TertiaryTextColor)
                };
                infoRow.Children.Add(paceText);
            }

            if (!string.IsNullOrEmpty(resetText))
            {
                var resetTextBlock = new TextBlock
                {
                    Text = resetText,
                    FontSize = 11,
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

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => PointerEnteredPopup?.Invoke();
    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => PointerExitedPopup?.Invoke();

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
