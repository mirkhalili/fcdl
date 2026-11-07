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
        int w = Math.Min(MaxThreads, fabric.PixelWidth), h = Math.Min(MaxThreads, fabric.PixelHeight);
        var crop = new CroppedBitmap(fabric, new Int32Rect(0, 0, w, h)); crop.Freeze(); byte[] map = DesignAnalysis.ReadBinary(crop);
        Title = english ? "3D yarn detail" : "جزئیات سه‌بعدی نخ"; Width = 900; Height = 720; MinWidth = 500; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel();
        var hint = new TextBlock { Text = english ? $"Rotatable geometry, first {w} × {h} threads. Approximate appearance, not physical simulation." : $"نمونه چرخان از {w} × {h} نخ نخست؛ نمایش تقریبی است و شبیه‌سازی فیزیکی نیست.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), Foreground = Brushes.White };
        DockPanel.SetDock(hint, Dock.Top); root.Children.Add(hint); root.Background = new SolidColorBrush(Color.FromRgb(24, 37, 45));
        var viewport = new HelixViewport3D { ShowViewCube = true, ShowCoordinateSystem = false, Background = new SolidColorBrush(Color.FromRgb(35, 48, 55)), Camera = new PerspectiveCamera(new Point3D(w * .7, -h * .9, Math.Max(w, h) * 1.15), new Vector3D(-w * .7, h * .9, -Math.Max(w, h) * 1.15), new Vector3D(0, 0, 1), 42) };
        viewport.Children.Add(new DefaultLights()); var group = new Model3DGroup();
        Color warp = FabricSimulation.ParseColor(settings.WarpColor);
        for (int x = 0; x < w; x++) group.Children.Add(BuildYarn(map, w, h, true, x, warp, settings.WarpYarn, settings.Weave));
        for (int y = 0; y < h; y++)
        {
            string id = rowWefts.Count == fabric.PixelHeight ? rowWefts[y] : "1"; var yarn = FabricSimulation.YarnFor(settings, id);
            group.Children.Add(BuildYarn(map, w, h, false, y, FabricSimulation.ParseColor(yarn.Color), yarn.Yarn, settings.Weave));
        }
        viewport.Children.Add(new ModelVisual3D { Content = group }); root.Children.Add(viewport); Content = root;
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
