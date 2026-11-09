using System.IO;
using System.Windows;
using System.Windows.Media;

namespace JacquardStudio;
public static class ThemeManager
{
    public static bool IsLight { get; private set; }
    public static event EventHandler? Changed;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JacquardStudio", "theme.txt");
    public static bool ReadLight()
    {
        try { return File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Trim() == "light"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    public static void Apply(bool light, bool persist)
    {
        IsLight = light;
        var app = Application.Current ?? throw new InvalidOperationException("Application resources are unavailable.");
        var colors = light
            ? new[] { "#F2F5F8", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#C9D4DD", "#536778", "#147F6E", "#172B3B", "#E3EAF0", "#BAC9D4", "#E8EFF3", "#EDF2F5" }
            : new[] { "#0C1420", "#111B29", "#0B121D", "#161F2D", "#2B3A4E", "#9BAFC3", "#54E0C2", "#E5EEF6", "#263749", "#374C60", "#172432", "#111B29" };
        string[] keys = { "AppBackground", "HeaderBackground", "CanvasBackground", "Surface", "Border", "Muted", "Accent", "PrimaryText", "ButtonBackground", "ButtonBorder", "GalleryBackground", "ListBackground" };
        for (int i = 0; i < keys.Length; i++) app.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        string[] graphKeys = { "GraphBackground", "GraphGrid", "GraphHeader", "GraphSurface", "GraphNode", "GraphNodeHeader", "GraphPreview", "GraphSelection", "GraphSelectedItem" };
        string[] graphColors = light
            ? new[] { "#DFE2E4", "#C5C9CD", "#F0F1F2", "#E9EBED", "#F8F9FA", "#E2E6E9", "#CDD2D5", "#A95B16", "#BDDAED" }
            : new[] { "#242424", "#343434", "#303030", "#292929", "#383838", "#303030", "#252525", "#F39A4A", "#485761" };
        for (int i = 0; i < graphKeys.Length; i++) app.Resources[graphKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(graphColors[i]));
        app.Resources["ActionBackground"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(light ? "#176956" : "#287764"));
        Changed?.Invoke(null, EventArgs.Empty);
        if (!persist) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, light ? "light" : "dark"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* The in-memory theme still works. */ }
    }
}
