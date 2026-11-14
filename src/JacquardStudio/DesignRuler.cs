using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace JacquardStudio;

/// <summary>Scroll- and zoom-aware pixel ruler used by the design workspace.</summary>
public sealed class DesignRuler : FrameworkElement
{
    public bool Horizontal { get; set; }
    public double ZoomFactor { get; set; } = 1;
    public double ScrollOffset { get; set; }
    public double ImageOrigin { get; set; }
    public int ImageLength { get; set; }
    public int BaseStep { get; set; } = 50;
    public Brush BackgroundBrush { get; set; } = Brushes.Transparent;
    public Brush TickBrush { get; set; } = Brushes.Gray;
    public Brush TextBrush { get; set; } = Brushes.White;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(BackgroundBrush, null, new Rect(RenderSize));
        if (ZoomFactor <= 0 || ImageLength <= 0 || BaseStep <= 0) return;

        const double minorTick = 4;
        double majorStep = BaseStep * 10.0;
        while (majorStep * ZoomFactor < 64 && majorStep < 1_000_000) majorStep *= 2;
        double minorStep = majorStep / 10;
        double visiblePixelsStart = Math.Max(0, (ScrollOffset - ImageOrigin) / ZoomFactor);
        double visiblePixelsEnd = Math.Min(ImageLength, (ScrollOffset + (Horizontal ? ActualWidth : ActualHeight) - ImageOrigin) / ZoomFactor);
        if (visiblePixelsEnd < 0 || visiblePixelsStart > ImageLength) return;
        int first = Math.Max(0, (int)(Math.Floor(visiblePixelsStart / minorStep) * minorStep));
        int last = Math.Min(ImageLength, (int)Math.Ceiling(visiblePixelsEnd / minorStep) * (int)minorStep);
        var pen = new Pen(TickBrush, 1);
        for (int value = first; value <= last; value += Math.Max(1, (int)minorStep))
        {
            double position = ImageOrigin + value * ZoomFactor - ScrollOffset;
            bool major = Math.Abs(value % majorStep) < .001;
            if (Horizontal)
            {
                double bottom = ActualHeight;
                dc.DrawLine(pen, new Point(position, bottom), new Point(position, bottom - (major ? 12 : minorTick)));
                if (major)
                {
                    var label = Label(value);
                    dc.DrawText(label, new Point(position + 3, 1));
                }
            }
            else
            {
                double right = ActualWidth;
                dc.DrawLine(pen, new Point(right, position), new Point(right - (major ? 12 : minorTick), position));
                if (major)
                {
                    var label = Label(value);
                    dc.DrawText(label, new Point(1, position + 1));
                }
            }
        }
        dc.DrawLine(pen, Horizontal ? new Point(0, ActualHeight - .5) : new Point(ActualWidth - .5, 0),
                         Horizontal ? new Point(ActualWidth, ActualHeight - .5) : new Point(ActualWidth - .5, ActualHeight));
    }

    private FormattedText Label(int value)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return new FormattedText(value.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9, TextBrush, dpi);
    }
}
