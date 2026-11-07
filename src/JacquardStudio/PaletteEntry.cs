using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;
public sealed class PaletteEntry : INotifyPropertyChanged
{
    public int Index { get; init; }
    public string ColorLabel { get; init; } = "";
    public Brush Brush { get; init; } = Brushes.Black;
    public int ColorNumber => Index + 1;
    public Brush ColorNumberForeground => Brush is SolidColorBrush color && color.Color.R * 299 + color.Color.G * 587 + color.Color.B * 114 > 150_000 ? Brushes.Black : Brushes.White;
    public string? PatternPath { get; private set; }
    public BitmapSource? PatternPreview { get; private set; }
    public ImageSource? PatternGridPreview { get; private set; }
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
        if (pattern.Width <= 64 && pattern.Height <= 64)
        {
            var group = new DrawingGroup();
            using (var drawing = group.Open())
            {
                var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(100, 100, 110, 120)), 0.06);
                for (int y = 0; y < pattern.Height; y++)
                for (int x = 0; x < pattern.Width; x++)
                    drawing.DrawRectangle(pattern.Values[y * pattern.Width + x] == 0 ? Brushes.Black : Brushes.White, gridPen, new System.Windows.Rect(x, y, 1, 1));
            }
            group.Freeze(); var visual = new DrawingImage(group); visual.Freeze(); PatternGridPreview = visual;
        }
        else PatternGridPreview = preview;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternGridPreview)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternSize)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternPreview)));
    }
}
