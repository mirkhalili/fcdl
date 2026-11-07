using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>View-only operations; the underlying machine bitmap always remains binary.</summary>
public static class CompositionView
{
    public static BitmapSource Fabric(MachineComposition map)
    {
        var cropped = new CroppedBitmap(map.Image, new Int32Rect(map.FabricStart, 0, map.FabricWidth, map.Image.PixelHeight));
        cropped.Freeze(); return cropped;
    }
    public static BitmapSource RedPreview(BitmapSource binary)
    {
        var values = DesignAnalysis.ReadBinary(binary); var pixels = new byte[checked(values.Length * 4)];
        for (int i = 0; i < values.Length; i++)
        {
            byte other = values[i] == 0 ? (byte)0 : (byte)255;
            pixels[i * 4] = pixels[i * 4 + 1] = other; pixels[i * 4 + 2] = 255; pixels[i * 4 + 3] = 255;
        }
        var image = BitmapSource.Create(binary.PixelWidth, binary.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, binary.PixelWidth * 4); image.Freeze(); return image;
    }
    public static (double Looseness, double Tightness) Extremes(WarpAnalysis analysis, int start, int length)
    {
        if (start < 0 || length < 1 || start + length > analysis.DeviationPercent.Length) throw new ArgumentOutOfRangeException(nameof(start));
        var values = analysis.DeviationPercent.Skip(start).Take(length);
        return (Math.Max(0, -values.Min()), Math.Max(0, values.Max()));
    }
}
