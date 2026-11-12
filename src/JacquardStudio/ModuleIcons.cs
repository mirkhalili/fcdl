using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace JacquardStudio;
/// <summary>Vector module symbols shared by the workspace and library.</summary>
public static class ModuleIcons
{
    public static string PathFor(ModuleKind kind) => kind switch
    {
        ModuleKind.ExportData => "M5,7 L14,7 14,4 M14,4 L20,10 14,16 14,13 5,13 Z",
        ModuleKind.BinaryLoad => "M4,5 H20 V19 H4 Z M8,8 V16 M12,8 V16 M16,8 V16",
        ModuleKind.BinaryConvert => "M4,4 H11 V11 H4 Z M13,13 H20 V20 H13 Z M13,5 L20,5 M17,2 L20,5 17,8",
        ModuleKind.SeamCheck => "M4,5 H20 V19 H4 Z M12,5 V19 M9,9 L11,11 9,13 M15,9 L13,11 15,13",
        ModuleKind.Compare => "M4,6 H10 V18 H4 Z M14,6 H20 V18 H14 Z M12,4 V20",
        ModuleKind.QualityCheck => "M5,12 L10,17 20,6 M5,5 H19 V19 H5 Z",
        ModuleKind.Load => "M4,5 H20 V19 H4 Z M5,17 L10,11 13,14 16,10 20,16",
        ModuleKind.VectorLoad => "M5,18 L9,5 19,8 16,19 Z M5,18 L16,19 M9,5 L16,19",
        ModuleKind.MachineDefinition => "M12,3 V6 M12,18 V21 M3,12 H6 M18,12 H21 M7,7 H17 V17 H7 Z",
        ModuleKind.DesignIdentity => "M5,4 H19 V20 H5 Z M8,8 H16 M8,11 H16 M8,14 H14 M8,17 H12",
        ModuleKind.AiEdit => "M12,3 L14,9 20,11 14,13 12,19 10,13 4,11 10,9 Z M18,3 V7 M16,5 H20",
        ModuleKind.DesignEditor => "M5,18 L7,13 16,4 20,8 11,17 Z M7,13 L11,17",
        ModuleKind.Crop => "M6,3 V17 H20 M3,6 H17 V21",
        ModuleKind.Repeat => "M4,9 C4,5 8,4 12,4 H19 M16,1 L19,4 16,7 M20,15 C20,19 16,20 12,20 H5 M8,17 L5,20 8,23",
        ModuleKind.Report => "M6,3 H15 L19,7 V21 H6 Z M14,3 V8 H19 M9,12 H16 M9,16 H16",
        ModuleKind.Resize => "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15 M7,7 L17,17",
        ModuleKind.Colors => "M12,3 C7,3 3,7 3,12 C3,17 7,21 12,21 C15,21 16,19 15,17 C14,15 16,14 18,14 C20,14 21,12 21,10 C20,6 16,3 12,3 Z M7,10 H8 M11,7 H12 M16,8 H17",
        ModuleKind.Weaves => "M4,5 H20 M4,10 H20 M4,15 H20 M4,20 H20 M5,4 V20 M10,4 V20 M15,4 V20 M20,4 V20",
        ModuleKind.CreateWeave => "M4,5 H14 M4,10 H14 M4,15 H14 M5,4 V19 M10,4 V19 M18,13 V21 M14,17 H22",
        ModuleKind.ApplyWeaves => "M4,5 H13 M4,10 H13 M4,15 H13 M5,4 V19 M10,4 V19 M14,15 L17,18 22,11",
        ModuleKind.AutoWeaves => "M4,5 H13 M4,10 H13 M4,15 H13 M5,4 V19 M10,4 V19 M18,3 L19,7 23,8 19,9 18,13 17,9 13,8 17,7 Z",
        ModuleKind.Selvedge => "M4,4 H20 V20 H4 Z M8,4 V20 M16,4 V20",
        ModuleKind.Floats => "M4,6 H20 M4,12 H20 M4,18 H20 M7,4 V20 M17,4 V20 M10,9 L14,15",
        ModuleKind.Tension => "M4,12 H20 M4,12 L7,9 M4,12 L7,15 M20,12 L17,9 M20,12 L17,15 M10,6 V18 M14,6 V18",
        ModuleKind.Weft => "M4,6 H20 M4,10 H20 M4,14 H20 M4,18 H20 M17,3 L20,6 17,9",
        ModuleKind.Simulation => "M4,5 H20 V19 H4 Z M4,10 H20 M4,15 H20 M9,5 V19 M14,5 V19",
        ModuleKind.Machine => "M4,8 H20 V19 H4 Z M7,8 V5 H17 V8 M8,12 H9 M12,12 H13 M16,12 H17 M7,16 H17",
        ModuleKind.PeriodicFloats => "M4,8 H20 M4,16 H20 M7,5 V19 M17,5 V19 M10,11 L14,13 M2,4 L5,1 M19,23 L22,20",
        ModuleKind.MatchPalette => "M4,8 H10 V14 H4 Z M14,8 H20 V14 H14 Z M10,11 H14 M12,9 L14,11 12,13",
        ModuleKind.RectangleMask => "M4,4 H20 V20 H4 Z M8,8 H16 V16 H8 Z",
        ModuleKind.ApplyMask => "M4,4 H20 V20 H4 Z M7,12 L10,15 17,8",
        ModuleKind.BlankDesign => "M4,4 H20 V20 H4 Z M12,8 V16 M8,12 H16",
        _ => "M4,4 H20 V20 H4 Z"
    };
    public static FrameworkElement Create(ModuleKind kind,double size)
    {
        var grid=new Grid {Width=size,Height=size,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
        grid.Children.Add(new Ellipse {Fill=new SolidColorBrush(Color.FromRgb(28,48,70)),Stroke=new SolidColorBrush(Color.FromRgb(214,155,84)),StrokeThickness=1});
        var weave=new Path {Data=Geometry.Parse("M 3,18 C 8,6 22,6 29,18 M 3,14 C 8,26 22,26 29,14"),Stroke=new SolidColorBrush(Color.FromRgb(55,116,170)),StrokeThickness=1.3,Stretch=Stretch.Uniform,Width=size*.85,Height=size*.55,Opacity=.8};grid.Children.Add(weave);
        var symbol=new Path {Data=Geometry.Parse(PathFor(kind)),Stroke=new SolidColorBrush(Color.FromRgb(255,195,125)),StrokeThickness=1.5,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Stretch=Stretch.Uniform,Width=size*.65,Height=size*.65};grid.Children.Add(symbol);return grid;
    }
}
