using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml.Linq;
using Path = System.Windows.Shapes.Path;

namespace JacquardStudio;

/// <summary>Loads the standalone Jacquard Studio SVG glyphs supplied in the Vazir design system.</summary>
public static class StudioIconCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ModuleGlyphs = new(() => LoadGlyphs("module-icons.json"));
    private static readonly Lazy<IReadOnlyDictionary<string, string>> ToolGlyphs = new(() => LoadGlyphs("tools-icons.json"));
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, Geometry> GeometryCache = new(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> LoadGlyphs(string fileName)
    {
        try
        {
            using var stream = Application.GetResourceStream(new Uri(
                $"pack://application:,,,/JacquardStudio;component/Assets/Studio/{fileName}", UriKind.Absolute))?.Stream;
            if (stream is null) return new Dictionary<string, string>();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UriFormatException or InvalidOperationException)
        {
            return new Dictionary<string, string>();
        }
    }

    public static bool HasGlyph(ModuleKind kind) => ModuleGlyphs.Value.ContainsKey(ModuleSlug(kind));

    public static string ModuleSlug(ModuleKind kind) => kind switch
    {
        ModuleKind.Load => "bitmap-import", ModuleKind.VectorLoad => "vector-import", ModuleKind.BinaryLoad => "machine-import",
        ModuleKind.BinaryConvert => "monochrome", ModuleKind.BlankDesign => "blank-canvas", ModuleKind.Colors => "color-management",
        ModuleKind.Compare => "compare-designs", ModuleKind.DesignIdentity => "design-record", ModuleKind.DesignEditor => "pattern-edit",
        ModuleKind.ExportData => "image-table-export", ModuleKind.Floats => "float-control", ModuleKind.Machine => "machine-output",
        ModuleKind.Repeat => "repeat", ModuleKind.Report => "report", ModuleKind.Resize => "resize", ModuleKind.SeamCheck => "repeat-seam",
        ModuleKind.Tension => "tension-analysis", ModuleKind.VectorDesign => "vector-editor", ModuleKind.Weaves => "weave-library",
        ModuleKind.CreateWeave => "weave-build", ModuleKind.ApplyWeaves => "weave-mapping", ModuleKind.AutoWeaves => "auto-map", ModuleKind.AiEdit => "ai",
        ModuleKind.MachineDefinition => "machine-definition", ModuleKind.Simulation => "fabric-simulation", ModuleKind.MatchPalette => "palette-match",
        ModuleKind.PeriodicFloats => "repeat-float", ModuleKind.QualityCheck => "quality-control", ModuleKind.Crop => "crop",
        ModuleKind.Selvedge => "selvedge", ModuleKind.Weft => "weft-insertion", ModuleKind.RectangleMask => "rect-mask",
        ModuleKind.ApplyMask => "apply-mask", _ => ""
    };

    public static string PathDataFor(ModuleKind kind) =>
        ModuleGlyphs.Value.TryGetValue(ModuleSlug(kind), out var markup)
            ? CachedGeometry(markup).ToString(CultureInfo.InvariantCulture)
            : ModuleIcons.PathForFallback(kind);

    public static string ToolSlug(string name) => name switch
    {
        "Settings" => "settings", "YarnDefinition" => "sliders", "MergeDesign" => "connect",
        "ImageToWeave" => "grid", "Undo" => "undo", "Redo" => "redo", _ => name
    };

    private static Geometry CachedGeometry(string svgMarkup)
    {
        lock (CacheLock)
        {
            if (GeometryCache.TryGetValue(svgMarkup, out var geometry)) return geometry;
            geometry = ParseSvgGeometry(svgMarkup);
            geometry.Freeze();
            GeometryCache.Add(svgMarkup, geometry);
            return geometry;
        }
    }

    private static Geometry ParseSvgGeometry(string svgMarkup)
    {
        XDocument document = XDocument.Parse(
            "<svg xmlns=\"http://www.w3.org/2000/svg\">" + svgMarkup + "</svg>",
            LoadOptions.None);
        var group = new GeometryGroup();
        foreach (var element in document.Root!.Elements())
        {
            double Number(string name, double fallback = 0) =>
                double.TryParse((string?)element.Attribute(name), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value) ? value : fallback;

            Geometry geometry = element.Name.LocalName switch
            {
                "path" => Geometry.Parse((string?)element.Attribute("d") ?? throw new FormatException("SVG path has no d attribute.")),
                "rect" => ParseRectangle(Number("x"), Number("y"), Number("width"), Number("height"), Number("rx"), Number("ry", Number("rx"))),
                "circle" => new EllipseGeometry(new Point(Number("cx"), Number("cy")), Number("r"), Number("r")),
                "ellipse" => new EllipseGeometry(new Point(Number("cx"), Number("cy")), Number("rx"), Number("ry")),
                _ => throw new FormatException($"Unsupported SVG glyph element '{element.Name.LocalName}'.")
            };
            group.Children.Add(geometry);
        }
        if (group.Children.Count == 0) throw new FormatException("SVG glyph contains no drawable geometry.");
        return group;
    }

    private static Geometry ParseRectangle(double x, double y, double width, double height, double radiusX, double radiusY)
    {
        if (width <= 0 || height <= 0) throw new FormatException("SVG rectangle has invalid dimensions.");
        return new RectangleGeometry(new Rect(x, y, width, height), Math.Max(0, radiusX), Math.Max(0, radiusY));
    }

    public static Path CreateTool(string glyph, double size, Brush? stroke = null)
    {
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        string key = ToolGlyphs.Value.TryGetValue(glyph, out var pathData) ? pathData :
            ModuleGlyphs.Value.TryGetValue(glyph, out pathData) ? pathData : "<path d=\"M4 4h16v16H4Z\"/>";
        Geometry geometry = CachedGeometry(key);
        var path = new Path
        {
            Data = geometry, Width = size, Height = size, Stretch = Stretch.Uniform,
            Fill = Brushes.Transparent, Stroke = stroke ?? Brushes.White, StrokeThickness = 1.55,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false
        };
        return path;
    }

    public static Path Create(ModuleKind kind, double size)
    {
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        string key = ModuleGlyphs.Value.TryGetValue(ModuleSlug(kind), out var glyph)
            ? glyph
            : $"<path d=\"{ModuleIcons.PathForFallback(kind)}\"/>";
        Geometry geometry = CachedGeometry(key);
        var path = new Path
        {
            Data = geometry,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Fill = Brushes.Transparent,
            Stroke = Brushes.White,
            StrokeThickness = 1.45,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };
        path.SetResourceReference(Shape.StrokeProperty, "IconGlyph");
        return path;
    }

    public static BitmapSource? CreateRaster(string name, int size = 18)
    {
        if (string.IsNullOrWhiteSpace(name) || size is < 8 or > 256) return null;
        string glyph = ToolSlug(name);
        if (!ToolGlyphs.Value.TryGetValue(glyph, out string? pathData) && !ModuleGlyphs.Value.TryGetValue(glyph, out pathData)) return null;
        try
        {
            var geometry = CachedGeometry(pathData);
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.PushTransform(new ScaleTransform(size / 24d, size / 24d));
                context.DrawGeometry(Brushes.Transparent,
                    new Pen(Brushes.White, 1.55) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
                context.Pop();
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or NotSupportedException or System.Xml.XmlException)
        {
            return null;
        }
    }
}
