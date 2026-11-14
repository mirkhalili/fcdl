namespace JacquardStudio;

/// <summary>Small, local hook-map presets reconstructed from the supplied pattern chart.</summary>
public static class PatternLibrary
{
    public sealed record Preset(string Name, WeavePattern Pattern, string Category = "مرجع قدیمی / Legacy");
    private static WeavePattern Twill(int warp, int weft, int direction = 1)
    {
        int n = warp + weft;
        return FromRows(Enumerable.Range(0, n).Select(y => new string(Enumerable.Range(0, n).Select(x => ((x + direction * y + n * 2) % n) < warp ? 'X' : '.').ToArray())).ToArray());
    }
    private static WeavePattern Basket(int xGroup, int yGroup)
        => FromRows(Enumerable.Range(0, yGroup * 2).Select(y => new string(Enumerable.Range(0, xGroup * 2).Select(x => ((x / xGroup) + (y / yGroup)) % 2 == 0 ? 'X' : '.').ToArray())).ToArray());
    private static WeavePattern SteepTwill() => FromRows(Enumerable.Range(0, 8).Select(y => new string(Enumerable.Range(0, 4).Select(x => (x + y / 2) % 4 < 2 ? 'X' : '.').ToArray())).ToArray());
    private static WeavePattern Satin(int repeat, int step)
        => FromRows(Enumerable.Range(0, repeat).Select(y => new string(Enumerable.Range(0, repeat).Select(x => x == y * step % repeat ? '.' : 'X').ToArray())).ToArray());
    private static WeavePattern FromRows(params string[] rows)
    {
        int width = rows[0].Length;
        if (rows.Any(row => row.Length != width)) throw new ArgumentException("Uneven preset rows.");
        return new WeavePattern(width, rows.Length, rows.SelectMany(row => row.Select(c => c == 'X' ? (byte)0 : (byte)255)).ToArray());
    }
    public static IReadOnlyList<Preset> Presets { get; } = new[]
    {
        new Preset("p4-4_clb", FromRows("..XX", "..XX", "XX..", "XX..")),
        new Preset("s6-2z_clb", FromRows("..XXXXXX", ".XXXXXX.", "XXXXXX..", "XXXXX..X", "XXXX..XX", "XXX..XXX", "XX..XXXX", "X..XXXXX")),
        new Preset("s4-4z_clb", FromRows("....XXXX", "...XXXX.", "..XXXX..", ".XXXX...", "XXXX....", "XXX....X", "XX....XX", "X....XXX")),
        new Preset("s1-3z_clb", FromRows("...X", "..X.", ".X..", "X...")),
        new Preset("s2-6z_clb", FromRows("......XX", ".....XX.", "....XX..", "...XX...", "..XX....", ".XX.....", "XX......", "X......X")),
        new Preset("تافته ۱/۱ / Plain 1/1", Basket(1,1), "تافته / Plain"),
        new Preset("پاناما ۲×۲ / Basket 2×2", Basket(2,2), "پاناما / Basket"),
        new Preset("پاناما ۳×۳ / Basket 3×3", Basket(3,3), "پاناما / Basket"),
        new Preset("ریپس گروه ۲ در عرض / Rib 2×1", Basket(2,1), "ریپس / Rib"),
        new Preset("ریپس گروه ۲ در طول / Rib 1×2", Basket(1,2), "ریپس / Rib"),
        new Preset("ریپس گروه ۳ در عرض / Rib 3×1", Basket(3,1), "ریپس / Rib"),
        new Preset("سرژه ۲/۱ راست / Twill 2/1 Z", Twill(2,1), "سرژه / Twill"),
        new Preset("سرژه ۱/۲ راست / Twill 1/2 Z", Twill(1,2), "سرژه / Twill"),
        new Preset("سرژه ۲/۲ راست / Twill 2/2 Z", Twill(2,2), "سرژه / Twill"),
        new Preset("سرژه ۲/۲ چپ / Twill 2/2 S", Twill(2,2,-1), "سرژه / Twill"),
        new Preset("سرژه ۳/۱ راست / Twill 3/1 Z", Twill(3,1), "سرژه / Twill"),
        new Preset("گاباردین‌مانند سرژه ۲/۲ / Gabardine-style 2/2", SteepTwill(), "گاباردین‌مانند / Gabardine-style"),
        new Preset("ساتین ۵، گام ۲ / Satin 5 step 2", Satin(5,2), "ساتین / Satin"),
        new Preset("ساتین ۸، گام ۳ / Satin 8 step 3", Satin(8,3), "ساتین / Satin")
    };
}
