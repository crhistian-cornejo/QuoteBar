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

    // Page instances (lazy-loaded)
    private GeneralSettingsPage? _generalPage;
    private ProvidersSettingsPage? _providersPage;
    private AppearanceSettingsPage? _appearancePage;
    private NotificationsSettingsPage? _notificationsPage;
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

            _generalPage?.OnThemeChanged();
            _providersPage?.OnThemeChanged();
            _appearancePage?.OnThemeChanged();
            _notificationsPage?.OnThemeChanged();
            _aboutPage?.OnThemeChanged();

            ShowPage(_currentPage, useTransition: false);
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

        // NOTE: Adding XamlControlsResources can crash in unpackaged WinUI 3 apps
        // (missing AcrylicBackgroundFillColorDefaultBrush). We avoid it to keep
        // Settings stable.
        // App.xaml.cs already documents this for the app root.
        //
        // If we later move to packaged/MSIX, we can reconsider enabling it here.
        //
        // _rootGrid.Resources.MergedDictionaries.Add(new XamlControlsResources());

        // Extra defense: some WinUI components may try to resolve Acrylic brushes
        // even when we don't explicitly use XamlControlsResources.
        // Provide a safe fallback so missing resources don't crash the Settings window.
        try
        {
            if (!_rootGrid.Resources.ContainsKey("AcrylicBackgroundFillColorDefaultBrush"))
            {
                _rootGrid.Resources["AcrylicBackgroundFillColorDefaultBrush"] = new SolidColorBrush(Colors.Transparent);
            }
        }
        catch { }
 
        // Two columns: Sidebar (240px) + Content (*)
        _rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        _rootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // === LEFT SIDEBAR (includes titlebar area) ===
        _sidebarBorder = new Border
        {
            Background = new SolidColorBrush(_theme.SurfaceColor),
            RequestedTheme = _theme.CurrentTheme
        };

        var sidebarStack = new StackPanel();

        // Titlebar area in sidebar (48px height for custom titlebar)
        var titleBarArea = CreateSidebarTitleBar();
        sidebarStack.Children.Add(titleBarArea);

        // Menu items with padding
        _menuPanel = new StackPanel 
        { 
            Spacing = 2,
            Margin = new Thickness(12, 8, 12, 12)
        };

        _menuPanel.Children.Add(CreateMenuItem("General", "\uE713", true));
        _menuPanel.Children.Add(CreateMenuItem("Providers", "\uE774", false));
        _menuPanel.Children.Add(CreateMenuItem("Appearance", "\uE790", false));
        _menuPanel.Children.Add(CreateMenuItem("Notifications", "\uEA8F", false));
        _menuPanel.Children.Add(CreateMenuItem("About", "\uE946", false));

        sidebarStack.Children.Add(_menuPanel);
        _sidebarBorder.Child = sidebarStack;
        Grid.SetColumn(_sidebarBorder, 0);

        // === RIGHT CONTENT AREA ===
        var contentBorder = new Border
        {
            // Subtle separator line on left
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1, 0, 0, 0)
        };

        // Use Frame for built-in navigation transitions
        _contentFrame = new Frame
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            RequestedTheme = _theme.CurrentTheme
        };

        // Wrap content in a ContentControl for manual content switching with animation
        var contentControl = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            RequestedTheme = _theme.CurrentTheme
        };
        
        contentBorder.Child = _contentFrame;
        Grid.SetColumn(contentBorder, 1);

        // Show initial page
        var startPage = _initialPage ?? "General";
        _currentPage = startPage;
        ShowPage(startPage, useTransition: false);
        UpdateMenuSelection(startPage);

        _rootGrid.Children.Add(_sidebarBorder);
        _rootGrid.Children.Add(contentBorder);

        Content = _rootGrid;
    }

    private Grid CreateSidebarTitleBar()
    {
        var titleBarGrid = new Grid
        {
            Height = 48,
            Margin = new Thickness(16, 0, 0, 8)
        };

        titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // App icon
        FrameworkElement iconElement;
        try
        {
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-NATIVE.png");
            if (System.IO.File.Exists(logoPath))
            {
                iconElement = new Image
                {
                    Width = 18,
                    Height = 18,
                    Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute)),
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            else
            {
                iconElement = new FontIcon
                {
                    Glyph = "\uE9D9",
                    FontSize = 16,
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
                FontSize = 16,
                Foreground = new SolidColorBrush(_theme.AccentColor),
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        Grid.SetColumn(iconElement, 0);

        var titleText = new TextBlock
        {
            Text = "Settings",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = new SolidColorBrush(_theme.TextColor)
        };
        Grid.SetColumn(titleText, 1);

        titleBarGrid.Children.Add(iconElement);
        titleBarGrid.Children.Add(titleText);

        return titleBarGrid;
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
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-NATIVE.ico");
            if (!System.IO.File.Exists(iconPath))
                iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-NATIVE.png");
            if (System.IO.File.Exists(iconPath))
                _appWindow.SetIcon(iconPath);
        }
        catch { }

        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = _appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
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
