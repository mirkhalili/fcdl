using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;

namespace JacquardStudio;

/// <summary>Inkscape-inspired, theme-aware SVG workspace with editable Bézier paths and layers.</summary>
public sealed class VectorEditorWindow : Window
{
    private enum Tool { Select, Nodes, Bezier, Curve, ConvertLineToBezier, Pencil, Rectangle, Ellipse, Star, Line, Text, Zoom }
    private static readonly (Tool Tool, string Name, string Shortcut, string Glyph)[] ToolSpecs =
    {
        (Tool.Select, "انتخاب و جابه‌جایی", "F1", "M5,3 L19,15 12,15 9,21 Z"),
        (Tool.Nodes, "ویرایش گره‌ها و دسته‌ها", "N", "M4,4 H20 V20 H4 Z M4,4 L20,20 M20,4 L4,20"),
        (Tool.Bezier, "قلم بزیه؛ کلیک برای گوشه و کشیدن برای منحنی", "B", "M4,19 C4,5 20,5 20,19 M4,19 H20 M4,19 L4,16 M20,19 L20,16"),
        (Tool.Curve, "منحنی بزیه سه‌مرحله‌ای؛ آغاز، پایان، قوس؛ Shift برای تقارن", "C", "M3,17 C7,2 17,2 21,17 M3,17 L21,17 M12,4 V2"),
        (Tool.ConvertLineToBezier, "تبدیل خط مستقیم به منحنی بزیه قابل ویرایش", "K", "M4,19 L20,5 M4,19 L8,19 M20,5 L16,5 M11,12 L8,9 M13,12 L16,15"),
        (Tool.Pencil, "مداد آزاد", "P", "M4,20 L6,14 16,4 20,8 10,18 Z M6,14 L10,18"),
        (Tool.Rectangle, "مستطیل", "R", "M4,5 H20 V19 H4 Z"),
        (Tool.Ellipse, "بیضی و دایره", "E", "M12,4 C17,4 20,7 20,12 C20,17 17,20 12,20 C7,20 4,17 4,12 C4,7 7,4 12,4 Z"),
        (Tool.Star, "ستاره و چندضلعی", "*", "M12,3 L14.5,9 21,9.5 16,14 17.5,21 12,17.5 6.5,21 8,14 3,9.5 9.5,9 Z"),
        (Tool.Line, "خط مستقیم", "L", "M4,20 L20,4 M4,20 H8 M20,4 V8"),
        (Tool.Text, "متن برداری", "T", "M4,5 H20 M12,5 V20 M8,20 H16"),
        (Tool.Zoom, "بزرگ‌نمایی روی صفحه", "Z", "M10,4 A6,6 0 1 0 10,16 A6,6 0 1 0 10,4 M15,15 L21,21")
    };
    private static readonly string[] Swatches = { "#171717", "#FFFFFF", "#C9A58B", "#D9A441", "#7CC5B7", "#6CA0D9", "#A981C5", "#D9726C", "#506276", "#A9B1B9", "#2F755A", "#E7B5A9", "#E8E1D9", "#60514A", "#91A66E", "#3C4652" };
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private VectorDesignDocument document;
    private readonly DrawingSurface surface;
    private readonly ScrollViewer drawingScroll;
    private readonly TextBlock statusText;
    private readonly TextBlock zoomText;
    private readonly Button undoButton;
    private readonly Button redoButton;
    private readonly Button finishPathButton;
    private readonly Button cancelPathButton;
    private readonly List<ToggleButton> toolButtons = new();
    private readonly List<VectorDesignDocument> undo = new(), redo = new();
    private readonly ListBox layerList = new() { SelectionMode = SelectionMode.Single, MinHeight = 96, MaxHeight = 180, Margin = new Thickness(0, 4, 0, 6) };
    private readonly TextBox pageWidth = new() { Width = 82, Text = "640", FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox pageHeight = new() { Width = 82, Text = "480", FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox fillOpacityText = new() { Width = 55, Text = "100", FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox strokeWidthText = new() { Width = 66, Text = "2", FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox layerNameText = new() { Margin = new Thickness(0, 3, 0, 4) };
    private readonly TextBox objectNameText = new() { Margin = new Thickness(0, 3, 0, 4) };
    private readonly TextBox objectText = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 52, MaxHeight = 90, Margin = new Thickness(0, 3, 0, 4) };
    private readonly ColorSwatch fillSwatch = new("#C9A58B", "رنگ داخل");
    private readonly ColorSwatch strokeSwatch = new("#242424", "رنگ دورخط");
    private readonly TextBlock selectionInfo = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 4, 1, 3) };
    private readonly StackPanel textObjectPanel = new() { Visibility = Visibility.Collapsed };
    private readonly Slider starPointsSlider = new() { Minimum = 3, Maximum = 16, Value = 5, TickFrequency = 1, IsSnapToTickEnabled = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Thickness(2, 4, 2, 2) };
    private readonly TextBlock starPointsValue = new() { Text = "۵", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4) };
    private BitmapSource? referenceImage;
    private string fillValue = "#C9A58B";
    private string strokeValue = "#242424";
    private bool fillEnabled = true;
    private bool strokeEnabled = true;
    private double zoom = 1;
    private Tool tool = Tool.Select;
    private Guid? selectedObjectId;
    private bool updatingUi;
    private bool dirty;
    private bool accepted;
    private bool skipClosePrompt;
    private string? currentFile;
    private VectorDesignPath? bezierDraft;
    private VectorDesignDocument? bezierOrigin;
    private int pendingBezierNode = -1;
    private Point bezierAnchorDown;
    private VectorDesignPath? curveDraft;
    private int curveStage;
    private List<Point>? freehandPoints;
    private Point? gestureStart;
    private Point pointer;
    private bool pointerInside;
    private VectorDesignObject? previewObject;
    private VectorDesignDocument? gestureOrigin;
    private VectorDesignObject? gestureObjectOrigin;
    private bool movingObject;
    private bool resizingObject;
    private bool movingNode;
    private bool gestureChanged;
    private int resizeHandle = -1;
    private Rect resizeBounds;
    private Point resizeOpposite;
    private Point gesturePoint;
    private int selectedNodePath = -1;
    private int selectedNodeIndex = -1;
    private int selectedHandle = -1; // 0 anchor, 1 incoming, 2 outgoing
    private int movingPathIndex = -1;
    private int movingNodeIndex = -1;
    private int movingHandleKind;
    private bool constrainAspect;

    public string? ResultSvg { get; private set; }
    public BitmapSource? ResultImage { get; private set; }
    public VectorDesignDocument? ResultDocument { get; private set; }

