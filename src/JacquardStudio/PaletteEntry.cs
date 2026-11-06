using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace JacquardStudio;
public sealed class PaletteEntry : INotifyPropertyChanged
{
    public int Index { get; init; }
    public string ColorLabel { get; init; } = "";
    public Brush Brush { get; init; } = Brushes.Black;
    public string? PatternPath { get; private set; }
    public string PatternLabel => PatternPath is null ? "—" : Path.GetFileName(PatternPath);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Assign(string path) { PatternPath = path; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternLabel))); }
}
