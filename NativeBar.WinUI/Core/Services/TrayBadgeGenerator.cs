using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using NativeBar.WinUI.Core.Models;

namespace NativeBar.WinUI.Core.Services;

/// <summary>
/// Generates tray icon badges showing usage percentages for up to 3 providers
/// </summary>
public static class TrayBadgeGenerator
{
    /// <summary>
    /// Provider abbreviation mapping for badge display
    /// </summary>
    private static readonly Dictionary<string, string> ProviderAbbreviations = new()
    {
        { "claude", "C" },
        { "gemini", "G" },
        { "copilot", "P" },  // P for GitHub coPilot
        { "cursor", "U" },   // U for cUrsor
        { "codex", "X" },    // X for codeX
        { "droid", "D" },
        { "zai", "Z" }
    };

    /// <summary>
    /// Provider colors for badge display
    /// </summary>
    private static readonly Dictionary<string, Color> ProviderColors = new()
    {
        { "claude", Color.FromArgb(217, 119, 87) },    // #D97757
        { "gemini", Color.FromArgb(66, 133, 244) },    // #4285F4
        { "copilot", Color.FromArgb(36, 41, 47) },     // #24292F
        { "cursor", Color.FromArgb(0, 122, 255) },     // #007AFF
        { "codex", Color.FromArgb(124, 58, 237) },     // #7C3AED
        { "droid", Color.FromArgb(238, 96, 24) },      // #EE6018
        { "zai", Color.FromArgb(232, 90, 106) }        // #E85A6A
    };

    /// <summary>
    /// Generate a tray badge bitmap showing usage for configured providers
    /// Shows "left" percentage (100 - used) for each provider
    /// </summary>
    /// <param name="usageData">Dictionary of provider ID to usage percentage (0-100)</param>
    /// <param name="providerOrder">Ordered list of provider IDs to display (max 3)</param>
    /// <param name="isDarkMode">Whether to use dark mode colors</param>
    /// <returns>16x16 bitmap for tray icon</returns>
    public static Bitmap GenerateBadge(
        Dictionary<string, double> usageData,
        List<string> providerOrder,
        bool isDarkMode = true)
    {
        // Standard tray icon size
        const int size = 16;
        var bitmap = new Bitmap(size, size);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Color.Transparent);

        // Get providers with data (max 3)
        var activeProviders = providerOrder
            .Where(p => usageData.ContainsKey(p))
            .Take(3)
            .ToList();

        if (activeProviders.Count == 0)
        {
            // No data - show "?" icon
            DrawNoDataBadge(g, size, isDarkMode);
            return bitmap;
        }

        // Calculate "remaining" percentages
        var remainingData = activeProviders
            .Select(p => (Provider: p, Remaining: Math.Max(0, 100 - usageData[p])))
            .ToList();

        // Choose layout based on count
        switch (activeProviders.Count)
        {
            case 1:
                DrawSingleProviderBadge(g, size, remainingData[0], isDarkMode);
                break;
            case 2:
                DrawTwoProviderBadge(g, size, remainingData, isDarkMode);
                break;
            case 3:
                DrawThreeProviderBadge(g, size, remainingData, isDarkMode);
                break;
        }

        return bitmap;
    }

    /// <summary>
    /// Single provider: Show large percentage with provider color
    /// Format: "6%" in provider color
    /// </summary>
    private static void DrawSingleProviderBadge(
        Graphics g, int size,
        (string Provider, double Remaining) data,
        bool isDarkMode)
    {
        var color = GetProviderColor(data.Provider, isDarkMode);
        var text = $"{(int)data.Remaining}";
        
        using var font = new Font("Segoe UI", 9, FontStyle.Bold);
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        g.DrawString(text, font, brush, new RectangleF(0, 0, size, size), sf);
    }

    /// <summary>
    /// Two providers: Stacked vertically
    /// Format: "C:6" on top, "G:29" on bottom
    /// </summary>
    private static void DrawTwoProviderBadge(
        Graphics g, int size,
        List<(string Provider, double Remaining)> data,
        bool isDarkMode)
    {
        using var font = new Font("Segoe UI", 5.5f, FontStyle.Bold);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        for (int i = 0; i < 2; i++)
        {
            var color = GetProviderColor(data[i].Provider, isDarkMode);
            var abbr = GetAbbreviation(data[i].Provider);
            var text = $"{abbr}{(int)data[i].Remaining}";

            using var brush = new SolidBrush(color);
            var y = i == 0 ? 0 : size / 2;
            g.DrawString(text, font, brush, new RectangleF(0, y, size, size / 2), sf);
        }
    }

    /// <summary>
    /// Three providers: Compact stacked layout
    /// Format: Three lines with abbreviated provider + percentage
    /// </summary>
    private static void DrawThreeProviderBadge(
        Graphics g, int size,
        List<(string Provider, double Remaining)> data,
        bool isDarkMode)
    {
        using var font = new Font("Segoe UI", 4f, FontStyle.Bold);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        var rowHeight = size / 3f;

        for (int i = 0; i < 3; i++)
        {
            var color = GetProviderColor(data[i].Provider, isDarkMode);
            var abbr = GetAbbreviation(data[i].Provider);
            var text = $"{abbr}{(int)data[i].Remaining}";

            using var brush = new SolidBrush(color);
            g.DrawString(text, font, brush, new RectangleF(0, i * rowHeight, size, rowHeight), sf);
        }
    }

    /// <summary>
    /// No data badge - shows "?" 
    /// </summary>
    private static void DrawNoDataBadge(Graphics g, int size, bool isDarkMode)
    {
        var color = isDarkMode ? Color.FromArgb(180, 180, 180) : Color.FromArgb(100, 100, 100);
        
        using var font = new Font("Segoe UI", 10, FontStyle.Bold);
        using var brush = new SolidBrush(color);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        g.DrawString("?", font, brush, new RectangleF(0, 0, size, size), sf);
    }

    private static string GetAbbreviation(string providerId)
    {
        return ProviderAbbreviations.TryGetValue(providerId.ToLower(), out var abbr) ? abbr : providerId[0].ToString().ToUpper();
    }

    private static Color GetProviderColor(string providerId, bool isDarkMode)
    {
        if (ProviderColors.TryGetValue(providerId.ToLower(), out var color))
        {
            // For dark icons in dark mode, use white
            if (isDarkMode && (providerId == "copilot" || providerId == "cursor" || providerId == "codex"))
            {
                return Color.White;
            }
            return color;
        }
        return isDarkMode ? Color.White : Color.Black;
    }

    /// <summary>
    /// Convenience method to generate badge from UsageStore
    /// </summary>
    public static Bitmap GenerateBadgeFromSnapshots(
        Dictionary<string, UsageSnapshot?> snapshots,
        List<string> providerOrder,
        bool isDarkMode = true)
    {
        var usageData = new Dictionary<string, double>();
        
        foreach (var (providerId, snapshot) in snapshots)
        {
            if (snapshot?.Primary != null && !snapshot.IsLoading && snapshot.ErrorMessage == null)
            {
                usageData[providerId] = snapshot.Primary.UsedPercent;
            }
        }

        return GenerateBadge(usageData, providerOrder, isDarkMode);
    }
}
