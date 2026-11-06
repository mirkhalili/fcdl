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
    public string? PatternPath { get; private set; }
    public BitmapSource? PatternPreview { get; private set; }
    public string PatternLabel => PatternPath is null ? "—" : Path.GetFileName(PatternPath);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Assign(string path, WeavePattern pattern)
    {
        var preview = BitmapSource.Create(pattern.Width, pattern.Height, 96, 96, PixelFormats.Gray8, null, pattern.Values, pattern.Width);
        preview.Freeze();
        PatternPath = path; PatternPreview = preview;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternPreview)));
    }
}
