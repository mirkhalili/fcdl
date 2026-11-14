using System.Windows;
using System.Windows.Media;

namespace JacquardStudio;

/// <summary>View-only pixel grid. Draw only visible lines, and only when they are distinguishable.</summary>
public sealed class PixelGridOverlay : FrameworkElement
{
    public int PixelsWide { get; set; }
    public int PixelsHigh { get; set; }
    public double Zoom { get; set; } = 1;
    public int ConfiguredStep { get; set; }
    public Brush GridBrush { get; set; } = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0));
    public Rect VisibleImage { get; set; }
    public int GridStep()
    {
        if (PixelsWide <= 0 || PixelsHigh <= 0 || ActualWidth <= 0 || ActualHeight <= 0 || Zoom <= 0) return 1;
        double onScreen = Math.Min(ActualWidth / PixelsWide, ActualHeight / PixelsHigh) * Zoom;
        int step = 1;
        while (step < 4194304 && onScreen * step < 4) step *= 2;
        return step;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (PixelsWide <= 0 || PixelsHigh <= 0 || ActualWidth <= 0 || ActualHeight <= 0 || Zoom <= 0) return;
        double cellX = ActualWidth / PixelsWide, cellY = ActualHeight / PixelsHigh;
        // Aggregate dense pixels into readable power-of-two grid spacing.
        if (VisibleImage.IsEmpty) return;
        int step = ConfiguredStep > 0 ? Math.Max(ConfiguredStep, GridStep()) : GridStep();
        var visible = Rect.Intersect(new Rect(0, 0, ActualWidth, ActualHeight), VisibleImage);
        if (visible.IsEmpty) return;
        var pen = new Pen(GridBrush, 1 / Zoom);
        dc.PushClip(new RectangleGeometry(visible));
        int minX = Math.Max(0, (int)Math.Floor(visible.Left / cellX)), maxX = Math.Min(PixelsWide, (int)Math.Ceiling(visible.Right / cellX));
        int minY = Math.Max(0, (int)Math.Floor(visible.Top / cellY)), maxY = Math.Min(PixelsHigh, (int)Math.Ceiling(visible.Bottom / cellY));
        for (int x = (int)(((long)minX + step - 1) / step * step); x <= maxX; x += step) dc.DrawLine(pen, new Point(x * cellX, visible.Top), new Point(x * cellX, visible.Bottom));
        for (int y = (int)(((long)minY + step - 1) / step * step); y <= maxY; y += step) dc.DrawLine(pen, new Point(visible.Left, y * cellY), new Point(visible.Right, y * cellY));
        dc.Pop();
    }
}
