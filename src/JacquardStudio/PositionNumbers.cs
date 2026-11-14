using System.Globalization;

namespace JacquardStudio;

/// <summary>UI positions are one-based; persisted/composer offsets remain zero-based for SAM compatibility.</summary>
public static class PositionNumbers
{
    public static string ToStored(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        if (!long.TryParse(NumberInput.Normalize(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)) return text;
        if (n == 0) return "-1"; // rejected by the composer; never silently reserve the first hook
        if (n < 0 || n > (long)int.MaxValue + 1) return text;
        return (n - 1).ToString(CultureInfo.InvariantCulture);
    }
    public static string ToDisplay(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        if (!long.TryParse(NumberInput.Normalize(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)) return text;
        if (n == -1) return "0"; // preserve an invalid zero draft
        if (n < 0 || n > int.MaxValue) return text;
        return (n + 1).ToString(CultureInfo.InvariantCulture);
    }
    public static string ListToStored(string text) => string.Join(",", NumberInput.Normalize(text).Split(',').Select(ToStored));
    public static string ListToDisplay(string text) => string.Join(",", NumberInput.Normalize(text).Split(',').Select(ToDisplay));
}
