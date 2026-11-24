using System.IO;
using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace JacquardStudio;
public static class ThemeManager
{
    public static IReadOnlyList<(string Id,string Name)> AvailableThemes { get; } = Array.AsReadOnly(new (string Id,string Name)[]
    {
        ("dark","تیره"),
        ("light","روشن"),
        ("calm","آرام — سبز مریم‌گلی"),
        ("ocean","اقیانوس"),
        ("jacquard","ژاکارد — آبی کبالت"),
    });
    public static bool IsLight { get; private set; }
    public static string Current { get; private set; }="dark";
    public static event EventHandler? Changed;
    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JacquardStudio", "theme.txt");
    public static bool ReadLight()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return true;
            return File.ReadAllText(SettingsPath).Trim() is "light" or "calm" or "jacquard";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    public static void Apply(bool light, bool persist) => Apply(light ? "light" : "dark",persist);
    public static void Apply(string theme, bool persist)
    {
        if(!AvailableThemes.Any(candidate=>candidate.Id==theme)) theme="dark";
        Current=theme; bool light=theme is "light" or "calm" or "jacquard"; IsLight=light;
        var app = Application.Current ?? throw new InvalidOperationException("Application resources are unavailable.");
        var libraryTheme = light ? Wpf.Ui.Appearance.ApplicationTheme.Light : Wpf.Ui.Appearance.ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(libraryTheme, WindowBackdropType.None, updateAccent: false);
        var colors = light
            ? new[] { "#F2F5F8", "#FFFFFF", "#FFFFFF", "#FFFFFF", "#C9D4DD", "#536778", "#147F6E", "#172B3B", "#E3EAF0", "#BAC9D4", "#E8EFF3", "#EDF2F5" }
            : new[] { "#1A1A1A", "#202020", "#1D1D1D", "#252525", "#3C3A38", "#ABA59F", "#C9A58B", "#E9E5E0", "#30302F", "#464341", "#292827", "#1F1F1F" };
        if(theme=="calm") colors=new[]{"#F4EFE8","#FCF9F4","#F3EEE6","#FBF8F2","#D7D0C5","#716D65","#557361","#26342E","#EAE5DC","#CFC8BD","#F0EBE3","#FFFCF7"};
        else if(theme=="ocean") colors=new[]{"#101D2C","#17293B","#0B1928","#1D3042","#385064","#A8BECC","#73C6CB","#E7F2F4","#294459","#456277","#233C4C","#142636"};
        else if(theme=="jacquard") colors=new[]{"#F5F7FB","#FFFFFF","#FBFCFE","#FFFFFF","#DCE4F0","#586B84","#315FE7","#172A45","#EEF3FF","#CAD7EA","#F2F6FC","#FFFFFF"};
        string[] keys = { "AppBackground", "HeaderBackground", "CanvasBackground", "Surface", "Border", "Muted", "Accent", "PrimaryText", "ButtonBackground", "ButtonBorder", "GalleryBackground", "ListBackground" };
        for (int i = 0; i < keys.Length; i++) app.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        app.Resources["WindowBackground"] = app.Resources["AppBackground"];
        app.Resources["WindowForeground"] = app.Resources["PrimaryText"];
        string[] graphKeys = { "GraphBackground", "GraphGrid", "GraphHeader", "GraphSurface", "GraphNode", "GraphNodeHeader", "GraphPreview", "GraphSelection", "GraphSelectedItem" };
        string[] graphColors = light
            ? new[] { "#F1F3F5", "#D9DEE3", "#F8F9FA", "#FFFFFF", "#FFFFFF", "#F0F2F4", "#E2E7EA", "#202428", "#E5E9EC" }
            : new[] { "#222222", "#31302F", "#1F1F1F", "#252525", "#343332", "#2B2A29", "#202020", "#C9A58B", "#3C3936" };
        if(theme=="calm") graphColors=new[]{"#EDE8DF","#DCD5C9","#F7F2EA","#F5F0E8","#FBF8F2","#E5E4D9","#EEEAE2","#557361","#D4DDD1"};
        else if(theme=="ocean") graphColors=new[]{"#142637","#253C4C","#1B3143","#1D3446","#294257","#20384A","#142635","#72C5C3","#365C6C"};
        else if(theme=="jacquard") graphColors=new[]{"#F7F9FC","#D6E1F1","#FFFFFF","#FFFFFF","#FFFFFF","#EDF3FC","#EAF0FA","#315FE7","#E7EEFC"};
        for (int i = 0; i < graphKeys.Length; i++) app.Resources[graphKeys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(graphColors[i]));
        string actionBackground = theme switch { "calm" => "#EAE5DC", "ocean" => "#73C6CB", "jacquard" => "#315FE7", "light" => "#202428", _ => "#C9A58B" };
        string accentForeground = theme switch { "calm" => "#26342E", "ocean" => "#10252C", "jacquard" or "light" => "#FFFFFF", _ => "#211D19" };
        string selectionFill = theme switch { "calm" => "#33557361", "ocean" => "#3373C6CB", "jacquard" => "#33315FE7", "light" => "#33202428", _ => "#33C9A58B" };
        string dangerBackground = theme switch { "calm" => "#EBDDD8", "ocean" => "#543943", "jacquard" => "#FBE7EB", "light" => "#F4DFE1", _ => "#563638" };
        app.Resources["ActionBackground"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(actionBackground));
        app.Resources["AccentForeground"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(accentForeground));
        app.Resources["SelectionFill"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(selectionFill));
        app.Resources["DangerBackground"]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dangerBackground));
        ApplicationAccentColorManager.Apply((Color)ColorConverter.ConvertFromString(colors[6]), libraryTheme);
        Changed?.Invoke(null, EventArgs.Empty);
        if (!persist) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!); File.WriteAllText(SettingsPath, theme); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* The in-memory theme still works. */ }
    }
}
