using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Committed stage results. Replacing an upstream stage invalidates every downstream stage.</summary>
public sealed class WorkflowSession
{
    public BitmapSource Source { get; }
    public BitmapSource? Resized { get; private set; }
    public PaletteResult? Palette { get; private set; }
    public BitmapSource? ColorPreview { get; private set; }
    public int? AppliedColorLimit { get; private set; }
    public WeavePattern?[] Patterns { get; private set; } = Array.Empty<WeavePattern?>();
    public BitmapSource? PatternPreview { get; private set; }
    // 0=base weave, 1=black, 2=white. Immutable-by-replacement for undo snapshots.
    public byte[]? PixelOverrides { get; private set; }

    public WorkflowSession(BitmapSource source) => Source = source;
    public void ApplySize(int width, int height)
    {
        // Commit only after successful processing so failures never discard previous work.
        var image = DesignProcessor.Resize(Source, width, height);
        Resized = image; Palette = null; ColorPreview = null; AppliedColorLimit = null;
        Patterns = Array.Empty<WeavePattern?>(); PatternPreview = null; PixelOverrides = null;
    }
    public void ApplyColors(int maximum)
    {
        var input = Resized ?? throw new InvalidOperationException("Apply size first.");
        var palette = DesignProcessor.Prepare(input, input.PixelWidth, input.PixelHeight, maximum);
        var patterns = new WeavePattern?[palette.Colors.Length];
        var preview = DesignProcessor.Render(palette, patterns);
        Palette = palette; Patterns = patterns; ColorPreview = preview; PatternPreview = preview; AppliedColorLimit = maximum; PixelOverrides = null;
    }
    public void MergeColors(int source, int target)
    {
        var palette = Palette ?? throw new InvalidOperationException("Apply colors first.");
        var next = DesignProcessor.MergeColors(palette, source, target);
        var patterns = Patterns.Where((_, i) => i != source).ToArray();
        var colorPreview = DesignProcessor.Render(next, new WeavePattern?[next.Colors.Length]);
        var patternPreview = ApplyOverrides(DesignProcessor.Render(next, patterns), PixelOverrides);
        Palette = next; Patterns = patterns; ColorPreview = colorPreview; PatternPreview = patternPreview;
    }
    public void AssignPattern(int index, WeavePattern pattern)
    {
        var palette = Palette ?? throw new InvalidOperationException("Apply colors first.");
        if ((uint)index >= (uint)Patterns.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var next = (WeavePattern?[])Patterns.Clone(); next[index] = pattern;
        var preview = ApplyOverrides(DesignProcessor.Render(palette, next), PixelOverrides);
        Patterns = next; PatternPreview = preview;
    }
    internal static WorkflowSession Restore(BitmapSource source, BitmapSource? resized, PaletteResult? palette, int? limit, WeavePattern?[] patterns, byte[]? overrides = null)
    {
        var session = new WorkflowSession(source) { Resized = resized, Palette = palette, AppliedColorLimit = limit, Patterns = patterns, PixelOverrides = overrides };
        if (palette is not null)
        {
            session.ColorPreview = DesignProcessor.Render(palette, new WeavePattern?[palette.Colors.Length]);
            session.PatternPreview = ApplyOverrides(DesignProcessor.Render(palette, patterns), overrides);
        }
        return session;
    }
    private static BitmapSource ApplyOverrides(BitmapSource image, byte[]? overrides)
    {
        if (overrides is null) return image;
        if (overrides.Length != image.PixelWidth * image.PixelHeight || overrides.Any(v => v > 2)) throw new ArgumentException("Invalid pixel overrides.");
        int stride = image.PixelWidth * 4; byte[] bytes = new byte[stride * image.PixelHeight]; image.CopyPixels(bytes, stride, 0);
        for (int i = 0; i < overrides.Length; i++) if (overrides[i] != 0) { byte v = overrides[i] == 1 ? (byte)0 : (byte)255; bytes[i * 4] = bytes[i * 4 + 1] = bytes[i * 4 + 2] = v; }
        var output = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, bytes, stride); output.Freeze(); return output;
    }
    public void TogglePixel(int x, int y)
    {
        if (!CanSave || Palette is null || PatternPreview is null) throw new InvalidOperationException("Assign every weave before editing pixels.");
        if ((uint)x >= Palette.Width || (uint)y >= Palette.Height) throw new ArgumentOutOfRangeException(nameof(x));
        var current = new byte[4]; PatternPreview.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), current, 4, 0);
        byte v = current[0] == 0 ? (byte)255 : (byte)0;
        var next = PixelOverrides is null ? new byte[Palette.Width * Palette.Height] : (byte[])PixelOverrides.Clone(); next[y * Palette.Width + x] = v == 0 ? (byte)1 : (byte)2;
        var image = new WriteableBitmap(PatternPreview); image.WritePixels(new System.Windows.Int32Rect(x, y, 1, 1), new byte[] {v,v,v,255}, 4, 0); image.Freeze();
        PixelOverrides = next; PatternPreview = image;
    }
    // Stage results are replaced, never modified in place; share them for bounded undo snapshots.
    public WorkflowSession Snapshot() => new(Source) { Resized = Resized, Palette = Palette, ColorPreview = ColorPreview, AppliedColorLimit = AppliedColorLimit, Patterns = Patterns, PatternPreview = PatternPreview, PixelOverrides = PixelOverrides };
    public bool CanSave => Palette is not null && Patterns.Length > 0 && Patterns.All(p => p is not null);
}
