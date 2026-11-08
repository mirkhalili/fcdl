using System.IO;
using SkiaSharp;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public static class WebpImageLoader
{
    public static ImageResult Load(string path, int pageIndex)
    {
        if (pageIndex != 0) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        using var codec = SKCodec.Create(path) ?? throw new InvalidDataException("Invalid WebP image.");
        if (codec.EncodedFormat != SKEncodedImageFormat.Webp) throw new InvalidDataException("The file is not a WebP image.");
        if (codec.FrameCount > 1) throw new NotSupportedException("Animated WebP is not yet supported. Export a still frame first.");
        int width = codec.Info.Width, height = codec.Info.Height;
        if (width < 1 || height < 1 || (long)width * height > ImageLoader.MaxPixels) throw new InvalidDataException("Image exceeds the 40 megapixel preview limit.");
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (codec.GetPixels(info, out byte[] bytes) != SKCodecResult.Success) throw new InvalidDataException("Could not decode WebP image.");
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, bytes, checked(width * 4)); bitmap.Freeze();
        return new ImageResult(bitmap, 1, 0, "WebP");
    }
}
