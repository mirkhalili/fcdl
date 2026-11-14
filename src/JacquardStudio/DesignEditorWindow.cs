using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace JacquardStudio;

/// <summary>Transactional, layer-based raster design editor.</summary>
public sealed class DesignEditorWindow : Window
{
    private static readonly string[] ToolNames =
    {
        "قلم", "خط", "مستطیل", "مستطیل پر", "پرکردن", "قطره‌چکان", "انتخاب مستطیلی", "انتخاب آزاد",
        "جابه‌جایی انتخاب", "پاک‌کن", "کانتور رنگ", "بیضی", "بیضی پر", "انتخاب رنگ یکسان", "رسم کمان", "متن"
    };
    private static readonly string[] ToolPaths =
    {
        "M 4,19 L 7,13 17,3 21,7 11,17 Z M 7,13 L 11,17",
        "M 3,20 L 21,4",
        "M 3,4 L 21,4 21,20 3,20 Z",
        "M 3,4 L 21,4 21,20 3,20 Z M 5,18 L 19,6",
        "M 4,8 L 12,2 20,10 12,18 Z M 4,8 L 12,16 M 20,18 L 20,22",
        "M 5,20 L 9,19 20,8 16,4 5,15 Z M 14,6 L 18,10",
        "M 3,4 L 21,4 21,20 3,20 Z",
        "M 4,18 C -1,7 12,-1 20,7 C 26,14 18,21 10,20 L 4,18",
        "M 12,2 L 12,22 M 2,12 L 22,12 M 12,2 L 9,5 M 12,2 L 15,5 M 2,12 L 5,9 M 2,12 L 5,15",
        "M 3,15 L 13,4 21,11 13,20 8,20 Z M 9,9 L 17,16",
        "M 3,18 L 3,4 17,4 M 7,18 L 7,8 17,8 M 7,18 L 21,18",
        "M 12,3 C 0,3 0,21 12,21 C 24,21 24,3 12,3 Z",
        "M 12,3 C 0,3 0,21 12,21 C 24,21 24,3 12,3 Z M 5,17 L 18,6",
        "M 5,5 L 10,5 10,10 5,10 Z M 14,14 L 19,14 19,19 14,19 Z M 10,8 L 16,14",
        "M 3,18 C 3,-1 21,-1 21,18",
        "M 3,5 L 21,5 M 12,5 L 12,20 M 8,20 L 16,20"
    };
    private static readonly Color[] Palette =
    {
        Color.FromRgb(19, 25, 37), Colors.White, Color.FromRgb(76, 224, 211),
        Color.FromRgb(31, 108, 209), Color.FromRgb(44, 168, 100), Color.FromRgb(255, 196, 55),
        Color.FromRgb(241, 115, 83), Color.FromRgb(223, 57, 93), Color.FromRgb(158, 104, 219),
        Color.FromRgb(76, 86, 105), Color.FromRgb(117, 134, 153), Color.FromRgb(197, 210, 219),
        Color.FromRgb(79, 43, 35), Color.FromRgb(151, 85, 51), Color.FromRgb(94, 143, 137),
        Color.FromRgb(255, 143, 191)
    };
    private readonly List<Color> paletteColors = new(Palette);

