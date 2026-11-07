namespace JacquardStudio;

/// <summary>Small, local hook-map presets reconstructed from the supplied pattern chart.</summary>
public static class PatternLibrary
{
    public sealed record Preset(string Name, WeavePattern Pattern);
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
        new Preset("s2-6z_clb", FromRows("......XX", ".....XX.", "....XX..", "...XX...", "..XX....", ".XX.....", "XX......", "X......X"))
    };
}
