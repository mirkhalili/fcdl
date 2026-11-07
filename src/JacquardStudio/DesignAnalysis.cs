using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;
public enum FloatDirection { Warp, Weft }
public readonly record struct FloatRun(int X, int Y, int Length, bool Black, FloatDirection Direction);
public sealed record WarpAnalysis(int[] Counts, double Mean, double[] DeviationPercent);

/// <summary>Pixel-based analytical indicators, not a physical tension measurement.</summary>
public static class DesignAnalysis
{
    public static byte[] ReadBinary(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var bgra = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)]; converted.CopyPixels(bgra, bitmap.PixelWidth * 4, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight];
        for (int i = 0; i < pixels.Length; i++)
        {
            int j = i * 4; byte v = bgra[j];
            if (bgra[j + 3] != 255 || bgra[j + 1] != v || bgra[j + 2] != v || (v != 0 && v != 255)) throw new ArgumentException("Completed pure binary pattern required.");
            pixels[i] = v;
        }
        return pixels;
    }
    public static WarpAnalysis Analyze(byte[] pixels, int width, int height)
    {
        Validate(pixels, width, height);
        int[] counts = new int[width]; for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) if (pixels[y * width + x] == 0) counts[x]++;
        double mean = counts.Average();
        var deviations = counts.Select(n => mean == 0 ? 0 : 100d * (n - mean) / mean).ToArray();
        return new WarpAnalysis(counts, mean, deviations);
    }
    public static BitmapSource Heatmap(WarpAnalysis result, int height)
    {
        int w = result.Counts.Length; double max = result.DeviationPercent.Select(Math.Abs).DefaultIfEmpty(0).Max();
        var row = new byte[w * 4];
        for (int x = 0; x < w; x++)
        {
            double n = max == 0 ? 0 : Math.Abs(result.DeviationPercent[x]) / max;
            // Neutral gray-white; red = positive, blue = negative. Balanced deviations = neutral.
            byte other = (byte)Math.Round(240 - 170 * n);
            row[x * 4] = result.DeviationPercent[x] < 0 ? (byte)240 : other;
            row[x * 4 + 1] = other;
            row[x * 4 + 2] = result.DeviationPercent[x] > 0 ? (byte)240 : other;
            row[x * 4 + 3] = 255;
        }
        byte[] pixels = new byte[checked(w * height * 4)]; for (int y = 0; y < height; y++) Buffer.BlockCopy(row, 0, pixels, y * row.Length, row.Length);
        var image = BitmapSource.Create(w, height, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4); image.Freeze(); return image;
    }
    public static List<FloatRun> FindFloats(byte[] pixels, int width, int height, int minimum, FloatDirection direction)
    {
        Validate(pixels, width, height); if (minimum < 1) throw new ArgumentOutOfRangeException(nameof(minimum));
        int lines = direction == FloatDirection.Warp ? width : height, length = direction == FloatDirection.Warp ? height : width;
        var runs = new List<FloatRun>();
        for (int line = 0; line < lines; line++)
        {
            int start = 0;
            while (start < length)
            {
                byte value = direction == FloatDirection.Warp ? pixels[start * width + line] : pixels[line * width + start];
                int end = start + 1;
                while (end < length && (direction == FloatDirection.Warp ? pixels[end * width + line] : pixels[line * width + end]) == value) end++;
                if (end - start >= minimum) runs.Add(new FloatRun(direction == FloatDirection.Warp ? line : start, direction == FloatDirection.Warp ? start : line, end - start, value == 0, direction));
                start = end;
            }
        }
        return runs;
    }
    private static void Validate(byte[] pixels, int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > DesignProcessor.MaxPixels || pixels.Length != (long)width * height || pixels.Any(v => v != 0 && v != 255)) throw new ArgumentException("Invalid binary design.");
    }
}
