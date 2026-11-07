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

    public WorkflowSession(BitmapSource source) => Source = source;
    public void ApplySize(int width, int height)
    {
        // Commit only after successful processing so failures never discard previous work.
        var image = DesignProcessor.Resize(Source, width, height);
        Resized = image; Palette = null; ColorPreview = null; AppliedColorLimit = null;
        Patterns = Array.Empty<WeavePattern?>(); PatternPreview = null;
    }
    public void ApplyColors(int maximum)
    {
        var input = Resized ?? throw new InvalidOperationException("Apply size first.");
        var palette = DesignProcessor.Prepare(input, input.PixelWidth, input.PixelHeight, maximum);
        var patterns = new WeavePattern?[palette.Colors.Length];
        var preview = DesignProcessor.Render(palette, patterns);
        Palette = palette; Patterns = patterns; ColorPreview = preview; PatternPreview = preview; AppliedColorLimit = maximum;
    }
    public void MergeColors(int source, int target)
    {
        var palette = Palette ?? throw new InvalidOperationException("Apply colors first.");
        var next = DesignProcessor.MergeColors(palette, source, target);
        var patterns = Patterns.Where((_, i) => i != source).ToArray();
        var colorPreview = DesignProcessor.Render(next, new WeavePattern?[next.Colors.Length]);
        var patternPreview = DesignProcessor.Render(next, patterns);
        Palette = next; Patterns = patterns; ColorPreview = colorPreview; PatternPreview = patternPreview;
    }
    public void AssignPattern(int index, WeavePattern pattern)
    {
        var palette = Palette ?? throw new InvalidOperationException("Apply colors first.");
        if ((uint)index >= (uint)Patterns.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var next = (WeavePattern?[])Patterns.Clone(); next[index] = pattern;
        var preview = DesignProcessor.Render(palette, next);
        Patterns = next; PatternPreview = preview;
    }
    internal static WorkflowSession Restore(BitmapSource source, BitmapSource? resized, PaletteResult? palette, int? limit, WeavePattern?[] patterns)
    {
        var session = new WorkflowSession(source) { Resized = resized, Palette = palette, AppliedColorLimit = limit, Patterns = patterns };
        if (palette is not null)
        {
            session.ColorPreview = DesignProcessor.Render(palette, new WeavePattern?[palette.Colors.Length]);
            session.PatternPreview = DesignProcessor.Render(palette, patterns);
        }
        return session;
    }
    public bool CanSave => Palette is not null && Patterns.Length > 0 && Patterns.All(p => p is not null);
}
