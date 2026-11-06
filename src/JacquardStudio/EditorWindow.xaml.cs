using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace JacquardStudio;

public sealed class PaletteEntry : INotifyPropertyChanged
{
    public int Index { get; init; }
    public string ColorLabel { get; init; } = "";
    public Brush Brush { get; init; } = Brushes.Black;
    public WeavePattern? Pattern { get; private set; }
    public string? PatternPath { get; private set; }
    public string PatternLabel => PatternPath is null ? "—" : Path.GetFileName(PatternPath);
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Assign(WeavePattern pattern, string path) { Pattern = pattern; PatternPath = path; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PatternLabel))); }
}

public partial class EditorWindow : Window
{
    private readonly BitmapSource source;
    private readonly string sourcePath;
    private readonly bool english;
    private PaletteResult? design;
    private List<PaletteEntry> entries = new();
    private BitmapSource? rendered;
    private bool busy;
    public EditorWindow(BitmapSource source, string sourcePath, bool english)
    {
        this.source = source; this.sourcePath = sourcePath; this.english = english;
        InitializeComponent();
        WidthInput.Text = source.PixelWidth.ToString(); HeightInput.Text = source.PixelHeight.ToString();
        if (english) Translate();
        Loaded += async (_, _) => await PrepareAsync(false);
    }
    private string T(string fa, string en) => english ? en : fa;
    private void Translate()
    {
        FlowDirection = FlowDirection.LeftToRight; Title = "Prepare design"; TitleText.Text = "Size, colors and patterns";
        SubtitleText.Text = "1. Set dimensions and color count  ·  2. Assign a pattern to each color";
        SaveButton.Content = "Save final image"; SizeTitle.Text = "1  Size and colors"; WidthLabel.Text = "Width (pixels)";
        HeightLabel.Text = "Height (pixels)"; ColorsLabel.Text = "Max colors"; ApplyButton.Content = "Apply size and colors";
        PaletteTitle.Text = "2  Pattern per color"; PreviewTitle.Text = "Result preview";
        HintText.Text = "Patterns must be pure black and white. Tiles repeat from the top-left; unassigned colors stay visible in preview.";
    }
    private void SetBusy(bool value)
    {
        busy = value; ApplyButton.IsEnabled = !value; PaletteList.IsEnabled = !value;
        WidthInput.IsEnabled = !value; HeightInput.IsEnabled = !value; ColorInput.IsEnabled = !value;
        SaveButton.IsEnabled = !value && design is not null && entries.All(e => e.Pattern is not null);
    }
    private async void Apply_Click(object sender, RoutedEventArgs e) => await PrepareAsync(true);
    private async Task PrepareAsync(bool confirm)
    {
        if (busy) return;
        if (!int.TryParse(WidthInput.Text, out int w) || !int.TryParse(HeightInput.Text, out int h) ||
            !int.TryParse(ColorInput.Text, out int colors) || w <= 0 || h <= 0 || (long)w * h > DesignProcessor.MaxPixels || colors is < 1 or > 50)
        {
            MessageBox.Show(this, T("ابعاد مثبت تا مجموع ۴ میلیون پیکسل و تعداد رنگ ۱ تا ۵۰ را وارد کنید.", "Enter positive dimensions totaling at most 4 million pixels and 1–50 colors.")); return;
        }
        if (confirm && entries.Any(e => e.Pattern is not null) && MessageBox.Show(this,
            T("تغییر اندازه یا رنگ، پترن‌های انتخاب‌شده را پاک می‌کند. ادامه می‌دهید؟", "Changing size or colors clears assigned patterns. Continue?"),
            Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        SetBusy(true); StatusText.Text = T("در حال بررسی تصویر...", "Analyzing image...");
        try
        {
            var result = await Task.Run(() => DesignProcessor.Prepare(source, w, h, colors));
            var newEntries = result.Colors.Select((c, i) => new PaletteEntry { Index = i, ColorLabel = $"#{c:X6}  ·  {result.Counts[i]:N0} px", Brush = new SolidColorBrush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)) }).ToList();
            var image = await Task.Run(() => DesignProcessor.Render(result, new WeavePattern?[result.Colors.Length]));
            design = result; entries = newEntries; rendered = image;
            PaletteList.ItemsSource = entries; ResultImage.Source = image;
            SummaryText.Text = T($"رنگ‌های تصویر پس از تغییر اندازه: {result.SourceColorCount:N0}  |  رنگ‌های خروجی: {result.Colors.Length}", $"Colors after resize: {result.SourceColorCount:N0}  |  Output colors: {result.Colors.Length}");
            StatusText.Text = T("با استفاده از دکمه کنار هر رنگ، یک فایل پترن سیاه‌وسفید انتخاب کنید.", "Use the button beside each color to choose a black-and-white pattern.");
            UpdateAssigned();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(this, T("پردازش ممکن نشد: ", "Processing failed: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); StatusText.Text = T("پردازش ناموفق بود.", "Processing failed."); }
        finally { SetBusy(false); }
    }
    private async void Pattern_Click(object sender, RoutedEventArgs e)
    {
        if (busy || design is null || (sender as Button)?.DataContext is not PaletteEntry entry) return;
        var dialog = new OpenFileDialog { Filter = "BMP and TIFF|*.bmp;*.tif;*.tiff", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true);
        try
        {
            var pattern = await Task.Run(() => { var input = ImageLoader.Load(dialog.FileName, 0); if (input.PageCount != 1) throw new InvalidDataException("Multi-page pattern files are not supported."); return DesignProcessor.ReadPattern(input.Image); });
            // Build a snapshot first; only commit the assignment if rendering succeeds.
            var patterns = entries.Select(e => e == entry ? pattern : e.Pattern).ToArray();
            var image = await Task.Run(() => DesignProcessor.Render(design, patterns));
            entry.Assign(pattern, dialog.FileName); rendered = image; ResultImage.Source = image; UpdateAssigned();
            StatusText.Text = T("پترن روی همه پیکسل‌های این رنگ اعمال شد.", "Pattern applied to every pixel of this color.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(this, T("پترن معتبر نیست. تصویر باید فقط سیاه و سفید خالص، بدون شفافیت و تک‌صفحه‌ای باشد.\n", "Invalid pattern. Use a single-page, fully opaque image with only pure black and white.\n") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private void UpdateAssigned()
    {
        int assigned = entries.Count(e => e.Pattern is not null);
        AssignedText.Text = T($"{assigned} از {entries.Count}", $"{assigned} of {entries.Count}");
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (busy || design is null || rendered is null || entries.Any(e => e.Pattern is null)) return;
        var dialog = new SaveFileDialog { Filter = "Bitmap (*.bmp)|*.bmp|TIFF (*.tif)|*.tif", AddExtension = true, OverwritePrompt = true, FileName = "prepared-design.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        string target = Path.GetFullPath(dialog.FileName);
        if (new[] { sourcePath }.Concat(entries.Select(x => x.PatternPath!)).Any(p => string.Equals(Path.GetFullPath(p), target, StringComparison.OrdinalIgnoreCase)))
        { MessageBox.Show(this, T("خروجی نباید روی فایل اصلی یا پترن ذخیره شود.", "Output must not overwrite the source or a pattern.")); return; }
        SetBusy(true);
        try
        {
            var frame = rendered; bool isTiff = dialog.FilterIndex == 2;
            await Task.Run(() => SaveImage(frame, target, isTiff));
            StatusText.Text = T("تصویر ذخیره شد؛ این فایل ماشین نیست.", "Image saved; this is not a loom file.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { MessageBox.Show(this, T("ذخیره ناموفق بود: ", "Save failed: ") + ex.Message); }
        finally { SetBusy(false); }
    }
    private static void SaveImage(BitmapSource frame, string target, bool isTiff)
    {
        string tmp = Path.Combine(Path.GetDirectoryName(target)!, ".jacquard-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            BitmapEncoder encoder = isTiff ? new TiffBitmapEncoder() : new BmpBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame));
            using (var file = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) encoder.Save(file);
            File.Move(tmp, target, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
