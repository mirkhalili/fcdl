using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;
/// <summary>Single-page A4 report. Raster PDF preserves WPF's Persian shaping and embedded font rendering.</summary>
public static class DesignReport
{
    public const int PixelWidth = 1240, PixelHeight = 1754;
    private static readonly Brush Ink = Brush("#172E3C"), Muted = Brush("#556B77"), Accent = Brush("#138D7A"), Line = Brush("#D7E5E4");
    private static Brush Brush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    public static string PersianDate(DateTimeOffset? created)
    {
        if (created is null) return "نامشخص / Unknown";
        var d = created.Value.DateTime;
        try { var c = new PersianCalendar(); return $"{c.GetYear(d):0000}/{c.GetMonth(d):00}/{c.GetDayOfMonth(d):00}"; }
        catch (ArgumentOutOfRangeException) { return "نامشخص / Unknown"; }
    }
    private static TextBlock Text(string text, double size, Brush? color = null, FontWeight? weight = null) => new() { Text = text, FontSize = size, Foreground = color ?? Ink, FontWeight = weight ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    public static FrameworkElement CreatePage(WorkflowSession session, IReadOnlyList<PaletteEntry> entries, string projectName, DateTimeOffset? created, bool english, DesignMetadata? metadata = null)
    {
        var palette = session.Palette ?? throw new InvalidOperationException("A color palette is required for a report.");
        if (entries.Count != palette.Colors.Length) throw new ArgumentException("Palette metadata does not match.");
        var page = new Grid { Width = 793.7, Height = 1122.5, Background = Brushes.White, FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft };
        System.Windows.Documents.TextElement.SetFontFamily(page, (FontFamily)Application.Current.FindResource("VazirFont"));
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(215) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
        page.Margin = new Thickness(0);
        metadata ??= new DesignMetadata();
        var header = new Grid { Margin = new Thickness(30, 18, 30, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(173) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(173) });
        var titles = new StackPanel();
        titles.Children.Add(Text("JACQUARD STUDIO  /  DESIGN REPORT", 9, Accent, FontWeights.Bold));
        titles.Children.Add(new TextBlock { Text = english ? "Jacquard design report" : "گزارش طرح ژاکارد", FontSize = 20, Foreground = Ink, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 3, 0, 0) });
        titles.Children.Add(Text((english ? "Project: " : "پروژه: ") + projectName, 12, Ink, FontWeights.SemiBold));
        titles.Children.Add(Text((english ? "Created: " : "ایجاد شمسی: ") + PersianDate(created), 10, Muted));
        var info = new TextBlock { Text = (english ? "Designer: " : "طراح: ") + metadata.Designer + "\n" + (english ? "Use: " : "کاربرد: ") + metadata.Usage + "\n" + (english ? "Notes: " : "توضیحات: ") + metadata.Description, Foreground = Ink, FontSize = 10, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 102, Margin = new Thickness(0, 5, 8, 0) };
        titles.Children.Add(info); header.Children.Add(titles);
        void AddImage(int column, string caption, ImageSource source)
        {
            var stack = new StackPanel { Margin = new Thickness(3, 0, 3, 0) };
            stack.Children.Add(new TextBlock { Text = caption, Foreground = Muted, FontSize = 10, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 3) });
            var image = new Image { Source = source, Stretch = Stretch.Uniform, Width = 157, Height = 157, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            stack.Children.Add(new Border { Background = Brush("#F0F7F6"), BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(3), Child = image, CornerRadius = new CornerRadius(6) });
            Grid.SetColumn(stack, column); header.Children.Add(stack);
        }
        AddImage(1, english ? "Color design" : "طرح رنگی", session.ColorPreview ?? session.Resized ?? session.Source);
        AddImage(2, session.CanSave ? (english ? "Final woven image" : "تصویر نهایی بافت‌ریزی") : (english ? "Incomplete weave" : "بافت‌ریزی ناتمام"), session.PatternPreview ?? session.Source);
        page.Children.Add(header);
        var metrics = new Border { Margin = new Thickness(30, 4, 30, 4), Background = Brush("#EAF5F2"), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 5, 14, 5) };
        metrics.Child = Text(english ? $"WARP  {palette.Width:N0}     ·     WEFT  {palette.Height:N0}     ·     PIXELS  {(long)palette.Width * palette.Height:N0}     ·     COLORS  {entries.Count}" : $"تار: {palette.Width:N0}     ·     پود: {palette.Height:N0}     ·     کل پیکسل: {(long)palette.Width * palette.Height:N0}     ·     رنگ: {entries.Count}", 13, Ink, FontWeights.SemiBold);
        Grid.SetRow(metrics, 1); page.Children.Add(metrics);
        var body = new Grid { Margin = new Thickness(30, 3, 30, 0) };
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(Text(english ? "COLORS  /  WEAVES  /  SHARE" : "رنگ‌ها  /  بافت‌های اختصاص‌یافته  /  درصد", 14, Accent, FontWeights.Bold));
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(columns, 1); body.Children.Add(columns);
        int rows = Math.Max(1, (entries.Count + 1) / 2);
        double rowHeight = Math.Min(68, 785d / rows), pictureSize = Math.Max(25, rowHeight - 4);
        for (int c = 0; c < 2; c++)
        {
            var list = new StackPanel { Margin = new Thickness(c == 0 ? 0 : 8, 0, c == 0 ? 8 : 0, 0) };
            Grid.SetColumn(list, c); columns.Children.Add(list);
            foreach (var entry in entries.Where((_, index) => index % 2 == c))
            {
                var row = new Grid { Height = rowHeight - 1, Margin = new Thickness(0, 0, 0, 1), Background = Brush("#F5F9F8") };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pictureSize + 4) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
                var swatch = new Border { Width = 20, Height = 20, Background = entry.Brush, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) }; row.Children.Add(swatch);
                var code = Text(entry.ColorCode, 10); code.FlowDirection = FlowDirection.LeftToRight; Grid.SetColumn(code, 1); row.Children.Add(code);
                var pat = new Image { Source = entry.PatternGridPreview, Width = pictureSize, Height = pictureSize, Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(pat, BitmapScalingMode.NearestNeighbor); Grid.SetColumn(pat, 2); row.Children.Add(pat);
                var label = Text(entry.PatternLabel, 10, Muted); label.Margin = new Thickness(4, 0, 4, 0); Grid.SetColumn(label, 3); row.Children.Add(label);
                var count = Text(entry.PixelCount.ToString("N0"), 9, Muted); count.FlowDirection = FlowDirection.LeftToRight; Grid.SetColumn(count, 4); row.Children.Add(count);
                var pct = Text(entry.PercentText, 10, Ink, FontWeights.SemiBold); pct.FlowDirection = FlowDirection.LeftToRight; Grid.SetColumn(pct, 5); row.Children.Add(pct);
                list.Children.Add(row);
            }
        }
        Grid.SetRow(body, 2); page.Children.Add(body);
        var footer = new Border { Margin = new Thickness(30, 0, 30, 5), BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = Text(english ? "Jacquard Studio  •  Design summary  •  Mirkhalili" : "ژاکارد استودیو  •  خلاصه طرح  •  میرخلیلی", 10, Muted) }; Grid.SetRow(footer, 3); page.Children.Add(footer);
        return page;
    }
    public static BitmapSource Render(FrameworkElement page)
    {
        page.Measure(new Size(793.7, 1122.5)); page.Arrange(new Rect(0, 0, 793.7, 1122.5)); page.UpdateLayout();
        var bitmap = new RenderTargetBitmap(PixelWidth, PixelHeight, 150, 150, PixelFormats.Pbgra32); bitmap.Render(page); bitmap.Freeze(); return bitmap;
    }
    public static void SavePdf(BitmapSource bitmap, string path, IEnumerable<string> protectedPaths)
    {
        string target = Path.GetFullPath(path);
        if (!target.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Report extension must be .pdf.");
        if (protectedPaths.Any(p => string.Equals(Path.GetFullPath(p), target, StringComparison.OrdinalIgnoreCase))) throw new IOException("Cannot overwrite a source or project file.");
        var encoder = new JpegBitmapEncoder { QualityLevel = 92 }; encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var jpg = new MemoryStream(); encoder.Save(jpg); byte[] image = jpg.ToArray();
        string temp = Path.Combine(Path.GetDirectoryName(target)!, ".report-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                void Write(string s) { byte[] data = Encoding.ASCII.GetBytes(s); f.Write(data); }
                var offsets = new long[6]; Write("%PDF-1.4\n%----\n");
                void Obj(int n, string body) { offsets[n] = f.Position; Write($"{n} 0 obj\n{body}\nendobj\n"); }
                Obj(1, "<< /Type /Catalog /Pages 2 0 R >>"); Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
                Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>");
                offsets[4] = f.Position; Write($"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {bitmap.PixelWidth} /Height {bitmap.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {image.Length} >>\nstream\n"); f.Write(image); Write("\nendstream\nendobj\n");
                string content = "q\n595 0 0 842 0 0 cm\n/Im0 Do\nQ\n"; Obj(5, $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
                long xref = f.Position; Write("xref\n0 6\n0000000000 65535 f \n"); for (int i = 1; i <= 5; i++) Write($"{offsets[i]:0000000000} 00000 n \n");
                Write($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            }
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
