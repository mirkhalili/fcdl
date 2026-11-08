using HelixToolkit.Wpf;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Rotatable geometric yarn sample; diameter is estimated, not a mechanical simulation.</summary>
public sealed class Fabric3DWindow : Window
{
    public const int MaxThreads = 32;
    public BitmapSource? Captured { get; private set; }
    public SimulationSettings? CapturedSettings { get; private set; }
    public Fabric3DWindow(BitmapSource fabric, IReadOnlyList<string> rowWefts, SimulationSettings settings, bool english, string[]? weftColors = null, FabricOverview? overview = null)
    {
        FabricSimulation.Validate(settings);
        SetResourceReference(FontFamilyProperty, "VazirFont");
        string T(string fa, string en) => english ? en : fa;
        Title = T("نمای سه‌بعدی بافت", "3D weave detail"); Width = 1200; Height = 760; MinWidth = 740; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(24, 37, 45)) };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), Foreground = Brushes.White };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(6), FlowDirection = FlowDirection.LeftToRight };
        var left = new Button { Content = "◀ X", Margin = new Thickness(3) }; var right = new Button { Content = "X ▶", Margin = new Thickness(3) };
        var up = new Button { Content = "▲ Y", Margin = new Thickness(3) }; var down = new Button { Content = "Y ▼", Margin = new Thickness(3) }; var flip = new Button { Content = T("پشت / رو", "Front / back"), Margin = new Thickness(3) };
        bar.Children.Add(left); bar.Children.Add(right); bar.Children.Add(up); bar.Children.Add(down); bar.Children.Add(flip); DockPanel.SetDock(bar, Dock.Bottom); root.Children.Add(bar);
        var side = new StackPanel { Margin = new Thickness(12), FlowDirection = english ? FlowDirection.LeftToRight : FlowDirection.RightToLeft };
        var scroll = new ScrollViewer { Width = 270, Content = side, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = new SolidColorBrush(Color.FromRgb(17, 27, 41)) }; DockPanel.SetDock(scroll, Dock.Right); root.Children.Add(scroll);
        var viewport = new HelixViewport3D { ShowViewCube = true, ShowCoordinateSystem = false, Background = new SolidColorBrush(Color.FromRgb(35, 48, 55)) };
        root.Children.Add(viewport); Content = root;
        TextBlock Label(string text) => new() { FontFamily = FontFamily, Text = text, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 3) };
        TextBox Input(string value) => new() { FontFamily = FontFamily, Text = value, FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(6) };
        ComboBox Unit() => new() { FontFamily = FontFamily, ItemsSource = YarnGeometry.Units, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 0) };
        side.Children.Add(new TextBlock { Text = T("تنظیم نخ و تراکم", "Yarn & density"), Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.SemiBold });
        side.Children.Add(Label(T("نمره نخ تار", "Warp yarn count"))); var warpCount = Input("30"); side.Children.Add(warpCount); var warpUnit = Unit(); side.Children.Add(warpUnit);
        side.Children.Add(Label(T("نمره نخ پود", "Weft yarn count"))); var weftCount = Input("30"); side.Children.Add(weftCount); var weftUnit = Unit(); side.Children.Add(weftUnit);
        side.Children.Add(Label(T("تراکم تار (نخ در سانتی‌متر)", "Warp ends per cm"))); var warpDensity = Input("20"); side.Children.Add(warpDensity);
        side.Children.Add(Label(T("تراکم پود (رج در سانتی‌متر)", "Weft picks per cm"))); var weftDensity = Input("20"); side.Children.Add(weftDensity);
        side.Children.Add(Label(T("چگالی مؤثر نخ (گرم بر سانتی‌متر مکعب)", "Effective yarn density (g/cm³)"))); var materialDensity = Input("1.0"); side.Children.Add(materialDensity);
        side.Children.Add(Label(T("رنگ تار", "Warp color"))); var warpColor = Input(settings.WarpColor); side.Children.Add(warpColor);
        var chooseWarp = new Button { Content = T("انتخاب رنگ تار", "Choose warp color"), Margin = new Thickness(0, 5, 0, 0) }; side.Children.Add(chooseWarp);
        chooseWarp.Click += (_, _) => { var selected = ColorChooser.Pick(this, warpColor.Text); if (selected is not null) warpColor.Text = selected; };
        var rowIds = rowWefts.Count == fabric.PixelHeight ? rowWefts.Distinct(StringComparer.Ordinal).ToArray() : overview is not null ? new[] { "1" } : Array.Empty<string>();
        var colorSet = weftColors is { Length: > 0 } ? (string[])weftColors.Clone() : rowIds.Length > 0 ? rowIds.Select(id => FabricSimulation.YarnFor(settings, id).Color).ToArray() : new[] { settings.WeftColor };
        if (colorSet.Length > 256) throw new ArgumentException("At most 256 weft colors are supported.");
        var colorInputs = new List<TextBox>();
        for (int i = 0; i < colorSet.Length; i++)
        {
            int n = i; side.Children.Add(Label(T($"رنگ پود {(i < rowIds.Length ? rowIds[i] : (i + 1).ToString())}", $"Weft {(i < rowIds.Length ? rowIds[i] : (i + 1).ToString())} color"))); var input = Input(colorSet[i]); colorInputs.Add(input); side.Children.Add(input);
            var choose = new Button { Content = T("انتخاب رنگ", "Choose color"), Margin = new Thickness(0, 5, 0, 0) }; side.Children.Add(choose);
            choose.Click += (_, _) => { var selected = ColorChooser.Pick(this, colorInputs[n].Text); if (selected is not null) colorInputs[n].Text = selected; };
        }
        var apply = new Button { Content = T("اعمال تنظیمات", "Apply settings"), Margin = new Thickness(0, 12, 0, 0) }; side.Children.Add(apply);
        side.Children.Add(new TextBlock { Text = T("قطر از نمره و چگالی مؤثر تخمین زده می‌شود؛ نتیجه اندازه‌گیری فیزیکی نیست.", "Diameter is estimated from count and effective density; this is not a physical measurement."), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 10, 0, 0), FontSize = 11 });
        int offsetX = 0, offsetY = 0; bool back = false;
        double warpSpacing = .5, weftSpacing = .5, warpDiameter = YarnGeometry.DiameterMm(30, 1), weftDiameter = YarnGeometry.DiameterMm(30, 1); Color warp = FabricSimulation.ParseColor(settings.WarpColor); Color[] colors = colorSet.Select(FabricSimulation.ParseColor).ToArray();
        if (overview is not null)
        {
            var capture = new Button { Content = T("ثبت نمای 3D", "Capture 3D view"), Margin = new Thickness(3) }; bar.Children.Add(capture);
            capture.Click += (_, _) =>
            {
                int cw = Math.Max(1, (int)Math.Ceiling(viewport.ActualWidth)), ch = Math.Max(1, (int)Math.Ceiling(viewport.ActualHeight));
                if ((long)cw * ch > 4_000_000) { MessageBox.Show(this, T("پنجره را کوچک‌تر کنید.", "Make the window smaller.")); return; }
                var bitmap = new RenderTargetBitmap(cw, ch, 96, 96, PixelFormats.Pbgra32); bitmap.Render(viewport); bitmap.Freeze(); Captured = bitmap;
                static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                var savedWefts = (settings.Wefts ?? Array.Empty<SimulatedYarn>()).ToDictionary(v => v.Id, StringComparer.Ordinal);
                for (int i = 0; i < rowIds.Length; i++) { var item = FabricSimulation.YarnFor(settings, rowIds[i]); savedWefts[rowIds[i]] = item with { Color = Hex(colors[i]) }; }
                CapturedSettings = settings with { WarpColor = Hex(warp), WeftColor = rowIds.Length == 0 ? Hex(colors[0]) : settings.WeftColor, Wefts = savedWefts.Count == 0 ? settings.Wefts : savedWefts.Values.ToArray() };
                DialogResult = true;
            };
        }
        void RenderPart()
        {
            int w = Math.Min(MaxThreads, fabric.PixelWidth - offsetX), h = Math.Min(MaxThreads, fabric.PixelHeight - offsetY);
            var crop = new CroppedBitmap(fabric, new Int32Rect(offsetX, offsetY, w, h)); crop.Freeze(); byte[] map = DesignAnalysis.ReadBinary(crop);
            var group = new Model3DGroup(); double scaleX = overview?.ScaleX ?? 1, scaleY = overview?.ScaleY ?? 1;
            double dx = warpSpacing * scaleX, dy = weftSpacing * scaleY, wd = warpDiameter * scaleX, fd = weftDiameter * scaleY;
            for (int x = 0; x < w; x++) group.Children.Add(BuildYarn(map, w, h, true, x, warp, settings.WarpYarn, settings.Weave, dx, dy, wd, fd));
            for (int y = 0; y < h; y++)
            {
                string id = rowWefts.Count == fabric.PixelHeight ? rowWefts[offsetY + y] : "1"; var yarn = FabricSimulation.YarnFor(settings, id);
                int colorIndex = weftColors is { Length: > 0 } || rowIds.Length == 0 ? (offsetY + y) % colors.Length : Array.IndexOf(rowIds, id);
                Color color = colors[colorIndex];
                group.Children.Add(BuildYarn(map, w, h, false, y, color, yarn.Yarn, settings.Weave, dx, dy, wd, fd));
            }
            viewport.Children.Clear(); viewport.Children.Add(new DefaultLights()); viewport.Children.Add(new ModelVisual3D { Content = group });
            double widthMm = w * dx, heightMm = h * dy, span = Math.Max(widthMm, heightMm);
            double cameraZ = span * (back ? -1.15 : 1.15);
            viewport.Camera = new PerspectiveCamera(new Point3D(widthMm * .7, -heightMm * .9, cameraZ), new Vector3D(-widthMm * .7, heightMm * .9, -cameraZ), new Vector3D(0, 0, back ? -1 : 1), 42);
            hint.Text = (english ? $"Warp {offsetX + 1}–{offsetX + w} of {fabric.PixelWidth}; row {offsetY + 1}–{offsetY + h} of {fabric.PixelHeight}." : $"تار {offsetX + 1} تا {offsetX + w} از {fabric.PixelWidth}؛ رج {offsetY + 1} تا {offsetY + h} از {fabric.PixelHeight}.") + T($" قطر تقریبی تار {warpDiameter:F3} و پود {weftDiameter:F3} میلی‌متر.", $" Estimated diameters: warp {warpDiameter:F3} mm, weft {weftDiameter:F3} mm.") + (warpDiameter > warpSpacing || weftDiameter > weftSpacing ? T(" هشدار: نخ‌های مجاور هم‌پوشانی دارند.", " Warning: adjacent yarns overlap.") : "");
            if (overview is not null) hint.Text = T($"نمای کلی {overview.OriginalWidth} تار × {overview.OriginalHeight} رج؛ نمونه {w} × {h}. نخ‌ها نماینده‌اند و جزئیات کاهش یافته است. ", $"Full overview {overview.OriginalWidth} × {overview.OriginalHeight}; sample {w} × {h}. Yarns are representative. ") + hint.Text;
            left.IsEnabled = offsetX > 0; up.IsEnabled = offsetY > 0; right.IsEnabled = offsetX + w < fabric.PixelWidth; down.IsEnabled = offsetY + h < fabric.PixelHeight;
        }
        static double Parse(TextBox box) { if (!double.TryParse(NumberInput.Normalize(box.Text).Replace('٫', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) throw new ArgumentException("Invalid number."); return n; }
        apply.Click += (_, _) =>
        {
            try
            {
                double density = Parse(materialDensity); double wd = YarnGeometry.DiameterMm(YarnGeometry.ToTex(Parse(warpCount), warpUnit.SelectedIndex), density), fd = YarnGeometry.DiameterMm(YarnGeometry.ToTex(Parse(weftCount), weftUnit.SelectedIndex), density);
                double ws = YarnGeometry.SpacingMm(Parse(warpDensity)), fs = YarnGeometry.SpacingMm(Parse(weftDensity)); var wc = FabricSimulation.ParseColor(warpColor.Text); var cs = colorInputs.Select(x => FabricSimulation.ParseColor(x.Text)).ToArray();
                warpDiameter = wd; weftDiameter = fd; warpSpacing = ws; weftSpacing = fs; warp = wc; colors = cs; RenderPart();
            }
            catch (ArgumentException ex) { MessageBox.Show(this, T("تنظیمات معتبر نیست: ", "Invalid settings: ") + ex.Message, Title); }
        };
        left.Click += (_, _) => { offsetX = Math.Max(0, offsetX - MaxThreads); RenderPart(); }; right.Click += (_, _) => { offsetX += MaxThreads; RenderPart(); };
        up.Click += (_, _) => { offsetY = Math.Max(0, offsetY - MaxThreads); RenderPart(); }; down.Click += (_, _) => { offsetY += MaxThreads; RenderPart(); };
        flip.Click += (_, _) => { back = !back; RenderPart(); }; RenderPart();
    }
    public static BitmapSource PatternBitmap(WeavePattern pattern)
    {
        if (pattern.Width < 1 || pattern.Height < 1 || (long)pattern.Width * pattern.Height > DesignProcessor.MaxPatternPixels || pattern.Values.Length != (long)pattern.Width * pattern.Height || pattern.Values.Any(v => v != 0 && v != 255)) throw new ArgumentException("Invalid binary weave.");
        var image = BitmapSource.Create(pattern.Width, pattern.Height, 96, 96, PixelFormats.Gray8, null, pattern.Values, pattern.Width); image.Freeze(); return image;
    }
    public static void ShowPattern(Window owner, WeavePattern pattern, bool english, string[]? colors = null)
    {
        var window = new Fabric3DWindow(PatternBitmap(pattern), Array.Empty<string>(), new SimulationSettings(Weave: 0), english, colors) { Owner = owner }; window.ShowDialog();
    }
    private static GeometryModel3D BuildYarn(byte[] map, int w, int h, bool warp, int index, Color color, int yarn, int weave, double dx, double dy, double warpDiameter, double weftDiameter)
    {
        int length = warp ? h : w, segments = length * 4, sides = 8; double radius = (warp ? warpDiameter : weftDiameter) / 2;
        var mesh = new MeshGeometry3D();
        double HeightAt(int n)
        {
            n = Math.Clamp(n, 0, length - 1); int x = warp ? index : n, y = warp ? n : index;
            bool top = FabricSimulation.WarpOnTop(map, w, x, y, weave);
            return (warp == top ? 1 : -1) * (warpDiameter + weftDiameter) / 4;
        }
        for (int i = 0; i <= segments; i++)
        {
            double along = i / 4.0 - .5; int n = (int)Math.Floor(along); double t = along - n;
            double z = HeightAt(n) * (1 - t) + HeightAt(n + 1) * t;
            for (int j = 0; j < sides; j++)
            {
                double angle = 2 * Math.PI * j / sides, offset = radius * Math.Cos(angle), dz = radius * Math.Sin(angle);
                mesh.Positions.Add(warp ? new Point3D((index - w / 2.0) * dx + offset, (along - h / 2.0) * dy, z + dz) : new Point3D((along - w / 2.0) * dx, (index - h / 2.0) * dy + offset, z + dz));
            }
        }
        for (int i = 0; i < segments; i++) for (int j = 0; j < sides; j++)
        {
            int a = i * sides + j, b = i * sides + (j + 1) % sides, c = (i + 1) * sides + j, d = (i + 1) * sides + (j + 1) % sides;
            mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(c); mesh.TriangleIndices.Add(d);
        }
        mesh.Freeze(); var material = new MaterialGroup(); material.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(80, 80, 80)), yarn == 0 ? 60 : 12)); material.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }
}
