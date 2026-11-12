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
        ModuleKind.ExportData => "M4,4 H14 L19,9 V20 H4 Z M14,4 V9 H19 M6,12 H12 M6,15 H12 M6,18 H12 M14,14 H22 M19,11 L22,14 19,17",
        ModuleKind.BinaryLoad => "M4,4 H14 L19,9 V20 H4 Z M14,4 V9 H19 M6,12 H13 M6,15 H13 M6,18 H13 M16,13 L21,16 16,19 M21,16 H13",
        ModuleKind.BinaryConvert => "M4,4 H11 V11 H4 Z M13,13 H20 V20 H13 Z M13,5 L20,5 M17,2 L20,5 17,8",
        ModuleKind.SeamCheck => "M4,5 H20 V19 H4 Z M12,5 V19 M9,9 L11,11 9,13 M15,9 L13,11 15,13",
        ModuleKind.Compare => "M4,6 H10 V18 H4 Z M14,6 H20 V18 H14 Z M12,4 V20",
        ModuleKind.QualityCheck => "M5,12 L10,17 20,6 M5,5 H19 V19 H5 Z",
        ModuleKind.Load => "M4,5 H20 V19 H4 Z M5,17 L10,11 13,14 16,10 20,16",
        ModuleKind.VectorLoad => "M5,19 C5,5 19,5 19,19 M5,19 H19 M5,5 H19 M3,3 H7 V7 H3 Z M17,3 H21 V7 H17 Z M3,17 H7 V21 H3 Z M17,17 H21 V21 H17 Z",
        ModuleKind.MachineDefinition => "M12,3 V6 M12,18 V21 M3,12 H6 M18,12 H21 M7,7 H17 V17 H7 Z",
        ModuleKind.DesignIdentity => "M5,4 H19 V20 H5 Z M8,8 H16 M8,11 H16 M8,14 H14 M8,17 H12",
        ModuleKind.AiEdit => "M12,3 L14,9 20,11 14,13 12,19 10,13 4,11 10,9 Z M18,3 V7 M16,5 H20",
        ModuleKind.DesignEditor => "M3,5 H15 M3,9 H14 M3,13 H12 M5,3 V16 M9,3 V14 M13,3 V10 M10,19 L12,15 19,8 22,11 15,18 Z",
        ModuleKind.Crop => "M6,3 V17 H20 M3,6 H17 V21",
        ModuleKind.Repeat => "M4,9 C4,5 8,4 12,4 H19 M16,1 L19,4 16,7 M20,15 C20,19 16,20 12,20 H5 M8,17 L5,20 8,23",
        ModuleKind.Report => "M6,3 H15 L19,7 V21 H6 Z M14,3 V8 H19 M9,12 H16 M9,16 H16",
        ModuleKind.Resize => "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15 M7,7 L17,17",
        ModuleKind.Colors => "M12,3 C7,3 3,7 3,12 C3,17 7,21 12,21 C15,21 16,19 15,17 C14,15 16,14 18,14 C20,14 21,12 21,10 C20,6 16,3 12,3 Z M7,10 H8 M11,7 H12 M16,8 H17 M8,15 H9",
        ModuleKind.Weaves => "M3,4 H12 V20 H3 Z M12,4 H21 V20 H12 Z M5,7 H10 M5,10 H10 M5,13 H10 M14,7 H19 M14,10 H19 M14,13 H19",
        ModuleKind.CreateWeave => "M4,5 H14 M4,10 H14 M4,15 H14 M5,4 V19 M10,4 V19 M18,13 V21 M14,17 H22",
        ModuleKind.ApplyWeaves => "M4,5 H13 M4,10 H13 M4,15 H13 M5,4 V19 M10,4 V19 M14,15 L17,18 22,11",
        ModuleKind.AutoWeaves => "M4,5 H13 M4,10 H13 M4,15 H13 M5,4 V19 M10,4 V19 M18,3 L19,7 23,8 19,9 18,13 17,9 13,8 17,7 Z",
        ModuleKind.Selvedge => "M4,4 H20 V20 H4 Z M8,4 V20 M16,4 V20",
        ModuleKind.Floats => "M3,5 H21 M3,10 H21 M3,15 H21 M3,20 H21 M5,3 V21 M10,3 V21 M15,3 V21 M20,3 V21 M14,12 L18,16 M14,16 L18,12",
        ModuleKind.Tension => "M4,5 V19 M8,5 V19 M12,5 V19 M16,5 V19 M3,8 H18 M3,12 H18 M4,19 C7,11 13,8 19,8 M17,5 L20,8 17,11",
        ModuleKind.Weft => "M4,3 V21 M8,3 V21 M12,3 V21 M16,3 V21 M20,3 V21 M3,15 C8,10 16,10 21,7 C18,13 12,16 3,15 Z",
        ModuleKind.Simulation => "M4,4 V20 M8,4 V20 M12,4 V20 M16,4 V20 M20,4 V20 M3,6 H21 M3,10 H21 M3,14 H21 M3,18 H21 M12,17 C15,12 20,12 23,17 C20,21 15,21 12,17 Z M17,16 V18",
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
        grid.Children.Add(new Ellipse {Fill=new SolidColorBrush(Color.FromRgb(17,35,54)),Stroke=new SolidColorBrush(Color.FromRgb(211,162,96)),StrokeThickness=1});
        var weave=new Path {Data=Geometry.Parse("M 3,4 L 9,10 3,16 M 3,9 L 9,15 3,21 M 21,3 L 15,9 21,15 M 21,8 L 15,14 21,20 M 4,3 L 4,21 M 20,3 L 20,21"),Stroke=new SolidColorBrush(Color.FromRgb(60,124,176)),StrokeThickness=1.3,Stretch=Stretch.Fill,Width=size*.84,Height=size*.78,Opacity=.68};grid.Children.Add(weave);
        var under=new Path {Data=Geometry.Parse(PathFor(kind)),Stroke=new SolidColorBrush(Color.FromRgb(17,35,54)),StrokeThickness=3.6,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Stretch=Stretch.Uniform,Width=size*.66,Height=size*.66};grid.Children.Add(under);
        var symbol=new Path {Data=Geometry.Parse(PathFor(kind)),Stroke=new SolidColorBrush(Color.FromRgb(245,190,112)),StrokeThickness=1.55,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Stretch=Stretch.Uniform,Width=size*.66,Height=size*.66};grid.Children.Add(symbol);return grid;
    }
}
