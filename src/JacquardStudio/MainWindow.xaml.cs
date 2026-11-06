using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace JacquardStudio;

public partial class MainWindow : Window
{
    private enum Stage { Size, Colors, Patterns, Machine }
    private Stage stage = Stage.Size;
    private bool english, busy;
    private string? path;
    private string? projectPath;
    private string sourceName = "";
    private int machineFormatIndex = 1;
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
            path = filename; projectPath = null; sourceName = Path.GetFileName(filename); current = result; session = new WorkflowSession(result.Image); entries.Clear(); PaletteList.ItemsSource = null; ColorPaletteList.ItemsSource = null;
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
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (busy || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files || !ConfirmDiscard()) return;
        if (Path.GetExtension(files[0]).Equals(".sam", StringComparison.OrdinalIgnoreCase)) _ = LoadProjectAsync(files[0]);
        else _ = LoadAsync(files[0], 0);
    }
    private void Inputs_Changed(object sender, TextChangedEventArgs e) { if (IsInitialized) RefreshControls(); }
    private void SetBusy(bool value) { busy = value; RefreshControls(); }
    private void RefreshControls()
    {
        if (!IsInitialized) return;
        OpenButton.IsEnabled = !busy; OpenProjectButton.IsEnabled = !busy; SaveProjectButton.IsEnabled = !busy && session is not null; EmptyButton.IsEnabled = !busy; SettingsBorder.IsEnabled = !busy;
        SizeStepButton.IsEnabled = !busy && session is not null;
        ColorStepButton.IsEnabled = !busy && session?.Resized is not null && !SizeDirty;
        PatternStepButton.IsEnabled = !busy && session?.Palette is not null && !SizeDirty && !ColorsDirty;
        MachineStepButton.IsEnabled = !busy;
        MachineExportButton.IsEnabled = !busy && stage == Stage.Machine && machineFormatIndex == 1 && session is not null &&
            (MachineUseSource.IsChecked == true || (session.CanSave && !SizeDirty && !ColorsDirty));
        SaveSizeButton.IsEnabled = !busy && stage == Stage.Size && session?.Resized is not null && !SizeDirty;
        SaveColorButton.IsEnabled = !busy && stage == Stage.Colors && session?.ColorPreview is not null && !SizeDirty && !ColorsDirty;
        ToColorButton.IsEnabled = ColorStepButton.IsEnabled; ToPatternButton.IsEnabled = PatternStepButton.IsEnabled;
        SaveButton.IsEnabled = !busy && stage == Stage.Patterns && session?.PatternPreview is not null && !SizeDirty && !ColorsDirty;
        PreviousPage.IsEnabled = !busy && current is not null && current.PageIndex > 0;
        NextPage.IsEnabled = !busy && current is not null && current.PageIndex + 1 < current.PageCount;
        PageValue.Text = current is null ? "—" : $"{current.PageIndex + 1} / {current.PageCount}";
        foreach (var pair in new[] { (SizeStepButton, Stage.Size), (ColorStepButton, Stage.Colors), (PatternStepButton, Stage.Patterns), (MachineStepButton, Stage.Machine) })
        {
            pair.Item1.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString((session is not null || stage == Stage.Machine) && pair.Item2 == stage ? "#31574F" : "#263749"));
            pair.Item1.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString((session is not null || stage == Stage.Machine) && pair.Item2 == stage ? "#54E0C2" : "#374C60"));
        }
    }
    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "SAM project (*.sam)|*.sam", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true && ConfirmDiscard()) _ = LoadProjectAsync(dialog.FileName);
    }
    private async Task LoadProjectAsync(string filename)
    {
        SetBusy(true); StatusText.Text = T("در حال باز کردن پروژه…", "Opening project…");
        try
        {
            var state = await Task.Run(() => ProjectFile.Load(filename));
            // Only replace current work after the complete archive has been checked.
            session = state.Session; path = filename; projectPath = filename; sourceName = state.SourceName;
            current = new ImageResult(session.Source, 1, 0, "SAM");
            WidthInput.Text = state.WidthInput; HeightInput.Text = state.HeightInput; ColorInput.Text = state.ColorInput;
            english = state.English; stage = Enum.Parse<Stage>(state.Stage); machineFormatIndex = state.MachineFormatIndex;
            switch (machineFormatIndex) { case 0: FormatJC4.IsChecked = true; break; case 1: FormatJC5.IsChecked = true; break; case 2: FormatZC5.IsChecked = true; break; case 3: FormatEP.IsChecked = true; break; }
            entries = new List<PaletteEntry>();
            if (session.Palette is { } palette)
            {
                for (int i = 0; i < palette.Colors.Length; i++)
                {
                    int c = palette.Colors[i];
                    var entry = new PaletteEntry { Index = i, ColorLabel = $"#{c:X6} · {palette.Counts[i]:N0} px", Brush = new SolidColorBrush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)) };
                    if (session.Patterns[i] is { } pattern) entry.Restore(state.PatternNames[i] ?? "pattern", pattern);
                    entries.Add(entry);
                }
            }
            PaletteList.ItemsSource = entries; ColorPaletteList.ItemsSource = entries;
            FileValue.Text = Path.GetFileName(filename); FormatValue.Text = "SAM"; SetZoom(1); UpdateLabels(); ShowStage();
            StatusText.Text = T("پروژه باز شد؛ تصویر و پترن‌ها در پروژه قرار دارند.", "Project opened; the image and patterns are embedded.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or OverflowException or FormatException)
        { MessageBox.Show(this, T("باز کردن پروژه ممکن نشد: ", "Could not open project: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); StatusText.Text = T("باز کردن پروژه ناموفق بود.", "Could not open project."); }
        finally { SetBusy(false); }
    }
    private async void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session is null || path is null) return;
        var dialog = new SaveFileDialog { Filter = "SAM project (*.sam)|*.sam", AddExtension = true, DefaultExt = ".sam", OverwritePrompt = true, FileName = projectPath is null ? Path.GetFileNameWithoutExtension(sourceName) + ".sam" : Path.GetFileName(projectPath) };
        if (dialog.ShowDialog(this) != true) return;
        var names = session.Patterns.Select((p, i) => p is null ? null : entries[i].PatternLabel).ToArray();
        var state = new ProjectState(session, sourceName, WidthInput.Text, HeightInput.Text, ColorInput.Text, stage.ToString(), english, machineFormatIndex, names);
        var protectedPaths = entries.Select(x => x.PatternPath).OfType<string>().ToList();
        if (!string.Equals(path, projectPath, StringComparison.OrdinalIgnoreCase)) protectedPaths.Add(path);
        SetBusy(true); StatusText.Text = T("در حال ذخیره پروژه…", "Saving project…");
        try
        {
            await Task.Run(() => ProjectFile.Save(dialog.FileName, state, protectedPaths));
            projectPath = dialog.FileName; StatusText.Text = T("پروژه ذخیره شد.", "Project saved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        { MessageBox.Show(this, T("ذخیره پروژه ممکن نشد: ", "Could not save project: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); StatusText.Text = T("ذخیره پروژه ناموفق بود.", "Project save failed."); }
        finally { SetBusy(false); }
    }
    private void MachineFormat_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as RadioButton)?.Tag is string tag && int.TryParse(tag, out int selected)) machineFormatIndex = selected;
        if (IsInitialized) RefreshControls();
    }
    private void MachineSource_Changed(object sender, RoutedEventArgs e) { if (IsInitialized) ShowStage(); }
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
    private void MachineStep_Click(object sender, RoutedEventArgs e) { if (!busy) { stage = Stage.Machine; ShowStage(); } }
    private void ShowStage()
    {
        bool hasFile = session is not null;
        NoFilePanel.Visibility = !hasFile && stage != Stage.Machine ? Visibility.Visible : Visibility.Collapsed;
        MachinePanel.Visibility = stage == Stage.Machine ? Visibility.Visible : Visibility.Collapsed;
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
                Stage.Machine => MachineUseSource.IsChecked == true ? session.Source : session.PatternPreview ?? session.ColorPreview ?? session.Resized ?? session.Source,
                _ => session.Source
            };
            PreviewImage.Source = view; ImageScroll.Visibility = Visibility.Visible; EmptyState.Visibility = Visibility.Collapsed;
            PreviewDetails.Text = $"{view.PixelWidth:N0} × {view.PixelHeight:N0} px";
        }
        CanvasTitle.Text = stage == Stage.Machine ? T("پیش‌نمایش طرح فعلی", "Current design preview") : !hasFile ? T("پیش‌نمایش طرح", "Design preview") : stage switch
        {
            Stage.Size => T("پیش‌نمایش اندازه", "Size preview"),
            Stage.Colors => T("پیش‌نمایش رنگ‌ها", "Color preview"),
            _ => T("پیش‌نمایش بافت", "Pattern preview")
        };
        SafetyText.Text = stage == Stage.Machine
            ? T("فایل JC5 آزمایشی است؛ قبل از تولید مستقل بررسی شود.", "JC5 is experimental; verify independently before production.")
            : T("خروجی این مرحله تصویر است، نه فایل دستگاه بافندگی.", "This stage outputs an image, not a loom file.");
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
            var pattern = await Task.Run(() => { var input = ImageLoader.Load(dialog.FileName, 0); if (input.PageCount != 1) throw new InvalidDataException("Multi-page pattern files are not supported."); var p = DesignProcessor.ReadPattern(input.Image); session.AssignPattern(entry.Index, p); return p; });
            entry.Assign(dialog.FileName, pattern); ShowStage(); StatusText.Text = T("پترن روی همه نقاط این رنگ اعمال شد.", "Pattern applied to every pixel of this color.");
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
        if (busy || session is null || path is null) return;
        BitmapSource? frame = stage switch
        {
            Stage.Size when !SizeDirty => session.Resized,
            Stage.Colors when !SizeDirty && !ColorsDirty => session.ColorPreview,
            Stage.Patterns when !SizeDirty && !ColorsDirty => session.PatternPreview,
            _ => null
        };
        if (frame is null) return;
        if (stage == Stage.Patterns && !session.CanSave && MessageBox.Show(this,
            T("همه رنگ‌ها بافت ندارند. در تصویر ذخیره‌شده رنگ‌های بدون بافت بدون تغییر باقی می‌مانند. ادامه می‌دهید؟", "Not all colors have patterns. Unassigned colors remain unchanged in the saved image. Continue?"),
            Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var defaultName = stage switch { Stage.Size => "resized.bmp", Stage.Colors => "colors.bmp", _ => "prepared-design.bmp" };
        var dialog = new SaveFileDialog { Filter = "Bitmap (*.bmp)|*.bmp|TIFF (*.tif)|*.tif", AddExtension = true, OverwritePrompt = true, FileName = defaultName };
        if (dialog.ShowDialog(this) != true) return;
        var protectedPaths = new[] { path }.Concat(entries.Select(x => x.PatternPath).OfType<string>()).ToArray();
        SetBusy(true);
        try
        {
            bool tiff = dialog.FilterIndex == 2;
            await Task.Run(() => ImageExporter.Save(frame, dialog.FileName, tiff, protectedPaths));
            StatusText.Text = T("تصویر این مرحله ذخیره شد؛ این فایل ماشین نیست.", "Stage image saved; this is not a loom file.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        { MessageBox.Show(this, T("ذخیره ناموفق بود: ", "Save failed: ") + ex.Message); }
        finally { SetBusy(false); }
    }
    private async void MachineExport_Click(object sender, RoutedEventArgs e)
    {
        if (busy || machineFormatIndex != 1 || session is null || path is null) return;
        BitmapSource? frame = MachineUseSource.IsChecked == true ? session.Source :
            session.CanSave && !SizeDirty && !ColorsDirty ? session.PatternPreview : null;
        if (frame is null) return;
        string warning = T($"خروجی JC5 آزمایشی و تک‌ناحیه‌ای است: {frame.PixelWidth} قلاب و {frame.PixelHeight} رج، سیاه = بیت ۱. نگاشت یا لبه دستگاه به آن اضافه نمی‌شود. ابتدا با DesignScope و بدون تولید بررسی کنید. ادامه می‌دهید؟",
            $"Experimental single-area JC5: {frame.PixelWidth} hooks and {frame.PixelHeight} picks, black = bit 1. No machine mapping or selvedges are added. Verify in DesignScope before production. Continue?");
        if (MessageBox.Show(this, warning, Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var dialog = new SaveFileDialog { Filter = "JC5 (*.jc5)|*.jc5", DefaultExt = ".jc5", AddExtension = true, OverwritePrompt = true, FileName = "design.jc5" };
        if (dialog.ShowDialog(this) != true) return;
        var protectedPaths = new[] { path }.Concat(projectPath is null ? Array.Empty<string>() : new[] { projectPath }).Concat(entries.Select(x => x.PatternPath).OfType<string>()).ToArray();
        SetBusy(true);
        try
        {
            await Task.Run(() => Jc5Codec.Save(frame, dialog.FileName, protectedPaths));
            StatusText.Text = T("JC5 آزمایشی ذخیره شد؛ پیش از تولید آن را در DesignScope بررسی کنید.", "Experimental JC5 saved; verify it in DesignScope before production.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        { MessageBox.Show(this, T("ذخیره JC5 ناموفق بود: ", "JC5 save failed: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private async void MachineImport_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "JC5 (*.jc5)|*.jc5", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || !ConfirmDiscard()) return;
        SetBusy(true);
        try
        {
            var image = await Task.Run(() => Jc5Codec.Load(dialog.FileName));
            var next = new WorkflowSession(image); next.ApplySize(image.PixelWidth, image.PixelHeight);
            session = next; path = dialog.FileName; projectPath = null; sourceName = Path.GetFileName(path);
            current = new ImageResult(image, 1, 0, "JC5 (experimental)"); entries.Clear(); PaletteList.ItemsSource = null; ColorPaletteList.ItemsSource = null;
            WidthInput.Text = image.PixelWidth.ToString(); HeightInput.Text = image.PixelHeight.ToString(); ColorInput.Text = "50";
            FileValue.Text = sourceName; FormatValue.Text = "JC5 · experimental"; stage = Stage.Size; SetZoom(1); ShowStage();
            StatusText.Text = T("JC5 باز شد؛ از مرحله اندازه می‌توانید نقشه را BMP یا TIFF ذخیره کنید. داده‌های غیرتصویری و بیت‌های اضافی حفظ نمی‌شوند.", "JC5 opened; save the hook map as BMP or TIFF from the size stage. Non-image data and padding bits are not preserved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or OverflowException)
        { MessageBox.Show(this, T("خواندن JC5 ممکن نشد: ", "Could not read JC5: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
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
        LanguageButton.Content = english ? "فارسی" : "English"; AboutButton.Content = T("درباره", "About"); OpenProjectButton.Content = T("باز کردن پروژه", "Open project"); SaveProjectButton.Content = T("ذخیره پروژه", "Save project"); ColorColumn.Header = T("رنگ", "Color"); PatternColumn.Header = T("بافت", "Pattern");
        SubtitleText.Text = T("فضای کار طراحی و بازرسی ژاکارد", "Jacquard design workspace"); BadgeText.Text = T("نسخه آزمایشی", "EXPERIMENTAL");
        OpenButton.Content = T("+  باز کردن تصویر", "+  Open image"); SizeStepButton.Content = T("۱  تغییر اندازه", "1  Resize"); ColorStepButton.Content = T("۲  کاهش رنگ", "2  Colors"); PatternStepButton.Content = T("۳  اختصاص بافت", "3  Patterns"); MachineStepButton.Content = T("۴  فایل ماشین", "4  Loom file");
        FileCaption.Text = T("فایل ورودی", "INPUT FILE"); NoFileTitle.Text = T("شروع کار", "Get started"); NoFileHint.Text = T("ابتدا یک تصویر BMP یا TIFF باز کنید یا در پنجره رها کنید.", "Open or drop a BMP or TIFF image to start.");
        SizeTitle.Text = T("مرحله ۱ · تغییر اندازه", "Stage 1 · Resize"); SizeHint.Text = T("ابعاد خروجی را وارد و اعمال کنید. تصویر سمت چپ نتیجه همین مرحله است.", "Set and apply the output dimensions; the preview shows the result.");
        WidthLabel.Text = T("عرض (پیکسل)", "Width (px)"); HeightLabel.Text = T("ارتفاع (پیکسل)", "Height (px)"); ApplySizeButton.Content = T("اعمال اندازه", "Apply size"); SaveSizeButton.Content = T("ذخیره خروجی اندازه", "Save resized image"); ToColorButton.Content = T("مرحله بعد: رنگ ←", "Next: colors →");
        SizeNotice.Text = T("روش تغییر اندازه نزدیک‌ترین همسایه است؛ نسبت تصویر خودکار حفظ نمی‌شود.", "Resize uses nearest-neighbor; aspect ratio is not locked.");
        ColorTitle.Text = T("مرحله ۲ · کاهش رنگ", "Stage 2 · Colors"); ColorHint.Text = T("ورودی این مرحله تصویر تغییراندازه‌یافته است؛ بیش از ۵۰ رنگ به‌طور خودکار کاهش می‌یابد.", "Input is the resized image; over 50 colors are reduced automatically.");
        ColorLimitLabel.Text = T("حداکثر تعداد رنگ (۱ تا ۵۰)", "Maximum colors (1–50)"); ApplyColorButton.Content = T("اعمال تعداد رنگ", "Apply color limit"); SaveColorButton.Content = T("ذخیره خروجی رنگ", "Save color image"); ToPatternButton.Content = T("مرحله بعد: بافت ←", "Next: patterns →");
        PatternTitle.Text = T("مرحله ۳ · اختصاص بافت", "Stage 3 · Patterns"); PatternHint.Text = T("برای هر رنگ یک پترن سیاه‌وسفید انتخاب کنید؛ بافت در تمام نقاط آن رنگ تکرار می‌شود.", "Choose a black-and-white pattern per color; it repeats wherever the color occurs."); SaveButton.Content = T("ذخیره خروجی بافت", "Save pattern image");
        MachineTitle.Text = T("مرحله ۴ · فایل ماشین", "Stage 4 · Loom file"); MachineHint.Text = T("JC5 تک‌ناحیه‌ای بر اساس نمونه DesignScope به‌صورت آزمایشی فعال است؛ سایر قالب‌ها هنوز پشتیبانی نمی‌شوند.", "Experimental single-area JC5 based on DesignScope samples; other formats are not supported.");
        MachineFormatLabel.Text = T("قالب هدف", "Target format"); MachineUseSource.Content = T("استفاده از تصویر اصلی سیاه‌وسفید به جای نتیجه بافت", "Use original black-and-white image instead of pattern result"); MachineExportButton.Content = T("تبدیل و ذخیره فایل ماشین", "Convert and save loom file"); MachineImportButton.Content = T("انتخاب فایل ماشین", "Select loom file");
        MachineNotice.Text = T("عرض تصویر = تعداد قلاب و ارتفاع = تعداد رج. رنگ سیاه بیت ۱ است. لبه، ناحیه اضافی و تنظیمات دستگاه اضافه نمی‌شوند. قبل از تولید در DesignScope و روی دستگاه آزمایش کنید.", "Image width = hooks and height = picks; black is bit 1. No selvedge, extra areas or machine settings are added. Verify in DesignScope and on the controller before production.");
        SafetyText.Text = T("خروجی تصویر است، نه فایل دستگاه بافندگی.", "Output is an image, not a loom file."); CanvasSubtitle.Text = T("خروجی هر مرحله در این قسمت نمایش داده می‌شود", "The result of each stage appears here");
        EmptyTitle.Text = T("طرح خود را وارد کنید", "Bring in your design"); EmptyHint.Text = T("یک تصویر BMP یا TIFF انتخاب کنید.", "Choose a BMP or TIFF image."); EmptyButton.Content = T("انتخاب تصویر", "Choose image");
        if (session is null) StatusText.Text = T("آماده برای باز کردن تصویر", "Ready to open an image");
    }
}
