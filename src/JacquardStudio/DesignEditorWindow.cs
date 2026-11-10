using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Transactional image editor; the upstream bitmap is never modified.</summary>
public sealed class DesignEditorWindow : Window
{
    private readonly DesignDrawing drawing;
    private readonly Image image=new() { Stretch=Stretch.Fill, HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Cursor=Cursors.Cross };
    private readonly TextBlock status=new() { Margin=new Thickness(6),TextWrapping=TextWrapping.Wrap };
    private readonly ComboBox tool=new() { ItemsSource=new[]{"قلم پیکسلی","خط","مستطیل توخالی","پرکردن ناحیه","قطره‌چکان"},SelectedIndex=0,Width=155,Margin=new Thickness(4) };
    private readonly List<byte[]> undo=new(),redo=new();
    private Color color=Colors.Black; private readonly Button colorButton=new() { Content="انتخاب رنگ",Margin=new Thickness(4) };
    private double zoom; private (int X,int Y)? start,last; private byte[]? before;
    public BitmapSource? Result { get; private set; }
    public DesignEditorWindow(BitmapSource input)
    {
        drawing=new DesignDrawing(input); Title="طراحی و ویرایش طرح"; Width=1050; Height=780; MinWidth=620; MinHeight=480; WindowStartupLocation=WindowStartupLocation.CenterOwner; FlowDirection=FlowDirection.RightToLeft;
        SetResourceReference(BackgroundProperty,"AppBackground"); SetResourceReference(ForegroundProperty,"PrimaryText");
        var root=new DockPanel {Margin=new Thickness(12)}; var bar=new WrapPanel(); DockPanel.SetDock(bar,Dock.Top); root.Children.Add(bar); bar.Children.Add(tool);
        colorButton.Click+=(_,_)=>{var selected=ColorChooser.Pick(this,$"#{color.R:X2}{color.G:X2}{color.B:X2}");if(selected is not null)color=(Color)ColorConverter.ConvertFromString(selected);UpdateColor();}; bar.Children.Add(colorButton); UpdateColor();
        void Add(string text,Action action) {var b=new Button {Content=text,Margin=new Thickness(4),Padding=new Thickness(9,5,9,5)};b.Click+=(_,_)=>action();bar.Children.Add(b);}
        Add("−",()=>{zoom=Math.Max(.05,zoom/1.5);Refresh();}); Add("+",()=>{zoom=Math.Min(24,zoom*1.5);Refresh();});
        Add("بازگشت",()=>Undo(undo,redo)); Add("بازانجام",()=>Undo(redo,undo));
        Add("اعمال و بستن",()=>{Result=drawing.Image();DialogResult=true;}); Add("انصراف",()=>DialogResult=false);
        DockPanel.SetDock(status,Dock.Bottom); root.Children.Add(status); var scroll=new ScrollViewer {FlowDirection=FlowDirection.LeftToRight,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=image}; scroll.SetResourceReference(BackgroundProperty,"CanvasBackground");root.Children.Add(scroll); Content=root;
        RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor); image.MouseLeftButtonDown+=Down; image.MouseMove+=Move; image.MouseLeftButtonUp+=Up; image.LostMouseCapture+=(_,_)=>{if(before is not null){drawing.Restore(before);before=null;start=last=null;Refresh();}};
        zoom=Math.Min(1,Math.Min(850.0/drawing.Width,570.0/drawing.Height)); Refresh();
    }
    private void UpdateColor() { colorButton.Background=new SolidColorBrush(color); colorButton.Foreground=new SolidColorBrush((color.R*299+color.G*587+color.B*114)/1000<128 ? Colors.White : Colors.Black); }
    private void Refresh() { image.Source=drawing.Image();image.Width=Math.Max(1,drawing.Width*zoom);image.Height=Math.Max(1,drawing.Height*zoom);status.Text=$"{drawing.Width} × {drawing.Height} پیکسل | زوم {zoom*100:0}% | ابزار: {tool.Text} | بازگشت: {undo.Count} | نقاشی روی تصویر کپی انجام می‌شود."; }
    private (int X,int Y)? Pixel(MouseEventArgs e) {var p=e.GetPosition(image);return GraphImageOperations.FindPixel(drawing.Width,drawing.Height,p.X,p.Y,image.ActualWidth,image.ActualHeight);}
    private void Push(List<byte[]> list,byte[] snapshot) {list.Add(snapshot); int max=Math.Max(1,Math.Min(20,(64*1024*1024)/snapshot.Length));while(list.Count>max)list.RemoveAt(0);}
    private void Undo(List<byte[]> from,List<byte[]> to) {if(before is not null || from.Count==0)return;Push(to,drawing.Snapshot());var snapshot=from[^1];from.RemoveAt(from.Count-1);drawing.Restore(snapshot);Refresh();}
    private void Down(object sender,MouseButtonEventArgs e)
    {
        var p=Pixel(e); if(p is null)return;
        if(tool.SelectedIndex==4) {color=drawing.At(p.Value.X,p.Value.Y);UpdateColor();Refresh();return;}
        before=drawing.Snapshot(); start=last=p;
        if(tool.SelectedIndex==3) {drawing.Fill(p.Value.X,p.Value.Y,color);Commit();return;}
        if(tool.SelectedIndex is 0 or 1 or 2) drawing.Line(p.Value.X,p.Value.Y,p.Value.X,p.Value.Y,color);
        image.CaptureMouse();Refresh(); e.Handled=true;
    }
    private void Move(object sender,MouseEventArgs e)
    {
        if(before is null || start is null || e.LeftButton!=MouseButtonState.Pressed)return;var p=Pixel(e);if(p is null)return;
        if(tool.SelectedIndex==0 && last is { } prev) drawing.Line(prev.X,prev.Y,p.Value.X,p.Value.Y,color);
        else { drawing.Restore(before); if(tool.SelectedIndex==1)drawing.Line(start.Value.X,start.Value.Y,p.Value.X,p.Value.Y,color);else if(tool.SelectedIndex==2)drawing.Rectangle(start.Value.X,start.Value.Y,p.Value.X,p.Value.Y,color); }
        last=p;Refresh();
    }
    private void Up(object sender,MouseButtonEventArgs e) {if(before is null)return;Commit();image.ReleaseMouseCapture();e.Handled=true;}
    private void Commit() {if(before is null)return;Push(undo,before);redo.Clear();before=null;start=last=null;Refresh();}
}
