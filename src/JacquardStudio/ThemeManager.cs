using System.IO;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace JacquardStudio;

/// <summary>The single, reference-matched Jacquard Studio design system. No legacy themes are shipped.</summary>
public static class ThemeManager
{
    public const string ThemeId = "jacquard";
    public static IReadOnlyList<(string Id, string Name)> AvailableThemes { get; } = Array.AsReadOnly(new[]
    {
        (ThemeId, "ژاکارد — آبی کبالت"),
    });
    public static bool IsLight => true;
    public static string Current => ThemeId;
    public static event EventHandler? Changed;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JacquardStudio", "theme.txt");

    /// <summary>Legacy preferences are read for compatibility, but the new design has one fixed palette.</summary>
    public static bool ReadLight() => true;
    public static void Apply(bool light, bool persist) => Apply(ThemeId, persist);

    public static void Apply(string theme, bool persist)
    {
        var app = Application.Current ?? throw new InvalidOperationException("Application resources are unavailable.");
        ApplicationThemeManager.Apply(Wpf.Ui.Appearance.ApplicationTheme.Light, WindowBackdropType.None, updateAccent: false);

        var colors = new Dictionary<string, Color>
        {
            ["AppBackground"] = Color.FromRgb(0xF4, 0xF6, 0xFA),
            ["HeaderBackground"] = Color.FromRgb(0xFB, 0xFC, 0xFE),
            ["CanvasBackground"] = Color.FromRgb(0xF8, 0xFA, 0xFD),
            ["Surface"] = Colors.White,
            ["Border"] = Color.FromRgb(0xE1, 0xE5, 0xEC),
            ["Muted"] = Color.FromRgb(0x70, 0x78, 0x87),
            ["Accent"] = Color.FromRgb(0x2C, 0x60, 0xF6),
            ["PrimaryText"] = Color.FromRgb(0x18, 0x1F, 0x2B),
            ["ButtonBackground"] = Color.FromRgb(0xF1, 0xF4, 0xF8),
            ["ButtonBorder"] = Color.FromRgb(0xD8, 0xE0, 0xEA),
            ["GalleryBackground"] = Color.FromRgb(0xF1, 0xF5, 0xFA),
            ["ListBackground"] = Colors.White,
            ["ActionBackground"] = Color.FromRgb(0x2C, 0x60, 0xF6),
            ["AccentForeground"] = Colors.White,
            ["SelectionFill"] = Color.FromArgb(0x33, 0x2C, 0x60, 0xF6),
            ["DangerBackground"] = Color.FromRgb(0xFC, 0xEB, 0xED),
            ["GraphBackground"] = Color.FromRgb(0xF8, 0xFA, 0xFD),
            ["GraphGrid"] = Color.FromRgb(0xE5, 0xEB, 0xF3),
            ["GraphHeader"] = Color.FromRgb(0xFB, 0xFC, 0xFE),
            ["GraphSurface"] = Colors.White,
            ["GraphNode"] = Colors.White,
            ["GraphNodeHeader"] = Color.FromRgb(0xF2, 0xF5, 0xFA),
            ["GraphPreview"] = Color.FromRgb(0xF4, 0xF6, 0xFA),
            ["GraphSelection"] = Color.FromRgb(0x2C, 0x60, 0xF6),
            ["GraphSelectedItem"] = Color.FromRgb(0xED, 0xF2, 0xFF),
            ["IconGlyph"] = Color.FromRgb(0x2C, 0x60, 0xF6),
            ["WindowBackground"] = Color.FromRgb(0xF4, 0xF6, 0xFA),
            ["WindowForeground"] = Color.FromRgb(0x18, 0x1F, 0x2B),
        };
        foreach (var (key, color) in colors) app.Resources[key] = new SolidColorBrush(color);
        ApplicationAccentColorManager.Apply(colors["Accent"], Wpf.Ui.Appearance.ApplicationTheme.Light);
        Changed?.Invoke(null, EventArgs.Empty);
        if (!persist) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, ThemeId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The fixed in-memory Jacquard palette remains active if preferences cannot be persisted.
        }
    }
}
