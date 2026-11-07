using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace JacquardStudio;

public partial class MainWindow : Window
{
    private enum Stage { Size, Colors, Patterns, Machine, Analysis, Floats }
    private Stage stage = Stage.Size;
    private bool english, busy, bankOpen, lightTheme;
    private int? bankTargetIndex;
    private readonly WeaveBank bank = new();
    private string? path;
    private string? projectPath;
    private string sourceName = "";
    private int machineFormatIndex = 1;
    private ImageResult? current;
    private WorkflowSession? session;
    private List<PaletteEntry> entries = new();
    private double zoom = 1;
    private DateTimeOffset? projectCreated;
    private DesignMetadata metadata = new();
    private WarpAnalysis? warpAnalysis;
    private BitmapSource? heatmap;
    private List<FloatRun>? floatRuns;
    private int floatIndex, lastFloatMinimum;
    private FloatDirection lastFloatDirection;
    private sealed record HistoryState(WorkflowSession Session, Stage Stage, string?[] Names, string?[] Paths);
    private readonly List<HistoryState> undoHistory = new(), redoHistory = new();

    public MainWindow() { InitializeComponent(); WeaveCanvas.Changed += (_, _) => RefreshControls(); lightTheme = ThemeManager.ReadLight(); ThemeManager.Apply(lightTheme, false); VersionText.Text = AppVersion.Display; UpdateLabels(); RefreshControls(); UpdateWindowTitle(); }
    private void UpdateWindowTitle() => Title = $"Jacquard Studio ({(projectPath is null ? T("پروژه جدید", "New project") : Path.GetFileNameWithoutExtension(projectPath))})";
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
            path = filename; projectPath = null; projectCreated = DateTimeOffset.Now; metadata = new(); ClearHistory(); sourceName = Path.GetFileName(filename); current = result; session = new WorkflowSession(result.Image); RebuildPaletteEntries();
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
    private void Window_DragOver(object sender, DragEventArgs e) { e.Effects = !busy && !bankOpen && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (busy || bankOpen || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files || !ConfirmDiscard()) return;
        if (Path.GetExtension(files[0]).Equals(".sam", StringComparison.OrdinalIgnoreCase)) _ = LoadProjectAsync(files[0]);
        else _ = LoadAsync(files[0], 0);
    }
    private void Inputs_Changed(object sender, TextChangedEventArgs e) { if (IsInitialized) RefreshControls(); }
    private void SetBusy(bool value) { busy = value; RefreshControls(); }
    private void RefreshControls()
    {
        if (!IsInitialized) return;
        OpenButton.IsEnabled = !busy; OpenProjectButton.IsEnabled = !busy && !bankOpen; SaveProjectButton.IsEnabled = !busy && session is not null; SaveProjectAsButton.IsEnabled = !busy && session is not null; BankButton.IsEnabled = !busy; EmptyButton.IsEnabled = !busy; SettingsBorder.IsEnabled = !busy; BankWorkspace.IsEnabled = !busy;
        SizeStepButton.IsEnabled = !busy && session is not null;
        ColorStepButton.IsEnabled = !busy && session?.Resized is not null && !SizeDirty;
        PatternStepButton.IsEnabled = !busy && session?.Palette is not null && !SizeDirty && !ColorsDirty;
        MachineStepButton.IsEnabled = !busy;
        AnalysisStepButton.IsEnabled = FloatStepButton.IsEnabled = !busy && session?.CanSave == true && !SizeDirty && !ColorsDirty;
        InfoButton.IsEnabled = !busy && !bankOpen && session is not null;
        FloatPreviousButton.IsEnabled = !busy && floatRuns is { Count: > 0 } && floatIndex > 0;
        FloatNextButton.IsEnabled = !busy && floatRuns is { Count: > 0 } && floatIndex < floatRuns.Count - 1;
        MachineExportButton.IsEnabled = !busy && stage == Stage.Machine && (machineFormatIndex is 1 or 4) && session is not null &&
            (MachineUseSource.IsChecked == true || (session.CanSave && !SizeDirty && !ColorsDirty));
        SaveSizeButton.IsEnabled = !busy && stage == Stage.Size && session?.Resized is not null && !SizeDirty;
        SaveColorButton.IsEnabled = !busy && stage == Stage.Colors && session?.ColorPreview is not null && !SizeDirty && !ColorsDirty;
        ToColorButton.IsEnabled = ColorStepButton.IsEnabled; ToPatternButton.IsEnabled = PatternStepButton.IsEnabled;
        SaveButton.IsEnabled = !busy && stage == Stage.Patterns && session?.PatternPreview is not null && !SizeDirty && !ColorsDirty;
        int selectedCount = ColorPaletteList.SelectedItems.Count;
        bool canEditColors = !busy && stage == Stage.Colors && !SizeDirty && !ColorsDirty && session?.Palette?.Colors.Length > 1;
        DeleteColorButton.IsEnabled = canEditColors && selectedCount == 1;
        MergeColorButton.IsEnabled = canEditColors && selectedCount == 2;
        ColorSelectionHint.Text = selectedCount > 2 ? T("برای ادغام فقط دو رنگ را انتخاب کنید.", "Select only two colors to merge.") : T("برای ادغام، دو رنگ را انتخاب کنید.", "Select two colors to merge.");
        UpdateBankButtons();
        UndoButton.IsEnabled = !busy && (bankOpen ? WeaveCanvas.CanUndo : undoHistory.Count > 0);
        RedoButton.IsEnabled = !busy && (bankOpen ? WeaveCanvas.CanRedo : redoHistory.Count > 0);
        ReportButton.IsEnabled = !busy && !bankOpen && session?.Palette is not null && !SizeDirty && !ColorsDirty;
        var dimensions = session?.Resized ?? session?.Source;
        StatsText.Text = dimensions is null ? "" : T($"تار {dimensions.PixelWidth:N0} · پود {dimensions.PixelHeight:N0} · {(long)dimensions.PixelWidth * dimensions.PixelHeight:N0} پیکسل · {session?.Palette?.Colors.Length ?? 0} رنگ", $"Warp {dimensions.PixelWidth:N0} · Weft {dimensions.PixelHeight:N0} · {(long)dimensions.PixelWidth * dimensions.PixelHeight:N0} px · {session?.Palette?.Colors.Length ?? 0} colors");
        PreviousPage.IsEnabled = !busy && current is not null && current.PageIndex > 0;
        NextPage.IsEnabled = !busy && current is not null && current.PageIndex + 1 < current.PageCount;
        PageValue.Text = current is null ? "—" : $"{current.PageIndex + 1} / {current.PageCount}";
        foreach (var pair in new[] { (SizeStepButton, Stage.Size), (ColorStepButton, Stage.Colors), (PatternStepButton, Stage.Patterns), (MachineStepButton, Stage.Machine), (AnalysisStepButton, Stage.Analysis), (FloatStepButton, Stage.Floats) })
        {
            bool active = (session is not null || stage == Stage.Machine) && pair.Item2 == stage;
            pair.Item1.Background = active ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(lightTheme ? "#D2EFE7" : "#31574F")) : (Brush)FindResource("ButtonBackground");
            pair.Item1.BorderBrush = active ? (Brush)FindResource("Accent") : (Brush)FindResource("ButtonBorder");
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
            session = state.Session; path = filename; projectPath = filename; projectCreated = state.CreatedAt; metadata = state.Metadata ?? new(); ClearHistory(); sourceName = state.SourceName;
            current = new ImageResult(session.Source, 1, 0, "SAM");
            WidthInput.Text = state.WidthInput; HeightInput.Text = state.HeightInput; ColorInput.Text = state.ColorInput;
            english = state.English; stage = Enum.Parse<Stage>(state.Stage); machineFormatIndex = state.MachineFormatIndex;
            switch (machineFormatIndex) { case 0: FormatJC4.IsChecked = true; break; case 1: FormatJC5.IsChecked = true; break; case 2: FormatZC5.IsChecked = true; break; case 3: FormatEP.IsChecked = true; break; case 4: FormatJC6.IsChecked = true; break; }
            RebuildPaletteEntries(state.PatternNames);
            FileValue.Text = Path.GetFileName(filename); FormatValue.Text = "SAM"; SetZoom(1); UpdateLabels(); ShowStage();
            StatusText.Text = T("پروژه باز شد؛ تصویر و پترن‌ها در پروژه قرار دارند.", "Project opened; the image and patterns are embedded.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or OverflowException or FormatException)
        { MessageBox.Show(this, T("باز کردن پروژه ممکن نشد: ", "Could not open project: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); StatusText.Text = T("باز کردن پروژه ناموفق بود.", "Could not open project."); }
        finally { SetBusy(false); }
    }
    private async void SaveProject_Click(object sender, RoutedEventArgs e) => await SaveProjectAsync(false);
    private async void SaveProjectAs_Click(object sender, RoutedEventArgs e) => await SaveProjectAsync(true);
    private async Task SaveProjectAsync(bool saveAs)
    {
        if (busy || session is null || path is null) return;
        string? destination = projectPath;
        if (ProjectSavePolicy.RequiresDialog(destination, saveAs))
        {
            var dialog = new SaveFileDialog { Filter = "SAM project (*.sam)|*.sam", AddExtension = true, DefaultExt = ".sam", OverwritePrompt = true, FileName = projectPath is null ? Path.GetFileNameWithoutExtension(sourceName) + ".sam" : Path.GetFileName(projectPath) };
            if (dialog.ShowDialog(this) != true) return;
            destination = dialog.FileName;
        }
        if (string.IsNullOrEmpty(destination)) return;
        var names = session.Patterns.Select((p, i) => p is null ? null : entries[i].PatternLabel).ToArray();
        var state = new ProjectState(session, sourceName, WidthInput.Text, HeightInput.Text, ColorInput.Text, stage.ToString(), english, machineFormatIndex, names, projectCreated, metadata);
        var protectedPaths = entries.Select(x => x.PatternPath).OfType<string>().ToList();
        if (!string.Equals(path, projectPath, StringComparison.OrdinalIgnoreCase)) protectedPaths.Add(path);
        SetBusy(true); StatusText.Text = T("در حال ذخیره پروژه…", "Saving project…");
        try
        {
            await Task.Run(() => ProjectFile.Save(destination, state, protectedPaths));
            projectPath = destination; UpdateWindowTitle(); StatusText.Text = T("پروژه ذخیره شد.", "Project saved.");
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
    private void AnalysisStep_Click(object sender, RoutedEventArgs e) { if (!busy && session?.CanSave == true && !SizeDirty && !ColorsDirty) { stage = Stage.Analysis; ShowStage(); } }
    private void FloatStep_Click(object sender, RoutedEventArgs e) { if (!busy && session?.CanSave == true && !SizeDirty && !ColorsDirty) { stage = Stage.Floats; ShowStage(); } }
    private void ShowStage()
    {
        HoverCard.Visibility = Visibility.Collapsed;
        WorkflowRoot.Visibility = bankOpen ? Visibility.Collapsed : Visibility.Visible;
        BankWorkspace.Visibility = bankOpen ? Visibility.Visible : Visibility.Collapsed;
        UpdateWindowTitle();
        bool hasFile = session is not null;
        NoFilePanel.Visibility = !hasFile && stage != Stage.Machine ? Visibility.Visible : Visibility.Collapsed;
        MachinePanel.Visibility = stage == Stage.Machine ? Visibility.Visible : Visibility.Collapsed;
        MachineScroll.Visibility = MachinePanel.Visibility;
        SizePanel.Visibility = hasFile && stage == Stage.Size ? Visibility.Visible : Visibility.Collapsed;
        ColorPanel.Visibility = hasFile && stage == Stage.Colors ? Visibility.Visible : Visibility.Collapsed;
        ColorScroll.Visibility = ColorPanel.Visibility;
        AnalysisPanel.Visibility = stage == Stage.Analysis ? Visibility.Visible : Visibility.Collapsed;
        FloatPanel.Visibility = stage == Stage.Floats ? Visibility.Visible : Visibility.Collapsed;
        bool showPatterns = hasFile && stage == Stage.Patterns && session?.Palette is not null;
        RegularSettings.Visibility = showPatterns ? Visibility.Collapsed : Visibility.Visible;
        PatternWorkspace.Visibility = showPatterns ? Visibility.Visible : Visibility.Collapsed;
        if (session is null) { PreviewImage.Source = null; ImageScroll.Visibility = Visibility.Collapsed; EmptyState.Visibility = Visibility.Visible; }
        else
        {
            BitmapSource view = stage switch
            {
                Stage.Size => session.Resized ?? session.Source,
                Stage.Colors => session.ColorPreview ?? session.Resized ?? session.Source,
                Stage.Analysis => heatmap ?? session.PatternPreview ?? session.Source,
                Stage.Floats => session.PatternPreview ?? session.Source,
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
            Stage.Analysis => T("نقشه حرارتی تارها", "Warp heatmap"),
            Stage.Floats => T("پیش‌نمایش و اصلاح فلوت", "Float inspection and editing"),
            _ => T("پیش‌نمایش بافت", "Pattern preview")
        };
        SafetyText.Text = stage == Stage.Machine
            ? T("فایل JC5 آزمایشی است؛ قبل از تولید مستقل بررسی شود.", "JC5 is experimental; verify independently before production.")
            : T("خروجی این مرحله تصویر است، نه فایل دستگاه بافندگی.", "This stage outputs an image, not a loom file.");
        UpdateSummary(); RefreshControls();
    }
    private bool ConfirmPatternLoss()
    {
        if (session?.Patterns.Any(p => p is not null) != true && session?.PixelOverrides is null) return true;
        return MessageBox.Show(this, T("تغییر این مرحله بافت‌های انتخاب‌شده و اصلاح نقاط فلوت را پاک می‌کند. ادامه می‌دهید؟", "Changing this stage clears assigned weaves and edited float points. Continue?"), Title,
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
    private async void ApplySize_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session is null) return;
        if (!int.TryParse(WidthInput.Text, out int w) || !int.TryParse(HeightInput.Text, out int h) || w <= 0 || h <= 0 || (long)w * h > DesignProcessor.MaxPixels)
        { MessageBox.Show(this, T("ابعاد مثبت تا مجموع ۴ میلیون پیکسل وارد کنید.", "Enter positive dimensions totaling at most 4 million pixels.")); return; }
        if (!ConfirmPatternLoss()) return;
        var before = CaptureHistory(); SetBusy(true); StatusText.Text = T("در حال تغییر اندازه…", "Resizing…");
        try
        {
            await Task.Run(() => session.ApplySize(w, h)); RecordHistory(before); RebuildPaletteEntries(); stage = Stage.Size; ShowStage();
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
        var before = CaptureHistory(); SetBusy(true); StatusText.Text = T("در حال بررسی رنگ‌ها…", "Analyzing colors…");
        try
        {
            await Task.Run(() => session.ApplyColors(colors)); RecordHistory(before);
            RebuildPaletteEntries(); stage = Stage.Colors; ShowStage();
            StatusText.Text = T("رنگ‌ها اعمال شدند. مرحله بافت را انتخاب کنید.", "Colors applied. Continue to patterns.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(this, T("کاهش رنگ ممکن نشد: ", "Color reduction failed: ") + ex.Message); StatusText.Text = T("کاهش رنگ ناموفق بود.", "Color reduction failed."); }
        finally { SetBusy(false); }
    }
    private void RebuildPaletteEntries(string?[]? restoredNames = null, IReadOnlyList<PaletteEntry>? previous = null, int removedIndex = -1)
    {
        if (session?.Palette is not { } palette) { entries.Clear(); ColorPaletteList.ItemsSource = null; PatternCards.ItemsSource = null; return; }
        var next = new List<PaletteEntry>();
        for (int i = 0; i < palette.Colors.Length; i++)
        {
            int c = palette.Colors[i], count = palette.Counts[i];
            var entry = new PaletteEntry { Index = i, ColorCode = $"#{c:X6}", PixelCount = count, Percent = 100d * count / palette.Indices.Length,
                ColorLabel = $"#{c:X6} · {count:N0} px", Brush = new SolidColorBrush(Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c)) };
            if (session.Patterns[i] is { } pattern)
            {
                int oldIndex = removedIndex >= 0 && i >= removedIndex ? i + 1 : i;
                var old = previous is not null && oldIndex < previous.Count ? previous[oldIndex] : null;
                if (old?.PatternPath is { } originalPath) entry.Assign(originalPath, pattern);
                else entry.Restore(restoredNames is not null ? restoredNames[i] ?? "pattern" : old?.PatternLabel ?? "pattern", pattern);
            }
            next.Add(entry);
        }
        entries = next; ColorPaletteList.ItemsSource = entries; PatternCards.ItemsSource = entries;
    }
    private void ColorSelection_Changed(object sender, SelectionChangedEventArgs e) { if (IsInitialized) RefreshControls(); }
    private async void DeleteColor_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is not { } palette || ColorPaletteList.SelectedItems.Count != 1 || ColorPaletteList.SelectedItem is not PaletteEntry source || palette.Colors.Length < 2 || SizeDirty || ColorsDirty) return;
        int target = DesignProcessor.NearestRemainingColor(palette, source.Index);
        await MergeColorsAsync(source.Index, target, true);
    }
    private async void MergeColor_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || ColorPaletteList.SelectedItems.Count != 2 || SizeDirty || ColorsDirty) return;
        if (ColorPaletteList.SelectedItems[0] is not PaletteEntry first || ColorPaletteList.SelectedItems[1] is not PaletteEntry second) return;
        var choice = new MergeChoiceWindow(first, second, english) { Owner = this };
        if (choice.ShowDialog() != true || choice.Destination is not int target) return;
        int source = target == first.Index ? second.Index : first.Index;
        await MergeColorsAsync(source, target, false);
    }
    private async Task MergeColorsAsync(int source, int target, bool deleting)
    {
        if (session?.Palette is not { } palette) return;
        string from = $"#{palette.Colors[source]:X6}", to = $"#{palette.Colors[target]:X6}";
        bool sourceHasPattern = session.Patterns[source] is not null;
        string message = deleting
            ? T($"رنگ {from} حذف می‌شود و تمام پیکسل‌های آن به نزدیک‌ترین رنگ {to} تغییر می‌کنند.", $"Color {from} will be removed and all its pixels reassigned to nearest color {to}.")
            : T($"تمام پیکسل‌های {from} به {to} تبدیل می‌شوند و رنگ {from} حذف می‌شود.", $"All {from} pixels will become {to}; {from} will be removed.");
        if (sourceHasPattern) message += T(" بافت رنگ حذف‌شده نیز حذف و بافت مقصد حفظ می‌شود.", " The removed color's pattern is discarded; the target pattern is kept.");
        if (MessageBox.Show(this, message + T(" ادامه می‌دهید؟", " Continue?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var prior = entries.ToArray(); var before = CaptureHistory(); SetBusy(true);
        try
        {
            await Task.Run(() => session.MergeColors(source, target)); RecordHistory(before);
            RebuildPaletteEntries(previous: prior, removedIndex: source); ShowStage();
            StatusText.Text = T("رنگ‌ها به‌روزرسانی شدند؛ تصویر و تعداد پیکسل‌ها اصلاح شدند.", "Colors updated; preview and pixel counts recalculated.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(this, T("تغییر رنگ ممکن نشد: ", "Could not change colors: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private HistoryState CaptureHistory() => new(session!.Snapshot(), stage, entries.Select(x => (string?)x.PatternLabel).ToArray(), entries.Select(x => x.PatternPath).ToArray());
    private void ClearHistory() { undoHistory.Clear(); redoHistory.Clear(); InvalidateAnalysis(); }
    private void InvalidateAnalysis() { warpAnalysis = null; heatmap = null; floatRuns = null; floatIndex = 0; if (IsInitialized) { AnalysisResultText.Text = T("برای محاسبه، دکمه بالا را بزنید.", "Run analysis to calculate."); FloatResultText.Text = T("هنوز بررسی نشده است.", "Not scanned yet."); FloatCurrentText.Text = ""; } }
    private void RecordHistory(HistoryState state) { InvalidateAnalysis(); undoHistory.Add(state); if (undoHistory.Count > 3) undoHistory.RemoveAt(0); redoHistory.Clear(); }
    private void RestoreHistory(HistoryState state)
    {
        session = state.Session.Snapshot(); stage = state.Stage; InvalidateAnalysis();
        if (session.Resized is not null) { WidthInput.Text = session.Resized.PixelWidth.ToString(); HeightInput.Text = session.Resized.PixelHeight.ToString(); }
        if (session.AppliedColorLimit is int limit) ColorInput.Text = limit.ToString();
        RebuildPaletteEntries(state.Names);
        for (int i = 0; i < entries.Count; i++) if (state.Paths[i] is string path && session.Patterns[i] is { } pattern) entries[i].Assign(path, pattern);
        ShowStage(); StatusText.Text = T("تاریخچه تغییرات بازیابی شد.", "History state restored.");
    }
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (bankOpen) { WeaveCanvas.Undo(); return; }
        if (session is null || undoHistory.Count == 0) return;
        redoHistory.Add(CaptureHistory()); var state = undoHistory[^1]; undoHistory.RemoveAt(undoHistory.Count - 1); RestoreHistory(state);
    }
    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (bankOpen) { WeaveCanvas.Redo(); return; }
        if (session is null || redoHistory.Count == 0) return;
        undoHistory.Add(CaptureHistory()); var state = redoHistory[^1]; redoHistory.RemoveAt(redoHistory.Count - 1); RestoreHistory(state);
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || Keyboard.FocusedElement is TextBox) return;
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) { Redo_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Z) { Undo_Click(sender, e); e.Handled = true; }
        else if (e.Key == Key.Y) { Redo_Click(sender, e); e.Handled = true; }
    }
    private void EditorHelper_Click(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button { Tag: string action }) return;
        if (!int.TryParse(StepXInput.Text, out int dx) || !int.TryParse(StepYInput.Text, out int dy) || dx < 1 || dy < 1 || dx > 64 || dy > 64)
        { MessageBox.Show(this, T("گام‌های X و Y باید بین ۱ تا ۶۴ باشند.", "X and Y steps must be between 1 and 64.")); return; }
        try { WeaveCanvas.ApplyHelper(action, dx, dy); }
        catch (ArgumentException ex) { MessageBox.Show(this, T("ابعاد پاناما باید مضرب ۴ و ابعاد ساتین مضرب ۵ باشند. ", "Panama dimensions must be multiples of 4 and satin dimensions multiples of 5. ") + ex.Message); }
    }
    private void EditorGrid_Click(object sender, RoutedEventArgs e) => WeaveCanvas.GridVisible = !WeaveCanvas.GridVisible;
    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (session?.Palette is not { } palette || SizeDirty || ColorsDirty || stage is not (Stage.Patterns or Stage.Analysis or Stage.Floats)) { HoverCard.Visibility = Visibility.Collapsed; return; }
        var point = e.GetPosition(PreviewImage);
        var hit = PreviewHitTest.Find(palette, point.X, point.Y, PreviewImage.ActualWidth, PreviewImage.ActualHeight);
        if (hit is null) { HoverCard.Visibility = Visibility.Collapsed; return; }
        var (x, y, index) = hit.Value; var entry = entries[index];
        if (stage == Stage.Analysis && warpAnalysis is { } analysis)
            HoverDetails.Text = T($"تار {x} · سیاه: {analysis.Counts[x]:N0}\nمیانگین: {analysis.Mean:0.##} · اختلاف: {analysis.DeviationPercent[x]:+0.##;-0.##;0}%", $"Warp {x} · black: {analysis.Counts[x]:N0}\nMean: {analysis.Mean:0.##} · deviation: {analysis.DeviationPercent[x]:+0.##;-0.##;0}%");
        else
            HoverDetails.Text = T($"رنگ: {entry.ColorCode}\nتعداد پیکسل رنگ: {entry.PixelCount:N0} ({entry.PercentText})\nبافت: {entry.PatternLabel} · {entry.PatternSize}\nمختصات از بالا-چپ: X={x}, Y={y}", $"Color: {entry.ColorCode}\nColor pixels: {entry.PixelCount:N0} ({entry.PercentText})\nWeave: {entry.PatternLabel} · {entry.PatternSize}\nTop-left origin: X={x}, Y={y}");
        HoverCard.Visibility = Visibility.Visible;
    }
    private void PreviewImage_MouseLeave(object sender, MouseEventArgs e) => HoverCard.Visibility = Visibility.Collapsed;
    private async void RunAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.CanSave != true || SizeDirty || ColorsDirty || session.PatternPreview is not { } preview) return;
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => { var bytes = DesignAnalysis.ReadBinary(preview); var a = DesignAnalysis.Analyze(bytes, preview.PixelWidth, preview.PixelHeight); return (a, image: DesignAnalysis.Heatmap(a, preview.PixelHeight)); });
            warpAnalysis = result.a; heatmap = result.image;
            AnalysisResultText.Text = T($"میانگین نقاط سیاه هر تار: {result.a.Mean:0.##}\nقرمز: شاخص کشش · آبی: شاخص شل‌شدگی · خاکستری: برابر\nبرای اطلاعات هر تار، نشانگر را روی آن ببرید.", $"Mean black pixels per warp: {result.a.Mean:0.##}\nRed: above average · Blue: below · Gray: equal\nHover a warp for its values."); ShowStage();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException) { MessageBox.Show(this, ex.Message, Title); }
        finally { SetBusy(false); }
    }
    private void FloatParameters_Changed(object sender, TextChangedEventArgs e) => ClearFloatResults();
    private void FloatDirection_Changed(object sender, RoutedEventArgs e) => ClearFloatResults();
    private void ClearFloatResults()
    {
        if (!IsInitialized || busy) return;
        floatRuns = null; floatIndex = 0; FloatResultText.Text = T("پارامتر تغییر کرد؛ دوباره بررسی کنید.", "Parameters changed; scan again."); FloatCurrentText.Text = "";
        if (stage == Stage.Floats) ShowStage(); else RefreshControls();
    }
    private async void FloatScan_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.CanSave != true || SizeDirty || ColorsDirty) return;
        if (!int.TryParse(FloatMinimum.Text, out int minimum) || minimum < 1 || minimum > DesignProcessor.MaxPixels)
        { MessageBox.Show(this, T("طول باید یک عدد مثبت باشد.", "Length must be a positive integer.")); return; }
        await ScanFloatsAsync(minimum, FloatWarp.IsChecked == true ? FloatDirection.Warp : FloatDirection.Weft, 0, true);
    }
    private async Task ScanFloatsAsync(int minimum, FloatDirection direction, int desiredIndex, bool focus)
    {
        if (session?.PatternPreview is not { } preview) return; SetBusy(true);
        try
        {
            var found = await Task.Run(() => DesignAnalysis.FindFloats(DesignAnalysis.ReadBinary(preview), preview.PixelWidth, preview.PixelHeight, minimum, direction));
            floatRuns = found; floatIndex = found.Count == 0 ? 0 : Math.Min(desiredIndex, found.Count - 1); lastFloatMinimum = minimum; lastFloatDirection = direction;
            FloatResultText.Text = T($"تعداد دنباله‌های پیوسته: {found.Count:N0} (طول حداقل {minimum})", $"Continuous runs: {found.Count:N0} (minimum {minimum})");
            ShowFloat(focus);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException) { MessageBox.Show(this, ex.Message, Title); }
        finally { SetBusy(false); }
    }
    private void FloatPrevious_Click(object sender, RoutedEventArgs e) { if (!busy && floatRuns is { Count: > 0 } && floatIndex > 0) { floatIndex--; ShowFloat(true); } }
    private void FloatNext_Click(object sender, RoutedEventArgs e) { if (!busy && floatRuns is { Count: > 0 } && floatIndex < floatRuns.Count - 1) { floatIndex++; ShowFloat(true); } }
    private void ShowFloat(bool focus)
    {
        ShowStage();
        if (floatRuns is not { Count: > 0 } || session?.PatternPreview is not { } preview) { FloatCurrentText.Text = T("فلوتی با این طول پیدا نشد.", "No runs meet the threshold."); return; }
        var run = floatRuns[floatIndex];
        FloatCurrentText.Text = T($"مورد {floatIndex + 1:N0} از {floatRuns.Count:N0} · {(run.Black ? "سیاه" : "سفید")} · طول {run.Length:N0}\nشروع X={run.X}, Y={run.Y} · برای اصلاح روی نقطه کلیک کنید.", $"Run {floatIndex + 1:N0} / {floatRuns.Count:N0} · {(run.Black ? "black" : "white")} · length {run.Length:N0}\nStart X={run.X}, Y={run.Y} · click a pixel to toggle.");
        var highlighted = new WriteableBitmap(preview); byte[] mark = new byte[run.Length * 4];
        for (int i = 0; i < run.Length; i++) { mark[i * 4] = 20; mark[i * 4 + 1] = 115; mark[i * 4 + 2] = 255; mark[i * 4 + 3] = 255; }
        bool warp = run.Direction == FloatDirection.Warp;
        highlighted.WritePixels(new Int32Rect(run.X, run.Y, warp ? 1 : run.Length, warp ? run.Length : 1), mark, warp ? 4 : run.Length * 4, 0); highlighted.Freeze(); PreviewImage.Source = highlighted;
        if (focus)
        {
            SetZoom(8); UpdateLayout(); double x = run.X + (warp ? 0 : run.Length / 2d), y = run.Y + (warp ? run.Length / 2d : 0);
            ImageScroll.ScrollToHorizontalOffset(Math.Max(0, x * zoom - ImageScroll.ViewportWidth / 2)); ImageScroll.ScrollToVerticalOffset(Math.Max(0, y * zoom - ImageScroll.ViewportHeight / 2));
        }
        RefreshControls();
    }
    private async void PreviewImage_Click(object sender, MouseButtonEventArgs e)
    {
        if (busy || stage != Stage.Floats || session?.CanSave != true || SizeDirty || ColorsDirty || session.Palette is not { } palette) return;
        Point p = e.GetPosition(PreviewImage); var hit = PreviewHitTest.Find(palette, p.X, p.Y, PreviewImage.ActualWidth, PreviewImage.ActualHeight); if (hit is null) return;
        bool rescan = floatRuns is not null; int index = floatIndex, minimum = lastFloatMinimum; var direction = lastFloatDirection; var before = CaptureHistory(); SetBusy(true);
        try
        {
            await Task.Run(() => session.TogglePixel(hit.Value.X, hit.Value.Y)); RecordHistory(before); ShowStage();
            StatusText.Text = T("نقطه اصلاح شد؛ با ذخیره SAM و خروجی بافت یا ماشین حفظ می‌شود.", "Pixel edited; retained in SAM and final image or loom output.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { MessageBox.Show(this, ex.Message, Title); rescan = false; }
        finally { SetBusy(false); }
        if (rescan) await ScanFloatsAsync(minimum, direction, index, false);
        e.Handled = true;
    }
    private void Info_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session is null) return;
        var dialog = new DesignInfoWindow(metadata, english) { Owner = this };
        if (dialog.ShowDialog() == true) { metadata = dialog.Result; StatusText.Text = T("اطلاعات طرح ثبت شد؛ پروژه را ذخیره کنید.", "Design information updated; save the project."); }
    }
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2) { ToggleMaximize(); return; }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
    private void TitleBar_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this)));
    private void ToggleMaximize() { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void CloseWindow_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
    private void MaximizeWindow_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void MinimizeWindow_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private bool ConfirmDiscardDrawing()
    {
        if (!WeaveCanvas.IsDirty) return true;
        return MessageBox.Show(this, T("تغییرات بافت در بانک ذخیره نشده است. کنار گذاشته شود؟", "The weave drawing has unsaved changes. Discard them?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
    private async void Bank_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (bankOpen) { if (ConfirmDiscardDrawing()) { bankOpen = false; bankTargetIndex = null; WeaveCanvas.DiscardChanges(); ShowStage(); } return; }
        bankTargetIndex = null; bankOpen = true; ShowStage(); await ReloadBankAsync();
    }
    private async void PatternBank_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || (sender as Button)?.DataContext is not PaletteEntry entry) return;
        bankTargetIndex = entry.Index; bankOpen = true; ShowStage(); await ReloadBankAsync();
    }
    private void BankBack_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !ConfirmDiscardDrawing()) return;
        WeaveCanvas.DiscardChanges(); bankOpen = false; bankTargetIndex = null; ShowStage();
    }
    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    { if (bankOpen && !ConfirmDiscardDrawing()) e.Cancel = true; }
    private async Task ReloadBankAsync(string? selectedId = null)
    {
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => { var items = bank.Load(out int skipped); return (items, skipped); });
            BankList.ItemsSource = result.items;
            if (selectedId is not null) BankList.SelectedItem = result.items.FirstOrDefault(x => x.Id == selectedId);
            BankStatus.Text = T($"{result.items.Count} بافت در دسترس" + (result.skipped > 0 ? $" · {result.skipped} فایل نامعتبر نادیده گرفته شد" : ""),
                $"{result.items.Count} weaves available" + (result.skipped > 0 ? $" · {result.skipped} invalid items skipped" : ""));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { MessageBox.Show(this, T("خواندن بانک ممکن نشد: ", "Could not load weave bank: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private void UpdateBankButtons()
    {
        if (!IsInitialized) return;
        var item = BankList.SelectedItem as BankItem;
        BankUseButton.IsEnabled = !busy && item is not null && bankTargetIndex is int index && session?.Palette is not null && index >= 0 && index < entries.Count;
        BankEditButton.IsEnabled = !busy && item is not null;
        BankDeleteButton.IsEnabled = !busy && item is not null && !item.IsPreset;
    }
    private void BankSelection_Changed(object sender, SelectionChangedEventArgs e) { if (IsInitialized) UpdateBankButtons(); }
    private void EditorReset_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (!int.TryParse(WeaveWidth.Text, out int w) || !int.TryParse(WeaveHeight.Text, out int h) || w < 1 || h < 1 || w > WeaveEditor.MaxSide || h > WeaveEditor.MaxSide)
        { MessageBox.Show(this, T("عرض و ارتفاع باید بین ۱ تا ۶۴ باشند.", "Width and height must be between 1 and 64.")); return; }
        if (!ConfirmDiscardDrawing()) return;
        WeaveCanvas.Reset(w, h);
    }
    private void BankEdit_Click(object sender, RoutedEventArgs e)
    {
        if (busy || BankList.SelectedItem is not BankItem item || !ConfirmDiscardDrawing()) return;
        if (item.Pattern.Width > WeaveEditor.MaxSide || item.Pattern.Height > WeaveEditor.MaxSide)
        { MessageBox.Show(this, T("این بافت برای ویرایش شبکه‌ای بزرگ است؛ ویرایشگر تا ۶۴×۶۴ پیکسل را می‌پذیرد. بافت همچنان قابل استفاده است.", "This weave exceeds the 64×64 editor limit; it can still be applied.")); return; }
        WeaveCanvas.SetPattern(item.Pattern); WeaveWidth.Text = item.Pattern.Width.ToString(); WeaveHeight.Text = item.Pattern.Height.ToString(); WeaveName.Text = item.Name;
    }
    private async void BankSaveDrawing_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            var item = bank.SaveNew(WeaveName.Text, WeaveCanvas.Snapshot()); WeaveCanvas.MarkSaved(); await ReloadBankAsync(item.Id);
            StatusText.Text = T("بافت در بانک ذخیره شد.", "Weave saved to the bank.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { MessageBox.Show(this, T("ذخیره بافت ممکن نشد: ", "Could not save weave: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private async void BankImport_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "BMP and TIFF|*.bmp;*.tif;*.tiff", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true);
        try
        {
            var item = await Task.Run(() => { var input = ImageLoader.Load(dialog.FileName, 0); if (input.PageCount != 1) throw new InvalidDataException("Multi-page patterns are not supported."); return bank.SaveNew(Path.GetFileNameWithoutExtension(dialog.FileName), DesignProcessor.ReadPattern(input.Image)); });
            SetBusy(false); await ReloadBankAsync(item.Id); StatusText.Text = T("بافت به بانک افزوده شد.", "Weave added to bank.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(this, T("بافت باید مات، تک‌صفحه‌ای و سیاه‌وسفید خالص باشد: ", "Weave must be opaque, single-page and pure black/white: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private async void BankDelete_Click(object sender, RoutedEventArgs e)
    {
        if (busy || BankList.SelectedItem is not BankItem item || item.IsPreset) return;
        if (MessageBox.Show(this, T($"بافت «{item.Name}» از بانک حذف شود؟ پروژه‌های ذخیره‌شده مستقل می‌مانند.", $"Delete {item.Name} from the bank? Saved projects remain self-contained."), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try { bank.Delete(item); await ReloadBankAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { MessageBox.Show(this, T("حذف بافت ممکن نشد: ", "Could not delete weave: ") + ex.Message); }
    }
    private async void BankUse_Click(object sender, RoutedEventArgs e)
    {
        if (busy || BankList.SelectedItem is not BankItem item || bankTargetIndex is not int index || session?.Palette is null || index < 0 || index >= entries.Count || !ConfirmDiscardDrawing()) return;
        var entry = entries[index]; var before = CaptureHistory(); SetBusy(true);
        try
        {
            await Task.Run(() => session.AssignPattern(index, item.Pattern)); RecordHistory(before);
            entry.Restore(item.Name, item.Pattern); WeaveCanvas.DiscardChanges(); bankOpen = false; bankTargetIndex = null; ShowStage();
            StatusText.Text = T("بافت بانک به رنگ انتخاب‌شده اختصاص یافت.", "Library weave applied to the selected color.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(this, T("اعمال بافت ممکن نشد: ", "Could not apply weave: ") + ex.Message); }
        finally { SetBusy(false); }
    }
    private async void Pattern_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || (sender as Button)?.DataContext is not PaletteEntry entry) return;
        var dialog = new OpenFileDialog { Filter = "BMP and TIFF|*.bmp;*.tif;*.tiff", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        var before = CaptureHistory(); SetBusy(true); StatusText.Text = T("در حال اعمال پترن…", "Applying pattern…");
        try
        {
            var pattern = await Task.Run(() => { var input = ImageLoader.Load(dialog.FileName, 0); if (input.PageCount != 1) throw new InvalidDataException("Multi-page pattern files are not supported."); var p = DesignProcessor.ReadPattern(input.Image); session.AssignPattern(entry.Index, p); return p; });
            RecordHistory(before); entry.Assign(dialog.FileName, pattern); ShowStage(); StatusText.Text = T("پترن روی همه نقاط این رنگ اعمال شد.", "Pattern applied to every pixel of this color.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(this, T("پترن باید تک‌صفحه‌ای، مات و فقط سیاه‌وسفید خالص باشد.\n", "Pattern must be single-page, opaque and pure black and white.\n") + ex.Message); StatusText.Text = T("اعمال پترن ناموفق بود.", "Pattern could not be applied."); }
        finally { SetBusy(false); }
    }
    private async void PatternPreset_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || sender is not Button button || button.DataContext is not PaletteEntry entry) return;
        var menu = new ContextMenu { PlacementTarget = button };
        foreach (var preset in PatternLibrary.Presets)
        {
            var item = new MenuItem { Header = $"{preset.Name}  ·  {preset.Pattern.Width} × {preset.Pattern.Height}" };
            item.Click += async (_, _) =>
            {
                if (busy || session?.Palette is null) return;
                var before = CaptureHistory(); SetBusy(true);
                try
                {
                    await Task.Run(() => session.AssignPattern(entry.Index, preset.Pattern)); RecordHistory(before);
                    entry.Restore(preset.Name, preset.Pattern); ShowStage();
                    StatusText.Text = T("الگو روی همه نقاط این رنگ اعمال شد.", "Pattern applied to every pixel of this color.");
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
                { MessageBox.Show(this, T("اعمال الگو ممکن نشد: ", "Could not apply pattern: ") + ex.Message); }
                finally { SetBusy(false); }
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
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
        if (busy || machineFormatIndex is not (1 or 4) || session is null || path is null) return;
        BitmapSource? frame = MachineUseSource.IsChecked == true ? session.Source :
            session.CanSave && !SizeDirty && !ColorsDirty ? session.PatternPreview : null;
        if (frame is null) return;
        string format = machineFormatIndex == 4 ? "JC6" : "JC5";
        bool redAsBlack = MachineRedAsBlack.IsChecked == true;
        string warning = T($"خروجی {format} آزمایشی و تک‌ناحیه‌ای است: {frame.PixelWidth} قلاب و {frame.PixelHeight} رج، سیاه{(redAsBlack ? " و قرمز" : "")} = بیت ۱. نگاشت یا لبه دستگاه به آن اضافه نمی‌شود. ابتدا با DesignScope و بدون تولید بررسی کنید. ادامه می‌دهید؟",
            $"Experimental single-area {format}: {frame.PixelWidth} hooks and {frame.PixelHeight} picks, black{(redAsBlack ? " and red" : "")} = bit 1. No machine mapping or selvedges are added. Verify in DesignScope before production. Continue?");
        if (MessageBox.Show(this, warning, Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var dialog = new SaveFileDialog { Filter = $"{format} (*.jc5)|*.jc5", DefaultExt = ".jc5", AddExtension = true, OverwritePrompt = true, FileName = MachineFileNaming.SuggestedName(machineFormatIndex) };
        if (dialog.ShowDialog(this) != true) return;
        if (!MachineFileNaming.ValidDestination(dialog.FileName, machineFormatIndex))
        {
            MessageBox.Show(this, T("برای JC6 نام فایل باید شامل عدد ۶ باشد و پسوند آن jc5 بماند؛ مانند design6.jc5.", "For JC6, the name must contain 6 and the extension must be .jc5, e.g. design6.jc5."), Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var protectedPaths = new[] { path }.Concat(projectPath is null ? Array.Empty<string>() : new[] { projectPath }).Concat(entries.Select(x => x.PatternPath).OfType<string>()).ToArray();
        SetBusy(true);
        try
        {
            await Task.Run(() => Jc5Codec.Save(frame, dialog.FileName, protectedPaths, redAsBlack));
            StatusText.Text = T($"{format} آزمایشی ذخیره شد؛ پیش از تولید آن را در DesignScope بررسی کنید.", $"Experimental {format} saved; verify it in DesignScope before production.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        { MessageBox.Show(this, T("ذخیره JC5 ناموفق بود: ", "JC5 save failed: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private async void MachineImport_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "JC5 / JC6 (*.jc5;*.jc6)|*.jc5;*.jc6", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || !ConfirmDiscard()) return;
        SetBusy(true);
        try
        {
            bool legacy = MachineLegacyImport.IsChecked == true;
            var image = await Task.Run(() => Jc5Codec.Load(dialog.FileName, legacy));
            var next = new WorkflowSession(image); next.ApplySize(image.PixelWidth, image.PixelHeight);
            session = next; path = dialog.FileName; projectPath = null; projectCreated = DateTimeOffset.Now; metadata = new(); ClearHistory(); sourceName = Path.GetFileName(path);
            current = new ImageResult(image, 1, 0, "JC5 (experimental)"); RebuildPaletteEntries();
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
    private void Report_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || SizeDirty || ColorsDirty) return;
        try
        {
            string name = projectPath is null ? T("پروژه جدید", "New project") : Path.GetFileNameWithoutExtension(projectPath);
            var page = DesignReport.CreatePage(session, entries, name, projectCreated, english, metadata); var bitmap = DesignReport.Render(page);
            var protectedPaths = new[] { path, projectPath }.OfType<string>().Concat(entries.Select(x => x.PatternPath).OfType<string>());
            new ReportWindow(bitmap, name, protectedPaths, english) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(this, T("ساخت گزارش ممکن نشد: ", "Could not prepare report: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow(english) { Owner = this }.ShowDialog();
    private void Language_Click(object sender, RoutedEventArgs e) { english = !english; UpdateLabels(); ShowStage(); }
    private void Theme_Click(object sender, RoutedEventArgs e) { lightTheme = !lightTheme; ThemeManager.Apply(lightTheme, true); RefreshControls(); }
    private void UpdateLabels()
    {
        FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        SettingsBorder.FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        LanguageButton.Content = english ? "FA" : "EN"; LanguageButton.ToolTip = T("تغییر زبان", "Change language"); AboutButton.ToolTip = T("درباره", "About"); OpenProjectButton.ToolTip = T("باز کردن پروژه", "Open project"); SaveProjectButton.ToolTip = T("ذخیره پروژه", "Save project"); SaveProjectAsButton.ToolTip = T("ذخیره با نام دیگر", "Save project as"); ThemeButton.ToolTip = T("تغییر تم", "Change theme"); BankButton.ToolTip = T("بانک و طراحی بافت", "Weave bank and editor"); UndoButton.ToolTip = T("بازگشت (Ctrl+Z)", "Undo (Ctrl+Z)"); RedoButton.ToolTip = T("بازانجام (Ctrl+Y)", "Redo (Ctrl+Y)"); ReportButton.ToolTip = T("گزارش PDF و چاپ", "PDF report and print"); InfoButton.Content = T("اطلاعات طرح", "Design info"); InfoButton.ToolTip = T("طراح، کاربرد و توضیحات", "Designer, use and description");
        AnalysisStepButton.Content = T("۵  تحلیل تار", "5  Warp analysis"); FloatStepButton.Content = T("۶  بررسی فلوت", "6  Float check");
        AnalysisTitle.Text = T("تحلیل تارها", "Warp analysis"); AnalysisHint.Text = T("تعداد نقاط سیاه هر ستون با میانگین همه ستون‌ها مقایسه می‌شود. قرمز: شاخص کشش بالاتر؛ آبی: شاخص شل‌شدگی. این شاخص اندازه‌گیری فیزیکی کشش نیست.", "Black pixels per column compared with the overall mean. Red: higher tension indicator; blue: looseness indicator. This is not a physical tension measurement."); RunAnalysisButton.Content = T("محاسبه نقشه حرارتی", "Calculate heatmap");
        FloatTitle.Text = T("بررسی فلوت", "Float inspection"); FloatHint.Text = T("طول حداقل و جهت را انتخاب کنید؛ دنباله‌های سیاه یا سفید پیوسته شمرده می‌شوند. روی تصویر کلیک کنید تا نقطه عوض شود.", "Choose a minimum length and direction. Contiguous black and white runs are counted. Click the image to toggle a pixel."); FloatLengthLabel.Text = T("حداقل طول فلوت", "Minimum run length"); FloatWarp.Content = T("تار (ستون)", "Warp (column)"); FloatWeft.Content = T("پود (ردیف)", "Weft (row)"); FloatScanButton.Content = T("بررسی فلوت‌ها", "Scan floats"); FloatPreviousButton.Content = T("قبلی", "Previous"); FloatNextButton.Content = T("بعدی", "Next"); ColorSwatchColumn.Header = T("رنگ", "Color"); ColorCodeColumn.Header = T("کد رنگ", "Color code"); ColorCountColumn.Header = T("پیکسل", "Pixels"); ColorPercentColumn.Header = T("درصد", "Percent");
        OpenButton.Content = T("+  باز کردن تصویر", "+  Open image"); SizeStepButton.Content = T("۱  تغییر اندازه", "1  Resize"); ColorStepButton.Content = T("۲  کاهش رنگ", "2  Colors"); PatternStepButton.Content = T("۳  اختصاص بافت", "3  Patterns"); MachineStepButton.Content = T("۴  فایل ماشین", "4  Loom file");
        FileCaption.Text = T("فایل ورودی", "INPUT FILE"); NoFileTitle.Text = T("شروع کار", "Get started"); NoFileHint.Text = T("ابتدا یک تصویر BMP یا TIFF باز کنید یا در پنجره رها کنید.", "Open or drop a BMP or TIFF image to start.");
        SizeTitle.Text = T("مرحله ۱ · تغییر اندازه", "Stage 1 · Resize"); SizeHint.Text = T("ابعاد خروجی را وارد و اعمال کنید. تصویر سمت چپ نتیجه همین مرحله است.", "Set and apply the output dimensions; the preview shows the result.");
        WidthLabel.Text = T("عرض (پیکسل)", "Width (px)"); HeightLabel.Text = T("ارتفاع (پیکسل)", "Height (px)"); ApplySizeButton.Content = T("اعمال اندازه", "Apply size"); SaveSizeButton.Content = T("ذخیره خروجی اندازه", "Save resized image"); ToColorButton.Content = T("مرحله بعد: رنگ ←", "Next: colors →");
        SizeNotice.Text = T("روش تغییر اندازه نزدیک‌ترین همسایه است؛ نسبت تصویر خودکار حفظ نمی‌شود.", "Resize uses nearest-neighbor; aspect ratio is not locked.");
        ColorTitle.Text = T("مرحله ۲ · کاهش رنگ", "Stage 2 · Colors"); ColorHint.Text = T("ورودی این مرحله تصویر تغییراندازه‌یافته است؛ بیش از ۵۰ رنگ به‌طور خودکار کاهش می‌یابد.", "Input is the resized image; over 50 colors are reduced automatically.");
        ColorLimitLabel.Text = T("حداکثر تعداد رنگ (۱ تا ۵۰)", "Maximum colors (1–50)"); ApplyColorButton.Content = T("اعمال تعداد رنگ", "Apply color limit"); SaveColorButton.Content = T("ذخیره خروجی رنگ", "Save color image"); ToPatternButton.Content = T("مرحله بعد: بافت ←", "Next: patterns →");
        SaveButton.Content = T("ذخیره تصویر", "Save image");
        ColorSelectionHint.Text = T("برای ادغام، دو رنگ را انتخاب کنید.", "Select two colors to merge."); DeleteColorButton.Content = T("حذف رنگ انتخابی", "Remove selected"); MergeColorButton.Content = T("ادغام دو رنگ", "Merge two colors");
        BankTitle.Text = T("بانک و طراحی بافت", "Weave bank and editor"); BankHint.Text = T("یک بافت انتخاب کنید یا روی شبکه ترسیم کنید.", "Select a weave or draw on the grid."); BankBackButton.Content = T("بازگشت", "Back");
        EditorTitle.Text = T("طراحی بافت", "Weave editor"); EditorHint.Text = T("روی خانه‌ها کلیک کنید یا بکشید؛ سیاه و سفید تغییر می‌کنند.", "Click or drag cells to paint black and white.");
        WeaveWidthLabel.Text = T("عرض", "Width"); WeaveHeightLabel.Text = T("ارتفاع", "Height"); EditorResetButton.Content = T("ساخت شبکه", "Create grid"); BankSaveDrawingButton.Content = T("ذخیره در بانک", "Save to bank");
        BankListTitle.Text = T("بافت‌های بانک", "Weave library"); BankUseButton.Content = T("اختصاص به رنگ", "Apply to color"); BankEditButton.Content = T("ویرایش انتخاب‌شده", "Edit selection"); BankImportButton.Content = T("افزودن از فایل BMP/TIFF", "Import BMP/TIFF"); BankDeleteButton.Content = T("حذف از بانک", "Delete from bank");
        UpdateWindowTitle();
        DeveloperText.Text = T("برنامه‌نویس: میرخلیلی", "Developer: Mirkhalili");
        MachineTitle.Text = T("مرحله ۴ · فایل ماشین", "Stage 4 · Loom file"); MachineHint.Text = T("JC5 و JC6 تک‌ناحیه‌ای بر اساس نمونه DesignScope به‌صورت آزمایشی فعال هستند؛ سایر قالب‌ها پشتیبانی نمی‌شوند.", "Experimental single-area JC5 and JC6 based on DesignScope samples; other formats are not supported.");
        MachineFormatLabel.Text = T("قالب هدف", "Target format"); MachineRedAsBlack.Content = T("نقشه قرمز و سفید: قرمز = بافت", "Red/white hook map: red = black"); MachineLegacyImport.Content = T("خواندن فایل خروجی نسخه قدیمی ۰۰۶", "Import a file created by old version 006"); MachineUseSource.Content = T("استفاده از نقشه اصلی به جای نتیجه بافت", "Use original hook map instead of pattern result"); MachineExportButton.Content = T("تبدیل و ذخیره فایل ماشین", "Convert and save loom file"); MachineImportButton.Content = T("انتخاب فایل ماشین", "Select loom file");
        MachineNotice.Text = T("عرض تصویر = تعداد قلاب و ارتفاع = تعداد رج. رنگ سیاه بیت ۱ است. لبه، ناحیه اضافی و تنظیمات دستگاه اضافه نمی‌شوند. قبل از تولید در DesignScope و روی دستگاه آزمایش کنید.", "Image width = hooks and height = picks; black is bit 1. No selvedge, extra areas or machine settings are added. Verify in DesignScope and on the controller before production.");
        SafetyText.Text = T("خروجی تصویر است، نه فایل دستگاه بافندگی.", "Output is an image, not a loom file."); CanvasSubtitle.Text = T("خروجی هر مرحله در این قسمت نمایش داده می‌شود", "The result of each stage appears here");
        EmptyTitle.Text = T("طرح خود را وارد کنید", "Bring in your design"); EmptyHint.Text = T("یک تصویر BMP یا TIFF انتخاب کنید.", "Choose a BMP or TIFF image."); EmptyButton.Content = T("انتخاب تصویر", "Choose image");
        if (session is null) StatusText.Text = T("آماده برای باز کردن تصویر", "Ready to open an image");
    }
}
