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
    public static FrameworkElement CreatePage(WorkflowSession session, IReadOnlyList<PaletteEntry> entries, string projectName, DateTimeOffset? created, bool english, DesignMetadata? metadata = null, MachineSetup? machine = null, WarpAnalysis? analysis = null, MachineComposition? composition = null)
    {
        var palette = session.Palette ?? throw new InvalidOperationException("A color palette is required for a report.");
        if (entries.Count != palette.Colors.Length) throw new ArgumentException("Palette metadata does not match.");
        var page = new Grid { Width = 793.7, Height = 1122.5, Background = Brushes.White, FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft };
        System.Windows.Documents.TextElement.SetFontFamily(page, (FontFamily)Application.Current.FindResource("VazirFont"));
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(215) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(170) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
        page.Margin = new Thickness(0);
        metadata ??= new DesignMetadata();
        var header = new Grid { Margin = new Thickness(30, 18, 30, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(173) }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(173) });
        var titles = new StackPanel();
        var brand = new StackPanel { Orientation = Orientation.Horizontal };
        brand.Children.Add(new Image { Source = new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/app.ico", UriKind.Absolute)), Width = 27, Height = 27, Margin = new Thickness(0, 0, 7, 0) });
        brand.Children.Add(Text("JACQUARD STUDIO  /  DESIGN REPORT", 9, Accent, FontWeights.Bold)); titles.Children.Add(brand);
        titles.Children.Add(new TextBlock { Text = english ? "Jacquard design report" : "گزارش طرح ژاکارد", FontSize = 20, Foreground = Ink, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 3, 0, 0) });
        titles.Children.Add(Text((english ? "Project: " : "پروژه: ") + projectName, 12, Ink, FontWeights.SemiBold));
        titles.Children.Add(Text(PersianDate(created), 10, Muted));
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
        AddImage(2, session.CanSave ? (english ? "Final woven image" : "تصویر نهایی بافت‌ریزی") : (english ? "Incomplete weave" : "بافت‌ریزی ناتمام"), composition?.Image ?? session.PatternPreview ?? session.Source);
        page.Children.Add(header);
        var metrics = new Border { Margin = new Thickness(30, 4, 30, 4), Background = Brush("#EAF5F2"), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 5, 14, 5) };
        metrics.Child = Text(english ? $"WARP  {palette.Width:N0}     ·     WEFT  {palette.Height:N0}     ·     PIXELS  {(long)palette.Width * palette.Height:N0}     ·     COLORS  {entries.Count}" : $"تار: {palette.Width:N0}     ·     پود: {palette.Height:N0}     ·     کل پیکسل: {(long)palette.Width * palette.Height:N0}     ·     رنگ: {entries.Count}", 13, Ink, FontWeights.SemiBold);
        Grid.SetRow(metrics, 1); page.Children.Add(metrics);
        machine ??= new MachineSetup();
        string auto = english ? "auto" : "خودکار";
        string total = composition is null ? (string.IsNullOrWhiteSpace(machine.TotalHooks) ? auto : machine.TotalHooks) : composition.Image.PixelWidth.ToString("N0");
        string start = composition is null ? (string.IsNullOrWhiteSpace(machine.DesignStart) ? auto : PositionNumbers.ToDisplay(machine.DesignStart)) : (composition.DesignStart + 1).ToString();
        string layout = machine.AbsoluteLayout ? (english ? $"Total hooks: {total}   Design start: {start}   Wefts: {machine.WeftCount} at {PositionNumbers.ToDisplay(machine.WeftStart)}" : $"کل تار: {total}   شروع طرح: {start}   پود: {machine.WeftCount} از {PositionNumbers.ToDisplay(machine.WeftStart)}") : (english ? "Legacy box layout" : "چیدمان قدیمی پودگذاری");
        var extra = new StackPanel { Margin = new Thickness(30, 1, 30, 2) };
        var edges = new Grid { Height = 55 }; edges.ColumnDefinitions.Add(new ColumnDefinition()); edges.ColumnDefinitions.Add(new ColumnDefinition());
        void AddEdge(int column, string caption, string width, string name, WeavePattern? pattern)
        {
            var card = new Grid { Margin = new Thickness(2), Background = Brush("#F5F9F8") }; card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); card.ColumnDefinitions.Add(new ColumnDefinition());
            if (pattern is not null)
            {
                var entry = new PaletteEntry(); entry.Restore(name, pattern);
                var image = new Image { Source = entry.PatternGridPreview, Width = 46, Height = 46, Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor); card.Children.Add(image);
            }
            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
            details.Children.Add(Text(caption + ": " + width, 10, Ink, FontWeights.SemiBold));
            details.Children.Add(Text((string.IsNullOrWhiteSpace(name) ? "—" : name) + (pattern is null ? "" : $"  ({pattern.Width}×{pattern.Height})"), 9, Muted));
            Grid.SetColumn(details, 1); card.Children.Add(details); Grid.SetColumn(card, column); edges.Children.Add(card);
        }
        AddEdge(0, english ? "Left selvedge" : "کناره چپ", machine.LeftWidth, machine.LeftName, machine.LeftPattern);
        AddEdge(1, english ? "Right selvedge" : "کناره راست", machine.RightWidth, machine.RightName, machine.RightPattern);
        extra.Children.Add(edges); extra.Children.Add(Text(layout, 10, Ink));
        extra.Children.Add(Text((english ? "Weft sequence: " : "توالی پود: ") + (string.IsNullOrWhiteSpace(machine.Sequence) ? (english ? "natural" : "طبیعی") : machine.Sequence) + (machine.StartFromBottom ? (english ? " · bottom" : " · شروع از پایین") : (english ? " · top" : " · شروع از بالا")), 10, Ink));
        if (analysis is not null)
        {
            var summary = WarpRanges.Summarize(analysis, (composition?.FabricStart ?? 0) + 1, 3, 20);
            var groups = new Grid { Margin = new Thickness(0, 2, 0, 0) }; groups.ColumnDefinitions.Add(new ColumnDefinition()); groups.ColumnDefinitions.Add(new ColumnDefinition());
            void AddRanges(int column, string heading, IReadOnlyList<WarpRange> ranges, int count)
            {
                var list = new StackPanel();
                list.Children.Add(Text(heading + $" ({ranges.Count}/{count})", 9, Accent, FontWeights.SemiBold));
                if (ranges.Count == 0) list.Children.Add(Text(english ? "None" : "موردی نیست", 9, Muted));
                foreach (var r in ranges)
                {
                    string percentage = WarpDisplay.Range(r);
                    list.Children.Add(Text(english ? $"Warp {r.Start}–{r.End}: {percentage}" : $"تار {r.Start}–{r.End}: {percentage}", 9, Ink));
                }
                var border = new Border { BorderBrush = Line, BorderThickness = new Thickness(1), Padding = new Thickness(5, 1, 5, 1), Child = list }; Grid.SetColumn(border, column); groups.Children.Add(border);
            }
            AddRanges(0, english ? "Tightest >20%" : "کشیدگی بالاتر از ۲۰٪", summary.Tight, summary.TightCount);
            AddRanges(1, english ? "Loosest >20%" : "شل‌شدگی بالاتر از ۲۰٪", summary.Loose, summary.LooseCount);
            extra.Children.Add(groups);
            extra.Children.Add(Text(english ? "Relative indicators; not physical tension measurements." : "شاخص نسبی؛ نه اندازه‌گیری فیزیکی کشش.", 9, Muted));
        }
        else extra.Children.Add(Text(english ? "Percentages unavailable until all weaves are assigned." : "درصدها پس از تکمیل بافت‌ریزی در دسترس‌اند.", 9, Muted));
        Grid.SetRow(extra, 2); page.Children.Add(extra);
        var body = new Grid { Margin = new Thickness(30, 3, 30, 0) };
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(Text(english ? "COLORS  /  WEAVES  /  SHARE" : "رنگ‌ها  /  بافت‌های اختصاص‌یافته  /  درصد", 14, Accent, FontWeights.Bold));
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition()); columns.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetRow(columns, 1); body.Children.Add(columns);
        int rows = Math.Max(1, (entries.Count + 1) / 2);
        double rowHeight = Math.Min(68, 605d / rows), pictureSize = Math.Max(18, rowHeight - 4);
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
        Grid.SetRow(body, 3); page.Children.Add(body);
        var footer = new Border { Margin = new Thickness(30, 0, 30, 5), BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = Text(english ? "Jacquard Studio  •  Design summary  •  Mirkhalili" : "ژاکارد استودیو  •  خلاصه طرح  •  میرخلیلی", 10, Muted) }; Grid.SetRow(footer, 4); page.Children.Add(footer);
        return page;
    }
    /// <summary>Additional A4 pages with the fabric preview and every applied weft, without truncating at high counts.</summary>
    public static IReadOnlyList<FrameworkElement> CreateSimulationPages(BitmapSource preview, SimulationSettings settings, IReadOnlyList<string> ids, bool english)
    {
        FabricSimulation.Validate(settings);
        if (ids.Count is < 1 or > 256) throw new ArgumentException("Invalid weft count for report.");
        const int perPage = 46; int pageCount = (ids.Count + perPage - 1) / perPage;
        var result = new List<FrameworkElement>();
        for (int pageNumber = 0; pageNumber < pageCount; pageNumber++)
        {
            var page = new Grid { Width = 793.7, Height = 1122.5, Background = Brushes.White, FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft, Margin = new Thickness(0) };
            System.Windows.Documents.TextElement.SetFontFamily(page, (FontFamily)Application.Current.FindResource("VazirFont"));
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) }); page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(320) }); page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) }); page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
            page.Children.Add(new TextBlock { Text = (english ? "Fabric simulation · " : "شبیه‌سازی پارچه · ") + $"{pageNumber + 1}/{pageCount}", Margin = new Thickness(30, 18, 30, 0), Foreground = Accent, FontSize = 20, FontWeight = FontWeights.Bold });
            var image = new Image { Source = preview, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Height = 300, MaxWidth = 720 };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality); Grid.SetRow(image, 1); page.Children.Add(image);
            string warpName = FabricSimulation.YarnNames[settings.WarpYarn];
            var info = new TextBlock { Text = (english ? "Warp: " : "نخ تار: ") + warpName + "   " + settings.WarpColor + "   ·   " + (settings.Weave == 0 ? (english ? "project weave" : "بافت پروژه") : settings.Weave == 1 ? (english ? "plain" : "ساده") : (english ? "twill" : "جناغی")) + "   ·   " + (settings.Mode == 2 ? (english ? "3D; large designs sampled" : "سه‌بعدی؛ طرح بزرگ نمونه‌برداری شده") : settings.Mode == 0 ? (english ? "flat" : "معمولی") : (english ? "relief" : "برجسته")) + "\n" + (english ? "Stylized appearance, not a physical simulation." : "تصویرسازی تقریبی است، نه شبیه‌سازی فیزیکی."), Foreground = Ink, FontSize = 12, Margin = new Thickness(30, 4, 30, 0), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(info, 2); page.Children.Add(info);
            var list = new Grid { Margin = new Thickness(30, 0, 30, 0) }; list.ColumnDefinitions.Add(new ColumnDefinition()); list.ColumnDefinitions.Add(new ColumnDefinition());
            for (int column = 0; column < 2; column++)
            {
                var rows = new StackPanel { Margin = new Thickness(4, 0, 4, 0) }; Grid.SetColumn(rows, column); list.Children.Add(rows);
                foreach (string id in ids.Skip(pageNumber * perPage).Take(perPage).Where((_, index) => index % 2 == column))
                {
                    var yarn = FabricSimulation.YarnFor(settings, id); Color color = FabricSimulation.ParseColor(yarn.Color);
                    var row = new Grid { Height = 25, Background = Brush("#F5F9F8"), Margin = new Thickness(0, 0, 0, 1) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.Children.Add(new Border { Width = 17, Height = 17, Background = new SolidColorBrush(color), BorderBrush = Line, BorderThickness = new Thickness(1) });
                    var label = Text((english ? "Weft " : "پود ") + id + " · " + FabricSimulation.YarnNames[yarn.Yarn] + " · " + yarn.Color, 9, Ink); Grid.SetColumn(label, 1); row.Children.Add(label); rows.Children.Add(row);
                }
            }
            Grid.SetRow(list, 3); page.Children.Add(list);
            var footer = new Border { Margin = new Thickness(30, 0, 30, 5), BorderBrush = Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = Text(english ? "Jacquard Studio • Fabric preview • Mirkhalili" : "ژاکارد استودیو • نمای پارچه • میرخلیلی", 10, Muted) }; Grid.SetRow(footer, 4); page.Children.Add(footer);
            result.Add(page);
        }
        return result;
    }
    public static BitmapSource Render(FrameworkElement page)
    {
        page.Measure(new Size(793.7, 1122.5)); page.Arrange(new Rect(0, 0, 793.7, 1122.5)); page.UpdateLayout();
        var bitmap = new RenderTargetBitmap(PixelWidth, PixelHeight, 150, 150, PixelFormats.Pbgra32); bitmap.Render(page); bitmap.Freeze(); return bitmap;
    }
    public static void SavePdf(BitmapSource bitmap, string path, IEnumerable<string> protectedPaths) => SavePdf(new[] { bitmap }, path, protectedPaths);
    public static void SavePdf(IReadOnlyList<BitmapSource> pages, string path, IEnumerable<string> protectedPaths)
    {
        if (pages.Count is < 1 or > 16) throw new ArgumentException("Invalid number of report pages.");
        string target = Path.GetFullPath(path);
        if (!target.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Report extension must be .pdf.");
        if (protectedPaths.Any(p => string.Equals(Path.GetFullPath(p), target, StringComparison.OrdinalIgnoreCase))) throw new IOException("Cannot overwrite a source or project file.");
        string temp = Path.Combine(Path.GetDirectoryName(target)!, ".report-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var f = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                void Write(string s) { byte[] data = Encoding.ASCII.GetBytes(s); f.Write(data); }
                int count = 2 + pages.Count * 3; var offsets = new long[count + 1]; Write("%PDF-1.4\n%----\n");
                void Obj(int n, string body) { offsets[n] = f.Position; Write($"{n} 0 obj\n{body}\nendobj\n"); }
                Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
                Obj(2, $"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{3 + i * 3} 0 R"))}] /Count {pages.Count} >>");
                for (int i = 0; i < pages.Count; i++)
                {
                    var bitmap = pages[i]; int page = 3 + i * 3, picture = page + 1, contentId = page + 2;
                    var encoder = new JpegBitmapEncoder { QualityLevel = 92 }; encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var jpg = new MemoryStream(); encoder.Save(jpg); byte[] image = jpg.ToArray();
                    Obj(page, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im0 {picture} 0 R >> >> /Contents {contentId} 0 R >>");
                    offsets[picture] = f.Position; Write($"{picture} 0 obj\n<< /Type /XObject /Subtype /Image /Width {bitmap.PixelWidth} /Height {bitmap.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {image.Length} >>\nstream\n"); f.Write(image); Write("\nendstream\nendobj\n");
                    string content = "q\n595 0 0 842 0 0 cm\n/Im0 Do\nQ\n"; Obj(contentId, $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
                }
                long xref = f.Position; Write($"xref\n0 {count + 1}\n0000000000 65535 f \n"); for (int i = 1; i <= count; i++) Write($"{offsets[i]:0000000000} 00000 n \n");
                Write($"trailer\n<< /Size {count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            }
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
