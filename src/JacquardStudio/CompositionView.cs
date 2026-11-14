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
    public static BitmapSource HeatStrip(WarpAnalysis analysis, MachineComposition map)
    {
        int width = map.Image.PixelWidth;
        if (analysis.Counts.Length != map.FabricWidth || map.FabricStart < 0 || (long)map.FabricStart + map.FabricWidth > width) throw new ArgumentException("Analysis does not match the fabric layout.");
        var strip = DesignAnalysis.Heatmap(analysis, 1); var fabric = new byte[checked(map.FabricWidth * 4)]; strip.CopyPixels(fabric, map.FabricWidth * 4, 0);
        var pixels = new byte[checked(width * 4)];
        for (int x = 0; x < width; x++) { pixels[x * 4] = pixels[x * 4 + 1] = pixels[x * 4 + 2] = 120; pixels[x * 4 + 3] = 255; }
        Buffer.BlockCopy(fabric, 0, pixels, map.FabricStart * 4, fabric.Length);
        var output = BitmapSource.Create(width, 1, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); output.Freeze(); return output;
    }
    public static (double Looseness, double Tightness) Extremes(WarpAnalysis analysis, int start, int length)
    {
        if (start < 0 || length < 1 || start + length > analysis.DeviationPercent.Length) throw new ArgumentOutOfRangeException(nameof(start));
        var values = analysis.DeviationPercent.Skip(start).Take(length);
        return (Math.Max(0, -values.Min()), Math.Max(0, values.Max()));
    }
}
