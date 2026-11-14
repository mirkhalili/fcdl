using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;

namespace JacquardStudio;

public sealed record VectorDocument(byte[] PdfBytes, int PageIndex);
public record ImageResult(BitmapSource Image, int PageCount, int PageIndex, string Format, VectorDocument? Vector = null, int OriginalWidth = 0, int OriginalHeight = 0);

/// <summary>Preview only; this class does not interpret images as loom instructions.</summary>
public static class ImageLoader
{
    public const long MaxFileBytes = 128L * 1024 * 1024;
    public const long MaxPixels = 40_000_000;
    public const int MaxFrames = 100;

    public static ImageResult Load(string path, int pageIndex, bool limitWidth = false)
    {
        var result=LoadOriginal(path,pageIndex);
        if(!limitWidth || result.Image.PixelWidth<=4000)return result;
        int sourceWidth=result.Image.PixelWidth,sourceHeight=result.Image.PixelHeight;
        return result with { Image=LimitWidth(result.Image), OriginalWidth=sourceWidth,OriginalHeight=sourceHeight };
    }
    /// <summary>Limit display/image import width without modifying a file, preserving alpha and aspect ratio.</summary>
    public static BitmapSource LimitWidth(BitmapSource source)
    {
        if(source.PixelWidth<=4000)return source;
        if(source.PixelWidth<1 || source.PixelHeight<1 || (long)source.PixelWidth*source.PixelHeight>MaxPixels)throw new InvalidDataException("ابعاد تصویر خارج از محدوده بارگذاری است.");
        int width=4000,height=Math.Max(1,(int)Math.Round((double)source.PixelHeight*width/source.PixelWidth,MidpointRounding.AwayFromZero));
        var converted=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);var row=new byte[checked(source.PixelWidth*4)];var output=new byte[checked(width*height*4)];int cached=-1;
        for(int y=0;y<height;y++) {int sy=(int)((2L*y+1)*source.PixelHeight/(2L*height));if(sy!=cached){converted.CopyPixels(new Int32Rect(0,sy,source.PixelWidth,1),row,row.Length,0);cached=sy;}
            for(int x=0;x<width;x++){int sx=(int)((2L*x+1)*source.PixelWidth/(2L*width));Buffer.BlockCopy(row,sx*4,output,(y*width+x)*4,4);}}
        var bitmap=BitmapSource.Create(width,height,source.DpiX,source.DpiY,PixelFormats.Bgra32,null,output,width*4);bitmap.Freeze();return bitmap;
    }
    private static ImageResult LoadOriginal(string path, int pageIndex)
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
