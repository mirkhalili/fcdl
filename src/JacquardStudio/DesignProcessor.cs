using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed record PaletteResult(int Width, int Height, int SourceColorCount, int[] Colors, byte[] Indices, int[] Counts);
public sealed record WeavePattern(int Width, int Height, byte[] Values, string[]? WeftColors = null);

/// <summary>Image preparation only. Output is not a machine file or a validated weaving plan.</summary>
public static class DesignProcessor
{
    public const int MaxPixels = 4_000_000;
    public const int MaxPatternPixels = 262_144;

    private static byte[] GetPixels(BitmapSource source, int maxPixels)
    {
        if (source.PixelWidth <= 0 || source.PixelHeight <= 0 || (long)source.PixelWidth * source.PixelHeight > maxPixels)
            throw new InvalidDataException($"Image exceeds the processing limit of {maxPixels:N0} pixels.");
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = checked(source.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * source.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    // Resize is a separate, lossless-in-color pipeline stage. Pixel centers use nearest-neighbor.
    public static BitmapSource Resize(BitmapSource source, int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(width), "Output size must be positive and at most 4,000,000 pixels.");
        byte[] pixels = GetPixels(source, (int)ImageLoader.MaxPixels);
        int stride = checked(width * 4);
        byte[] output = new byte[checked(stride * height)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int sy = (int)(((2L * y + 1) * source.PixelHeight) / (2L * height));
            int sx = (int)(((2L * x + 1) * source.PixelWidth) / (2L * width));
            int input = (sy * source.PixelWidth + sx) * 4, dest = (y * width + x) * 4;
            if (pixels[input + 3] < 128) { output[dest] = output[dest + 1] = output[dest + 2] = 255; }
            else { output[dest] = pixels[input]; output[dest + 1] = pixels[input + 1]; output[dest + 2] = pixels[input + 2]; }
            output[dest + 3] = 255;
        }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, output, stride);
        result.Freeze(); return result;
    }

