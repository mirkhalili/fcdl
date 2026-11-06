using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed record PaletteResult(int Width, int Height, int SourceColorCount, int[] Colors, byte[] Indices, int[] Counts);
public sealed record WeavePattern(int Width, int Height, byte[] Values);

/// <summary>Image preparation only. Output is not a machine file or a validated weaving plan.</summary>
public static class DesignProcessor
{
    public const int MaxPixels = 4_000_000;
    public const int MaxPatternPixels = 262_144;

    private sealed record Sample(int Color, int Count);
    private sealed class Bucket
    {
        public int Count; public long R, G, B;
        public void Add(int color) { Count++; R += (color >> 16) & 255; G += (color >> 8) & 255; B += color & 255; }
        public Sample Sample() => new(((int)(R / Count) << 16) | ((int)(G / Count) << 8) | (int)(B / Count), Count);
    }

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
        byte[] pixels = GetPixels(source, MaxPixels);
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 255) throw new InvalidDataException("Transparent pixels are not supported; flatten the image first.");
        int stride = checked(width * 4);
        byte[] output = new byte[checked(stride * height)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int sy = (int)((long)y * source.PixelHeight / height);
            int sx = (int)((long)x * source.PixelWidth / width);
            Buffer.BlockCopy(pixels, (sy * source.PixelWidth + sx) * 4, output, (y * width + x) * 4, 4);
        }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, output, stride);
        result.Freeze(); return result;
    }

    public static PaletteResult Prepare(BitmapSource source, int width, int height, int requestedColors)
    {
        if (width < 1 || height < 1 || (long)width * height > MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(width), "Output size must be positive and at most 4,000,000 pixels.");
        if (requestedColors is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(requestedColors));
        byte[] original = GetPixels(source, MaxPixels);
        int total = checked(width * height);
        int[] scaled = new int[total];
        var unique = new HashSet<int>();
        var histogram = new Bucket?[32768];
        var exact = new Dictionary<int, int>();
        for (int y = 0; y < height; y++)
        {
            int sy = (int)((long)y * source.PixelHeight / height);
            for (int x = 0; x < width; x++)
            {
                int sx = (int)((long)x * source.PixelWidth / width);
                int o = (sy * source.PixelWidth + sx) * 4;
                if (original[o + 3] != 255) throw new InvalidDataException("Transparent pixels are not supported; flatten the image first.");
                int color = (original[o + 2] << 16) | (original[o + 1] << 8) | original[o];
                scaled[y * width + x] = color;
                unique.Add(color);
                if (unique.Count <= 50) exact[color] = exact.GetValueOrDefault(color) + 1;
                int bin = ((color >> 19) << 10) | (((color >> 11) & 31) << 5) | ((color >> 3) & 31);
                (histogram[bin] ??= new Bucket()).Add(color);
            }
        }
        // For <=50 exact colors retain them exactly when possible; for larger sets use 5-bit histogram buckets.
        List<Sample> samples = unique.Count <= 50
            ? exact.OrderBy(p => p.Key).Select(p => new Sample(p.Key, p.Value)).ToList()
            : histogram.Where(b => b is not null).Select(b => b!.Sample()).ToList();
        var boxes = new List<List<Sample>> { samples };
        while (boxes.Count < requestedColors)
        {
            int best = -1, bestRange = -1, channel = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2) continue;
                for (int c = 0; c < 3; c++)
                {
                    int shift = c * 8;
                    int range = boxes[i].Max(s => (s.Color >> shift) & 255) - boxes[i].Min(s => (s.Color >> shift) & 255);
                    if (range > bestRange) { best = i; bestRange = range; channel = shift; }
                }
            }
            if (best < 0 || bestRange == 0) break;
            var sorted = boxes[best].OrderBy(s => (s.Color >> channel) & 255).ThenBy(s => s.Color).ToList();
            long half = sorted.Sum(s => (long)s.Count) / 2, acc = 0; int split = 0;
            while (split < sorted.Count - 1 && acc < half) acc += sorted[split++].Count;
            split = Math.Clamp(split, 1, sorted.Count - 1);
            boxes[best] = sorted.Take(split).ToList();
            boxes.Add(sorted.Skip(split).ToList());
        }
        int[] palette = boxes.Select(box =>
        {
            long n = box.Sum(s => (long)s.Count);
            int r = (int)(box.Sum(s => (long)((s.Color >> 16) & 255) * s.Count) / n);
            int g = (int)(box.Sum(s => (long)((s.Color >> 8) & 255) * s.Count) / n);
            int b = (int)(box.Sum(s => (long)(s.Color & 255) * s.Count) / n);
            return (r << 16) | (g << 8) | b;
        }).Distinct().OrderBy(c => c).ToArray();
        // Cache mappings by exact color for small palettes or by 5-bit histogram bucket otherwise.
        var lookup = new Dictionary<int, byte>();
        var binColors = histogram.Select(b => b?.Sample().Color ?? 0).ToArray();
        var indices = new byte[total]; var counts = new int[palette.Length];
        for (int i = 0; i < total; i++)
        {
            int color = scaled[i];
            int key = unique.Count <= 50 ? color : ((color >> 19) << 10) | (((color >> 11) & 31) << 5) | ((color >> 3) & 31);
            if (!lookup.TryGetValue(key, out byte idx))
            {
                int candidate = unique.Count <= 50 ? color : binColors[key];
                idx = (byte)Nearest(candidate, palette); lookup.Add(key, idx);
            }
            indices[i] = idx; counts[idx]++;
        }
        return new PaletteResult(width, height, unique.Count, palette, indices, counts);
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
