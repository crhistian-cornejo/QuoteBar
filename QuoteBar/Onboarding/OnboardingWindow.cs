using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QuoteBar.Core.Providers;
using QuoteBar.Core.Services;
using QuoteBar.Settings.Helpers;
using Windows.Graphics;
using WinRT.Interop;

namespace QuoteBar.Onboarding;

/// <summary>
/// Onboarding wizard for new users - 3 steps: Welcome, Providers, Completion
/// </summary>
public sealed class OnboardingWindow : Window
{
    public event Action? OnboardingCompleted;

    private readonly AppWindow _appWindow;
    private readonly ThemeService _theme = ThemeService.Instance;
    private Grid _rootGrid = null!;
    private Grid _contentGrid = null!;
    private StackPanel _dotsPanel = null!;
    
    private int _currentStep = 0;
    private const int TotalSteps = 3;
    
    // Steps
    private readonly UIElement[] _steps = new UIElement[3];

    public OnboardingWindow()
    {
        try
        {
            DebugLogger.Log("OnboardingWindow", "Constructor start");

            Title = "Welcome to QuoteBar";

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

            DebugLogger.Log("OnboardingWindow", "Constructor complete");
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("OnboardingWindow", "Constructor error", ex);
            throw;
        }
    }

    private void ConfigureWindow()
    {
        // Size: 640x560 (increased height for better spacing)
        _appWindow.Resize(new SizeInt32(640, 560));

        // Center on screen
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var centerX = (displayArea.WorkArea.Width - 640) / 2;
        var centerY = (displayArea.WorkArea.Height - 560) / 2;
        _appWindow.Move(new PointInt32(centerX, centerY));

        // Hide titlebar buttons but keep drag region
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // Custom titlebar
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            var titleBar = _appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    private void OnThemeChanged(ElementTheme theme)
    {
        if (_rootGrid != null) _rootGrid.RequestedTheme = theme;
    }

    private void BuildUI()
    {
        _rootGrid = new Grid
        {
            RequestedTheme = _theme.CurrentTheme,
            Background = new SolidColorBrush(Colors.Transparent)
        };

        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); // Titlebar
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
        _rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Progress dots

        // Titlebar drag region
        var titleBar = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
        Grid.SetRow(titleBar, 0);
        _rootGrid.Children.Add(titleBar);

        // Content area
        _contentGrid = new Grid();
        Grid.SetRow(_contentGrid, 1);
        _rootGrid.Children.Add(_contentGrid);

        // Progress dots
        _dotsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24),
            Spacing = 8
        };
        Grid.SetRow(_dotsPanel, 2);
        _rootGrid.Children.Add(_dotsPanel);

        // Create dots
        for (int i = 0; i < TotalSteps; i++)
        {
            var dot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(i == 0 ? _theme.AccentColor : Windows.UI.Color.FromArgb(77, 128, 128, 128))
            };
            _dotsPanel.Children.Add(dot);
        }

        // Build steps
        _steps[0] = BuildWelcomeStep();
        _steps[1] = BuildProvidersStep();
        _steps[2] = BuildCompletionStep();

        // Show first step
        ShowStep(0);

        Content = _rootGrid;
    }

    private void UpdateDots()
    {
        for (int i = 0; i < TotalSteps; i++)
        {
            if (_dotsPanel.Children[i] is Border dot)
            {
                dot.Background = new SolidColorBrush(
                    i <= _currentStep ? _theme.AccentColor : Windows.UI.Color.FromArgb(77, 128, 128, 128));
            }
        }
    }

    private void ShowStep(int stepIndex)
    {
        _currentStep = stepIndex;

        // Clear and add new step
        _contentGrid.Children.Clear();
        _contentGrid.Children.Add(_steps[stepIndex]);

        UpdateDots();
    }

    private void GoNext()
    {
        if (_currentStep < TotalSteps - 1)
        {
            ShowStep(_currentStep + 1);
        }
    }

    private void GoBack()
    {
        if (_currentStep > 0)
        {
            ShowStep(_currentStep - 1);
        }
    }

    private void CompleteOnboarding()
    {
        DebugLogger.Log("OnboardingWindow", "Completing onboarding");

        // Mark onboarding as completed and save current version
        SettingsService.Instance.Settings.OnboardingCompleted = true;
        SettingsService.Instance.Settings.OnboardingVersion = App.CURRENT_ONBOARDING_VERSION;
        SettingsService.Instance.Save();

        DebugLogger.Log("OnboardingWindow", $"Saved OnboardingVersion: {App.CURRENT_ONBOARDING_VERSION}");

        // Fire event
        OnboardingCompleted?.Invoke();

        // Close window
        Close();
    }

    #region Step 1: Welcome

    private UIElement BuildWelcomeStep()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 32
        };

        // App Logo
        var logoContainer = new Border
        {
            Width = 96,
            Height = 96,
            CornerRadius = new CornerRadius(20),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Load the actual app logo
        var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-128.png");
        if (!System.IO.File.Exists(logoPath))
        {
            logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-256.png");
        }

        if (System.IO.File.Exists(logoPath))
        {
            var logoImage = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(logoPath, UriKind.Absolute)),
                Width = 96,
                Height = 96,
                Stretch = Stretch.Uniform
            };
            logoContainer.Child = logoImage;
        }
        else
        {
            // Fallback: styled "Q" if no logo found
            logoContainer.Background = new SolidColorBrush(_theme.AccentColor);
            var logoText = new TextBlock
            {
                Text = "Q",
                FontSize = 48,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            logoContainer.Child = logoText;
        }
        panel.Children.Add(logoContainer);

        // Title
        var titleText = new TextBlock
        {
            Text = "Welcome to QuoteBar",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(_theme.TextColor)
        };
        panel.Children.Add(titleText);

        // Subtitle
        var subtitleText = new TextBlock
        {
            Text = "Monitor your AI usage across Claude, Cursor, Copilot, and more.\nAll your quotas in one place, right in your system tray.",
            FontSize = 14,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        };
        panel.Children.Add(subtitleText);

        // Spacer
        panel.Children.Add(new Border { Height = 32 });

        // Get Started button
        var getStartedBtn = new Button
        {
            Content = "Get Started",
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Width = 200,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        getStartedBtn.Click += (s, e) => GoNext();
        panel.Children.Add(getStartedBtn);

        return panel;
    }

    #endregion

    #region Step 2: Providers

    private UIElement BuildProvidersStep()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 24
        };

        // Title
        var titleText = new TextBlock
        {
            Text = "Supported Providers",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(_theme.TextColor)
        };
        panel.Children.Add(titleText);

        // Subtitle
        var subtitleText = new TextBlock
        {
            Text = "QuoteBar automatically detects your AI subscriptions.\nYou can configure each provider in Settings.",
            FontSize = 14,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 450
        };
        panel.Children.Add(subtitleText);

        // Provider grid
        var providersGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 16)
        };

        // 2 columns x 5 rows
        providersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        providersGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });

        var providers = ProviderRegistry.Instance.GetAllProviders().Take(10).ToList();
        for (int i = 0; i < providers.Count; i++)
        {
            providersGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int i = 0; i < providers.Count; i++)
        {
            var provider = providers[i];
            var providerItem = CreateProviderItem(provider);
            Grid.SetRow(providerItem, i / 2);
            Grid.SetColumn(providerItem, i % 2);
            providersGrid.Children.Add(providerItem);
        }

        panel.Children.Add(providersGrid);

        // Buttons row
        var buttonsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12
        };

        var backBtn = new Button
        {
            Content = "Back",
            Width = 100,
            Height = 36
        };
        backBtn.Click += (s, e) => GoBack();
        buttonsPanel.Children.Add(backBtn);

        var nextBtn = new Button
        {
            Content = "Continue",
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Width = 120,
            Height = 36
        };
        nextBtn.Click += (s, e) => GoNext();
        buttonsPanel.Children.Add(nextBtn);

        panel.Children.Add(buttonsPanel);

        return panel;
    }

    private FrameworkElement CreateProviderItem(IProviderDescriptor provider)
    {
        var container = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Padding = new Thickness(12, 8, 12, 8)
        };

        // Icon with colored background
        var iconBorder = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(ProviderIconHelper.ParseColor(provider.PrimaryColor))
        };

        // Use SVG icon from ProviderIconHelper
        var iconImage = ProviderIconHelper.CreateProviderImage(provider.Id, size: 18, forIconWithBackground: true);
        if (iconImage != null)
        {
            iconBorder.Child = iconImage;
        }
        else
        {
            // Fallback to initial letter
            iconBorder.Child = ProviderIconHelper.CreateProviderInitial(provider.DisplayName, 14);
        }
        container.Children.Add(iconBorder);

        // Provider name
        var nameText = new TextBlock
        {
            Text = provider.DisplayName,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(_theme.TextColor)
        };
        container.Children.Add(nameText);

        return container;
    }

    #endregion

    #region Step 3: Completion

    private UIElement BuildCompletionStep()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 24
        };

        // Success icon
        var successBorder = new Border
        {
            Width = 80,
            Height = 80,
            CornerRadius = new CornerRadius(40),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(38, 34, 197, 94)), // Green with alpha
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var successIcon = new FontIcon
        {
            Glyph = "\uE73E", // Checkmark
            FontSize = 40,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94))
        };
        successBorder.Child = successIcon;
        panel.Children.Add(successBorder);

        // Title
        var titleText = new TextBlock
        {
            Text = "You're All Set!",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(_theme.TextColor)
        };
        panel.Children.Add(titleText);

        // Subtitle
        var subtitleText = new TextBlock
        {
            Text = "QuoteBar is now running in your system tray.\nClick the icon to view your AI usage anytime.",
            FontSize = 14,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor),
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        };
        panel.Children.Add(subtitleText);

        // Spacer
        panel.Children.Add(new Border { Height = 24 });

        // Info card
        var infoCard = new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(_theme.SurfaceColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 400
        };

        var infoPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14
        };

        var infoIcon = new FontIcon
        {
            Glyph = "\uE946", // System tray icon
            FontSize = 24,
            Foreground = new SolidColorBrush(_theme.AccentColor)
        };
        infoPanel.Children.Add(infoIcon);

        var infoTextPanel = new StackPanel { Spacing = 2 };
        infoTextPanel.Children.Add(new TextBlock
        {
            Text = "Find QuoteBar in the system tray",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(_theme.TextColor)
        });
        infoTextPanel.Children.Add(new TextBlock
        {
            Text = "Right-click for menu, left-click to view usage",
            FontSize = 12,
            Foreground = new SolidColorBrush(_theme.SecondaryTextColor)
        });
        infoPanel.Children.Add(infoTextPanel);

        infoCard.Child = infoPanel;
        panel.Children.Add(infoCard);

        // Spacer
        panel.Children.Add(new Border { Height = 16 });

        // Open Dashboard button
        var openBtn = new Button
        {
            Content = "Open QuoteBar",
            Style = Application.Current.Resources["AccentButtonStyle"] as Style,
            Width = 200,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        openBtn.Click += (s, e) => CompleteOnboarding();
        panel.Children.Add(openBtn);

        // Hint
        var hintText = new TextBlock
        {
            Text = "You can access Settings anytime from the tray menu",
            FontSize = 11,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 128, 128, 128)),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        panel.Children.Add(hintText);

        return panel;
    }

    #endregion

    #region Helpers

    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            return Windows.UI.Color.FromArgb(
                255,
                Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16)
            );
        }
        return Windows.UI.Color.FromArgb(255, 100, 100, 100);
    }

    #endregion
}
