using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using NativeBar.WinUI.Core.Models;
using Svg;

namespace NativeBar.WinUI.Core.Services;

/// <summary>
/// Manages multiple tray icons, one per selected provider.
/// Each icon shows: [Provider Logo] [Remaining %]
/// Example: 🟠90% 🔵77% 🟣81%
/// </summary>
public class TrayIconsService : IDisposable
{
    private readonly Dictionary<string, TrayIconWithLegacy> _icons = new();
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Action _onLeftClick;
    private readonly Action _onRightClick;
    private readonly Action _onExitClick;
    private bool _disposed;

    // Icon size for tray (16x16 standard, but we render at higher res for quality)
    private const int IconSize = 64; // Render size (will be scaled down by Windows)
    private const int DisplayIconSize = 16; // Actual display size

    /// <summary>
    /// Provider colors
    /// </summary>
    private static readonly Dictionary<string, Color> ProviderColors = new()
    {
        { "claude", Color.FromArgb(217, 119, 87) },
        { "gemini", Color.FromArgb(66, 133, 244) },
        { "copilot", Color.FromArgb(36, 41, 47) },
        { "cursor", Color.FromArgb(0, 122, 255) },
        { "codex", Color.FromArgb(124, 58, 237) },
        { "droid", Color.FromArgb(238, 96, 24) },
        { "antigravity", Color.FromArgb(255, 107, 107) },
        { "zai", Color.FromArgb(232, 90, 106) }
    };

    /// <summary>
    /// SVG file names for each provider
    /// </summary>
    private static readonly Dictionary<string, string> ProviderSvgFiles = new()
    {
        { "claude", "claude.svg" },
        { "gemini", "gemini.svg" },
        { "copilot", "github-copilot.svg" },
        { "cursor", "cursor.svg" },
        { "codex", "openai.svg" },
        { "droid", "droid.svg" },
        { "antigravity", "antigravity.svg" },
        { "zai", "zai.svg" }
    };

    // Cache for SVG bitmaps
    private static readonly Dictionary<string, Bitmap?> SvgCache = new();

    public TrayIconsService(
        DispatcherQueue dispatcherQueue,
        Action onLeftClick,
        Action onRightClick,
        Action onExitClick)
    {
        _dispatcherQueue = dispatcherQueue;
        _onLeftClick = onLeftClick;
        _onRightClick = onRightClick;
        _onExitClick = onExitClick;
    }

