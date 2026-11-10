using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed record SimulatedYarn(string Id, int Yarn, string Color, string Count = "30", int Unit = 0);
// Legacy single-weft fields remain as defaults when opening SAM v6.
public sealed record SimulationSettings(int WarpYarn = 0, int WeftYarn = 1, string WarpColor = "#DABFA0", string WeftColor = "#537481", int Weave = 0, int Shadow = 60, SimulatedYarn[]? Wefts = null, int Mode = 1, string WarpCount = "30", int WarpUnit = 0, string WarpDensity = "40", string WeftDensity = "40", string MaterialDensity = "1.0", bool ReverseSide = false, int RepeatX = 1, int RepeatY = 1);

public static class FabricSimulation
{
    public static readonly string[] YarnNames = { "فیلامنت / Filament", "شانل / Chenille", "پنبه / Cotton", "پشم / Wool" };
    public static Color ParseColor(string value)
    {
        string text = NumberInput.Normalize(value.Trim()).TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int color))
            throw new ArgumentException("رنگ نخ باید شش رقم هگزادسیمال باشد (مانند #DABFA0). / Yarn colors must be six hexadecimal digits.");
        return Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color);
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
    public static bool WarpOnTop(byte[] map, int w, int x, int y, int weave) => weave switch { 1 => (x + y) % 2 == 0, 2 => (x + y) % 4 == 0, _ => map[y * w + x] == 255 };
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
        double factor = Math.Min(16.0 / Math.Min(dx, dy), 2400.0 / Math.Max(totalW * dx, totalH * dy));
        int outW = Math.Max(1, (int)Math.Round(totalW * dx * factor)), outH = Math.Max(1, (int)Math.Round(totalH * dy * factor));
        bool TopAt(int gridX,int gridY)
        {
            int localX=gridX%w, localY=gridY%h; if(s.ReverseSide)localX=w-1-localX;
            bool top=WarpOnTop(map,w,localX,localY,s.Weave); return s.ReverseSide ? !top : top;
        }
        var pixels = new byte[checked(outW * outH * 4)]; double shade = s.Shadow / 100.0;
        for (int py = 0; py < outH; py++)
        for (int px = 0; px < outW; px++)
        {
            double fx = (px + .5) * totalW / outW, fy = (py + .5) * totalH / outH;
            int gx=Math.Min(totalW-1,(int)fx),gy=Math.Min(totalH-1,(int)fy); int x=gx%w,y=gy%h; double u=fx-gx,v=fy-gy;
            bool warpTop = TopAt(gx,gy);
            // Model actual projected widths, white openings, and the over/under structure.
            double warpCross = Math.Abs(u - .5), weftCross = Math.Abs(v - .5);
            double wr = warpRatio / 2, fr = rowRatios[y] / 2;
            bool warpPresent = warpCross < wr, weftPresent = weftCross < fr;
            int q = (py * outW + px) * 4;
            if (!warpPresent && !weftPresent)
            {
                // A slight darkening in the gap gives the threads a soft edge rather than an outline.
                double near = Math.Min(warpCross - wr, weftCross - fr); double shadow = Math.Exp(-Math.Max(0, near) * 24) * .09 * shade;
                Color gap = Color.FromRgb(248, 248, 248); double light = 1 - shadow;
                if (s.Mode == 1)
                {
                    int hash = unchecked((int)(fx * 31) * 73856093 ^ (int)(fy * 31) * 19349663);
                    bool fuzzWarp = warpCross - wr < .07 && (hash & 31) < (s.WarpYarn == 0 ? 1 : 4);
                    bool fuzzWeft = weftCross - fr < .07 && ((hash >> 5) & 31) < (rowYarns[y] == 0 ? 1 : 4);
                    if (fuzzWarp || fuzzWeft) { Color hair = fuzzWarp ? warp : rowColors[y]; gap = Color.FromRgb((byte)((gap.R*3 + hair.R)/4), (byte)((gap.G*3 + hair.G)/4), (byte)((gap.B*3 + hair.B)/4)); }
                }
                pixels[q] = (byte)(gap.B * light); pixels[q + 1] = (byte)(gap.G * light); pixels[q + 2] = (byte)(gap.R * light); pixels[q + 3] = 255; continue;
            }
            bool useWarp = warpPresent && (!weftPresent || warpTop);
            Color c = useWarp ? warp : rowColors[y]; int yarn = useWarp ? s.WarpYarn : rowYarns[y];
            double halfWidth = useWarp ? wr : fr, cross = useWarp ? u : v, axial = useWarp ? fy : fx;
            double d = Math.Clamp((cross - .5) / Math.Max(.00001, halfWidth), -1, 1);
            double normal = Math.Sqrt(Math.Max(0, 1 - d * d));
            double edgeShade = (1 - normal) * .14 * shade;
            double crossingShade = 0;
            if (!useWarp && weftPresent)
            {
                // Fade weft where it emerges from under a warp (current or neighbouring white cell).
                if (warpTop) crossingShade = Math.Exp(-Math.Max(0, warpCross - wr) * 18) * .20 * shade;
                else
                {
                    if (gx > 0 && TopAt(gx-1,gy)) crossingShade = Math.Max(crossingShade, Math.Exp(-Math.Max(0, u + .5 - wr) * 12) * .20 * shade);
                    if (gx + 1 < totalW && TopAt(gx+1,gy)) crossingShade = Math.Max(crossingShade, Math.Exp(-Math.Max(0, 1.5 - wr - u) * 12) * .20 * shade);
                }
            }
            double lightLevel = Math.Clamp(1 - edgeShade - crossingShade, .55, 1.0);
            if (s.Mode == 1)
            {
                // Fine deterministic fibers / hairiness; normal mode remains smooth.
                int hash = unchecked((x * 73856093) ^ (y * 19349663) ^ ((int)(axial * 24) * 83492791) ^ ((int)(cross * 48) * 265443576));
                double noise = (hash & 255) / 255.0 - .5;
                double fiber = yarn == 0 ? .025 : yarn == 1 ? .15 : yarn == 3 ? .13 : .09;
                lightLevel = Math.Clamp(lightLevel + fiber * (noise + .25 * Math.Sin(axial * 22 + cross * 60)), .45, 1.12);
            }
            pixels[q] = (byte)Math.Clamp((int)(c.B * lightLevel), 0, 255); pixels[q + 1] = (byte)Math.Clamp((int)(c.G * lightLevel), 0, 255); pixels[q + 2] = (byte)Math.Clamp((int)(c.R * lightLevel), 0, 255); pixels[q + 3] = 255;
        }
        var result = BitmapSource.Create(outW, outH, 96, 96, PixelFormats.Bgra32, null, pixels, outW * 4); result.Freeze(); return result;
    }
}
