using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;

namespace JacquardStudio;

public sealed class VectorDocument
{
    public byte[] PdfBytes { get; }
    public int PageIndex { get; }
    public string? SvgMarkup { get; }
    public bool IsSvg => SvgMarkup is not null;

    public VectorDocument(byte[] pdfBytes, int pageIndex)
    {
        PdfBytes = pdfBytes ?? throw new ArgumentNullException(nameof(pdfBytes));
        PageIndex = pageIndex;
    }

    public VectorDocument(string svgMarkup)
    {
        SvgMarkup = svgMarkup ?? throw new ArgumentNullException(nameof(svgMarkup));
        PdfBytes = Array.Empty<byte>();
        PageIndex = 0;
    }
}
public record ImageResult(
    BitmapSource Image,
    int PageCount,
    int PageIndex,
    string Format,
    VectorDocument? Vector = null,
    int OriginalWidth = 0,
    int OriginalHeight = 0,
    bool ColorLimitApplied = false);

/// <summary>Preview only; this class does not interpret images as loom instructions.</summary>
public static class ImageLoader
{
    public const long MaxFileBytes = 128L * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    public const int MaxFrames = 100;
    public const int MaxImportColors = 256;
    public const int MaxImportWidth = 4000;

    public static ImageResult Load(string path, int pageIndex, bool limitWidth = false, bool limitColors = false)
    {
        var result = LoadOriginal(path, pageIndex);
        if (limitWidth && result.Image.PixelWidth > MaxImportWidth)
        {
            int sourceWidth = result.Image.PixelWidth, sourceHeight = result.Image.PixelHeight;
            result = result with
            {
                Image = LimitWidth(result.Image),
                OriginalWidth = sourceWidth,
                OriginalHeight = sourceHeight,
            };
        }

        // Keep original vector data for later re-rendering; palette reduction applies to raster maps.
        if (limitColors && result.Vector is null)
            result = result with { Image = LimitColors(result.Image), ColorLimitApplied = true };
        return result;
    }

