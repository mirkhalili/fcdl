using System.IO;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed record VectorDocument(byte[] PdfBytes, int PageIndex);
public record ImageResult(BitmapSource Image, int PageCount, int PageIndex, string Format, VectorDocument? Vector = null);

/// <summary>Preview only; this class does not interpret images as loom instructions.</summary>
public static class ImageLoader
{
    public const long MaxFileBytes = 128L * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    public const int MaxFrames = 100;

    public static ImageResult Load(string path, int pageIndex)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("File not found.", path);
        if (info.Length > MaxFileBytes) throw new InvalidDataException("File exceeds the 128 MiB preview limit.");
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".pdf" or ".ai") return PdfImageLoader.Load(path, pageIndex);
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
