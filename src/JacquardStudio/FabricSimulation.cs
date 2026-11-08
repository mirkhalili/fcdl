using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

public sealed record SimulatedYarn(string Id, int Yarn, string Color);
// Legacy single-weft fields remain as defaults when opening SAM v6.
public sealed record SimulationSettings(int WarpYarn = 0, int WeftYarn = 1, string WarpColor = "#DABFA0", string WeftColor = "#537481", int Weave = 0, int Shadow = 60, SimulatedYarn[]? Wefts = null, int Mode = 1);

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
    public static void Validate(SimulationSettings s)
    {
        if ((uint)s.WarpYarn >= YarnNames.Length || (uint)s.WeftYarn >= YarnNames.Length || s.Weave is < 0 or > 2 || s.Shadow is < 0 or > 100 || s.Mode is < 0 or > 2)
            throw new ArgumentException("Invalid simulation settings.");
        ParseColor(s.WarpColor); ParseColor(s.WeftColor);
        if (s.Wefts is { } list)
        {
            if (list.Length > 256 || list.Any(y => y is null || string.IsNullOrWhiteSpace(y.Id) || y.Id.Length > 32 || (uint)y.Yarn >= YarnNames.Length) || list.Select(y => y.Id).Distinct(StringComparer.Ordinal).Count() != list.Length) throw new ArgumentException("Invalid simulated wefts.");
            foreach (var y in list) ParseColor(y.Color);
        }
    }
    public static bool WarpOnTop(byte[] map, int w, int x, int y, int weave) => weave switch { 1 => (x + y) % 2 == 0, 2 => (x + y) % 4 == 0, _ => map[y * w + x] == 0 };
    /// <summary>Procedural, height-shaded yarn appearance, not a physical cloth simulation.</summary>
    public static BitmapSource Render(BitmapSource fabric, SimulationSettings s, IReadOnlyList<string>? rowWefts = null)
    {
        Validate(s); if (s.Mode == 2) throw new ArgumentException("3D mode requires a captured 3D viewport."); Color warp = ParseColor(s.WarpColor); int w = fabric.PixelWidth, h = fabric.PixelHeight; byte[] map = DesignAnalysis.ReadBinary(fabric);
        if (rowWefts is { Count: > 0 } && rowWefts.Count != h) throw new ArgumentException("Weft sequence does not match fabric rows.");
        var rowColors = new Color[h]; var rowYarns = new int[h];
        for (int y = 0; y < h; y++) { var yarn = YarnFor(s, rowWefts is { Count: > 0 } ? rowWefts[y] : "1"); rowColors[y] = ParseColor(yarn.Color); rowYarns[y] = yarn.Yarn; }
        double factor = Math.Min(16.0, 2400.0 / Math.Max(w, h));
        int outW = Math.Max(1, (int)Math.Round(w * factor)), outH = Math.Max(1, (int)Math.Round(h * factor));
        var pixels = new byte[checked(outW * outH * 4)]; double shade = s.Shadow / 100.0;
        for (int py = 0; py < outH; py++)
        for (int px = 0; px < outW; px++)
        {
            double fx = (px + .5) * w / outW, fy = (py + .5) * h / outH;
            int x = Math.Min(w - 1, (int)fx), y = Math.Min(h - 1, (int)fy); double u = fx - x, v = fy - y;
            bool warpTop = WarpOnTop(map, w, x, y, s.Weave);
            // A cylindrical top thread and the lower crossing thread; exposed edges reveal the lower yarn.
            double topCross = warpTop ? u : v; bool topVisible = Math.Abs(topCross - .5) < .46;
            bool useWarp = topVisible ? warpTop : !warpTop;
            Color c = useWarp ? warp : rowColors[y]; int yarn = useWarp ? s.WarpYarn : rowYarns[y];
            if (s.Mode == 0)
            {
                // Flat diagram of the actual weave: broad colored yarns with a dark outline.
                bool topEdge = Math.Abs(Math.Abs(topCross - .5) - .46) < .045;
                Color flat = topEdge ? Color.FromRgb(32, 32, 32) : c; int q = (py * outW + px) * 4;
                pixels[q] = flat.B; pixels[q + 1] = flat.G; pixels[q + 2] = flat.R; pixels[q + 3] = 255; continue;
            }
            double cross = useWarp ? u : v, axial = useWarp ? fy : fx;
            double d = Math.Clamp((cross - .5) / .5, -1, 1); double normal = Math.Sqrt(Math.Max(0, 1 - d * d));
            double directional = Math.Clamp(.68 * normal - .25 * d + .35, .2, 1.15);
            double occlusion = !topVisible ? .32 * shade : shade * .13 * Math.Pow(Math.Abs((useWarp ? v : u) - .5) * 2, 4);
            double specular = yarn == 0 ? .22 * shade * Math.Pow(Math.Max(0, normal - .22 * d), 12) : .035 * shade * Math.Pow(normal, 5);
            double twist = Math.Sin(axial * 8.6 + cross * 28 + (useWarp ? x : y) * .73);
            int hash = unchecked((x * 73856093) ^ (y * 19349663) ^ ((int)(axial * 16) * 83492791) ^ ((int)(cross * 32) * 265443576));
            double noise = ((hash & 255) / 255.0 - .5);
            double fiber = shade * (yarn == 1 ? .16 : yarn == 3 ? .11 : yarn == 2 ? .075 : .035) * (noise + .38 * twist);
            double light = Math.Clamp(1 - shade * .28 + shade * .30 * directional - occlusion + specular + fiber, .20, 1.28);
            int i = (py * outW + px) * 4;
            pixels[i] = (byte)Math.Clamp((int)(c.B * light), 0, 255); pixels[i + 1] = (byte)Math.Clamp((int)(c.G * light), 0, 255); pixels[i + 2] = (byte)Math.Clamp((int)(c.R * light), 0, 255); pixels[i + 3] = 255;
        }
        var result = BitmapSource.Create(outW, outH, 96, 96, PixelFormats.Bgra32, null, pixels, outW * 4); result.Freeze(); return result;
    }
}