    public static PaletteResult Prepare(BitmapSource source, int width, int height, int requestedColors)
    {
        if (width < 1 || height < 1 || (long)width * height > MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(width), "Output size must be positive and at most 4,000,000 pixels.");
        if (requestedColors is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(requestedColors));
        byte[] original = GetPixels(source, (int)ImageLoader.MaxPixels);
        int total = checked(width * height);
        var sparse = total <= 262_144 ? new Dictionary<int, int>() : null;
        int[]? frequency = sparse is null ? new int[1 << 24] : null; int unique = 0;
        int ColorAt(int x, int y)
        {
            int sy = (int)(((2L * y + 1) * source.PixelHeight) / (2L * height)), sx = (int)(((2L * x + 1) * source.PixelWidth) / (2L * width));
            int o = (sy * source.PixelWidth + sx) * 4;
            return original[o + 3] < 128 ? 0xffffff : (original[o + 2] << 16) | (original[o + 1] << 8) | original[o];
        }
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int c = ColorAt(x, y);
            if (sparse is not null) { int n = sparse.GetValueOrDefault(c); if (n == 0) unique++; sparse[c] = n + 1; }
            else if (frequency![c]++ == 0) unique++;
        }
        // Keep exact colors with highest pixel counts. Ties deterministically prefer the lower RGB value.
        var top = new PriorityQueue<int, (int count, int inverseColor)>();
        void Consider(int color, int count) { top.Enqueue(color, (count, -color)); if (top.Count > requestedColors) top.Dequeue(); }
        if (sparse is not null) { foreach (var pair in sparse) Consider(pair.Key, pair.Value); }
        else for (int c = 0; c < frequency!.Length; c++) if (frequency[c] > 0) Consider(c, frequency[c]);
        int[] palette = new int[top.Count]; for (int i = 0; i < palette.Length; i++) palette[i] = top.Dequeue(); Array.Sort(palette);
        // Reuse frequency as an exact-color -> palette index cache to avoid another unbounded dictionary.
        var indices = new byte[total]; var counts = new int[palette.Length];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int color = ColorAt(x, y); int mapped = sparse is null ? frequency![color] : sparse[color];
            if (mapped > 0) { mapped = -Nearest(color, palette) - 1; if (sparse is null) frequency![color] = mapped; else sparse[color] = mapped; }
            int index = -mapped - 1; indices[y * width + x] = (byte)index; counts[index]++;
        }
        return new PaletteResult(width, height, unique, palette, indices, counts);
    }

    /// <summary>View-only highlight: keep the chosen color intact and mute all other regions.</summary>
    public static BitmapSource RenderHighlight(PaletteResult design, int selected)
    {
        if ((uint)selected >= (uint)design.Colors.Length || design.Indices.Length != (long)design.Width * design.Height) throw new ArgumentException("Invalid highlighted color.");
        int stride = checked(design.Width * 4); var pixels = new byte[checked(stride * design.Height)];
        for (int i = 0; i < design.Indices.Length; i++)
        {
            int index = design.Indices[i]; if ((uint)index >= (uint)design.Colors.Length) throw new ArgumentException("Invalid palette index.");
            int c = design.Colors[index], b = c & 255, g = (c >> 8) & 255, r = (c >> 16) & 255;
            if (index != selected) { r = (r + 3 * 150) / 4; g = (g + 3 * 150) / 4; b = (b + 3 * 150) / 4; }
            pixels[i * 4] = (byte)b; pixels[i * 4 + 1] = (byte)g; pixels[i * 4 + 2] = (byte)r; pixels[i * 4 + 3] = 255;
        }
        var result = BitmapSource.Create(design.Width, design.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride); result.Freeze(); return result;
    }

    /// <summary>Replace all pixels of one palette color with a selected surviving color.</summary>
    public static PaletteResult MergeColors(PaletteResult palette, int source, int target)
    {
        if (palette.Colors.Length < 2 || (uint)source >= (uint)palette.Colors.Length || (uint)target >= (uint)palette.Colors.Length || source == target)
            throw new ArgumentException("Select two different colors; at least one color must remain.");
        var colors = palette.Colors.Where((_, i) => i != source).ToArray();
        int newTarget = target > source ? target - 1 : target;
        var indices = new byte[palette.Indices.Length]; var counts = new int[colors.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            int old = palette.Indices[i];
            int next = old == source ? newTarget : old > source ? old - 1 : old;
            indices[i] = (byte)next; counts[next]++;
        }
        return new PaletteResult(palette.Width, palette.Height, palette.SourceColorCount, colors, indices, counts);
    }
    public static int NearestRemainingColor(PaletteResult palette, int source)
    {
        if (palette.Colors.Length < 2 || (uint)source >= (uint)palette.Colors.Length) throw new ArgumentException("At least two colors are required.");
        int color = palette.Colors[source], best = -1, bestDistance = int.MaxValue;
        for (int i = 0; i < palette.Colors.Length; i++)
        {
            if (i == source) continue;
            int c = palette.Colors[i];
            int r = ((c >> 16) & 255) - ((color >> 16) & 255);
            int g = ((c >> 8) & 255) - ((color >> 8) & 255);
            int b = (c & 255) - (color & 255);
            int distance = r * r + g * g + b * b;
            if (distance < bestDistance) { best = i; bestDistance = distance; }
        }
        return best;
    }

    private static int Nearest(int color, int[] palette)
    {
        int best = 0, distance = int.MaxValue;
        for (int i = 0; i < palette.Length; i++)
        {
            int r = ((color >> 16) & 255) - ((palette[i] >> 16) & 255);
            int g = ((color >> 8) & 255) - ((palette[i] >> 8) & 255);
            int b = (color & 255) - (palette[i] & 255);
            int d = r * r + g * g + b * b;
            if (d < distance) { best = i; distance = d; }
        }
        return best;
    }

    public static WeavePattern ReadPattern(BitmapSource source)
    {
        byte[] pixels = GetPixels(source, MaxPatternPixels);
        byte[] result = new byte[source.PixelWidth * source.PixelHeight];
        for (int i = 0; i < result.Length; i++)
        {
            int p = i * 4;
            if (pixels[p + 3] != 255 || pixels[p] != pixels[p + 1] || pixels[p] != pixels[p + 2] || (pixels[p] != 0 && pixels[p] != 255))
                throw new InvalidDataException("Pattern must be fully opaque and use only pure black and pure white pixels.");
            result[i] = pixels[p];
        }
        return new WeavePattern(source.PixelWidth, source.PixelHeight, result);
    }

    public static BitmapSource Render(PaletteResult design, IReadOnlyList<WeavePattern?> patterns)
    {
        if (patterns.Count != design.Colors.Length) throw new ArgumentException("Pattern count does not match palette.");
        int stride = checked(design.Width * 4);
        byte[] pixels = new byte[checked(stride * design.Height)];
        for (int y = 0; y < design.Height; y++)
        for (int x = 0; x < design.Width; x++)
        {
            int i = y * design.Width + x, color = design.Colors[design.Indices[i]];
            var pattern = patterns[design.Indices[i]];
            if (pattern is not null)
            {
                int gray = pattern.Values[(y % pattern.Height) * pattern.Width + x % pattern.Width];
                color = gray * 0x010101;
            }
            int o = i * 4; pixels[o] = (byte)color; pixels[o + 1] = (byte)(color >> 8); pixels[o + 2] = (byte)(color >> 16); pixels[o + 3] = 255;
        }
        var image = BitmapSource.Create(design.Width, design.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        image.Freeze(); return image;
    }
}
