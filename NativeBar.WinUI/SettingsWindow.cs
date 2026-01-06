using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using NativeBar.WinUI.Core.Providers.Zai;
using NativeBar.WinUI.Core.Services;
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

    public SettingsWindow(string? initialPage = null)
    {
        try
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log", $"[{DateTime.Now}] SettingsWindow constructor start\n");

            _initialPage = initialPage;
            Title = "NativeBar Settings";

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

            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log", $"[{DateTime.Now}] SettingsWindow constructor complete\n");
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText("D:\\NativeBar\\debug.log", $"[{DateTime.Now}] SettingsWindow ERROR: {ex.Message}\n{ex.StackTrace}\n");
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
            Text = "NativeBar Settings",
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

        // Start at login
        _startupToggle = CreateToggleSwitch(_settings.Settings.StartAtLogin);
        _startupToggle.Toggled += (s, e) =>
        {
            _settings.Settings.StartAtLogin = _startupToggle.IsOn;
            _settings.Save();
        };
        stack.Children.Add(CreateSettingCard(
            "Start at login",
            "Automatically start NativeBar when you log in to Windows",
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

        // Hover delay
        _hoverSlider = new Slider
        {
            Minimum = 100,
            Maximum = 1000,
            Value = _settings.Settings.HoverDelayMs,
            Width = 150,
            StepFrequency = 50
        };
        _hoverSlider.ValueChanged += (s, e) =>
        {
            _settings.Settings.HoverDelayMs = (int)_hoverSlider.Value;
            _settings.Save();
        };
        stack.Children.Add(CreateSettingCard(
            "Hover delay",
            "Delay before showing popup on hover (ms)",
            _hoverSlider));

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

        // Provider cards - using SVG icons where available
        stack.Children.Add(CreateProviderCard("Codex", "codex", "#7C3AED", true, "CLI detected"));
        stack.Children.Add(CreateProviderCard("Claude", "claude", "#D97757", true, "CLI detected"));
        stack.Children.Add(CreateProviderCard("Cursor", "cursor", "#007AFF", false, "Not configured"));
        stack.Children.Add(CreateProviderCard("Gemini", "gemini", "#4285F4", false, "Not configured"));
        stack.Children.Add(CreateProviderCard("Copilot", "copilot", "#24292F", false, "Not configured"));
        stack.Children.Add(CreateProviderCard("Droid", "droid", "#EE6018", false, "Not configured"));
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

        // Configure button
        var configButton = new Button
        {
            Content = isConnected ? "Configure" : "Connect",
            Padding = new Thickness(16, 8, 16, 8)
        };

        if (!isConnected)
        {
            configButton.Background = new SolidColorBrush(_theme.AccentColor);
            configButton.Foreground = new SolidColorBrush(Colors.White);
        }

        configButton.Click += async (s, e) =>
        {
            // Show configuration dialog
            var dialog = new ContentDialog
            {
                Title = $"Configure {name}",
                Content = $"Provider configuration for {name} will be available in a future update.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        };

        Grid.SetColumn(configButton, 2);

        grid.Children.Add(iconElement);
        grid.Children.Add(infoStack);
        grid.Children.Add(configButton);
        card.Child = grid;

        return card;
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
            Padding = new Thickness(12, 8, 12, 8)
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

        scroll.Content = stack;
        return scroll;
    }

    private ScrollViewer CreateNotificationsSettings()
    {
        var scroll = new ScrollViewer { Padding = new Thickness(24) };
        var stack = new StackPanel { Spacing = 16 };

        stack.Children.Add(CreateHeader("Notifications"));

        // Test notification button
        var testButton = new Button
        {
            Content = "Send Test Notification",
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 0, 16)
        };
        testButton.Click += (s, e) =>
        {
            NotificationService.Instance.ShowToast("Test Notification", "This is a test notification from NativeBar!");
        };
        stack.Children.Add(testButton);

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

        stack.Children.Add(CreateHeader("About NativeBar"));

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
            Text = "NativeBar",
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

    private TextBlock CreateHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
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
