namespace JacquardStudio;

/// <summary>Interleave picks, preserving repeat periods; split is an exact inverse for valid composites.</summary>
public static class CompositeWeave
{
    public const int MaxWefts = 256;
    private static void Validate(WeavePattern pattern)
    {
        if (pattern.Width < 1 || pattern.Height < 1 || pattern.Width > WeaveEditor.MaxSide || pattern.Height > WeaveEditor.MaxSide || (long)pattern.Width * pattern.Height > DesignProcessor.MaxPatternPixels || pattern.Values.Length != (long)pattern.Width * pattern.Height || pattern.Values.Any(v => v != 0 && v != 255)) throw new ArgumentException("Invalid or excessive binary weave dimensions.");
    }
    private static int Lcm(int a, int b, int limit)
    {
        int x = a, y = b; while (y != 0) { int next = x % y; x = y; y = next; }
        long result = (long)a / x * b;
        if (result > limit) throw new ArgumentException("The common repeat is too large to edit. Reduce the weave dimensions or number of wefts.");
        return (int)result;
    }
    public static WeavePattern Combine(IReadOnlyList<WeavePattern> patterns)
    {
        if (patterns.Count is < 1 or > MaxWefts) throw new ArgumentException("Select 1–256 wefts.");
        int width = 1, repeatHeight = 1;
        foreach (var pattern in patterns)
        {
            Validate(pattern); width = Lcm(width, pattern.Width, WeaveEditor.MaxSide);
            repeatHeight = Lcm(repeatHeight, pattern.Height, WeaveEditor.MaxSide / patterns.Count);
        }
        int height = checked(repeatHeight * patterns.Count);
        if ((long)width * height > DesignProcessor.MaxPatternPixels) throw new ArgumentException("The combined weave exceeds the 262,144-pixel editor limit.");
        var values = new byte[checked(width * height)];
        for (int y = 0; y < height; y++)
        {
            var part = patterns[y % patterns.Count]; int sourceRow = (y / patterns.Count) % part.Height;
            for (int x = 0; x < width; x++) values[y * width + x] = part.Values[sourceRow * part.Width + x % part.Width];
        }
        return new WeavePattern(width, height, values);
    }
    public static WeavePattern[] Split(WeavePattern combined, int count)
    {
        Validate(combined);
        if (count is < 1 or > MaxWefts || combined.Height % count != 0 || combined.Height < count) throw new ArgumentException("The composite height must be a positive multiple of the number of wefts.");
        int height = combined.Height / count, width = combined.Width;
        var result = new WeavePattern[count];
        for (int part = 0; part < count; part++)
        {
            var data = new byte[checked(width * height)];
            for (int y = 0; y < height; y++) Buffer.BlockCopy(combined.Values, (y * count + part) * width, data, y * width, width);
            result[part] = new WeavePattern(width, height, data);
        }
        return result;
    }
}
