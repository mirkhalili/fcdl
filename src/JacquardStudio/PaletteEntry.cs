using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;
public sealed class PaletteEntry : INotifyPropertyChanged
{
    public int Index { get; init; }
    public string ColorLabel { get; init; } = "";
    public string ColorCode { get; init; } = "";
    public int PixelCount { get; init; }
    public string PixelCountText => PixelCount.ToString("N0");
    public double Percent { get; init; }
    public string PercentText => $"{Percent:0.##}%";
    public Brush Brush { get; init; } = Brushes.Black;
    public int ColorNumber => Index + 1;
    public Brush ColorNumberForeground => Brush is SolidColorBrush color && color.Color.R * 299 + color.Color.G * 587 + color.Color.B * 114 > 150_000 ? Brushes.Black : Brushes.White;
    public string? PatternPath { get; private set; }
    public BitmapSource? PatternPreview { get; private set; }
    public ImageSource? PatternGridPreview { get; private set; }
    private bool showGrid = true;
    public bool ShowGrid { get => showGrid; set { if (showGrid == value) return; showGrid = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayPatternPreview))); } }
    public ImageSource? DisplayPatternPreview => ShowGrid ? PatternGridPreview : PatternPreview;
    public string PatternSize { get; private set; } = "—";
    private string? patternName;
    public string PatternLabel => patternName ?? "—";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Assign(string path, WeavePattern pattern) => SetPattern(Path.GetFileName(path), path, pattern);
    public void Restore(string name, WeavePattern pattern) => SetPattern(name, null, pattern);
    private void SetPattern(string name, string? path, WeavePattern pattern)
    {
        var preview = BitmapSource.Create(pattern.Width, pattern.Height, 96, 96, PixelFormats.Gray8, null, pattern.Values, pattern.Width);
        preview.Freeze();
        patternName = name; PatternPath = path; PatternPreview = preview;
        PatternSize = $"{pattern.Width} × {pattern.Height}";
        // Draw a bounded number of major lines even for large or very wide weaves.
        // The card is only 105px across; sub-pixel lines would be indistinguishable.
        int step = Math.Max(1, (Math.Max(pattern.Width, pattern.Height) + 63) / 64);
        var group = new DrawingGroup();
        using (var drawing = group.Open())
        {
            drawing.DrawImage(preview, new System.Windows.Rect(0, 0, pattern.Width, pattern.Height));
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(130, 100, 110, 120)), .06 * step);
            for (int x = 0; x <= pattern.Width; x += step) drawing.DrawLine(gridPen, new System.Windows.Point(x, 0), new System.Windows.Point(x, pattern.Height));
            for (int y = 0; y <= pattern.Height; y += step) drawing.DrawLine(gridPen, new System.Windows.Point(0, y), new System.Windows.Point(pattern.Width, y));
        }
        group.Freeze(); var visual = new DrawingImage(group); visual.Freeze(); PatternGridPreview = visual;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternGridPreview)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayPatternPreview)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternSize)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternPreview)));
    }
}
