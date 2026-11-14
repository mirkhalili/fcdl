using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed record FabricOverview(BitmapSource Image, string[] RowWefts, int OriginalWidth, int OriginalHeight)
{
    public double ScaleX => (double)OriginalWidth / Image.PixelWidth;
    public double ScaleY => (double)OriginalHeight / Image.PixelHeight;
    /// <summary>Bounded representative samples from the entire fabric, across the full extent.</summary>
    public static FabricOverview Create(BitmapSource fabric, IReadOnlyList<string>? rows, int weave)
    {
        int width = fabric.PixelWidth, height = fabric.PixelHeight;
        if (rows is { Count: > 0 } && rows.Count != height) throw new ArgumentException("Weft rows do not match the fabric.");
        var source = DesignAnalysis.ReadBinary(fabric); int w = Math.Min(Fabric3DWindow.MaxThreads, width), h = Math.Min(Fabric3DWindow.MaxThreads, height);
        var pixels = new byte[w * h]; var sampled = rows is { Count: > 0 } ? new string[h] : Array.Empty<string>();
        for (int y = 0; y < h; y++)
        {
            int sy = (int)(((2L * y + 1) * height) / (2L * h));
            if (sampled.Length > 0) sampled[y] = rows![sy];
            for (int x = 0; x < w; x++)
            {
                int sx = (int)(((2L * x + 1) * width) / (2L * w));
                pixels[y * w + x] = FabricSimulation.WarpOnTop(source, width, sx, sy, weave) ? (byte)255 : (byte)0;
            }
        }
        var image = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, pixels, w); image.Freeze(); return new FabricOverview(image, sampled, width, height);
    }
}
