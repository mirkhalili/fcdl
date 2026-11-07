using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

// IDs refer to the built-in yarn bank; all settings are stored in SAM, not in the machine output.
public sealed record SimulationSettings(int WarpYarn = 0, int WeftYarn = 1, string WarpColor = "#DABFA0", string WeftColor = "#537481", int Weave = 0, int Shadow = 60);

public static class FabricSimulation
{
    public static readonly string[] YarnNames = { "فیلامنت / Filament", "شانل / Chenille", "پنبه / Cotton", "پشم / Wool" };
    private static Color ParseColor(string value)
    {
        string text = NumberInput.Normalize(value.Trim()).TrimStart('#');
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int color))
            throw new ArgumentException("رنگ نخ باید شش رقم هگزادسیمال باشد (مانند #DABFA0). / Yarn colors must be six hexadecimal digits.");
        return Color.FromRgb((byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }
    public static void Validate(SimulationSettings s)
    {
        if ((uint)s.WarpYarn >= YarnNames.Length || (uint)s.WeftYarn >= YarnNames.Length || s.Weave is < 0 or > 2 || s.Shadow is < 0 or > 100)
            throw new ArgumentException("Invalid simulation settings.");
        ParseColor(s.WarpColor); ParseColor(s.WeftColor);
    }
    /// <summary>Stylized over/under yarn preview; not a physical simulation or a loom map.</summary>
    public static BitmapSource Render(BitmapSource fabric, SimulationSettings s)
    {
        Validate(s); Color warp = ParseColor(s.WarpColor), weft = ParseColor(s.WeftColor);
        int w = fabric.PixelWidth, h = fabric.PixelHeight; byte[] map = DesignAnalysis.ReadBinary(fabric);
        double factor = Math.Min(8.0, 1200.0 / Math.Max(w, h));
        int outW = Math.Max(1, (int)Math.Round(w * factor)), outH = Math.Max(1, (int)Math.Round(h * factor));
        var pixels = new byte[checked(outW * outH * 4)]; double shade = s.Shadow / 100.0;
        for (int py = 0; py < outH; py++)
        for (int px = 0; px < outW; px++)
        {
            double fx = (px + .5) * w / outW, fy = (py + .5) * h / outH;
            int x = Math.Min(w - 1, (int)fx), y = Math.Min(h - 1, (int)fy);
            bool warpOnTop = s.Weave switch { 1 => (x + y) % 2 == 0, 2 => (x + y) % 4 == 0, _ => map[y * w + x] == 0 };
            Color c = warpOnTop ? warp : weft; int yarn = warpOnTop ? s.WarpYarn : s.WeftYarn;
            double cross = warpOnTop ? fx - x : fy - y; double axial = warpOnTop ? fy : fx;
            double round = Math.Sqrt(Math.Max(0, 1 - Math.Pow(2 * cross - 1, 2)));
            double baseLight = 1 - shade * .34 * (1 - round);
            double shine = yarn == 0 ? shade * .23 * Math.Pow(round, 8) : 0;
            // Reproducible, approximate surface variation; chenille/wool are softer and irregular.
            int hash = unchecked((x * 73856093) ^ (y * 19349663) ^ ((int)(axial * 8) * 83492791));
            double grain = ((hash & 255) / 255.0 - .5) * shade * (yarn == 1 ? .24 : yarn == 3 ? .14 : .04);
            double light = Math.Clamp(baseLight + shine + grain, 0, 1.35); int i = (py * outW + px) * 4;
            pixels[i] = (byte)Math.Clamp((int)(c.B * light), 0, 255); pixels[i + 1] = (byte)Math.Clamp((int)(c.G * light), 0, 255); pixels[i + 2] = (byte)Math.Clamp((int)(c.R * light), 0, 255); pixels[i + 3] = 255;
        }
        var result = BitmapSource.Create(outW, outH, 96, 96, PixelFormats.Bgra32, null, pixels, outW * 4); result.Freeze(); return result;
    }
}
