using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Svg;

/// <summary>
/// Tool to generate PNG and ICO icons from LOGO.svg
/// Run this before building to update application icons
/// </summary>
class Program
{
    static readonly int[] IconSizes = { 16, 24, 32, 48, 64, 128, 256 };

    static int Main(string[] args)
    {
        try
        {
            // Find the Assets folder relative to this tool
            var toolDir = AppContext.BaseDirectory;
            var repoRoot = Path.GetFullPath(Path.Combine(toolDir, "..", "..", "..", "..", ".."));
            var assetsDir = Path.Combine(repoRoot, "NativeBar.WinUI", "Assets");
            var svgPath = Path.Combine(assetsDir, "LOGO.svg");

            // Allow override via command line
            if (args.Length > 0)
            {
                svgPath = args[0];
                assetsDir = Path.GetDirectoryName(svgPath) ?? assetsDir;
            }

            if (!File.Exists(svgPath))
            {
                Console.WriteLine($"ERROR: SVG not found: {svgPath}");
                return 1;
            }

            Console.WriteLine($"Loading SVG from: {svgPath}");
            Console.WriteLine($"Output directory: {assetsDir}");

            var svgDoc = SvgDocument.Open(svgPath);
            var bitmaps = new List<Bitmap>();

            // Generate PNGs at each size
            foreach (var size in IconSizes)
            {
                var bitmap = RenderSvgToBitmap(svgDoc, size);
                bitmaps.Add(bitmap);

                var pngPath = Path.Combine(assetsDir, $"LOGO-{size}.png");
                bitmap.Save(pngPath, ImageFormat.Png);
                Console.WriteLine($"Generated: LOGO-{size}.png");
            }

            // Generate ICO file with all sizes
            var icoPath = Path.Combine(assetsDir, "app.ico");
            CreateIcoFile(bitmaps, icoPath);
            Console.WriteLine($"Generated: app.ico");

            // Dispose bitmaps
            foreach (var bmp in bitmaps)
            {
                bmp.Dispose();
            }

            Console.WriteLine("Done!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    static Bitmap RenderSvgToBitmap(SvgDocument svgDoc, int size)
    {
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

            svgDoc.Draw(g);
        }

        return bitmap;
    }

    static void CreateIcoFile(List<Bitmap> bitmaps, string outputPath)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // ICO Header
        writer.Write((short)0);             // Reserved
        writer.Write((short)1);             // Type: 1 = ICO
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
