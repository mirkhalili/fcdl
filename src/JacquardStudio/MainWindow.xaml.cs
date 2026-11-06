using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace JacquardStudio;

public partial class MainWindow : Window
{
    private enum Stage { Size, Colors, Patterns }
    private Stage stage = Stage.Size;
    private bool english, busy;
    private string? path;
    private ImageResult? current;
    private WorkflowSession? session;
    private List<PaletteEntry> entries = new();
    private double zoom = 1;

    public MainWindow() { InitializeComponent(); VersionText.Text = AppVersion.Display; UpdateLabels(); RefreshControls(); }
    private string T(string fa, string en) => english ? en : fa;
    private bool SizeDirty => session?.Resized is null || !int.TryParse(WidthInput.Text, out int w) || !int.TryParse(HeightInput.Text, out int h) || w != session.Resized.PixelWidth || h != session.Resized.PixelHeight;
    private bool ColorsDirty => session?.Palette is null || !int.TryParse(ColorInput.Text, out int colors) || colors != session.AppliedColorLimit;

    private bool ConfirmDiscard()
    {
        if (session?.Resized is null) return true;
        return MessageBox.Show(this, T("با تغییر فایل یا صفحه، مراحل انجام‌شده پاک می‌شوند. ادامه می‌دهید؟", "Opening another file or page discards the current stages. Continue?"), Title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "BMP and TIFF|*.bmp;*.tif;*.tiff|All files|*.*", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true && ConfirmDiscard()) _ = LoadAsync(dialog.FileName, 0);
    }
    private async Task LoadAsync(string filename, int page)
    {
        SetBusy(true); StatusText.Text = T("در حال باز کردن تصویر…", "Loading image…");
        try
        {
            var result = await Task.Run(() => ImageLoader.Load(filename, page));
            path = filename; current = result; session = new WorkflowSession(result.Image); entries.Clear(); PaletteList.ItemsSource = null; ColorPaletteList.ItemsSource = null;
            WidthInput.Text = result.Image.PixelWidth.ToString(); HeightInput.Text = result.Image.PixelHeight.ToString(); ColorInput.Text = "50";
            FileValue.Text = Path.GetFileName(filename); FormatValue.Text = $"{result.Format} · {result.Image.Format.BitsPerPixel} bpp";
            zoom = 1; SetZoom(1); stage = Stage.Size; ShowStage();
            StatusText.Text = T("اندازه را تعیین و اعمال کنید.", "Set and apply the image size.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(this, T("باز کردن تصویر ممکن نشد.\n", "Could not open the image.\n") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); StatusText.Text = T("باز کردن تصویر ناموفق بود.", "Could not open image."); }
        finally { SetBusy(false); }
    }
    private void PreviousPage_Click(object sender, RoutedEventArgs e) { if (!busy && path is not null && current is not null && current.PageIndex > 0 && ConfirmDiscard()) _ = LoadAsync(path, current.PageIndex - 1); }
    private void NextPage_Click(object sender, RoutedEventArgs e) { if (!busy && path is not null && current is not null && current.PageIndex + 1 < current.PageCount && ConfirmDiscard()) _ = LoadAsync(path, current.PageIndex + 1); }
    private void Window_DragOver(object sender, DragEventArgs e) { e.Effects = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Window_Drop(object sender, DragEventArgs e) { if (!busy && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && ConfirmDiscard()) _ = LoadAsync(files[0], 0); }
    private void Inputs_Changed(object sender, TextChangedEventArgs e) { if (IsInitialized) RefreshControls(); }
    private void SetBusy(bool value) { busy = value; RefreshControls(); }
    private void RefreshControls()
    {
        if (!IsInitialized) return;
        OpenButton.IsEnabled = !busy; EmptyButton.IsEnabled = !busy; SettingsBorder.IsEnabled = !busy;
        SizeStepButton.IsEnabled = !busy && session is not null;
        ColorStepButton.IsEnabled = !busy && session?.Resized is not null && !SizeDirty;
        PatternStepButton.IsEnabled = !busy && session?.Palette is not null && !SizeDirty && !ColorsDirty;
        ToColorButton.IsEnabled = ColorStepButton.IsEnabled; ToPatternButton.IsEnabled = PatternStepButton.IsEnabled;
        SaveButton.IsEnabled = !busy && stage == Stage.Patterns && session?.CanSave == true && !SizeDirty && !ColorsDirty;
        PreviousPage.IsEnabled = !busy && current is not null && current.PageIndex > 0;
        NextPage.IsEnabled = !busy && current is not null && current.PageIndex + 1 < current.PageCount;
        PageValue.Text = current is null ? "—" : $"{current.PageIndex + 1} / {current.PageCount}";
        foreach (var pair in new[] { (SizeStepButton, Stage.Size), (ColorStepButton, Stage.Colors), (PatternStepButton, Stage.Patterns) })
        {
            pair.Item1.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(session is not null && pair.Item2 == stage ? "#31574F" : "#263749"));
            pair.Item1.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(session is not null && pair.Item2 == stage ? "#54E0C2" : "#374C60"));
        }
    }
    private void SizeStep_Click(object sender, RoutedEventArgs e) { if (!busy && session is not null) { stage = Stage.Size; ShowStage(); } }
    private async void ColorStep_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Resized is null || SizeDirty) return;
        stage = Stage.Colors; ShowStage();
        if (session.Palette is null) await ApplyColorsAsync(false);
    }
    private void PatternStep_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || SizeDirty || ColorsDirty) return;
        stage = Stage.Patterns; ShowStage();
    }
    private void ShowStage()
    {
        bool hasFile = session is not null;
        NoFilePanel.Visibility = hasFile ? Visibility.Collapsed : Visibility.Visible;
        SizePanel.Visibility = hasFile && stage == Stage.Size ? Visibility.Visible : Visibility.Collapsed;
        ColorPanel.Visibility = hasFile && stage == Stage.Colors ? Visibility.Visible : Visibility.Collapsed;
        PatternPanel.Visibility = hasFile && stage == Stage.Patterns ? Visibility.Visible : Visibility.Collapsed;
        if (session is null) { PreviewImage.Source = null; ImageScroll.Visibility = Visibility.Collapsed; EmptyState.Visibility = Visibility.Visible; }
        else
        {
            BitmapSource view = stage switch
            {
                Stage.Size => session.Resized ?? session.Source,
                Stage.Colors => session.ColorPreview ?? session.Resized ?? session.Source,
                Stage.Patterns => session.PatternPreview ?? session.ColorPreview ?? session.Source,
                _ => session.Source
            };
            PreviewImage.Source = view; ImageScroll.Visibility = Visibility.Visible; EmptyState.Visibility = Visibility.Collapsed;
            PreviewDetails.Text = $"{view.PixelWidth:N0} × {view.PixelHeight:N0} px";
        }
        CanvasTitle.Text = !hasFile ? T("پیش‌نمایش طرح", "Design preview") : stage switch
        {
            Stage.Size => T("پیش‌نمایش اندازه", "Size preview"),
            Stage.Colors => T("پیش‌نمایش رنگ‌ها", "Color preview"),
            _ => T("پیش‌نمایش بافت", "Pattern preview")
        };
        UpdateSummary(); RefreshControls();
    }
    private bool ConfirmPatternLoss()
    {
        if (session?.Patterns.Any(p => p is not null) != true) return true;
        return MessageBox.Show(this, T("تغییر این مرحله پترن‌های انتخاب‌شده را پاک می‌کند. ادامه می‌دهید؟", "Changing this stage clears the assigned patterns. Continue?"), Title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
    private async void ApplySize_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session is null) return;
        if (!int.TryParse(WidthInput.Text, out int w) || !int.TryParse(HeightInput.Text, out int h) || w <= 0 || h <= 0 || (long)w * h > DesignProcessor.MaxPixels)
        { MessageBox.Show(this, T("ابعاد مثبت تا مجموع ۴ میلیون پیکسل وارد کنید.", "Enter positive dimensions totaling at most 4 million pixels.")); return; }
        if (!ConfirmPatternLoss()) return;
        SetBusy(true); StatusText.Text = T("در حال تغییر اندازه…", "Resizing…");
        try
        {
            await Task.Run(() => session.ApplySize(w, h)); entries.Clear(); PaletteList.ItemsSource = null; ColorPaletteList.ItemsSource = null; stage = Stage.Size; ShowStage();
            StatusText.Text = T("اندازه اعمال شد. مرحله رنگ را انتخاب کنید.", "Size applied. Continue to colors.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(this, T("تغییر اندازه ممکن نشد: ", "Resizing failed: ") + ex.Message); StatusText.Text = T("تغییر اندازه ناموفق بود.", "Resizing failed."); }
        finally { SetBusy(false); }
    }
    private async void ApplyColor_Click(object sender, RoutedEventArgs e) => await ApplyColorsAsync(true);
    private async Task ApplyColorsAsync(bool confirm)
    {
        if (busy || session?.Resized is null || SizeDirty) return;
        if (!int.TryParse(ColorInput.Text, out int colors) || colors is < 1 or > 50)
        { MessageBox.Show(this, T("تعداد رنگ باید بین ۱ تا ۵۰ باشد.", "The color count must be between 1 and 50.")); return; }
        if (confirm && !ConfirmPatternLoss()) return;
        SetBusy(true); StatusText.Text = T("در حال بررسی رنگ‌ها…", "Analyzing colors…");
        try
        {
            await Task.Run(() => session.ApplyColors(colors));
            var palette = session.Palette!;
            entries = palette.Colors.Select((c, i) => new PaletteEntry { Index = i, ColorLabel = $"#{c:X6} · {palette.Counts[i]:N0} px", Brush = new SolidColorBrush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)) }).ToList();
            PaletteList.ItemsSource = entries; ColorPaletteList.ItemsSource = entries; stage = Stage.Colors; ShowStage();
            StatusText.Text = T("رنگ‌ها اعمال شدند. مرحله بافت را انتخاب کنید.", "Colors applied. Continue to patterns.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(this, T("کاهش رنگ ممکن نشد: ", "Color reduction failed: ") + ex.Message); StatusText.Text = T("کاهش رنگ ناموفق بود.", "Color reduction failed."); }
        finally { SetBusy(false); }
    }
    private async void Pattern_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || (sender as Button)?.DataContext is not PaletteEntry entry) return;
        var dialog = new OpenFileDialog { Filter = "BMP and TIFF|*.bmp;*.tif;*.tiff", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true); StatusText.Text = T("در حال اعمال پترن…", "Applying pattern…");
        try
        {
            await Task.Run(() => { var input = ImageLoader.Load(dialog.FileName, 0); if (input.PageCount != 1) throw new InvalidDataException("Multi-page pattern files are not supported."); session.AssignPattern(entry.Index, DesignProcessor.ReadPattern(input.Image)); });
            entry.Assign(dialog.FileName); ShowStage(); StatusText.Text = T("پترن روی همه نقاط این رنگ اعمال شد.", "Pattern applied to every pixel of this color.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(this, T("پترن باید تک‌صفحه‌ای، مات و فقط سیاه‌وسفید خالص باشد.\n", "Pattern must be single-page, opaque and pure black and white.\n") + ex.Message); StatusText.Text = T("اعمال پترن ناموفق بود.", "Pattern could not be applied."); }
        finally { SetBusy(false); }
    }
    private void UpdateSummary()
    {
        if (session?.Palette is not { } palette) { ColorSummary.Text = ""; AssignedText.Text = ""; return; }
        ColorSummary.Text = T($"رنگ‌های ورودی: {palette.SourceColorCount:N0}  |  رنگ‌های خروجی: {palette.Colors.Length}", $"Input colors: {palette.SourceColorCount:N0}  |  Output colors: {palette.Colors.Length}");
        int assigned = session.Patterns.Count(p => p is not null);
        AssignedText.Text = T($"پترن‌های انتخاب‌شده: {assigned} از {entries.Count}", $"Assigned patterns: {assigned} of {entries.Count}");
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.CanSave != true || session.PatternPreview is null || path is null) return;
        var dialog = new SaveFileDialog { Filter = "Bitmap (*.bmp)|*.bmp|TIFF (*.tif)|*.tif", AddExtension = true, OverwritePrompt = true, FileName = "prepared-design.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        string target = Path.GetFullPath(dialog.FileName);
        string ext = Path.GetExtension(target).ToLowerInvariant();
        if ((dialog.FilterIndex == 1 && ext != ".bmp") || (dialog.FilterIndex == 2 && ext is not (".tif" or ".tiff")))
        { MessageBox.Show(this, T("پسوند فایل باید با قالب انتخاب‌شده مطابقت داشته باشد.", "The file extension must match the selected format.")); return; }
        if (new[] { path }.Concat(entries.Select(x => x.PatternPath!)).Any(p => string.Equals(Path.GetFullPath(p), target, StringComparison.OrdinalIgnoreCase)))
        { MessageBox.Show(this, T("خروجی نباید روی فایل اصلی یا پترن ذخیره شود.", "Output must not overwrite the source or a pattern.")); return; }
        SetBusy(true);
        try { var frame = session.PatternPreview; await Task.Run(() => SaveImage(frame, target, dialog.FilterIndex == 2)); StatusText.Text = T("تصویر ذخیره شد؛ این فایل ماشین نیست.", "Image saved; this is not a loom file."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { MessageBox.Show(this, T("ذخیره ناموفق بود: ", "Save failed: ") + ex.Message); }
        finally { SetBusy(false); }
    }
    private static void SaveImage(BitmapSource frame, string target, bool isTiff)
    {
        string tmp = Path.Combine(Path.GetDirectoryName(target)!, ".jacquard-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { BitmapEncoder encoder = isTiff ? new TiffBitmapEncoder() : new BmpBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(frame)); using (var file = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) encoder.Save(file); File.Move(tmp, target, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    private void SetZoom(double value) { zoom = Math.Clamp(value, 0.125, 8); PreviewImage.LayoutTransform = new ScaleTransform(zoom, zoom); ZoomText.Text = $"{zoom * 100:0}%"; }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(zoom * 2);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(zoom / 2);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(1);
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow(english) { Owner = this }.ShowDialog();
    private void Language_Click(object sender, RoutedEventArgs e) { english = !english; UpdateLabels(); ShowStage(); }
    private void UpdateLabels()
    {
        FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        SettingsBorder.FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        LanguageButton.Content = english ? "فارسی" : "English"; AboutButton.Content = T("درباره", "About");
        SubtitleText.Text = T("فضای کار طراحی و بازرسی ژاکارد", "Jacquard design workspace"); BadgeText.Text = T("نسخه آزمایشی", "EXPERIMENTAL");
        OpenButton.Content = T("+  باز کردن تصویر", "+  Open image"); SizeStepButton.Content = T("۱  تغییر اندازه", "1  Resize"); ColorStepButton.Content = T("۲  کاهش رنگ", "2  Colors"); PatternStepButton.Content = T("۳  اختصاص بافت", "3  Patterns");
        FileCaption.Text = T("فایل ورودی", "INPUT FILE"); NoFileTitle.Text = T("شروع کار", "Get started"); NoFileHint.Text = T("ابتدا یک تصویر BMP یا TIFF باز کنید یا در پنجره رها کنید.", "Open or drop a BMP or TIFF image to start.");
        SizeTitle.Text = T("مرحله ۱ · تغییر اندازه", "Stage 1 · Resize"); SizeHint.Text = T("ابعاد خروجی را وارد و اعمال کنید. تصویر سمت راست نتیجه همین مرحله است.", "Set and apply the output dimensions; the preview shows the result.");
        WidthLabel.Text = T("عرض (پیکسل)", "Width (px)"); HeightLabel.Text = T("ارتفاع (پیکسل)", "Height (px)"); ApplySizeButton.Content = T("اعمال اندازه", "Apply size"); ToColorButton.Content = T("مرحله بعد: رنگ ←", "Next: colors →");
        SizeNotice.Text = T("روش تغییر اندازه نزدیک‌ترین همسایه است؛ نسبت تصویر خودکار حفظ نمی‌شود.", "Resize uses nearest-neighbor; aspect ratio is not locked.");
        ColorTitle.Text = T("مرحله ۲ · کاهش رنگ", "Stage 2 · Colors"); ColorHint.Text = T("ورودی این مرحله تصویر تغییراندازه‌یافته است؛ بیش از ۵۰ رنگ به‌طور خودکار کاهش می‌یابد.", "Input is the resized image; over 50 colors are reduced automatically.");
        ColorLimitLabel.Text = T("حداکثر تعداد رنگ (۱ تا ۵۰)", "Maximum colors (1–50)"); ApplyColorButton.Content = T("اعمال تعداد رنگ", "Apply color limit"); ToPatternButton.Content = T("مرحله بعد: بافت ←", "Next: patterns →");
        PatternTitle.Text = T("مرحله ۳ · اختصاص بافت", "Stage 3 · Patterns"); PatternHint.Text = T("برای هر رنگ یک پترن سیاه‌وسفید انتخاب کنید؛ بافت در تمام نقاط آن رنگ تکرار می‌شود.", "Choose a black-and-white pattern per color; it repeats wherever the color occurs."); SaveButton.Content = T("ذخیره تصویر نهایی", "Save final image");
        SafetyText.Text = T("خروجی تصویر است، نه فایل دستگاه بافندگی.", "Output is an image, not a loom file."); CanvasSubtitle.Text = T("خروجی هر مرحله در این قسمت نمایش داده می‌شود", "The result of each stage appears here");
        EmptyTitle.Text = T("طرح خود را وارد کنید", "Bring in your design"); EmptyHint.Text = T("یک تصویر BMP یا TIFF انتخاب کنید.", "Choose a BMP or TIFF image."); EmptyButton.Content = T("انتخاب تصویر", "Choose image");
        if (session is null) StatusText.Text = T("آماده برای باز کردن تصویر", "Ready to open an image");
    }
}
