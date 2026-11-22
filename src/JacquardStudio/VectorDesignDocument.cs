using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using System.Xml.Linq;

namespace JacquardStudio;

/// <summary>A cubic-capable vector path. Handles are stored in document coordinates.</summary>
public sealed class VectorDesignPath
{
    public List<VectorDesignNode> Nodes { get; set; } = new();
    public bool Closed { get; set; }
    public VectorDesignPath Clone() => new() { Closed = Closed, Nodes = Nodes.Select(node => node.Clone()).ToList() };
}

/// <summary>One path anchor and its optional incoming/outgoing Bézier control points.</summary>
public sealed class VectorDesignNode
{
    public double X { get; set; }
    public double Y { get; set; }
    public double InX { get; set; }
    public double InY { get; set; }
    public double OutX { get; set; }
    public double OutY { get; set; }
    public bool HasIn { get; set; }
    public bool HasOut { get; set; }
    public bool CurveToNext { get; set; }
    public VectorDesignNode Clone() => (VectorDesignNode)MemberwiseClone();
}

/// <summary>A styled SVG object. Most artwork is represented by one or more editable paths.</summary>
public sealed class VectorDesignObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "مسیر";
    public List<VectorDesignPath> Paths { get; set; } = new();
    public string Fill { get; set; } = "#C9A58B";
    public string Stroke { get; set; } = "#242424";
    public double StrokeWidth { get; set; } = 2;
    public double Opacity { get; set; } = 1;
    public double FillOpacity { get; set; } = 1;
    public double StrokeOpacity { get; set; } = 1;
    public bool Visible { get; set; } = true;
    public string? Text { get; set; }
    public double TextX { get; set; }
    public double TextY { get; set; }
    public double FontSize { get; set; } = 40;
    public string FontFamily { get; set; } = "Vazirmatn";

    public VectorDesignObject Clone() => new()
    {
        Id = Id,
        Name = Name,
        Paths = Paths.Select(path => path.Clone()).ToList(),
        Fill = Fill,
        Stroke = Stroke,
        StrokeWidth = StrokeWidth,
        Opacity = Opacity,
        FillOpacity = FillOpacity,
        StrokeOpacity = StrokeOpacity,
        Visible = Visible,
        Text = Text,
        TextX = TextX,
        TextY = TextY,
        FontSize = FontSize,
        FontFamily = FontFamily
    };
}

/// <summary>A named, ordered collection of vector objects.</summary>
public sealed class VectorDesignLayer
{
    public string Name { get; set; } = "لایه ۱";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public List<VectorDesignObject> Objects { get; set; } = new();
    public VectorDesignLayer Clone() => new()
    {
        Name = Name,
        Visible = Visible,
        Locked = Locked,
        Objects = Objects.Select(item => item.Clone()).ToList()
    };
}

/// <summary>
/// A self-contained, editable SVG document used by the vector studio and the Jacquard image workflow.
/// Imports deliberately accept the common SVG path/shape/layer subset and reject unsafe or unsupported paint servers.
/// </summary>
public sealed class VectorDesignDocument
{
    public const int MaxDimension = 8192;
    public const int MaxObjects = 20000;
    public const int MaxNodes = 200000;
    private const int MaxSvgCharacters = (int)ImageLoader.MaxFileBytes;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string InkscapeNamespace = "http://www.inkscape.org/namespaces/inkscape";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public int Width { get; set; }
    public int Height { get; set; }
    public int ActiveLayer { get; set; }
    public List<VectorDesignLayer> Layers { get; set; } = new();

    public VectorDesignDocument(int width = 640, int height = 480)
    {
        Width = width;
        Height = height;
        Layers.Add(new VectorDesignLayer());
        Validate();
    }

    public VectorDesignLayer CurrentLayer => Layers[ActiveLayer];

    public VectorDesignDocument Clone() => new()
    {
        Width = Width,
        Height = Height,
        ActiveLayer = ActiveLayer,
        Layers = Layers.Select(layer => layer.Clone()).ToList()
    };

    public void Validate()
    {
        if (Width < 1 || Height < 1 || Width > MaxDimension || Height > MaxDimension || (long)Width * Height > DesignProcessor.MaxPixels)
            throw new InvalidDataException("ابعاد صفحه برداری باید مثبت و حداکثر چهار میلیون پیکسل باشد.");
        if (Layers is null || Layers.Count is < 1 or > 64 || ActiveLayer < 0 || ActiveLayer >= Layers.Count)
            throw new InvalidDataException("تعداد لایه‌ها یا لایه فعال معتبر نیست.");
        int objectCount = 0, nodeCount = 0;
        foreach (var layer in Layers)
        {
            if (layer is null || string.IsNullOrWhiteSpace(layer.Name) || layer.Name.Length > 80 || layer.Objects is null)
                throw new InvalidDataException("مشخصات لایه برداری معتبر نیست.");
            foreach (var item in layer.Objects)
            {
                if (++objectCount > MaxObjects || item is null || item.Id == Guid.Empty || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 160 || item.Paths is null)
                    throw new InvalidDataException("تعداد یا مشخصات اشیای برداری معتبر نیست.");
                CheckPaint(item.Fill); CheckPaint(item.Stroke);
                if (!double.IsFinite(item.StrokeWidth) || item.StrokeWidth is < 0 or > 512 ||
                    !double.IsFinite(item.Opacity) || item.Opacity is < 0 or > 1 ||
                    !double.IsFinite(item.FillOpacity) || item.FillOpacity is < 0 or > 1 ||
                    !double.IsFinite(item.StrokeOpacity) || item.StrokeOpacity is < 0 or > 1 ||
                    !double.IsFinite(item.TextX) || !double.IsFinite(item.TextY) ||
                    !double.IsFinite(item.FontSize) || item.FontSize is < 1 or > 512 ||
                    item.FontFamily is null || item.FontFamily.Length > 100 || item.Text?.Length > 4096)
                    throw new InvalidDataException("ویژگی‌های ظاهری شیء برداری معتبر نیست.");
                foreach (var path in item.Paths)
                {
                    if (path is null || path.Nodes is null || path.Nodes.Count < 1) throw new InvalidDataException("مسیر برداری خالی یا نامعتبر است.");
                    foreach (var node in path.Nodes)
                    {
                        if (++nodeCount > MaxNodes || node is null) throw new InvalidDataException("تعداد گره‌های برداری بیش از حد مجاز است.");
                        CheckCoordinate(node.X); CheckCoordinate(node.Y);
                        if (node.HasIn) { CheckCoordinate(node.InX); CheckCoordinate(node.InY); }
                        if (node.HasOut) { CheckCoordinate(node.OutX); CheckCoordinate(node.OutY); }
                    }
                }
            }
        }
    }

