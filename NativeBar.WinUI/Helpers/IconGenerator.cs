using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using NativeBar.WinUI.Core.Services;
using Svg;

namespace NativeBar.WinUI.Helpers;

/// <summary>
/// Generates application icons from SVG source.
/// Creates PNG files at multiple sizes and ICO file for Windows.
/// </summary>
public static class IconGenerator
{
    private static readonly int[] IconSizes = { 16, 32, 48, 64, 128, 256 };

    /// <summary>
    /// Generate all icon sizes from LOGO.svg if they don't exist
    /// </summary>
    public static void EnsureIconsExist()
    {
        try
        {
            var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
            var svgPath = Path.Combine(assetsPath, "LOGO.svg");

            if (!File.Exists(svgPath))
            {
                DebugLogger.LogError("IconGenerator", $"SVG not found: {svgPath}");
                return;
            }

            // Check if we need to regenerate
            var icoPath = Path.Combine(assetsPath, "app.ico");
            if (File.Exists(icoPath))
            {
                // Icons already exist
                return;
            }

            GenerateAllIcons(svgPath, assetsPath);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("IconGenerator", "EnsureIconsExist failed", ex);
        }
    }

    /// <summary>
    /// Force regenerate all icons from SVG
    /// </summary>
    public static void RegenerateIcons()
    {
        try
        {
            var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
            var svgPath = Path.Combine(assetsPath, "LOGO.svg");

            if (!File.Exists(svgPath))
            {
                DebugLogger.LogError("IconGenerator", $"SVG not found: {svgPath}");
                return;
            }

            GenerateAllIcons(svgPath, assetsPath);
        }
        catch (Exception ex)
        {
            DebugLogger.LogError("IconGenerator", "RegenerateIcons failed", ex);
        }
    }

    private static void GenerateAllIcons(string svgPath, string outputPath)
    {
        DebugLogger.Log("IconGenerator", "Generating icons from SVG...");

        var svgDoc = SvgDocument.Open(svgPath);
        var bitmaps = new List<Bitmap>();

        try
        {
            // Generate PNGs at each size
            foreach (var size in IconSizes)
            {
                var bitmap = RenderSvgToBitmap(svgDoc, size);
                bitmaps.Add(bitmap);

                // Save PNG
                var pngPath = Path.Combine(outputPath, $"LOGO-{size}.png");
                bitmap.Save(pngPath, ImageFormat.Png);
                DebugLogger.Log("IconGenerator", $"Generated: LOGO-{size}.png");
            }

            // Generate ICO file with all sizes
            var icoPath = Path.Combine(outputPath, "app.ico");
            CreateIcoFile(bitmaps, icoPath);
            DebugLogger.Log("IconGenerator", $"Generated: app.ico");
        }
        finally
        {
            // Dispose all bitmaps
            foreach (var bmp in bitmaps)
            {
                bmp.Dispose();
            }
        }
    }

    /// <summary>
    /// Render SVG to bitmap at specified size with high quality
    /// </summary>
    public static Bitmap RenderSvgToBitmap(SvgDocument svgDoc, int size)
    {
        // Create a copy to avoid modifying the original
        svgDoc.Width = size;
        svgDoc.Height = size;

        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);

            // Render SVG
            svgDoc.Draw(g);
        }

        return bitmap;
    }

    /// <summary>
    /// Load SVG and render at specified size
    /// </summary>
    public static Bitmap LoadSvgAsBitmap(string svgPath, int size)
    {
        var svgDoc = SvgDocument.Open(svgPath);
        return RenderSvgToBitmap(svgDoc, size);
    }

    /// <summary>
    /// Get icon handle for tray icon (16x16)
    /// </summary>
    public static IntPtr GetTrayIconHandle()
    {
        var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        var svgPath = Path.Combine(assetsPath, "LOGO.svg");

        if (File.Exists(svgPath))
        {
            try
            {
                using var bitmap = LoadSvgAsBitmap(svgPath, 16);
                return bitmap.GetHicon();
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("IconGenerator", "GetTrayIconHandle from SVG failed", ex);
            }
        }

        // Fallback to PNG if SVG fails
        var pngPath = Path.Combine(assetsPath, "LOGO-16.png");
        if (File.Exists(pngPath))
        {
            try
            {
                using var bitmap = new Bitmap(pngPath);
                using var resized = new Bitmap(bitmap, 16, 16);
                return resized.GetHicon();
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("IconGenerator", "GetTrayIconHandle from PNG failed", ex);
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Get bitmap for tray icon at specified size
    /// </summary>
    public static Bitmap? GetLogoBitmap(int size)
    {
        var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        var svgPath = Path.Combine(assetsPath, "LOGO.svg");

        if (File.Exists(svgPath))
        {
            try
            {
                return LoadSvgAsBitmap(svgPath, size);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("IconGenerator", $"GetLogoBitmap({size}) from SVG failed", ex);
            }
        }

        // Fallback to pre-generated PNG
        var pngPath = Path.Combine(assetsPath, $"LOGO-{size}.png");
        if (!File.Exists(pngPath))
        {
            // Try nearest size
            pngPath = Path.Combine(assetsPath, "LOGO-NATIVE.png");
        }

        if (File.Exists(pngPath))
        {
            try
            {
                var original = new Bitmap(pngPath);
                if (original.Width != size || original.Height != size)
                {
                    var resized = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(resized))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawImage(original, 0, 0, size, size);
                    }
                    original.Dispose();
                    return resized;
                }
                return original;
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("IconGenerator", $"GetLogoBitmap({size}) from PNG failed", ex);
            }
        }

        return null;
    }

    /// <summary>
    /// Create ICO file from multiple bitmap sizes
    /// ICO format: https://en.wikipedia.org/wiki/ICO_(file_format)
    /// </summary>
    private static void CreateIcoFile(List<Bitmap> bitmaps, string outputPath)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // ICO Header
        writer.Write((short)0);           // Reserved
        writer.Write((short)1);           // Type: 1 = ICO
        writer.Write((short)bitmaps.Count); // Number of images

        // Calculate offsets
        var headerSize = 6 + (16 * bitmaps.Count);
        var imageDataOffset = headerSize;
        var imageData = new List<byte[]>();

        // Convert each bitmap to PNG data
        foreach (var bmp in bitmaps)
        {
            using var pngMs = new MemoryStream();
            bmp.Save(pngMs, ImageFormat.Png);
            imageData.Add(pngMs.ToArray());
        }

        // Write ICONDIRENTRY for each image
        for (int i = 0; i < bitmaps.Count; i++)
        {
            var bmp = bitmaps[i];
            var data = imageData[i];

            writer.Write((byte)(bmp.Width >= 256 ? 0 : bmp.Width));   // Width (0 = 256)
            writer.Write((byte)(bmp.Height >= 256 ? 0 : bmp.Height)); // Height (0 = 256)
            writer.Write((byte)0);         // Color palette
            writer.Write((byte)0);         // Reserved
            writer.Write((short)1);        // Color planes
            writer.Write((short)32);       // Bits per pixel
            writer.Write(data.Length);     // Size of image data
            writer.Write(imageDataOffset); // Offset to image data

            imageDataOffset += data.Length;
        }

        // Write image data
        foreach (var data in imageData)
        {
            writer.Write(data);
        }

        // Save to file
        File.WriteAllBytes(outputPath, ms.ToArray());
    }
}
