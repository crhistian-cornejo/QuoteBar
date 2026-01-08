using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using NativeBar.WinUI.Core.Services;

namespace NativeBar.WinUI.Settings.Helpers;

/// <summary>
/// Helper class for provider icons and colors
/// </summary>
public static class ProviderIconHelper
{
    /// <summary>
    /// Get SVG filename for a provider
    /// </summary>
    public static string? GetProviderSvgFileName(string providerId, bool forIconWithBackground = false, bool? isDarkMode = null)
    {
        var isDark = isDarkMode ?? ThemeService.Instance.IsDarkMode;

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
                "minimax" => "minimax-white.svg",
                "augment" => "augment-white.svg",
                _ => null
            };
        }

        // Use white versions for dark icons in dark mode (when shown without background)
        return providerId.ToLower() switch
        {
            "claude" => "claude.svg",
            "codex" => isDark ? "openai-white.svg" : "openai.svg",
            "gemini" => "gemini.svg",
            "copilot" => isDark ? "github-copilot-white.svg" : "github-copilot.svg",
            "cursor" => isDark ? "cursor-white.svg" : "cursor.svg",
            "droid" => isDark ? "droid-white.svg" : "droid.svg",
            "antigravity" => isDark ? "antigravity.svg" : "antigravity-black.svg",
            "zai" => isDark ? "zai-white.svg" : "zai.svg",
            "minimax" => isDark ? "minimax-white.svg" : "minimax-color.svg",
            "augment" => isDark ? "augment-white.svg" : "augment.svg",
            _ => null
        };
    }

    /// <summary>
    /// Parse hex color string to Windows.UI.Color
    /// </summary>
    public static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }

    /// <summary>
    /// Get the full path to a provider's SVG icon
    /// </summary>
    public static string? GetProviderIconPath(string providerId, bool forIconWithBackground = false)
    {
        var svgFileName = GetProviderSvgFileName(providerId, forIconWithBackground);
        if (string.IsNullOrEmpty(svgFileName))
            return null;

        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icons", svgFileName);
        return System.IO.File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Create provider initial letter TextBlock (fallback when no icon)
    /// </summary>
    public static TextBlock CreateProviderInitial(string name, double fontSize = 16)
    {
        return new TextBlock
        {
            Text = name.Length > 0 ? name[0].ToString().ToUpper() : "?",
            FontSize = fontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>
    /// Create an Image element with provider SVG icon
    /// </summary>
    public static Image? CreateProviderImage(string providerId, double size = 20, bool forIconWithBackground = false)
    {
        var iconPath = GetProviderIconPath(providerId, forIconWithBackground);
        if (iconPath == null)
            return null;

        return new Image
        {
            Width = size,
            Height = size,
            Source = new SvgImageSource(new Uri(iconPath, UriKind.Absolute)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
