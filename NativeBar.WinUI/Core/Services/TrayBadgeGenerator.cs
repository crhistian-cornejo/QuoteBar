using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using NativeBar.WinUI.Core.Models;
using Svg;

namespace NativeBar.WinUI.Core.Services;

/// <summary>
/// Generates tray icon badges showing up to 3 provider logos with usage percentages
/// Layout: [Logo1 %] [Logo2 %] [Logo3 %] in a row
/// </summary>
public static class TrayBadgeGenerator
{
    // Use 32x32 for better quality (Windows supports high DPI icons)
    private const int IconSize = 32;

    /// <summary>
    /// Provider colors for badge backgrounds
    /// </summary>
    private static readonly Dictionary<string, Color> ProviderColors = new()
    {
        { "claude", Color.FromArgb(217, 119, 87) },    // #D97757
        { "gemini", Color.FromArgb(66, 133, 244) },    // #4285F4
        { "copilot", Color.FromArgb(36, 41, 47) },     // #24292F
        { "cursor", Color.FromArgb(0, 122, 255) },     // #007AFF
        { "codex", Color.FromArgb(124, 58, 237) },     // #7C3AED
        { "droid", Color.FromArgb(238, 96, 24) },      // #EE6018
        { "antigravity", Color.FromArgb(255, 107, 107) }, // #FF6B6B
        { "zai", Color.FromArgb(232, 90, 106) },        // #E85A6A
        { "augment", Color.FromArgb(60, 60, 60) }         // Dark gray (neutral)
    };

    /// <summary>
    /// SVG file names for each provider (white versions for dark backgrounds)
    /// </summary>
    private static readonly Dictionary<string, string> ProviderSvgFiles = new()
    {
        { "claude", "claude-white.svg" },
        { "gemini", "gemini-white.svg" },
        { "copilot", "github-copilot-white.svg" },
        { "cursor", "cursor-white.svg" },
        { "codex", "openai-white.svg" },
        { "droid", "droid-white.svg" },
        { "antigravity", "antigravity.svg" },
        { "zai", "zai-white.svg" },
        { "augment", "augment-white.svg" }
    };

    // Cache for loaded SVG bitmaps
    private static readonly Dictionary<string, Bitmap?> SvgCache = new();

    /// <summary>
    /// Generate a tray badge bitmap showing a single provider with usage percentage
    /// </summary>
    public static Bitmap GenerateBadge(
        string providerId,
        double usagePercent,
        bool isDarkMode = true)
    {
        var bitmap = new Bitmap(IconSize, IconSize);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        if (string.IsNullOrEmpty(providerId))
        {
            DrawNoDataBadge(g, isDarkMode);
            return bitmap;
        }

        var remaining = Math.Max(0, 100 - usagePercent);
        DrawSingleProviderLarge(g, providerId, remaining, isDarkMode);

        return bitmap;
    }