    private DesignCanvas doc;
    private readonly BitmapSource input;
    private readonly DesignBlankSettings? blank;
    private readonly Image picture = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Image tracing = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Image selectionImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Polyline path = new() { StrokeThickness = 2, IsHitTestVisible = false };
    private readonly Grid view = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Cursor = Cursors.Cross, Background = Brushes.Transparent };
    private readonly TextBlock status = new() { Margin = new Thickness(10, 5, 10, 5), TextWrapping = TextWrapping.Wrap, FlowDirection = FlowDirection.RightToLeft };
    private readonly Slider brushSize = new() { Minimum = 1, Maximum = 32, Value = 2, TickFrequency = 1, IsSnapToTickEnabled = true, FlowDirection = FlowDirection.LeftToRight };
    private readonly Slider brushOpacity = new() { Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 5, IsSnapToTickEnabled = true, FlowDirection = FlowDirection.LeftToRight };
    private readonly Slider layerOpacity = new() { Minimum = 0, Maximum = 100, Value = 100, TickFrequency = 5, IsSnapToTickEnabled = true, FlowDirection = FlowDirection.LeftToRight };
    private readonly Slider traceOpacitySlider = new() { Minimum = 0, Maximum = 100, Value = 40, TickFrequency = 5, IsSnapToTickEnabled = true, FlowDirection = FlowDirection.LeftToRight };
    private readonly Slider gridInterval = new() { Minimum = 1, Maximum = 64, Value = 1, TickFrequency = 1, IsSnapToTickEnabled = true, FlowDirection = FlowDirection.LeftToRight };
    private readonly ComboBox arcMode = new() { ItemsSource = new[] { "نیم‌کمان بالا", "نیم‌کمان پایین", "نیم‌کمان راست", "نیم‌کمان چپ", "ربع بالا راست", "ربع پایین راست", "ربع پایین چپ", "ربع بالا چپ" }, SelectedIndex = 0, Margin = new Thickness(2), ToolTip = "محدوده رسم کمان" };
    private readonly TextBlock brushSizeValue = new(), brushOpacityValue = new(), layerOpacityValue = new(), traceOpacityValue = new(), gridIntervalValue = new(), zoomValue = new();
    private readonly PixelGridOverlay pixelGrid = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly DesignRuler horizontalRuler = new() { Horizontal = true, BaseStep = 50, IsHitTestVisible = false };
    private readonly DesignRuler verticalRuler = new() { Horizontal = false, BaseStep = 50, IsHitTestVisible = false };
    private readonly CheckBox gridToggle = new() { Content = "نمایش شبکه", IsChecked = false, Margin = new Thickness(2) };
    private readonly CheckBox repeatContour = new() { Content = "تکرار کانتور در راپورت", Margin = new Thickness(2) };
    private readonly ListBox layers = new() { Height = 158, Margin = new Thickness(0, 5, 0, 6), SelectionMode = SelectionMode.Single };
    private readonly TextBox layerName = new() { Margin = new Thickness(0, 3, 0, 4), ToolTip = "نام لایه فعال" };
    private readonly TextBox traceOpacityText = new() { Width = 52, Margin = new Thickness(3), FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox traceXText = new() { Width = 52, Margin = new Thickness(2), FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox traceYText = new() { Width = 52, Margin = new Thickness(2), FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox traceWText = new() { Width = 52, Margin = new Thickness(2), FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox traceHText = new() { Width = 52, Margin = new Thickness(2), FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox resizeWidthText = new() { Width = 78, FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox resizeHeightText = new() { Width = 78, FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox resizeXText = new() { Width = 78, Text = "0", FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox resizeYText = new() { Width = 78, Text = "0", FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox repeatColumnsText = new() { Width = 58, Text = "2", FlowDirection = FlowDirection.LeftToRight };
    private readonly TextBox repeatRowsText = new() { Width = 58, Text = "2", FlowDirection = FlowDirection.LeftToRight };
    private readonly List<DesignCanvas> undo = new(), redo = new();
    private readonly List<ToggleButton> toolButtons = new();
    private ScrollViewer? workspace;
    private Border? page;
    private Button? undoButton, redoButton;
    private TextBlock? colorHex;
    private Border? colorSample;
    private UniformGrid? paletteGrid;
    private bool updating;
    private bool addingSelection, subtractingSelection;
    private Color color = Colors.Black;
    private double zoom = 1;
    private int selectedTool;
    private (int X, int Y)? start, last;
    private readonly List<(int X, int Y)> pathPoints = new();
    private DesignCanvas? before;
    private DesignCanvas.Clip? clipboard;
    private string? tracePng;
    private bool traceVisible = true;
    private int traceAlpha = 40, traceX, traceY, traceW, traceH;

    public BitmapSource? Result { get; private set; }
    public DesignDocumentData? Document { get; private set; }

    public DesignEditorWindow(BitmapSource image, DesignDocumentData? saved = null, DesignBlankSettings? blank = null)
    {
        input = image;
        this.blank = blank;
        doc = saved is null ? new DesignCanvas(image) : new DesignCanvas(image, saved);
        if (saved is not null)
        {
            tracePng = saved.TracePng;
            traceVisible = saved.TraceVisible;
            traceAlpha = saved.TraceOpacity;
            traceX = saved.TraceX;
            traceY = saved.TraceY;
            traceW = saved.TraceWidth;
            traceH = saved.TraceHeight;
            if (traceW < 0 || traceH < 0 || traceW > 16000 || traceH > 16000 || Math.Abs((long)traceX) > 16000 || Math.Abs((long)traceY) > 16000)
                throw new InvalidDataException("ابعاد تصویر مرجع معتبر نیست.");
            if (traceAlpha < 0 || traceAlpha > 100) throw new InvalidDataException("شفافیت مرجع معتبر نیست.");
            if (tracePng is not null) tracing.Source = DesignCanvas.Decode(tracePng);
        }

        InstallTheme();
        Title = "استودیو طراحی طرح";
        Width = 1460;
        Height = 940;
        MinWidth = 1120;
        MinHeight = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        FlowDirection = FlowDirection.RightToLeft;
        Background = ThemeBrush("DesignBackground");
        Foreground = ThemeBrush("DesignText");

        var root = new DockPanel { LastChildFill = true, Background = ThemeBrush("DesignBackground"), FlowDirection = FlowDirection.LeftToRight };
        var topBar = BuildTopBar();
        DockPanel.SetDock(topBar, Dock.Top);
        root.Children.Add(topBar);

        var toolRail = BuildToolRail();
        DockPanel.SetDock(toolRail, Dock.Left);
        root.Children.Add(toolRail);

        var rightRail = BuildPropertiesRail();
        DockPanel.SetDock(rightRail, Dock.Right);
        root.Children.Add(rightRail);

        var footer = new Border
        {
            Background = ThemeBrush("DesignPanel"),
            BorderBrush = ThemeBrush("DesignBorder"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = status
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        root.Children.Add(BuildWorkspace());
        Content = root;
        PreviewKeyDown += Keys;
        Loaded += (_, _) => { FitPage(); UpdateGrid(); UpdateRulers(); };
        zoom = Math.Min(1, Math.Min(900.0 / doc.Width, 650.0 / doc.Height));
        Refresh();
    }

    private void InstallTheme()
    {
        Resources["DesignBackground"] = MakeBrush("#0A1321");
        Resources["DesignPanel"] = MakeBrush("#111E2D");
        Resources["DesignPanelRaised"] = MakeBrush("#18283A");
        Resources["DesignBorder"] = MakeBrush("#2A4054");
        Resources["DesignMuted"] = MakeBrush("#93A8BA");
        Resources["DesignText"] = MakeBrush("#E8F1F4");
        Resources["DesignAccent"] = MakeBrush("#4CE0D3");
        Resources["DesignAccentSoft"] = MakeBrush("#263F51");
        Resources["DesignDanger"] = MakeBrush("#613741");
        Resources["DesignCanvas"] = MakeBrush("#0B1522");
        Resources["DesignGrid"] = MakeBrush("#884CE0D3");
        // The application controls use DynamicResource keys. Override them only in this window
        // so native text boxes, combo boxes, check boxes and focus states share this palette.
        Resources["AppBackground"] = ThemeBrush("DesignBackground");
        Resources["CanvasBackground"] = ThemeBrush("DesignCanvas");
        Resources["PrimaryText"] = ThemeBrush("DesignText");
        Resources["Accent"] = ThemeBrush("DesignAccent");
        Resources["AccentForeground"] = ThemeBrush("DesignBackground");
        Resources["ButtonBackground"] = ThemeBrush("DesignPanelRaised");
        Resources["ButtonBorder"] = ThemeBrush("DesignBorder");
        Resources["ListBackground"] = ThemeBrush("DesignPanelRaised");
        Resources["Surface"] = ThemeBrush("DesignPanel");
        Resources["Border"] = ThemeBrush("DesignBorder");
        Resources["Muted"] = ThemeBrush("DesignMuted");
        Resources["GraphSelectedItem"] = ThemeBrush("DesignAccentSoft");
        Resources["ActionBackground"] = ThemeBrush("DesignAccent");
        Resources["SelectionFill"] = MakeBrush("#664CE0D3");
        Resources[SystemColors.HighlightBrushKey] = ThemeBrush("DesignAccent");
        Resources[SystemColors.ControlBrushKey] = ThemeBrush("DesignPanelRaised");
        Resources[SystemColors.ControlTextBrushKey] = ThemeBrush("DesignText");
    }

    private static SolidColorBrush MakeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private Brush ThemeBrush(string key) => (Brush)Resources[key];

    private WrapPanel BuildTopBar()
    {
        var bar = new WrapPanel
        {
            Name = "DesignTopBar",
            FlowDirection = FlowDirection.RightToLeft,
            Background = ThemeBrush("DesignPanel"),
            Margin = new Thickness(10, 10, 10, 7),
            VerticalAlignment = VerticalAlignment.Center
        };
        var brand = new Border
        {
            Background = ThemeBrush("DesignPanelRaised"),
            BorderBrush = ThemeBrush("DesignBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(2, 0, 8, 0),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "JACQUARD  /  DESIGN", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = ThemeBrush("DesignAccent") },
                    new TextBlock { Text = "استودیو طراحی", FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = ThemeBrush("DesignText") }
                }
            }
        };
        bar.Children.Add(brand);
        AddButton(bar, "＋ تصویر", ImportImage, tooltip: "واردکردن تصویر به‌صورت یک لایه جدید");
        AddButton(bar, "⇧ خروجی", ExportImage, tooltip: "ذخیره خروجی ترکیب‌شده به‌صورت BMP یا TIFF");
        AddSeparator(bar);
        undoButton = AddButton(bar, "↶ بازگشت", () => History(undo, redo), tooltip: "Ctrl+Z");
        redoButton = AddButton(bar, "↷ بازانجام", () => History(redo, undo), tooltip: "Ctrl+Y");
        AddSeparator(bar);
        AddButton(bar, "−", () => Zoom(zoom / 1.25), tooltip: "کوچک‌نمایی");
        zoomValue.Text = "100٪";
        zoomValue.Foreground = ThemeBrush("DesignAccent");
        zoomValue.FontWeight = FontWeights.Bold;
        zoomValue.VerticalAlignment = VerticalAlignment.Center;
        zoomValue.MinWidth = 48;
        zoomValue.TextAlignment = TextAlignment.Center;
        bar.Children.Add(zoomValue);
        AddButton(bar, "+", () => Zoom(zoom * 1.25), tooltip: "بزرگ‌نمایی");
        AddButton(bar, "برازش", FitPage, tooltip: "جای‌دادن بوم در فضای کاری");
        AddButton(bar, "۱۰۰٪", () => Zoom(1), tooltip: "اندازه واقعی پیکسل‌ها");
        AddSeparator(bar);
        AddButton(bar, "انصراف", () => DialogResult = false, danger: true);
        AddButton(bar, "اعمال و بستن", Apply, accent: true);
        return bar;
    }

    private Border AddSeparator(Panel panel)
    {
        var separator = new Border
        {
            Width = 1,
            Height = 27,
            Background = ThemeBrush("DesignBorder"),
            Margin = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(separator);
        return separator;
    }

    private Button AddButton(Panel panel, string label, Action action, bool accent = false, bool danger = false, string? tooltip = null)
    {
        var background = accent ? ThemeBrush("DesignAccent") : danger ? ThemeBrush("DesignDanger") : ThemeBrush("DesignPanelRaised");
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(3, 2, 3, 2),
            Padding = new Thickness(10, 5, 10, 5),
            MinHeight = 31,
            MinWidth = 34,
            Background = background,
            Foreground = accent ? ThemeBrush("DesignBackground") : ThemeBrush("DesignText"),
            BorderBrush = accent ? ThemeBrush("DesignAccent") : ThemeBrush("DesignBorder"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = tooltip,
            FontWeight = accent ? FontWeights.Bold : FontWeights.Normal
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        panel.Children.Add(button);
        return button;
    }

    private ScrollViewer BuildToolRail()
    {
        var rail = new StackPanel { Name = "DesignToolRail", Width = 60, FlowDirection = FlowDirection.RightToLeft, Margin = new Thickness(5, 4, 5, 8), HorizontalAlignment = HorizontalAlignment.Center };
        rail.Children.Add(new TextBlock { Text = "ابزار", Foreground = ThemeBrush("DesignMuted"), FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 8) });
        for (int i = 0; i < ToolPaths.Length; i++)
        {
            int index = i;
            var glyph = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(ToolPaths[i]),
                Width = 20,
                Height = 20,
                Stretch = Stretch.Uniform,
                Stroke = ThemeBrush(i == selectedTool ? "DesignAccent" : "DesignText"),
                StrokeThickness = 1.8,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent
            };
            if (i == 6) glyph.StrokeDashArray = new DoubleCollection { 2, 2 };
            var button = new ToggleButton
            {
                Name = $"DesignTool_{i}",
                Content = glyph,
                Width = 44,
                Height = 43,
                Margin = new Thickness(1, 2, 1, 2),
                Padding = new Thickness(5),
                Background = ThemeBrush("DesignPanel"),
                Foreground = ThemeBrush("DesignText"),
                BorderBrush = ThemeBrush("DesignBorder"),
                BorderThickness = new Thickness(1),
                IsChecked = i == selectedTool,
                ToolTip = ToolNames[i],
                Cursor = Cursors.Hand
            };
            AutomationProperties.SetName(button, ToolNames[i]);
            button.Click += (_, _) => SelectTool(index);
            toolButtons.Add(button);
            rail.Children.Add(button);
        }
        return new ScrollViewer
        {
            Width = 70,
            FlowDirection = FlowDirection.RightToLeft,
            Content = rail,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = ThemeBrush("DesignBackground")
        };
    }

    private void SelectTool(int index)
    {
        selectedTool = Math.Clamp(index, 0, ToolNames.Length - 1);
        for (int i = 0; i < toolButtons.Count; i++)
        {
            bool active = i == selectedTool;
            toolButtons[i].IsChecked = active;
            toolButtons[i].Background = active ? ThemeBrush("DesignAccentSoft") : ThemeBrush("DesignPanel");
            toolButtons[i].BorderBrush = active ? ThemeBrush("DesignAccent") : ThemeBrush("DesignBorder");
            if (toolButtons[i].Content is System.Windows.Shapes.Path glyph) glyph.Stroke = ThemeBrush(active ? "DesignAccent" : "DesignText");
        }
        view.Cursor = selectedTool == 15 ? Cursors.IBeam : Cursors.Cross;
        RefreshStatus();
    }

    private ScrollViewer BuildPropertiesRail()
    {
        var panel = new StackPanel { Name = "DesignPropertiesPanel", Margin = new Thickness(9, 5, 5, 8) };
        panel.Children.Add(BuildColorSection());
        panel.Children.Add(BuildBrushSection());
        panel.Children.Add(BuildLayerSection());
        panel.Children.Add(BuildSelectionSection());
        panel.Children.Add(BuildTransformSection());
        panel.Children.Add(BuildGridSection());
        panel.Children.Add(BuildTraceSection());
        panel.Children.Add(new TextBlock
        {
            Text = blank is null ? "" : $"بوم مستقل: {blank.Width} تار × {blank.Height} پود  ·  تراکم {blank.WarpDensity:0.##} / {blank.WeftDensity:0.##} در سانتی‌متر",
            Foreground = ThemeBrush("DesignMuted"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 6, 4, 12)
        });
        return new ScrollViewer
        {
            Name = "DesignRightRail",
            Width = 306,
            FlowDirection = FlowDirection.RightToLeft,
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = ThemeBrush("DesignBackground")
        };
    }

    private Border Section(string title, UIElement content)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = ThemeBrush("DesignAccent"),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(1, 0, 1, 7)
        });
        body.Children.Add(content);
        return new Border
        {
            Background = ThemeBrush("DesignPanel"),
            BorderBrush = ThemeBrush("DesignBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9),
            Margin = new Thickness(0, 0, 0, 8),
            Child = body
        };
    }

    private UIElement BuildColorSection()
    {
        colorSample = new Border
        {
            Width = 40,
            Height = 36,
            CornerRadius = new CornerRadius(5),
            BorderBrush = ThemeBrush("DesignBorder"),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(color),
            Margin = new Thickness(2)
        };
        colorHex = new TextBlock { Text = "#000000", Foreground = ThemeBrush("DesignText"), VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
        var currentRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        currentRow.Children.Add(colorSample);
        currentRow.Children.Add(colorHex);
        var pick = new Button
        {
            Content = "＋ افزودن رنگ دلخواه…",
            Margin = new Thickness(3, 2, 3, 2),
            Padding = new Thickness(8, 5, 8, 5),
            Background = ThemeBrush("DesignPanelRaised"),
            Foreground = ThemeBrush("DesignText"),
            BorderBrush = ThemeBrush("DesignBorder"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        pick.Click += (_, _) => ChooseColor();
        var content = new StackPanel();
        content.Children.Add(currentRow);
        content.Children.Add(pick);
        paletteGrid = new UniformGrid { Columns = 8, Margin = new Thickness(0, 5, 0, 0) };
        foreach (var swatch in paletteColors) AddPaletteSwatch(swatch);
        content.Children.Add(paletteGrid);
        return Section("رنگ و پالت", content);
    }

    private void AddPaletteSwatch(Color swatch)
    {
        if (paletteGrid is null) return;
        var button = new Button
        {
            Width = 25,
            Height = 24,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            Background = new SolidColorBrush(swatch),
            BorderBrush = ThemeBrush("DesignBorder"),
            BorderThickness = new Thickness(1),
            ToolTip = $"#{swatch.R:X2}{swatch.G:X2}{swatch.B:X2}",
            Cursor = Cursors.Hand
        };
        button.Click += (_, _) => { color = swatch; UpdateColor(); };
        paletteGrid.Children.Add(button);
    }

    private void AddPaletteColor(Color swatch)
    {
        if (paletteColors.Contains(swatch)) return;
        if (paletteColors.Count >= 32)
        {
            paletteColors.RemoveAt(Palette.Length);
            paletteGrid?.Children.RemoveAt(Palette.Length);
        }
        paletteColors.Add(swatch);
        AddPaletteSwatch(swatch);
    }

    private UIElement BuildBrushSection()
    {
        var content = new StackPanel();
        content.Children.Add(SliderRow("ضخامت قلم", brushSize, brushSizeValue, "2 px"));
        content.Children.Add(SliderRow("شفافیت قلم", brushOpacity, brushOpacityValue, "100٪"));
        brushSize.ValueChanged += (_, _) => brushSizeValue.Text = $"{(int)Math.Round(brushSize.Value)} px";
        brushOpacity.ValueChanged += (_, _) => brushOpacityValue.Text = $"{(int)Math.Round(brushOpacity.Value)}٪";
        content.Children.Add(new TextBlock { Text = "حالت کمان", Foreground = ThemeBrush("DesignMuted"), FontSize = 10, Margin = new Thickness(2, 4, 2, 1) });
        arcMode.Background = ThemeBrush("DesignPanelRaised");
        arcMode.Foreground = ThemeBrush("DesignText");
        arcMode.BorderBrush = ThemeBrush("DesignBorder");
        arcMode.MinHeight = 30;
        arcMode.SelectionChanged += (_, _) => arcModeIndex = Math.Clamp(arcMode.SelectedIndex, 0, ArcAngles.Length - 1);
        content.Children.Add(arcMode);
        repeatContour.Foreground = ThemeBrush("DesignText");
        content.Children.Add(repeatContour);
        return Section("قلم", content);
    }

    private UIElement SliderRow(string title, Slider slider, TextBlock value, string initial)
    {
        value.Text = initial;
        value.Foreground = ThemeBrush("DesignText");
        value.FontSize = 10;
        value.FontWeight = FontWeights.SemiBold;
        value.MinWidth = 40;
        value.TextAlignment = TextAlignment.Left;
        var header = new DockPanel();
        DockPanel.SetDock(value, Dock.Right);
        header.Children.Add(value);
        header.Children.Add(new TextBlock { Text = title, Foreground = ThemeBrush("DesignMuted"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        slider.Height = 23;
        slider.Margin = new Thickness(0, 1, 0, 5);
        slider.Foreground = ThemeBrush("DesignAccent");
        slider.ToolTip = title;
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(slider);
        return stack;
    }

    private UIElement BuildLayerSection()
    {
        var content = new StackPanel();
        layers.Background = ThemeBrush("DesignPanelRaised");
        layers.Foreground = ThemeBrush("DesignText");
        layers.BorderBrush = ThemeBrush("DesignBorder");
        layers.BorderThickness = new Thickness(1);
        layers.SelectionChanged += (_, _) =>
        {
            if (updating || layers.SelectedIndex < 0) return;
            doc.Active = doc.Layers.Count - 1 - layers.SelectedIndex;
            RefreshLayers();
            RefreshStatus();
        };
        content.Children.Add(layers);

        var actions = new WrapPanel();
        AddButton(actions, "+ لایه", () => Mutate(() => doc.AddLayer($"لایه {doc.Layers.Count + 1}")));
        AddButton(actions, "واردکردن", ImportImage, tooltip: "تصویر را به‌صورت لایه وارد کنید");
        AddButton(actions, "حذف", DeleteLayer);
        AddButton(actions, "↑", () => Mutate(() => doc.Reorder(1)), tooltip: "انتقال لایه به بالا");
        AddButton(actions, "↓", () => Mutate(() => doc.Reorder(-1)), tooltip: "انتقال لایه به پایین");
        AddButton(actions, "◉", () => Mutate(() => doc.Current.Visible = !doc.Current.Visible), tooltip: "نمایش یا پنهان‌کردن لایه");
        AddButton(actions, "🔒", () => Mutate(() => doc.Current.Locked = !doc.Current.Locked), tooltip: "قفل یا بازکردن لایه");
        content.Children.Add(actions);

        content.Children.Add(new TextBlock { Text = "نام لایه", Foreground = ThemeBrush("DesignMuted"), FontSize = 10, Margin = new Thickness(2, 5, 2, 0) });
        content.Children.Add(layerName);
        layerName.KeyDown += (_, e) => { if (e.Key == Key.Enter) { RenameLayer(); e.Handled = true; } };

        content.Children.Add(SliderRow("نمایانی لایه", layerOpacity, layerOpacityValue, "100٪"));
        layerOpacity.ValueChanged += (_, _) => layerOpacityValue.Text = $"{(int)Math.Round(layerOpacity.Value)}٪";
        var opacityActions = new WrapPanel();
        AddButton(opacityActions, "اعمال نام و شفافیت", RenameLayer, accent: true);
        content.Children.Add(opacityActions);
        return Section("لایه‌ها", content);
    }

    private UIElement BuildSelectionSection()
    {
        var content = new StackPanel();
        var row = new WrapPanel();
        AddButton(row, "همه", () => Mutate(doc.SelectAll));
        AddButton(row, "معکوس", () => Mutate(doc.InvertSelection));
        AddButton(row, "لغو انتخاب", () => Mutate(doc.SelectAll));
        content.Children.Add(row);
        var clipboardButtons = new WrapPanel();
        AddButton(clipboardButtons, "کپی", () => Copy());
        AddButton(clipboardButtons, "برش", () => { if (Copy()) Mutate(doc.ClearSelected); });
        AddButton(clipboardButtons, "چسباندن", Paste);
        AddButton(clipboardButtons, "پاک‌کردن پیکسل", () => Mutate(doc.ClearSelected), danger: true);
        content.Children.Add(clipboardButtons);
        var crop = new WrapPanel();
        AddButton(crop, "حذف بیرون انتخاب", () => Mutate(doc.ClearOutsideSelection), tooltip: "پیکسل‌های بیرون ناحیه انتخاب را شفاف می‌کند");
        content.Children.Add(crop);
        var hint = new TextBlock
        {
            Text = "برای افزودن به انتخاب، Shift را نگه دارید؛ برای کم‌کردن از انتخاب، Alt را نگه دارید.",
            Foreground = ThemeBrush("DesignMuted"),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(3, 4, 3, 0)
        };
        content.Children.Add(hint);
        return Section("انتخاب و کلیپ‌بورد", content);
    }

    private UIElement BuildTransformSection()
    {
        var content = new StackPanel();
        var sizeGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        sizeGrid.ColumnDefinitions.Add(new ColumnDefinition());
        sizeGrid.ColumnDefinitions.Add(new ColumnDefinition());
        sizeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sizeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        resizeWidthText.Text = doc.Width.ToString(CultureInfo.InvariantCulture);
        resizeHeightText.Text = doc.Height.ToString(CultureInfo.InvariantCulture);
        var widthField = MakeLabeledInput("عرض", resizeWidthText);
        var heightField = MakeLabeledInput("ارتفاع", resizeHeightText);
        var xField = MakeLabeledInput("جای‌گذاری X", resizeXText);
        var yField = MakeLabeledInput("جای‌گذاری Y", resizeYText);
        Grid.SetRow(widthField, 0); Grid.SetColumn(widthField, 0);
        Grid.SetRow(heightField, 0); Grid.SetColumn(heightField, 1);
        Grid.SetRow(xField, 1); Grid.SetColumn(xField, 0);
        Grid.SetRow(yField, 1); Grid.SetColumn(yField, 1);
        sizeGrid.Children.Add(widthField); sizeGrid.Children.Add(heightField); sizeGrid.Children.Add(xField); sizeGrid.Children.Add(yField);
        content.Children.Add(sizeGrid);
        var resizeButtons = new WrapPanel();
        AddButton(resizeButtons, "تغییر اندازه لایه", ResizeLayer, accent: true, tooltip: "تغییر مقیاس لایه فعال؛ اندازه بوم ثابت می‌ماند");
        content.Children.Add(resizeButtons);

        var transformButtons = new WrapPanel();
        AddButton(transformButtons, "↔ آینه افقی", () => Mutate(() => doc.FlipActiveLayer(true, false)));
        AddButton(transformButtons, "↕ آینه عمودی", () => Mutate(() => doc.FlipActiveLayer(false, true)));
        AddButton(transformButtons, "⟲ چرخش", () => Mutate(() => doc.RotateActiveLayer(-1)));
        AddButton(transformButtons, "⟳ چرخش", () => Mutate(() => doc.RotateActiveLayer(1)));
        AddButton(transformButtons, "ترکیب با زیرین", () => Mutate(doc.MergeDown), tooltip: "ادغام لایه فعال با لایه زیر آن");
        content.Children.Add(transformButtons);

        var align = new WrapPanel();
        AddButton(align, "چپ", () => Mutate(() => doc.AlignSelection(0, 1)), tooltip: "تراز انتخاب در سمت چپ بوم");
        AddButton(align, "مرکز", () => Mutate(() => doc.AlignSelection(1, 1)), tooltip: "تراز انتخاب در مرکز بوم");
        AddButton(align, "راست", () => Mutate(() => doc.AlignSelection(2, 1)), tooltip: "تراز انتخاب در سمت راست بوم");
        content.Children.Add(align);

        var repeatRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        repeatRow.Children.Add(FieldStack("ستون", repeatColumnsText));
        repeatRow.Children.Add(FieldStack("ردیف", repeatRowsText));
        content.Children.Add(repeatRow);
        var mirrorRow = new WrapPanel();
        var mirrorColumns = new CheckBox { Content = "آینه ستون‌های یکی‌درمیان", Foreground = ThemeBrush("DesignText"), Margin = new Thickness(3) };
        var mirrorRows = new CheckBox { Content = "آینه ردیف‌های یکی‌درمیان", Foreground = ThemeBrush("DesignText"), Margin = new Thickness(3) };
        mirrorRow.Children.Add(mirrorColumns); mirrorRow.Children.Add(mirrorRows);
        content.Children.Add(mirrorRow);
        var repeatButton = new WrapPanel();
        AddButton(repeatButton, "تکرار طرح در بوم", () => Mutate(() =>
        {
            if (!NumberInput.TryInt(repeatColumnsText.Text, out int columns) || !NumberInput.TryInt(repeatRowsText.Text, out int rows))
                throw new ArgumentException("تعداد ردیف و ستون باید عدد صحیح باشد.");
            doc.RepeatActiveLayer(columns, rows, mirrorColumns.IsChecked == true, mirrorRows.IsChecked == true);
        }), accent: true);
        content.Children.Add(repeatButton);
        return Section("تبدیل، تراز و تکرار", content);
    }

    private UIElement MakeLabeledInput(string label, TextBox inputBox) => FieldStack(label, inputBox);

    private StackPanel FieldStack(string label, TextBox inputBox)
    {
        var stack = new StackPanel { Margin = new Thickness(2) };
        stack.Children.Add(new TextBlock { Text = label, Foreground = ThemeBrush("DesignMuted"), FontSize = 10, Margin = new Thickness(1, 2, 1, 1) });
        StyleInput(inputBox);
        stack.Children.Add(inputBox);
        return stack;
    }

    private UIElement BuildGridSection()
    {
        var content = new StackPanel();
        gridToggle.Foreground = ThemeBrush("DesignText");
        gridToggle.Checked += (_, _) => SetGridVisible(true);
        gridToggle.Unchecked += (_, _) => SetGridVisible(false);
        content.Children.Add(gridToggle);
        content.Children.Add(SliderRow("فاصله خطوط اصلی (پیکسل)", gridInterval, gridIntervalValue, "1 px"));
        gridInterval.ValueChanged += (_, _) =>
        {
            gridIntervalValue.Text = $"{(int)Math.Round(gridInterval.Value)} px";
            pixelGrid.ConfiguredStep = (int)Math.Round(gridInterval.Value);
            pixelGrid.InvalidateVisual();
        };
        var note = new TextBlock { Text = "شبکه و خط‌کش فقط راهنمای دیداری هستند و در خروجی ذخیره نمی‌شوند.", Foreground = ThemeBrush("DesignMuted"), FontSize = 10, TextWrapping = TextWrapping.Wrap };
        content.Children.Add(note);
        return Section("شبکه و خط‌کش", content);
    }

    private UIElement BuildTraceSection()
    {
        var body = new StackPanel();
        var buttons = new WrapPanel();
        AddButton(buttons, "ورودی طرح", () => SetTrace(input), tooltip: "استفاده از تصویر ورودی به‌عنوان مرجع");
        AddButton(buttons, "بازکردن…", LoadTrace, tooltip: "بازکردن تصویر مرجع");
        AddButton(buttons, "نمایش/پنهان", () => { traceVisible = !traceVisible; Refresh(); });
        AddButton(buttons, "حذف مرجع", () => { tracePng = null; tracing.Source = null; Refresh(); }, danger: true);
        body.Children.Add(buttons);

        var geometry = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        traceXText.Text = traceX.ToString(CultureInfo.InvariantCulture);
        traceYText.Text = traceY.ToString(CultureInfo.InvariantCulture);
        traceWText.Text = (traceW == 0 ? doc.Width : traceW).ToString(CultureInfo.InvariantCulture);
        traceHText.Text = (traceH == 0 ? doc.Height : traceH).ToString(CultureInfo.InvariantCulture);
        geometry.Children.Add(traceXText); geometry.Children.Add(traceYText); geometry.Children.Add(traceWText); geometry.Children.Add(traceHText);
        AddButton(geometry, "اعمال X/Y/W/H", SetTraceGeometry, tooltip: "مختصات و اندازه تصویر مرجع برحسب پیکسل");
        body.Children.Add(new TextBlock { Text = "X · Y · عرض · ارتفاع", Foreground = ThemeBrush("DesignMuted"), FontSize = 10, Margin = new Thickness(2, 4, 2, 1) });
        body.Children.Add(geometry);

        var opacityRow = new StackPanel();
        traceOpacitySlider.Value = traceAlpha;
        opacityRow.Children.Add(SliderRow("شفافیت مرجع", traceOpacitySlider, traceOpacityValue, $"{traceAlpha}٪"));
        traceOpacitySlider.ValueChanged += (_, _) => traceOpacityValue.Text = $"{(int)Math.Round(traceOpacitySlider.Value)}٪";
        var opacityBox = new WrapPanel();
        traceOpacityText.Text = traceAlpha.ToString(CultureInfo.InvariantCulture);
        StyleInput(traceOpacityText);
        opacityBox.Children.Add(traceOpacityText);
        AddButton(opacityBox, "ثبت شفافیت", () =>
        {
            if (!NumberInput.TryInt(traceOpacityText.Text, out int value) || value is < 0 or > 100)
            {
                MessageBox.Show(this, "درصد شفافیت باید بین ۰ تا ۱۰۰ باشد.", "تصویر مرجع");
                return;
            }
            traceAlpha = value;
            traceOpacitySlider.Value = value;
            Refresh();
        });
        AddButton(opacityBox, "استفاده از نوار", () => { traceAlpha = (int)Math.Round(traceOpacitySlider.Value); traceOpacityText.Text = traceAlpha.ToString(CultureInfo.InvariantCulture); Refresh(); });
        opacityRow.Children.Add(opacityBox);
        body.Children.Add(opacityRow);
        body.Children.Add(new TextBlock { Text = "تصویر مرجع فقط روی صفحه دیده می‌شود و در تصویر خروجی حضور ندارد.", Foreground = ThemeBrush("DesignMuted"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 2, 0) });

        var expander = new Expander { Header = "تصویر مرجع (Tracing)", IsExpanded = false, Foreground = ThemeBrush("DesignText"), Content = body };
        return Section("مرجع", expander);
    }

    private Grid BuildWorkspace()
    {
        var grid = new Grid { Name = "DesignWorkspace", FlowDirection = FlowDirection.LeftToRight, Background = ThemeBrush("DesignBackground") };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(27) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var rulerCorner = new Border
        {
            Background = ThemeBrush("DesignPanelRaised"),
            BorderBrush = ThemeBrush("DesignBorder"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new TextBlock { Text = "px", Foreground = ThemeBrush("DesignAccent"), FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        grid.Children.Add(rulerCorner);
        horizontalRuler.BackgroundBrush = ThemeBrush("DesignPanelRaised");
        horizontalRuler.TickBrush = ThemeBrush("DesignMuted");
        horizontalRuler.TextBrush = ThemeBrush("DesignText");
        horizontalRuler.HorizontalAlignment = HorizontalAlignment.Stretch;
        horizontalRuler.Height = 27;
        Grid.SetColumn(horizontalRuler, 1);
        grid.Children.Add(horizontalRuler);

        verticalRuler.BackgroundBrush = ThemeBrush("DesignPanelRaised");
        verticalRuler.TickBrush = ThemeBrush("DesignMuted");
        verticalRuler.TextBrush = ThemeBrush("DesignText");
        verticalRuler.Width = 31;
        Grid.SetRow(verticalRuler, 1);
        grid.Children.Add(verticalRuler);

        pixelGrid.GridBrush = ThemeBrush("DesignGrid");
        view.ClipToBounds = true;
        view.Children.Add(picture);
        view.Children.Add(tracing);
        view.Children.Add(selectionImage);
        view.Children.Add(pixelGrid);
        path.Stroke = ThemeBrush("DesignAccent");
        view.Children.Add(path);
        foreach (var img in new[] { picture, tracing, selectionImage }) RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
        var checker = MakeCheckerboard();
        page = new Border
        {
            Name = "DesignCanvasPage",
            Child = view,
            BorderBrush = ThemeBrush("DesignAccent"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(28),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = checker,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 20, ShadowDepth = 3, Opacity = .45, Color = Color.FromRgb(0, 0, 0) }
        };
        workspace = new ScrollViewer
        {
            Name = "DesignCanvasScrollViewer",
            Content = page,
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = ThemeBrush("DesignCanvas"),
            Padding = new Thickness(0)
        };
        workspace.ScrollChanged += (_, _) => { UpdateGrid(); UpdateRulers(); };
        workspace.SizeChanged += (_, _) => { UpdateGrid(); UpdateRulers(); };
        grid.SizeChanged += (_, _) => { UpdateGrid(); UpdateRulers(); };
        Grid.SetRow(workspace, 1);
        Grid.SetColumn(workspace, 1);
        grid.Children.Add(workspace);
        view.MouseLeftButtonDown += Down;
        view.MouseMove += Move;
        view.MouseLeftButtonUp += Up;
        view.LostMouseCapture += (_, _) =>
        {
            if (start is null) return;
            if (before is not null) doc = before;
            before = null; start = last = null; pathPoints.Clear(); path.Points.Clear(); Refresh();
        };
        return grid;
    }

    private DrawingBrush MakeCheckerboard()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(ThemeBrush("DesignCanvas"), null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        var tile = new SolidColorBrush(Color.FromRgb(25, 39, 54));
        tile.Freeze();
        group.Children.Add(new GeometryDrawing(tile, null, new RectangleGeometry(new Rect(0, 0, 12, 12))));
        group.Children.Add(new GeometryDrawing(tile, null, new RectangleGeometry(new Rect(12, 12, 12, 12))));
        return new DrawingBrush(group) { Viewport = new Rect(0, 0, 24, 24), ViewportUnits = BrushMappingMode.Absolute, TileMode = TileMode.Tile, Stretch = Stretch.None };
    }

    private void StyleInput(TextBox box)
    {
        box.Margin = new Thickness(2);
        box.Padding = new Thickness(6, 4, 6, 4);
        box.MinHeight = 29;
        box.Background = ThemeBrush("DesignPanelRaised");
        box.Foreground = ThemeBrush("DesignText");
        box.BorderBrush = ThemeBrush("DesignBorder");
        box.BorderThickness = new Thickness(1);
        box.CaretBrush = ThemeBrush("DesignAccent");
        box.VerticalContentAlignment = VerticalAlignment.Center;
    }

    private void ChooseColor()
    {
        var chosen = ColorChooser.Pick(this, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
        if (chosen is null) return;
        try { color = (Color)ColorConverter.ConvertFromString(chosen); AddPaletteColor(color); UpdateColor(); }
        catch (FormatException) { MessageBox.Show(this, "کد رنگ معتبر نیست.", "رنگ"); }
    }

    private void UpdateColor()
    {
        if (colorSample is not null) colorSample.Background = new SolidColorBrush(color);
        if (colorHex is not null) colorHex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private Color Ink
    {
        get
        {
            byte alpha = (byte)Math.Clamp((int)Math.Round(color.A * brushOpacity.Value / 100.0), 0, 255);
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }
    }

    private int BrushSize => (int)Math.Clamp(Math.Round(brushSize.Value), 1, 32);

    private void ImportImage()
    {
        var picker = new OpenFileDialog { Filter = "تصویر|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif", CheckFileExists = true, Multiselect = false };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var image = ImageLoader.Load(picker.FileName, 0, true).Image;
            int x = (doc.Width - image.PixelWidth) / 2, y = (doc.Height - image.PixelHeight) / 2;
            string name = System.IO.Path.GetFileNameWithoutExtension(picker.FileName);
            if (string.IsNullOrWhiteSpace(name)) name = "تصویر واردشده";
            if (name.Length > 80) name = name[..80];
            Mutate(() => doc.ImportLayer(image, x, y, name));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException)
        {
            MessageBox.Show(this, ex.Message, "واردکردن تصویر", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportImage()
    {
        var picker = new SaveFileDialog
        {
            Title = "ذخیره خروجی طراحی",
            Filter = "Bitmap (*.bmp)|*.bmp|TIFF (*.tif)|*.tif|TIFF (*.tiff)|*.tiff",
            DefaultExt = ".bmp",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "design-export.bmp"
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            string extension = System.IO.Path.GetExtension(picker.FileName).ToLowerInvariant();
            bool tiff = extension is ".tif" or ".tiff";
            ImageExporter.Save(doc.Composite(), picker.FileName, tiff, Array.Empty<string>());
            status.Text = $"خروجی ذخیره شد: {picker.FileName}";
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "ذخیره خروجی", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteLayer()
    {
        if (MessageBox.Show(this, "لایه فعال حذف شود؟", "حذف لایه", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            Mutate(doc.DeleteLayer);
    }

    private void RenameLayer()
    {
        Mutate(() =>
        {
            string name = DesignCanvas.Name(layerName.Text);
            doc.Current.Name = name;
            doc.Current.Opacity = (int)Math.Round(layerOpacity.Value);
        });
    }

    private void ResizeLayer()
    {
        Mutate(() =>
        {
            if (!NumberInput.TryInt(resizeWidthText.Text, out int width) || !NumberInput.TryInt(resizeHeightText.Text, out int height) ||
                !NumberInput.TryInt(resizeXText.Text, out int x) || !NumberInput.TryInt(resizeYText.Text, out int y))
                throw new ArgumentException("عرض، ارتفاع و مختصات باید عدد صحیح باشند.");
            doc.ResizeActiveLayer(width, height, x, y);
        });
    }

    private void SetTraceGeometry()
    {
        if (!NumberInput.TryInt(traceXText.Text, out int x) || !NumberInput.TryInt(traceYText.Text, out int y) ||
            !NumberInput.TryInt(traceWText.Text, out int width) || !NumberInput.TryInt(traceHText.Text, out int height) ||
            Math.Abs((long)x) > 16000 || Math.Abs((long)y) > 16000 || width < 1 || height < 1 || width > 16000 || height > 16000)
        {
            MessageBox.Show(this, "مختصات باید بین ۱۶۰۰۰- و ۱۶۰۰۰ و ابعاد بین ۱ و ۱۶۰۰۰ باشد.", "تصویر مرجع");
            return;
        }
        traceX = x; traceY = y; traceW = width; traceH = height;
        Refresh();
    }

    private void SetGridVisible(bool visible)
    {
        pixelGrid.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateGrid();
    }

    private void Zoom(double next)
    {
        zoom = Math.Clamp(next, .05, 24);
        Refresh();
    }

    private void FitPage()
    {
        if (workspace is null || workspace.ViewportWidth <= 90 || workspace.ViewportHeight <= 90) return;
        double fitted = Math.Min((workspace.ViewportWidth - 80) / doc.Width, (workspace.ViewportHeight - 80) / doc.Height);
        Zoom(Math.Clamp(fitted, .05, 24));
    }

    private void UpdateGrid()
    {
        if (workspace is null || !view.IsLoaded || pixelGrid.Visibility != Visibility.Visible) return;
        try
        {
            Point origin = workspace.TranslatePoint(new Point(0, 0), view);
            pixelGrid.PixelsWide = doc.Width;
            pixelGrid.PixelsHigh = doc.Height;
            pixelGrid.Zoom = 1;
            pixelGrid.ConfiguredStep = (int)Math.Round(gridInterval.Value);
            pixelGrid.VisibleImage = new Rect(origin.X, origin.Y, Math.Max(0, workspace.ViewportWidth), Math.Max(0, workspace.ViewportHeight));
            pixelGrid.InvalidateVisual();
        }
        catch (InvalidOperationException) { }
    }

    private void UpdateRulers()
    {
        if (workspace is null || page is null || !workspace.IsLoaded) return;
        try
        {
            Point origin = view.TranslatePoint(new Point(0, 0), workspace);
            horizontalRuler.ImageLength = doc.Width;
            horizontalRuler.ZoomFactor = zoom;
            horizontalRuler.ScrollOffset = workspace.HorizontalOffset;
            horizontalRuler.ImageOrigin = workspace.HorizontalOffset + origin.X;
            horizontalRuler.InvalidateVisual();
            verticalRuler.ImageLength = doc.Height;
            verticalRuler.ZoomFactor = zoom;
            verticalRuler.ScrollOffset = workspace.VerticalOffset;
            verticalRuler.ImageOrigin = workspace.VerticalOffset + origin.Y;
            verticalRuler.InvalidateVisual();
        }
        catch (InvalidOperationException) { }
    }

    private void RefreshLayers()
    {
        updating = true;
        layers.Items.Clear();
        for (int i = doc.Layers.Count - 1; i >= 0; i--)
        {
            var layer = doc.Layers[i];
            string icon = layer.Locked ? "▣" : layer.Visible ? "◉" : "○";
            layers.Items.Add($"{icon}  {layer.Name}  ·  {layer.Opacity}٪");
        }
        layers.SelectedIndex = doc.Layers.Count - 1 - doc.Active;
        layerName.Text = doc.Current.Name;
        layerOpacity.Value = doc.Current.Opacity;
        layerOpacityValue.Text = $"{doc.Current.Opacity}٪";
        updating = false;
    }

    private void Refresh()
    {
        picture.Source = doc.Composite();
        tracing.Visibility = traceVisible && tracePng is not null ? Visibility.Visible : Visibility.Collapsed;
        tracing.Opacity = traceAlpha / 100.0;
        tracing.Width = (traceW == 0 ? doc.Width : traceW) * zoom;
        tracing.Height = (traceH == 0 ? doc.Height : traceH) * zoom;
        tracing.RenderTransform = new TranslateTransform(traceX * zoom, traceY * zoom);
        view.Width = Math.Max(1, doc.Width * zoom);
        view.Height = Math.Max(1, doc.Height * zoom);
        path.StrokeThickness = Math.Max(1, zoom * 1.5);
        RefreshSelection();
        RefreshLayers();
        RefreshStatus();
        zoomValue.Text = $"{zoom * 100:0}%";
        undoButton!.IsEnabled = undo.Count > 0;
        redoButton!.IsEnabled = redo.Count > 0;
        Dispatcher.BeginInvoke(new Action(() => { UpdateGrid(); UpdateRulers(); }));
    }

    private void RefreshStatus()
    {
        status.Text = $"{doc.Width} × {doc.Height} px    ·    زوم {zoom * 100:0}%    ·    انتخاب {doc.SelectedCount:N0} px    ·    لایه فعال: {doc.Current.Name}    ·    {ToolNames[selectedTool]}";
    }

    private void RefreshSelection()
    {
        if (doc.Selection is null) { selectionImage.Source = null; return; }
        int width = doc.Width, height = doc.Height;
        byte[] pixels = new byte[checked(width * height * 4)];
        var mask = doc.Selection;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int p = y * width + x;
                if (!mask[p]) continue;
                if (x > 0 && x < width - 1 && y > 0 && y < height - 1 && mask[p - 1] && mask[p + 1] && mask[p - width] && mask[p + width]) continue;
                int i = p * 4;
                pixels[i] = 0; pixels[i + 1] = 230; pixels[i + 2] = 255; pixels[i + 3] = 240;
            }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        selectionImage.Source = bitmap;
    }

    private (int X, int Y)? Pixel(MouseEventArgs e)
    {
        var point = e.GetPosition(view);
        return GraphImageOperations.FindPixel(doc.Width, doc.Height, point.X, point.Y, view.ActualWidth, view.ActualHeight);
    }

    private void Push(List<DesignCanvas> list, DesignCanvas snapshot)
    {
        list.Add(snapshot);
        long sum = list.Sum(item => item.MemoryBytes);
        while (list.Count > 1 && sum > 96L * 1024 * 1024)
        {
            sum -= list[0].MemoryBytes;
            list.RemoveAt(0);
        }
        while (list.Count > 20) list.RemoveAt(0);
    }

    private void Mutate(Action action)
    {
        if (start is not null) return;
        var old = doc.Clone();
        try
        {
            action();
            Push(undo, old);
            redo.Clear();
            Refresh();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            doc = old;
            Refresh();
            MessageBox.Show(this, ex.Message, "طراحی", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void History(List<DesignCanvas> from, List<DesignCanvas> to)
    {
        if (start is not null || from.Count == 0) return;
        Push(to, doc.Clone());
        doc = from[^1];
        from.RemoveAt(from.Count - 1);
        Refresh();
    }

    private bool Copy()
    {
        try
        {
            clipboard = doc.Copy();
            status.Text = "ناحیه لایه فعال در کلیپ‌بورد داخلی کپی شد.";
            return true;
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "کلیپ‌بورد");
            return false;
        }
    }

    private void Paste()
    {
        if (clipboard is null) { MessageBox.Show(this, "ابتدا یک ناحیه را کپی کنید.", "کلیپ‌بورد"); return; }
        Mutate(() => { doc.AddLayer("ناحیه چسبانده‌شده"); doc.Paste(clipboard, clipboard.X, clipboard.Y); });
    }

    private void Apply()
    {
        try
        {
            var result = doc.Composite();
            var saved = doc.Save();
            saved.TracePng = tracePng;
            saved.TraceOpacity = traceAlpha;
            saved.TraceVisible = traceVisible;
            saved.TraceX = traceX; saved.TraceY = traceY; saved.TraceWidth = traceW; saved.TraceHeight = traceH;
            Result = result;
            Document = saved;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or OverflowException)
        {
            MessageBox.Show(this, ex.Message, "خطای ذخیره طراحی", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetTrace(BitmapSource bitmap)
    {
        try
        {
            if ((long)bitmap.PixelWidth * bitmap.PixelHeight > DesignProcessor.MaxPixels)
                throw new ArgumentException("تصویر مرجع باید حداکثر چهار میلیون پیکسل باشد.");
            tracePng = ModuleExecutor.EmbedImage(bitmap);
            tracing.Source = bitmap;
            traceVisible = true;
            Refresh();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "تصویر مرجع");
        }
    }

    private void LoadTrace()
    {
        var picker = new OpenFileDialog { Filter = "تصویر|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.gif", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        try { SetTrace(ImageLoader.Load(picker.FileName, 0, true).Image); }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException)
        { MessageBox.Show(this, ex.Message, "تصویر مرجع"); }
    }

    private string? PromptText()
    {
        var dialog = new Window
        {
            Title = "افزودن متن",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 420,
            Height = 250,
            MinWidth = 360,
            MinHeight = 210,
            ResizeMode = ResizeMode.NoResize,
            FlowDirection = FlowDirection.RightToLeft,
            Background = ThemeBrush("DesignBackground"),
            Foreground = ThemeBrush("DesignText")
        };
        var root = new DockPanel { Margin = new Thickness(14) };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        var accept = new Button { Content = "افزودن متن", Padding = new Thickness(12, 6, 12, 6), Background = ThemeBrush("DesignAccent"), Foreground = ThemeBrush("DesignBackground"), BorderBrush = ThemeBrush("DesignAccent"), BorderThickness = new Thickness(1), IsDefault = true };
        var cancel = new Button { Content = "انصراف", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(5, 0, 0, 0), Background = ThemeBrush("DesignPanelRaised"), Foreground = ThemeBrush("DesignText"), BorderBrush = ThemeBrush("DesignBorder"), BorderThickness = new Thickness(1), IsCancel = true };
        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Children.Add(accept); buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        var inputBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 512, FontSize = 16, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        StyleInput(inputBox);
        root.Children.Add(inputBox);
        accept.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(inputBox.Text)) dialog.DialogResult = true; else MessageBox.Show(dialog, "متن را وارد کنید.", "افزودن متن"); };
        dialog.Content = root;
        dialog.Loaded += (_, _) => inputBox.Focus();
        return dialog.ShowDialog() == true ? inputBox.Text : null;
    }

    private void Keys(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.Z) { History(undo, redo); e.Handled = true; }
            else if (e.Key == Key.Y) { History(redo, undo); e.Handled = true; }
            else if (e.Key == Key.C) { Copy(); e.Handled = true; }
            else if (e.Key == Key.X) { if (Copy()) Mutate(doc.ClearSelected); e.Handled = true; }
            else if (e.Key == Key.V) { Paste(); e.Handled = true; }
            else if (e.Key == Key.A) { Mutate(doc.SelectAll); e.Handled = true; }
        }
        else if (e.Key == Key.Delete) { Mutate(doc.ClearSelected); e.Handled = true; }
        else if (e.Key == Key.Escape && start is not null)
        {
            if (before is not null) doc = before;
            before = null; start = last = null; pathPoints.Clear(); path.Points.Clear();
            view.ReleaseMouseCapture(); Refresh(); e.Handled = true;
        }
    }

    private void Down(object sender, MouseButtonEventArgs e)
    {
        var point = Pixel(e);
        if (point is null) return;
        int t = selectedTool;
        if (t == 5) { color = doc.At(point.Value.X, point.Value.Y); UpdateColor(); return; }
        if (t == 13) { Mutate(() => doc.SelectColor(point.Value.X, point.Value.Y)); return; }
        if (t == 4) { Mutate(() => doc.Fill(point.Value.X, point.Value.Y, Ink)); return; }
        if (t == 10)
        {
            var target = doc.At(point.Value.X, point.Value.Y);
            Mutate(() => doc.Contour(target, Ink, BrushSize, repeatContour.IsChecked == true));
            return;
        }
        if (t == 15)
        {
            if (doc.Current.Locked) { MessageBox.Show(this, "لایه قفل است.", "طراحی"); return; }
            string? text = PromptText();
            if (!string.IsNullOrWhiteSpace(text)) Mutate(() => doc.DrawText(point.Value.X, point.Value.Y, text, Ink, Math.Clamp(BrushSize * 4, 6, 128)));
            return;
        }
        if (t is 0 or 1 or 2 or 3 or 9 or 11 or 12 or 14 || t is 6 or 7 or 8)
        {
            if ((t is 0 or 1 or 2 or 3 or 9 or 11 or 12 or 14 or 8) && doc.Current.Locked)
            { MessageBox.Show(this, "لایه قفل است.", "طراحی"); return; }
            before = doc.Clone();
        }
        addingSelection = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        subtractingSelection = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        start = last = point;
        pathPoints.Clear(); path.Points.Clear();
        if (t == 7) AddPath(point.Value);
        if (t is 0 or 1 or 2 or 3 or 9 or 11 or 12 or 14)
            doc.Line(point.Value.X, point.Value.Y, point.Value.X, point.Value.Y, t == 9 ? Colors.Transparent : Ink, BrushSize);
        view.CaptureMouse();
        Refresh();
        e.Handled = true;
    }

    private void AddPath((int X, int Y) point)
    {
        if (pathPoints.Count >= 2048 || pathPoints.Count > 0 && pathPoints[^1] == point) return;
        pathPoints.Add(point);
        path.Points.Add(new Point((point.X + .5) * zoom, (point.Y + .5) * zoom));
    }

    private void Move(object sender, MouseEventArgs e)
    {
        if (start is null || e.LeftButton != MouseButtonState.Pressed) return;
        var point = Pixel(e);
        if (point is null) return;
        int t = selectedTool;
        if (t == 7) { AddPath(point.Value); last = point; return; }
        if ((t is 0 or 9) && last is { } previous)
            doc.Line(previous.X, previous.Y, point.Value.X, point.Value.Y, t == 9 ? Colors.Transparent : Ink, BrushSize);
        else if ((t is 1 or 2 or 3 or 11 or 12 or 14) && before is not null)
            DrawShape(point.Value);
        last = point;
        if (t is 0 or 1 or 2 or 3 or 9 or 11 or 12 or 14) Refresh();
    }

    private void DrawShape((int X, int Y) point)
    {
        if (before is null || start is null) return;
        int t = selectedTool;
        doc = before.Clone();
        if (t == 1) doc.Line(start.Value.X, start.Value.Y, point.X, point.Y, Ink, BrushSize);
        else if (t == 14) DrawArc(start.Value.X, start.Value.Y, point.X, point.Y);
        else if (t is 11 or 12) doc.Ellipse(start.Value.X, start.Value.Y, point.X, point.Y, Ink, BrushSize, t == 12);
        else doc.Rectangle(start.Value.X, start.Value.Y, point.X, point.Y, Ink, BrushSize, t == 3);
    }

    private void DrawArc(int x0, int y0, int x1, int y1)
    {
        // The arc mode selector lives in the right-hand properties rail.
        var mode = arcModeIndex;
        var angle = ArcAngles[mode];
        doc.ArcSection(x0, y0, x1, y1, Ink, BrushSize, angle.Start, angle.Sweep);
    }

    private static readonly (double Start, double Sweep)[] ArcAngles =
    {
        (180, 180), (0, 180), (270, 180), (90, 180), (270, 90), (0, 90), (90, 90), (180, 90)
    };
    private int arcModeIndex;

    private void Up(object sender, MouseButtonEventArgs e)
    {
        if (start is null) return;
        var begin = start.Value;
        var point = Pixel(e) ?? last ?? begin;
        int t = selectedTool;
        try
        {
            if ((t is 0 or 9) && last is { } previous && previous != point)
                doc.Line(previous.X, previous.Y, point.X, point.Y, t == 9 ? Colors.Transparent : Ink, BrushSize);
            if (t is 1 or 2 or 3 or 11 or 12 or 14) DrawShape(point);
            if (t == 6)
            {
                if (addingSelection || subtractingSelection) doc.CombineRectangleSelection(begin.X, begin.Y, point.X, point.Y, subtractingSelection);
                else doc.SelectRectangle(begin.X, begin.Y, point.X, point.Y);
            }
            else if (t == 7) { AddPath(point); doc.SelectPolygon(pathPoints); }
            else if (t == 8) doc.MoveSelection(point.X - begin.X, point.Y - begin.Y);
            if (before is not null) { Push(undo, before); redo.Clear(); }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            if (before is not null) doc = before;
            MessageBox.Show(this, ex.Message, "طراحی", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        before = null; start = last = null; pathPoints.Clear(); path.Points.Clear();
        view.ReleaseMouseCapture();
        Refresh();
        e.Handled = true;
    }
}
