using System.Windows;
using System.Windows.Media;

namespace JacquardStudio;

/// <summary>View-only pixel grid. Draw only visible lines, and only when they are distinguishable.</summary>
public sealed class PixelGridOverlay : FrameworkElement
{
    public int PixelsWide { get; set; }
    public int PixelsHigh { get; set; }
    public double Zoom { get; set; } = 1;
    public Rect VisibleImage { get; set; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (PixelsWide <= 0 || PixelsHigh <= 0 || ActualWidth <= 0 || ActualHeight <= 0 || Zoom <= 0) return;
        double cellX = ActualWidth / PixelsWide, cellY = ActualHeight / PixelsHigh;
        // Avoid a solid field of grid lines when pixels are smaller than four screen DIPs.
        if (Math.Min(cellX, cellY) * Zoom < 4 || VisibleImage.IsEmpty) return;
        var visible = Rect.Intersect(new Rect(0, 0, ActualWidth, ActualHeight), VisibleImage);
        if (visible.IsEmpty) return;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 1 / Zoom);
        dc.PushClip(new RectangleGeometry(visible));
        int minX = Math.Max(0, (int)Math.Floor(visible.Left / cellX)), maxX = Math.Min(PixelsWide, (int)Math.Ceiling(visible.Right / cellX));
        int minY = Math.Max(0, (int)Math.Floor(visible.Top / cellY)), maxY = Math.Min(PixelsHigh, (int)Math.Ceiling(visible.Bottom / cellY));
        for (int x = minX; x <= maxX; x++) dc.DrawLine(pen, new Point(x * cellX, visible.Top), new Point(x * cellX, visible.Bottom));
        for (int y = minY; y <= maxY; y++) dc.DrawLine(pen, new Point(visible.Left, y * cellY), new Point(visible.Right, y * cellY));
        dc.Pop();
    }
}
