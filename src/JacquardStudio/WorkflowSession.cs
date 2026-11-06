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
    public void AssignPattern(int index, WeavePattern pattern)
    {
        var palette = Palette ?? throw new InvalidOperationException("Apply colors first.");
        if ((uint)index >= (uint)Patterns.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var next = (WeavePattern?[])Patterns.Clone(); next[index] = pattern;
        var preview = DesignProcessor.Render(palette, next);
        Patterns = next; PatternPreview = preview;
    }
    public bool CanSave => Palette is not null && Patterns.Length > 0 && Patterns.All(p => p is not null);
}