    /// <summary>Limit display/image import width without modifying a file, preserving alpha and aspect ratio.</summary>
    public static BitmapSource LimitWidth(BitmapSource source)
    {
        if (source.PixelWidth <= MaxImportWidth) return source;
        if (source.PixelWidth < 1 || source.PixelHeight < 1 || (long)source.PixelWidth * source.PixelHeight > MaxPixels)
            throw new InvalidDataException("ابعاد تصویر خارج از محدوده بارگذاری است.");
        int width = MaxImportWidth;
        int height = Math.Max(1, (int)Math.Round((double)source.PixelHeight * width / source.PixelWidth, MidpointRounding.AwayFromZero));
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var row = new byte[checked(source.PixelWidth * 4)];
        var output = new byte[checked(width * height * 4)];
        int cached = -1;
        for (int y = 0; y < height; y++)
        {
            int sy = (int)((2L * y + 1) * source.PixelHeight / (2L * height));
            if (sy != cached)
            {
                converted.CopyPixels(new Int32Rect(0, sy, source.PixelWidth, 1), row, row.Length, 0);
                cached = sy;
            }
            for (int x = 0; x < width; x++)
            {
                int sx = (int)((2L * x + 1) * source.PixelWidth / (2L * width));
                Buffer.BlockCopy(row, sx * 4, output, (y * width + x) * 4, 4);
            }
        }
        var bitmap = BitmapSource.Create(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, output, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Convert a raster map to an indexed image with at most 256 colors. A representative
    /// sample is used to build the palette when the source is larger than the processing limit.
    /// </summary>
    public static BitmapSource LimitColors(BitmapSource source, int maximumColors = MaxImportColors)
    {
        if (source.PixelWidth < 1 || source.PixelHeight < 1 || (long)source.PixelWidth * source.PixelHeight > MaxPixels)
            throw new InvalidDataException("ابعاد تصویر خارج از محدوده بارگذاری است.");
        if (maximumColors is < 1 or > MaxImportColors)
            throw new ArgumentOutOfRangeException(nameof(maximumColors), "The import palette must contain between 1 and 256 colors.");

        if (source.Format.BitsPerPixel <= 8 && source.Palette is { } existingPalette &&
            existingPalette.Colors.Count is > 0 && existingPalette.Colors.Count <= maximumColors)
            return source;

        // Converting every raster format to BGRA32 also handles alpha palettes consistently.
        BitmapSource opaque = FlattenTransparencyOnWhite(source);
        long pixels = (long)opaque.PixelWidth * opaque.PixelHeight;
        int sampleWidth = opaque.PixelWidth;
        int sampleHeight = opaque.PixelHeight;
        if (pixels > DesignProcessor.MaxPixels)
        {
            double scale = Math.Sqrt((double)DesignProcessor.MaxPixels / pixels);
            sampleWidth = Math.Max(1, (int)Math.Floor(opaque.PixelWidth * scale));
            sampleHeight = Math.Max(1, (int)Math.Floor(opaque.PixelHeight * scale));
            sampleHeight = Math.Min(sampleHeight, Math.Max(1, DesignProcessor.MaxPixels / sampleWidth));
        }

        var sampledPalette = DesignProcessor.Prepare(opaque, sampleWidth, sampleHeight, maximumColors);
        var colors = sampledPalette.Colors
            .Select(value => Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value))
            .ToArray();
        if (colors.Length is < 1 or > MaxImportColors)
            throw new InvalidDataException("ساخت پالت ۲۵۶ رنگ ممکن نشد.");

        var palette = new BitmapPalette(colors);
        var converted = new FormatConvertedBitmap(opaque, PixelFormats.Indexed8, palette, 0);
        int stride = opaque.PixelWidth;
        var indices = new byte[checked(stride * opaque.PixelHeight)];
        converted.CopyPixels(indices, stride, 0);
        var indexed = BitmapSource.Create(opaque.PixelWidth, opaque.PixelHeight, opaque.DpiX, opaque.DpiY, PixelFormats.Indexed8, palette, indices, stride);
        indexed.Freeze();
        return indexed;
    }

    private static BitmapSource FlattenTransparencyOnWhite(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = checked(source.PixelWidth * 4);
        var pixels = new byte[checked(stride * source.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] < 128)
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 255;
            pixels[offset + 3] = 255;
        }
        var opaque = BitmapSource.Create(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        opaque.Freeze();
        return opaque;
    }

    private static ImageResult LoadOriginal(string path, int pageIndex)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("File not found.", path);
        if (info.Length > MaxFileBytes) throw new InvalidDataException("File exceeds the 128 MiB preview limit.");
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".pdf" or ".ai") return PdfImageLoader.Load(path, pageIndex);
        if (ext == ".svg")
        {
            if (pageIndex != 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
            string markup = File.ReadAllText(path, new System.Text.UTF8Encoding(false, true));
            var document = VectorDesignDocument.FromSvg(markup);
            return new ImageResult(document.Render(), 1, 0, "SVG", new VectorDocument(markup));
        }
        if (ext == ".webp") return WebpImageLoader.Load(path, pageIndex);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        string format = decoder switch
        {
            BmpBitmapDecoder => "BMP",
            TiffBitmapDecoder => "TIFF",
            PngBitmapDecoder => "PNG",
            JpegBitmapDecoder => "JPEG",
            GifBitmapDecoder => "GIF",
            IconBitmapDecoder => "ICO",
            WmpBitmapDecoder => "HD Photo",
            _ => decoder.CodecInfo?.FriendlyName ?? "WIC image"
        };
        if (decoder.Frames.Count is 0 or > MaxFrames) throw new InvalidDataException("Invalid or excessive frame count.");
        if (pageIndex < 0 || pageIndex >= decoder.Frames.Count) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        var frame = decoder.Frames[pageIndex];
        if ((long)frame.PixelWidth * frame.PixelHeight > MaxPixels) throw new InvalidDataException("Image exceeds the 40 megapixel preview limit.");
        frame.Freeze();
        return new ImageResult(frame, decoder.Frames.Count, pageIndex, format);
    }
}
