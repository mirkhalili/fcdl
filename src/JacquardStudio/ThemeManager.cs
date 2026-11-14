using System.IO;
using System.Windows;
using System.Windows.Media;

namespace JacquardStudio;
public static class ThemeManager
{
    public static bool IsLight { get; private set; }
    public static string Current { get; private set; }="dark";
    public static event EventHandler? Changed;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JacquardStudio", "theme.txt");
    public static bool ReadLight()
    {
        try { return File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Trim() == "light"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    public static void Apply(bool light, bool persist) => Apply(light ? "light" : "dark",persist);
    public static void Apply(string theme, bool persist)
    {
        if(theme is not ("dark" or "light" or "calm" or "ocean")) theme="dark";
        Current=theme; bool light=theme is "light" or "calm"; IsLight=light;
        var app = Application.Current ?? throw new InvalidOperationException("Application resources are unavailable.");
        var colors = light
            ? new[] { "#F2F5F8", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#C9D4DD", "#536778", "#147F6E", "#172B3B", "#E3EAF0", "#BAC9D4", "#E8EFF3", "#EDF2F5" }
            : new[] { "#1A1A1A", "#202020", "#1D1D1D", "#252525", "#3C3A38", "#ABA59F", "#C9A58B", "#E9E5E0", "#30302F", "#464341", "#292827", "#1F1F1F" };
        if(theme=="calm") colors=new[]{"#F4EFE8","#FCF9F4","#F3EEE6","#FBF8F2","#D7D0C5","#716D65","#557361","#26342E","#EAE5DC","#CFC8BD","#F0EBE3","#FFFCF7"};
        else if(theme=="ocean") colors=new[]{"#101D2C","#17293B","#0B1928","#1D3042","#385064","#A8BECC","#73C6CB","#E7F2F4","#294459","#456277","#233C4C","#142636"};
        string[] keys = { "AppBackground", "HeaderBackground", "CanvasBackground", "Surface", "Border", "Muted", "Accent", "PrimaryText", "ButtonBackground", "ButtonBorder", "GalleryBackground", "ListBackground" };
        for (int i = 0; i < keys.Length; i++) app.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        string[] graphKeys = { "GraphBackground", "GraphGrid", "GraphHeader", "GraphSurface", "GraphNode", "GraphNodeHeader", "GraphPreview", "GraphSelection", "GraphSelectedItem" };
        string[] graphColors = light
            ? new[] { "#DFE2E4", "#C5C9CD", "#F0F1F2", "#E9EBED", "#F8F9FA", "#E2E6E9", "#CDD2D5", "#A95B16", "#BDDAED" }
            : new[] { "#222222", "#31302F", "#1F1F1F", "#252525", "#343332", "#2B2A29", "#202020", "#C9A58B", "#3C3936" };
        if(theme=="calm") graphColors=new[]{"#EDE8DF","#DCD5C9","#F7F2EA","#F5F0E8","#FBF8F2","#E5E4D9","#EEEAE2","#557361","#D4DDD1"};
        else if(theme=="ocean") graphColors=new[]{"#142637","#253C4C","#1B3143","#1D3446","#294257","#20384A","#142635","#72C5C3","#365C6C"};
        for (int i = 0; i < graphKeys.Length; i++) app.Resources[graphKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(graphColors[i]));
        string actionBackground = theme switch { "calm" => "#EAE5DC", "ocean" => "#73C6CB", "light" => "#E3EAF0", _ => "#C9A58B" };
        string accentForeground = theme switch { "calm" => "#26342E", "ocean" => "#10252C", "light" => "#172B3B", _ => "#211D19" };
        string selectionFill = theme switch { "calm" => "#33557361", "ocean" => "#3373C6CB", "light" => "#33147F6E", _ => "#33C9A58B" };
        string dangerBackground = theme switch { "calm" => "#EBDDD8", "ocean" => "#543943", "light" => "#F4DFE1", _ => "#563638" };
        app.Resources["ActionBackground"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(actionBackground));
        app.Resources["AccentForeground"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentForeground));
        app.Resources["SelectionFill"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(selectionFill));
        app.Resources["DangerBackground"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dangerBackground));
        Changed?.Invoke(null, EventArgs.Empty);
        if (!persist) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, theme); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* The in-memory theme still works. */ }
    }
}
