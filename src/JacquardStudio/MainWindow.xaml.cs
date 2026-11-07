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
    private enum Stage { Size, Colors, Patterns, Machine, Analysis, Floats, Selvedge, Weft }
    private Stage stage = Stage.Size;
    private bool english, busy, bankOpen, lightTheme, projectDirty, allowClose, closePromptActive;
    private int? bankTargetIndex;
    private string? bankSelvedgeSide;
    private readonly WeaveBank bank = new();
    private string? path;
    private string? projectPath;
    private string sourceName = "";
    private int machineFormatIndex = 1;
    private ImageResult? current;
    private WorkflowSession? session;
    private List<PaletteEntry> entries = new();
    private double zoom = 1;
    private BitmapSource? machinePreview, redPreview;
    private MachineComposition? composedMap;
    private bool loadingMachineSettings, absoluteLayout;
    private System.Windows.Threading.DispatcherTimer? selvedgeTimer;
    private DateTimeOffset? projectCreated;
    private DesignMetadata metadata = new();
    private WarpAnalysis? warpAnalysis;
    private List<FloatRun>? floatRuns;
    private int floatIndex, lastFloatMinimum;
    private FloatDirection lastFloatDirection;
    private sealed record HistoryState(WorkflowSession Session, Stage Stage, string?[] Names, string?[] Paths);
    private readonly List<HistoryState> undoHistory = new(), redoHistory = new();

    public MainWindow() { InitializeComponent(); selvedgeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) }; selvedgeTimer.Tick += async (_, _) => { selvedgeTimer.Stop(); if (!busy && !bankOpen) { if (stage == Stage.Selvedge) await PreviewSelvedgesAsync(false); else if (stage is Stage.Weft or Stage.Machine) await ComposeMachineAsync(false); } }; WeaveCanvas.Changed += (_, _) => RefreshControls(); lightTheme = ThemeManager.ReadLight(); ThemeManager.Apply(lightTheme, false); VersionText.Text = AppVersion.Display; LoadMachineSettings(new MachineSetup(AbsoluteLayout: true)); UpdateLabels(); RefreshControls(); UpdateWindowTitle(); }
    private void UpdateWindowTitle() => Title = $"Jacquard Studio ({(projectPath is null ? T("پروژه جدید", "New project") : Path.GetFileNameWithoutExtension(projectPath))}{(projectDirty && session is not null ? " *" : "")})";
    private void MarkProjectDirty() { if (session is not null) { projectDirty = true; UpdateWindowTitle(); } }
    private string T(string fa, string en) => english ? en : fa;
    private bool SizeDirty => session?.Resized is null || !int.TryParse(WidthInput.Text, out int w) || !int.TryParse(HeightInput.Text, out int h) || w != session.Resized.PixelWidth || h != session.Resized.PixelHeight;
    private bool ColorsDirty => session?.Palette is null || !int.TryParse(ColorInput.Text, out int colors) || colors != session.AppliedColorLimit;

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (bankOpen && !ConfirmDiscardDrawing()) return false;
        if (session is null || !projectDirty) return true;
        var answer = MessageBox.Show(this, T("تغییرات پروژه ذخیره نشده است. پیش از ادامه ذخیره شود؟", "The project has unsaved changes. Save before continuing?"), Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return answer == MessageBoxResult.No || (answer == MessageBoxResult.Yes && await SaveProjectAsync(false));
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "BMP and TIFF|*.bmp;*.tif;*.tiff|All files|*.*", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true && await ConfirmDiscardAsync()) await LoadAsync(dialog.FileName, 0);
    }
    private async Task LoadAsync(string filename, int page)
    {
        SetBusy(true); StatusText.Text = T("در حال باز کردن تصویر…", "Loading image…");
        try
        {
            var result = await Task.Run(() => ImageLoader.Load(filename, page));
            path = filename; projectPath = null; projectCreated = DateTimeOffset.Now; metadata = new(); LoadMachineSettings(new MachineSetup(AbsoluteLayout: true)); ClearHistory(); sourceName = Path.GetFileName(filename); current = result; session = new WorkflowSession(result.Image); RebuildPaletteEntries();
            WidthInput.Text = result.Image.PixelWidth.ToString(); HeightInput.Text = result.Image.PixelHeight.ToString(); ColorInput.Text = "50";
            FileValue.Text = Path.GetFileName(filename); FormatValue.Text = $"{result.Format} · {result.Image.Format.BitsPerPixel} bpp";
            zoom = 1; SetZoom(1); stage = Stage.Size; projectDirty = true; ShowStage();
            StatusText.Text = T("اندازه را تعیین و اعمال کنید.", "Set and apply the image size.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { MessageBox.Show(this, T("باز کردن تصویر ممکن نشد.\n", "Could not open the image.\n") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); StatusText.Text = T("باز کردن تصویر ناموفق بود.", "Could not open image."); }
        finally { SetBusy(false); }
    }
    private async void PreviousPage_Click(object sender, RoutedEventArgs e) { if (!busy && path is not null && current is not null && current.PageIndex > 0 && await ConfirmDiscardAsync()) await LoadAsync(path, current.PageIndex - 1); }
    private async void NextPage_Click(object sender, RoutedEventArgs e) { if (!busy && path is not null && current is not null && current.PageIndex + 1 < current.PageCount && await ConfirmDiscardAsync()) await LoadAsync(path, current.PageIndex + 1); }
    private void Window_DragOver(object sender, DragEventArgs e) { e.Effects = !busy && !bankOpen && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (busy || bankOpen || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files || !await ConfirmDiscardAsync()) return;
        if (Path.GetExtension(files[0]).Equals(".sam", StringComparison.OrdinalIgnoreCase)) await LoadProjectAsync(files[0]);
        else await LoadAsync(files[0], 0);
    }
    private void Inputs_Changed(object sender, TextChangedEventArgs e) { if (IsInitialized) { MarkProjectDirty(); RefreshControls(); } }
    private void SetBusy(bool value) { busy = value; RefreshControls(); }
    private void RefreshControls()
    {
        if (!IsInitialized) return;
        OpenButton.IsEnabled = !busy; OpenProjectButton.IsEnabled = !busy && !bankOpen; SaveProjectButton.IsEnabled = !busy && session is not null; SaveProjectAsButton.IsEnabled = !busy && session is not null; BankButton.IsEnabled = !busy; EmptyButton.IsEnabled = !busy; SettingsBorder.IsEnabled = !busy; BankWorkspace.IsEnabled = !busy;
        SizeStepButton.IsEnabled = !busy && session is not null;
        ColorStepButton.IsEnabled = !busy && session?.Resized is not null && !SizeDirty;
        PatternStepButton.IsEnabled = !busy && session?.Palette is not null && !SizeDirty && !ColorsDirty;
        MachineStepButton.IsEnabled = !busy;
        AnalysisStepButton.IsEnabled = FloatStepButton.IsEnabled = SelvedgeStepButton.IsEnabled = WeftStepButton.IsEnabled = !busy && session?.CanSave == true && !SizeDirty && !ColorsDirty;
        InfoButton.IsEnabled = !busy && !bankOpen && session is not null;
        FloatPreviousButton.IsEnabled = !busy && floatRuns is { Count: > 0 } && floatIndex > 0;
        FloatNextButton.IsEnabled = !busy && floatRuns is { Count: > 0 } && floatIndex < floatRuns.Count - 1;
        MachinePreviewButton.IsEnabled = !busy && stage == Stage.Weft && MachineInput() is not null;
        SelvedgePreviewButton.IsEnabled = !busy && stage == Stage.Selvedge && MachineInput() is not null;
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
        foreach (var pair in new[] { (SizeStepButton, Stage.Size), (ColorStepButton, Stage.Colors), (PatternStepButton, Stage.Patterns), (SelvedgeStepButton, Stage.Selvedge), (WeftStepButton, Stage.Weft), (FloatStepButton, Stage.Floats), (AnalysisStepButton, Stage.Analysis), (MachineStepButton, Stage.Machine) })
        {
            bool active = (session is not null || stage == Stage.Machine) && pair.Item2 == stage;
            pair.Item1.Background = active ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(lightTheme ? "#D2EFE7" : "#31574F")) : (Brush)FindResource("ButtonBackground");
            pair.Item1.BorderBrush = active ? (Brush)FindResource("Accent") : (Brush)FindResource("ButtonBorder");
        }
    }
    private async void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "SAM project (*.sam)|*.sam", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true && await ConfirmDiscardAsync()) await LoadProjectAsync(dialog.FileName);
    }
    private async Task LoadProjectAsync(string filename)
    {
        SetBusy(true); StatusText.Text = T("در حال باز کردن پروژه…", "Opening project…");
        try
        {
            var state = await Task.Run(() => ProjectFile.Load(filename));
            // Only replace current work after the complete archive has been checked.
            session = state.Session; path = filename; projectPath = filename; projectCreated = state.CreatedAt; metadata = state.Metadata ?? new(); LoadMachineSettings(state.Machine ?? new MachineSetup(AbsoluteLayout: true)); ClearHistory(); sourceName = state.SourceName;
            current = new ImageResult(session.Source, 1, 0, "SAM");
            WidthInput.Text = state.WidthInput; HeightInput.Text = state.HeightInput; ColorInput.Text = state.ColorInput;
            english = state.English; stage = Enum.Parse<Stage>(state.Stage); machineFormatIndex = state.MachineFormatIndex;
            switch (machineFormatIndex) { case 0: FormatJC4.IsChecked = true; break; case 1: FormatJC5.IsChecked = true; break; case 2: FormatZC5.IsChecked = true; break; case 3: FormatEP.IsChecked = true; break; case 4: FormatJC6.IsChecked = true; break; }
            RebuildPaletteEntries(state.PatternNames);
            FileValue.Text = Path.GetFileName(filename); FormatValue.Text = "SAM"; SetZoom(1); projectDirty = false; UpdateLabels(); ShowStage();
            StatusText.Text = T("پروژه باز شد؛ تصویر و پترن‌ها در پروژه قرار دارند.", "Project opened; the image and patterns are embedded.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or OverflowException or FormatException)
        { MessageBox.Show(this, T("باز کردن پروژه ممکن نشد: ", "Could not open project: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); StatusText.Text = T("باز کردن پروژه ناموفق بود.", "Could not open project."); }
        finally { SetBusy(false); }
    }
    private async void SaveProject_Click(object sender, RoutedEventArgs e) => await SaveProjectAsync(false);
    private async void SaveProjectAs_Click(object sender, RoutedEventArgs e) => await SaveProjectAsync(true);
    private async Task<bool> SaveProjectAsync(bool saveAs)
    {
        if (busy || session is null || path is null) return false;
        string? destination = projectPath;
        if (ProjectSavePolicy.RequiresDialog(destination, saveAs))
        {
            var dialog = new SaveFileDialog { Filter = "SAM project (*.sam)|*.sam", AddExtension = true, DefaultExt = ".sam", OverwritePrompt = true, FileName = projectPath is null ? Path.GetFileNameWithoutExtension(sourceName) + ".sam" : Path.GetFileName(projectPath) };
            if (dialog.ShowDialog(this) != true) return false;
            destination = dialog.FileName;
        }
        if (string.IsNullOrEmpty(destination)) return false;
        var names = session.Patterns.Select((p, i) => p is null ? null : entries[i].PatternLabel).ToArray();
        var state = new ProjectState(session, sourceName, WidthInput.Text, HeightInput.Text, ColorInput.Text, stage.ToString(), english, machineFormatIndex, names, projectCreated, metadata, ReadMachineSettings());
        var protectedPaths = entries.Select(x => x.PatternPath).OfType<string>().ToList();
        if (!string.Equals(path, projectPath, StringComparison.OrdinalIgnoreCase)) protectedPaths.Add(path);
        SetBusy(true); StatusText.Text = T("در حال ذخیره پروژه…", "Saving project…");
        try
        {
            await Task.Run(() => ProjectFile.Save(destination, state, protectedPaths));
            projectPath = destination; projectDirty = false; UpdateWindowTitle(); StatusText.Text = T("پروژه ذخیره شد.", "Project saved."); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        { MessageBox.Show(this, T("ذخیره پروژه ممکن نشد: ", "Could not save project: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); StatusText.Text = T("ذخیره پروژه ناموفق بود.", "Project save failed."); return false; }
        finally { SetBusy(false); }
    }
    private void MachineFormat_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as RadioButton)?.Tag is string tag && int.TryParse(tag, out int selected)) machineFormatIndex = selected;
        if (IsInitialized) { MarkProjectDirty(); RefreshControls(); }
    }
    private void MachineSource_Changed(object sender, RoutedEventArgs e) { if (IsInitialized && !loadingMachineSettings) MachineSettingsChanged(); }
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
    private async void MachineStep_Click(object sender, RoutedEventArgs e) { if (!busy) { stage = Stage.Machine; ShowStage(); await RefreshMachineBankAsync(); if (!busy && MachineInput() is not null) await ComposeMachineAsync(false); } }
    private void OpenNavigation_Click(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button button) return;
        var menu = new ContextMenu { PlacementTarget = button };
        var image = new MenuItem { Header = T("باز کردن تصویر", "Open image") }; image.Click += Open_Click;
        var project = new MenuItem { Header = T("باز کردن پروژه", "Open project") }; project.Click += OpenProject_Click;
        menu.Items.Add(image); menu.Items.Add(project); menu.IsOpen = true;
    }
    private async void SelvedgeStep_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.CanSave != true || SizeDirty || ColorsDirty) return;
        stage = Stage.Selvedge; machinePreview = null; ShowStage(); await RefreshMachineBankAsync();
        if (!busy) await PreviewSelvedgesAsync(false);
    }
    private async void WeftStep_Click(object sender, RoutedEventArgs e)
    { if (!busy && session?.CanSave == true && !SizeDirty && !ColorsDirty) { stage = Stage.Weft; ShowStage(); await ComposeMachineAsync(false); } }
    private async void AnalysisStep_Click(object sender, RoutedEventArgs e)
    { if (!busy && session?.CanSave == true && !SizeDirty && !ColorsDirty) { stage = Stage.Analysis; ShowStage(); await ComposeMachineAsync(true); } }
    private async void FloatStep_Click(object sender, RoutedEventArgs e)
    { if (!busy && session?.CanSave == true && !SizeDirty && !ColorsDirty) { stage = Stage.Floats; ShowStage(); await ComposeMachineAsync(true); } }
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
        SelvedgePanel.Visibility = stage == Stage.Selvedge ? Visibility.Visible : Visibility.Collapsed;
        WeftPanel.Visibility = stage == Stage.Weft ? Visibility.Visible : Visibility.Collapsed;
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
                Stage.Selvedge or Stage.Weft => machinePreview ?? session.PatternPreview ?? session.Source,
                Stage.Analysis or Stage.Floats => machinePreview ?? session.PatternPreview ?? session.Source,
                Stage.Patterns => session.PatternPreview ?? session.ColorPreview ?? session.Source,
                Stage.Machine => (MachineRedAsBlack.IsChecked == true ? redPreview : machinePreview) ?? (MachineUseSource.IsChecked == true ? session.Source : session.PatternPreview ?? session.ColorPreview ?? session.Resized ?? session.Source),
                _ => session.Source
            };
            PreviewImage.Source = view; ImageScroll.Visibility = Visibility.Visible; EmptyState.Visibility = Visibility.Collapsed;
            PreviewDetails.Text = $"{view.PixelWidth:N0} × {view.PixelHeight:N0} px";
        }
        CanvasTitle.Text = stage == Stage.Machine ? (machinePreview is null ? T("پیش‌نمایش طرح فعلی", "Current design preview") : T("نقشه نهایی ماشین", "Final machine map")) : !hasFile ? T("پیش‌نمایش طرح", "Design preview") : stage switch
        {
            Stage.Size => T("پیش‌نمایش اندازه", "Size preview"),
            Stage.Colors => T("پیش‌نمایش رنگ‌ها", "Color preview"),
            Stage.Selvedge => T("پیش‌نمایش کناره‌ها", "Selvedge preview"),
            Stage.Weft => T("پیش‌نمایش پودگذاری", "Weft layout preview"),
            Stage.Analysis => composedMap is null ? T("نقشه ترکیبی آماده نیست", "Composite map unavailable") : T("تحلیل درصد تارها", "Warp percentage analysis"),
            Stage.Floats => composedMap is null ? T("نقشه ترکیبی آماده نیست", "Composite map unavailable") : T("پیش‌نمایش و اصلاح فلوت", "Float inspection and editing"),
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
    private void InvalidateAnalysis() { machinePreview = null; redPreview = null; composedMap = null; warpAnalysis = null; floatRuns = null; floatIndex = 0; if (IsInitialized) { AnalysisResultText.Text = T("برای محاسبه، دکمه بالا را بزنید.", "Run analysis to calculate."); FloatResultText.Text = T("هنوز بررسی نشده است.", "Not scanned yet."); FloatCurrentText.Text = ""; } }
    private void RecordHistory(HistoryState state) { MarkProjectDirty(); InvalidateAnalysis(); undoHistory.Add(state); if (undoHistory.Count > 3) undoHistory.RemoveAt(0); redoHistory.Clear(); }
    private void RestoreHistory(HistoryState state)
    {
        session = state.Session.Snapshot(); stage = state.Stage; MarkProjectDirty(); InvalidateAnalysis();
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
        if (busy) return;
        if (e.Key == Key.F1) { Help_Click(sender, e); e.Handled = true; return; }
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.Alt && (int)e.Key >= (int)Key.D1 && (int)e.Key <= (int)Key.D9 && !bankOpen)
        {
            Button[] buttons = { OpenButton, SizeStepButton, ColorStepButton, PatternStepButton, SelvedgeStepButton, FloatStepButton, AnalysisStepButton, WeftStepButton, MachineStepButton };
            var button = buttons[(int)e.Key - (int)Key.D1]; if (button.IsEnabled) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; return;
        }
        if ((mods & ModifierKeys.Control) == 0 || (mods & ModifierKeys.Alt) != 0) return;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        if (e.Key == Key.O && !bankOpen) { if (shift) OpenProject_Click(sender, e); else Open_Click(sender, e); }
        else if (e.Key == Key.S && !bankOpen) { if (shift) SaveProjectAs_Click(sender, e); else SaveProject_Click(sender, e); }
        else if (e.Key == Key.P && ReportButton.IsEnabled) Report_Click(sender, e);
        else if (e.Key == Key.B) Bank_Click(sender, e);
        else if (e.Key == Key.Z && Keyboard.FocusedElement is not TextBox) { if (shift) Redo_Click(sender, e); else Undo_Click(sender, e); }
        else if (e.Key == Key.Y && Keyboard.FocusedElement is not TextBox) Redo_Click(sender, e);
        else if (e.Key is Key.Add or Key.OemPlus) ZoomIn_Click(sender, e);
        else if (e.Key is Key.Subtract or Key.OemMinus) ZoomOut_Click(sender, e);
        else if (e.Key is Key.D0 or Key.NumPad0) ZoomReset_Click(sender, e);
        else return;
        e.Handled = true;
    }
    private void Help_Click(object sender, RoutedEventArgs e)
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/JacquardStudio;component/Help.fa.txt", UriKind.Absolute));
        if (resource is null) { MessageBox.Show(this, T("راهنما یافت نشد.", "Help file not found.")); return; }
        using var reader = new StreamReader(resource.Stream); string text = reader.ReadToEnd();
        var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 26, Margin = new Thickness(24), Foreground = (Brush)FindResource("PrimaryText") };
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        new Window { Title = T("راهنمای برنامه", "User guide (Persian)"), Owner = this, Width = 820, Height = 720, MinWidth = 440, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner, FlowDirection = FlowDirection.RightToLeft, Background = (Brush)FindResource("AppBackground"), Content = scroll }.ShowDialog();
    }
    private void EditorHelper_Click(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button { Tag: string action }) return;
        if (!int.TryParse(StepXInput.Text, out int dx) || !int.TryParse(StepYInput.Text, out int dy) || dx < 1 || dy < 1 || dx > 64 || dy > 64)
        { MessageBox.Show(this, T("گام‌های X و Y باید بین ۱ تا ۶۴ باشند.", "X and Y steps must be between 1 and 64.")); return; }
        int repeat = 5, step = 2;
        if (action == "satin" && (!int.TryParse(SatinRepeat.Text, out repeat) || !int.TryParse(SatinStep.Text, out step))) { MessageBox.Show(this, T("تکرار و گام ساتین باید عدد صحیح باشند.", "Satin repeat and step must be integers.")); return; }
        try { WeaveCanvas.ApplyHelper(action, dx, dy, repeat, step); }
        catch (ArgumentException ex)
        {
            string message = action == "panama" ? T("عرض و ارتفاع پاناما باید زوج باشند.", "Panama width and height must be even.") : action == "satin" ? T("ساتین: تکرار حداقل ۵، گام بین ۲ و تکرار منهای ۲ و نسبت به تکرار متباین باشد؛ ابعاد نیز مضرب تکرار باشند.", "Satin: repeat ≥5, step 2 to repeat-2 coprime with repeat; both dimensions must be multiples of repeat.") : ex.Message;
            MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private void EditorGrid_Click(object sender, RoutedEventArgs e) => WeaveCanvas.GridVisible = !WeaveCanvas.GridVisible;
    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if ((stage is Stage.Machine or Stage.Selvedge or Stage.Weft or Stage.Floats or Stage.Analysis) && machinePreview is { } map)
        {
            var local = e.GetPosition(PreviewImage);
            if (local.X < 0 || local.Y < 0 || local.X >= PreviewImage.ActualWidth || local.Y >= PreviewImage.ActualHeight) { HoverCard.Visibility = Visibility.Collapsed; return; }
            int hx = (int)(local.X * map.PixelWidth / PreviewImage.ActualWidth), hy = (int)(local.Y * map.PixelHeight / PreviewImage.ActualHeight);
            var pixel = new byte[1]; map.CopyPixels(new Int32Rect(hx, hy, 1, 1), pixel, 1, 0);
            if (stage == Stage.Analysis && composedMap is { } composition && warpAnalysis is { } analysis && hx >= composition.FabricStart && hx < composition.FabricStart + composition.FabricWidth)
            {
                double d = analysis.DeviationPercent[hx - composition.FabricStart];
                HoverDetails.Text = T($"تار {hx} · شل‌شدگی {Math.Max(0, -d):0.##}% · کشیدگی {Math.Max(0, d):0.##}%", $"Warp {hx} · looseness {Math.Max(0, -d):0.##}% · tightness {Math.Max(0, d):0.##}%");
            }
            else HoverDetails.Text = T($"ستون / قلاب منطقی: {hx} · رج: {hy}\nبیت فایل: {(pixel[0] == 0 ? 1 : 0)}", $"Logical hook: {hx} · row: {hy}\nFile bit: {(pixel[0] == 0 ? 1 : 0)}");
            HoverCard.Visibility = Visibility.Visible; return;
        }
        if (session?.Palette is not { } palette || SizeDirty || ColorsDirty || stage != Stage.Patterns) { HoverCard.Visibility = Visibility.Collapsed; return; }
        var point = e.GetPosition(PreviewImage); var hit = PreviewHitTest.Find(palette, point.X, point.Y, PreviewImage.ActualWidth, PreviewImage.ActualHeight);
        if (hit is null) { HoverCard.Visibility = Visibility.Collapsed; return; }
        var (x, y, index) = hit.Value; var entry = entries[index];
        HoverDetails.Text = T($"رنگ: {entry.ColorCode}\nتعداد پیکسل رنگ: {entry.PixelCount:N0} ({entry.PercentText})\nبافت: {entry.PatternLabel} · {entry.PatternSize}\nمختصات از بالا-چپ: X={x}, Y={y}", $"Color: {entry.ColorCode}\nColor pixels: {entry.PixelCount:N0} ({entry.PercentText})\nWeave: {entry.PatternLabel} · {entry.PatternSize}\nTop-left origin: X={x}, Y={y}");
        HoverCard.Visibility = Visibility.Visible;
    }
    private void PreviewImage_MouseLeave(object sender, MouseEventArgs e) => HoverCard.Visibility = Visibility.Collapsed;
    private async void RunAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.CanSave != true || SizeDirty || ColorsDirty) return;
        var composition = composedMap ?? await ComposeMachineAsync(true); if (composition is null) return;
        SetBusy(true);
        try
        {
            var fabric = CompositionView.Fabric(composition);
            warpAnalysis = await Task.Run(() => DesignAnalysis.Analyze(DesignAnalysis.ReadBinary(fabric), fabric.PixelWidth, fabric.PixelHeight));
            var (loose, tight) = CompositionView.Extremes(warpAnalysis, 0, fabric.PixelWidth);
            AnalysisResultText.Text = T($"بیشترین شل‌شدگی: {loose:0.##}%\nبیشترین کشیدگی: {tight:0.##}%", $"Maximum looseness: {loose:0.##}%\nMaximum tightness: {tight:0.##}%"); ShowStage();
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
        var composition = composedMap ?? await ComposeMachineAsync(true); if (composition is null) return;
        var preview = CompositionView.Fabric(composition); SetBusy(true);
        try
        {
            var found = await Task.Run(() => DesignAnalysis.FindFloats(DesignAnalysis.ReadBinary(preview), preview.PixelWidth, preview.PixelHeight, minimum, direction).Select(r => r with { X = r.X + composition.FabricStart }).ToList());
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
        if (floatRuns is not { Count: > 0 } || machinePreview is not { } preview) { FloatCurrentText.Text = T("فلوتی با این طول پیدا نشد.", "No runs meet the threshold."); return; }
        var run = floatRuns[floatIndex];
        FloatCurrentText.Text = T($"مورد {floatIndex + 1:N0} از {floatRuns.Count:N0} · {(run.Black ? "سیاه" : "سفید")} · طول {run.Length:N0}\nشروع X={run.X}, Y={run.Y} · برای اصلاح روی نقطه کلیک کنید.", $"Run {floatIndex + 1:N0} / {floatRuns.Count:N0} · {(run.Black ? "black" : "white")} · length {run.Length:N0}\nStart X={run.X}, Y={run.Y} · click a pixel to toggle.");
        var highlighted = new WriteableBitmap(new FormatConvertedBitmap(preview, PixelFormats.Bgra32, null, 0)); byte[] mark = new byte[run.Length * 4];
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
        if (busy || stage != Stage.Floats || session?.CanSave != true || SizeDirty || ColorsDirty || composedMap is not { } map) return;
        Point p = e.GetPosition(PreviewImage); if (p.X < 0 || p.Y < 0 || p.X >= PreviewImage.ActualWidth || p.Y >= PreviewImage.ActualHeight) return;
        int x = (int)(p.X * map.Image.PixelWidth / PreviewImage.ActualWidth), y = (int)(p.Y * map.Image.PixelHeight / PreviewImage.ActualHeight);
        int bodyX = x - map.DesignStart;
        if (bodyX < 0 || bodyX >= session.Palette!.Width || map.SignalHooks.Contains(x))
        { StatusText.Text = T("فقط نقاط خود طرح قابل ویرایش‌اند؛ کناره را از تنظیمات کناره تغییر دهید.", "Only design pixels can be edited; change selvedges in their settings."); return; }
        bool rescan = floatRuns is not null; int index = floatIndex, minimum = lastFloatMinimum; var direction = lastFloatDirection; var before = CaptureHistory(); SetBusy(true);
        try { await Task.Run(() => session.TogglePixel(bodyX, y)); RecordHistory(before); StatusText.Text = T("نقطه اصلاح شد؛ پروژه را ذخیره کنید.", "Pixel edited; save the project."); }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { MessageBox.Show(this, ex.Message, Title); rescan = false; }
        finally { SetBusy(false); }
        await ComposeMachineAsync(true); if (rescan) await ScanFloatsAsync(minimum, direction, index, false);
        e.Handled = true;
    }
    private void Info_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session is null) return;
        var dialog = new DesignInfoWindow(metadata, english) { Owner = this };
        if (dialog.ShowDialog() == true) { metadata = dialog.Result; MarkProjectDirty(); StatusText.Text = T("اطلاعات طرح ثبت شد؛ پروژه را ذخیره کنید.", "Design information updated; save the project."); }
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
        if (bankOpen) { if (ConfirmDiscardDrawing()) { bankOpen = false; bankTargetIndex = null; bankSelvedgeSide = null; WeaveCanvas.DiscardChanges(); ShowStage(); } return; }
        bankTargetIndex = null; bankSelvedgeSide = null; bankOpen = true; ShowStage(); await ReloadBankAsync();
    }
    private async void PatternBank_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || (sender as Button)?.DataContext is not PaletteEntry entry) return;
        bankSelvedgeSide = null; bankTargetIndex = entry.Index; bankOpen = true; ShowStage(); await ReloadBankAsync();
    }
    private async void SelvedgeBank_Click(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button { Tag: string side } || side is not ("left" or "right")) return;
        bankSelvedgeSide = side; bankTargetIndex = null; bankOpen = true; ShowStage(); await ReloadBankAsync();
    }
    private void BankBack_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !ConfirmDiscardDrawing()) return;
        WeaveCanvas.DiscardChanges(); bankOpen = false; bankTargetIndex = null; bankSelvedgeSide = null; ShowStage();
    }
    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true; if (busy || closePromptActive) return;
        closePromptActive = true;
        try { if (await ConfirmDiscardAsync()) { allowClose = true; Dispatcher.BeginInvoke(new Action(Close)); } }
        finally { closePromptActive = false; }
    }
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
        BankUseButton.Content = bankSelvedgeSide is null ? T("اختصاص به رنگ", "Apply to color") : T("اختصاص به کناره", "Apply to selvedge");
        BankUseButton.IsEnabled = !busy && item is not null && (bankSelvedgeSide is not null || (bankTargetIndex is int index && session?.Palette is not null && index >= 0 && index < entries.Count));
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
        if (busy || BankList.SelectedItem is not BankItem item || !ConfirmDiscardDrawing()) return;
        if (bankSelvedgeSide is string side)
        {
            var settings = ReadMachineSettings(); settings = side == "left" ? settings with { LeftPattern = item.Pattern, LeftName = item.Name } : settings with { RightPattern = item.Pattern, RightName = item.Name };
            LoadMachineSettings(settings); MarkProjectDirty(); InvalidateAnalysis(); WeaveCanvas.DiscardChanges(); bankOpen = false; bankSelvedgeSide = null; ShowStage();
            await PreviewSelvedgesAsync(false); return;
        }
        if (bankTargetIndex is not int index || session?.Palette is null || index < 0 || index >= entries.Count) return;
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
    private MachineSetup ReadMachineSettings()
    {
        var left = LeftSelvedgePattern.SelectedItem as BankItem; var right = RightSelvedgePattern.SelectedItem as BankItem;
        return new MachineSetup(LeftSelvedgeWidth.Text, RightSelvedgeWidth.Text, left?.Pattern, right?.Pattern, left?.Name ?? "", right?.Name ?? "", (absoluteLayout ? WeftCountInput.Text.Trim() is not ("" or "0" or "۰" or "٠") : BoxEnabled.IsChecked == true), Math.Max(0, BoxPlacement.SelectedIndex), BoxHooks.Text, BoxCodes.Text, BoxSequence.Text, BoxBottom.IsChecked == true, MachineUseSource.IsChecked == true, MachineRedAsBlack.IsChecked == true, absoluteLayout, TotalHooksInput.Text, DesignStartInput.Text, WeftStartInput.Text, WeftCountInput.Text);
    }
    private void InstallMachineChoices(IEnumerable<BankItem> items, MachineSetup settings)
    {
        var choices = items.ToList();
        BankItem? Make(WeavePattern? p, string name, string side)
        {
            if (p is null) return null;
            var item = new BankItem { Id = "project-" + side, Name = name, Pattern = p }; choices.Insert(0, item); return item;
        }
        var left = Make(settings.LeftPattern, settings.LeftName, "left"); var right = Make(settings.RightPattern, settings.RightName, "right");
        LeftSelvedgePattern.ItemsSource = choices; RightSelvedgePattern.ItemsSource = choices;
        LeftSelvedgePattern.SelectedItem = left; RightSelvedgePattern.SelectedItem = right;
    }
    private void LoadMachineSettings(MachineSetup setup)
    {
        loadingMachineSettings = true;
        try
        {
            absoluteLayout = setup.AbsoluteLayout; BoxEnabled.Visibility = absoluteLayout ? Visibility.Collapsed : Visibility.Visible; AbsoluteFields.Visibility = absoluteLayout ? Visibility.Visible : Visibility.Collapsed; LegacySetup.Visibility = absoluteLayout ? Visibility.Collapsed : Visibility.Visible;
            TotalHooksInput.Text = setup.TotalHooks; DesignStartInput.Text = setup.DesignStart; WeftStartInput.Text = setup.WeftStart; WeftCountInput.Text = setup.WeftCount;
            LeftSelvedgeWidth.Text = setup.LeftWidth; RightSelvedgeWidth.Text = setup.RightWidth; BoxEnabled.IsChecked = setup.BoxEnabled;
            BoxPlacement.SelectedIndex = setup.BoxPlacement; BoxHooks.Text = setup.BoxHooks; BoxCodes.Text = setup.BoxCodes; BoxSequence.Text = setup.Sequence; BoxBottom.IsChecked = setup.StartFromBottom; MachineUseSource.IsChecked = setup.UseSource; MachineRedAsBlack.IsChecked = setup.RedAsBlack;
            var presets = PatternLibrary.Presets.Select((p, index) => new BankItem { Id = "preset-" + index, Name = p.Name, Pattern = p.Pattern, IsPreset = true });
            InstallMachineChoices(presets, setup); InvalidateAnalysis(); MachineSetupSummary.Text = "";
        }
        finally { loadingMachineSettings = false; }
    }
    private async Task RefreshMachineBankAsync()
    {
        if (busy) return; var setup = ReadMachineSettings(); SetBusy(true);
        try
        {
            var result = await Task.Run(() => { var items = bank.Load(out int skipped); return (items, skipped); });
            loadingMachineSettings = true; try { InstallMachineChoices(result.items, setup); } finally { loadingMachineSettings = false; }
            if (result.skipped > 0) MachineSetupSummary.Text = T($"{result.skipped} بافت نامعتبر بانک نادیده گرفته شد.", $"{result.skipped} invalid bank items skipped.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { MessageBox.Show(this, T("خواندن بانک ممکن نشد: ", "Could not read weave bank: ") + ex.Message, Title); }
        finally { SetBusy(false); }
    }
    private async void RefreshMachineBank_Click(object sender, RoutedEventArgs e) => await RefreshMachineBankAsync();
    private void MachineSettings_TextChanged(object sender, TextChangedEventArgs e) => MachineSettingsChanged();
    private void MachineSettings_SelectionChanged(object sender, SelectionChangedEventArgs e) => MachineSettingsChanged();
    private void MachineSettings_CheckChanged(object sender, RoutedEventArgs e) => MachineSettingsChanged();
    private void MachineSettingsChanged()
    {
        if (!IsInitialized || loadingMachineSettings) return;
        MarkProjectDirty(); InvalidateAnalysis(); MachineSetupSummary.Text = T("تنظیمات تغییر کرد؛ پیش‌نمایش را دوباره بسازید.", "Settings changed; regenerate the preview.");
        if ((stage is Stage.Machine or Stage.Weft or Stage.Selvedge) && !busy) ShowStage();
        if ((stage is Stage.Selvedge or Stage.Weft or Stage.Machine) && selvedgeTimer is not null) { selvedgeTimer.Stop(); selvedgeTimer.Start(); }
    }
    private BitmapSource? MachineInput() => session is null ? null : stage == Stage.Machine && MachineUseSource.IsChecked == true ? session.Source : session.CanSave && !SizeDirty && !ColorsDirty ? session.PatternPreview : null;
    private async Task<MachineComposition?> ComposeMachineAsync(bool showErrors = true)
    {
        var frame = MachineInput(); if (frame is null) return null;
        var settings = ReadMachineSettings(); bool red = MachineRedAsBlack.IsChecked == true;
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => MachineComposer.Compose(frame, settings, red));
            composedMap = result; machinePreview = result.Image; redPreview = stage == Stage.Machine && MachineRedAsBlack.IsChecked == true ? await Task.Run(() => CompositionView.RedPreview(result.Image)) : null;
            string signals = result.SignalHooks.Length == 0 ? T("غیرفعال", "off") : string.Join(", ", result.SignalHooks);
            MachineSetupSummary.Text = T($"نقشه نهایی: {result.Image.PixelWidth:N0} قلاب × {result.Image.PixelHeight:N0} رج · قلاب‌های فرمان: {signals}", $"Final map: {result.Image.PixelWidth:N0} hooks × {result.Image.PixelHeight:N0} picks · signal hooks: {signals}");
            ShowStage(); return result;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { composedMap = null; machinePreview = null; redPreview = null; MachineSetupSummary.Text = T("تنظیمات معتبر نیست: ", "Invalid setup: ") + ex.Message; StatusText.Text = MachineSetupSummary.Text; if (showErrors) MessageBox.Show(this, MachineSetupSummary.Text, Title, MessageBoxButton.OK, MessageBoxImage.Warning); ShowStage(); return null; }
        finally { SetBusy(false); }
    }
    private void UpgradeLayout_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, T("تغییر به چیدمان جدید، کدهای قدیمی باکس را غیرفعال می‌کند. ادامه می‌دهید؟", "Switching to the new layout disables legacy box codes. Continue?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        absoluteLayout = true; BoxEnabled.IsChecked = false; BoxEnabled.Visibility = Visibility.Collapsed; BoxSequence.Text = ""; AbsoluteFields.Visibility = Visibility.Visible; LegacySetup.Visibility = Visibility.Collapsed; MachineSettingsChanged();
    }
    private async Task PreviewSelvedgesAsync(bool showErrors)
    {
        var frame = MachineInput(); if (busy || frame is null) return;
        var setup = ReadMachineSettings() with { AbsoluteLayout = false, BoxEnabled = false }; bool red = MachineRedAsBlack.IsChecked == true;
        SetBusy(true);
        try { var result = await Task.Run(() => MachineComposer.Compose(frame, setup, red)); composedMap = null; machinePreview = result.Image; redPreview = null; SelvedgeSummary.Text = T($"عرض طرح همراه کناره‌ها: {result.Image.PixelWidth:N0} تار", $"Design with selvedges: {result.Image.PixelWidth:N0} hooks"); ShowStage(); }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException) { SelvedgeSummary.Text = T("عرض یا بافت کناره معتبر نیست.", "Invalid selvedge width or weave."); if (showErrors) MessageBox.Show(this, SelvedgeSummary.Text + "\n" + ex.Message, Title); }
        finally { SetBusy(false); }
    }
    private async void SelvedgePreview_Click(object sender, RoutedEventArgs e) => await PreviewSelvedgesAsync(true);
    private async void MachinePreview_Click(object sender, RoutedEventArgs e) { if (!busy) await ComposeMachineAsync(); }
    private async void MachineExport_Click(object sender, RoutedEventArgs e)
    {
        if (busy || machineFormatIndex is not (1 or 4) || session is null || path is null) return;
        var composed = await ComposeMachineAsync(); if (composed is null) return;
        string format = machineFormatIndex == 4 ? "JC6" : "JC5";
        bool overwriteColumns = !absoluteLayout && BoxEnabled.IsChecked == true && BoxPlacement.SelectedIndex == 2;
        string warning = T($"خروجی {format} آزمایشی و تک‌ناحیه‌ای است: {composed.Image.PixelWidth} قلاب و {composed.Image.PixelHeight} رج. بیت ۱ سیاه است. " + (overwriteColumns ? "قلاب‌های فرمان محتوای ستون‌های موجود را جایگزین می‌کنند. " : "") + "توالی و نگاشت منطقی را در پیش‌نمایش و اتصال و قطبیت قلاب‌های واقعی را با متخصص دستگاه بررسی کنید. ادامه می‌دهید؟",
            $"Experimental single-area {format}: {composed.Image.PixelWidth} hooks and {composed.Image.PixelHeight} picks. Bit 1 is black. " + (overwriteColumns ? "Signal hooks replace existing columns. " : "") + "Verify the preview, physical wiring and signal polarity with the machine specialist. Continue?");
        if (MessageBox.Show(this, warning, Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var dialog = new SaveFileDialog { Filter = $"{format} (*.jc5)|*.jc5", DefaultExt = ".jc5", AddExtension = true, OverwritePrompt = true, FileName = MachineFileNaming.SuggestedName(machineFormatIndex) };
        if (dialog.ShowDialog(this) != true) return;
        if (!MachineFileNaming.ValidDestination(dialog.FileName, machineFormatIndex))
        { MessageBox.Show(this, T("برای JC6 نام فایل باید شامل عدد ۶ باشد و پسوند آن jc5 بماند؛ مانند design6.jc5.", "For JC6, the name must contain 6 and the extension must be .jc5, e.g. design6.jc5."), Title, MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var protectedPaths = new[] { path }.Concat(projectPath is null ? Array.Empty<string>() : new[] { projectPath }).Concat(entries.Select(x => x.PatternPath).OfType<string>()).ToArray();
        SetBusy(true);
        try
        {
            await Task.Run(() => Jc5Codec.Save(composed.Image, dialog.FileName, protectedPaths));
            StatusText.Text = T($"{format} آزمایشی ذخیره شد؛ پیش از تولید آن را مستقل بررسی کنید.", $"Experimental {format} saved; verify independently before production.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        { MessageBox.Show(this, T("ذخیره فایل ماشین ناموفق بود: ", "Loom file save failed: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private async void MachineImport_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "JC5 / JC6 (*.jc5;*.jc6)|*.jc5;*.jc6", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || !await ConfirmDiscardAsync()) return;
        SetBusy(true);
        try
        {
            bool legacy = MachineLegacyImport.IsChecked == true;
            var image = await Task.Run(() => Jc5Codec.Load(dialog.FileName, legacy));
            var next = new WorkflowSession(image); next.ApplySize(image.PixelWidth, image.PixelHeight);
            session = next; path = dialog.FileName; projectPath = null; projectCreated = DateTimeOffset.Now; metadata = new(); LoadMachineSettings(new MachineSetup(AbsoluteLayout: true)); ClearHistory(); sourceName = Path.GetFileName(path);
            current = new ImageResult(image, 1, 0, "JC5 (experimental)"); RebuildPaletteEntries();
            WidthInput.Text = image.PixelWidth.ToString(); HeightInput.Text = image.PixelHeight.ToString(); ColorInput.Text = "50";
            FileValue.Text = sourceName; FormatValue.Text = "JC5 · experimental"; stage = Stage.Size; SetZoom(1); projectDirty = true; ShowStage();
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
    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || SizeDirty || ColorsDirty) return;
        var currentSession = session; var setup = ReadMachineSettings();
        SetBusy(true); BitmapSource? bitmap = null;
        try
        {
            WarpAnalysis? reportAnalysis = null; MachineComposition? reportMap = null;
            if (currentSession.CanSave && currentSession.PatternPreview is { } pattern)
            {
                var input = setup.UseSource ? currentSession.Source : pattern;
                reportMap = await Task.Run(() => MachineComposer.Compose(input, setup, setup.RedAsBlack));
                var fabric = CompositionView.Fabric(reportMap);
                reportAnalysis = await Task.Run(() => DesignAnalysis.Analyze(DesignAnalysis.ReadBinary(fabric), fabric.PixelWidth, fabric.PixelHeight));
            }
            string name = projectPath is null ? T("پروژه جدید", "New project") : Path.GetFileNameWithoutExtension(projectPath);
            var page = DesignReport.CreatePage(currentSession, entries, name, projectCreated, english, metadata, setup, reportAnalysis, reportMap); bitmap = DesignReport.Render(page);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(this, T("ساخت گزارش ممکن نشد: ", "Could not prepare report: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
        if (bitmap is not null)
        {
            string name = projectPath is null ? T("پروژه جدید", "New project") : Path.GetFileNameWithoutExtension(projectPath);
            var protectedPaths = new[] { path, projectPath }.OfType<string>().Concat(entries.Select(x => x.PatternPath).OfType<string>());
            new ReportWindow(bitmap, name, protectedPaths, english) { Owner = this }.ShowDialog();
        }
    }
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow(english) { Owner = this }.ShowDialog();
    private void Language_Click(object sender, RoutedEventArgs e) { english = !english; MarkProjectDirty(); UpdateLabels(); ShowStage(); }
    private void Theme_Click(object sender, RoutedEventArgs e) { lightTheme = !lightTheme; ThemeManager.Apply(lightTheme, true); RefreshControls(); }
    private void UpdateLabels()
    {
        FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        SettingsBorder.FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        LanguageButton.Content = english ? "FA" : "EN"; LanguageButton.ToolTip = T("تغییر زبان", "Change language"); AboutButton.ToolTip = T("درباره", "About"); HelpButton.ToolTip = T("راهنمای برنامه (F1)", "Help (F1)"); OpenProjectButton.ToolTip = T("باز کردن پروژه", "Open project"); SaveProjectButton.ToolTip = T("ذخیره پروژه", "Save project"); SaveProjectAsButton.ToolTip = T("ذخیره با نام دیگر", "Save project as"); ThemeButton.ToolTip = T("تغییر تم", "Change theme"); BankButton.ToolTip = T("بانک و طراحی بافت", "Weave bank and editor"); UndoButton.ToolTip = T("بازگشت (Ctrl+Z)", "Undo (Ctrl+Z)"); RedoButton.ToolTip = T("بازانجام (Ctrl+Y)", "Redo (Ctrl+Y)"); ReportButton.ToolTip = T("گزارش PDF و چاپ", "PDF report and print"); InfoButton.Content = T("اطلاعات طرح", "Design info"); InfoButton.ToolTip = T("طراح، کاربرد و توضیحات", "Designer, use and description");
        OpenButton.ToolTip = T("باز کردن پروژه یا فایل", "Open project or image"); SizeStepButton.ToolTip = T("تنظیم اندازه", "Resize"); ColorStepButton.ToolTip = T("مدیریت رنگ", "Color management"); PatternStepButton.ToolTip = T("بافت‌ریزی", "Weave assignment"); SelvedgeStepButton.ToolTip = T("تعریف کناره", "Selvedges"); WeftStepButton.ToolTip = T("تعریف پودگذاری", "Weft / box motion"); FloatStepButton.ToolTip = T("فلوت‌گیری", "Float check"); AnalysisStepButton.ToolTip = T("تحلیل", "Analysis"); MachineStepButton.ToolTip = T("خروجی ماشین", "Machine output");
        AnalysisTitle.Text = T("تحلیل تارها", "Warp analysis"); AnalysisHint.Text = T("درصدها شاخص نسبی‌اند، نه اندازه‌گیری فیزیکی.", "Percentages are relative indicators, not physical measurements."); RunAnalysisButton.Content = T("محاسبه درصدها", "Calculate percentages");
        FloatTitle.Text = T("بررسی فلوت", "Float inspection"); FloatHint.Text = T("کناره‌ها نیز بررسی می‌شوند. فقط نقاط خود طرح قابل اصلاح‌اند؛ کناره را از تنظیمات آن تغییر دهید.", "Selvedges are included. Only design pixels can be edited; adjust selvedges in their settings."); FloatLengthLabel.Text = T("حداقل طول فلوت", "Minimum run length"); FloatWarp.Content = T("تار (ستون)", "Warp (column)"); FloatWeft.Content = T("پود (ردیف)", "Weft (row)"); FloatScanButton.Content = T("بررسی فلوت‌ها", "Scan floats"); FloatPreviousButton.Content = T("قبلی", "Previous"); FloatNextButton.Content = T("بعدی", "Next"); ColorSwatchColumn.Header = T("رنگ", "Color"); ColorCodeColumn.Header = T("کد رنگ", "Color code"); ColorCountColumn.Header = T("پیکسل", "Pixels"); ColorPercentColumn.Header = T("درصد", "Percent");
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
        MachineTitle.Text = T("خروجی ماشین", "Machine output"); MachineHint.Text = T("JC5 و JC6 تک‌ناحیه‌ای بر اساس نمونه DesignScope به‌صورت آزمایشی فعال هستند؛ سایر قالب‌ها پشتیبانی نمی‌شوند.", "Experimental single-area JC5 and JC6 based on DesignScope samples; other formats are not supported.");
        MachineFormatLabel.Text = T("قالب هدف", "Target format"); MachineRedAsBlack.Content = T("نمایش بافت به رنگ قرمز / قرمز = بیت فعال", "Display active pixels in red / red = active"); MachineLegacyImport.Content = T("خواندن فایل خروجی نسخه قدیمی ۰۰۶", "Import a file created by old version 006"); MachineUseSource.Content = T("استفاده از نقشه اصلی به جای نتیجه بافت", "Use original hook map instead of pattern result"); MachineExportButton.Content = T("تبدیل و ذخیره فایل ماشین", "Convert and save loom file"); MachineImportButton.Content = T("انتخاب فایل ماشین", "Select loom file");
        SelvedgeTitle.Text = T("تعریف کناره", "Selvedges"); WeftTitle.Text = T("تعریف پودگذاری", "Weft / box motion"); MachineSetupHint.Text = T("کناره‌ها به عرض طرح اضافه می‌شوند؛ برای هر سمت، پهنا و بافت بانک را انتخاب کنید.", "Selvedges are added to the design width; select a width and library weave for each side.");
        WeftHint.Text = T("شماره ستون‌ها از صفر و از چپ است؛ ستون‌های خالی سفید می‌مانند.", "Columns are zero-based from the left; unused columns stay white."); TotalHooksLabel.Text = T("تعداد کل تارها (خالی = خودکار)", "Total hooks (blank = automatic)"); DesignStartLabel.Text = T("شروع طرح (خالی = پس از کناره)", "Design start (blank = after selvedge)"); WeftCountLabel.Text = T("تعداد پود", "Number of wefts"); WeftStartLabel.Text = T("شروع پود از ستون", "Weft starting column"); SelvedgePreviewButton.Content = T("پیش‌نمایش کناره‌ها", "Preview selvedges"); LeftSelvedgeBank.Content = T("انتخاب کناره چپ از بانک", "Choose left weave from bank"); RightSelvedgeBank.Content = T("انتخاب کناره راست از بانک", "Choose right weave from bank"); LegacySetup.Header = T("تنظیمات قدیمی پروژه", "Legacy project settings"); UpgradeLayoutButton.Content = T("تغییر به چیدمان جدید", "Switch to new layout");
        LeftSelvedgeLabel.Text = T("کناره چپ: تعداد قلاب و بافت", "Left selvedge: hooks and weave"); RightSelvedgeLabel.Text = T("کناره راست: تعداد قلاب و بافت", "Right selvedge: hooks and weave"); RefreshMachineBankButton.Content = T("به‌روزرسانی فهرست بانک", "Refresh weave bank");
        BoxEnabled.Content = T("فعال‌سازی قلاب‌های باکس‌موشن", "Enable box-motion signal hooks"); BoxPlacementLabel.Text = T("محل قلاب‌های فرمان", "Signal hook placement"); BoxAddLeft.Content = T("قلاب‌های اضافه در چپ", "Add signal hooks on left"); BoxAddRight.Content = T("قلاب‌های اضافه در راست", "Add signal hooks on right"); BoxReserved.Content = T("ستون‌های موجود (جایگزین محتوا)", "Existing columns (replace contents)");
        BoxHooksLabel.Text = T("تعداد قلاب اضافه / شماره ستون‌های موجود", "Extra hook count / existing column indices"); BoxCodesLabel.Text = T("الگوی هر پود؛ هر خط مثل 1=10", "Weft codes; one per line, e.g. 1=10"); BoxSequenceLabel.Text = T("توالی پودها؛ خالی = ترتیب طبیعی", "Weft sequence; blank = natural order"); BoxBottom.Content = T("شروع توالی از پایین تصویر", "Start sequence at image bottom");
        BoxSafetyHint.Text = T("در هر رج فقط ستون پود انتخابی سیاه است؛ اتصال و قطبیت واقعی دستگاه باید مستقل تأیید شود.", "Only the selected weft column is black per row. Independently verify wiring and polarity."); MachinePreviewButton.Content = T("پیش‌نمایش نقشه نهایی ماشین", "Preview final machine map");
        MachineNotice.Text = T("نقشه خروجی تک‌ناحیه‌ای و آزمایشی است. کناره و فرمان فقط به صورت ستون‌های منطقی درج می‌شوند؛ نگاشت فیزیکی و قطبیت کنترلر را قبل از تولید بررسی کنید.", "Output is experimental and single-area. Selvedges and signals are logical columns only; verify physical mapping and polarity before production.");
        SafetyText.Text = T("خروجی تصویر است، نه فایل دستگاه بافندگی.", "Output is an image, not a loom file."); CanvasSubtitle.Text = T("خروجی هر مرحله در این قسمت نمایش داده می‌شود", "The result of each stage appears here");
        EmptyTitle.Text = T("طرح خود را وارد کنید", "Bring in your design"); EmptyHint.Text = T("یک تصویر BMP یا TIFF انتخاب کنید.", "Choose a BMP or TIFF image."); EmptyButton.Content = T("انتخاب تصویر", "Choose image");
        if (session is null) StatusText.Text = T("آماده برای باز کردن تصویر", "Ready to open an image");
    }
}
