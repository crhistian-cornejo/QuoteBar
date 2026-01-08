using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using NativeBar.WinUI.Core.Services;
using NativeBar.WinUI.Settings;
using NativeBar.WinUI.Settings.Pages;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace NativeBar.WinUI;

/// <summary>
/// Settings window with native Windows 11 styling, integrated sidebar, and smooth transitions
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly ThemeService _theme = ThemeService.Instance;
    private Grid _rootGrid = null!;
    private Border _sidebarBorder = null!;
    private StackPanel _menuPanel = null!;
    private Frame _contentFrame = null!;
    private string _currentPage = "General";
    private readonly string? _initialPage;

    // Custom titlebar element (drag region)
    private Grid _titleBarGrid = null!;

    // Page instances (lazy-loaded)
    private GeneralSettingsPage? _generalPage;
    private ProvidersSettingsPage? _providersPage;
    private ProviderOrderSettingsPage? _providerOrderPage;
    private AppearanceSettingsPage? _appearancePage;
    private NotificationsSettingsPage? _notificationsPage;
    private CostTrackingSettingsPage? _costTrackingPage;
    private AboutSettingsPage? _aboutPage;

    public SettingsWindow(string? initialPage = null)
    {
        try
        {
            DebugLogger.Log("SettingsWindow", "Constructor start");

            _initialPage = initialPage;
            Title = "QuoteBar Settings";

            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            BuildUI();
            ConfigureWindow();

            try
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            }
            catch { }

            _theme.ThemeChanged += OnThemeChanged;

            DebugLogger.Log("SettingsWindow", "Constructor complete");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("SettingsWindow", "Constructor error", ex);
            throw;
        }
    }

    public void NavigateToPage(string pageName)
    {
        SelectMenuItem(pageName);
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        try
        {
            if (_rootGrid != null) _rootGrid.RequestedTheme = theme;
            if (_sidebarBorder != null)
            {
                _sidebarBorder.RequestedTheme = theme;
                _sidebarBorder.Background = new SolidColorBrush(_theme.SurfaceColor);
            }
            if (_contentFrame != null) _contentFrame.RequestedTheme = theme;

            // Update titlebar button colors for theme
            UpdateTitleBarColors();

            _generalPage?.OnThemeChanged();
            _providersPage?.OnThemeChanged();
            _providerOrderPage?.OnThemeChanged();
            _costTrackingPage?.OnThemeChanged();
            _appearancePage?.OnThemeChanged();
            _notificationsPage?.OnThemeChanged();
            _aboutPage?.OnThemeChanged();

            ShowPage(_currentPage, useTransition: false);
            UpdateMenuColors();
        }
        catch { }
    }

    private void UpdateTitleBarColors()
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        var titleBar = _appWindow.TitleBar;
        if (_theme.IsDarkMode)
        {
            titleBar.ButtonForegroundColor = Colors.White;
            titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(128, 255, 255, 255);
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(25, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 255, 255, 255);
            titleBar.ButtonPressedForegroundColor = Colors.White;
        }
        else
        {
            titleBar.ButtonForegroundColor = Colors.Black;
            titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(128, 0, 0, 0);
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(25, 0, 0, 0);
            titleBar.ButtonHoverForegroundColor = Colors.Black;
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 0, 0, 0);
            titleBar.ButtonPressedForegroundColor = Colors.Black;
        }
    }

    private void UpdateMenuColors()
    {
        foreach (var child in _menuPanel.Children)
        {
            if (child is Border border && border.Tag != null)
            {
                bool isSelected = border.Tag.ToString() == _currentPage;
                UpdateMenuItemVisual(border, isSelected);
            }
        }
    }

    private void UpdateMenuItemVisual(Border border, bool isSelected)
    {
        border.Background = new SolidColorBrush(isSelected ? _theme.SelectedColor : Colors.Transparent);

        if (border.Child is Grid grid)
        {
            // Update indicator bar
            if (grid.Children[0] is Border indicator)
            {
                indicator.Background = new SolidColorBrush(isSelected ? _theme.AccentColor : Colors.Transparent);
            }
            // Update icon and text
            if (grid.Children[1] is StackPanel stack)
            {
                if (stack.Children[0] is FontIcon icon)
                {
                    icon.Foreground = new SolidColorBrush(isSelected ? _theme.AccentColor : _theme.TextColor);
                }
                if (stack.Children[1] is TextBlock text)
                {
                    text.Foreground = new SolidColorBrush(_theme.TextColor);
                    text.FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                }
            }
        }
    }

    private void BuildUI()
    {
        _rootGrid = new Grid { RequestedTheme = _theme.CurrentTheme };

        // Extra defense for missing resources
        try
        {
            if (!_rootGrid.Resources.ContainsKey("AcrylicBackgroundFillColorDefaultBrush"))
            {
                _rootGrid.Resources["AcrylicBackgroundFillColorDefaultBrush"] = new SolidColorBrush(Colors.Transparent);
            }
        }
        catch { }

        // Two rows: Titlebar (32px) + Content (*)
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // === TITLEBAR ROW (spans full width) ===
        _titleBarGrid = new Grid { Height = 32 };
        _titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) }); // Sidebar width
        _titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Sidebar titlebar area with icon and title
        var sidebarTitleArea = new Grid
        {
            Background = new SolidColorBrush(_theme.SurfaceColor)
        };
        sidebarTitleArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sidebarTitleArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FrameworkElement iconElement;
        try
        {
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-32.png");
            if (!System.IO.File.Exists(logoPath))
                logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-24.png");

            if (System.IO.File.Exists(logoPath))
            {
                iconElement = new Image
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(13, 0, 0, 0),
                    Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute)),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                iconElement = new FontIcon
                {
                    Glyph = "\uE9D9",
                    FontSize = 14,
                    Margin = new Thickness(13, 0, 0, 0),
                    Foreground = new SolidColorBrush(_theme.AccentColor),
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
                Margin = new Thickness(13, 0, 0, 0),
                Foreground = new SolidColorBrush(_theme.AccentColor),
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        Grid.SetColumn(iconElement, 0);

        var titleText = new TextBlock
        {
            Text = "Settings",
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = new SolidColorBrush(_theme.TextColor)
        };
        Grid.SetColumn(titleText, 1);

        sidebarTitleArea.Children.Add(iconElement);
        sidebarTitleArea.Children.Add(titleText);
        Grid.SetColumn(sidebarTitleArea, 0);

        // Content titlebar area (transparent for window buttons)
        var contentTitleArea = new Border { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetColumn(contentTitleArea, 1);

        _titleBarGrid.Children.Add(sidebarTitleArea);
        _titleBarGrid.Children.Add(contentTitleArea);
        Grid.SetRow(_titleBarGrid, 0);

        // === CONTENT ROW (Sidebar + Content) ===
        var contentRow = new Grid();
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left sidebar (menu items only)
        _sidebarBorder = new Border
        {
            Background = new SolidColorBrush(_theme.SurfaceColor),
            RequestedTheme = _theme.CurrentTheme
        };

        _menuPanel = new StackPanel
        {
            Spacing = 2,
            Margin = new Thickness(12, 8, 12, 12)
        };

        _menuPanel.Children.Add(CreateMenuItem("General", "\uE713", true));
        _menuPanel.Children.Add(CreateMenuItem("Providers", "\uE774", false));
        _menuPanel.Children.Add(CreateMenuItem("Provider Order", "\uE8A6", false));
        _menuPanel.Children.Add(CreateMenuItem("Cost Tracking", "\uE9D9", false));
        _menuPanel.Children.Add(CreateMenuItem("Appearance", "\uE790", false));
        _menuPanel.Children.Add(CreateMenuItem("Notifications", "\uEA8F", false));
        _menuPanel.Children.Add(CreateMenuItem("About", "\uE946", false));

        _sidebarBorder.Child = _menuPanel;
        Grid.SetColumn(_sidebarBorder, 0);

        // Right content area
        var contentBorder = new Border
        {
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1, 0, 0, 0)
        };

        _contentFrame = new Frame
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RequestedTheme = _theme.CurrentTheme
        };

        contentBorder.Child = _contentFrame;
        Grid.SetColumn(contentBorder, 1);

        contentRow.Children.Add(_sidebarBorder);
        contentRow.Children.Add(contentBorder);
        Grid.SetRow(contentRow, 1);

        // Show initial page
        var startPage = _initialPage ?? "General";
        _currentPage = startPage;
        ShowPage(startPage, useTransition: false);
        UpdateMenuSelection(startPage);

        _rootGrid.Children.Add(_titleBarGrid);
        _rootGrid.Children.Add(contentRow);

        Content = _rootGrid;
    }

    private Border CreateMenuItem(string name, string glyph, bool isSelected)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(isSelected ? _theme.SelectedColor : Colors.Transparent),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
            Tag = name,
            Height = 40,
            // Enable keyboard navigation
            IsTabStop = true,
            FocusVisualMargin = new Thickness(-2),
            FocusVisualPrimaryThickness = new Thickness(2)
        };

        // Set accessibility properties
        AutomationProperties.SetName(border, $"{name} settings");
        AutomationProperties.SetAutomationId(border, $"SettingsNav_{name}");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) }); // Indicator
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Content

        // Selection indicator bar
        var indicator = new Border
        {
            Width = 3,
            Height = 16,
            CornerRadius = new CornerRadius(1.5),
            Background = new SolidColorBrush(isSelected ? _theme.AccentColor : Colors.Transparent),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(indicator, 0);

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
            Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Foreground = new SolidColorBrush(isSelected ? _theme.AccentColor : _theme.TextColor)
        };

        var text = new TextBlock
        {
            Text = name,
            FontSize = 14,
            FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = new SolidColorBrush(_theme.TextColor),
            VerticalAlignment = VerticalAlignment.Center
        };

        stack.Children.Add(icon);
        stack.Children.Add(text);
        Grid.SetColumn(stack, 1);

        grid.Children.Add(indicator);
        grid.Children.Add(stack);
        border.Child = grid;

        border.PointerPressed += (s, e) => SelectMenuItem(name);

        // Keyboard support
        border.KeyDown += (s, e) =>
        {
            if (e.Key == VirtualKey.Enter || e.Key == VirtualKey.Space)
            {
                SelectMenuItem(name);
                e.Handled = true;
            }
        };

        // Focus state
        border.GotFocus += (s, e) =>
        {
            if (border.Tag?.ToString() != _currentPage)
            {
                border.Background = new SolidColorBrush(_theme.HoverColor);
            }
        };
        border.LostFocus += (s, e) =>
        {
            if (border.Tag?.ToString() != _currentPage)
            {
                border.Background = new SolidColorBrush(Colors.Transparent);
            }
        };

        border.PointerEntered += (s, e) =>
        {
            if (border.Tag?.ToString() != _currentPage)
            {
                border.Background = new SolidColorBrush(_theme.HoverColor);
            }
        };
        border.PointerExited += (s, e) =>
        {
            if (border.Tag?.ToString() != _currentPage)
            {
                border.Background = new SolidColorBrush(Colors.Transparent);
            }
        };

        return border;
    }

    private void SelectMenuItem(string pageName)
    {
        if (_currentPage == pageName) return;
        
        _currentPage = pageName;
        UpdateMenuSelection(pageName);
        ShowPage(pageName, useTransition: true);
    }

    private void UpdateMenuSelection(string pageName)
    {
        foreach (var child in _menuPanel.Children)
        {
            if (child is Border border && border.Tag != null)
            {
                bool isSelected = border.Tag.ToString() == pageName;
                UpdateMenuItemVisual(border, isSelected);
            }
        }
    }

    private void ShowPage(string pageName, bool useTransition)
    {
        try
        {
            DebugLogger.Log("SettingsWindow", $"ShowPage: {pageName}");

            FrameworkElement pageContent;
            try
            {
                ISettingsPage page = pageName switch
                {
                    "General" => _generalPage ??= new GeneralSettingsPage(),
                    "Providers" => GetProvidersPage(),
                    "Provider Order" => _providerOrderPage ??= new ProviderOrderSettingsPage(),
                    "Cost Tracking" => _costTrackingPage ??= new CostTrackingSettingsPage(),
                    "Appearance" => GetAppearancePage(),
                    "Notifications" => _notificationsPage ??= new NotificationsSettingsPage(),
                    "About" => _aboutPage ??= new AboutSettingsPage(),
                    _ => _generalPage ??= new GeneralSettingsPage()
                };

                pageContent = page.Content;
            }
            catch (Exception pageEx)
            {
                DebugLogger.LogError("SettingsWindow", $"Page creation failed for {pageName}", pageEx);
                pageContent = new TextBlock
                {
                    Text = $"Error loading {pageName}: {pageEx.Message}",
                    Foreground = new SolidColorBrush(Colors.Red),
                    Margin = new Thickness(24),
                    TextWrapping = TextWrapping.Wrap
                };
            }

            if (pageContent != null)
            {
                pageContent.RequestedTheme = _theme.CurrentTheme;

                if (useTransition)
                {
                    // Apply entrance animation
                    var transition = new EntranceThemeTransition
                    {
                        FromHorizontalOffset = 40,
                        FromVerticalOffset = 0
                    };
                    
                    if (pageContent is Panel panel)
                    {
                        panel.ChildrenTransitions = [transition];
                    }
                }

                _contentFrame.Content = pageContent;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("SettingsWindow", $"ShowPage ERROR for {pageName}", ex);
        }
    }

    private ProvidersSettingsPage GetProvidersPage()
    {
        if (_providersPage == null)
        {
            _providersPage = new ProvidersSettingsPage();
            _providersPage.SetDispatcherQueue(DispatcherQueue);
            _providersPage.RequestRefresh += () =>
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    if (_currentPage == "Providers")
                    {
                        _providersPage.Refresh();
                        _contentFrame.Content = _providersPage.Content;
                    }
                });
            };
        }
        return _providersPage;
    }

    private AppearanceSettingsPage GetAppearancePage()
    {
        if (_appearancePage == null)
        {
            _appearancePage = new AppearanceSettingsPage();
            _appearancePage.ThemeChanged += (theme) =>
            {
                if (_rootGrid != null) _rootGrid.RequestedTheme = theme;
            };
        }
        return _appearancePage;
    }

    private void ConfigureWindow()
    {
        _appWindow.Resize(new SizeInt32(920, 640));

        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        var x = (workArea.Width - 920) / 2 + workArea.X;
        var y = (workArea.Height - 640) / 2 + workArea.Y;
        _appWindow.Move(new PointInt32(x, y));

        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
                _appWindow.SetIcon(iconPath);
        }
        catch { }

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = _appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Standard; // 32px

            // Transparent buttons that blend with content
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            // Theme-aware button colors
            if (_theme.IsDarkMode)
            {
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(128, 255, 255, 255);
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(25, 255, 255, 255);
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 255, 255, 255);
                titleBar.ButtonPressedForegroundColor = Colors.White;
            }
            else
            {
                titleBar.ButtonForegroundColor = Colors.Black;
                titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(128, 0, 0, 0);
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(25, 0, 0, 0);
                titleBar.ButtonHoverForegroundColor = Colors.Black;
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 0, 0, 0);
                titleBar.ButtonPressedForegroundColor = Colors.Black;
            }

            // Set the drag region to be the entire titlebar area
            SetTitleBar(_titleBarGrid);
        }
    }
}
