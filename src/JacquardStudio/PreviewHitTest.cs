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
    /// <summary>Indices in the inclusive rectangle, in image-local coordinates; endpoints may lie outside the viewport.</summary>
    public static int[] ColorsInRectangle(PaletteResult palette, double x1, double y1, double x2, double y2, double displayWidth, double displayHeight)
    {
        if (!double.IsFinite(x1) || !double.IsFinite(y1) || !double.IsFinite(x2) || !double.IsFinite(y2) || !double.IsFinite(displayWidth) || !double.IsFinite(displayHeight) || displayWidth <= 0 || displayHeight <= 0) return Array.Empty<int>();
        if (Math.Max(x1,x2) < 0 || Math.Max(y1,y2) < 0 || Math.Min(x1,x2) >= displayWidth || Math.Min(y1,y2) >= displayHeight) return Array.Empty<int>();
        int left = Math.Clamp((int)Math.Floor(Math.Min(x1,x2) * palette.Width / displayWidth), 0, palette.Width-1), right = Math.Clamp((int)Math.Floor(Math.Max(x1,x2) * palette.Width / displayWidth), 0, palette.Width-1);
        int top = Math.Clamp((int)Math.Floor(Math.Min(y1,y2) * palette.Height / displayHeight), 0, palette.Height-1), bottom = Math.Clamp((int)Math.Floor(Math.Max(y1,y2) * palette.Height / displayHeight), 0, palette.Height-1);
        var found = new bool[palette.Colors.Length]; for (int y=top;y<=bottom;y++) for (int x=left;x<=right;x++) found[palette.Indices[y*palette.Width+x]] = true;
        return Enumerable.Range(0,found.Length).Where(i => found[i]).ToArray();
    }
}
