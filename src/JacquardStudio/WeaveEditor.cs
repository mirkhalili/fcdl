using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace JacquardStudio;
/// <summary>Binary grid editor with bounded per-stroke history and reversible helper operations.</summary>
public sealed class WeaveEditor : FrameworkElement
{
    public const int MaxSide = 64;
    private int width = 8, height = 8;
    private byte[] values = Enumerable.Repeat((byte)255, 64).ToArray();
    private WeavePattern saved = new(8, 8, Enumerable.Repeat((byte)255, 64).ToArray());
    private readonly List<WeavePattern> undo = new(), redo = new();
    private bool painting, gridVisible = true; private byte paintValue;
    public event EventHandler? Changed;
    public bool IsDirty => saved.Width != width || saved.Height != height || !saved.Values.SequenceEqual(values);
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public bool GridVisible { get => gridVisible; set { gridVisible = value; InvalidateVisual(); Changed?.Invoke(this, EventArgs.Empty); } }
    public int PatternWidth => width;
    public int PatternHeight => height;
    public WeavePattern Snapshot() => new(width, height, (byte[])values.Clone());
    private void Restore(WeavePattern p) { width = p.Width; height = p.Height; values = (byte[])p.Values.Clone(); InvalidateVisual(); Changed?.Invoke(this, EventArgs.Empty); }
    private static void Push(List<WeavePattern> stack, WeavePattern p) { stack.Add(p); if (stack.Count > 100) stack.RemoveAt(0); }
    private void BeginEdit() { Push(undo, Snapshot()); redo.Clear(); }
    public void Undo() { if (!CanUndo) return; Push(redo, Snapshot()); var p = undo[^1]; undo.RemoveAt(undo.Count - 1); Restore(p); }
    public void Redo() { if (!CanRedo) return; Push(undo, Snapshot()); var p = redo[^1]; redo.RemoveAt(redo.Count - 1); Restore(p); }
    public void SetPattern(WeavePattern pattern, bool dirty = false)
    {
        if (pattern.Width < 1 || pattern.Height < 1 || pattern.Width > MaxSide || pattern.Height > MaxSide || pattern.Values.Length != pattern.Width * pattern.Height || pattern.Values.Any(v => v != 0 && v != 255)) throw new ArgumentException("Editor supports opaque binary patterns up to 64 × 64.");
        if (dirty) BeginEdit(); else { undo.Clear(); redo.Clear(); }
        Restore(pattern); if (!dirty) saved = Snapshot(); Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Reset(int w, int h)
    {
        if (w < 1 || h < 1 || w > MaxSide || h > MaxSide) throw new ArgumentOutOfRangeException(nameof(w));
        SetPattern(new WeavePattern(w, h, Enumerable.Repeat((byte)255, w * h).ToArray()), true);
    }
    public void MarkSaved() { saved = Snapshot(); Changed?.Invoke(this, EventArgs.Empty); }
    public void DiscardChanges() { undo.Clear(); redo.Clear(); Restore(saved); }
    public void ApplyHelper(string operation, int stepX = 1, int stepY = 1)
    {
        if (stepX < 1 || stepY < 1 || stepX > MaxSide || stepY > MaxSide) throw new ArgumentOutOfRangeException(nameof(stepX));
        byte[] next = (byte[])values.Clone();
        if (operation is "NE" or "NW" or "SE" or "SW")
        {
            Array.Fill(next, (byte)255);
            bool right = operation.EndsWith('E'), down = operation.StartsWith('S');
            int x = right ? 0 : width - 1, y = down ? 0 : height - 1;
            // Wrap in both axes and stop on the first repeated cell.
            var visited = new HashSet<int>();
            while (visited.Add(y * width + x)) { next[y * width + x] = 0; x = (x + (right ? stepX : -stepX) % width + width) % width; y = (y + (down ? stepY : -stepY) % height + height) % height; }
        }
        else for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            next[i] = operation switch {
                "plain" => (byte)((x + y) % 2 == 0 ? 0 : 255),
                "twillRight" => (byte)(((x + y) % 4) < 2 ? 0 : 255),
                "twillLeft" => (byte)(((x - y + height * 4) % 4) < 2 ? 0 : 255),
                "invert" => values[i] == 0 ? (byte)255 : (byte)0,
                "mirrorX" => values[y * width + (width - 1 - x)],
                "mirrorY" => values[(height - 1 - y) * width + x],
                "clear" => (byte)255,
                _ => throw new ArgumentException("Unknown helper operation.", nameof(operation))
            };
        }
        if (next.SequenceEqual(values)) return;
        BeginEdit(); values = next; InvalidateVisual(); Changed?.Invoke(this, EventArgs.Empty);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double size = Math.Min(ActualWidth / width, ActualHeight / height); if (size <= 0) return;
        double x0 = (ActualWidth - size * width) / 2, y0 = (ActualHeight - size * height) / 2;
        var pen = gridVisible ? new Pen(Brushes.LightSlateGray, Math.Min(0.6, size / 8)) : null;
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
        BeginEdit(); paintValue = values[i] == 0 ? (byte)255 : (byte)0; painting = true; CaptureMouse(); Paint(i); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (painting && e.LeftButton == MouseButtonState.Pressed) Paint(Hit(e.GetPosition(this))); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { base.OnMouseLeftButtonUp(e); painting = false; ReleaseMouseCapture(); }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); painting = false; }
    private void Paint(int index) { if (index >= 0 && values[index] != paintValue) { values[index] = paintValue; InvalidateVisual(); Changed?.Invoke(this, EventArgs.Empty); } }
}
