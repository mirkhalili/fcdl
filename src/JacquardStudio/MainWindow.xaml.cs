using System.IO;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace JacquardStudio;

public partial class MainWindow : Window
{
    private enum Stage { Size, Colors, Patterns, Machine, Analysis, Floats, Selvedge, Weft, Simulation }
    private Stage stage = Stage.Size;
    private bool english, busy, bankOpen, lightTheme, projectDirty, allowClose, closePromptActive;
    private ModuleKind? moduleEditorKind;
    public WorkflowSession? ModuleEditorResult { get; private set; }
    public WeavePattern? ModuleWeaveResult { get; private set; }
    public string ModuleWeaveName { get; private set; } = "";
    private int moduleProtectedLeft, moduleProtectedRight;
    public int EditedFloatMinimum { get; private set; }
    public bool EditedFloatWarp { get; private set; }
    private IReadOnlyList<BankItem> connectedWeaves = Array.Empty<BankItem>();
    public Visibility ConnectedWeavesVisibility => moduleEditorKind==ModuleKind.ApplyWeaves ? Visibility.Visible : Visibility.Collapsed;
    private int? bankTargetIndex;
    private string? bankSelvedgeSide;
    private readonly WeaveBank bank = new();
    private IReadOnlyList<BankItem> allBankItems = Array.Empty<BankItem>();
    private bool loadingBankCategories;
    private string? path;
    private string? projectPath;
    private string sourceName = "";
    private int machineFormatIndex = 1;
    private ImageResult? current;
    private WorkflowSession? session;
    private List<PaletteEntry> entries = new();
    private double zoom = 1;
    private bool panning, selectingColors;
    private Point colorSelectionStart;
    private string? colorSortProperty; private ListSortDirection colorSortDirection = ListSortDirection.Ascending;
    private Point panStart;
    private double panHorizontal, panVertical;
    private BitmapSource? machinePreview, redPreview, simulationPreview, colorHighlight;
    private int? highlightedColor;
    private bool loadingSimulationSettings;
    private int simulationGridWidth, simulationGridHeight;
    private readonly ColorSwatch warpSwatch = new("#DABFA0", "رنگ تار / Warp color");
    private int defaultWeftYarn = 1; private string defaultWeftColor = "#537481";
    private readonly Dictionary<string, SimulatedYarn> simulationYarns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> weftGeometryLabels = new(StringComparer.Ordinal);
    private MachineComposition? composedMap, simulationMap;
    private bool loadingMachineSettings, absoluteLayout;
    private WeftSettings appliedWeft = new();
    private System.Windows.Threading.DispatcherTimer? selvedgeTimer;
    private DateTimeOffset? projectCreated;
    private DesignMetadata metadata = new();
    private WarpAnalysis? warpAnalysis;
    private List<FloatRun>? floatRuns;
    private int floatIndex, lastFloatMinimum;
    private FloatDirection lastFloatDirection;
    private sealed record HistoryState(WorkflowSession Session, Stage Stage, string?[] Names, string?[] Paths);
    private readonly List<HistoryState> undoHistory = new(), redoHistory = new();

