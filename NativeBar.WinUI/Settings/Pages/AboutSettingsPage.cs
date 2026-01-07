using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NativeBar.WinUI.Core.Services;
using NativeBar.WinUI.Settings.Controls;

namespace NativeBar.WinUI.Settings.Pages;

/// <summary>
/// About settings page - app info, version, links
/// </summary>
public class AboutSettingsPage : ISettingsPage
{
    private readonly ThemeService _theme = ThemeService.Instance;
    private ScrollViewer? _content;

    public FrameworkElement Content => _content ??= CreateContent();

    private ScrollViewer CreateContent()
    {
        var scroll = new ScrollViewer
        {
            Padding = new Thickness(28, 20, 28, 24),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var stack = new StackPanel { Spacing = 12 };

        stack.Children.Add(SettingCard.CreateHeader("About QuoteBar"));

        // App info card
        var infoCard = new Border
        {
            Background = new SolidColorBrush(_theme.CardColor),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(32, 28, 32, 28),
            BorderBrush = new SolidColorBrush(_theme.BorderColor),
            BorderThickness = new Thickness(1)
        };

        var infoStack = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };

        // Logo
        FrameworkElement logoElement = CreateLogoElement();
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

        // Check for updates button
        var updateButton = new Button
        {
            Content = "Check for updates",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(16, 8, 16, 8)
        };
        updateButton.Click += async (s, e) =>
        {
            if (_content?.XamlRoot == null) return;
            
            var dialog = new ContentDialog
            {
                Title = "Check for Updates",
                Content = "You are running the latest version of NativeBar!",
                CloseButtonText = "OK",
                XamlRoot = _content.XamlRoot
            };
            await dialog.ShowAsync();
        };
        stack.Children.Add(updateButton);

        scroll.Content = stack;
        return scroll;
    }

    private FrameworkElement CreateLogoElement()
    {
        try
        {
            // Try LOGO-128.png first (best quality for 80x80 display)
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-128.png");
            if (!System.IO.File.Exists(logoPath))
            {
                // Fallback to LOGO-256.png
                logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "LOGO-256.png");
            }

            if (System.IO.File.Exists(logoPath))
            {
                var image = new Image
                {
                    Width = 80,
                    Height = 80,
                    Source = new BitmapImage(new Uri(logoPath)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                return image;
            }
        }
        catch { }

        // Fallback
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
        return logoBorder;
    }

    private HyperlinkButton CreateLinkButton(string text, string url)
    {
        return new HyperlinkButton
        {
            Content = text,
            NavigateUri = new Uri(url),
            Foreground = new SolidColorBrush(_theme.AccentColor),
            Padding = new Thickness(8, 4, 8, 4)
        };
    }

    public void OnThemeChanged()
    {
        _content = null; // Force recreation on next access
    }
}
