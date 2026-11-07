namespace JacquardStudio;
/// <summary>Maps image-local WPF coordinates (after zoom and scroll transforms) to original palette pixels.</summary>
public static class PreviewHitTest
{
    public static (int X, int Y, int ColorIndex)? Find(PaletteResult palette, double x, double y, double displayedWidth, double displayedHeight)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(displayedWidth) || !double.IsFinite(displayedHeight) || displayedWidth <= 0 || displayedHeight <= 0 || x < 0 || y < 0 || x >= displayedWidth || y >= displayedHeight) return null;
        int px = (int)Math.Floor(x * palette.Width / displayedWidth), py = (int)Math.Floor(y * palette.Height / displayedHeight);
        return (px, py, palette.Indices[py * palette.Width + px]);
    }
}
