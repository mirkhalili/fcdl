using HelixToolkit.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Small, rotatable geometric yarn sample. Not a mechanical or colorimetric simulation.</summary>
public sealed class Fabric3DWindow : Window
{
    public const int MaxThreads = 32;
    public Fabric3DWindow(BitmapSource fabric, IReadOnlyList<string> rowWefts, SimulationSettings settings, bool english)
    {
        FabricSimulation.Validate(settings);
        Title = english ? "3D weave detail" : "نمای سه‌بعدی بافت"; Width = 900; Height = 720; MinWidth = 500; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(24, 37, 45)) };
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), Foreground = Brushes.White };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(6), FlowDirection = FlowDirection.LeftToRight };
        var left = new Button { Content = "◀ X", Margin = new Thickness(3) }; var right = new Button { Content = "X ▶", Margin = new Thickness(3) };
        var up = new Button { Content = "▲ Y", Margin = new Thickness(3) }; var down = new Button { Content = "Y ▼", Margin = new Thickness(3) };
        bar.Children.Add(left); bar.Children.Add(right); bar.Children.Add(up); bar.Children.Add(down); DockPanel.SetDock(bar, Dock.Bottom); root.Children.Add(bar);
        var viewport = new HelixViewport3D { ShowViewCube = true, ShowCoordinateSystem = false, Background = new SolidColorBrush(Color.FromRgb(35, 48, 55)) };
        root.Children.Add(viewport); Content = root; int offsetX = 0, offsetY = 0;
        void RenderPart()
        {
            int w = Math.Min(MaxThreads, fabric.PixelWidth - offsetX), h = Math.Min(MaxThreads, fabric.PixelHeight - offsetY);
            var crop = new CroppedBitmap(fabric, new Int32Rect(offsetX, offsetY, w, h)); crop.Freeze(); byte[] map = DesignAnalysis.ReadBinary(crop);
            var group = new Model3DGroup(); Color warp = FabricSimulation.ParseColor(settings.WarpColor);
            for (int x = 0; x < w; x++) group.Children.Add(BuildYarn(map, w, h, true, x, warp, settings.WarpYarn, settings.Weave));
            for (int y = 0; y < h; y++)
            {
                string id = rowWefts.Count == fabric.PixelHeight ? rowWefts[offsetY + y] : "1"; var yarn = FabricSimulation.YarnFor(settings, id);
                group.Children.Add(BuildYarn(map, w, h, false, y, FabricSimulation.ParseColor(yarn.Color), yarn.Yarn, settings.Weave));
            }
            viewport.Children.Clear(); viewport.Children.Add(new DefaultLights()); viewport.Children.Add(new ModelVisual3D { Content = group });
            viewport.Camera = new PerspectiveCamera(new Point3D(w * .7, -h * .9, Math.Max(w, h) * 1.15), new Vector3D(-w * .7, h * .9, -Math.Max(w, h) * 1.15), new Vector3D(0, 0, 1), 42);
            hint.Text = english ? $"Warp {offsetX + 1}–{offsetX + w} of {fabric.PixelWidth}; row {offsetY + 1}–{offsetY + h} of {fabric.PixelHeight}. Rotate with the mouse. Approximate geometry." : $"تار {offsetX + 1} تا {offsetX + w} از {fabric.PixelWidth}؛ رج {offsetY + 1} تا {offsetY + h} از {fabric.PixelHeight}. با ماوس بچرخانید؛ هندسه تقریبی است.";
            left.IsEnabled = offsetX > 0; up.IsEnabled = offsetY > 0; right.IsEnabled = offsetX + w < fabric.PixelWidth; down.IsEnabled = offsetY + h < fabric.PixelHeight;
        }
        left.Click += (_, _) => { offsetX = Math.Max(0, offsetX - MaxThreads); RenderPart(); }; right.Click += (_, _) => { offsetX += MaxThreads; RenderPart(); };
        up.Click += (_, _) => { offsetY = Math.Max(0, offsetY - MaxThreads); RenderPart(); }; down.Click += (_, _) => { offsetY += MaxThreads; RenderPart(); };
        RenderPart();
    }
    public static BitmapSource PatternBitmap(WeavePattern pattern)
    {
        if (pattern.Width < 1 || pattern.Height < 1 || (long)pattern.Width * pattern.Height > DesignProcessor.MaxPatternPixels || pattern.Values.Length != (long)pattern.Width * pattern.Height || pattern.Values.Any(v => v != 0 && v != 255)) throw new ArgumentException("Invalid binary weave.");
        var image = BitmapSource.Create(pattern.Width, pattern.Height, 96, 96, System.Windows.Media.PixelFormats.Gray8, null, pattern.Values, pattern.Width); image.Freeze(); return image;
    }
    public static void ShowPattern(Window owner, WeavePattern pattern, bool english)
    {
        var window = new Fabric3DWindow(PatternBitmap(pattern), Array.Empty<string>(), new SimulationSettings(Weave: 0), english) { Owner = owner }; window.ShowDialog();
    }
    private static GeometryModel3D BuildYarn(byte[] map, int w, int h, bool warp, int index, Color color, int yarn, int weave)
    {
        int length = warp ? h : w, segments = length * 4, sides = 8; const double radius = .25;
        var mesh = new MeshGeometry3D();
        double HeightAt(int n)
        {
            n = Math.Clamp(n, 0, length - 1); int x = warp ? index : n, y = warp ? n : index;
            bool top = FabricSimulation.WarpOnTop(map, w, x, y, weave);
            return (warp == top ? .22 : -.22);
        }
        for (int i = 0; i <= segments; i++)
        {
            double along = i / 4.0 - .5; int n = (int)Math.Floor(along); double t = along - n;
            double z = HeightAt(n) * (1 - t) + HeightAt(n + 1) * t;
            for (int j = 0; j < sides; j++)
            {
                double angle = 2 * Math.PI * j / sides, offset = radius * Math.Cos(angle), dz = radius * Math.Sin(angle);
                mesh.Positions.Add(warp ? new Point3D(index + offset - w / 2.0, along - h / 2.0, z + dz) : new Point3D(along - w / 2.0, index + offset - h / 2.0, z + dz));
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
