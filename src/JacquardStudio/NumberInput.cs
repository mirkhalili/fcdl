using System.Globalization;

namespace JacquardStudio;

/// <summary>Accept Persian, Arabic-Indic, and ASCII digits without changing user-entered text.</summary>
public static class NumberInput
{
    public static string Normalize(string text) => new(text.Select(c => c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c == '،' ? ',' : c).ToArray());
    public static bool TryInt(string text, out int value) => int.TryParse(Normalize(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