    private static void CheckCoordinate(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > 1_000_000) throw new InvalidDataException("مختصات شیء برداری خارج از محدوده مجاز است.");
    }

    private static void CheckPaint(string paint)
    {
        if (paint is null || paint.Length > 64) throw new InvalidDataException("رنگ برداری معتبر نیست.");
        if (paint.Equals("none", StringComparison.OrdinalIgnoreCase) || paint.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return;
        if (paint.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("گرادیان و الگوهای خارجی SVG هنوز در استودیوی برداری قابل ویرایش نیستند.");
        _ = ParseColor(paint);
    }

    public static Color ParseColor(string value)
    {
        try
        {
            string text = value.Trim();
            if (text.StartsWith('#'))
            {
                string hex = text[1..];
                if (hex.Length is 3 or 4)
                {
                    byte r = Convert.ToByte(new string(hex[0], 2), 16), g = Convert.ToByte(new string(hex[1], 2), 16), b = Convert.ToByte(new string(hex[2], 2), 16);
                    byte a = hex.Length == 4 ? Convert.ToByte(new string(hex[3], 2), 16) : (byte)255;
                    return Color.FromArgb(a, r, g, b);
                }
                if (hex.Length == 6) return Color.FromRgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
                if (hex.Length == 8) return Color.FromArgb(Convert.ToByte(hex[6..8], 16), Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
            }
            bool rgba = text.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')');
            bool rgb = text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')');
            if (rgba || rgb)
            {
                int prefix = rgba ? 5 : 4;
                var parts = text[prefix..^1].Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length == (rgba ? 4 : 3))
                {
                    byte Channel(string part)
                    {
                        bool percent = part.EndsWith('%');
                        string number = percent ? part[..^1] : part;
                        double parsed = double.Parse(number, NumberStyles.Float, Invariant);
                        if (!double.IsFinite(parsed)) throw new FormatException();
                        parsed = percent ? parsed * 2.55 : parsed;
                        if (parsed is < 0 or > 255) throw new FormatException();
                        return (byte)Math.Round(parsed, MidpointRounding.AwayFromZero);
                    }
                    byte alpha = 255;
                    if (rgba)
                    {
                        string part = parts[3]; bool percent = part.EndsWith('%');
                        double parsed = double.Parse(percent ? part[..^1] : part, NumberStyles.Float, Invariant);
                        if (!double.IsFinite(parsed) || parsed < 0 || parsed > (percent ? 100 : 1)) throw new FormatException();
                        alpha = (byte)Math.Round((percent ? parsed / 100 : parsed) * 255, MidpointRounding.AwayFromZero);
                    }
                    return Color.FromArgb(alpha, Channel(parts[0]), Channel(parts[1]), Channel(parts[2]));
                }
            }
            object? parsedColor = ColorConverter.ConvertFromString(text);
            if (parsedColor is Color color) return color;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException) { }
        throw new InvalidDataException("رنگ SVG قابل خواندن نیست: " + value);
    }

    public string ToSvg()
    {
        Validate();
        XNamespace svg = SvgNamespace;
        XNamespace inkscape = InkscapeNamespace;
        var root = new XElement(svg + "svg",
            new XAttribute("version", "1.1"),
            new XAttribute(XNamespace.Xmlns + "inkscape", InkscapeNamespace),
            new XAttribute("width", Width.ToString(Invariant) + "px"),
            new XAttribute("height", Height.ToString(Invariant) + "px"),
            new XAttribute("viewBox", FormattableString.Invariant($"0 0 {Width} {Height}")),
            new XElement(svg + "rect", new XAttribute("id", "page-background"), new XAttribute("x", "0"), new XAttribute("y", "0"),
                new XAttribute("width", Width.ToString(Invariant)), new XAttribute("height", Height.ToString(Invariant)), new XAttribute("fill", "#FFFFFF")));

        for (int layerIndex = 0; layerIndex < Layers.Count; layerIndex++)
        {
            var layer = Layers[layerIndex];
            var group = new XElement(svg + "g",
                new XAttribute("id", "layer-" + (layerIndex + 1).ToString(Invariant)),
                new XAttribute(inkscape + "groupmode", "layer"),
                new XAttribute(inkscape + "label", layer.Name),
                new XAttribute("data-layer", "true"),
                new XAttribute("data-name", layer.Name),
                new XAttribute("data-locked", layer.Locked ? "true" : "false"));
            if (!layer.Visible) group.SetAttributeValue("display", "none");
            foreach (var item in layer.Objects)
            {
                if (item.Text is { } text)
                {
                    var textElement = new XElement(svg + "text",
                        new XAttribute("id", "object-" + item.Id.ToString("N")),
                        new XAttribute("data-name", item.Name),
                        new XAttribute("x", F(item.TextX)), new XAttribute("y", F(item.TextY)),
                        new XAttribute("font-family", item.FontFamily), new XAttribute("font-size", F(item.FontSize)),
                        new XAttribute("fill", item.Fill), new XAttribute("stroke", item.Stroke),
                        new XAttribute("stroke-width", F(item.StrokeWidth)),
                        new XAttribute("opacity", F(item.Opacity)),
                        new XAttribute("fill-opacity", F(item.FillOpacity)),
                        new XAttribute("stroke-opacity", F(item.StrokeOpacity)),
                        new XAttribute("direction", "rtl"),
                        new XAttribute(XNamespace.Xml + "space", "preserve"), text);
                    if (!item.Visible) textElement.SetAttributeValue("display", "none");
                    group.Add(textElement);
                }
                else
                {
                    var pathData = new XElement(svg + "path",
                        new XAttribute("id", "object-" + item.Id.ToString("N")),
                        new XAttribute("data-name", item.Name),
                        new XAttribute("d", ToSvgPathData(item.Paths)),
                        new XAttribute("fill", item.Fill), new XAttribute("stroke", item.Stroke),
                        new XAttribute("stroke-width", F(item.StrokeWidth)),
                        new XAttribute("stroke-linecap", "round"), new XAttribute("stroke-linejoin", "round"),
                        new XAttribute("opacity", F(item.Opacity)),
                        new XAttribute("fill-opacity", F(item.FillOpacity)),
                        new XAttribute("stroke-opacity", F(item.StrokeOpacity)));
                    if (!item.Visible) pathData.SetAttributeValue("display", "none");
                    group.Add(pathData);
                }
            }
            root.Add(group);
        }
        var svgText = new XDocument(new XDeclaration("1.0", "utf-8", null), root).ToString(SaveOptions.DisableFormatting);
        if (Encoding.UTF8.GetByteCount(svgText) > ImageLoader.MaxFileBytes) throw new InvalidDataException("پرونده SVG بیش از سقف ۱۲۸ مگابایت است.");
        return svgText;
    }

    public static string ToSvgPathData(IEnumerable<VectorDesignPath> paths)
    {
        var output = new StringBuilder();
        foreach (var path in paths)
        {
            if (path.Nodes.Count == 0) continue;
            if (output.Length > 0) output.Append(' ');
            var first = path.Nodes[0];
            output.Append("M ").Append(F(first.X)).Append(' ').Append(F(first.Y));
            for (int i = 1; i < path.Nodes.Count; i++) AppendSegment(output, path.Nodes[i - 1], path.Nodes[i]);
            if (path.Closed && path.Nodes.Count > 1)
            {
                AppendSegment(output, path.Nodes[^1], first);
                output.Append(" Z");
            }
        }
        return output.ToString();
    }

    private static void AppendSegment(StringBuilder output, VectorDesignNode from, VectorDesignNode to)
    {
        if (from.CurveToNext)
        {
            var first = from.HasOut ? new Point(from.OutX, from.OutY) : new Point(from.X, from.Y);
            var second = to.HasIn ? new Point(to.InX, to.InY) : new Point(to.X, to.Y);
            output.Append(" C ").Append(F(first.X)).Append(' ').Append(F(first.Y)).Append(' ')
                .Append(F(second.X)).Append(' ').Append(F(second.Y)).Append(' ')
                .Append(F(to.X)).Append(' ').Append(F(to.Y));
        }
        else output.Append(" L ").Append(F(to.X)).Append(' ').Append(F(to.Y));
    }

    private static string F(double value) => value.ToString("0.########", Invariant);

    public static VectorDesignDocument FromSvg(string markup)
    {
        if (string.IsNullOrWhiteSpace(markup) || markup.Length > MaxSvgCharacters || Encoding.UTF8.GetByteCount(markup) > ImageLoader.MaxFileBytes)
            throw new InvalidDataException("پرونده SVG خالی یا بیش از حد بزرگ است.");
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxSvgCharacters, IgnoreComments = true, IgnoreProcessingInstructions = true };
        XDocument xml;
        try
        {
            using var text = new StringReader(markup);
            using var reader = XmlReader.Create(text, settings);
            xml = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            throw new InvalidDataException("ساختار SVG معتبر نیست.", ex);
        }

        var root = xml.Root;
        if (root is null || root.Name.LocalName != "svg") throw new InvalidDataException("ریشه پرونده باید عنصر SVG باشد.");
        var viewBox = ParseViewBox((string?)root.Attribute("viewBox"));
        double rawWidth = ParseLength((string?)root.Attribute("width"), viewBox?.Width ?? 640);
        double rawHeight = ParseLength((string?)root.Attribute("height"), viewBox?.Height ?? 480);
        if (viewBox is Rect declaredViewBox && declaredViewBox.Width > 0 && declaredViewBox.Height > 0)
        {
            if (root.Attribute("width") is null) rawWidth = declaredViewBox.Width;
            if (root.Attribute("height") is null) rawHeight = declaredViewBox.Height;
        }
        int width = Dimension(rawWidth), height = Dimension(rawHeight);
        var document = new VectorDesignDocument(width, height);
        document.Layers.Clear();
        document.Layers.Add(new VectorDesignLayer { Name = "لایه ۱" });
        document.ActiveLayer = 0;

        Affine viewport = viewBox is Rect box && box.Width > 0 && box.Height > 0
            ? new Affine(width / box.Width, 0, 0, height / box.Height, -box.X * width / box.Width, -box.Y * height / box.Height)
            : Affine.Identity;
        var baseStyle = SvgStyle.Default;
        int nextLayer = 1;
        Walk(root, baseStyle, viewport, document, document.Layers[0], ref nextLayer);
        if (document.Layers.Count == 0) document.Layers.Add(new VectorDesignLayer());
        document.Validate();
        return document;
    }

    private static void Walk(XElement element, SvgStyle inherited, Affine inheritedTransform, VectorDesignDocument document, VectorDesignLayer layer, ref int nextLayer, int depth = 0)
    {
        if (depth > 128) throw new InvalidDataException("ساختار گروه‌های SVG بیش از حد تو در تو است.");
        string name = element.Name.LocalName;
        if (name is "defs" or "title" or "desc" or "metadata" or "namedview" or "symbol" or "clipPath" or "mask" or "filter" or "linearGradient" or "radialGradient" or "pattern") return;
        if (name is not ("svg" or "g" or "path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon" or "text")) return;

        bool isLayer = name == "g" && ((string?)element.Attribute("data-layer") == "true" || element.Attribute(XName.Get("groupmode", InkscapeNamespace))?.Value == "layer");
        SvgStyle style = ApplyStyle(element, inherited);
        if (style.DisplayNone && !isLayer) return;
        Affine transform = Multiply(inheritedTransform, ParseTransform((string?)element.Attribute("transform")));
        VectorDesignLayer targetLayer = layer;
        if (isLayer)
        {
            string layerName = (string?)element.Attribute("data-name") ?? (string?)element.Attribute(XName.Get("label", InkscapeNamespace)) ?? (string?)element.Attribute("id") ?? $"لایه {nextLayer}";
            bool reuseEmptyDefault = document.Layers.Count == 1 && ReferenceEquals(layer, document.Layers[0]) && layer.Objects.Count == 0 && layer.Name == "لایه ۱";
            if (reuseEmptyDefault) targetLayer = layer;
            else
            {
                if (document.Layers.Count >= 64) throw new InvalidDataException("پرونده SVG بیش از ۶۴ لایه دارد.");
                targetLayer = new VectorDesignLayer(); document.Layers.Add(targetLayer);
            }
            targetLayer.Name = SafeName(layerName, $"لایه {nextLayer}", 80);
            targetLayer.Visible = !style.DisplayNone;
            targetLayer.Locked = string.Equals((string?)element.Attribute("data-locked"), "true", StringComparison.OrdinalIgnoreCase);
            nextLayer++;
            // Layer visibility is stored on the layer itself so its children remain editable in the model.
            style = style with { DisplayNone = false };
        }

        if (name is "svg" or "g")
        {
            foreach (var child in element.Elements()) Walk(child, style, transform, document, targetLayer, ref nextLayer, depth + 1);
            return;
        }

        if (name == "rect" && (string?)element.Attribute("id") == "page-background") return;
        if (document.Layers.Sum(item => item.Objects.Count) >= MaxObjects) throw new InvalidDataException("پرونده SVG بیش از حد مجاز شیء دارد.");
        string objectName = SafeName((string?)element.Attribute("data-name") ?? (string?)element.Attribute("id") ?? name, name);
        var item = new VectorDesignObject
        {
            Name = objectName,
            Fill = style.Fill,
            Stroke = style.Stroke,
            StrokeWidth = style.StrokeWidth * transform.ScaleEstimate,
            Opacity = style.Opacity,
            FillOpacity = style.FillOpacity,
            StrokeOpacity = style.StrokeOpacity,
            Visible = true,
            FontFamily = style.FontFamily,
            FontSize = style.FontSize * transform.ScaleEstimate
        };

        if (name == "text")
        {
            item.Text = string.Concat(element.DescendantNodesAndSelf().OfType<XText>().Select(node => node.Value));
            if (item.Text.Length > 4096) throw new InvalidDataException("متن SVG بیش از حد طولانی است.");
            var point = transform.Transform(new Point(ParseLength((string?)element.Attribute("x"), 0), ParseLength((string?)element.Attribute("y"), 0)));
            item.TextX = point.X; item.TextY = point.Y;
        }
        else
        {
            item.Paths = ShapePaths(element, name);
            foreach (var path in item.Paths)
                foreach (var node in path.Nodes)
                {
                    var point = transform.Transform(new Point(node.X, node.Y)); node.X = point.X; node.Y = point.Y;
                    if (node.HasIn) { point = transform.Transform(new Point(node.InX, node.InY)); node.InX = point.X; node.InY = point.Y; }
                    if (node.HasOut) { point = transform.Transform(new Point(node.OutX, node.OutY)); node.OutX = point.X; node.OutY = point.Y; }
                }
        }
        targetLayer.Objects.Add(item);
    }

    private static List<VectorDesignPath> ShapePaths(XElement element, string kind)
    {
        switch (kind)
        {
            case "path": return SvgPathParser.Parse((string?)element.Attribute("d") ?? "");
            case "rect":
            {
                double x = ParseLength((string?)element.Attribute("x"), 0), y = ParseLength((string?)element.Attribute("y"), 0);
                double w = ParseLength((string?)element.Attribute("width"), 0), h = ParseLength((string?)element.Attribute("height"), 0);
                if (w <= 0 || h <= 0) return new();
                double rx = Math.Clamp(ParseLength((string?)element.Attribute("rx"), 0), 0, w / 2), ry = Math.Clamp(ParseLength((string?)element.Attribute("ry"), rx), 0, h / 2);
                return new() { RectanglePath(x, y, w, h, rx, ry) };
            }
            case "circle":
            {
                double cx = ParseLength((string?)element.Attribute("cx"), 0), cy = ParseLength((string?)element.Attribute("cy"), 0), radius = ParseLength((string?)element.Attribute("r"), 0);
                return radius <= 0 ? new() : new() { EllipsePath(cx - radius, cy - radius, cx + radius, cy + radius) };
            }
            case "ellipse":
            {
                double cx = ParseLength((string?)element.Attribute("cx"), 0), cy = ParseLength((string?)element.Attribute("cy"), 0);
                double rx = ParseLength((string?)element.Attribute("rx"), 0), ry = ParseLength((string?)element.Attribute("ry"), 0);
                return rx <= 0 || ry <= 0 ? new() : new() { EllipsePath(cx - rx, cy - ry, cx + rx, cy + ry) };
            }
            case "line":
            {
                var path = new VectorDesignPath();
                path.Nodes.Add(Node(ParseLength((string?)element.Attribute("x1"), 0), ParseLength((string?)element.Attribute("y1"), 0)));
                path.Nodes.Add(Node(ParseLength((string?)element.Attribute("x2"), 0), ParseLength((string?)element.Attribute("y2"), 0)));
                return new() { path };
            }
            case "polyline":
            case "polygon":
            {
                var numbers = ParsePointList((string?)element.Attribute("points"));
                if (numbers.Count < 2) return new();
                var path = new VectorDesignPath { Closed = kind == "polygon" };
                for (int i = 0; i < numbers.Count; i += 2) path.Nodes.Add(Node(numbers[i], numbers[i + 1]));
                return new() { path };
            }
            default: return new();
        }
    }

    private static VectorDesignPath RectanglePath(double x, double y, double width, double height, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0)
        {
            return new VectorDesignPath { Closed = true, Nodes = new List<VectorDesignNode> { Node(x, y), Node(x + width, y), Node(x + width, y + height), Node(x, y + height) } };
        }
        double k = 0.5522847498307936;
        var path = new VectorDesignPath { Closed = true };
        path.Nodes.Add(new VectorDesignNode { X = x + rx, Y = y, InX = x + rx - k * rx, InY = y, OutX = x + rx + k * rx, OutY = y, HasIn = true, HasOut = true, CurveToNext = false });
        path.Nodes.Add(new VectorDesignNode { X = x + width - rx, Y = y, InX = x + width - rx - k * rx, InY = y, OutX = x + width - rx + k * rx, OutY = y, HasIn = true, HasOut = true, CurveToNext = true });
        path.Nodes.Add(new VectorDesignNode { X = x + width, Y = y + ry, InX = x + width, InY = y + ry - k * ry, OutX = x + width, OutY = y + ry + k * ry, HasIn = true, HasOut = true, CurveToNext = false });
        path.Nodes.Add(new VectorDesignNode { X = x + width, Y = y + height - ry, InX = x + width, InY = y + height - ry - k * ry, OutX = x + width, OutY = y + height - ry + k * ry, HasIn = true, HasOut = true, CurveToNext = true });
        path.Nodes.Add(new VectorDesignNode { X = x + width - rx, Y = y + height, InX = x + width - rx + k * rx, InY = y + height, OutX = x + width - rx - k * rx, OutY = y + height, HasIn = true, HasOut = true, CurveToNext = false });
        path.Nodes.Add(new VectorDesignNode { X = x + rx, Y = y + height, InX = x + rx + k * rx, InY = y + height, OutX = x + rx - k * rx, OutY = y + height, HasIn = true, HasOut = true, CurveToNext = true });
        path.Nodes.Add(new VectorDesignNode { X = x, Y = y + height - ry, InX = x, InY = y + height - ry + k * ry, OutX = x, OutY = y + height - ry - k * ry, HasIn = true, HasOut = true, CurveToNext = false });
        path.Nodes.Add(new VectorDesignNode { X = x, Y = y + ry, InX = x, InY = y + ry + k * ry, OutX = x, OutY = y + ry - k * ry, HasIn = true, HasOut = true, CurveToNext = true });
        return path;
    }

    public static VectorDesignPath EllipsePath(double left, double top, double right, double bottom)
    {
        double rx = Math.Abs(right - left) / 2, ry = Math.Abs(bottom - top) / 2, cx = (right + left) / 2, cy = (bottom + top) / 2, k = 0.5522847498307936;
        if (rx <= 0 || ry <= 0) return new VectorDesignPath { Nodes = new List<VectorDesignNode> { Node(cx, cy) } };
        return new VectorDesignPath
        {
            Closed = true,
            Nodes = new List<VectorDesignNode>
            {
                new() { X=cx+rx, Y=cy, InX=cx+rx, InY=cy-k*ry, OutX=cx+rx, OutY=cy+k*ry, HasIn=true, HasOut=true, CurveToNext=true },
                new() { X=cx, Y=cy+ry, InX=cx+k*rx, InY=cy+ry, OutX=cx-k*rx, OutY=cy+ry, HasIn=true, HasOut=true, CurveToNext=true },
                new() { X=cx-rx, Y=cy, InX=cx-rx, InY=cy+k*ry, OutX=cx-rx, OutY=cy-k*ry, HasIn=true, HasOut=true, CurveToNext=true },
                new() { X=cx, Y=cy-ry, InX=cx-k*rx, InY=cy-ry, OutX=cx+k*rx, OutY=cy-ry, HasIn=true, HasOut=true, CurveToNext=true }
            }
        };
    }

    /// <summary>Creates a two-anchor cubic arc from a quadratic-style control point.</summary>
    public static VectorDesignPath CreateCubicArc(Point start, Point end, Point control, bool symmetric)
    {
        if (!double.IsFinite(start.X) || !double.IsFinite(start.Y) || !double.IsFinite(end.X) || !double.IsFinite(end.Y) || !double.IsFinite(control.X) || !double.IsFinite(control.Y))
            throw new ArgumentOutOfRangeException(nameof(control), "مختصات منحنی باید محدود و معتبر باشند.");
        Vector chord = end - start;
        double length = chord.Length;
        if (length < 1e-6) throw new ArgumentException("نقطه آغاز و پایان منحنی باید متفاوت باشند.", nameof(end));
        if (symmetric)
        {
            Vector normal = new(-chord.Y / length, chord.X / length);
            double offset = Vector.Multiply(control - start, normal);
            control = new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2) + normal * offset;
        }
        var first = Node(start.X, start.Y);
        var last = Node(end.X, end.Y);
        first.CurveToNext = true;
        first.HasOut = true;
        first.OutX = start.X + (control.X - start.X) * (2.0 / 3);
        first.OutY = start.Y + (control.Y - start.Y) * (2.0 / 3);
        last.HasIn = true;
        last.InX = end.X + (control.X - end.X) * (2.0 / 3);
        last.InY = end.Y + (control.Y - end.Y) * (2.0 / 3);
        return new VectorDesignPath { Nodes = new List<VectorDesignNode> { first, last } };
    }

    /// <summary>Converts one straight path segment to an equivalent editable cubic Bézier segment.</summary>
    public static bool ConvertLineSegmentToBezier(VectorDesignPath path, int startNodeIndex)
    {
        ArgumentNullException.ThrowIfNull(path);
        if ((uint)startNodeIndex >= (uint)path.Nodes.Count) throw new ArgumentOutOfRangeException(nameof(startNodeIndex));
        if (path.Nodes.Count < 2 || !path.Closed && startNodeIndex == path.Nodes.Count - 1) return false;
        var from = path.Nodes[startNodeIndex];
        var to = path.Nodes[(startNodeIndex + 1) % path.Nodes.Count];
        if (from.CurveToNext) return false;
        double dx = to.X - from.X, dy = to.Y - from.Y;
        if (dx * dx + dy * dy < 1e-12) return false;
        from.HasOut = true;
        from.OutX = from.X + dx / 3;
        from.OutY = from.Y + dy / 3;
        to.HasIn = true;
        to.InX = from.X + 2 * dx / 3;
        to.InY = from.Y + 2 * dy / 3;
        from.CurveToNext = true;
        return true;
    }

    public static VectorDesignNode Node(double x, double y) => new() { X = x, Y = y };

    public BitmapSource Render(int width = 0, int height = 0)
    {
        Validate();
        width = width == 0 ? Width : width;
        height = height == 0 ? Height : height;
        if (width < 1 || height < 1 || (long)width * height > DesignProcessor.MaxPixels)
            throw new ArgumentOutOfRangeException(nameof(width), "اندازه خروجی SVG از سقف چهار میلیون پیکسل بیشتر است.");
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) return dispatcher.Invoke(() => RenderCore(width, height));
        return RenderCore(width, height);
    }

    private BitmapSource RenderCore(int targetWidth, int targetHeight)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, targetWidth, targetHeight));
            dc.PushTransform(new ScaleTransform((double)targetWidth / Width, (double)targetHeight / Height));
            foreach (var layer in Layers)
            {
                if (!layer.Visible) continue;
                foreach (var item in layer.Objects)
                {
                    if (!item.Visible) continue;
                    DrawObject(dc, item);
                }
            }
            dc.Pop();
        }
        var output = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
        output.Render(visual);
        output.Freeze();
        return output;
    }

    internal static void DrawObject(DrawingContext dc, VectorDesignObject item)
    {
        if (item.Text is { } text)
        {
            if (text.Length == 0 || item.Fill.Equals("none", StringComparison.OrdinalIgnoreCase) || item.Fill.Equals("transparent", StringComparison.OrdinalIgnoreCase)) return;
            var brush = MakeBrush(item.Fill, item.Opacity * item.FillOpacity);
            if (brush is null) return;
            var typeface = new Typeface(new FontFamily(string.IsNullOrWhiteSpace(item.FontFamily) ? "Vazirmatn" : item.FontFamily), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("fa-IR"), FlowDirection.RightToLeft, typeface, item.FontSize, brush, 1.0)
            {
                MaxTextWidth = 16000,
                MaxTextHeight = 4096,
                Trimming = TextTrimming.None
            };
            dc.DrawText(formatted, new Point(item.TextX, item.TextY - item.FontSize * 0.8));
            return;
        }
        var geometry = GeometryFor(item.Paths);
        if (geometry is null) return;
        var fill = MakeBrush(item.Fill, item.Opacity * item.FillOpacity);
        var strokeBrush = MakeBrush(item.Stroke, item.Opacity * item.StrokeOpacity);
        Pen? pen = strokeBrush is null || item.StrokeWidth <= 0 ? null : new Pen(strokeBrush, item.StrokeWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        dc.DrawGeometry(fill, pen, geometry);
    }

    internal static Geometry? GeometryFor(IReadOnlyList<VectorDesignPath> paths)
    {
        var geometry = new PathGeometry();
        foreach (var path in paths)
        {
            if (path.Nodes.Count == 0) continue;
            var first = path.Nodes[0];
            var figure = new PathFigure { StartPoint = new Point(first.X, first.Y), IsClosed = path.Closed, IsFilled = true };
            for (int i = 1; i < path.Nodes.Count; i++) AddSegment(figure, path.Nodes[i - 1], path.Nodes[i]);
            if (path.Closed && path.Nodes.Count > 1) AddSegment(figure, path.Nodes[^1], first);
            geometry.Figures.Add(figure);
        }
        if (geometry.Figures.Count == 0) return null;
        geometry.Freeze();
        return geometry;
    }

    private static void AddSegment(PathFigure figure, VectorDesignNode from, VectorDesignNode to)
    {
        if (from.CurveToNext)
        {
            Point one = from.HasOut ? new Point(from.OutX, from.OutY) : new Point(from.X, from.Y);
            Point two = to.HasIn ? new Point(to.InX, to.InY) : new Point(to.X, to.Y);
            figure.Segments.Add(new BezierSegment(one, two, new Point(to.X, to.Y), true));
        }
        else figure.Segments.Add(new LineSegment(new Point(to.X, to.Y), true));
    }

    internal static Brush? MakeBrush(string paint, double opacity)
    {
        if (paint.Equals("none", StringComparison.OrdinalIgnoreCase) || paint.Equals("transparent", StringComparison.OrdinalIgnoreCase) || opacity <= 0) return null;
        var brush = new SolidColorBrush(ParseColor(paint)) { Opacity = Math.Clamp(opacity, 0, 1) };
        brush.Freeze();
        return brush;
    }

    private static double ParseLength(string? value, double fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        string trimmed = value.Trim();
        if (trimmed.EndsWith('%')) return fallback;
        int end = trimmed.Length;
        while (end > 0 && (char.IsLetter(trimmed[end - 1]) || trimmed[end - 1] == '%')) end--;
        if (double.TryParse(trimmed[..end], NumberStyles.Float, Invariant, out double parsed) && double.IsFinite(parsed)) return parsed;
        return fallback;
    }

    private static int Dimension(double value)
    {
        if (!double.IsFinite(value) || value < 1 || value > MaxDimension) throw new InvalidDataException("ابعاد صفحه SVG باید بین ۱ تا ۸۱۹۲ پیکسل باشد.");
        int result = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return Math.Max(1, result);
    }

    private static Rect? ParseViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var items = Regex.Split(value.Trim(), @"[\s,]+");
        if (items.Length != 4 || !items.All(part => double.TryParse(part, NumberStyles.Float, Invariant, out _))) throw new InvalidDataException("viewBox در SVG معتبر نیست.");
        var values = items.Select(part => double.Parse(part, Invariant)).ToArray();
        if (values.Any(v => !double.IsFinite(v)) || values[2] <= 0 || values[3] <= 0) throw new InvalidDataException("viewBox در SVG معتبر نیست.");
        return new Rect(values[0], values[1], values[2], values[3]);
    }

    private static List<double> ParsePointList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new();
        var matches = Regex.Matches(value, @"[-+]?(?:(?:\d+\.?\d*)|(?:\.\d+))(?:[eE][-+]?\d+)?");
        if (matches.Count % 2 != 0 || matches.Count > MaxNodes * 2) throw new InvalidDataException("فهرست نقاط SVG معتبر نیست.");
        return matches.Cast<Match>().Select(match => double.Parse(match.Value, NumberStyles.Float, Invariant)).ToList();
    }

    private static string SafeName(string? name, string fallback, int maximum = 160)
    {
        string safe = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        if (safe.Length > maximum) safe = safe[..maximum];
        return safe;
    }

    private sealed record SvgStyle(string Fill, string Stroke, double StrokeWidth, double Opacity, double FillOpacity, double StrokeOpacity, string FontFamily, double FontSize, bool DisplayNone, string Color)
    {
        public static SvgStyle Default { get; } = new("#000000", "none", 1, 1, 1, 1, "Vazirmatn", 16, false, "#000000");
    }

    private static SvgStyle ApplyStyle(XElement element, SvgStyle parent)
    {
        string? styleAttribute = (string?)element.Attribute("style");
        var styleValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(styleAttribute))
            foreach (string declaration in styleAttribute.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = declaration.IndexOf(':');
                if (separator > 0) styleValues[declaration[..separator].Trim()] = declaration[(separator + 1)..].Trim();
            }
        string? Value(string key) => styleValues.TryGetValue(key, out var styleValue) ? styleValue : (string?)element.Attribute(key);
        string color = Value("color") is { } cssColor && !cssColor.Equals("currentColor", StringComparison.OrdinalIgnoreCase) ? cssColor : parent.Color;
        string ResolvePaint(string fallback, string? value)
        {
            if (value is null) return fallback;
            if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase)) return color;
            if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("گرادیان، فیلتر و الگوهای SVG در این نسخه قابل تبدیل به رنگ تخت نیستند.");
            return value.Trim();
        }
        double Number(string? text, double fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            string part = text.Trim().TrimEnd('%');
            if (!double.TryParse(part, NumberStyles.Float, Invariant, out double value) || !double.IsFinite(value)) return fallback;
            if (text.Trim().EndsWith('%')) value /= 100;
            return value;
        }
        bool hidden = parent.DisplayNone || string.Equals(Value("display"), "none", StringComparison.OrdinalIgnoreCase) || string.Equals(Value("visibility"), "hidden", StringComparison.OrdinalIgnoreCase);
        return new SvgStyle(
            ResolvePaint(parent.Fill, Value("fill")), ResolvePaint(parent.Stroke, Value("stroke")),
            Math.Clamp(Number(Value("stroke-width"), parent.StrokeWidth), 0, 512),
            Math.Clamp(parent.Opacity * Number(Value("opacity"), 1), 0, 1),
            Math.Clamp(parent.FillOpacity * Number(Value("fill-opacity"), 1), 0, 1),
            Math.Clamp(parent.StrokeOpacity * Number(Value("stroke-opacity"), 1), 0, 1),
            Value("font-family") ?? parent.FontFamily, Math.Clamp(Number(Value("font-size"), parent.FontSize), 1, 512), hidden, color);
    }

    private readonly record struct Affine(double A, double B, double C, double D, double E, double F)
    {
        public static Affine Identity => new(1, 0, 0, 1, 0, 0);
        public double ScaleEstimate => Math.Sqrt(Math.Abs(A * D - B * C));
        public Point Transform(Point point) => new(A * point.X + C * point.Y + E, B * point.X + D * point.Y + F);
    }

    private static Affine Multiply(Affine left, Affine right) => new(
        left.A * right.A + left.C * right.B,
        left.B * right.A + left.D * right.B,
        left.A * right.C + left.C * right.D,
        left.B * right.C + left.D * right.D,
        left.A * right.E + left.C * right.F + left.E,
        left.B * right.E + left.D * right.F + left.F);

    private static Affine ParseTransform(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Affine.Identity;
        var result = Affine.Identity;
        var matches = Regex.Matches(text, @"([A-Za-z]+)\s*\(([^)]*)\)");
        foreach (Match match in matches)
        {
            string name = match.Groups[1].Value;
            var values = Regex.Matches(match.Groups[2].Value, @"[-+]?(?:(?:\d+\.?\d*)|(?:\.\d+))(?:[eE][-+]?\d+)?").Cast<Match>().Select(m => double.Parse(m.Value, NumberStyles.Float, Invariant)).ToArray();
            Affine next = name switch
            {
                "matrix" when values.Length == 6 => new Affine(values[0], values[1], values[2], values[3], values[4], values[5]),
                "translate" when values.Length is 1 or 2 => new Affine(1, 0, 0, 1, values[0], values.Length == 2 ? values[1] : 0),
                "scale" when values.Length is 1 or 2 => new Affine(values[0], 0, 0, values.Length == 2 ? values[1] : values[0], 0, 0),
                "rotate" when values.Length is 1 or 3 => Rotation(values),
                _ => throw new NotSupportedException("نوع تبدیل SVG پشتیبانی نمی‌شود: " + name)
            };
            if (new[] { next.A, next.B, next.C, next.D, next.E, next.F }.Any(value => !double.IsFinite(value) || Math.Abs(value) > 1_000_000)) throw new InvalidDataException("تبدیل SVG بیش از حد بزرگ است.");
            result = Multiply(result, next);
        }
        return result;
    }

    private static Affine Rotation(double[] values)
    {
        double angle = values[0] * Math.PI / 180, cos = Math.Cos(angle), sin = Math.Sin(angle);
        var rotation = new Affine(cos, sin, -sin, cos, 0, 0);
        if (values.Length == 1) return rotation;
        double cx = values[1], cy = values[2];
        return Multiply(Multiply(new Affine(1, 0, 0, 1, cx, cy), rotation), new Affine(1, 0, 0, 1, -cx, -cy));
    }

    private static class SvgPathParser
    {
        private static readonly Regex TokenPattern = new(@"[-+]?(?:(?:\d+\.?\d*)|(?:\.\d+))(?:[eE][-+]?\d+)?|[A-Za-z]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static List<VectorDesignPath> Parse(string data)
        {
            if (string.IsNullOrWhiteSpace(data)) return new();
            var matches = TokenPattern.Matches(data);
            if (matches.Count == 0 || matches.Count > MaxNodes * 8) throw new InvalidDataException("داده مسیر SVG معتبر نیست یا بیش از حد بزرگ است.");
            int previousEnd = 0;
            foreach (Match match in matches)
            {
                for (int i = previousEnd; i < match.Index; i++) if (!char.IsWhiteSpace(data[i]) && data[i] != ',') throw new InvalidDataException("جداکننده مسیر SVG معتبر نیست.");
                previousEnd = match.Index + match.Length;
            }
            for (int i = previousEnd; i < data.Length; i++) if (!char.IsWhiteSpace(data[i]) && data[i] != ',') throw new InvalidDataException("داده اضافی در مسیر SVG وجود دارد.");
            var tokens = matches.Select(match => match.Value).ToArray();
            var paths = new List<VectorDesignPath>();
            VectorDesignPath? path = null;
            Point current = new(), start = new(), lastCubicControl = new(), lastQuadControl = new();
            char command = '\0', previousCommand = '\0';
            int index = 0, totalNodes = 0;

            double Number()
            {
                if (index >= tokens.Length || IsCommand(tokens[index])) throw new InvalidDataException("داده عددی مسیر SVG ناقص است.");
                double number = double.Parse(tokens[index++], NumberStyles.Float, Invariant);
                if (!double.IsFinite(number) || Math.Abs(number) > 1_000_000) throw new InvalidDataException("مختصات مسیر SVG خارج از محدوده است.");
                return number;
            }
            Point Pair(bool relative)
            {
                double x = Number(), y = Number();
                return relative ? new Point(current.X + x, current.Y + y) : new Point(x, y);
            }
            void Begin(Point point)
            {
                if (path is { Nodes.Count: > 0 }) paths.Add(path);
                path = new VectorDesignPath();
                path.Nodes.Add(Node(point.X, point.Y));
                current = start = point;
                totalNodes++;
            }
            void Line(Point point)
            {
                EnsurePath();
                path!.Nodes[^1].CurveToNext = false;
                path.Nodes.Add(Node(point.X, point.Y)); current = point; totalNodes++;
            }
            void Cubic(Point one, Point two, Point end)
            {
                EnsurePath();
                var from = path!.Nodes[^1];
                from.CurveToNext = true; from.HasOut = true; from.OutX = one.X; from.OutY = one.Y;
                var to = Node(end.X, end.Y); to.HasIn = true; to.InX = two.X; to.InY = two.Y;
                path.Nodes.Add(to); current = end; lastCubicControl = two; totalNodes++;
            }
            void EnsurePath() { if (path is null || path.Nodes.Count == 0) throw new InvalidDataException("مسیر SVG باید با M شروع شود."); }
            void CheckCount() { if (totalNodes > MaxNodes) throw new InvalidDataException("تعداد گره‌های SVG بیش از حد مجاز است."); }

            while (index < tokens.Length)
            {
                if (IsCommand(tokens[index]))
                {
                    command = tokens[index++][0];
                    if (char.ToUpperInvariant(command) == 'Z')
                    {
                        EnsurePath(); path!.Closed = true; current = start; previousCommand = command; command = '\0'; CheckCount(); continue;
                    }
                }
                if (command == '\0') throw new InvalidDataException("فرمان مسیر SVG مشخص نیست.");
                char upper = char.ToUpperInvariant(command); bool relative = char.IsLower(command);
                switch (upper)
                {
                    case 'M':
                    {
                        Point point = Pair(relative); Begin(point); previousCommand = command; command = relative ? 'l' : 'L'; break;
                    }
                    case 'L': Line(Pair(relative)); previousCommand = command; break;
                    case 'H':
                    {
                        double x = Number(); Line(new Point(relative ? current.X + x : x, current.Y)); previousCommand = command; break;
                    }
                    case 'V':
                    {
                        double y = Number(); Line(new Point(current.X, relative ? current.Y + y : y)); previousCommand = command; break;
                    }
                    case 'C':
                    {
                        Point one = Pair(relative), two = Pair(relative), end = Pair(relative); Cubic(one, two, end); previousCommand = command; break;
                    }
                    case 'S':
                    {
                        Point one = char.ToUpperInvariant(previousCommand) is 'C' or 'S' ? new Point(2 * current.X - lastCubicControl.X, 2 * current.Y - lastCubicControl.Y) : current;
                        Point two = Pair(relative), end = Pair(relative); Cubic(one, two, end); previousCommand = command; break;
                    }
                    case 'Q':
                    {
                        Point quad = Pair(relative), end = Pair(relative);
                        Point one = new(current.X + 2.0 / 3 * (quad.X - current.X), current.Y + 2.0 / 3 * (quad.Y - current.Y));
                        Point two = new(end.X + 2.0 / 3 * (quad.X - end.X), end.Y + 2.0 / 3 * (quad.Y - end.Y));
                        Cubic(one, two, end); lastQuadControl = quad; previousCommand = command; break;
                    }
                    case 'T':
                    {
                        Point quad = char.ToUpperInvariant(previousCommand) is 'Q' or 'T' ? new Point(2 * current.X - lastQuadControl.X, 2 * current.Y - lastQuadControl.Y) : current;
                        Point end = Pair(relative);
                        Point one = new(current.X + 2.0 / 3 * (quad.X - current.X), current.Y + 2.0 / 3 * (quad.Y - current.Y));
                        Point two = new(end.X + 2.0 / 3 * (quad.X - end.X), end.Y + 2.0 / 3 * (quad.Y - end.Y));
                        Cubic(one, two, end); lastQuadControl = quad; previousCommand = command; break;
                    }
                    case 'A':
                    {
                        double rx = Number(), ry = Number(), angle = Number(), large = Number(), sweep = Number();
                        if (large is not (0 or 1) || sweep is not (0 or 1)) throw new InvalidDataException("پرچم کمان SVG باید صفر یا یک باشد.");
                        Point end = Pair(relative); AppendArc(path ?? throw new InvalidDataException("مسیر SVG باید با M شروع شود."), current, end, rx, ry, angle, large == 1, sweep == 1);
                        current = end; previousCommand = command; totalNodes += 4; CheckCount(); break;
                    }
                    default: throw new NotSupportedException("فرمان مسیر SVG پشتیبانی نمی‌شود: " + command);
                }
                CheckCount();
            }
            if (path is { Nodes.Count: > 0 }) paths.Add(path);
            return paths;
        }

        private static bool IsCommand(string token) => token.Length == 1 && char.IsLetter(token[0]);

        private static void AppendArc(VectorDesignPath path, Point from, Point to, double rx, double ry, double rotation, bool largeArc, bool sweep)
        {
            rx = Math.Abs(rx); ry = Math.Abs(ry);
            if (rx < 1e-9 || ry < 1e-9 || (from - to).Length < 1e-9) { path.Nodes[^1].CurveToNext = false; path.Nodes.Add(Node(to.X, to.Y)); return; }
            double phi = rotation * Math.PI / 180, cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (from.X - to.X) / 2, dy = (from.Y - to.Y) / 2;
            double xp = cos * dx + sin * dy, yp = -sin * dx + cos * dy;
            double scale = xp * xp / (rx * rx) + yp * yp / (ry * ry);
            if (scale > 1) { double factor = Math.Sqrt(scale); rx *= factor; ry *= factor; }
            double numerator = Math.Max(0, rx * rx * ry * ry - rx * rx * yp * yp - ry * ry * xp * xp);
            double denominator = rx * rx * yp * yp + ry * ry * xp * xp;
            double coefficient = denominator < 1e-12 ? 0 : Math.Sqrt(numerator / denominator) * (largeArc == sweep ? -1 : 1);
            double cxp = coefficient * rx * yp / ry, cyp = coefficient * -ry * xp / rx;
            double cx = cos * cxp - sin * cyp + (from.X + to.X) / 2;
            double cy = sin * cxp + cos * cyp + (from.Y + to.Y) / 2;
            double ux = (xp - cxp) / rx, uy = (yp - cyp) / ry;
            double vx = (-xp - cxp) / rx, vy = (-yp - cyp) / ry;
            double theta = Math.Atan2(uy, ux), delta = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            if (!sweep && delta > 0) delta -= 2 * Math.PI;
            if (sweep && delta < 0) delta += 2 * Math.PI;
            int segments = Math.Clamp((int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2)), 1, 4);
            double step = delta / segments;
            Point EllipsePoint(double angle) => new(cx + cos * rx * Math.Cos(angle) - sin * ry * Math.Sin(angle), cy + sin * rx * Math.Cos(angle) + cos * ry * Math.Sin(angle));
            Point Derivative(double angle) => new(-cos * rx * Math.Sin(angle) - sin * ry * Math.Cos(angle), -sin * rx * Math.Sin(angle) + cos * ry * Math.Cos(angle));
            for (int i = 0; i < segments; i++)
            {
                double a = theta + i * step, b = a + step, k = 4.0 / 3 * Math.Tan((b - a) / 4);
                Point p1 = EllipsePoint(a), p2 = EllipsePoint(b), d1 = Derivative(a), d2 = Derivative(b);
                var fromNode = path.Nodes[^1]; fromNode.CurveToNext = true; fromNode.HasOut = true; fromNode.OutX = p1.X + k * d1.X; fromNode.OutY = p1.Y + k * d1.Y;
                var endNode = Node(p2.X, p2.Y); endNode.HasIn = true; endNode.InX = p2.X - k * d2.X; endNode.InY = p2.Y - k * d2.Y;
                path.Nodes.Add(endNode);
            }
            path.Nodes[^1].X = to.X; path.Nodes[^1].Y = to.Y;
        }
    }
}