    public MainWindow() { InitializeComponent(); SimulationWarpYarn.ItemsSource = FabricSimulation.YarnNames; SimulationWarpUnit.ItemsSource = YarnGeometry.Units; SimulationWarpColorHost.Children.Add(warpSwatch); warpSwatch.ColorChanged += (_, _) => SimulationSettingsChanged(); LoadSimulationSettings(new SimulationSettings()); selvedgeTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) }; selvedgeTimer.Tick += async (_, _) => { selvedgeTimer.Stop(); if (!busy && !bankOpen) { if (stage == Stage.Selvedge) await PreviewSelvedgesAsync(false); else if (stage is Stage.Weft or Stage.Machine) await ComposeMachineAsync(false); } }; WeaveCanvas.Changed += (_, _) => { WeaveCanvas.SizeForScrolling(); RefreshControls(); }; WeaveCanvas.SizeForScrolling(); lightTheme = ThemeManager.IsLight; ThemeManager.Apply(ThemeManager.Current, false); VersionText.Text = AppVersion.Display; LoadMachineSettings(new MachineSetup(AbsoluteLayout: true)); UpdateLabels(); RefreshControls(); UpdateWindowTitle(); }
    /// <summary>Use the existing stage UI on an isolated snapshot; no graph state changes before Apply.</summary>
    public void ConfigureModuleEditor(ModuleKind kind, WorkflowSession prepared, IReadOnlyList<BankItem>? projectWeaves=null)
    {
        if (kind is not (ModuleKind.Resize or ModuleKind.Colors or ModuleKind.ApplyWeaves or ModuleKind.Floats or ModuleKind.Tension)) throw new ArgumentException("This module is not supported by the legacy stage editor.");
        moduleEditorKind=kind; connectedWeaves=projectWeaves ?? Array.Empty<BankItem>(); AllowDrop=false; session=prepared.Snapshot(); path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"jacquard-graph-input"); sourceName="Graph input"; current=new ImageResult(session.Source,1,0,"Graph");
        WidthInput.Text=session.Resized!.PixelWidth.ToString(); HeightInput.Text=session.Resized.PixelHeight.ToString(); ColorInput.Text=(session.AppliedColorLimit ?? 256).ToString();
        stage=kind switch { ModuleKind.Resize => Stage.Size, ModuleKind.ApplyWeaves => Stage.Patterns, ModuleKind.Floats => Stage.Floats, ModuleKind.Tension => Stage.Analysis, _ => Stage.Colors }; Navigation.Visibility=Visibility.Collapsed;
        OpenButton.Visibility=SaveProjectButton.Visibility=SaveProjectAsButton.Visibility=InfoButton.Visibility=ReportButton.Visibility=BankButton.Visibility=Visibility.Collapsed;
        ModuleApplyButton.Visibility=ModuleCancelButton.Visibility=Visibility.Visible; ToColorButton.Visibility=ToPatternButton.Visibility=Visibility.Collapsed;
        RebuildPaletteEntries(); ShowStage(); UpdateWindowTitle();
    }
    public void ConfigureBinaryModuleEditor(ModuleKind kind, WorkflowSession prepared, int minimum, bool warp, int protectedLeft, int protectedRight)
    {
        if(kind is not (ModuleKind.Floats or ModuleKind.Tension)) throw new ArgumentException("Invalid binary editor.");
        moduleProtectedLeft=protectedLeft; moduleProtectedRight=protectedRight; ConfigureModuleEditor(kind,prepared);
        FloatMinimum.Text=minimum.ToString(); FloatWarp.IsChecked=warp; FloatWeft.IsChecked=!warp;
        Loaded+=async (_,_)=>{ if(await ComposeMachineAsync(true) is not null && kind==ModuleKind.Tension) await RunAnalysisAsync(); };
    }
    /// <summary>Expose the original weave designer and bank on an independent node draft.</summary>
    public void ConfigureWeaveModuleEditor(WeavePattern? initial, string? name)
    {
        moduleEditorKind=ModuleKind.CreateWeave; AllowDrop=false; bankOpen=true;
        Navigation.Visibility=Visibility.Collapsed;
        OpenButton.Visibility=SaveProjectButton.Visibility=SaveProjectAsButton.Visibility=InfoButton.Visibility=ReportButton.Visibility=BankButton.Visibility=Visibility.Collapsed;
        BankBackButton.Visibility=BankUseButton.Visibility=Visibility.Collapsed;
        ModuleApplyButton.Visibility=ModuleCancelButton.Visibility=Visibility.Visible;
        if(initial is not null) { WeaveCanvas.SetPattern(initial); WeaveWidth.Text=initial.Width.ToString(); WeaveHeight.Text=initial.Height.ToString(); }
        WeaveName.Text=string.IsNullOrWhiteSpace(name) ? "بافت پروژه" : name;
        ShowStage(); Loaded += async (_,_) => await ReloadBankAsync();
    }
    private void ModuleApply_Click(object sender,RoutedEventArgs e)
    {
        if (busy || moduleEditorKind is null) return;
        if (moduleEditorKind==ModuleKind.CreateWeave)
        {
            if (string.IsNullOrWhiteSpace(WeaveName.Text) || WeaveName.Text.Trim().Length>100) { MessageBox.Show(this,T("نام بافت باید ۱ تا ۱۰۰ حرف باشد.","The weave name must contain 1 to 100 characters.")); return; }
            ModuleWeaveResult=WeaveCanvas.Snapshot(); ModuleWeaveName=WeaveName.Text.Trim(); allowClose=true; DialogResult=true; return;
        }
        if (session is null) return;
        if (bankOpen) { MessageBox.Show(this,T("ابتدا از بانک به ماژول بازگردید.","Return from the bank before applying.")); return; }
        if (moduleEditorKind==ModuleKind.ApplyWeaves && !session.CanSave) { MessageBox.Show(this,T("ابتدا برای همه رنگ‌ها بافت انتخاب کنید.","Assign a weave to every color first.")); return; }
        if (SizeDirty || moduleEditorKind==ModuleKind.Colors && ColorsDirty) { MessageBox.Show(this,T("ابتدا تغییر اندازه یا تعداد رنگ را در همین پنجره اعمال کنید.","Apply the current size or color limit first.")); return; }
        if(moduleEditorKind==ModuleKind.Floats) { if(!NumberInput.TryInt(FloatMinimum.Text,out int minimum) || minimum<1 || minimum>DesignProcessor.MaxPixels) {MessageBox.Show(this,"حداقل طول فلوت معتبر نیست.");return;} EditedFloatMinimum=minimum; EditedFloatWarp=FloatWarp.IsChecked==true; }
        ModuleEditorResult=session.Snapshot(); allowClose=true; DialogResult=true;
    }
    private void ModuleCancel_Click(object sender,RoutedEventArgs e) { if (busy) return; allowClose=true; DialogResult=false; }
    private void UpdateWindowTitle() => Title = $"Jacquard Studio ({(projectPath is null ? T("پروژه جدید", "New project") : Path.GetFileNameWithoutExtension(projectPath))}{(projectDirty && session is not null ? " *" : "")})";
    private void MarkProjectDirty() { if (session is not null) { projectDirty = true; UpdateWindowTitle(); } }
    private string T(string fa, string en) => english ? en : fa;
    private bool SizeDirty => session?.Resized is null || !NumberInput.TryInt(WidthInput.Text, out int w) || !NumberInput.TryInt(HeightInput.Text, out int h) || w != session.Resized.PixelWidth || h != session.Resized.PixelHeight;
    private bool ColorsDirty => session?.Palette is null || !NumberInput.TryInt(ColorInput.Text, out int colors) || colors != session.AppliedColorLimit;

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
        var dialog = new OpenFileDialog { Filter = "Supported files|*.sam;*.bmp;*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.ico;*.wdp;*.jxr;*.pdf;*.ai;*.jc5;*.jc6|SAM project (*.sam)|*.sam|Images and PDF/AI|*.bmp;*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.ico;*.wdp;*.jxr;*.pdf;*.ai|Machine (*.jc5;*.jc6)|*.jc5;*.jc6|All files (*.*)|*.*", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true || !await ConfirmDiscardAsync()) return;
        await OpenAnyAsync(dialog.FileName);
    }
    private async Task OpenAnyAsync(string filename)
    {
        string ext = Path.GetExtension(filename).ToLowerInvariant();
        if (ext == ".sam") await LoadProjectAsync(filename);
        else if (ext is ".jc5" or ".jc6") await LoadMachineAsync(filename);
        else await LoadAsync(filename, 0);
    }
    private async Task LoadAsync(string filename, int page, bool showErrors = true)
    {
        SetBusy(true); StatusText.Text = T("در حال باز کردن تصویر…", "Loading image…");
        try
        {
            var result = await Task.Run(() => ImageLoader.Load(filename, page));
            var next = new WorkflowSession(result.Image, result.Vector);
            double scale = Math.Min(1, Math.Sqrt((double)DesignProcessor.MaxPixels / ((long)result.Image.PixelWidth * result.Image.PixelHeight)));
            int initialWidth = Math.Min(DesignProcessor.MaxPixels, Math.Max(1, (int)Math.Floor(result.Image.PixelWidth * scale)));
            int initialHeight = Math.Min(DesignProcessor.MaxPixels / initialWidth, Math.Max(1, (int)Math.Floor(result.Image.PixelHeight * scale)));
            await Task.Run(() => next.ApplySize(initialWidth, initialHeight));
            path = filename; projectPath = null; projectCreated = DateTimeOffset.Now; metadata = new(); LoadMachineSettings(new MachineSetup(AbsoluteLayout: true)); LoadSimulationSettings(new SimulationSettings()); ClearHistory(); sourceName = Path.GetFileName(filename); current = result; session = next; RebuildPaletteEntries();
            WidthInput.Text = initialWidth.ToString(); HeightInput.Text = initialHeight.ToString(); ColorInput.Text = "256";
            FileValue.Text = Path.GetFileName(filename); FormatValue.Text = $"{result.Format} · {result.Image.Format.BitsPerPixel} bpp";
            zoom = 1; SetZoom(1); stage = Stage.Size; projectDirty = true; ShowStage();
            StatusText.Text = scale < 1 ? T("تصویر بزرگ بود؛ اندازه اولیه متناسب با سقف ۴ میلیون پیکسل کاهش یافت.", "Image was scaled to fit the 4-million-pixel processing limit.") : T("اندازه اولیه اعمال شد؛ در صورت نیاز آن را تغییر دهید.", "Original dimensions applied; change them if needed.");
        }
        catch (Exception ex)
        {
            StatusText.Text = T("باز کردن تصویر ناموفق بود.", "Could not open image.");
            if (!showErrors) throw;
            MessageBox.Show(this, T("باز کردن تصویر ممکن نشد.\n", "Could not open the image.\n") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false); }
    }
    private async void PreviousPage_Click(object sender, RoutedEventArgs e) { if (!busy && path is not null && current is not null && current.PageIndex > 0 && await ConfirmDiscardAsync()) await LoadAsync(path, current.PageIndex - 1); }
    private async void NextPage_Click(object sender, RoutedEventArgs e) { if (!busy && path is not null && current is not null && current.PageIndex + 1 < current.PageCount && await ConfirmDiscardAsync()) await LoadAsync(path, current.PageIndex + 1); }
    private void Window_DragOver(object sender, DragEventArgs e) { e.Effects = !busy && !bankOpen && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (busy || bankOpen || e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } files || !await ConfirmDiscardAsync()) return;
        await OpenAnyAsync(files[0]);
    }
    private void Inputs_Changed(object sender, TextChangedEventArgs e) { if (IsInitialized) { MarkProjectDirty(); RefreshControls(); } }
    private void SetBusy(bool value) { busy = value; RefreshControls(); }
    private void RefreshControls()
    {
        if (!IsInitialized) return;
        OpenButton.IsEnabled = !busy; SaveProjectButton.IsEnabled = !busy && session is not null; SaveProjectAsButton.IsEnabled = !busy && session is not null; BankButton.IsEnabled = !busy; EmptyButton.IsEnabled = !busy; SettingsBorder.IsEnabled = !busy; BankWorkspace.IsEnabled = !busy;
        SizeStepButton.IsEnabled = !busy && session is not null;
        ColorStepButton.IsEnabled = !busy && session?.Resized is not null && !SizeDirty;
        PatternStepButton.IsEnabled = !busy && session?.Palette is not null && !SizeDirty && !ColorsDirty;
        MachineStepButton.IsEnabled = !busy;
        AnalysisStepButton.IsEnabled = FloatStepButton.IsEnabled = SelvedgeStepButton.IsEnabled = WeftStepButton.IsEnabled = SimulationStepButton.IsEnabled = !busy && session?.CanSave == true && !SizeDirty && !ColorsDirty;
        InfoButton.IsEnabled = !busy && !bankOpen && session is not null;
        FloatAutoButton.IsEnabled = !busy && stage == Stage.Floats && session?.CanSave == true && !SizeDirty && !ColorsDirty;
        FloatPreviousButton.IsEnabled = !busy && floatRuns is { Count: > 0 } && floatIndex > 0;
        FloatNextButton.IsEnabled = !busy && floatRuns is { Count: > 0 } && floatIndex < floatRuns.Count - 1;
        SimulationRenderButton.IsEnabled = !busy && stage == Stage.Simulation && session?.CanSave == true;
        SimulationSaveButton.IsEnabled = !busy && stage == Stage.Simulation && simulationPreview is not null && simulationMap is not null;
        MachinePreviewButton.IsEnabled = !busy && stage == Stage.Weft && MachineInput() is not null;
        SelvedgePreviewButton.IsEnabled = !busy && stage == Stage.Selvedge && MachineInput() is not null;
        MachineExportButton.IsEnabled = !busy && stage == Stage.Machine && (machineFormatIndex is 1 or 4 or 5) && session is not null &&
            (MachineUseSource.IsChecked == true || (session.CanSave && !SizeDirty && !ColorsDirty));
        SaveSizeButton.IsEnabled = !busy && stage == Stage.Size && session?.Resized is not null && !SizeDirty;
        SaveColorButton.IsEnabled = !busy && stage == Stage.Colors && session?.ColorPreview is not null && !SizeDirty && !ColorsDirty;
        ToColorButton.IsEnabled = ColorStepButton.IsEnabled; ToPatternButton.IsEnabled = PatternStepButton.IsEnabled;
        RandomWeaveButton.IsEnabled = !busy && stage == Stage.Patterns && session?.Palette is not null && !SizeDirty && !ColorsDirty;
        SaveButton.IsEnabled = !busy && stage == Stage.Patterns && session?.PatternPreview is not null && !SizeDirty && !ColorsDirty;
        SavePaletteButton.IsEnabled = !busy && stage == Stage.Colors && session?.Palette is not null && !SizeDirty && !ColorsDirty;
        int selectedCount = ColorPaletteList.SelectedItems.Count;
        bool canEditColors = !busy && stage == Stage.Colors && !SizeDirty && !ColorsDirty && session?.Palette?.Colors.Length > 1;
        DeleteColorButton.IsEnabled = canEditColors && selectedCount > 0 && selectedCount < session!.Palette!.Colors.Length;
        MergeColorButton.IsEnabled = canEditColors && selectedCount >= 2;
        ColorSelectionHint.Text = T($"برای انتخاب رنگ‌ها، روی تصویر کادر بکشید. انتخاب‌شده: {selectedCount}. برای مرتب‌سازی سرستون را بزنید.", $"Drag on the image to select colors. Selected: {selectedCount}. Click a column header to sort.");
        UpdateBankButtons();
        UndoButton.IsEnabled = !busy && (bankOpen ? WeaveCanvas.CanUndo : undoHistory.Count > 0);
        RedoButton.IsEnabled = !busy && (bankOpen ? WeaveCanvas.CanRedo : redoHistory.Count > 0);
        ReportButton.IsEnabled = !busy && !bankOpen && session?.Palette is not null && !SizeDirty && !ColorsDirty;
        var dimensions = session?.Resized ?? session?.Source;
        StatsText.Text = dimensions is null ? "" : T($"تار {dimensions.PixelWidth:N0} · پود {dimensions.PixelHeight:N0} · {(long)dimensions.PixelWidth * dimensions.PixelHeight:N0} پیکسل · {session?.Palette?.Colors.Length ?? 0} رنگ", $"Warp {dimensions.PixelWidth:N0} · Weft {dimensions.PixelHeight:N0} · {(long)dimensions.PixelWidth * dimensions.PixelHeight:N0} px · {session?.Palette?.Colors.Length ?? 0} colors");
        PreviousPage.IsEnabled = !busy && current is not null && current.PageIndex > 0;
        NextPage.IsEnabled = !busy && current is not null && current.PageIndex + 1 < current.PageCount;
        PageValue.Text = current is null ? "—" : $"{current.PageIndex + 1} / {current.PageCount}";
        foreach (var pair in new[] { (SizeStepButton, Stage.Size), (ColorStepButton, Stage.Colors), (PatternStepButton, Stage.Patterns), (SelvedgeStepButton, Stage.Selvedge), (WeftStepButton, Stage.Weft), (FloatStepButton, Stage.Floats), (AnalysisStepButton, Stage.Analysis), (SimulationStepButton, Stage.Simulation), (MachineStepButton, Stage.Machine) })
        {
            bool active = (session is not null || stage == Stage.Machine) && pair.Item2 == stage;
            pair.Item1.Background = active ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(lightTheme ? "#D2EFE7" : "#31574F")) : (Brush)FindResource("ButtonBackground");
            pair.Item1.BorderBrush = active ? (Brush)FindResource("Accent") : (Brush)FindResource("ButtonBorder");
        }
    }
    public async void OpenLegacyProject(string filename) => await LoadProjectAsync(filename);
    private async Task LoadProjectAsync(string filename)
    {
        SetBusy(true); StatusText.Text = T("در حال باز کردن پروژه…", "Opening project…"); bool loaded = false;
        try
        {
            var state = await Task.Run(() => ProjectFile.Load(filename));
            // Only replace current work after the complete archive has been checked.
            session = state.Session; path = filename; projectPath = filename; projectCreated = state.CreatedAt; metadata = state.Metadata ?? new(); LoadMachineSettings(state.Machine ?? new MachineSetup(AbsoluteLayout: true), state.DraftWeft); LoadSimulationSettings(state.Simulation ?? new SimulationSettings()); ClearHistory(); sourceName = state.SourceName;
            current = new ImageResult(session.Source, 1, 0, "SAM");
            WidthInput.Text = state.WidthInput; HeightInput.Text = state.HeightInput; ColorInput.Text = state.ColorInput;
            english = state.English; stage = Enum.Parse<Stage>(state.Stage); machineFormatIndex = state.MachineFormatIndex;
            switch (machineFormatIndex) { case 0: FormatJC4.IsChecked = true; break; case 1: FormatJC5.IsChecked = true; break; case 2: FormatZC5.IsChecked = true; break; case 3: FormatEP.IsChecked = true; break; case 4: FormatJC6.IsChecked = true; break; case 5: FormatBonas.IsChecked = true; break; }
            RebuildPaletteEntries(state.PatternNames);
            FileValue.Text = Path.GetFileName(filename); FormatValue.Text = "SAM"; SetZoom(1); projectDirty = false; UpdateLabels(); ShowStage();
            StatusText.Text = T("پروژه باز شد؛ تصویر و پترن‌ها در پروژه قرار دارند.", "Project opened; the image and patterns are embedded."); loaded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException or OverflowException or FormatException)
        { MessageBox.Show(this, T("باز کردن پروژه ممکن نشد: ", "Could not open project: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); StatusText.Text = T("باز کردن پروژه ناموفق بود.", "Could not open project."); }
        finally { SetBusy(false); }
        if (loaded && stage == Stage.Simulation) await RenderSimulationAsync();
        else if (loaded && stage == Stage.Selvedge) await PreviewSelvedgesAsync(false);
        else if (loaded && (stage is Stage.Weft or Stage.Machine or Stage.Floats or Stage.Analysis) && MachineInput() is not null)
        { if (await ComposeMachineAsync(false) is not null && stage == Stage.Analysis) await RunAnalysisAsync(); }
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
        var state = new ProjectState(session, sourceName, WidthInput.Text, HeightInput.Text, ColorInput.Text, stage.ToString(), english, machineFormatIndex, names, projectCreated, metadata, ReadMachineSettings(), ReadWeftDraft(), ReadSimulationSettings());
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
    private void OpenNavigation_Click(object sender, RoutedEventArgs e) => Open_Click(sender, e);
    private async void SelvedgeStep_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.CanSave != true || SizeDirty || ColorsDirty) return;
        stage = Stage.Selvedge; machinePreview = null; ShowStage(); await RefreshMachineBankAsync();
        if (!busy) await PreviewSelvedgesAsync(false);
    }
    private async void WeftStep_Click(object sender, RoutedEventArgs e)
    { if (!busy && session?.CanSave == true && !SizeDirty && !ColorsDirty) { stage = Stage.Weft; ShowStage(); await ComposeMachineAsync(false); } }
    private async void SimulationStep_Click(object sender, RoutedEventArgs e)
    { if (!busy && session?.CanSave == true && !SizeDirty && !ColorsDirty) { stage = Stage.Simulation; simulationPreview = null; ShowStage(); await RenderSimulationAsync(); } }
    private async void AnalysisStep_Click(object sender, RoutedEventArgs e)
    { if (!busy && session?.CanSave == true && !SizeDirty && !ColorsDirty) { stage = Stage.Analysis; ShowStage(); if (await ComposeMachineAsync(true) is not null) await RunAnalysisAsync(); } }
    private async void FloatStep_Click(object sender, RoutedEventArgs e)
    { if (!busy && session?.CanSave == true && !SizeDirty && !ColorsDirty) { stage = Stage.Floats; ShowStage(); await ComposeMachineAsync(true); } }
    private void ShowStage()
    {
        HoverCard.Visibility = Visibility.Collapsed; HoverSwatch.Visibility = Visibility.Collapsed;
        AnalysisStripPanel.Visibility = stage == Stage.Analysis && warpAnalysis is not null && AnalysisStripImage.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        AnalysisStripImage.Visibility = AnalysisStripPanel.Visibility;
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
        SimulationPanel.Visibility = stage == Stage.Simulation ? Visibility.Visible : Visibility.Collapsed;
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
                Stage.Colors => colorHighlight ?? session.ColorPreview ?? session.Resized ?? session.Source,
                Stage.Selvedge => machinePreview ?? session.BasePatternPreview ?? session.Source,
                Stage.Weft => machinePreview ?? session.PatternPreview ?? session.Source,
                Stage.Simulation => simulationPreview ?? session.PatternPreview ?? session.Source,
                Stage.Analysis or Stage.Floats => machinePreview ?? session.PatternPreview ?? session.Source,
                Stage.Patterns => session.BasePatternPreview ?? session.ColorPreview ?? session.Source,
                Stage.Machine => (MachineRedAsBlack.IsChecked == true ? redPreview : machinePreview) ?? (MachineUseSource.IsChecked == true ? session.Source : session.PatternPreview ?? session.ColorPreview ?? session.Resized ?? session.Source),
                _ => session.Source
            };
            GridOverlay.Visibility = GridToggle.IsChecked == true && !(stage == Stage.Simulation && SimulationMode.SelectedIndex == 2 && simulationPreview is not null) ? Visibility.Visible : Visibility.Collapsed;
            PreviewImage.Source = view; GridOverlay.PixelsWide = stage == Stage.Simulation && simulationPreview is not null ? simulationGridWidth : view.PixelWidth; GridOverlay.PixelsHigh = stage == Stage.Simulation && simulationPreview is not null ? simulationGridHeight : view.PixelHeight; ImageScroll.Visibility = Visibility.Visible; EmptyState.Visibility = Visibility.Collapsed;
            Dispatcher.BeginInvoke(new Action(UpdatePixelGrid), System.Windows.Threading.DispatcherPriority.Loaded);
            PreviewDetails.Text = $"{view.PixelWidth:N0} × {view.PixelHeight:N0} px";
        }
        CanvasTitle.Text = stage == Stage.Machine ? (machinePreview is null ? T("پیش‌نمایش طرح فعلی", "Current design preview") : T("نقشه نهایی ماشین", "Final machine map")) : !hasFile ? T("پیش‌نمایش طرح", "Design preview") : stage switch
        {
            Stage.Size => T("پیش‌نمایش اندازه", "Size preview"),
            Stage.Colors => T("پیش‌نمایش رنگ‌ها", "Color preview"),
            Stage.Selvedge => T("پیش‌نمایش کناره‌ها", "Selvedge preview"),
            Stage.Weft => T("پیش‌نمایش پودگذاری", "Weft layout preview"),
            Stage.Simulation => T("نمای تقریبی پارچه", "Stylized fabric preview"),
            Stage.Analysis => composedMap is null ? T("نقشه ترکیبی آماده نیست", "Composite map unavailable") : T("تحلیل درصد تارها", "Warp percentage analysis"),
            Stage.Floats => composedMap is null ? T("نقشه ترکیبی آماده نیست", "Composite map unavailable") : T("پیش‌نمایش و اصلاح فلوت", "Float inspection and editing"),
            _ => T("پیش‌نمایش بافت", "Pattern preview")
        };
        SafetyText.Text = stage == Stage.Machine
            ? T("فایل JC5 آزمایشی است؛ قبل از تولید مستقل بررسی شود.", "JC5 is experimental; verify independently before production.")
            : T("خروجی این مرحله تصویر است، نه فایل دستگاه بافندگی.", "This stage outputs an image, not a loom file.");
        MachineDraftNotice.Visibility = session is not null && WeftHasDraft ? Visibility.Visible : Visibility.Collapsed;
        MachineDraftNotice.Text = T("پیش‌نویس پودگذاری هنوز اعمال نشده است؛ خروجی از تنظیمات قبلی استفاده می‌کند.", "The weft draft is not applied; output uses the previous settings.");
        if (stage == Stage.Weft && WeftHasDraft) MachineSetupSummary.Text = T("تغییرات پودگذاری هنوز اعمال نشده‌اند.", "Weft changes have not been applied.");
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
        if (!NumberInput.TryInt(WidthInput.Text, out int w) || !NumberInput.TryInt(HeightInput.Text, out int h) || w <= 0 || h <= 0 || (long)w * h > DesignProcessor.MaxPixels)
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
        if (!NumberInput.TryInt(ColorInput.Text, out int colors) || colors is < 1 or > 256)
        { MessageBox.Show(this, T("تعداد رنگ باید بین ۱ تا ۲۵۶ باشد.", "The color count must be between 1 and 256.")); return; }
        if (session.LockedColors.Count(x => x) > colors) { MessageBox.Show(this, T("تعداد رنگ نباید کمتر از تعداد رنگ‌های قفل‌شده باشد.", "The limit cannot be less than the number of locked colors.")); return; }
        if (confirm && !ConfirmPatternLoss()) return;
        var before = CaptureHistory(); SetBusy(true); StatusText.Text = T("در حال بررسی رنگ‌ها…", "Analyzing colors…");
        try
        {
            await Task.Run(() => session.ApplyColors(colors)); RecordHistory(before);
            RebuildPaletteEntries(); stage = Stage.Colors; ShowStage();
            StatusText.Text = session.LockedColors.Any(x => x) && session.Palette!.Colors.Length < colors
                ? T("با وجود قفل، افزایش تعداد رنگ‌های حذف‌شده را بازنمی‌گرداند؛ برای بازسازی از تصویر، قفل‌ها را باز کنید.", "With locks active, increasing the limit cannot restore removed colors; unlock all colors to rebuild from the source.")
                : T("رنگ‌ها اعمال شدند. مرحله بافت را انتخاب کنید.", "Colors applied. Continue to patterns.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(this, T("کاهش رنگ ممکن نشد: ", "Color reduction failed: ") + ex.Message); StatusText.Text = T("کاهش رنگ ناموفق بود.", "Color reduction failed."); }
        finally { SetBusy(false); }
    }
    private void RebuildPaletteEntries(string?[]? restoredNames = null, IReadOnlyList<PaletteEntry>? previous = null, int removedIndex = -1)
    {
        highlightedColor = null; colorHighlight = null;
        if (session?.Palette is not { } palette) { entries.Clear(); ColorPaletteList.ItemsSource = null; PatternCards.ItemsSource = null; return; }
        var next = new List<PaletteEntry>();
        for (int i = 0; i < palette.Colors.Length; i++)
        {
            int c = palette.Colors[i], count = palette.Counts[i];
            var entry = new PaletteEntry { ShowGrid = GridToggle.IsChecked == true, Index = i, IsLocked = session.LockedColors[i], ColorCode = $"#{c:X6}", PixelCount = count, Percent = 100d * count / palette.Indices.Length,
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
        entries = next; var view = new ListCollectionView(entries); if (colorSortProperty is not null) view.SortDescriptions.Add(new SortDescription(colorSortProperty, colorSortDirection)); ColorPaletteList.ItemsSource = view; PatternCards.ItemsSource = entries;
    }
    private async void HighlightColor_Click(object sender, RoutedEventArgs e)
    {
        if (busy || stage != Stage.Colors || SizeDirty || ColorsDirty || session?.Palette is not { } palette || sender is not Button { DataContext: PaletteEntry entry } || (uint)entry.Index >= (uint)palette.Colors.Length) return;
        if (highlightedColor == entry.Index) { highlightedColor = null; colorHighlight = null; foreach (var item in entries) item.Highlighted = false; ShowStage(); return; }
        SetBusy(true);
        try
        {
            var image = await Task.Run(() => DesignProcessor.RenderHighlight(palette, entry.Index));
            colorHighlight = image; highlightedColor = entry.Index;
            foreach (var item in entries) item.Highlighted = item.Index == highlightedColor;
            ShowStage();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException) { MessageBox.Show(this, T("نمایش رنگ ممکن نشد: ", "Could not highlight color: ") + ex.Message); }
        finally { SetBusy(false); }
    }
    private async void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (busy || stage != Stage.Colors || session?.Palette is null || SizeDirty || ColorsDirty || sender is not Button { DataContext: PaletteEntry entry }) return;
        var picked = ColorChooser.Pick(this, entry.ColorCode); if (picked is null || picked.Equals(entry.ColorCode, StringComparison.OrdinalIgnoreCase)) return;
        var c = FabricSimulation.ParseColor(picked); int rgb = (c.R << 16) | (c.G << 8) | c.B;
        if (session.Palette.Colors.Where((_, i) => i != entry.Index).Contains(rgb)) { MessageBox.Show(this, T("این رنگ قبلاً وجود دارد؛ برای ترکیب از ادغام رنگ استفاده کنید.", "This color already exists; use merge instead."), Title); return; }
        var before = CaptureHistory(); var prior = entries.ToArray(); SetBusy(true);
        try { await Task.Run(() => session.Recolor(entry.Index, rgb)); RecordHistory(before); RebuildPaletteEntries(previous: prior); ShowStage(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { MessageBox.Show(this, ex.Message, Title); }
        finally { SetBusy(false); }
    }
    private void ColorSelection_Changed(object sender, SelectionChangedEventArgs e) { if (IsInitialized) RefreshControls(); }
    private void ColorHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } column } || ColorPaletteList.ItemsSource is not ListCollectionView view) return;
        string? property = column == ColorNumberColumn ? nameof(PaletteEntry.ColorNumber) : column == ColorSwatchColumn ? nameof(PaletteEntry.ColorCode) : column == ColorCountColumn ? nameof(PaletteEntry.PixelCount) : column == ColorPercentColumn ? nameof(PaletteEntry.Percent) : column == ColorLockColumn ? nameof(PaletteEntry.IsLocked) : column == ColorShowColumn ? nameof(PaletteEntry.Highlighted) : null;
        if (property is null) return;
        colorSortDirection = colorSortProperty == property && colorSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending; colorSortProperty = property;
        view.SortDescriptions.Clear(); view.SortDescriptions.Add(new SortDescription(property, colorSortDirection));
    }
    private void ColorLock_Click(object sender, RoutedEventArgs e)
    {
        if (busy || stage != Stage.Colors || SizeDirty || ColorsDirty || session?.Palette is null || sender is not Button { DataContext: PaletteEntry entry }) return;
        var selected = ColorPaletteList.SelectedItems.OfType<PaletteEntry>().Select(x => x.Index).ToArray(); var before = CaptureHistory(); var prior = entries.ToArray();
        session.SetColorLock(entry.Index, !entry.IsLocked); RecordHistory(before); RebuildPaletteEntries(previous: prior);
        foreach (int index in selected) ColorPaletteList.SelectedItems.Add(entries[index]);
        ShowStage(); StatusText.Text = T("وضعیت قفل رنگ تغییر کرد؛ پروژه را ذخیره کنید.", "Color lock updated; save the project."); e.Handled = true;
    }
    private async void DeleteColor_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || SizeDirty || ColorsDirty) return;
        var selected = ColorPaletteList.SelectedItems.OfType<PaletteEntry>().Select(x => x.Index).ToArray();
        if (selected.Length == 0 || selected.Length >= session.Palette.Colors.Length) return;
        await RemoveColorsAsync(selected, null);
    }
    private async void MergeColor_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || SizeDirty || ColorsDirty) return;
        var selected = ColorPaletteList.SelectedItems.OfType<PaletteEntry>().OrderBy(x => x.Index).ToArray(); if (selected.Length < 2) return;
        var choice = new MergeChoiceWindow(selected, english) { Owner = this };
        if (choice.ShowDialog() != true || choice.Destination is not int target) return;
        await RemoveColorsAsync(selected.Where(x => x.Index != target).Select(x => x.Index).ToArray(), target);
    }
    private async Task RemoveColorsAsync(int[] removed, int? target)
    {
        if (session?.Palette is null || removed.Length == 0) return;
        if (removed.Any(i => session.LockedColors[i])) { MessageBox.Show(this, T("برای حذف یا ادغام، ابتدا قفل رنگ‌های حذف‌شونده را باز کنید.", "Unlock colors before removing or merging them.")); return; }
        string message = target is int t ? T($"{removed.Length} رنگ در رنگ شماره {t + 1} ادغام می‌شوند.", $"{removed.Length} colors will be merged into color {t + 1}.") : T($"{removed.Length} رنگ حذف و پیکسل‌هایشان به نزدیک‌ترین رنگ‌های باقی‌مانده منتقل می‌شوند.", $"{removed.Length} colors will be removed and mapped to the nearest remaining colors.");
        if (removed.Any(i => session.Patterns[i] is not null)) message += T(" بافت رنگ‌های حذف‌شده نیز حذف می‌شود.", " Removed colors' patterns are discarded.");
        if (MessageBox.Show(this, message + T(" ادامه می‌دهید؟", " Continue?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var prior = entries.Where(x => !removed.Contains(x.Index)).ToArray(); var before = CaptureHistory(); SetBusy(true);
        try { await Task.Run(() => session.RemoveColors(removed, target)); RecordHistory(before); RebuildPaletteEntries(previous: prior); ShowStage(); StatusText.Text = T("رنگ‌ها به‌روزرسانی شدند.", "Colors updated."); }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException) { MessageBox.Show(this, T("تغییر رنگ ممکن نشد: ", "Could not change colors: ") + ex.Message, Title); }
        finally { SetBusy(false); }
    }
    private HistoryState CaptureHistory() => new(session!.Snapshot(), stage, entries.Select(x => (string?)x.PatternLabel).ToArray(), entries.Select(x => x.PatternPath).ToArray());
    private void ClearHistory() { undoHistory.Clear(); redoHistory.Clear(); InvalidateAnalysis(); }
    private void InvalidateAnalysis() { machinePreview = null; redPreview = null; simulationPreview = null; simulationMap = null; composedMap = null; warpAnalysis = null; if (IsInitialized) { AnalysisStripImage.Source = null; AnalysisStripPanel.Visibility = Visibility.Collapsed; AnalysisRangeText.Text = ""; AnalysisHoverText.Text = ""; FloatAutoResult.Text = ""; } floatRuns = null; floatIndex = 0; if (IsInitialized) { AnalysisResultText.Text = T("برای محاسبه، دکمه بالا را بزنید.", "Run analysis to calculate."); FloatResultText.Text = T("هنوز بررسی نشده است.", "Not scanned yet."); FloatCurrentText.Text = ""; } }
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
    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (bankOpen) { WeaveCanvas.Undo(); return; }
        if (session is null || undoHistory.Count == 0) return;
        redoHistory.Add(CaptureHistory()); var state = undoHistory[^1]; undoHistory.RemoveAt(undoHistory.Count - 1); RestoreHistory(state); await RestoreHistoryPreviewAsync();
    }
    private async void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (bankOpen) { WeaveCanvas.Redo(); return; }
        if (session is null || redoHistory.Count == 0) return;
        undoHistory.Add(CaptureHistory()); var state = redoHistory[^1]; redoHistory.RemoveAt(redoHistory.Count - 1); RestoreHistory(state); await RestoreHistoryPreviewAsync();
    }
    private async Task RestoreHistoryPreviewAsync()
    {
        if (stage == Stage.Simulation) await RenderSimulationAsync();
        else if (stage == Stage.Selvedge) await PreviewSelvedgesAsync(false);
        else if (stage is Stage.Floats or Stage.Analysis or Stage.Weft or Stage.Machine)
        { if (await ComposeMachineAsync(false) is not null && stage == Stage.Analysis) await RunAnalysisAsync(); }
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (busy) return;
        if (e.Key == Key.F1) { Help_Click(sender, e); e.Handled = true; return; }
        var mods = Keyboard.Modifiers;
        if (moduleEditorKind is null && mods == ModifierKeys.Alt && (int)e.Key >= (int)Key.D1 && (int)e.Key <= (int)Key.D9 && !bankOpen)
        {
            Button[] buttons = { OpenButton, SizeStepButton, ColorStepButton, PatternStepButton, SelvedgeStepButton, FloatStepButton, AnalysisStepButton, WeftStepButton, SimulationStepButton };
            var button = buttons[(int)e.Key - (int)Key.D1]; if (button.IsEnabled) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); e.Handled = true; return;
        }
        if ((mods & ModifierKeys.Control) == 0 || (mods & ModifierKeys.Alt) != 0) return;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        if (moduleEditorKind is not null && e.Key is (Key.O or Key.S or Key.P or Key.B)) { e.Handled=true; return; }
        if (e.Key == Key.O && !bankOpen) { Open_Click(sender, e); }
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
        try { new GuideWindow(english) { Owner = this }.ShowDialog(); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException) { MessageBox.Show(this, T("راهنما باز نشد: ", "Could not open guide: ") + ex.Message, Title); }
    }
    private void EditorHelper_Click(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button { Tag: string action }) return;
        if (!NumberInput.TryInt(StepXInput.Text, out int dx) || !NumberInput.TryInt(StepYInput.Text, out int dy) || dx < 1 || dy < 1 || dx > 64 || dy > 64)
        { MessageBox.Show(this, T("گام‌های X و Y باید بین ۱ تا ۶۴ باشند.", "X and Y steps must be between 1 and 64.")); return; }
        int repeat = 5, step = 2;
        if (action == "satin" && (!NumberInput.TryInt(SatinRepeat.Text, out repeat) || !NumberInput.TryInt(SatinStep.Text, out step))) { MessageBox.Show(this, T("تکرار و گام ساتین باید عدد صحیح باشند.", "Satin repeat and step must be integers.")); return; }
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
        if (selectingColors) { UpdateColorSelection(e.GetPosition(PreviewImage)); HoverCard.Visibility = Visibility.Collapsed; return; }
        if (panning) { HoverCard.Visibility = Visibility.Collapsed; return; }
        HoverSwatch.Visibility = Visibility.Collapsed;
        if ((stage is Stage.Machine or Stage.Selvedge or Stage.Weft or Stage.Floats or Stage.Analysis) && machinePreview is { } map)
        {
            var local = e.GetPosition(PreviewImage);
            if (local.X < 0 || local.Y < 0 || local.X >= PreviewImage.ActualWidth || local.Y >= PreviewImage.ActualHeight) { HoverCard.Visibility = Visibility.Collapsed; return; }
            int hx = (int)(local.X * map.PixelWidth / PreviewImage.ActualWidth), hy = (int)(local.Y * map.PixelHeight / PreviewImage.ActualHeight);
            var pixel = new byte[1]; map.CopyPixels(new Int32Rect(hx, hy, 1, 1), pixel, 1, 0);
            if (stage == Stage.Analysis && composedMap is { } composition && warpAnalysis is { } analysis && hx >= composition.FabricStart && hx < composition.FabricStart + composition.FabricWidth)
            {
                double d = analysis.DeviationPercent[hx - composition.FabricStart];
                HoverDetails.Text = T($"تار {hx + 1} · ", $"Warp {hx + 1} · ") + WarpDisplay.Deviation(d, english); AnalysisHoverText.Text = HoverDetails.Text;
            }
            else HoverDetails.Text = T($"ستون / قلاب منطقی: {hx + 1} · رج: {hy + 1}\nبیت فایل: {(pixel[0] == 0 ? 1 : 0)}", $"Logical hook: {hx + 1} · row: {hy + 1}\nFile bit: {(pixel[0] == 0 ? 1 : 0)}");
            HoverCard.Visibility = Visibility.Visible; return;
        }
        if (session?.Palette is not { } palette || SizeDirty || ColorsDirty || stage is not (Stage.Patterns or Stage.Colors)) { HoverCard.Visibility = Visibility.Collapsed; return; }
        var point = e.GetPosition(PreviewImage); var hit = PreviewHitTest.Find(palette, point.X, point.Y, PreviewImage.ActualWidth, PreviewImage.ActualHeight);
        if (hit is null) { HoverCard.Visibility = Visibility.Collapsed; return; }
        var (x, y, index) = hit.Value; var entry = entries[index];
        HoverSwatch.Background = entry.Brush; HoverSwatch.Visibility = Visibility.Visible;
        HoverDetails.Text = T($"رنگ {entry.ColorNumber} · {entry.ColorCode}\nتعداد پیکسل: {entry.PixelCount:N0} ({entry.PercentText})\nمختصات از بالا-چپ: X={x + 1}, Y={y + 1}", $"Color {entry.ColorNumber} · {entry.ColorCode}\nPixels: {entry.PixelCount:N0} ({entry.PercentText})\nTop-left origin: X={x + 1}, Y={y + 1}") + (stage == Stage.Patterns ? T($"\nبافت: {entry.PatternLabel} · {entry.PatternSize}", $"\nWeave: {entry.PatternLabel} · {entry.PatternSize}") : "");
        HoverCard.Visibility = Visibility.Visible;
    }
    private void PreviewImage_MouseLeave(object sender, MouseEventArgs e) => HoverCard.Visibility = Visibility.Collapsed;
    private async void RunAnalysis_Click(object sender, RoutedEventArgs e) => await RunAnalysisAsync();
    private async Task RunAnalysisAsync()
    {
        if (busy || session?.CanSave != true || SizeDirty || ColorsDirty) return;
        var composition = composedMap ?? await ComposeMachineAsync(true); if (composition is null) return;
        SetBusy(true);
        try
        {
            var fabric = CompositionView.Fabric(composition);
            var result = await Task.Run(() => { var analysis = DesignAnalysis.Analyze(DesignAnalysis.ReadBinary(fabric), fabric.PixelWidth, fabric.PixelHeight); return (analysis, strip: CompositionView.HeatStrip(analysis, composition), ranges: WarpRanges.Summarize(analysis, composition.FabricStart + 1, 100, 20)); });
            warpAnalysis = result.analysis; AnalysisStripImage.Source = result.strip;
            var (loose, tight) = CompositionView.Extremes(result.analysis, 0, fabric.PixelWidth); double scale = Math.Max(loose, tight);
            var extremes = new List<string>();
            if (loose > 0) extremes.Add(T("بیشترین شل‌شدگی: ", "Maximum looseness: ") + WarpDisplay.Percentage(loose));
            if (tight > 0) extremes.Add(T("بیشترین کشیدگی: ", "Maximum tightness: ") + WarpDisplay.Percentage(tight));
            AnalysisResultText.Text = extremes.Count == 0 ? T("همه تارها متعادل‌اند.", "All warps are balanced.") : string.Join("\n", extremes);
            LegendLoose.Text = scale > 0 ? T("شل تا ", "Loose ") + WarpDisplay.Percentage(scale) : T("شل", "Loose");
            LegendNeutral.Text = T("تا ۲۰٪", "≤20%"); LegendTight.Text = scale > 0 ? T("کش تا ", "Tight ") + WarpDisplay.Percentage(scale) : T("کش", "Tight");
            string Format(IReadOnlyList<WarpRange> ranges) => string.Join(" · ", ranges.Select(r => $"{r.Start}–{r.End} ({WarpDisplay.Range(r)})"));
            var top = new List<string>(); var details = new List<string>();
            if (result.ranges.Tight.Count > 0) { string r = Format(result.ranges.Tight); top.Add(T("کشیدگی: ", "Tight: ") + Format(result.ranges.Tight.Take(3).ToArray())); details.Add(T($"بازه‌های کشیدگی ({result.ranges.Tight.Count}/{result.ranges.TightCount}): ", $"Tight ranges ({result.ranges.Tight.Count}/{result.ranges.TightCount}): ") + r); }
            if (result.ranges.Loose.Count > 0) { string r = Format(result.ranges.Loose); top.Add(T("شل‌شدگی: ", "Loose: ") + Format(result.ranges.Loose.Take(3).ToArray())); details.Add(T($"بازه‌های شل‌شدگی ({result.ranges.Loose.Count}/{result.ranges.LooseCount}): ", $"Loose ranges ({result.ranges.Loose.Count}/{result.ranges.LooseCount}): ") + r); }
            AnalysisTopRanges.Text = top.Count == 0 ? T("بازه بالاتر از ۲۰٪ وجود ندارد.", "No ranges exceed 20%.") : T("بازه‌های بالاتر از ۲۰٪: ", "Ranges above 20%: ") + string.Join("  |  ", top);
            AnalysisRangeText.Text = details.Count == 0 ? T("بازه‌ای بالاتر از ۲۰٪ وجود ندارد.", "No ranges exceed 20%.") : T("فقط بیش از ۲۰٪؛ حداکثر ۱۰۰ بازه در هر دسته. مقدار هر تار روی نوار قابل مشاهده است.\n", "Above 20% only; up to 100 ranges per group. Hover the strip for each warp.\n") + string.Join("\n", details);
            AnalysisHoverText.Text = T("برای درصد هر تار، نشانگر را روی نوار یا تصویر ببرید.", "Hover the strip or image to inspect a warp."); ShowStage();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException) { MessageBox.Show(this, ex.Message, Title); }
        finally { SetBusy(false); }
    }
    private void AnalysisStrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (composedMap is not { } map || warpAnalysis is not { } analysis || AnalysisStripImage.ActualWidth <= 0) return;
        double x = e.GetPosition(AnalysisStripImage).X; if (x < 0 || x >= AnalysisStripImage.ActualWidth) return;
        int hook = (int)(x / AnalysisStripImage.ActualWidth * map.Image.PixelWidth); int index = hook - map.FabricStart;
        if (index < 0 || index >= analysis.DeviationPercent.Length) { AnalysisHoverText.Text = T($"ستون {hook + 1}: خارج از محدوده بافت", $"Hook {hook + 1}: outside fabric"); return; }
        double d = analysis.DeviationPercent[index]; AnalysisHoverText.Text = T($"تار {hook + 1}: ", $"Warp {hook + 1}: ") + WarpDisplay.Deviation(d, english);
    }
    private void AnalysisStrip_MouseLeave(object sender, MouseEventArgs e) { if (warpAnalysis is not null) AnalysisHoverText.Text = T("برای درصد هر تار، نشانگر را روی نوار یا تصویر ببرید.", "Hover the strip or image to inspect a warp."); }
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
        if (!NumberInput.TryInt(FloatMinimum.Text, out int minimum) || minimum < 1 || minimum > DesignProcessor.MaxPixels)
        { MessageBox.Show(this, T("طول باید یک عدد مثبت باشد.", "Length must be a positive integer.")); return; }
        await ScanFloatsAsync(minimum, FloatWarp.IsChecked == true ? FloatDirection.Warp : FloatDirection.Weft, 0, true);
    }
    private async void FloatAuto_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.CanSave != true || SizeDirty || ColorsDirty || session.Palette is null) return;
        if (!NumberInput.TryInt(FloatMinimum.Text, out int minimum) || minimum < 1 || minimum > DesignProcessor.MaxPixels)
        { MessageBox.Show(this, T("طول باید یک عدد مثبت باشد.", "Length must be a positive integer.")); return; }
        var composition = composedMap ?? await ComposeMachineAsync(true); if (composition is null) return;
        var fabric = CompositionView.Fabric(composition); var direction = FloatWarp.IsChecked == true ? FloatDirection.Warp : FloatDirection.Weft; var currentSession = session;
        FloatCorrectionPlan? plan = null; SetBusy(true);
        try
        {
            plan = await Task.Run(() => { var runs = DesignAnalysis.FindFloats(DesignAnalysis.ReadBinary(fabric), fabric.PixelWidth, fabric.PixelHeight, minimum, direction).Select(r => r with { X = r.X + composition.FabricStart }); return FloatAutoCorrection.Plan(runs, composition.DesignStart, currentSession.Palette!.Width, fabric.PixelHeight, composition.SignalHooks); });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or OverflowException) { MessageBox.Show(this, ex.Message, Title); }
        finally { SetBusy(false); }
        if (plan is null) return;
        if(moduleEditorKind==ModuleKind.Floats && (moduleProtectedLeft>0 || moduleProtectedRight>0)) { var filtered=plan.Points.Where(p=>p.X>=moduleProtectedLeft && p.X<currentSession.Palette!.Width-moduleProtectedRight).ToArray(); plan=new FloatCorrectionPlan(filtered,plan.Skipped+plan.Points.Count-filtered.Length); }
        if (plan.Points.Count == 0) { FloatAutoResult.Text = T($"نقطه قابل اصلاحی پیدا نشد؛ {plan.Skipped:N0} مرکز خارج از طرح یا روی فرمان است.", $"No editable centers found; {plan.Skipped:N0} are outside the design or on signal hooks."); return; }
        string prompt = T($"مرکز {plan.Points.Count:N0} فلوت در طرح سیاه/سفید معکوس می‌شود؛ {plan.Skipped:N0} مرکز کناره یا فرمان تغییر نمی‌کند. اصلاح انجام شود؟", $"Toggle {plan.Points.Count:N0} float centers in the design; {plan.Skipped:N0} centers on selvedges or signal hooks are skipped. Apply?");
        if (MessageBox.Show(this, prompt, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var before = CaptureHistory(); bool changed = false; SetBusy(true);
        try { await Task.Run(() => currentSession.TogglePixels(plan.Points)); RecordHistory(before); changed = true; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException) { MessageBox.Show(this, ex.Message, Title); }
        finally { SetBusy(false); }
        if (!changed) return;
        if (await ComposeMachineAsync(true) is not null) await ScanFloatsAsync(minimum, direction, 0, false);
        FloatAutoResult.Text = T($"{plan.Points.Count:N0} مرکز اصلاح شد؛ {plan.Skipped:N0} مرکز خارج از طرح یا روی فرمان رد شد. نتایج دوباره بررسی شدند؛ برای فلوت‌های باقی‌مانده می‌توانید اصلاح را تکرار کنید.", $"Corrected {plan.Points.Count:N0} centers; skipped {plan.Skipped:N0} outside the design or on signal hooks. Rescanned; repeat if needed.");
        StatusText.Text = T("اصلاح خودکار انجام شد؛ با Ctrl+Z قابل بازگشت است. پروژه را ذخیره کنید.", "Automatic correction applied; undo with Ctrl+Z and save the project.");
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
        FloatCurrentText.Text = T($"مورد {floatIndex + 1:N0} از {floatRuns.Count:N0} · {(run.Black ? "سیاه" : "سفید")} · طول {run.Length:N0}\nشروع X={run.X + 1}, Y={run.Y + 1} · برای اصلاح روی نقطه کلیک کنید.", $"Run {floatIndex + 1:N0} / {floatRuns.Count:N0} · {(run.Black ? "black" : "white")} · length {run.Length:N0}\nStart X={run.X + 1}, Y={run.Y + 1} · click a pixel to toggle.");
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
        if (!busy && stage == Stage.Colors && !SizeDirty && !ColorsDirty && session?.Palette is not null)
        {
            colorSelectionStart = e.GetPosition(PreviewImage); if (!PreviewImage.CaptureMouse()) return; selectingColors = true; UpdateColorSelection(colorSelectionStart); e.Handled = true; return;
        }
        if (busy || stage != Stage.Floats || session?.CanSave != true || SizeDirty || ColorsDirty || composedMap is not { } map) return;
        Point p = e.GetPosition(PreviewImage); if (p.X < 0 || p.Y < 0 || p.X >= PreviewImage.ActualWidth || p.Y >= PreviewImage.ActualHeight) return;
        int x = (int)(p.X * map.Image.PixelWidth / PreviewImage.ActualWidth), y = (int)(p.Y * map.Image.PixelHeight / PreviewImage.ActualHeight);
        int bodyX = x - map.DesignStart;
        if (bodyX < 0 || bodyX >= session.Palette!.Width || map.SignalHooks.Contains(x) || moduleEditorKind==ModuleKind.Floats && (bodyX<moduleProtectedLeft || bodyX>=session.Palette.Width-moduleProtectedRight))
        { StatusText.Text = T("فقط نقاط خود طرح قابل ویرایش‌اند؛ کناره را از تنظیمات کناره تغییر دهید.", "Only design pixels can be edited; change selvedges in their settings."); return; }
        bool rescan = floatRuns is not null; int index = floatIndex, minimum = lastFloatMinimum; var direction = lastFloatDirection; var before = CaptureHistory(); SetBusy(true);
        try { await Task.Run(() => session.TogglePixel(bodyX, y)); RecordHistory(before); StatusText.Text = T("نقطه اصلاح شد؛ پروژه را ذخیره کنید.", "Pixel edited; save the project."); }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { MessageBox.Show(this, ex.Message, Title); rescan = false; }
        finally { SetBusy(false); }
        await ComposeMachineAsync(true); if (rescan) await ScanFloatsAsync(minimum, direction, index, false);
        e.Handled = true;
    }
    private void UpdateColorSelection(Point point)
    {
        double x1 = Math.Clamp(colorSelectionStart.X, 0, PreviewImage.ActualWidth), y1 = Math.Clamp(colorSelectionStart.Y, 0, PreviewImage.ActualHeight), x2 = Math.Clamp(point.X, 0, PreviewImage.ActualWidth), y2 = Math.Clamp(point.Y, 0, PreviewImage.ActualHeight);
        ColorSelectionRect.Margin = new Thickness(Math.Min(x1,x2), Math.Min(y1,y2), 0, 0); ColorSelectionRect.Width = Math.Max(1, Math.Abs(x2-x1)); ColorSelectionRect.Height = Math.Max(1, Math.Abs(y2-y1)); ColorSelectionRect.Visibility = Visibility.Visible;
    }
    private void PreviewImage_Up(object sender, MouseButtonEventArgs e)
    {
        if (!selectingColors || session?.Palette is not { } palette) return;
        var end = e.GetPosition(PreviewImage); selectingColors = false; ColorSelectionRect.Visibility = Visibility.Collapsed; PreviewImage.ReleaseMouseCapture(); ApplyColorRegionSelection(palette, colorSelectionStart, end, PreviewImage.ActualWidth, PreviewImage.ActualHeight); e.Handled = true;
    }
    private void ApplyColorRegionSelection(PaletteResult palette, Point start, Point end, double width, double height)
    {
        var indices = PreviewHitTest.ColorsInRectangle(palette, start.X, start.Y, end.X, end.Y, width, height);
        ColorPaletteList.SelectedItems.Clear(); foreach (int index in indices) ColorPaletteList.SelectedItems.Add(entries[index]);
    }
    private void PreviewImage_LostCapture(object sender, MouseEventArgs e) { selectingColors = false; ColorSelectionRect.Visibility = Visibility.Collapsed; }
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
    private async void ConnectedWeave_Click(object sender,RoutedEventArgs e)
    {
        if(busy || moduleEditorKind!=ModuleKind.ApplyWeaves || session?.Palette is null || (sender as Button)?.DataContext is not PaletteEntry entry) return;
        var dialog=new Window { Title="بافت‌های متصل",Width=460,Height=540,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,FlowDirection=FlowDirection.RightToLeft }; dialog.SetResourceReference(BackgroundProperty,"Surface");
        var root=new DockPanel { Margin=new Thickness(14) }; var confirm=new Button { Content="انتخاب بافت",Margin=new Thickness(4) }; DockPanel.SetDock(confirm,Dock.Bottom); root.Children.Add(confirm);
        var list=new ListBox { ItemsSource=connectedWeaves,ItemsPanel=(ItemsPanelTemplate)Application.Current.FindResource("WeaveGalleryPanel"),ItemTemplate=(DataTemplate)Application.Current.FindResource("WeaveGalleryItem"),ItemContainerStyle=(Style)Application.Current.FindResource("WeaveGalleryListItem") }; list.SetResourceReference(Control.BackgroundProperty,"ListBackground"); root.Children.Add(list); dialog.Content=root;
        confirm.Click+=(_,_)=>{if(list.SelectedItem is BankItem)dialog.DialogResult=true;}; if(dialog.ShowDialog()!=true || list.SelectedItem is not BankItem item)return;
        var before=CaptureHistory(); SetBusy(true); try { await Task.Run(()=>session.AssignPattern(entry.Index,item.Pattern)); RecordHistory(before); entry.Restore(item.Name,item.Pattern); ShowStage(); } catch(Exception ex) when(ex is ArgumentException or InvalidOperationException) { MessageBox.Show(this,ex.Message); } finally { SetBusy(false); }
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
        if (moduleEditorKind is not null) { if (busy) e.Cancel=true; return; }
        if (allowClose) return;
        e.Cancel = true; if (busy || closePromptActive) return;
        closePromptActive = true;
        try { if (await ConfirmDiscardAsync()) { allowClose = true; _ = Dispatcher.BeginInvoke(new Action(Close)); } }
        finally { closePromptActive = false; }
    }
    private async Task ReloadBankAsync(string? selectedId = null)
    {
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => { var items = bank.Load(out int skipped); return (items, skipped); });
            allBankItems = result.items; loadingBankCategories = true;
            try { BankCategory.ItemsSource = new[] { "همه / All" }.Concat(result.items.Select(x => x.Category).Distinct().OrderBy(x => x)).ToArray(); BankCategory.SelectedIndex = 0; }
            finally { loadingBankCategories = false; }
            ApplyBankCategory();
            if (selectedId is not null) BankList.SelectedItem = result.items.FirstOrDefault(x => x.Id == selectedId);
            BankStatus.Text = T($"{result.items.Count} بافت در دسترس" + (result.skipped > 0 ? $" · {result.skipped} فایل نامعتبر نادیده گرفته شد" : ""),
                $"{result.items.Count} weaves available" + (result.skipped > 0 ? $" · {result.skipped} invalid items skipped" : ""));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        { MessageBox.Show(this, T("خواندن بانک ممکن نشد: ", "Could not load weave bank: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private void ApplyBankCategory()
    {
        string? category = BankCategory.SelectedIndex > 0 ? BankCategory.SelectedItem as string : null;
        BankList.ItemsSource = category is null ? allBankItems : allBankItems.Where(x => x.Category == category).ToArray();
        UpdateBankButtons();
    }
    private void BankCategory_Changed(object sender, SelectionChangedEventArgs e) { if (IsInitialized && !loadingBankCategories) ApplyBankCategory(); }
    private void UpdateBankButtons()
    {
        if (!IsInitialized) return;
        var item = BankList.SelectedItem as BankItem;
        BankUseButton.Content = bankSelvedgeSide is null ? T("اختصاص به رنگ", "Apply to color") : T("اختصاص به کناره", "Apply to selvedge");
        BankUseButton.IsEnabled = !busy && item is not null && (bankSelvedgeSide is not null || (bankTargetIndex is int index && session?.Palette is not null && index >= 0 && index < entries.Count));
        BankEditButton.IsEnabled = Bank3DButton.IsEnabled = !busy && item is not null;
        CompositeButton.IsEnabled = !busy;
        BankDeleteButton.IsEnabled = !busy && item is not null && !item.IsPreset;
    }
    private void BankSelection_Changed(object sender, SelectionChangedEventArgs e) { if (IsInitialized) UpdateBankButtons(); }
    private void EditorReset_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (!NumberInput.TryInt(WeaveWidth.Text, out int w) || !NumberInput.TryInt(WeaveHeight.Text, out int h) || w < 1 || h < 1 || w > WeaveEditor.MaxSide || h > WeaveEditor.MaxSide || (long)w * h > DesignProcessor.MaxPatternPixels)
        { MessageBox.Show(this, T("هر بعد تا ۴۰۹۶ و مساحت حداکثر ۲۶۲۱۴۴ خانه باشد.", "Each dimension must be at most 4096, with at most 262144 cells.")); return; }
        if (!ConfirmDiscardDrawing()) return;
        WeaveCanvas.Reset(w, h);
    }
    private void BankEdit_Click(object sender, RoutedEventArgs e)
    {
        if (busy || BankList.SelectedItem is not BankItem item || !ConfirmDiscardDrawing()) return;
        if (item.Pattern.Width > WeaveEditor.MaxSide || item.Pattern.Height > WeaveEditor.MaxSide)
        { MessageBox.Show(this, T("بعد این بافت بیش از ۴۰۹۶ خانه است؛ همچنان می‌توان آن را استفاده و سه‌بعدی مشاهده کرد.", "A dimension exceeds the 4096-cell editor limit; it can still be applied or viewed in 3D.")); return; }
        WeaveCanvas.SetPattern(item.Pattern); WeaveWidth.Text = item.Pattern.Width.ToString(); WeaveHeight.Text = item.Pattern.Height.ToString(); WeaveName.Text = item.Name;
    }
    private void Editor3D_Click(object sender, RoutedEventArgs e) { if (!busy) Fabric3DWindow.ShowPattern(this, WeaveCanvas.Snapshot(), english); }
    private void Bank3D_Click(object sender, RoutedEventArgs e) { if (!busy && BankList.SelectedItem is BankItem item) Fabric3DWindow.ShowPattern(this, item.Pattern, english, item.WeftColors); }
    private async void Composite_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        try
        {
            var choices = bank.Load(out _); var window = new CompositeWeaveWindow(bank, choices, english) { Owner = this };
            if (window.ShowDialog() == true && window.SavedItem is { } item) await ReloadBankAsync(item.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(this, T("بافت ترکیبی باز نشد: ", "Could not open composite weave: ") + ex.Message, Title); }
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
        var dialog = new OpenFileDialog { Filter = "Images and PDF/AI|*.bmp;*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.ico;*.wdp;*.jxr;*.pdf;*.ai|All files|*.*", CheckFileExists = true };
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
            var draft = ReadWeftDraft(); var settings = ReadMachineSettings(); settings = side == "left" ? settings with { LeftPattern = item.Pattern, LeftName = item.Name } : settings with { RightPattern = item.Pattern, RightName = item.Name };
            LoadMachineSettings(settings, draft); MarkProjectDirty(); InvalidateAnalysis(); WeaveCanvas.DiscardChanges(); bankOpen = false; bankSelvedgeSide = null; ShowStage();
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
        var dialog = new OpenFileDialog { Filter = "Images and PDF/AI|*.bmp;*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.ico;*.wdp;*.jxr;*.pdf;*.ai|All files|*.*", CheckFileExists = true };
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
    private async void RandomWeave_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || SizeDirty || ColorsDirty) return;
        if (session.Patterns.Any(p => p is not null) && MessageBox.Show(this, T("بافت همه رنگ‌ها به‌صورت تصادفی جایگزین شود؟ امکان بازگردانی وجود دارد.", "Replace every assigned weave randomly? You can undo this action."), Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var before = CaptureHistory(); var active = session; SetBusy(true);
        try
        {
            var selected = await Task.Run(() => { var items = bank.Load(out _); if (items.Count == 0) throw new InvalidOperationException("Weave bank is empty."); return Enumerable.Range(0, active.Patterns.Length).Select(_ => items[Random.Shared.Next(items.Count)]).ToArray(); });
            await Task.Run(() => active.AssignPatterns(selected.Select(item => item.Pattern).ToArray()));
            RecordHistory(before); RebuildPaletteEntries(selected.Select(item => (string?)item.Name).ToArray()); ShowStage();
            StatusText.Text = T("برای همه رنگ‌ها بافت تصادفی از بانک انتخاب شد.", "A random bank weave was assigned to each color.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MessageBox.Show(this, T("بافت تصادفی ممکن نشد: ", "Could not assign random weaves: ") + ex.Message, Title); }
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
        int locked = session.LockedColors.Count(x => x);
        ColorSummary.Text = T($"رنگ‌های ورودی: {palette.SourceColorCount:N0}  |  خروجی: {palette.Colors.Length}  |  قفل: {locked}", $"Input colors: {palette.SourceColorCount:N0}  |  Output: {palette.Colors.Length}  |  Locked: {locked}");
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
            Stage.Patterns when !SizeDirty && !ColorsDirty => session.BasePatternPreview,
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
    private async void SavePalette_Click(object sender, RoutedEventArgs e)
    {
        if (busy || stage != Stage.Colors || session?.Palette is null || SizeDirty || ColorsDirty) return;
        var dialog = new SaveFileDialog { Filter = "PNG (*.png)|*.png", AddExtension = true, OverwritePrompt = true, FileName = "palette.png" }; if (dialog.ShowDialog(this) != true) return;
        var protectedPaths = new[] { path, projectPath }.OfType<string>().Concat(entries.Select(x => x.PatternPath).OfType<string>()).ToArray(); SetBusy(true);
        try { var image = PaletteImage.Render(ColorPaletteList.Items.OfType<PaletteEntry>().ToArray(), english); await Task.Run(() => PaletteImage.Save(image, dialog.FileName, protectedPaths)); StatusText.Text = T("جدول پالت رنگ ذخیره شد.", "Palette table saved."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException) { MessageBox.Show(this, T("ذخیره پالت ممکن نشد: ", "Could not save palette: ") + ex.Message); }
        finally { SetBusy(false); }
    }
    private MachineSetup ReadMachineFields()
    {
        var left = LeftSelvedgePattern.SelectedItem as BankItem; var right = RightSelvedgePattern.SelectedItem as BankItem;
        return new MachineSetup(LeftSelvedgeWidth.Text, RightSelvedgeWidth.Text, left?.Pattern, right?.Pattern, left?.Name ?? "", right?.Name ?? "", (absoluteLayout ? !string.IsNullOrWhiteSpace(WeftCountInput.Text) && (!NumberInput.TryInt(WeftCountInput.Text, out int count) || count != 0) : BoxEnabled.IsChecked == true), Math.Max(0, BoxPlacement.SelectedIndex), BoxPlacement.SelectedIndex == 2 ? PositionNumbers.ListToStored(BoxHooks.Text) : BoxHooks.Text, BoxCodes.Text, BoxSequence.Text, BoxBottom.IsChecked == true, MachineUseSource.IsChecked == true, MachineRedAsBlack.IsChecked == true, absoluteLayout, TotalHooksInput.Text, PositionNumbers.ToStored(DesignStartInput.Text), PositionNumbers.ToStored(WeftStartInput.Text), WeftCountInput.Text);
    }
    private WeftSettings ReadWeftDraft() => WeftSettings.FromMachine(ReadMachineFields());
    private MachineSetup ReadMachineSettings() => appliedWeft.ApplyTo(ReadMachineFields());
    private bool WeftHasDraft => ReadWeftDraft() != appliedWeft;
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
    private void LoadMachineSettings(MachineSetup setup, WeftSettings? draft = null)
    {
        loadingMachineSettings = true;
        try
        {
            appliedWeft = WeftSettings.FromMachine(setup); setup = (draft ?? appliedWeft).ApplyTo(setup);
            absoluteLayout = setup.AbsoluteLayout; BoxEnabled.Visibility = absoluteLayout ? Visibility.Collapsed : Visibility.Visible; AbsoluteFields.Visibility = absoluteLayout ? Visibility.Visible : Visibility.Collapsed; LegacySetup.Visibility = absoluteLayout ? Visibility.Collapsed : Visibility.Visible;
            TotalHooksInput.Text = setup.TotalHooks; DesignStartInput.Text = PositionNumbers.ToDisplay(setup.DesignStart); WeftStartInput.Text = PositionNumbers.ToDisplay(setup.WeftStart); WeftCountInput.Text = setup.WeftCount;
            LeftSelvedgeWidth.Text = setup.LeftWidth; RightSelvedgeWidth.Text = setup.RightWidth; BoxEnabled.IsChecked = setup.BoxEnabled;
            BoxPlacement.SelectedIndex = setup.BoxPlacement; BoxHooks.Text = setup.BoxPlacement == 2 ? PositionNumbers.ListToDisplay(setup.BoxHooks) : setup.BoxHooks; BoxCodes.Text = setup.BoxCodes; BoxSequence.Text = setup.Sequence; BoxBottom.IsChecked = setup.StartFromBottom; MachineUseSource.IsChecked = setup.UseSource; MachineRedAsBlack.IsChecked = setup.RedAsBlack;
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
    private void MachineSettings_TextChanged(object sender, TextChangedEventArgs e) => MachineSettingsChanged(sender);
    private void MachineSettings_SelectionChanged(object sender, SelectionChangedEventArgs e) => MachineSettingsChanged(sender);
    private void MachineSettings_CheckChanged(object sender, RoutedEventArgs e) => MachineSettingsChanged(sender);
    private void MachineSettingsChanged(object? sender = null)
    {
        if (!IsInitialized || loadingMachineSettings) return;
        MarkProjectDirty();
        bool weftField = sender == TotalHooksInput || sender == DesignStartInput || sender == WeftCountInput || sender == WeftStartInput || sender == BoxEnabled || sender == BoxPlacement || sender == BoxHooks || sender == BoxCodes || sender == BoxSequence || sender == BoxBottom;
        if (weftField)
        {
            MachineSetupSummary.Text = WeftHasDraft ? T("تغییرات پودگذاری هنوز اعمال نشده‌اند.", "Weft changes have not been applied.") : T("تنظیمات پودگذاری اعمال‌شده است.", "Weft settings are applied.");
            return;
        }
        InvalidateAnalysis(); MachineSetupSummary.Text = T("تنظیمات تغییر کرد؛ پیش‌نمایش را دوباره بسازید.", "Settings changed; regenerate the preview.");
        if ((stage is Stage.Machine or Stage.Weft or Stage.Selvedge) && !busy) ShowStage();
        if ((stage is Stage.Selvedge or Stage.Weft or Stage.Machine) && selvedgeTimer is not null) { selvedgeTimer.Stop(); selvedgeTimer.Start(); }
    }
    private async void ApplyWeft_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.PatternPreview is not { } frame || SizeDirty || ColorsDirty) return;
        var draft = ReadWeftDraft(); var settings = draft.ApplyTo(ReadMachineSettings());
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => MachineComposer.Compose(frame, settings, false));
            appliedWeft = draft; BuildSimulationWeftRows(); MarkProjectDirty(); InvalidateAnalysis(); composedMap = result; machinePreview = result.Image;
            MachineSetupSummary.Text = T($"پودگذاری اعمال شد؛ عرض نهایی {result.Image.PixelWidth:N0} تار.", $"Weft settings applied; final width {result.Image.PixelWidth:N0} hooks."); ShowStage();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { MachineSetupSummary.Text = T("اعمال نشد؛ تنظیمات پیشین حفظ شد: ", "Not applied; previous settings retained: ") + ex.Message; MessageBox.Show(this, MachineSetupSummary.Text, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private BitmapSource? MachineInput() => session is null ? null : stage == Stage.Machine && MachineUseSource.IsChecked == true ? session.Source : session.CanSave && !SizeDirty && !ColorsDirty ? (stage == Stage.Selvedge ? session.BasePatternPreview : session.PatternPreview) : null;
    private async Task<MachineComposition?> ComposeMachineAsync(bool showErrors = true)
    {
        var frame = MachineInput(); if (frame is null) return null;
        var settings = ReadMachineSettings(); bool red = MachineRedAsBlack.IsChecked == true;
        // Weft layout is downstream of float inspection and analysis; never leak it backwards.
        if (stage is Stage.Floats or Stage.Analysis) settings = MachineComposer.BeforeWeft(settings);
        SetBusy(true);
        try
        {
            var result = await Task.Run(() => MachineComposer.Compose(frame, settings, red));
            composedMap = result; machinePreview = result.Image; redPreview = stage == Stage.Machine && MachineRedAsBlack.IsChecked == true ? await Task.Run(() => CompositionView.RedPreview(result.Image)) : null;
            string signals = result.SignalHooks.Length == 0 ? T("غیرفعال", "off") : string.Join(", ", result.SignalHooks.Select(h => h + 1));
            MachineSetupSummary.Text = T($"نقشه نهایی: {result.Image.PixelWidth:N0} قلاب × {result.Image.PixelHeight:N0} رج · قلاب‌های فرمان: {signals}", $"Final map: {result.Image.PixelWidth:N0} hooks × {result.Image.PixelHeight:N0} picks · signal hooks: {signals}");
            ShowStage(); return result;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { composedMap = null; machinePreview = null; redPreview = null; MachineSetupSummary.Text = T("تنظیمات معتبر نیست: ", "Invalid setup: ") + ex.Message; StatusText.Text = MachineSetupSummary.Text; if (showErrors) MessageBox.Show(this, MachineSetupSummary.Text, Title, MessageBoxButton.OK, MessageBoxImage.Warning); ShowStage(); return null; }
        finally { SetBusy(false); }
    }
    private SimulationSettings ReadSimulationSettings() => new(SimulationWarpYarn.SelectedIndex, defaultWeftYarn, warpSwatch.Value, defaultWeftColor, SimulationWeave.SelectedIndex, (int)SimulationShadow.Value, FabricSimulation.WeftIds(appliedWeft).Select(id => simulationYarns.TryGetValue(id, out var value) ? value : new SimulatedYarn(id, defaultWeftYarn, defaultWeftColor)).ToArray(), SimulationMode.SelectedIndex, SimulationWarpCount.Text, SimulationWarpUnit.SelectedIndex, SimulationWarpDensity.Text, SimulationWeftDensity.Text, SimulationMaterialDensity.Text);
    private void LoadSimulationSettings(SimulationSettings settings)
    {
        loadingSimulationSettings = true;
        try
        {
            defaultWeftYarn = settings.WeftYarn; defaultWeftColor = settings.WeftColor; simulationYarns.Clear();
            foreach (var yarn in settings.Wefts ?? Array.Empty<SimulatedYarn>()) simulationYarns[yarn.Id] = yarn;
            SimulationWarpYarn.SelectedIndex = settings.WarpYarn; warpSwatch.Value = settings.WarpColor; SimulationWarpCount.Text = settings.WarpCount; SimulationWarpUnit.SelectedIndex = settings.WarpUnit; SimulationWarpDensity.Text = settings.WarpDensity; SimulationWeftDensity.Text = settings.WeftDensity; SimulationMaterialDensity.Text = settings.MaterialDensity; SimulationWeave.SelectedIndex = settings.Weave; SimulationMode.SelectedIndex = settings.Mode; SimulationShadow.Value = settings.Shadow; simulationPreview = null;
            BuildSimulationWeftRows();
        }
        finally { loadingSimulationSettings = false; }
    }
    private void BuildSimulationWeftRows()
    {
        if (SimulationWeftRows is null) return;
        var ids = FabricSimulation.WeftIds(appliedWeft);
        // Retain previously defined yarns when possible, while bounding archive size.
        foreach (string id in ids) if (!simulationYarns.ContainsKey(id)) simulationYarns[id] = new SimulatedYarn(id, defaultWeftYarn, defaultWeftColor);
        foreach (string old in simulationYarns.Keys.Where(id => !ids.Contains(id)).ToArray()) { if (simulationYarns.Count <= 256) break; simulationYarns.Remove(old); }
        bool wasLoading = loadingSimulationSettings; loadingSimulationSettings = true;
        try
        {
            SimulationWeftRows.Children.Clear(); weftGeometryLabels.Clear();
            foreach (string id in ids)
            {
                var value = simulationYarns[id];
                var box = new StackPanel { Margin = new Thickness(0, 3, 0, 9) };
                box.Children.Add(new TextBlock { Text = T($"پود {id}", $"Weft {id}"), FontWeight = FontWeights.SemiBold });
                var yarn = new ComboBox { ItemsSource = FabricSimulation.YarnNames, SelectedIndex = value.Yarn, Margin = new Thickness(0, 3, 0, 3) };
                var color = new ColorSwatch(value.Color, T($"رنگ پود {id}", $"Weft {id} color"));
                var count = new TextBox { Text = value.Count, FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(6) };
                var unit = new ComboBox { ItemsSource = YarnGeometry.Units, SelectedIndex = value.Unit, Width = 110 }; var countLine = new DockPanel { FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(0, 4, 0, 3) }; DockPanel.SetDock(unit, Dock.Right); countLine.Children.Add(unit); countLine.Children.Add(count);
                void Changed() { if (loadingSimulationSettings) return; simulationYarns[id] = new SimulatedYarn(id, yarn.SelectedIndex, color.Value, count.Text, unit.SelectedIndex); SimulationSettingsChanged(); }
                yarn.SelectionChanged += (_, _) => Changed(); color.ColorChanged += (_, _) => Changed(); count.TextChanged += (_, _) => Changed(); unit.SelectionChanged += (_, _) => Changed();
                box.Children.Add(yarn); box.Children.Add(color); box.Children.Add(new TextBlock { Text = T("نمره نخ پود", "Weft yarn count") }); box.Children.Add(countLine); var geometry = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11 }; geometry.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); box.Children.Add(geometry); weftGeometryLabels[id] = geometry; SimulationWeftRows.Children.Add(box);
            }
        }
        finally { loadingSimulationSettings = wasLoading; }
        UpdateSimulationGeometry();
    }
    private void UpdateSimulationGeometry()
    {
        string Format(double diameter, double spacing) => T($"قطر تخمینی {diameter:F3}، فاصله مراکز {spacing:F3}، فاصله خالی {Math.Max(0, spacing - diameter):F3} mm", $"Estimated diameter {diameter:F3}, center spacing {spacing:F3}, gap {Math.Max(0, spacing - diameter):F3} mm") + (diameter > spacing ? T(" · هم‌پوشانی", " · overlap") : "");
        try { double rho = FabricSimulation.Number(SimulationMaterialDensity.Text); double diameter = YarnGeometry.DiameterMm(YarnGeometry.ToTex(FabricSimulation.Number(SimulationWarpCount.Text), SimulationWarpUnit.SelectedIndex), rho); SimulationWarpGeometry.Text = Format(diameter, YarnGeometry.SpacingMm(FabricSimulation.Number(SimulationWarpDensity.Text))); }
        catch (ArgumentException) { SimulationWarpGeometry.Text = T("اعداد معتبر وارد کنید.", "Enter valid values."); }
        foreach (var (id, label) in weftGeometryLabels)
        {
            try { var yarn = simulationYarns[id]; double diameter = YarnGeometry.DiameterMm(YarnGeometry.ToTex(FabricSimulation.Number(yarn.Count), yarn.Unit), FabricSimulation.Number(SimulationMaterialDensity.Text)); label.Text = Format(diameter, YarnGeometry.SpacingMm(FabricSimulation.Number(SimulationWeftDensity.Text))); }
            catch (ArgumentException) { label.Text = T("اعداد معتبر وارد کنید.", "Enter valid values."); }
        }
    }
    private void SimulationSettingsChanged()
    {
        if (!IsInitialized || loadingSimulationSettings) return;
        UpdateSimulationGeometry(); simulationPreview = null; simulationMap = null; MarkProjectDirty();
        SimulationStatus.Text = T("برای مشاهده تغییرات، پیش‌نمایش را دوباره بسازید.", "Render again to see your changes.");
        if (stage == Stage.Simulation) ShowStage();
    }
    private void SimulationChanged(object sender, SelectionChangedEventArgs e) => SimulationSettingsChanged();
    private void SimulationTextChanged(object sender, TextChangedEventArgs e) => SimulationSettingsChanged();
    private void SimulationShadowChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => SimulationSettingsChanged();
    private async void SimulationRender_Click(object sender, RoutedEventArgs e) => await RenderSimulationAsync();
    private async Task RenderSimulationAsync()
    {
        if (busy || stage != Stage.Simulation || session?.CanSave != true) return;
        var settings = ReadSimulationSettings();
        try { FabricSimulation.Validate(settings); }
        catch (ArgumentException ex) { SimulationStatus.Text = ex.Message; return; }
        simulationPreview = null; simulationMap = null; ShowStage();
        var map = await ComposeMachineAsync(true); if (map is null) return;
        SetBusy(true);
        try
        {
            var fabric = CompositionView.Fabric(map);
            if (settings.Mode == 2)
            {
                var overview = await Task.Run(() => FabricOverview.Create(fabric, map.RowWefts, settings.Weave));
                var detail = new Fabric3DWindow(overview.Image, overview.RowWefts, settings with { Weave = 0 }, english, overview: overview) { Owner = this };
                if (detail.ShowDialog() != true || detail.Captured is null) { SimulationStatus.Text = T("برای پیش‌نمایش و گزارش، نمای سه‌بعدی را ثبت کنید.", "Capture the 3D view for preview and report."); return; }
                if (detail.CapturedSettings is { } edited) { LoadSimulationSettings(edited with { Weave = settings.Weave, Mode = 2 }); MarkProjectDirty(); }
                simulationPreview = detail.Captured; simulationGridWidth = detail.Captured.PixelWidth; simulationGridHeight = detail.Captured.PixelHeight;
            }
            else { simulationPreview = await Task.Run(() => FabricSimulation.Render(fabric, settings, map.RowWefts)); simulationGridWidth = fabric.PixelWidth; simulationGridHeight = fabric.PixelHeight; }
            simulationMap = map;
            double wd = FabricSimulation.WarpDiameter(settings), dx = YarnGeometry.SpacingMm(FabricSimulation.Number(settings.WarpDensity)), dy = YarnGeometry.SpacingMm(FabricSimulation.Number(settings.WeftDensity));
            bool overlap = wd > dx || FabricSimulation.WeftIds(appliedWeft).Any(id => FabricSimulation.WeftDiameter(settings, FabricSimulation.YarnFor(settings, id)) > dy);
            SimulationStatus.Text = T($"قطر تخمینی تار {wd:F3} و فاصله مراکز تار {dx:F3}، پود {dy:F3} میلی‌متر. ", $"Estimated warp diameter {wd:F3} mm; center spacing warp {dx:F3}, weft {dy:F3} mm. ") + (overlap ? T("هشدار: قطر از فاصله بیشتر است؛ پهنا در نمایش محدود شد. ", "Warning: diameter exceeds spacing; rendered width is capped. ") : "") + T("نمای تقریبی؛ در طرح بزرگ جزئیات نمونه‌برداری می‌شود.", "Approximate view; large designs are sampled."); ShowStage();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or OverflowException) { SimulationStatus.Text = ex.Message; }
        finally { SetBusy(false); }
    }
    private async void SimulationSave_Click(object sender, RoutedEventArgs e)
    {
        if (busy || simulationPreview is not { } frame || path is null) return;
        var dialog = new SaveFileDialog { Filter = "Bitmap (*.bmp)|*.bmp|TIFF (*.tif)|*.tif", AddExtension = true, OverwritePrompt = true, FileName = "fabric-preview.bmp" };
        if (dialog.ShowDialog(this) != true) return;
        var protectedPaths = new[] { path }.Concat(entries.Select(x => x.PatternPath).OfType<string>()).ToArray();
        SetBusy(true);
        try { await Task.Run(() => ImageExporter.Save(frame, dialog.FileName, dialog.FilterIndex == 2, protectedPaths)); SimulationStatus.Text = T("تصویر شبیه‌سازی ذخیره شد؛ فایل ماشین نیست.", "Preview saved; this is not a loom file."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { MessageBox.Show(this, ex.Message, Title); }
        finally { SetBusy(false); }
    }
    private void UpgradeLayout_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, T("تغییر به چیدمان جدید، کدهای قدیمی باکس را غیرفعال می‌کند. ادامه می‌دهید؟", "Switching to the new layout disables legacy box codes. Continue?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        absoluteLayout = true; BoxEnabled.IsChecked = false; BoxEnabled.Visibility = Visibility.Collapsed; BoxSequence.Text = ""; AbsoluteFields.Visibility = Visibility.Visible; LegacySetup.Visibility = Visibility.Collapsed; MarkProjectDirty(); MachineSetupSummary.Text = T("چیدمان جدید پیش‌نویس است؛ برای تغییر خروجی اعمال کنید.", "New layout is a draft; apply it to change the output.");
    }
    private async Task PreviewSelvedgesAsync(bool showErrors)
    {
        var frame = MachineInput(); if (busy || frame is null) return;
        var setup = MachineComposer.BeforeWeft(ReadMachineSettings()); bool red = MachineRedAsBlack.IsChecked == true;
        SetBusy(true);
        try { var result = await Task.Run(() => MachineComposer.Compose(frame, setup, red)); composedMap = null; machinePreview = result.Image; redPreview = null; SelvedgeSummary.Text = T($"عرض طرح همراه کناره‌ها: {result.Image.PixelWidth:N0} تار", $"Design with selvedges: {result.Image.PixelWidth:N0} hooks"); ShowStage(); }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException) { SelvedgeSummary.Text = T("عرض یا بافت کناره معتبر نیست.", "Invalid selvedge width or weave."); if (showErrors) MessageBox.Show(this, SelvedgeSummary.Text + "\n" + ex.Message, Title); }
        finally { SetBusy(false); }
    }
    private async void SelvedgePreview_Click(object sender, RoutedEventArgs e) => await PreviewSelvedgesAsync(true);
    private async void MachinePreview_Click(object sender, RoutedEventArgs e) { if (!busy) await ComposeMachineAsync(); }
    private async void MachineExport_Click(object sender, RoutedEventArgs e)
    {
        if (busy || machineFormatIndex is not (1 or 4 or 5) || session is null || path is null) return;
        var composed = await ComposeMachineAsync(); if (composed is null) return;
        string format = machineFormatIndex == 4 ? "JC6" : machineFormatIndex == 5 ? "Bonas JC5" : "JC5";
        bool overwriteColumns = !appliedWeft.AbsoluteLayout && appliedWeft.BoxEnabled && appliedWeft.BoxPlacement == 2;
        string warning = T($"خروجی {format} آزمایشی و تک‌ناحیه‌ای است: {composed.Image.PixelWidth} قلاب و {composed.Image.PixelHeight} رج. بیت ۱ سیاه است. " + (overwriteColumns ? "قلاب‌های فرمان محتوای ستون‌های موجود را جایگزین می‌کنند. " : "") + "توالی و نگاشت منطقی را در پیش‌نمایش و اتصال و قطبیت قلاب‌های واقعی را با متخصص دستگاه بررسی کنید. ادامه می‌دهید؟",
            $"Experimental single-area {format}: {composed.Image.PixelWidth} hooks and {composed.Image.PixelHeight} picks. Bit 1 is black. " + (overwriteColumns ? "Signal hooks replace existing columns. " : "") + "Verify the preview, physical wiring and signal polarity with the machine specialist. Continue?");
        if (WeftHasDraft) warning = T("پیش‌نویس پودگذاری اعمال نشده؛ این خروجی از تنظیمات قبلی استفاده می‌کند.\n\n", "The weft draft has not been applied; this export uses previous settings.\n\n") + warning;
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
        await LoadMachineAsync(dialog.FileName);
    }
    private async Task LoadMachineAsync(string filename)
    {
        SetBusy(true);
        try
        {
            bool legacy = MachineLegacyImport.IsChecked == true;
            var image = await Task.Run(() => Jc5Codec.Load(filename, legacy));
            var next = new WorkflowSession(image); next.ApplySize(image.PixelWidth, image.PixelHeight);
            session = next; path = filename; projectPath = null; projectCreated = DateTimeOffset.Now; metadata = new(); LoadMachineSettings(new MachineSetup(AbsoluteLayout: true)); LoadSimulationSettings(new SimulationSettings()); ClearHistory(); sourceName = Path.GetFileName(path);
            current = new ImageResult(image, 1, 0, "JC5 (experimental)"); RebuildPaletteEntries();
            WidthInput.Text = image.PixelWidth.ToString(); HeightInput.Text = image.PixelHeight.ToString(); ColorInput.Text = "256";
            FileValue.Text = sourceName; FormatValue.Text = "JC5 · experimental"; stage = Stage.Size; SetZoom(1); projectDirty = true; ShowStage();
            StatusText.Text = T("JC5 باز شد؛ از مرحله اندازه می‌توانید نقشه را BMP یا TIFF ذخیره کنید. داده‌های غیرتصویری و بیت‌های اضافی حفظ نمی‌شوند.", "JC5 opened; save the hook map as BMP or TIFF from the size stage. Non-image data and padding bits are not preserved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException or OverflowException)
        { MessageBox.Show(this, T("خواندن JC5 ممکن نشد: ", "Could not read JC5: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
    }
    private void ImageScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || PreviewImage.Source is null || e.Delta == 0) return;
        Point imagePoint = e.GetPosition(PreviewContent);
        var presenter = ImageScroll.Template.FindName("PART_ScrollContentPresenter", ImageScroll) as UIElement ?? ImageScroll;
        Point viewportPoint = e.GetPosition(presenter);
        double next = Math.Clamp(zoom * Math.Pow(1.25, e.Delta / 120d), 0.125, 8);
        SetZoom(next); ImageScroll.UpdateLayout();
        ImageScroll.ScrollToHorizontalOffset(PreviewViewport.ZoomOffset(imagePoint.X, viewportPoint.X, zoom));
        ImageScroll.ScrollToVerticalOffset(PreviewViewport.ZoomOffset(imagePoint.Y, viewportPoint.Y, zoom)); e.Handled = true;
    }
    private void ImageScroll_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle || PreviewImage.Source is null) return;
        panStart = e.GetPosition(ImageScroll); panHorizontal = ImageScroll.HorizontalOffset; panVertical = ImageScroll.VerticalOffset;
        if (!ImageScroll.CaptureMouse()) return; panning = true; ImageScroll.Cursor = Cursors.ScrollAll; HoverCard.Visibility = Visibility.Collapsed; e.Handled = true;
    }
    private void ImageScroll_MouseMove(object sender, MouseEventArgs e)
    {
        if (!panning) return; Point p = e.GetPosition(ImageScroll);
        ImageScroll.ScrollToHorizontalOffset(PreviewViewport.PanOffset(panHorizontal, panStart.X, p.X));
        ImageScroll.ScrollToVerticalOffset(PreviewViewport.PanOffset(panVertical, panStart.Y, p.Y)); e.Handled = true;
    }
    private void ImageScroll_MouseUp(object sender, MouseButtonEventArgs e)
    { if (panning && e.ChangedButton == MouseButton.Middle) { ImageScroll.ReleaseMouseCapture(); e.Handled = true; } }
    private void ImageScroll_LostMouseCapture(object sender, MouseEventArgs e) { panning = false; ImageScroll.ClearValue(CursorProperty); }
    private void SetZoom(double value) { zoom = Math.Clamp(value, 0.125, 8); PreviewContent.LayoutTransform = new ScaleTransform(zoom, zoom); ZoomText.Text = $"{zoom * 100:0}%"; Dispatcher.BeginInvoke(new Action(UpdatePixelGrid), System.Windows.Threading.DispatcherPriority.Loaded); }
    private void GridToggle_Changed(object sender, RoutedEventArgs e) { if (!IsInitialized) return; foreach (var entry in entries) entry.ShowGrid = GridToggle.IsChecked == true; GridOverlay.Visibility = GridToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; GridStepText.Text = ""; UpdatePixelGrid(); }
    private void ImageScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) { if (IsInitialized) UpdatePixelGrid(); }
    private void UpdatePixelGrid()
    {
        if (!IsInitialized || GridOverlay.Visibility != Visibility.Visible || PreviewImage.Source is null || zoom <= 0 || !PreviewImage.IsVisible) return;
        var presenter = ImageScroll.Template.FindName("PART_ScrollContentPresenter", ImageScroll) as System.Windows.Controls.ScrollContentPresenter;
        if (presenter is null || presenter.ActualWidth <= 0 || presenter.ActualHeight <= 0) return;
        var inverse = PreviewImage.TransformToVisual(presenter).Inverse;
        if (inverse is null) return;
        GridOverlay.Zoom = zoom; GridOverlay.VisibleImage = inverse.TransformBounds(new Rect(0, 0, presenter.ActualWidth, presenter.ActualHeight));
        int step = GridOverlay.GridStep(); GridStepText.Text = step == 1 ? T("هر خانه", "each cell") : T($"هر {step} خانه", $"every {step} cells");
        GridOverlay.InvalidateVisual();
    }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(zoom * 2);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(zoom / 2);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) => SetZoom(1);
    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        if (busy || session?.Palette is null || SizeDirty || ColorsDirty) return;
        if (WeftHasDraft && MessageBox.Show(this, T("پیش‌نویس پودگذاری اعمال نشده است. گزارش با تنظیمات اعمال‌شده قبلی ساخته شود؟", "The weft draft is not applied. Generate the report using the previous applied settings?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var currentSession = session; var setup = ReadMachineSettings();
        SetBusy(true); var reportPages = new List<BitmapSource>();
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
            for (int palettePage = 0; palettePage < (entries.Count + 49) / 50; palettePage++) { var page = DesignReport.CreatePage(currentSession, entries, name, projectCreated, english, metadata, setup, reportAnalysis, reportMap, palettePage); reportPages.Add(DesignReport.Render(page)); }
            if (currentSession.CanSave && currentSession.PatternPreview is { } woven)
            {
                var simSettings = ReadSimulationSettings(); FabricSimulation.Validate(simSettings);
                var simMap = await Task.Run(() => MachineComposer.Compose(woven, setup, false)); var fabric = CompositionView.Fabric(simMap);
                var normalSettings = simSettings with { Mode = 0, Weave = 0 };
                var simulated = await Task.Run(() => FabricSimulation.Render(fabric, normalSettings, simMap.RowWefts));
                foreach (var simPage in DesignReport.CreateSimulationPages(simulated, normalSettings, FabricSimulation.WeftIds(appliedWeft), english)) reportPages.Add(DesignReport.Render(simPage));
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { reportPages.Clear(); MessageBox.Show(this, T("ساخت گزارش ممکن نشد: ", "Could not prepare report: ") + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { SetBusy(false); }
        if (reportPages.Count > 0)
        {
            string name = projectPath is null ? T("پروژه جدید", "New project") : Path.GetFileNameWithoutExtension(projectPath);
            var protectedPaths = new[] { path, projectPath }.OfType<string>().Concat(entries.Select(x => x.PatternPath).OfType<string>());
            new ReportWindow(reportPages, name, protectedPaths, english) { Owner = this }.ShowDialog();
        }
    }
    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow(english) { Owner = this }.ShowDialog();
    private void Language_Click(object sender, RoutedEventArgs e) { english = !english; MarkProjectDirty(); UpdateLabels(); ShowStage(); }
    private void Theme_Click(object sender, RoutedEventArgs e) { lightTheme = !ThemeManager.IsLight; ThemeManager.Apply(lightTheme, true); RefreshControls(); }
    private void UpdateLabels()
    {
        FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        SettingsBorder.FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft;
        LanguageButton.Content = english ? "FA" : "EN"; LanguageButton.ToolTip = T("تغییر زبان", "Change language"); AboutButton.ToolTip = T("درباره", "About"); HelpButton.ToolTip = T("راهنمای برنامه (F1)", "Help (F1)"); SaveProjectButton.ToolTip = T("ذخیره پروژه", "Save project"); SaveProjectAsButton.ToolTip = T("ذخیره با نام دیگر", "Save project as"); ThemeButton.ToolTip = T("تغییر تم", "Change theme"); BankButton.ToolTip = T("بانک و طراحی بافت", "Weave bank and editor"); UndoButton.ToolTip = T("بازگشت (Ctrl+Z)", "Undo (Ctrl+Z)"); RedoButton.ToolTip = T("بازانجام (Ctrl+Y)", "Redo (Ctrl+Y)"); ReportButton.ToolTip = T("گزارش PDF و چاپ", "PDF report and print"); InfoButton.Content = T("اطلاعات طرح", "Design info"); InfoButton.ToolTip = T("طراح، کاربرد و توضیحات", "Designer, use and description");
        SimulationStepButton.ToolTip = T("شبیه‌سازی پارچه", "Fabric simulation");
        SimulationWeftsLabel.Text = T("نخ و رنگ هر پود اعمال‌شده", "Yarn and color for each applied weft");
        SimulationModeLabel.Text = T("حالت نمایش", "Appearance"); SimulationTitle.Text = T("شبیه‌سازی پارچه", "Fabric simulation"); SimulationHint.Text = T("نمای تصویری تقریبی؛ رفتار واقعی پارچه نیست و بر خروجی ماشین اثر ندارد.", "Stylized preview, not a physical simulation; it does not change loom output."); SimulationRenderButton.Content = T("ساخت پیش‌نمایش", "Render preview"); SimulationSaveButton.Content = T("ذخیره تصویر شبیه‌سازی", "Save preview image");
        GridToggle.Content = T("شبکه", "Grid"); GridToggle.ToolTip = T("در زوم کم، خطوط اصلی در فاصله نشان‌داده‌شده رسم می‌شوند؛ با زوم بیشتر هر پیکسل را ببینید.", "At low zoom major lines are shown at the displayed interval; zoom in for individual pixels.");
        OpenButton.ToolTip = T("باز کردن پروژه یا فایل", "Open project or image"); SizeStepButton.ToolTip = T("تنظیم اندازه", "Resize"); ColorStepButton.ToolTip = T("مدیریت رنگ", "Color management"); PatternStepButton.ToolTip = T("بافت‌ریزی", "Weave assignment"); SelvedgeStepButton.ToolTip = T("تعریف کناره", "Selvedges"); WeftStepButton.ToolTip = T("تعریف پودگذاری", "Weft / box motion"); FloatStepButton.ToolTip = T("فلوت‌گیری", "Float check"); AnalysisStepButton.ToolTip = T("تحلیل", "Analysis"); MachineStepButton.ToolTip = T("خروجی ماشین", "Machine output");
        AnalysisTitle.Text = T("تحلیل تارها", "Warp analysis"); AnalysisHint.Text = T("درصدها شاخص نسبی‌اند، نه اندازه‌گیری فیزیکی.", "Percentages are relative indicators, not physical measurements."); RunAnalysisButton.Content = T("محاسبه تحلیل", "Calculate analysis"); AnalysisLegendTitle.Text = T("راهنمای رنگ‌ها", "Color legend"); LegendExcluded.Text = T("خارج از بافت", "Outside fabric");
        FloatTitle.Text = T("بررسی فلوت", "Float inspection"); FloatHint.Text = T("کناره‌ها نیز بررسی می‌شوند. اصلاح خودکار مرکز هر فلوت را در طرح تغییر می‌دهد؛ مراکز کناره و فرمان رد می‌شوند.", "Selvedges are scanned. Auto correction toggles each center in the design; selvedge and signal centers are skipped."); FloatLengthLabel.Text = T("حداقل طول فلوت", "Minimum run length"); FloatWarp.Content = T("تار (ستون)", "Warp (column)"); FloatWeft.Content = T("پود (ردیف)", "Weft (row)"); FloatScanButton.Content = T("بررسی فلوت‌ها", "Scan floats"); FloatAutoButton.Content = T("اصلاح خودکار مرکز فلوت‌ها", "Auto-correct float centers"); FloatPreviousButton.Content = T("قبلی", "Previous"); FloatNextButton.Content = T("بعدی", "Next"); ColorNumberColumn.Header = T("شماره", "Number"); ColorLockColumn.Header = T("قفل", "Lock"); ColorShowColumn.Header = T("نمایش", "Show"); ColorSwatchColumn.Header = T("رنگ", "Color"); ColorCountColumn.Header = T("پیکسل", "Pixels"); ColorPercentColumn.Header = T("درصد", "Percent");
        FileCaption.Text = T("فایل ورودی", "INPUT FILE"); NoFileTitle.Text = T("شروع کار", "Get started"); NoFileHint.Text = T("تصویر، پروژه یا فایل ماشین پشتیبانی‌شده را باز کنید یا در پنجره رها کنید.", "Open or drop a supported image, project or loom file.");
        SizeTitle.Text = T("مرحله ۱ · تغییر اندازه", "Stage 1 · Resize"); SizeHint.Text = T("ابعاد خروجی را وارد و اعمال کنید. تصویر سمت چپ نتیجه همین مرحله است.", "Set and apply the output dimensions; the preview shows the result.");
        WidthLabel.Text = T("عرض (پیکسل)", "Width (px)"); HeightLabel.Text = T("ارتفاع (پیکسل)", "Height (px)"); ApplySizeButton.Content = T("اعمال اندازه", "Apply size"); SaveSizeButton.Content = T("ذخیره خروجی اندازه", "Save resized image"); ToColorButton.Content = T("مرحله بعد: رنگ ←", "Next: colors →");
        SizeNotice.Text = T("تصویر پیکسلی با نزدیک‌ترین همسایه و PDF/AI از منبع برداری بدون هموارسازی تغییر اندازه می‌یابند؛ نسبت ابعاد خودکار حفظ نمی‌شود.", "Raster images use nearest-neighbor; PDF/AI are rerendered from vectors without smoothing. Aspect ratio is not locked.");
        ColorTitle.Text = T("مرحله ۲ · کاهش رنگ", "Stage 2 · Colors"); ColorHint.Text = T("ورودی این مرحله تصویر تغییراندازه‌یافته است؛ ابتدا تا ۲۵۶ رنگ سازگار با تصویر نمایش داده می‌شود؛ سپس تعداد را کاهش دهید.", "Input is the resized image; up to 256 adaptive colors are shown first; reduce the count as needed.");
        ColorLimitLabel.Text = T("حداکثر تعداد رنگ (۱ تا ۲۵۶)", "Maximum colors (1–256)"); ApplyColorButton.Content = T("اعمال تعداد رنگ", "Apply color limit"); SaveColorButton.Content = T("خروجی تصویر", "Export image"); SavePaletteButton.Content = T("ذخیره پالت رنگ PNG", "Save palette PNG"); ToPatternButton.Content = T("مرحله بعد: بافت ←", "Next: patterns →");
        SaveButton.Content = T("ذخیره تصویر", "Save image");
        ColorSelectionHint.Text = T("برای انتخاب رنگ‌ها، روی تصویر کادر بکشید.", "Drag on the image to select colors."); DeleteColorButton.Content = T("حذف رنگ‌های انتخابی", "Remove selected"); MergeColorButton.Content = T("ادغام رنگ‌ها", "Merge colors");
        BankTitle.Text = T("بانک و طراحی بافت", "Weave bank and editor"); BankHint.Text = T("یک بافت انتخاب کنید یا روی شبکه ترسیم کنید.", "Select a weave or draw on the grid."); BankBackButton.Content = T("بازگشت", "Back");
        CompositeButton.Content = T("ساخت و تجزیه بافت ترکیبی", "Create / split composite"); Bank3DButton.Content = T("نمای سه‌بعدی انتخاب‌شده", "3D selected weave"); Editor3DButton.ToolTip = T("نمای سه‌بعدی بافت ترسیم‌شده", "3D view of current weave");
        EditorTitle.Text = T("طراحی بافت", "Weave editor"); EditorHint.Text = T("روی خانه‌ها کلیک کنید یا بکشید؛ سیاه و سفید تغییر می‌کنند.", "Click or drag cells to paint black and white.");
        WeaveWidthLabel.Text = T("عرض", "Width"); WeaveHeightLabel.Text = T("ارتفاع", "Height"); EditorResetButton.Content = T("ساخت شبکه", "Create grid"); BankSaveDrawingButton.Content = T("ذخیره در بانک", "Save to bank");
        RandomWeaveButton.Content = T("بافت تصادفی", "Random weaves"); BankListTitle.Text = T("بافت‌های بانک", "Weave library"); BankUseButton.Content = T("اختصاص به رنگ", "Apply to color"); BankEditButton.Content = T("ویرایش انتخاب‌شده", "Edit selection"); BankImportButton.Content = T("افزودن از تصویر یا PDF", "Import image or PDF"); BankDeleteButton.Content = T("حذف از بانک", "Delete from bank");
        UpdateWindowTitle();
        DeveloperText.Text = T("برنامه‌نویس: میرخلیلی", "Developer: Mirkhalili");
        MachineTitle.Text = T("خروجی ماشین", "Machine output"); MachineHint.Text = T("JC5 و JC6 و نمایه Bonas JC5 تک‌ناحیه‌ای بر اساس نمونه‌ها آزمایشی‌اند؛ سازگاری دستگاه را مستقل بررسی کنید. EP پشتیبانی نمی‌شود.", "Experimental single-area JC5, JC6 and Bonas JC5 sample profile; verify with your controller. EP is unsupported.");
        MachineFormatLabel.Text = T("قالب هدف", "Target format"); MachineRedAsBlack.Content = T("نمایش بافت به رنگ قرمز / قرمز = بیت فعال", "Display active pixels in red / red = active"); MachineLegacyImport.Content = T("خواندن فایل خروجی نسخه قدیمی ۰۰۶", "Import a file created by old version 006"); MachineUseSource.Content = T("استفاده از نقشه اصلی به جای نتیجه بافت", "Use original hook map instead of pattern result"); MachineExportButton.Content = T("تبدیل و ذخیره فایل ماشین", "Convert and save loom file"); MachineImportButton.Content = T("انتخاب فایل ماشین", "Select loom file");
        SelvedgeTitle.Text = T("تعریف کناره", "Selvedges"); WeftTitle.Text = T("تعریف پودگذاری", "Weft / box motion"); MachineSetupHint.Text = T("کناره‌ها به عرض طرح اضافه می‌شوند؛ برای هر سمت، پهنا و بافت بانک را انتخاب کنید.", "Selvedges are added to the design width; select a width and library weave for each side.");
        WeftHint.Text = T("شماره ستون‌ها از ۱ و از چپ است؛ ستون‌های خالی سفید می‌مانند.", "Columns are numbered from 1 at the left; unused columns stay white."); TotalHooksLabel.Text = T("تعداد کل تارها (خالی = خودکار)", "Total hooks (blank = automatic)"); DesignStartLabel.Text = T("شروع طرح (خالی = پس از کناره)", "Design start (blank = after selvedge)"); WeftCountLabel.Text = T("تعداد پود", "Number of wefts"); WeftStartLabel.Text = T("شروع پود از ستون", "Weft starting column"); SelvedgePreviewButton.Content = T("پیش‌نمایش کناره‌ها", "Preview selvedges"); LeftSelvedgeBank.Content = T("انتخاب کناره چپ از بانک", "Choose left weave from bank"); RightSelvedgeBank.Content = T("انتخاب کناره راست از بانک", "Choose right weave from bank"); LegacySetup.Header = T("تنظیمات قدیمی پروژه", "Legacy project settings"); UpgradeLayoutButton.Content = T("تغییر به چیدمان جدید", "Switch to new layout");
        LeftSelvedgeLabel.Text = T("کناره چپ: تعداد قلاب و بافت", "Left selvedge: hooks and weave"); RightSelvedgeLabel.Text = T("کناره راست: تعداد قلاب و بافت", "Right selvedge: hooks and weave"); RefreshMachineBankButton.Content = T("به‌روزرسانی فهرست بانک", "Refresh weave bank");
        BoxEnabled.Content = T("فعال‌سازی قلاب‌های باکس‌موشن", "Enable box-motion signal hooks"); BoxPlacementLabel.Text = T("محل قلاب‌های فرمان", "Signal hook placement"); BoxAddLeft.Content = T("قلاب‌های اضافه در چپ", "Add signal hooks on left"); BoxAddRight.Content = T("قلاب‌های اضافه در راست", "Add signal hooks on right"); BoxReserved.Content = T("ستون‌های موجود (جایگزین محتوا)", "Existing columns (replace contents)");
        BoxHooksLabel.Text = T("تعداد قلاب اضافه / شماره ستون‌های موجود", "Extra hook count / existing column indices"); BoxCodesLabel.Text = T("الگوی هر پود؛ هر خط مثل 1=10", "Weft codes; one per line, e.g. 1=10"); BoxSequenceLabel.Text = T("توالی پودها؛ خالی = ترتیب طبیعی", "Weft sequence; blank = natural order"); BoxBottom.Content = T("شروع توالی از پایین تصویر", "Start sequence at image bottom");
        BoxSafetyHint.Text = T("در هر رج فقط ستون پود انتخابی سیاه است؛ اتصال و قطبیت واقعی دستگاه باید مستقل تأیید شود.", "Only the selected weft column is black per row. Independently verify wiring and polarity."); ApplyWeftButton.Content = T("اعمال پودگذاری", "Apply weft settings"); MachinePreviewButton.Content = T("پیش‌نمایش تنظیمات اعمال‌شده", "Preview applied settings");
        MachineNotice.Text = T("نقشه خروجی تک‌ناحیه‌ای و آزمایشی است. کناره و فرمان فقط به صورت ستون‌های منطقی درج می‌شوند؛ نگاشت فیزیکی و قطبیت کنترلر را قبل از تولید بررسی کنید.", "Output is experimental and single-area. Selvedges and signals are logical columns only; verify physical mapping and polarity before production.");
        SafetyText.Text = T("خروجی تصویر است، نه فایل دستگاه بافندگی.", "Output is an image, not a loom file."); CanvasSubtitle.Text = T("خروجی هر مرحله در این قسمت نمایش داده می‌شود", "The result of each stage appears here");
        EmptyTitle.Text = T("طرح خود را وارد کنید", "Bring in your design"); EmptyHint.Text = T("تصویر یا پروژه را انتخاب کنید.", "Choose an image or project."); EmptyButton.Content = T("باز کردن تصویر یا پروژه", "Open image or project");
        if (session is null) StatusText.Text = T("آماده برای باز کردن تصویر", "Ready to open an image");
    }
}