    /// <summary>
    /// Update tray icons based on current usage data and selected providers
    /// </summary>
    public void UpdateIcons(
        Dictionary<string, UsageSnapshot?> snapshots,
        List<string> selectedProviders,
        bool isDarkMode)
    {
        if (_disposed) return;

        try
        {
            // Remove icons for providers no longer selected
            var toRemove = _icons.Keys.Where(k => !selectedProviders.Contains(k)).ToList();
            foreach (var providerId in toRemove)
            {
                RemoveIcon(providerId);
            }

            // Add/update icons for selected providers
            foreach (var providerId in selectedProviders)
            {
                var snapshot = snapshots.GetValueOrDefault(providerId);
                double? remainingPercent = null;

                if (snapshot?.Primary != null && !snapshot.IsLoading && snapshot.ErrorMessage == null)
                {
                    remainingPercent = Math.Max(0, 100 - snapshot.Primary.UsedPercent);
                }

                UpdateOrCreateIcon(providerId, remainingPercent, isDarkMode);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayIconsService", "UpdateIcons error", ex);
        }
    }

    /// <summary>
    /// Remove all tray icons
    /// </summary>
    public void RemoveAllIcons()
    {
        foreach (var providerId in _icons.Keys.ToList())
        {
            RemoveIcon(providerId);
        }
    }

    private void UpdateOrCreateIcon(string providerId, double? remainingPercent, bool isDarkMode)
    {
        try
        {
            var bitmap = GenerateProviderIcon(providerId, remainingPercent, isDarkMode);
            var iconHandle = bitmap.GetHicon();

            if (_icons.TryGetValue(providerId, out var existingIcon))
            {
                // Update existing icon
                existingIcon.UpdateIcon(iconHandle);
                existingIcon.UpdateToolTip(GetTooltip(providerId, remainingPercent));
            }
            else
            {
                // Create new icon
                var icon = new TrayIconWithLegacy();
                icon.Create(
                    iconHandle,
                    GetTooltip(providerId, remainingPercent),
                    () => _dispatcherQueue.TryEnqueue(() => _onLeftClick()),
                    () => _dispatcherQueue.TryEnqueue(() => _onRightClick())
                );
                _icons[providerId] = icon;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("TrayIconsService", $"UpdateOrCreateIcon({providerId}) error", ex);
        }
    }

    private void RemoveIcon(string providerId)
    {
        if (_icons.TryGetValue(providerId, out var icon))
        {
            icon.Dispose();
            _icons.Remove(providerId);
        }
    }

    private string GetTooltip(string providerId, double? remainingPercent)
    {
        var name = GetProviderDisplayName(providerId);
        if (remainingPercent.HasValue)
        {
            return $"{name}: {remainingPercent:F0}% remaining";
        }
        return $"{name}: No data";
    }

    private string GetProviderDisplayName(string providerId)
    {
        return providerId.ToLower() switch
        {
            "claude" => "Claude",
            "gemini" => "Gemini",
            "copilot" => "GitHub Copilot",
            "cursor" => "Cursor",
            "codex" => "Codex",
            "droid" => "Droid",
            "antigravity" => "Antigravity",
            "zai" => "z.ai",
            _ => providerId
        };
    }

    /// <summary>
    /// Generate icon bitmap: [Logo][Percentage]
    /// </summary>
    private Bitmap GenerateProviderIcon(string providerId, double? remainingPercent, bool isDarkMode)
    {
        var bitmap = new Bitmap(IconSize, IconSize);

        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        var color = GetProviderColor(providerId);

        // Layout: Logo on left (square), percentage on right
        const int logoSize = 28;
        const int padding = 2;

        // Draw provider logo (circular background with SVG)
        DrawProviderLogo(g, providerId, padding, (IconSize - logoSize) / 2, logoSize, color);

        // Draw percentage text
        if (remainingPercent.HasValue)
        {
            DrawPercentageText(g, remainingPercent.Value, logoSize + padding * 2, color, isDarkMode);
        }
        else
        {
            DrawNoDataText(g, logoSize + padding * 2, isDarkMode);
        }

        return bitmap;
    }

    private void DrawProviderLogo(Graphics g, string providerId, int x, int y, int size, Color color)
    {
        // Draw circular background
        using (var bgBrush = new SolidBrush(color))
        {
            g.FillEllipse(bgBrush, x, y, size, size);
        }

        // Draw SVG logo
        var logo = GetProviderLogo(providerId, size - 8);
        if (logo != null)
        {
            g.DrawImage(logo, x + 4, y + 4, size - 8, size - 8);
        }
        else
        {
            // Fallback: draw initial
            var initial = providerId.Length > 0 ? providerId[0].ToString().ToUpper() : "?";
            using var font = new Font("Segoe UI", size / 3f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.White);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            g.DrawString(initial, font, brush, new RectangleF(x, y, size, size), sf);
        }
    }

    private void DrawPercentageText(Graphics g, double percent, int startX, Color providerColor, bool isDarkMode)
    {
        var text = $"{(int)percent}%";

        // Color based on status
        Color textColor;
        if (percent <= 10)
            textColor = Color.FromArgb(220, 38, 38); // Red - critical
        else if (percent <= 25)
            textColor = Color.FromArgb(245, 158, 11); // Orange - warning
        else
            textColor = providerColor;

        using var font = new Font("Segoe UI", 18f, FontStyle.Bold);
        using var brush = new SolidBrush(textColor);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center
        };

        var textRect = new RectangleF(startX, 0, IconSize - startX, IconSize);
        g.DrawString(text, font, brush, textRect, sf);
    }

    private void DrawNoDataText(Graphics g, int startX, bool isDarkMode)
    {
        var textColor = isDarkMode ? Color.FromArgb(150, 150, 150) : Color.FromArgb(100, 100, 100);

        using var font = new Font("Segoe UI", 14f, FontStyle.Bold);
        using var brush = new SolidBrush(textColor);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center
        };

        var textRect = new RectangleF(startX, 0, IconSize - startX, IconSize);
        g.DrawString("--", font, brush, textRect, sf);
    }

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
            DebugLogger.LogError("TrayIconsService", $"Failed to load SVG for {providerId}", ex);
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        RemoveAllIcons();
    }
}

/// <summary>
/// Wrapper for TrayIcon using Win32 interop
/// </summary>
internal class TrayIconWithLegacy : IDisposable
{
    private TrayIcon? _trayIcon;
    private bool _disposed;

    public void Create(IntPtr iconHandle, string tooltip, Action onLeftClick, Action onRightClick)
    {
        _trayIcon = new TrayIcon();
        _trayIcon.Create();

        // Use Icon from handle
        var icon = System.Drawing.Icon.FromHandle(iconHandle);
        _trayIcon.UpdateIcon(icon.Handle);
        _trayIcon.UpdateToolTip(tooltip);

        _trayIcon.MessageWindow.MouseEventReceived += (sender, args) =>
        {
            if (args.MouseEvent == MouseEvent.IconLeftMouseUp)
            {
                onLeftClick();
            }
            else if (args.MouseEvent == MouseEvent.IconRightMouseUp)
            {
                onRightClick();
            }
        };
    }

    public void UpdateIcon(IntPtr iconHandle)
    {
        _trayIcon?.UpdateIcon(iconHandle);
    }

    public void UpdateToolTip(string tooltip)
    {
        _trayIcon?.UpdateToolTip(tooltip);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
