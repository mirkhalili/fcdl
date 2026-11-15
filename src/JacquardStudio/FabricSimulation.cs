using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed record SimulatedYarn(string Id, int Yarn, string Color, string Count = "30", int Unit = 0);
// Legacy single-weft fields remain as defaults when opening SAM v6.
public sealed record SimulationSettings(int WarpYarn = 0, int WeftYarn = 1, string WarpColor = "#DABFA0", string WeftColor = "#537481", int Weave = 0, int Shadow = 60, SimulatedYarn[]? Wefts = null, int Mode = 1, string WarpCount = "30", int WarpUnit = 0, string WarpDensity = "40", string WeftDensity = "40", string MaterialDensity = "1.0", bool ReverseSide = false, int RepeatX = 1, int RepeatY = 1);

public static class FabricSimulation
{
    public const int PreviewPixelsPerThread = 48;
    public const int MaxPreviewDimension = 2400;
    public const string ReferenceWarpColor = "#E21B27";
    public const string ReferenceWeftColor = "#F4F2EE";
    public static readonly string[] YarnNames = { "فیلامنت / Filament", "شانل / Chenille", "پنبه / Cotton", "پشم / Wool" };
    public static Color ParseColor(string value)
    {
        string text = NumberInput.Normalize(value.Trim()).TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int color))
            throw new ArgumentException("رنگ نخ باید شش رقم هگزادسیمال باشد (مانند #DABFA0). / Yarn colors must be six hexadecimal digits.");
        return Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }

    /// <summary>One-click reference setup: red warp, ivory weft, and a softly textured plain weave.</summary>
    public static SimulationSettings CreateReferencePreset(SimulationSettings current, IReadOnlyList<string>? weftIds = null)
    {
        var yarns = (current.Wefts ?? Array.Empty<SimulatedYarn>()).ToDictionary(yarn => yarn.Id, StringComparer.Ordinal);
        string[] ids = (weftIds is { Count: > 0 } ? weftIds : new[] { "1" })
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) ids = new[] { "1" };
        foreach (string id in ids) yarns[id] = new SimulatedYarn(id, 2, ReferenceWeftColor, "45", 0);
        return current with
        {
            WarpYarn = 2,
            WeftYarn = 2,
            WarpColor = ReferenceWarpColor,
            WeftColor = ReferenceWeftColor,
            Weave = 1,
            Shadow = 78,
            Mode = 1,
            WarpCount = "45",
            WarpUnit = 0,
            WarpDensity = "36",
            WeftDensity = "36",
            MaterialDensity = "1.0",
            ReverseSide = false,
            Wefts = yarns.Values.ToArray(),
        };
    }

    public static string[] WeftIds(WeftSettings settings)
    {
        if (!settings.BoxEnabled) return new[] { "1" };
        if (settings.AbsoluteLayout)
        {
            if (!NumberInput.TryInt(settings.WeftCount, out int count) || count is < 1 or > 256) throw new ArgumentException("Invalid applied weft count.");
            return Enumerable.Range(1, count).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToArray();
        }
        var ids = NumberInput.Normalize(settings.BoxCodes).Split(new[] {'\r', '\n'}, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2)[0].Trim()).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length is < 1 or > 256) throw new ArgumentException("Invalid applied weft codes.");
        return ids;
    }
    public static SimulatedYarn YarnFor(SimulationSettings s, string id) => s.Wefts?.FirstOrDefault(y => y.Id == id) ?? new SimulatedYarn(id, s.WeftYarn, s.WeftColor);
    public static double Number(string value)
    {
        if (value is null || !double.TryParse(NumberInput.Normalize(value).Replace('٫', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n)) throw new ArgumentException("Enter a valid finite number. / عدد معتبر وارد کنید.");
        return n;
    }
    public static double WarpDiameter(SimulationSettings s) => YarnGeometry.DiameterMm(YarnGeometry.ToTex(Number(s.WarpCount), s.WarpUnit), Number(s.MaterialDensity));
    public static double WeftDiameter(SimulationSettings s, SimulatedYarn yarn) => YarnGeometry.DiameterMm(YarnGeometry.ToTex(Number(yarn.Count), yarn.Unit), Number(s.MaterialDensity));
    public static void Validate(SimulationSettings s)
    {
        if ((uint)s.WarpYarn >= YarnNames.Length || (uint)s.WeftYarn >= YarnNames.Length || s.Weave is < 0 or > 2 || s.Shadow is < 0 or > 100 || s.Mode is < 0 or > 2 || s.RepeatX is < 1 or > 16 || s.RepeatY is < 1 or > 16)
            throw new ArgumentException("Invalid simulation settings.");
        ParseColor(s.WarpColor); ParseColor(s.WeftColor); _ = WarpDiameter(s); _ = YarnGeometry.SpacingMm(Number(s.WarpDensity)); _ = YarnGeometry.SpacingMm(Number(s.WeftDensity));
        if (s.Wefts is { } list)
        {
            if (list.Length > 256 || list.Any(y => y is null || string.IsNullOrWhiteSpace(y.Id) || y.Id.Length > 32 || (uint)y.Yarn >= YarnNames.Length) || list.Select(y => y.Id).Distinct(StringComparer.Ordinal).Count() != list.Length) throw new ArgumentException("Invalid simulated wefts.");
            foreach (var y in list) { ParseColor(y.Color); _ = WeftDiameter(s, y); }
        }
    }
    public static bool WarpOnTop(byte[] map, int w, int x, int y, int weave) => weave switch { 1 => ((x + y) & 1) == 0, 2 => ((x + y) & 3) < 2, _ => map[y * w + x] == 255 };
    /// <summary>Procedural, height-shaded yarn appearance, not a physical cloth simulation.</summary>
    public static BitmapSource Render(BitmapSource fabric, SimulationSettings s, IReadOnlyList<string>? rowWefts = null)
    {
        Validate(s); if (s.Mode == 2) throw new ArgumentException("3D mode requires a captured 3D viewport."); Color warp = ParseColor(s.WarpColor); int w = fabric.PixelWidth, h = fabric.PixelHeight; byte[] map = DesignAnalysis.ReadBinary(fabric);
        if (rowWefts is { Count: > 0 } && rowWefts.Count != h) throw new ArgumentException("Weft sequence does not match fabric rows.");
        double dx = YarnGeometry.SpacingMm(Number(s.WarpDensity)), dy = YarnGeometry.SpacingMm(Number(s.WeftDensity));
        double warpRatio = Math.Min(.98, WarpDiameter(s) / dx);
        var rowColors = new Color[h]; var rowYarns = new int[h]; var rowRatios = new double[h];
        var cache = new Dictionary<string, (Color color, int type, double ratio)>(StringComparer.Ordinal);
        for (int y = 0; y < h; y++)
        {
            string id = rowWefts is { Count: > 0 } ? rowWefts[y] : "1";
            if (!cache.TryGetValue(id, out var properties)) { var yarn = YarnFor(s, id); properties = (ParseColor(yarn.Color), yarn.Yarn, Math.Min(.98, WeftDiameter(s, yarn) / dy)); cache[id] = properties; }
            (rowColors[y], rowYarns[y], rowRatios[y]) = properties;
        }
        int totalW=checked(w*s.RepeatX), totalH=checked(h*s.RepeatY);
        double factor = Math.Min(PreviewPixelsPerThread / Math.Min(dx, dy), MaxPreviewDimension / Math.Max(totalW * dx, totalH * dy));
        int outW = Math.Max(1, (int)Math.Round(totalW * dx * factor)), outH = Math.Max(1, (int)Math.Round(totalH * dy * factor));
        bool TopAt(int gridX, int gridY)
        {
            int localX = gridX % w, localY = gridY % h;
            if (s.ReverseSide)
            {
                localX = w - 1 - localX;
                gridX = totalW - 1 - gridX;
            }
            // Project weave repeats its bitmap; built-in structures keep their phase across repeats.
            int patternX = s.Weave == 0 ? localX : gridX;
            int patternY = s.Weave == 0 ? localY : gridY;
            bool top = WarpOnTop(map, w, patternX, patternY, s.Weave);
            return s.ReverseSide ? !top : top;
        }
        var pixels = new byte[checked(outW * outH * 4)]; double shade = s.Shadow / 100.0;
        double antialiasX = Math.Max(.002, totalW / (double)outW * .55);
        double antialiasY = Math.Max(.002, totalH / (double)outH * .55);
        Color ground = Color.FromRgb(247, 245, 240);
        double wr = warpRatio / 2;
        for (int py = 0; py < outH; py++)
        for (int px = 0; px < outW; px++)
        {
            double fx = (px + .5) * totalW / outW, fy = (py + .5) * totalH / outH;
            int gx = Math.Min(totalW - 1, (int)fx), gy = Math.Min(totalH - 1, (int)fy);
            int y = gy % h;
            double u = fx - gx, v = fy - gy;
            double warpCross = Math.Abs(u - .5), weftCross = Math.Abs(v - .5);
            double fr = rowRatios[y] / 2;
            double warpCoverage = 1 - SmoothStep(wr - antialiasX, wr + antialiasX, warpCross);
            double weftCoverage = 1 - SmoothStep(fr - antialiasY, fr + antialiasY, weftCross);
            bool warpTop = TopAt(gx, gy);

            // Shade the under-thread at its crossing and at the point where it disappears below a neighboring thread.
            double warpOcclusion = 0, weftOcclusion = 0;
            if (warpTop)
                weftOcclusion = Math.Exp(-Math.Max(0, warpCross - wr) * 18) * .22 * shade;
            else
            {
                warpOcclusion = Math.Exp(-Math.Max(0, weftCross - fr) * 18) * .22 * shade;
                if (gx > 0 && TopAt(gx - 1, gy))
                    weftOcclusion = Math.Max(weftOcclusion, Math.Exp(-Math.Max(0, u + .5 - wr) * 12) * .20 * shade);
                if (gx + 1 < totalW && TopAt(gx + 1, gy))
                    weftOcclusion = Math.Max(weftOcclusion, Math.Exp(-Math.Max(0, 1.5 - wr - u) * 12) * .20 * shade);
            }

            double gapDistance = Math.Max(0, Math.Min(warpCross - wr, weftCross - fr));
            double gapShade = Math.Exp(-gapDistance * 24) * .075 * shade;
            Color backdrop = ScaleColor(ground, 1 - gapShade);
            Color warpSurface = ShadeYarn(warp, s.WarpYarn, fy, u, wr, shade, warpOcclusion, s.Mode == 1, gx);
            Color weftSurface = ShadeYarn(rowColors[y], rowYarns[y], fx, v, fr, shade, weftOcclusion, s.Mode == 1, gy);

            // Antialiased coverage gives each strand a rounded silhouette; blend back-to-front to preserve the weave map.
            Color result = warpTop
                ? Blend(Blend(backdrop, weftSurface, weftCoverage), warpSurface, warpCoverage)
                : Blend(Blend(backdrop, warpSurface, warpCoverage), weftSurface, weftCoverage);

            // Short wisps along the yarn edges suggest loose cotton/wool fibers without turning the cloth into pixel noise.
            double warpGap = warpCross - wr, weftGap = weftCross - fr;
            double nearestGap = Math.Min(warpGap, weftGap);
            if (s.Mode == 1 && nearestGap > 0 && nearestGap < .035 && (s.WarpYarn != 0 || rowYarns[y] != 0))
            {
                uint fuzz = HashNoise(px, py, gx + gy * 31);
                if ((fuzz & 255) < 38) result = Blend(result, warpGap < weftGap ? warp : rowColors[y], .13);
            }

            int q = (py * outW + px) * 4;
            pixels[q] = result.B; pixels[q + 1] = result.G; pixels[q + 2] = result.R; pixels[q + 3] = 255;
        }
        var resultBitmap = BitmapSource.Create(outW, outH, 96, 96, PixelFormats.Bgra32, null, pixels, outW * 4); resultBitmap.Freeze(); return resultBitmap;
    }

    private static Color ShadeYarn(Color color, int yarn, double axial, double across, double halfWidth, double shadow, double occlusion, bool fibers, int thread)
    {
        double d = Math.Clamp((across - .5) / Math.Max(.00001, halfWidth), -1, 1);
        double normal = Math.Sqrt(Math.Max(0, 1 - d * d));
        double light = .78 + .24 * normal + Math.Max(0, -d) * .035 - (1 - normal) * .17 * shadow - occlusion;
        if (fibers)
        {
            double phase = axial * Math.PI * 2 * 8 + d * 2.8 + Math.Sin(axial * Math.PI * 1.5 + thread * .41) * .22;
            double strand = Math.Cos(phase);
            double highlight = Math.Pow(Math.Max(0, strand), 8);
            double groove = Math.Pow(Math.Max(0, -strand), 8);
            int fiberX = (int)Math.Floor(axial * 28 + d * .8);
            int fiberY = (int)Math.Floor((d + 1) * 18);
            double grain = ((HashNoise(fiberX, fiberY, thread + yarn * 97) & 255) / 255.0) - .5;
            double slub = ((HashNoise((int)Math.Floor(axial * 5), yarn, thread + 313) & 255) / 255.0) - .5;
            double strength = yarn switch { 0 => .035, 1 => .16, 2 => .11, 3 => .14, _ => .10 };
            light += strength * (.52 * highlight - .34 * groove + .30 * grain + .18 * slub);
        }
        light = Math.Clamp(light, .45, 1.2);
        double sheen = .11 * shadow * Math.Pow(normal, 7);
        return Color.FromRgb(ToByte(color.R * light + (255 - color.R) * sheen),
            ToByte(color.G * light + (255 - color.G) * sheen), ToByte(color.B * light + (255 - color.B) * sheen));
    }

    private static uint HashNoise(int x, int y, int seed)
    {
        unchecked
        {
            uint value = (uint)x * 0x9E3779B1u ^ (uint)y * 0x85EBCA77u ^ (uint)seed * 0xC2B2AE3Du;
            value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15; value *= 0x846CA68Bu; value ^= value >> 16;
            return value;
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    private static Color ScaleColor(Color color, double scale) => Color.FromRgb(ToByte(color.R * scale), ToByte(color.G * scale), ToByte(color.B * scale));

    private static Color Blend(Color a, Color b, double amount)
    {
        double t = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(ToByte(a.R + (b.R - a.R) * t), ToByte(a.G + (b.G - a.G) * t), ToByte(a.B + (b.B - a.B) * t));
    }

    private static double SmoothStep(double edge0, double edge1, double value)
    {
        if (edge1 <= edge0) return value < edge0 ? 0 : 1;
        double t = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
