using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace JacquardStudio;
/// <summary>Small binary weave editor; the grid scales uniformly and drag paints the selected value.</summary>
public sealed class WeaveEditor : FrameworkElement
{
    public const int MaxSide = 64;
    private int width = 8, height = 8;
    private byte[] values = Enumerable.Repeat((byte)255, 64).ToArray();
    private bool painting; private byte paintValue;
    public bool IsDirty { get; private set; }
    public int PatternWidth => width;
    public int PatternHeight => height;
    public WeavePattern Snapshot() => new(width, height, (byte[])values.Clone());
    public void SetPattern(WeavePattern pattern, bool dirty = false)
    {
        if (pattern.Width < 1 || pattern.Height < 1 || pattern.Width > MaxSide || pattern.Height > MaxSide || pattern.Values.Length != pattern.Width * pattern.Height || pattern.Values.Any(v => v != 0 && v != 255)) throw new ArgumentException("Editor supports opaque binary patterns up to 64 × 64.");
        width = pattern.Width; height = pattern.Height; values = (byte[])pattern.Values.Clone(); IsDirty = dirty; InvalidateVisual();
    }
    public void Reset(int w, int h)
    {
        if (w < 1 || h < 1 || w > MaxSide || h > MaxSide) throw new ArgumentOutOfRangeException(nameof(w));
        SetPattern(new WeavePattern(w, h, Enumerable.Repeat((byte)255, w * h).ToArray()), true);
    }
    public void MarkSaved() => IsDirty = false;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double size = Math.Min(ActualWidth / width, ActualHeight / height);
        if (size <= 0) return;
        double x0 = (ActualWidth - size * width) / 2, y0 = (ActualHeight - size * height) / 2;
        var pen = new Pen(Brushes.LightSlateGray, Math.Min(0.6, size / 8));
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            dc.DrawRectangle(values[y * width + x] == 0 ? Brushes.Black : Brushes.White, pen, new Rect(x0 + x * size, y0 + y * size, size, size));
    }
    private int Hit(Point point)
    {
        double size = Math.Min(ActualWidth / width, ActualHeight / height); if (size <= 0) return -1;
        double x0 = (ActualWidth - size * width) / 2, y0 = (ActualHeight - size * height) / 2;
        int x = (int)Math.Floor((point.X - x0) / size), y = (int)Math.Floor((point.Y - y0) / size);
        return x >= 0 && y >= 0 && x < width && y < height ? y * width + x : -1;
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e); int i = Hit(e.GetPosition(this)); if (i < 0) return;
        paintValue = values[i] == 0 ? (byte)255 : (byte)0; painting = true; CaptureMouse(); Paint(i); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (painting && e.LeftButton == MouseButtonState.Pressed) Paint(Hit(e.GetPosition(this))); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); painting = false; ReleaseMouseCapture(); }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); painting = false; }
    private void Paint(int index) { if (index >= 0 && values[index] != paintValue) { values[index] = paintValue; IsDirty = true; InvalidateVisual(); } }
}