    /// <summary>
    /// Single provider: Large clear display with logo and percentage
    /// </summary>
    private static void DrawSingleProviderLarge(
        Graphics g,
        string providerId,
        double remaining,
        bool isDarkMode)
    {
        var color = GetProviderColor(providerId);

        // Draw full circular background
        using (var bgBrush = new SolidBrush(color))
        {
            g.FillEllipse(bgBrush, 0, 0, IconSize, IconSize);
        }

        // Draw provider logo (centered, larger - 18x18)
        var logo = GetProviderLogo(providerId, 18);
        if (logo != null)
        {
            g.DrawImage(logo, (IconSize - 18) / 2, 2, 18, 18);
        }

        // Draw percentage at bottom with background
        var text = $"{(int)remaining}%";

        // Status color for text background
        Color badgeBg;
        if (remaining <= 10)
            badgeBg = Color.FromArgb(200, 220, 38, 38); // Red
        else if (remaining <= 25)
            badgeBg = Color.FromArgb(200, 245, 158, 11); // Orange
        else
            badgeBg = Color.FromArgb(180, 0, 0, 0); // Dark

        // Draw rounded badge background
        var badgeY = IconSize - 12;
        using (var path = CreateRoundedRect(2, badgeY, IconSize - 4, 11, 4))
        using (var badgeBrush = new SolidBrush(badgeBg))
        {
            g.FillPath(badgeBrush, path);
        }

        // Draw percentage text
        using var font = new Font("Segoe UI", 7f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.White);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, font, textBrush, new RectangleF(0, badgeY, IconSize, 11), sf);
    }

    /// <summary>
    /// Single provider: Large logo with percentage badge
    /// </summary>
    private static void DrawSingleProvider(
        Graphics g,
        (string Provider, double Remaining) data,
        bool isDarkMode)
    {
        var color = GetProviderColor(data.Provider);

        // Draw circular background (full size)
        using (var bgBrush = new SolidBrush(color))
        {
            g.FillEllipse(bgBrush, 2, 2, IconSize - 4, IconSize - 4);
        }

        // Draw provider logo (centered, 16x16)
        var logo = GetProviderLogo(data.Provider, 16);
        if (logo != null)
        {
            g.DrawImage(logo, (IconSize - 16) / 2, 4, 16, 16);
        }

        // Draw percentage at bottom
        DrawPercentageBadge(g, data.Remaining, 0, IconSize - 12, IconSize, 12, isDarkMode);
    }

    /// <summary>
    /// Two providers: Side by side
    /// </summary>
    private static void DrawTwoProviders(
        Graphics g,
        List<(string Provider, double Remaining)> data,
        bool isDarkMode)
    {
        const int logoSize = 12;
        const int spacing = 2;
        var totalWidth = (logoSize + spacing) * 2 - spacing;
        var startX = (IconSize - totalWidth) / 2;

        for (int i = 0; i < 2; i++)
        {
            var x = startX + i * (logoSize + spacing);
            var color = GetProviderColor(data[i].Provider);

            // Draw circular background
            using (var bgBrush = new SolidBrush(color))
            {
                g.FillEllipse(bgBrush, x, 2, logoSize, logoSize);
            }

            // Draw provider logo
            var logo = GetProviderLogo(data[i].Provider, logoSize - 4);
            if (logo != null)
            {
                g.DrawImage(logo, x + 2, 4, logoSize - 4, logoSize - 4);
            }

            // Draw percentage below
            DrawSmallPercentage(g, data[i].Remaining, x, 16, logoSize, color, isDarkMode);
        }
    }

    /// <summary>
    /// Three providers: Compact row layout
    /// Layout: [●%] [●%] [●%]
    /// </summary>
    private static void DrawThreeProviders(
        Graphics g,
        List<(string Provider, double Remaining)> data,
        bool isDarkMode)
    {
        const int logoSize = 10;
        const int spacing = 1;
        var totalWidth = (logoSize + spacing) * 3 - spacing;
        var startX = (IconSize - totalWidth) / 2;

        for (int i = 0; i < 3; i++)
        {
            var x = startX + i * (logoSize + spacing);
            var color = GetProviderColor(data[i].Provider);

            // Draw circular background
            using (var bgBrush = new SolidBrush(color))
            {
                g.FillEllipse(bgBrush, x, 2, logoSize, logoSize);
            }

            // Draw provider logo
            var logo = GetProviderLogo(data[i].Provider, logoSize - 2);
            if (logo != null)
            {
                g.DrawImage(logo, x + 1, 3, logoSize - 2, logoSize - 2);
            }

            // Draw percentage below
            DrawSmallPercentage(g, data[i].Remaining, x, 14, logoSize, color, isDarkMode);
        }
    }

    /// <summary>
    /// Draw percentage badge with background
    /// </summary>
    private static void DrawPercentageBadge(
        Graphics g,
        double remaining,
        float x, float y, float width, float height,
        bool isDarkMode)
    {
        var text = $"{(int)remaining}%";
        
        // Background color based on remaining
        Color bgColor;
        if (remaining <= 10)
            bgColor = Color.FromArgb(220, 38, 38); // Red
        else if (remaining <= 25)
            bgColor = Color.FromArgb(245, 158, 11); // Orange
        else
            bgColor = isDarkMode ? Color.FromArgb(30, 30, 35) : Color.FromArgb(240, 240, 240);

        // Draw rounded background
        using (var path = CreateRoundedRect(x + 2, y, width - 4, height - 2, 3))
        using (var bgBrush = new SolidBrush(bgColor))
        {
            g.FillPath(bgBrush, path);
        }

        // Draw text
        var textColor = (remaining <= 25) ? Color.White : (isDarkMode ? Color.White : Color.Black);
        using var font = new Font("Segoe UI", 6f, FontStyle.Bold);
        using var brush = new SolidBrush(textColor);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString(text, font, brush, new RectangleF(x, y, width, height - 2), sf);
    }

    /// <summary>
    /// Draw small percentage text below logo
    /// </summary>
    private static void DrawSmallPercentage(
        Graphics g,
        double remaining,
        float x, float y, float width,
        Color providerColor,
        bool isDarkMode)
    {
        var text = $"{(int)remaining}";
        
        // Color based on status
        Color textColor;
        if (remaining <= 10)
            textColor = Color.FromArgb(220, 38, 38); // Red
        else if (remaining <= 25)
            textColor = Color.FromArgb(245, 158, 11); // Orange
        else
            textColor = providerColor;

        using var font = new Font("Segoe UI", 5f, FontStyle.Bold);
        using var brush = new SolidBrush(textColor);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Near
        };
        g.DrawString(text, font, brush, new RectangleF(x - 1, y, width + 2, 12), sf);
    }

    /// <summary>
    /// No data badge
    /// </summary>
    private static void DrawNoDataBadge(Graphics g, bool isDarkMode)
    {
        var bgColor = isDarkMode ? Color.FromArgb(60, 60, 65) : Color.FromArgb(200, 200, 200);
        var textColor = isDarkMode ? Color.FromArgb(150, 150, 150) : Color.FromArgb(100, 100, 100);

        using (var bgBrush = new SolidBrush(bgColor))
        {
            g.FillEllipse(bgBrush, 4, 4, IconSize - 8, IconSize - 8);
        }

        using var font = new Font("Segoe UI", 12, FontStyle.Bold);
        using var brush = new SolidBrush(textColor);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        g.DrawString("?", font, brush, new RectangleF(0, 0, IconSize, IconSize), sf);
    }

    /// <summary>
    /// Load and cache provider logo from SVG
    /// </summary>
    private static Bitmap? GetProviderLogo(string providerId, int size)
    {
        var cacheKey = $"{providerId}_{size}";
        
        if (SvgCache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            if (!ProviderSvgFiles.TryGetValue(providerId.ToLower(), out var svgFile))
                return null;

            var svgPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icons", svgFile);
            if (!File.Exists(svgPath))
            {
                SvgCache[cacheKey] = null;
                return null;
            }

            var svgDoc = SvgDocument.Open(svgPath);
            svgDoc.Width = size;
            svgDoc.Height = size;
            
            var bitmap = svgDoc.Draw(size, size);
            SvgCache[cacheKey] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayBadgeGenerator", $"Failed to load SVG for {providerId}", ex);
            SvgCache[cacheKey] = null;
            return null;
        }
    }

    private static Color GetProviderColor(string providerId)
    {
        return ProviderColors.TryGetValue(providerId.ToLower(), out var color) 
            ? color 
            : Color.FromArgb(100, 100, 100);
    }

    private static GraphicsPath CreateRoundedRect(float x, float y, float width, float height, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(width, height) / 2);

        path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
        path.AddArc(x + width - radius * 2, y, radius * 2, radius * 2, 270, 90);
        path.AddArc(x + width - radius * 2, y + height - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>
    /// Convenience method to generate badge from UsageStore snapshot for a single provider
    /// </summary>
    public static Bitmap GenerateBadgeFromSnapshot(
        string providerId,
        UsageSnapshot? snapshot,
        bool isDarkMode = true)
    {
        if (snapshot?.Primary != null && !snapshot.IsLoading && snapshot.ErrorMessage == null)
        {
            return GenerateBadge(providerId, snapshot.Primary.UsedPercent, isDarkMode);
        }

        // No data - show placeholder
        return GenerateBadge("", 0, isDarkMode);
    }
}