    public VectorEditorWindow(VectorDesignDocument? initial = null, BitmapSource? reference = null)
    {
        document = initial?.Clone() ?? new VectorDesignDocument();
        document.Validate();
        referenceImage = reference;
        Title = "Jacquard Studio · استودیو طراحی برداری";
        Width = 1560; Height = 960; MinWidth = 1100; MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
        FlowDirection = FlowDirection.LeftToRight;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "PrimaryText");
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0), GlassFrameThickness = new Thickness(0), UseAeroCaptionButtons = false });

        surface = new DrawingSurface();
        drawingScroll = new ScrollViewer { Name = "VectorCanvasScrollViewer", Background = Brush("GraphBackground"), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false, Padding = new Thickness(0) };
        statusText = new TextBlock { Text = "آماده · B قلم بزیه · C منحنی سه‌مرحله‌ای · Shift قوس متقارن", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(12, 0, 8, 0), FontSize = 11 };
        zoomText = new TextBlock { Text = "100٪", VerticalAlignment = VerticalAlignment.Center, MinWidth = 54, TextAlignment = TextAlignment.Center, Foreground = Brush("Accent"), FontWeight = FontWeights.Bold };
        undoButton = ToolbarButton("↶", "بازگشت (Ctrl+Z)", Undo);
        redoButton = ToolbarButton("↷", "بازانجام (Ctrl+Y)", Redo);
        finishPathButton = ToolbarButton("✓ پایان مسیر", "پایان مسیر جاری (Enter)", FinishActivePath);
        cancelPathButton = ToolbarButton("× لغو مسیر", "لغو مسیر جاری (Escape)", CancelActivePath, danger: true);

        Content = BuildShell();
        PreviewKeyDown += Window_KeyDown;
        Closing += Window_Closing;
        Loaded += (_, _) => { FitPage(); surface.Focus(); };
        fillSwatch.ColorChanged += (_, _) => { fillValue = fillSwatch.Value; fillEnabled = true; ApplyStyle(); };
        strokeSwatch.ColorChanged += (_, _) => { strokeValue = strokeSwatch.Value; strokeEnabled = true; ApplyStyle(); };
        layerList.SelectionChanged += LayerSelection_Changed;
        starPointsSlider.ValueChanged += (_, _) => { starPointsValue.Text = ((int)Math.Round(starPointsSlider.Value)).ToString(Invariant); RefreshSurface(); };
        RefreshAll();
    }

    private Grid BuildShell()
    {
        var shell = new Grid { Background = Brush("AppBackground") };
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        var titleBar = BuildTitleBar(); Grid.SetRow(titleBar, 0); shell.Children.Add(titleBar);
        var toolbar = BuildToolbar(); Grid.SetRow(toolbar, 1); shell.Children.Add(toolbar);

        var body = new Grid { FlowDirection = FlowDirection.LeftToRight };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        var toolRail = BuildToolRail(); Grid.SetColumn(toolRail, 0); body.Children.Add(toolRail);
        var workspace = BuildWorkspace(); Grid.SetColumn(workspace, 1); body.Children.Add(workspace);
        var inspector = BuildInspector(); Grid.SetColumn(inspector, 2); body.Children.Add(inspector);
        Grid.SetRow(body, 2); shell.Children.Add(body);

        var footer = new Border { Background = Brush("HeaderBackground"), BorderBrush = Brush("Border"), BorderThickness = new Thickness(0, 1, 0, 0) };
        var footerGrid = new Grid { FlowDirection = FlowDirection.LeftToRight };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.Children.Add(statusText);
        var safety = new TextBlock { Text = "SVG · خروجی تصویر پیکسلی است؛ فایل ماشین نیاز به کنترل جداگانه دارد", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Muted"), FontSize = 10, Margin = new Thickness(12, 0, 12, 0) };
        Grid.SetColumn(safety, 1); footerGrid.Children.Add(safety); footer.Child = footerGrid;
        Grid.SetRow(footer, 3); shell.Children.Add(footer);
        return shell;
    }

    private UIElement BuildTitleBar()
    {
        var titleBar = new Border { Background = Brush("HeaderBackground"), BorderBrush = Brush("Border"), BorderThickness = new Thickness(0, 0, 0, 1) };
        titleBar.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) ToggleMaximize(); else if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        var grid = new Grid { Margin = new Thickness(10, 0, 4, 0), FlowDirection = FlowDirection.LeftToRight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/JacquardStudio;component/Assets/sam-emblem.png")), Width = 23, Height = 23, Stretch = Stretch.Uniform, Margin = new Thickness(2, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(logo, 0); grid.Children.Add(logo);
        var title = new TextBlock { Text = "JACQUARD  /  VECTOR DESIGN", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, FlowDirection = FlowDirection.LeftToRight };
        Grid.SetColumn(title, 1); grid.Children.Add(title);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        controls.Children.Add(WindowButton("−", "کمینه کردن", () => SystemCommands.MinimizeWindow(this)));
        controls.Children.Add(WindowButton("□", "بزرگ‌نمایی / بازگردانی", ToggleMaximize));
        controls.Children.Add(WindowButton("✕", "بستن", () => CancelDialog(), danger: true));
        Grid.SetColumn(controls, 2); grid.Children.Add(controls);
        titleBar.Child = grid;
        return titleBar;
    }

    private Button WindowButton(string label, string tip, Action action, bool danger = false)
    {
        var button = new Button { Content = label, ToolTip = tip, Width = 42, Height = 32, MinHeight = 32, Padding = new Thickness(0), Margin = new Thickness(0), Background = danger ? Brush("DangerBackground") : Brushes.Transparent, BorderBrush = Brushes.Transparent };
        button.Click += (_, _) => action();
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        return button;
    }

    private UIElement BuildToolbar()
    {
        var border = new Border { Background = Brush("HeaderBackground"), BorderBrush = Brush("Border"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(8, 4, 8, 4) };
        var rail = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.RightToLeft, VerticalAlignment = VerticalAlignment.Center };
        AddToolbarButton(rail, "جدید", "صفحه برداری تازه", NewDocument);
        AddToolbarButton(rail, "بازکردن", "بازکردن SVG یا تصویر مرجع (Ctrl+O)", OpenDocument);
        AddToolbarButton(rail, "ذخیره SVG", "ذخیره طرح قابل ویرایش (Ctrl+S)", SaveSvg);
        AddToolbarButton(rail, "خروجی PNG", "خروجی تصویر PNG", ExportPng);
        AddToolbarSeparator(rail);
        rail.Children.Add(undoButton); rail.Children.Add(redoButton);
        AddToolbarSeparator(rail);
        AddToolbarButton(rail, "−", "کوچک‌نمایی", () => SetZoom(zoom / 1.2));
        rail.Children.Add(zoomText);
        AddToolbarButton(rail, "+", "بزرگ‌نمایی", () => SetZoom(zoom * 1.2));
        AddToolbarButton(rail, "برازش", "جای‌دادن صفحه در فضای کاری", FitPage);
        AddToolbarSeparator(rail);
        rail.Children.Add(finishPathButton); rail.Children.Add(cancelPathButton);
        AddToolbarSeparator(rail);
        AddToolbarButton(rail, "انصراف", "بستن و کنارگذاشتن تغییرات", CancelDialog, danger: true);
        AddToolbarButton(rail, "اعمال در پروژه", "اعمال SVG به گردش‌کار طراحی", ApplyToProject, accent: true);
        var scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = rail, FlowDirection = FlowDirection.RightToLeft, CanContentScroll = false };
        border.Child = scroll;
        return border;
    }

    private static Button ToolbarButton(string text, string tooltip, Action action, bool danger = false)
    {
        var button = new Button { Content = text, ToolTip = tooltip, Margin = new Thickness(3, 1, 3, 1), Padding = new Thickness(9, 5, 9, 5), MinHeight = 32, Background = Brush(danger ? "DangerBackground" : "ButtonBackground"), BorderBrush = Brush("ButtonBorder"), Cursor = Cursors.Hand };
        button.Click += (_, _) => action();
        AutomationProperties.SetName(button, tooltip);
        return button;
    }

    private void AddToolbarButton(Panel panel, string text, string tooltip, Action action, bool accent = false, bool danger = false)
    {
        var button = ToolbarButton(text, tooltip, action, danger);
        if (accent) { button.Background = Brush("ActionBackground"); button.Foreground = Brush("AccentForeground"); button.BorderBrush = Brush("Accent"); button.FontWeight = FontWeights.SemiBold; }
        panel.Children.Add(button);
    }

    private static void AddToolbarSeparator(Panel panel) => panel.Children.Add(new Border { Width = 1, Height = 28, Background = Brush("Border"), Margin = new Thickness(7, 3, 7, 3), VerticalAlignment = VerticalAlignment.Center });

    private UIElement BuildToolRail()
    {
        var stack = new StackPanel { Margin = new Thickness(4, 7, 4, 7), HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(new TextBlock { Text = "ابزار", HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush("Muted"), FontSize = 10, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        for (int i = 0; i < ToolSpecs.Length; i++)
        {
            int index = i;
            var spec = ToolSpecs[i];
            var glyph = new System.Windows.Shapes.Path { Data = Geometry.Parse(spec.Glyph), Width = 21, Height = 21, Stretch = Stretch.Uniform, Stroke = Brush(spec.Tool == tool ? "Accent" : "PrimaryText"), StrokeThickness = 1.7, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            var button = new ToggleButton { Name = "VectorTool_" + spec.Tool, Content = glyph, Width = 44, Height = 42, Margin = new Thickness(1, 2, 1, 2), Padding = new Thickness(4), Background = Brush(spec.Tool == tool ? "GraphSelectedItem" : "GraphHeader"), BorderBrush = Brush(spec.Tool == tool ? "Accent" : "Border"), BorderThickness = new Thickness(1), IsChecked = spec.Tool == tool, ToolTip = $"{spec.Name} ({spec.Shortcut})", Cursor = Cursors.Hand };
            AutomationProperties.SetName(button, spec.Name);
            button.Click += (_, _) => SelectTool(ToolSpecs[index].Tool);
            toolButtons.Add(button); stack.Children.Add(button);
        }
        var rail = new ScrollViewer { Background = Brush("GraphHeader"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack };
        var border = new Border { Background = Brush("GraphHeader"), BorderBrush = Brush("Border"), BorderThickness = new Thickness(0, 0, 1, 0), Child = rail };
        return border;
    }

    private UIElement BuildWorkspace()
    {
        surface.Document = document;
        surface.Zoom = zoom;
        surface.ReferenceImage = referenceImage;
        surface.Cursor = Cursors.Cross;
        surface.Focusable = true;
        surface.MouseLeftButtonDown += Surface_MouseLeftButtonDown;
        surface.MouseMove += Surface_MouseMove;
        surface.MouseLeftButtonUp += Surface_MouseLeftButtonUp;
        surface.MouseRightButtonDown += Surface_MouseRightButtonDown;
        surface.MouseWheel += Surface_MouseWheel;
        surface.LostMouseCapture += (_, _) => { if (pendingBezierNode >= 0) pendingBezierNode = -1; };
        var page = new Border { Name = "VectorPageFrame", Margin = new Thickness(42), Padding = new Thickness(1), Background = Brushes.White, BorderBrush = Brush("Border"), BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = surface, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = .28 } };
        drawingScroll.Content = page;
        drawingScroll.PreviewMouseWheel += (_, e) => { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SetZoom(zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1)); e.Handled = true; } };
        var workspace = new Grid { Background = Brush("GraphBackground"), ClipToBounds = true };
        workspace.Children.Add(drawingScroll);
        return workspace;
    }

    private UIElement BuildInspector()
    {
        var stack = new StackPanel { Margin = new Thickness(8, 7, 8, 8), FlowDirection = FlowDirection.RightToLeft };
        stack.Children.Add(BuildPageSection());
        stack.Children.Add(BuildStyleSection());
        stack.Children.Add(BuildShapeSection());
        stack.Children.Add(BuildObjectSection());
        stack.Children.Add(BuildLayerSection());
        stack.Children.Add(new TextBlock { Text = "راهنما: B قلم بزیه · C منحنی سه‌مرحله‌ای · K تبدیل خط به بزیه · N گره · F1 انتخاب · Ctrl+Z بازگشت · Enter پایان مسیر · Esc لغو", Foreground = Brush("Muted"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5, 8, 5, 10) });
        return new Border { Background = Brush("AppBackground"), BorderBrush = Brush("Border"), BorderThickness = new Thickness(1, 0, 0, 0), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack } };
    }

    private Border Section(string title, UIElement content)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = Brush("Accent"), FontSize = 12, Margin = new Thickness(1, 0, 1, 6) });
        inner.Children.Add(content);
        return new Border { Background = Brush("Surface"), BorderBrush = Brush("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(9), Margin = new Thickness(1, 0, 1, 7), Child = inner };
    }

    private UIElement BuildPageSection()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "اندازه صفحه (px)", Foreground = Brush("Muted"), FontSize = 10, Margin = new Thickness(1, 1, 1, 3) });
        var row = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight };
        StyleInput(pageWidth); StyleInput(pageHeight);
        row.Children.Add(new TextBlock { Text = "W", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Muted"), Margin = new Thickness(2) }); row.Children.Add(pageWidth);
        row.Children.Add(new TextBlock { Text = "H", VerticalAlignment = VerticalAlignment.Center, Foreground = Brush("Muted"), Margin = new Thickness(8, 2, 2, 2) }); row.Children.Add(pageHeight);
        body.Children.Add(row);
        var apply = SmallButton("اعمال اندازه", "تغییر اندازه صفحه SVG", ApplyPageSize); apply.Margin = new Thickness(0, 5, 0, 1); body.Children.Add(apply);
        return Section("صفحه SVG", body);
    }

    private UIElement BuildStyleSection()
    {
        var body = new StackPanel();
        var fillRow = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.RightToLeft, VerticalAlignment = VerticalAlignment.Center };
        fillRow.Children.Add(new TextBlock { Text = "پرکردن", Width = 74, VerticalAlignment = VerticalAlignment.Center }); fillRow.Children.Add(fillSwatch);
        var noFill = SmallButton("×", "بدون پرکردن", () => { fillEnabled = false; ApplyStyle(); }); noFill.Width = 32; noFill.Margin = new Thickness(3, 0, 0, 0); fillRow.Children.Add(noFill);
        body.Children.Add(fillRow);
        var strokeRow = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.RightToLeft, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 5, 0, 2) };
        strokeRow.Children.Add(new TextBlock { Text = "دورخط", Width = 74, VerticalAlignment = VerticalAlignment.Center }); strokeRow.Children.Add(strokeSwatch);
        var noStroke = SmallButton("×", "بدون دورخط", () => { strokeEnabled = false; ApplyStyle(); }); noStroke.Width = 32; noStroke.Margin = new Thickness(3, 0, 0, 0); strokeRow.Children.Add(noStroke);
        body.Children.Add(strokeRow);
        var widthRow = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 4, 0, 0) };
        widthRow.Children.Add(new TextBlock { Text = "ضخامت دورخط", Width = 100, VerticalAlignment = VerticalAlignment.Center }); StyleInput(strokeWidthText); widthRow.Children.Add(strokeWidthText); body.Children.Add(widthRow);
        var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(0, 4, 0, 4) };
        opacityRow.Children.Add(new TextBlock { Text = "شفافیت ٪", Width = 100, VerticalAlignment = VerticalAlignment.Center }); StyleInput(fillOpacityText); opacityRow.Children.Add(fillOpacityText); body.Children.Add(opacityRow);
        var palette = new UniformGrid { Columns = 8, Rows = 2, Margin = new Thickness(0, 3, 0, 4) };
        foreach (string color in Swatches)
        {
            string selected = color;
            var swatch = new Button { Width = 25, Height = 24, Padding = new Thickness(0), Margin = new Thickness(2), Background = BrushFromHex(color), BorderBrush = Brush("Border"), BorderThickness = new Thickness(1), ToolTip = color };
            swatch.Click += (_, _) => { fillValue = selected; fillEnabled = true; fillSwatch.Value = selected; ApplyStyle(); };
            AutomationProperties.SetName(swatch, "رنگ " + color); palette.Children.Add(swatch);
        }
        body.Children.Add(palette);
        body.Children.Add(SmallButton("اعمال رنگ و خط", "اعمال مشخصات ظاهری روی شیء انتخاب‌شده", ApplyStyle));
        return Section("رنگ و دورخط", body);
    }

    private UIElement BuildShapeSection()
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "تعداد پره‌های ستاره", Foreground = Brush("Muted"), FontSize = 10, Margin = new Thickness(2, 2, 2, 1) });
        var row = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, VerticalAlignment = VerticalAlignment.Center };
        starPointsSlider.Width = 190; row.Children.Add(starPointsSlider); row.Children.Add(starPointsValue); body.Children.Add(row);
        return Section("تنظیم شکل", body);
    }

    private UIElement BuildObjectSection()
    {
        var body = new StackPanel();
        selectionInfo.Foreground = Brush("Muted"); selectionInfo.FontSize = 10;
        body.Children.Add(selectionInfo);
        body.Children.Add(new TextBlock { Text = "نام شیء", Foreground = Brush("Muted"), FontSize = 10, Margin = new Thickness(2, 4, 2, 1) });
        StyleInput(objectNameText); body.Children.Add(objectNameText);
        objectNameText.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyObjectText(); };
        textObjectPanel.Children.Add(new TextBlock { Text = "متن برداری", Foreground = Brush("Muted"), FontSize = 10, Margin = new Thickness(2, 4, 2, 1) });
        StyleInput(objectText); textObjectPanel.Children.Add(objectText);
        textObjectPanel.Children.Add(SmallButton("اعمال متن", "به‌روزرسانی متن انتخاب‌شده", ApplyObjectText));
        body.Children.Add(textObjectPanel);
        body.Children.Add(SmallButton("حذف شیء انتخاب‌شده", "Delete", DeleteSelection, danger: true));
        return Section("شیء انتخاب‌شده", body);
    }

    private UIElement BuildLayerSection()
    {
        var body = new StackPanel();
        layerList.Background = Brush("ListBackground"); layerList.Foreground = Brush("PrimaryText"); layerList.BorderBrush = Brush("Border");
        body.Children.Add(layerList);
        StyleInput(layerNameText); body.Children.Add(layerNameText);
        layerNameText.KeyDown += (_, e) => { if (e.Key == Key.Enter) RenameLayer(); };
        var firstRow = new UniformGrid { Columns = 2 };
        firstRow.Children.Add(SmallButton("＋ لایه", "افزودن لایه", AddLayer));
        firstRow.Children.Add(SmallButton("حذف لایه", "حذف لایه فعال", DeleteLayer, danger: true));
        body.Children.Add(firstRow);
        var secondRow = new UniformGrid { Columns = 2 };
        secondRow.Children.Add(SmallButton("↑ بالا", "انتقال لایه به بالا", () => MoveLayer(1)));
        secondRow.Children.Add(SmallButton("↓ پایین", "انتقال لایه به پایین", () => MoveLayer(-1)));
        body.Children.Add(secondRow);
        var thirdRow = new UniformGrid { Columns = 2 };
        thirdRow.Children.Add(SmallButton("◉ نمایش", "نمایش یا پنهان‌کردن لایه", ToggleLayerVisibility));
        thirdRow.Children.Add(SmallButton("▣ قفل", "قفل یا بازکردن لایه", ToggleLayerLock));
        body.Children.Add(thirdRow);
        return Section("لایه‌ها · ترتیب SVG", body);
    }

    private static void StyleInput(TextBox input)
    {
        input.Padding = new Thickness(5, 3, 5, 3); input.Margin = new Thickness(2); input.MinHeight = 28;
        input.Background = Brush("ButtonBackground"); input.Foreground = Brush("PrimaryText"); input.BorderBrush = Brush("ButtonBorder");
    }

    private static Button SmallButton(string label, string tooltip, Action action, bool danger = false)
    {
        var button = new Button { Content = label, ToolTip = tooltip, Padding = new Thickness(7, 4, 7, 4), Margin = new Thickness(2), MinHeight = 29, Background = Brush(danger ? "DangerBackground" : "ButtonBackground"), BorderBrush = Brush("ButtonBorder"), Cursor = Cursors.Hand };
        button.Click += (_, _) => action(); AutomationProperties.SetName(button, tooltip); return button;
    }

    private static SolidColorBrush BrushFromHex(string value)
    {
        var brush = new SolidColorBrush(VectorDesignDocument.ParseColor(value)); brush.Freeze(); return brush;
    }

    private static SolidColorBrush Brush(string key) => (SolidColorBrush)(Application.Current?.FindResource(key) ?? new SolidColorBrush(Colors.Gray));

    private void SelectTool(Tool next)
    {
        if (bezierDraft is not null && next != Tool.Bezier) FinishBezier(false);
        if (curveStage > 0 && next != Tool.Curve) CancelCurve();
        tool = next;
        for (int i = 0; i < toolButtons.Count; i++)
        {
            bool active = ToolSpecs[i].Tool == tool;
            toolButtons[i].IsChecked = active;
            toolButtons[i].Background = Brush(active ? "GraphSelectedItem" : "GraphHeader");
            toolButtons[i].BorderBrush = Brush(active ? "Accent" : "Border");
            if (toolButtons[i].Content is System.Windows.Shapes.Path icon) icon.Stroke = Brush(active ? "Accent" : "PrimaryText");
        }
        surface.Cursor = tool switch { Tool.Select => Cursors.Arrow, Tool.Text => Cursors.IBeam, Tool.Zoom => Cursors.SizeAll, _ => Cursors.Cross };
        RefreshSurface(); UpdateStatus(); surface.Focus();
    }

    private void RefreshAll()
    {
        updatingUi = true;
        pageWidth.Text = document.Width.ToString(Invariant); pageHeight.Text = document.Height.ToString(Invariant);
        updatingUi = false;
        RefreshLayers(); RefreshProperties(); RefreshSurface(); UpdateStatus();
        undoButton.IsEnabled = undo.Count > 0; redoButton.IsEnabled = redo.Count > 0;
        finishPathButton.IsEnabled = bezierDraft is { Nodes.Count: >= 2 } || curveStage == 2;
        cancelPathButton.IsEnabled = bezierDraft is not null || curveStage > 0;
    }

    private void RefreshSurface()
    {
        surface.Document = document; surface.Zoom = zoom; surface.SelectedObjectId = selectedObjectId;
        surface.ActivePath = CurvePreview() ?? BezierPreview(); surface.ShapePreview = previewObject;
        surface.Pointer = pointer; surface.PointerInside = pointerInside;
        surface.EditorTool = tool; surface.SelectedNodePath = selectedNodePath; surface.SelectedNodeIndex = selectedNodeIndex; surface.SelectedHandle = selectedHandle;
        surface.InvalidateVisual();
    }

    private VectorDesignPath? BezierPreview()
    {
        if (bezierDraft is null) return null;
        var preview = bezierDraft.Clone();
        if (pointerInside && preview.Nodes.Count > 0)
        {
            var from = preview.Nodes[^1];
            from.CurveToNext = from.HasOut;
            preview.Nodes.Add(VectorDesignDocument.Node(pointer.X, pointer.Y));
        }
        return preview;
    }

    private VectorDesignPath? CurvePreview()
    {
        if (curveStage == 0 || curveDraft is null) return null;
        if (curveStage == 2) return curveDraft.Clone();
        var preview = curveDraft.Clone();
        if (pointerInside) preview.Nodes.Add(VectorDesignDocument.Node(pointer.X, pointer.Y));
        return preview;
    }

    private void RefreshLayers()
    {
        updatingUi = true;
        layerList.Items.Clear();
        for (int i = document.Layers.Count - 1; i >= 0; i--)
        {
            var layer = document.Layers[i];
            string icon = layer.Locked ? "▣" : layer.Visible ? "◉" : "○";
            layerList.Items.Add($"{icon}  {layer.Name}  ·  {layer.Objects.Count}");
        }
        layerList.SelectedIndex = document.Layers.Count - 1 - document.ActiveLayer;
        layerNameText.Text = document.CurrentLayer.Name;
        updatingUi = false;
    }

    private void RefreshProperties()
    {
        var selected = FindObject(selectedObjectId, out _, out _);
        if (selected is not null)
        {
            fillEnabled = !selected.Fill.Equals("none", StringComparison.OrdinalIgnoreCase);
            strokeEnabled = !selected.Stroke.Equals("none", StringComparison.OrdinalIgnoreCase);
            if (fillEnabled) fillValue = selected.Fill;
            if (strokeEnabled) strokeValue = selected.Stroke;
            fillSwatch.Value = SafeSwatch(fillValue); strokeSwatch.Value = SafeSwatch(strokeValue);
            strokeWidthText.Text = selected.StrokeWidth.ToString("0.##", Invariant);
            fillOpacityText.Text = (selected.Opacity * 100).ToString("0", Invariant);
            objectNameText.Text = selected.Name;
            textObjectPanel.Visibility = selected.Text is null ? Visibility.Collapsed : Visibility.Visible;
            if (selected.Text is not null) objectText.Text = selected.Text;
            Rect bounds = ObjectBounds(selected);
            selectionInfo.Text = selected.Text is null
                ? $"مسیر · {selected.Paths.Sum(path => path.Nodes.Count)} گره · X {bounds.X:0.#}, Y {bounds.Y:0.#} · {bounds.Width:0.#} × {bounds.Height:0.#}"
                : $"متن برداری · {selected.FontSize:0.#} px · X {selected.TextX:0.#}, Y {selected.TextY:0.#}";
        }
        else
        {
            fillSwatch.Value = SafeSwatch(fillValue); strokeSwatch.Value = SafeSwatch(strokeValue);
            strokeWidthText.Text = "2"; fillOpacityText.Text = "100";
            objectNameText.Text = ""; objectText.Text = ""; textObjectPanel.Visibility = Visibility.Collapsed;
            selectionInfo.Text = "شیئی انتخاب نشده است. ابزار رسم را انتخاب کنید یا روی یک شیء کلیک کنید.";
        }
    }

    private static string SafeSwatch(string paint) => paint is "none" or "transparent" ? "#FFFFFF" : paint;

    private void UpdateStatus()
    {
        string toolName = ToolSpecs.First(item => item.Tool == tool).Name;
        string suffix = curveStage switch
        {
            1 => " · منحنی: نقطه آغاز ثبت شد؛ روی نقطه پایان کلیک کنید",
            2 => $" · منحنی: حرکت نشانگر برای قوس · {(Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? "قوس متقارن" : "Shift برای تقارن")} · برای تکمیل کلیک کنید",
            _ => bezierDraft is null ? "" : $" · مسیر ناتمام: {bezierDraft.Nodes.Count} گره · Enter پایان / Esc لغو"
        };
        statusText.Text = $"{document.Width} × {document.Height} px  ·  {zoom * 100:0}%  ·  {document.Layers.Sum(layer => layer.Objects.Count)} شیء  ·  لایه: {document.CurrentLayer.Name}  ·  {toolName}{suffix}";
        zoomText.Text = $"{zoom * 100:0}%";
        undoButton.IsEnabled = undo.Count > 0; redoButton.IsEnabled = redo.Count > 0;
        finishPathButton.IsEnabled = bezierDraft is { Nodes.Count: >= 2 } || curveStage == 2;
        cancelPathButton.IsEnabled = bezierDraft is not null || curveStage > 0;
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        surface.Focus();
        Point point = DocumentPoint(e.GetPosition(surface)); pointer = point; pointerInside = true;
        if (tool is Tool.Bezier or Tool.Curve or Tool.Pencil or Tool.Rectangle or Tool.Ellipse or Tool.Star or Tool.Line or Tool.Text && document.CurrentLayer.Locked)
        {
            statusText.Text = "لایه فعال قفل است؛ پیش از ترسیم آن را باز کنید."; return;
        }
        switch (tool)
        {
            case Tool.Select: SelectDown(point); break;
            case Tool.Nodes: NodeDown(point); break;
            case Tool.Bezier: BezierDown(point, e.ClickCount); break;
            case Tool.Curve: CurveDown(point); break;
            case Tool.ConvertLineToBezier: ConvertLineToBezierAt(point); break;
            case Tool.Pencil:
                gestureOrigin = document.Clone(); gestureChanged = false; freehandPoints = new List<Point> { point }; surface.CaptureMouse(); break;
            case Tool.Rectangle: case Tool.Ellipse: case Tool.Star: case Tool.Line:
                gestureOrigin = document.Clone(); gestureChanged = false; gestureStart = point; pointer = point; surface.CaptureMouse(); break;
            case Tool.Text: AddText(point); break;
            case Tool.Zoom: SetZoom(zoom * 1.5); break;
        }
        RefreshSurface(); UpdateStatus(); e.Handled = true;
    }

    private void SelectDown(Point point)
    {
        if (selectedObjectId is Guid currentId && FindObject(currentId, out int currentLayer, out _) is { } selected && !document.Layers[currentLayer].Locked)
        {
            int handle = HitResizeHandle(selected, point);
            if (handle >= 0)
            {
                gestureOrigin = document.Clone(); gestureObjectOrigin = selected.Clone(); gestureChanged = false; resizeBounds = ObjectBounds(selected); resizeHandle = handle;
                resizeOpposite = OppositeHandle(resizeBounds, handle); gesturePoint = point; resizingObject = true; constrainAspect = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift); surface.CaptureMouse(); return;
            }
        }
        var hit = HitObject(point, out int layerIndex, out var objectIndex);
        if (hit is null) { selectedObjectId = null; selectedNodeIndex = selectedNodePath = -1; RefreshProperties(); return; }
        selectedObjectId = hit.Id; document.ActiveLayer = layerIndex;
        if (document.Layers[layerIndex].Locked) { RefreshLayers(); RefreshProperties(); return; }
        gestureOrigin = document.Clone(); gestureObjectOrigin = hit.Clone(); gestureChanged = false; gesturePoint = point; movingObject = true; surface.CaptureMouse();
        RefreshLayers(); RefreshProperties();
    }

    private void NodeDown(Point point)
    {
        if (selectedObjectId is not Guid id) return;
        var selected = FindObject(id, out int layerIndex, out _);
        if (selected is null || selected.Text is not null || document.Layers[layerIndex].Locked) return;
        if (!FindNodeHandle(selected, point, out int pathIndex, out int nodeIndex, out int handleKind)) return;
        gestureOrigin = document.Clone(); gestureObjectOrigin = selected.Clone(); gestureChanged = false; gesturePoint = point; movingPathIndex = pathIndex; movingNodeIndex = nodeIndex; movingHandleKind = handleKind;
        selectedNodePath = pathIndex; selectedNodeIndex = nodeIndex; selectedHandle = handleKind; movingNode = true; surface.CaptureMouse(); RefreshSurface();
    }

    private bool FindNodeHandle(VectorDesignObject item, Point point, out int pathIndex, out int nodeIndex, out int handleKind)
    {
        double threshold = 9 / zoom;
        for (int p = 0; p < item.Paths.Count; p++)
        {
            var path = item.Paths[p];
            for (int i = 0; i < path.Nodes.Count; i++)
            {
                var node = path.Nodes[i];
                if (node.HasIn && Distance(point, new Point(node.InX, node.InY)) <= threshold) { pathIndex = p; nodeIndex = i; handleKind = 1; return true; }
                if (node.HasOut && Distance(point, new Point(node.OutX, node.OutY)) <= threshold) { pathIndex = p; nodeIndex = i; handleKind = 2; return true; }
            }
        }
        for (int p = 0; p < item.Paths.Count; p++)
            for (int i = 0; i < item.Paths[p].Nodes.Count; i++)
                if (Distance(point, new Point(item.Paths[p].Nodes[i].X, item.Paths[p].Nodes[i].Y)) <= threshold) { pathIndex = p; nodeIndex = i; handleKind = 0; return true; }
        pathIndex = nodeIndex = handleKind = -1; return false;
    }

    private void ConvertLineToBezierAt(Point point)
    {
        double threshold = 10 / zoom, bestDistance = threshold;
        int bestLayer = -1, bestObject = -1, bestPath = -1, bestSegment = -1;
        for (int layerIndex = document.Layers.Count - 1; layerIndex >= 0; layerIndex--)
        {
            var layer = document.Layers[layerIndex];
            if (!layer.Visible || layer.Locked) continue;
            for (int objectIndex = layer.Objects.Count - 1; objectIndex >= 0; objectIndex--)
            {
                var item = layer.Objects[objectIndex];
                if (!item.Visible || item.Text is not null) continue;
                for (int pathIndex = 0; pathIndex < item.Paths.Count; pathIndex++)
                {
                    var path = item.Paths[pathIndex];
                    int segmentCount = path.Closed ? path.Nodes.Count : path.Nodes.Count - 1;
                    for (int segment = 0; segment < segmentCount; segment++)
                    {
                        var from = path.Nodes[segment];
                        if (from.CurveToNext) continue;
                        var to = path.Nodes[(segment + 1) % path.Nodes.Count];
                        if (Distance(new Point(from.X, from.Y), new Point(to.X, to.Y)) < 1e-6) continue;
                        double distance = DistanceToSegment(point, new Point(from.X, from.Y), new Point(to.X, to.Y));
                        if (distance <= bestDistance)
                        {
                            bestDistance = distance;
                            bestLayer = layerIndex; bestObject = objectIndex; bestPath = pathIndex; bestSegment = segment;
                        }
                    }
                }
            }
        }
        if (bestLayer < 0)
        {
            statusText.Text = "روی یک خط مستقیم از مسیر قابل ویرایش کلیک کنید.";
            return;
        }

        var before = document.Clone();
        try
        {
            var item = document.Layers[bestLayer].Objects[bestObject].Clone();
            if (!VectorDesignDocument.ConvertLineSegmentToBezier(item.Paths[bestPath], bestSegment))
            {
                statusText.Text = "این پاره‌خط از قبل منحنی است یا قابل تبدیل نیست.";
                return;
            }
            document.Layers[bestLayer].Objects[bestObject] = item;
            document.ActiveLayer = bestLayer;
            selectedObjectId = item.Id; selectedNodePath = bestPath; selectedNodeIndex = bestSegment; selectedHandle = -1;
            document.Validate(); Record(before); RefreshAll(); SelectTool(Tool.Nodes);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or OverflowException or FormatException)
        {
            document = before; RefreshAll();
            StudioDialogs.Show(this, ex.Message, "استودیو طراحی برداری", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        Vector line = end - start;
        double lengthSquared = line.LengthSquared;
        if (lengthSquared < 1e-12) return Distance(point, start);
        double t = Math.Clamp(Vector.Multiply(point - start, line) / lengthSquared, 0, 1);
        return Distance(point, start + line * t);
    }

    private void BezierDown(Point point, int clickCount)
    {
        if (bezierDraft is not null && bezierDraft.Nodes.Count >= 3 && Distance(point, new Point(bezierDraft.Nodes[0].X, bezierDraft.Nodes[0].Y)) <= 10 / zoom)
        {
            FinishBezier(true); return;
        }
        if (bezierDraft is null)
        {
            bezierOrigin = document.Clone(); bezierDraft = new VectorDesignPath();
        }
        var previous = bezierDraft.Nodes.LastOrDefault();
        bezierDraft.Nodes.Add(VectorDesignDocument.Node(point.X, point.Y));
        if (previous is not null && previous.HasOut) previous.CurveToNext = true;
        pendingBezierNode = bezierDraft.Nodes.Count - 1; bezierAnchorDown = point; pointer = point;
        surface.CaptureMouse();
        if (clickCount >= 2 && bezierDraft.Nodes.Count >= 2) FinishBezier(false);
    }

    private void CurveDown(Point point)
    {
        if (curveStage == 0)
        {
            curveDraft = new VectorDesignPath();
            curveDraft.Nodes.Add(VectorDesignDocument.Node(point.X, point.Y));
            curveStage = 1;
            pointer = point;
            return;
        }
        if (curveDraft is null || curveDraft.Nodes.Count == 0) { CancelCurve(); return; }
        if (curveStage == 1)
        {
            Point start = new(curveDraft.Nodes[0].X, curveDraft.Nodes[0].Y);
            if (Distance(start, point) < 1 / zoom) { statusText.Text = "نقطه پایان باید با نقطه آغاز متفاوت باشد."; return; }
            curveDraft.Nodes.Add(VectorDesignDocument.Node(point.X, point.Y));
            curveStage = 2;
            UpdateCurveDraft(point, IsShiftDown());
            return;
        }
        if (curveStage == 2)
        {
            UpdateCurveDraft(point, IsShiftDown());
            FinishCurve();
        }
    }

    private void UpdateCurveDraft(Point control, bool symmetric)
    {
        if (curveStage != 2 || curveDraft is null || curveDraft.Nodes.Count < 2) return;
        Point start = new(curveDraft.Nodes[0].X, curveDraft.Nodes[0].Y);
        Point end = new(curveDraft.Nodes[1].X, curveDraft.Nodes[1].Y);
        curveDraft = VectorDesignDocument.CreateCubicArc(start, end, control, symmetric);
    }

    private bool IsShiftDown() => Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);

    private void FinishCurve()
    {
        if (curveStage != 2 || curveDraft is null || curveDraft.Nodes.Count != 2) { CancelCurve(); return; }
        var before = document.Clone();
        try
        {
            var item = NewPathObject(curveDraft, closed: false);
            document.CurrentLayer.Objects.Add(item);
            document.Validate();
            selectedObjectId = item.Id;
            curveDraft = null; curveStage = 0;
            Record(before);
            surface.ReleaseMouseCapture(); RefreshAll(); surface.Focus();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or OverflowException or FormatException)
        {
            document = before; curveDraft = null; curveStage = 0; surface.ReleaseMouseCapture(); RefreshAll();
            StudioDialogs.Show(this, ex.Message, "استودیو طراحی برداری", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelCurve()
    {
        curveDraft = null; curveStage = 0; surface.ReleaseMouseCapture(); RefreshSurface(); UpdateStatus();
    }

    private void FinishActivePath()
    {
        if (curveStage == 2) FinishCurve();
        else if (bezierDraft is { Nodes.Count: >= 2 }) FinishBezier(false);
    }

    private void CancelActivePath()
    {
        if (curveStage > 0) CancelCurve();
        else if (bezierDraft is not null) CancelBezier();
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        Point point = DocumentPoint(e.GetPosition(surface)); pointer = point; pointerInside = true;
        if (tool == Tool.Curve && curveStage == 2) UpdateCurveDraft(point, IsShiftDown());
        else if (pendingBezierNode >= 0 && e.LeftButton == MouseButtonState.Pressed && bezierDraft is not null)
        {
            VectorDesignNode node = bezierDraft.Nodes[pendingBezierNode];
            double dx = point.X - bezierAnchorDown.X, dy = point.Y - bezierAnchorDown.Y;
            if (Math.Sqrt(dx * dx + dy * dy) >= 3 / zoom)
            {
                node.HasIn = node.HasOut = true; node.InX = node.X - dx; node.InY = node.Y - dy; node.OutX = node.X + dx; node.OutY = node.Y + dy;
                if (pendingBezierNode > 0) bezierDraft.Nodes[pendingBezierNode - 1].CurveToNext = true;
                if (pendingBezierNode < bezierDraft.Nodes.Count - 1) node.CurveToNext = true;
            }
        }
        else if (movingObject && gestureObjectOrigin is not null)
        {
            gestureChanged |= Distance(point, gesturePoint) > .1 / zoom;
            var moved = gestureObjectOrigin.Clone(); Translate(moved, point.X - gesturePoint.X, point.Y - gesturePoint.Y); ReplaceSelected(moved);
        }
        else if (resizingObject && gestureObjectOrigin is not null)
        {
            gestureChanged |= Distance(point, gesturePoint) > .1 / zoom;
            var resized = ResizeFromOriginal(gestureObjectOrigin, resizeBounds, resizeOpposite, resizeHandle, point, constrainAspect);
            ReplaceSelected(resized);
        }
        else if (movingNode && gestureObjectOrigin is not null)
        {
            gestureChanged |= Distance(point, gesturePoint) > .1 / zoom;
            var changed = MoveNodeFromOriginal(gestureObjectOrigin, movingPathIndex, movingNodeIndex, movingHandleKind, point);
            ReplaceSelected(changed);
        }
        else if (gestureStart is Point start && tool is Tool.Rectangle or Tool.Ellipse or Tool.Star or Tool.Line)
        {
            previewObject = MakeShape(tool, start, point, Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));
        }
        else if (tool == Tool.Pencil && freehandPoints is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            if (freehandPoints.Count < 12000 && Distance(freehandPoints[^1], point) >= 1.5 / zoom) freehandPoints.Add(point);
        }
        RefreshSurface(); UpdateStatus();
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (pendingBezierNode >= 0) pendingBezierNode = -1;
        if (movingObject || resizingObject || movingNode)
        {
            movingObject = resizingObject = movingNode = false;
            FinishGesture(); surface.ReleaseMouseCapture();
        }
        else if (gestureStart is Point start && tool is Tool.Rectangle or Tool.Ellipse or Tool.Star or Tool.Line)
        {
            var point = DocumentPoint(e.GetPosition(surface));
            var item = MakeShape(tool, start, point, Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift));
            gestureStart = null; previewObject = null;
            if (item is not null) { document.CurrentLayer.Objects.Add(item); selectedObjectId = item.Id; gestureChanged = true; FinishGesture(); }
            else { gestureOrigin = null; gestureChanged = false; }
            surface.ReleaseMouseCapture(); RefreshAll();
        }
        else if (tool == Tool.Pencil && freehandPoints is not null)
        {
            Point end = DocumentPoint(e.GetPosition(surface));
            if (freehandPoints.Count < 12000 && Distance(freehandPoints[^1], end) > 0.25) freehandPoints.Add(end);
            var points = freehandPoints; freehandPoints = null;
            if (points.Count >= 2)
            {
                var path = FreehandPath(points); var item = NewPathObject(path, closed: false);
                item.Fill = "none"; item.Stroke = strokeEnabled ? strokeValue : "none"; item.StrokeWidth = CurrentStrokeWidth();
                document.CurrentLayer.Objects.Add(item); selectedObjectId = item.Id; gestureChanged = true; FinishGesture();
            }
            else { gestureOrigin = null; gestureChanged = false; }
            surface.ReleaseMouseCapture(); RefreshAll();
        }
        else surface.ReleaseMouseCapture();
        UpdateStatus(); e.Handled = true;
    }

    private void Surface_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (tool == Tool.Bezier && bezierDraft is not null) { FinishBezier(false); e.Handled = true; }
        else if (tool == Tool.Curve && curveStage > 0) { CancelCurve(); e.Handled = true; }
    }

    private void Surface_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SetZoom(zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1)); e.Handled = true; }
    }

    private static double Distance(Point one, Point two) => (one - two).Length;
    private Point DocumentPoint(Point point) => new(Math.Clamp(point.X / zoom, 0, document.Width), Math.Clamp(point.Y / zoom, 0, document.Height));

    private void FinishBezier(bool close)
    {
        if (bezierDraft is null || bezierDraft.Nodes.Count < 2) { CancelBezier(); return; }
        bezierDraft.Closed = close;
        var item = NewPathObject(bezierDraft, close);
        if (!close) item.Fill = "none";
        document.CurrentLayer.Objects.Add(item); selectedObjectId = item.Id;
        gestureOrigin = bezierOrigin; gestureChanged = true; bezierOrigin = null; bezierDraft = null; pendingBezierNode = -1;
        FinishGesture(); surface.ReleaseMouseCapture(); RefreshAll(); surface.Focus();
    }

    private void CancelBezier()
    {
        bezierDraft = null; bezierOrigin = null; pendingBezierNode = -1; surface.ReleaseMouseCapture(); RefreshSurface(); UpdateStatus();
    }

    private VectorDesignObject NewPathObject(VectorDesignPath path, bool closed)
    {
        var clone = path.Clone(); clone.Closed = closed;
        return new VectorDesignObject
        {
            Name = closed ? "مسیر بسته" : "مسیر بزیه",
            Paths = new List<VectorDesignPath> { clone },
            Fill = closed && fillEnabled ? fillValue : "none",
            Stroke = strokeEnabled ? strokeValue : "none",
            StrokeWidth = CurrentStrokeWidth(),
            Opacity = CurrentOpacity()
        };
    }

    private VectorDesignObject? MakeShape(Tool shape, Point start, Point end, bool constrain)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        if (constrain && shape is Tool.Rectangle or Tool.Ellipse or Tool.Star)
        {
            double side = Math.Max(Math.Abs(dx), Math.Abs(dy)); dx = Math.Sign(dx == 0 ? 1 : dx) * side; dy = Math.Sign(dy == 0 ? 1 : dy) * side;
            end = new Point(start.X + dx, start.Y + dy);
        }
        if (Distance(start, end) < 1 / zoom) return null;
        var item = new VectorDesignObject { Fill = fillEnabled ? fillValue : "none", Stroke = strokeEnabled ? strokeValue : "none", StrokeWidth = CurrentStrokeWidth(), Opacity = CurrentOpacity() };
        if (shape == Tool.Line)
        {
            item.Name = "خط"; item.Fill = "none";
            item.Paths.Add(new VectorDesignPath { Nodes = new List<VectorDesignNode> { VectorDesignDocument.Node(start.X, start.Y), VectorDesignDocument.Node(end.X, end.Y) } });
        }
        else if (shape == Tool.Rectangle)
        {
            item.Name = "مستطیل";
            double left = Math.Min(start.X, end.X), top = Math.Min(start.Y, end.Y), right = Math.Max(start.X, end.X), bottom = Math.Max(start.Y, end.Y);
            item.Paths.Add(new VectorDesignPath { Closed = true, Nodes = new List<VectorDesignNode> { VectorDesignDocument.Node(left, top), VectorDesignDocument.Node(right, top), VectorDesignDocument.Node(right, bottom), VectorDesignDocument.Node(left, bottom) } });
        }
        else if (shape == Tool.Ellipse)
        {
            item.Name = "بیضی"; item.Paths.Add(VectorDesignDocument.EllipsePath(start.X, start.Y, end.X, end.Y));
        }
        else if (shape == Tool.Star)
        {
            item.Name = "ستاره"; item.Paths.Add(StarPath(start, end, (int)Math.Round(starPointsSlider.Value)));
        }
        return item;
    }

    private static VectorDesignPath StarPath(Point center, Point radiusPoint, int corners)
    {
        corners = Math.Clamp(corners, 3, 16);
        double outer = Distance(center, radiusPoint), inner = outer * .46, rotation = -Math.PI / 2;
        var path = new VectorDesignPath { Closed = true };
        for (int i = 0; i < corners * 2; i++)
        {
            double r = (i & 1) == 0 ? outer : inner, angle = rotation + i * Math.PI / corners;
            path.Nodes.Add(VectorDesignDocument.Node(center.X + Math.Cos(angle) * r, center.Y + Math.Sin(angle) * r));
        }
        return path;
    }

    private static VectorDesignPath FreehandPath(IReadOnlyList<Point> points)
    {
        if (points.Count < 2) throw new ArgumentException("برای مداد آزاد دست‌کم دو نقطه لازم است.");
        var path = new VectorDesignPath();
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i]; var node = VectorDesignDocument.Node(point.X, point.Y);
            if (points.Count > 2)
            {
                Point previous = points[Math.Max(0, i - 1)], next = points[Math.Min(points.Count - 1, i + 1)];
                double dx = (next.X - previous.X) / 6, dy = (next.Y - previous.Y) / 6;
                node.HasIn = node.HasOut = true; node.InX = point.X - dx; node.InY = point.Y - dy; node.OutX = point.X + dx; node.OutY = point.Y + dy;
                node.CurveToNext = i < points.Count - 1;
            }
            path.Nodes.Add(node);
        }
        return path;
    }

    private void AddText(Point point)
    {
        if (document.CurrentLayer.Locked) return;
        var item = new VectorDesignObject { Name = "متن", Text = "متن برداری", TextX = point.X, TextY = point.Y, FontSize = 40, Fill = fillEnabled ? fillValue : "#242424", Stroke = strokeEnabled ? strokeValue : "none", StrokeWidth = CurrentStrokeWidth(), Opacity = CurrentOpacity() };
        var before = document.Clone();
        document.CurrentLayer.Objects.Add(item); selectedObjectId = item.Id;
        Record(before); RefreshAll(); objectText.Focus(); objectText.SelectAll();
    }

    private double CurrentStrokeWidth() => NumberInput.TryInt(strokeWidthText.Text, out int value) ? Math.Clamp(value, 1, 256) : 2;
    private double CurrentOpacity() => NumberInput.TryInt(fillOpacityText.Text, out int value) ? Math.Clamp(value, 0, 100) / 100.0 : 1;

    private VectorDesignObject? FindObject(Guid? id, out int layerIndex, out int objectIndex)
    {
        layerIndex = objectIndex = -1;
        if (id is null) return null;
        for (int l = 0; l < document.Layers.Count; l++)
            for (int o = 0; o < document.Layers[l].Objects.Count; o++)
                if (document.Layers[l].Objects[o].Id == id.Value) { layerIndex = l; objectIndex = o; return document.Layers[l].Objects[o]; }
        return null;
    }

    private VectorDesignObject? HitObject(Point point, out int layerIndex, out int objectIndex)
    {
        for (int l = document.Layers.Count - 1; l >= 0; l--)
        {
            var layer = document.Layers[l]; if (!layer.Visible || layer.Locked) continue;
            for (int o = layer.Objects.Count - 1; o >= 0; o--)
            {
                var item = layer.Objects[o]; if (!item.Visible) continue;
                if (Hit(item, point)) { layerIndex = l; objectIndex = o; return item; }
            }
        }
        layerIndex = objectIndex = -1; return null;
    }

    private bool Hit(VectorDesignObject item, Point point)
    {
        if (item.Text is { } text)
        {
            Rect bounds = ObjectBounds(item); return bounds.Contains(point) && !string.IsNullOrEmpty(text);
        }
        var geometry = VectorDesignDocument.GeometryFor(item.Paths); if (geometry is null) return false;
        if (item.Fill != "none" && geometry.FillContains(point)) return true;
        if (item.Stroke != "none" && item.StrokeWidth > 0)
        {
            var pen = new Pen(Brushes.Black, Math.Max(item.StrokeWidth, 8 / zoom));
            return geometry.GetWidenedPathGeometry(pen).FillContains(point);
        }
        return false;
    }

    private Rect ObjectBounds(VectorDesignObject item)
    {
        if (item.Text is { } text)
        {
            var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("fa-IR"), FlowDirection.RightToLeft, new Typeface(item.FontFamily), item.FontSize, Brushes.Black, 1.0);
            return new Rect(item.TextX, item.TextY - item.FontSize * .8, Math.Max(item.FontSize * .4, formatted.WidthIncludingTrailingWhitespace), Math.Max(item.FontSize, formatted.Height));
        }
        var geometry = VectorDesignDocument.GeometryFor(item.Paths);
        if (geometry is null) return Rect.Empty;
        Rect bounds = geometry.Bounds;
        if (item.Stroke != "none" && item.StrokeWidth > 0) bounds.Inflate(item.StrokeWidth / 2, item.StrokeWidth / 2);
        return bounds;
    }

    private int HitResizeHandle(VectorDesignObject item, Point point)
    {
        if (tool != Tool.Select) return -1;
        Rect bounds = ObjectBounds(item); if (bounds.IsEmpty) return -1;
        Point[] handles = Handles(bounds);
        for (int i = 0; i < handles.Length; i++) if (Distance(handles[i], point) <= 8 / zoom) return i;
        return -1;
    }

    private static Point[] Handles(Rect r) => new[]
    {
        new Point(r.Left,r.Top), new Point(r.Left+r.Width/2,r.Top), new Point(r.Right,r.Top), new Point(r.Right,r.Top+r.Height/2),
        new Point(r.Right,r.Bottom), new Point(r.Left+r.Width/2,r.Bottom), new Point(r.Left,r.Bottom), new Point(r.Left,r.Top+r.Height/2)
    };

    private static Point OppositeHandle(Rect r, int handle) => Handles(r)[(handle + 4) % 8];
    private static double ScaleRatio(double delta, double originalSpan) => Math.Abs(originalSpan) < 1e-6 ? 1 : delta / originalSpan;

    private static VectorDesignObject ResizeFromOriginal(VectorDesignObject source, Rect bounds, Point opposite, int handle, Point pointer, bool preserveAspect)
    {
        var copy = source.Clone();
        Point moving = Handles(bounds)[handle];
        double sx = 1, sy = 1;
        if (handle is 0 or 2 or 4 or 6) { sx = ScaleRatio(pointer.X - opposite.X, moving.X - opposite.X); sy = ScaleRatio(pointer.Y - opposite.Y, moving.Y - opposite.Y); }
        else if (handle is 1 or 5) sy = ScaleRatio(pointer.Y - opposite.Y, moving.Y - opposite.Y);
        else sx = ScaleRatio(pointer.X - opposite.X, moving.X - opposite.X);
        if (preserveAspect && handle is 0 or 2 or 4 or 6) { double factor = Math.Abs(sx) > Math.Abs(sy) ? sx : sy; sx = sy = factor; }
        sx = Math.Clamp(sx, .01, 1000); sy = Math.Clamp(sy, .01, 1000);
        var matrix = new Matrix(sx, 0, 0, sy, opposite.X * (1 - sx), opposite.Y * (1 - sy));
        TransformObject(copy, matrix);
        return copy;
    }

    private static VectorDesignObject MoveNodeFromOriginal(VectorDesignObject source, int pathIndex, int nodeIndex, int handle, Point point)
    {
        var copy = source.Clone();
        if ((uint)pathIndex >= copy.Paths.Count || (uint)nodeIndex >= copy.Paths[pathIndex].Nodes.Count) return copy;
        var selected = copy.Paths[pathIndex].Nodes[nodeIndex];
        if (handle == 0)
        {
            var original = source.Paths[pathIndex].Nodes[nodeIndex]; double dx = point.X - original.X, dy = point.Y - original.Y;
            selected.X += dx; selected.Y += dy;
            if (selected.HasIn) { selected.InX += dx; selected.InY += dy; }
            if (selected.HasOut) { selected.OutX += dx; selected.OutY += dy; }
        }
        else
        {
            if (handle == 1) { selected.HasIn = true; selected.InX = point.X; selected.InY = point.Y; }
            else { selected.HasOut = true; selected.OutX = point.X; selected.OutY = point.Y; selected.CurveToNext = true; }
            if (handle == 1 && selected.HasOut) { selected.OutX = 2 * selected.X - point.X; selected.OutY = 2 * selected.Y - point.Y; }
            if (handle == 2 && selected.HasIn) { selected.InX = 2 * selected.X - point.X; selected.InY = 2 * selected.Y - point.Y; }
            if (handle == 1 && (nodeIndex > 0 || copy.Paths[pathIndex].Closed && copy.Paths[pathIndex].Nodes.Count > 1))
                copy.Paths[pathIndex].Nodes[(nodeIndex - 1 + copy.Paths[pathIndex].Nodes.Count) % copy.Paths[pathIndex].Nodes.Count].CurveToNext = true;
        }
        return copy;
    }

    private static void Translate(VectorDesignObject item, double dx, double dy)
    {
        if (item.Text is not null) { item.TextX += dx; item.TextY += dy; return; }
        foreach (var path in item.Paths)
            foreach (var node in path.Nodes)
            {
                node.X += dx; node.Y += dy;
                if (node.HasIn) { node.InX += dx; node.InY += dy; }
                if (node.HasOut) { node.OutX += dx; node.OutY += dy; }
            }
    }

    private static void TransformObject(VectorDesignObject item, Matrix transform)
    {
        if (item.Text is not null)
        {
            var p = transform.Transform(new Point(item.TextX, item.TextY)); item.TextX = p.X; item.TextY = p.Y;
            item.FontSize *= Math.Sqrt(Math.Abs(transform.M11 * transform.M22)); item.StrokeWidth *= Math.Sqrt(Math.Abs(transform.M11 * transform.M22)); return;
        }
        foreach (var path in item.Paths)
            foreach (var node in path.Nodes)
            {
                var p = transform.Transform(new Point(node.X, node.Y)); node.X = p.X; node.Y = p.Y;
                if (node.HasIn) { p = transform.Transform(new Point(node.InX, node.InY)); node.InX = p.X; node.InY = p.Y; }
                if (node.HasOut) { p = transform.Transform(new Point(node.OutX, node.OutY)); node.OutX = p.X; node.OutY = p.Y; }
            }
        item.StrokeWidth *= Math.Sqrt(Math.Abs(transform.M11 * transform.M22));
    }

    private void ReplaceSelected(VectorDesignObject item)
    {
        if (FindObject(selectedObjectId, out int layer, out int index) is not null) document.Layers[layer].Objects[index] = item;
    }

    private void FinishGesture()
    {
        if (gestureOrigin is not null && gestureChanged) Record(gestureOrigin);
        gestureOrigin = null; gestureObjectOrigin = null; gestureChanged = false; resizeHandle = -1; selectedNodePath = selectedNodeIndex = -1; selectedHandle = -1;
    }

    private void Record(VectorDesignDocument before)
    {
        undo.Add(before);
        while (undo.Count > 40) undo.RemoveAt(0);
        redo.Clear(); dirty = true;
    }

    private void Mutate(Action action)
    {
        var before = document.Clone();
        try { action(); document.Validate(); Record(before); RefreshAll(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or OverflowException or FormatException)
        {
            document = before; RefreshAll(); StudioDialogs.Show(this, ex.Message, "استودیو طراحی برداری", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Undo()
    {
        if (undo.Count == 0) return;
        redo.Add(document.Clone()); document = undo[^1]; undo.RemoveAt(undo.Count - 1); dirty = true; selectedObjectId = null; RefreshAll();
    }

    private void Redo()
    {
        if (redo.Count == 0) return;
        undo.Add(document.Clone()); document = redo[^1]; redo.RemoveAt(redo.Count - 1); dirty = true; selectedObjectId = null; RefreshAll();
    }

    private void DeleteSelection()
    {
        if (selectedObjectId is not Guid id || FindObject(id, out int layer, out _) is null) return;
        if (document.Layers[layer].Locked) { StudioDialogs.Show(this, "لایه شیء انتخاب‌شده قفل است."); return; }
        Mutate(() => { document.Layers[layer].Objects.RemoveAll(item => item.Id == id); selectedObjectId = null; });
    }

    private void ApplyStyle()
    {
        if (!NumberInput.TryInt(strokeWidthText.Text, out int width) || width is < 0 or > 256 || !NumberInput.TryInt(fillOpacityText.Text, out int opacity) || opacity is < 0 or > 100)
        {
            StudioDialogs.Show(this, "ضخامت دورخط باید بین صفر تا ۲۵۶ و شفافیت بین صفر تا ۱۰۰ باشد.", "رنگ و دورخط"); return;
        }
        fillValue = fillSwatch.Value; strokeValue = strokeSwatch.Value;
        if (selectedObjectId is not Guid id || FindObject(id, out int layerIndex, out _) is not { } item)
        {
            UpdateStatus(); return;
        }
        if (document.Layers[layerIndex].Locked) { StudioDialogs.Show(this, "لایه شیء انتخاب‌شده قفل است."); return; }
        Mutate(() => { item.Fill = fillEnabled ? fillValue : "none"; item.Stroke = strokeEnabled ? strokeValue : "none"; item.StrokeWidth = width; item.Opacity = opacity / 100.0; });
    }

    private void ApplyObjectText()
    {
        if (selectedObjectId is not Guid id || FindObject(id, out int layerIndex, out _) is not { } item) return;
        if (document.Layers[layerIndex].Locked) { StudioDialogs.Show(this, "لایه شیء انتخاب‌شده قفل است."); return; }
        string name = string.IsNullOrWhiteSpace(objectNameText.Text) ? item.Name : objectNameText.Text.Trim();
        if (name.Length > 160 || objectText.Text.Length > 4096) { StudioDialogs.Show(this, "نام شیء حداکثر ۱۶۰ و متن حداکثر ۴۰۹۶ نویسه است."); return; }
        Mutate(() => { item.Name = name; if (item.Text is not null) item.Text = objectText.Text; });
    }

    private void ApplyPageSize()
    {
        if (!NumberInput.TryInt(pageWidth.Text, out int width) || !NumberInput.TryInt(pageHeight.Text, out int height)) { StudioDialogs.Show(this, "عرض و ارتفاع باید عدد صحیح باشند."); return; }
        Mutate(() => { document.Width = width; document.Height = height; });
        FitPage();
    }

    private void AddLayer()
    {
        if (document.Layers.Count >= 64) { StudioDialogs.Show(this, "حداکثر ۶۴ لایه مجاز است."); return; }
        Mutate(() => { document.Layers.Add(new VectorDesignLayer { Name = $"لایه {document.Layers.Count + 1}" }); document.ActiveLayer = document.Layers.Count - 1; selectedObjectId = null; });
    }

    private void DeleteLayer()
    {
        if (document.Layers.Count <= 1) { StudioDialogs.Show(this, "آخرین لایه قابل حذف نیست."); return; }
        Mutate(() => { document.Layers.RemoveAt(document.ActiveLayer); document.ActiveLayer = Math.Min(document.ActiveLayer, document.Layers.Count - 1); selectedObjectId = null; });
    }

    private void RenameLayer()
    {
        if (string.IsNullOrWhiteSpace(layerNameText.Text) || layerNameText.Text.Trim().Length > 80) { StudioDialogs.Show(this, "نام لایه باید بین ۱ تا ۸۰ نویسه باشد."); return; }
        Mutate(() => document.CurrentLayer.Name = layerNameText.Text.Trim());
    }

    private void MoveLayer(int delta)
    {
        int target = document.ActiveLayer + delta;
        if ((uint)target >= document.Layers.Count) return;
        Mutate(() => { var layer = document.Layers[document.ActiveLayer]; document.Layers.RemoveAt(document.ActiveLayer); document.Layers.Insert(target, layer); document.ActiveLayer = target; });
    }

    private void ToggleLayerVisibility()
    {
        Mutate(() => { document.CurrentLayer.Visible = !document.CurrentLayer.Visible; if (!document.CurrentLayer.Visible && selectedObjectId is Guid id && document.CurrentLayer.Objects.Any(item => item.Id == id)) selectedObjectId = null; });
    }

    private void ToggleLayerLock() => Mutate(() => document.CurrentLayer.Locked = !document.CurrentLayer.Locked);

    private void LayerSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (updatingUi || layerList.SelectedIndex < 0) return;
        document.ActiveLayer = Math.Clamp(document.Layers.Count - 1 - layerList.SelectedIndex, 0, document.Layers.Count - 1);
        selectedObjectId = null; RefreshProperties(); RefreshSurface(); UpdateStatus();
    }

    private bool ConfirmReplaceDocument()
    {
        if (!dirty) return true;
        var result = StudioDialogs.Show(this, "تغییرات SVG ذخیره نشده‌اند. پیش از ادامه ذخیره شوند؟", "استودیو طراحی برداری", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Cancel) return false;
        if (result == MessageBoxResult.No) return true;
        SaveSvg();
        return !dirty;
    }

    private void NewDocument()
    {
        if (!ConfirmReplaceDocument()) return;
        document = new VectorDesignDocument(); currentFile = null; dirty = false; undo.Clear(); redo.Clear(); selectedObjectId = null; bezierDraft = null; curveDraft = null; curveStage = 0;
        referenceImage = null; surface.ReferenceImage = null; SetZoom(1); RefreshAll(); FitPage();
    }

    private void OpenDocument()
    {
        var picker = new OpenFileDialog { Title = "بازکردن SVG یا تصویر مرجع", Filter = "SVG و تصویرهای برداری/مرجع|*.svg;*.pdf;*.ai;*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|SVG|*.svg|PDF / AI سازگار|*.pdf;*.ai|تصویر|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff", CheckFileExists = true };
        if (picker.ShowDialog(this) != true || !ConfirmReplaceDocument()) return;
        try
        {
            string extension = Path.GetExtension(picker.FileName).ToLowerInvariant();
            if (extension == ".svg")
            {
                var info = new FileInfo(picker.FileName);
                if (info.Length > ImageLoader.MaxFileBytes) throw new InvalidDataException("پرونده SVG بیش از سقف ۱۲۸ مگابایت است.");
                string svg = File.ReadAllText(picker.FileName, new UTF8Encoding(false, true));
                var opened = VectorDesignDocument.FromSvg(svg);
                ReplaceDocument(opened, null, picker.FileName);
                statusText.Text = "SVG باز شد؛ مسیرها، شکل‌ها و لایه‌های پشتیبانی‌شده قابل ویرایش‌اند.";
            }
            else
            {
                var loaded = ImageLoader.Load(picker.FileName, 0, true, false);
                BitmapSource image = loaded.Image;
                if ((long)image.PixelWidth * image.PixelHeight > DesignProcessor.MaxPixels)
                {
                    double scale = Math.Sqrt((double)DesignProcessor.MaxPixels / ((long)image.PixelWidth * image.PixelHeight));
                    int w = Math.Max(1, (int)Math.Floor(image.PixelWidth * scale)), h = Math.Max(1, DesignProcessor.MaxPixels / w);
                    image = DesignProcessor.Resize(image, w, h);
                }
                ReplaceDocument(new VectorDesignDocument(image.PixelWidth, image.PixelHeight), image, null);
                statusText.Text = "تصویر فقط راهنمای ردیابی است و در SVG خروجی قرار نمی‌گیرد.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or System.Xml.XmlException)
        { StudioDialogs.Show(this, "بازکردن منبع ممکن نشد:\n" + ex.Message, "استودیو طراحی برداری", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ReplaceDocument(VectorDesignDocument next, BitmapSource? reference, string? filename)
    {
        document = next; currentFile = filename; selectedObjectId = null; undo.Clear(); redo.Clear(); dirty = false; bezierDraft = null; curveDraft = null; curveStage = 0;
        surface.ReferenceImage = reference; SetZoom(1); RefreshAll(); FitPage();
    }

    private void SaveSvg()
    {
        string? destination = currentFile;
        if (destination is null)
        {
            var picker = new SaveFileDialog { Title = "ذخیره طرح برداری", Filter = "Scalable Vector Graphics (*.svg)|*.svg", DefaultExt = ".svg", AddExtension = true, OverwritePrompt = true, FileName = "vector-design.svg" };
            if (picker.ShowDialog(this) != true) return;
            destination = picker.FileName;
        }
        try
        {
            string markup = document.ToSvg();
            WriteAtomic(destination, new UTF8Encoding(false).GetBytes(markup));
            currentFile = destination; dirty = false; statusText.Text = "SVG ذخیره شد: " + Path.GetFileName(destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        { StudioDialogs.Show(this, "ذخیره SVG ممکن نشد:\n" + ex.Message, "استودیو طراحی برداری", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        string destination = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, ".svg-" + Guid.NewGuid().ToString("N") + ".tmp");
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void ExportPng()
    {
        var picker = new SaveFileDialog { Title = "خروجی تصویر", Filter = "PNG image (*.png)|*.png", DefaultExt = ".png", AddExtension = true, OverwritePrompt = true, FileName = "vector-design.png" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var bitmap = document.Render(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new FileStream(picker.FileName, FileMode.Create, FileAccess.Write, FileShare.None); encoder.Save(output);
            statusText.Text = "تصویر PNG ذخیره شد: " + Path.GetFileName(picker.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        { StudioDialogs.Show(this, "ذخیره تصویر ممکن نشد:\n" + ex.Message, "خروجی PNG", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void ApplyToProject()
    {
        try
        {
            ResultSvg = document.ToSvg(); ResultImage = document.Render(); ResultDocument = document.Clone();
            accepted = true; DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException)
        { StudioDialogs.Show(this, "اعمال طرح ممکن نشد:\n" + ex.Message, "استودیو طراحی برداری", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void CancelDialog()
    {
        if (dirty)
        {
            var result = StudioDialogs.Show(this, "تغییرات SVG ذخیره نشده‌اند. ذخیره و بستن؟", "استودیو طراحی برداری", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) return;
            if (result == MessageBoxResult.Yes) { SaveSvg(); if (dirty) return; }
        }
        skipClosePrompt = true; accepted = false; DialogResult = false;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (accepted || skipClosePrompt || !dirty) return;
        var result = StudioDialogs.Show(this, "تغییرات SVG ذخیره نشده‌اند. بستن پنجره؟", "استودیو طراحی برداری", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (result == MessageBoxResult.Yes) { e.Cancel = true; SaveSvg(); if (!dirty) e.Cancel = false; }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (control && e.Key == Key.Z) { if (shift) Redo(); else Undo(); e.Handled = true; return; }
        if (control && e.Key == Key.Y) { Redo(); e.Handled = true; return; }
        if (control && e.Key == Key.S) { SaveSvg(); e.Handled = true; return; }
        if (control && e.Key == Key.O) { OpenDocument(); e.Handled = true; return; }
        if (e.Key == Key.Escape) { if (curveStage > 0) CancelCurve(); else if (bezierDraft is not null) CancelBezier(); else CancelDialog(); e.Handled = true; return; }
        if (e.Key == Key.Enter && curveStage == 2) { FinishCurve(); e.Handled = true; return; }
        if (e.Key == Key.Enter && bezierDraft is { Nodes.Count: >= 2 }) { FinishBezier(false); e.Handled = true; return; }
        if (e.Key == Key.Delete && e.OriginalSource is not TextBox) { DeleteSelection(); e.Handled = true; return; }
        if (e.OriginalSource is TextBox) return;
        Tool? selected = e.Key switch
        {
            Key.F1 => Tool.Select, Key.N => Tool.Nodes, Key.B => Tool.Bezier, Key.C => Tool.Curve, Key.K => Tool.ConvertLineToBezier, Key.P => Tool.Pencil,
            Key.R => Tool.Rectangle, Key.E => Tool.Ellipse, Key.L => Tool.Line, Key.T => Tool.Text, Key.Z => Tool.Zoom,
            _ => null
        };
        if (selected is Tool next) { SelectTool(next); e.Handled = true; }
        else if (e.Key is Key.Add or Key.OemPlus) { SetZoom(zoom * 1.2); e.Handled = true; }
        else if (e.Key is Key.Subtract or Key.OemMinus) { SetZoom(zoom / 1.2); e.Handled = true; }
        else if (e.Key == Key.Home) { FitPage(); e.Handled = true; }
    }

    private void SetZoom(double value)
    {
        zoom = Math.Clamp(value, .05, 8);
        surface.Width = document.Width * zoom; surface.Height = document.Height * zoom;
        RefreshSurface(); zoomText.Text = $"{zoom * 100:0}%";
    }

    private void FitPage()
    {
        if (drawingScroll.ViewportWidth < 20 || drawingScroll.ViewportHeight < 20) return;
        double availableWidth = drawingScroll.ViewportWidth - 88, availableHeight = drawingScroll.ViewportHeight - 88;
        if (availableWidth < 1 || availableHeight < 1) return;
        SetZoom(Math.Clamp(Math.Min(availableWidth / document.Width, availableHeight / document.Height), .05, 8));
        drawingScroll.ScrollToHorizontalOffset(0); drawingScroll.ScrollToVerticalOffset(0);
    }

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

}

/// <summary>Vector page and editing overlays, drawn as one retained-mode WPF visual.</summary>
internal sealed class DrawingSurface : FrameworkElement
{
    public VectorDesignDocument Document { get; set; } = new();
    public double Zoom { get; set; } = 1;
    public Guid? SelectedObjectId { get; set; }
    public BitmapSource? ReferenceImage { get; set; }
    public VectorDesignPath? ActivePath { get; set; }
    public VectorDesignObject? ShapePreview { get; set; }
    public Point Pointer { get; set; }
    public bool PointerInside { get; set; }
    public object? EditorTool { get; set; }
    public int SelectedNodePath { get; set; } = -1;
    public int SelectedNodeIndex { get; set; } = -1;
    public int SelectedHandle { get; set; } = -1;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.PushTransform(new ScaleTransform(Zoom, Zoom));
        dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(170, 177, 184)), 1 / Zoom), new Rect(0, 0, Document.Width, Document.Height));
        if (ReferenceImage is not null)
        {
            dc.PushOpacity(.28); dc.DrawImage(ReferenceImage, new Rect(0, 0, Document.Width, Document.Height)); dc.Pop();
        }
        foreach (var layer in Document.Layers)
        {
            if (!layer.Visible) continue;
            foreach (var item in layer.Objects) if (item.Visible) VectorDesignDocument.DrawObject(dc, item);
        }
        if (ShapePreview is not null) VectorDesignDocument.DrawObject(dc, ShapePreview);
        if (ActivePath is not null)
        {
            var geometry = VectorDesignDocument.GeometryFor(new[] { ActivePath });
            if (geometry is not null)
            {
                var accent = ColorBrush("Accent");
                var pen = new Pen(accent, 1.8 / Zoom) { DashStyle = DashStyles.Dash, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawGeometry(null, pen, geometry);
                foreach (var node in ActivePath.Nodes) dc.DrawEllipse(accent, null, new Point(node.X, node.Y), 3.5 / Zoom, 3.5 / Zoom);
            }
        }
        if (SelectedObjectId is Guid id)
        {
            var item = Document.Layers.SelectMany(layer => layer.Objects).FirstOrDefault(obj => obj.Id == id);
            if (item is not null)
            {
                Rect bounds = Bounds(item);
                if (!bounds.IsEmpty)
                {
                    var selectionPen = new Pen(ColorBrush("Accent"), 1.2 / Zoom) { DashStyle = DashStyles.Dash };
                    dc.DrawRectangle(null, selectionPen, bounds);
                    if (EditorTool?.ToString() == "Select")
                        foreach (Point handle in Handles(bounds)) dc.DrawRectangle(ColorBrush("Accent"), new Pen(Brushes.White, 1 / Zoom), new Rect(handle.X - 4 / Zoom, handle.Y - 4 / Zoom, 8 / Zoom, 8 / Zoom));
                }
                if (EditorTool?.ToString() == "Nodes" && item.Text is null) DrawNodes(dc, item);
            }
        }
        dc.Pop();
    }

    private void DrawNodes(DrawingContext dc, VectorDesignObject item)
    {
        for (int p = 0; p < item.Paths.Count; p++)
            for (int n = 0; n < item.Paths[p].Nodes.Count; n++)
            {
                var node = item.Paths[p].Nodes[n];
                if (node.HasIn) { dc.DrawLine(new Pen(ColorBrush("Accent"), 1 / Zoom), new Point(node.X, node.Y), new Point(node.InX, node.InY)); dc.DrawEllipse(Brushes.White, new Pen(ColorBrush("Accent"), 1.2 / Zoom), new Point(node.InX, node.InY), 3.5 / Zoom, 3.5 / Zoom); }
                if (node.HasOut) { dc.DrawLine(new Pen(ColorBrush("Accent"), 1 / Zoom), new Point(node.X, node.Y), new Point(node.OutX, node.OutY)); dc.DrawEllipse(Brushes.White, new Pen(ColorBrush("Accent"), 1.2 / Zoom), new Point(node.OutX, node.OutY), 3.5 / Zoom, 3.5 / Zoom); }
                var center = new Point(node.X, node.Y); var box = new Rect(center.X - 4 / Zoom, center.Y - 4 / Zoom, 8 / Zoom, 8 / Zoom);
                bool selected = p == SelectedNodePath && n == SelectedNodeIndex;
                dc.DrawRectangle(selected ? ColorBrush("Accent") : Brushes.White, new Pen(Brushes.Black, 1 / Zoom), box);
            }
    }

    private static Rect Bounds(VectorDesignObject item)
    {
        if (item.Text is { } text)
        {
            var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("fa-IR"), FlowDirection.RightToLeft, new Typeface(item.FontFamily), item.FontSize, Brushes.Black, 1.0);
            return new Rect(item.TextX, item.TextY - item.FontSize * .8, Math.Max(item.FontSize * .4, formatted.WidthIncludingTrailingWhitespace), Math.Max(item.FontSize, formatted.Height));
        }
        var geometry = VectorDesignDocument.GeometryFor(item.Paths); if (geometry is null) return Rect.Empty;
        var bounds = geometry.Bounds; if (item.Stroke != "none" && item.StrokeWidth > 0) bounds.Inflate(item.StrokeWidth / 2, item.StrokeWidth / 2); return bounds;
    }

    private static Point[] Handles(Rect r) => new[]
    {
        new Point(r.Left,r.Top), new Point(r.Left+r.Width/2,r.Top), new Point(r.Right,r.Top), new Point(r.Right,r.Top+r.Height/2),
        new Point(r.Right,r.Bottom), new Point(r.Left+r.Width/2,r.Bottom), new Point(r.Left,r.Bottom), new Point(r.Left,r.Top+r.Height/2)
    };

    private static SolidColorBrush ColorBrush(string key) => (SolidColorBrush)(Application.Current?.FindResource(key) ?? Brushes.DarkOrange);
}
