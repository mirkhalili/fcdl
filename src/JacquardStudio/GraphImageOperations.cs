using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public enum CropDirection { Left, Right, Top, Bottom }
public enum RepeatMode { Straight, HalfWidth, HalfHeight }
public sealed record GraphCrop(int X, int Y, int Width, int Height, string InputHash);

/// <summary>Pixel-exact graph image operations. Coordinates include the clicked boundary pixel.</summary>
public static class GraphImageOperations
{
    public static (int X,int Y)? FindPixel(int width,int height,double x,double y,double displayedWidth,double displayedHeight)
    {
        if(width<1 || height<1 || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(displayedWidth) || !double.IsFinite(displayedHeight) || displayedWidth<=0 || displayedHeight<=0 || x<0 || y<0 || x>=displayedWidth || y>=displayedHeight) return null;
        return (Math.Min(width-1,(int)Math.Floor(x*width/displayedWidth)),Math.Min(height-1,(int)Math.Floor(y*height/displayedHeight)));
    }
    public static GraphCrop Cut(GraphCrop rect, CropDirection direction, int x, int y)
    {
        if (!Enum.IsDefined(direction) || x < 0 || y < 0 || x >= rect.Width || y >= rect.Height) throw new ArgumentOutOfRangeException(nameof(x));
        return direction switch {
            CropDirection.Left => rect with { X=rect.X+x, Width=rect.Width-x },
            CropDirection.Right => rect with { Width=x+1 },
            CropDirection.Top => rect with { Y=rect.Y+y, Height=rect.Height-y },
            CropDirection.Bottom => rect with { Height=y+1 },
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
    }
    public static BitmapSource Crop(BitmapSource input, GraphCrop rect)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width < 1 || rect.Height < 1 || (long)rect.X+rect.Width > input.PixelWidth || (long)rect.Y+rect.Height > input.PixelHeight) throw new ArgumentException("Invalid crop rectangle.");
        var image=new CroppedBitmap(input,new Int32Rect(rect.X,rect.Y,rect.Width,rect.Height)); image.Freeze(); return image;
    }
    public static BitmapSource Repeat(BitmapSource source, int columns, int rows, RepeatMode mode)
    {
        if (columns < 1 || rows < 1 || !Enum.IsDefined(mode)) throw new ArgumentException("Invalid repeat settings.");
        long width=(long)source.PixelWidth*columns, height=(long)source.PixelHeight*rows;
        if (width > int.MaxValue || height > int.MaxValue || width * height > DesignProcessor.MaxPixels) throw new ArgumentException("Repeated image exceeds the 4 million pixel limit.");
        int w=(int)width,h=(int)height,sw=source.PixelWidth,sh=source.PixelHeight, stride=checked(sw*4); var original=new byte[checked(stride*sh)];
        new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0).CopyPixels(original,stride,0); var output=new byte[checked(w*h*4)];
        for(int y=0;y<h;y++) for(int x=0;x<w;x++)
        {
            int sx=x%sw, sy=y%sh;
            if (mode==RepeatMode.HalfWidth && (y/sh)%2==1) sx=(x-sw/2%sw+sw)%sw;
            if (mode==RepeatMode.HalfHeight && (x/sw)%2==1) sy=(y-sh/2%sh+sh)%sh;
            Buffer.BlockCopy(original,(sy*sw+sx)*4,output,(y*w+x)*4,4);
        }
        var image=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,output,w*4); image.Freeze(); return image;
    }
}
