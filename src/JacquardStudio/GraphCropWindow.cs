using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JacquardStudio;

/// <summary>Non-destructive crop draft. The dialog is visible even while its upstream graph is loading.</summary>
public sealed class GraphCropWindow : Window
{
    private BitmapSource? source;
    private GraphCrop? rect;
    private readonly Image preview=new() { Stretch=Stretch.Fill, HorizontalAlignment=HorizontalAlignment.Left, VerticalAlignment=VerticalAlignment.Top, Cursor=Cursors.Cross };
    private readonly TextBlock status=new() { Margin=new Thickness(8), TextWrapping=TextWrapping.Wrap };
    private readonly ComboBox direction=new() { Width=180, ItemsSource=new[]{"حذف از چپ", "حذف از راست", "حذف از بالا", "حذف از پایین"}, SelectedIndex=-1, Margin=new Thickness(5) };
    private readonly Button apply;
    private double zoom;
    private bool closed;
    public GraphCrop? Result { get; private set; }
    public GraphCropWindow(BitmapSource source, GraphCrop? initial, string hash) : this() => SetImage(source,initial,hash);
    public GraphCropWindow(Func<Task<(BitmapSource Image,GraphCrop? Initial,string Hash)>> load) : this()
    {
        status.Text="در حال آماده‌سازی تصویر ورودی…";
        Loaded+=async (_,_)=>{
            try { var data=await load(); if(!closed) SetImage(data.Image,data.Initial,data.Hash); }
            catch(Exception ex) when(ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or UnauthorizedAccessException) { if(!closed) status.Text="تصویر برش آماده نیست: " + ex.Message; }
        };
    }
    private GraphCropWindow()
    {
        Title="برش طرح"; Width=1000; Height=800; MinWidth=600; MinHeight=480; WindowStartupLocation=WindowStartupLocation.CenterOwner; FlowDirection=FlowDirection.RightToLeft;
        SetResourceReference(BackgroundProperty,"AppBackground");
        var root=new DockPanel { Margin=new Thickness(14) }; var toolbar=new WrapPanel(); DockPanel.SetDock(toolbar,Dock.Top); root.Children.Add(toolbar);
        toolbar.Children.Add(new TextBlock { Text="۱. جهت حذف را انتخاب کنید:",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(5) }); toolbar.Children.Add(direction);
        Button Add(string label, Action action) { var b=new Button { Content=label, Margin=new Thickness(4), Padding=new Thickness(9,5,9,5) }; b.Click+=(_,_)=>action(); toolbar.Children.Add(b); return b; }
        Add("−",()=>{zoom=Math.Max(.05,zoom/1.5); Refresh();}); Add("+",()=>{zoom=Math.Min(16,zoom*1.5); Refresh();});
        Add("بازنشانی برش",()=>{if(source is null || rect is null)return; rect=new GraphCrop(0,0,source.PixelWidth,source.PixelHeight,rect.InputHash); Refresh();});
        apply=Add("اعمال تنظیمات و بستن",()=>{ if(rect is null)return; if ((long)rect.Width*rect.Height>DesignProcessor.MaxPixels) { MessageBox.Show(this,"اندازه خروجی برش باید حداکثر چهار میلیون پیکسل باشد."); return; } Result=rect; DialogResult=true;}); apply.SetResourceReference(Control.BackgroundProperty,"ActionBackground"); apply.SetResourceReference(Control.ForegroundProperty,"AccentForeground"); apply.IsEnabled=false; Add("انصراف",()=>DialogResult=false);
        DockPanel.SetDock(status,Dock.Bottom); root.Children.Add(status); var scroll=new ScrollViewer { HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,FlowDirection=FlowDirection.LeftToRight, Content=preview };
        scroll.SetResourceReference(BackgroundProperty,"CanvasBackground"); root.Children.Add(scroll); Content=root;
        RenderOptions.SetBitmapScalingMode(preview,BitmapScalingMode.NearestNeighbor); preview.MouseLeftButtonDown+=Cut_Click; Closed+=(_,_)=>closed=true;
    }
    private void SetImage(BitmapSource image, GraphCrop? initial, string hash)
    {
        source=image; rect=initial ?? new GraphCrop(0,0,image.PixelWidth,image.PixelHeight,hash); zoom=Math.Min(1,Math.Min(850.0/rect.Width,600.0/rect.Height)); apply.IsEnabled=true; Refresh();
    }
    private void Refresh()
    {
        if(source is null || rect is null)return;
        preview.Source=GraphImageOperations.Crop(source,rect); preview.Width=Math.Max(1,rect.Width*zoom); preview.Height=Math.Max(1,rect.Height*zoom);
        status.Text=$"۲. روی پیکسل مرزی کلیک کنید؛ پیکسل انتخابی باقی می‌ماند. برای دقت از + و پیمایش استفاده کنید. اندازه: {rect.Width} × {rect.Height} · بزرگ‌نمایی {zoom*100:0}%";
    }
    private void Cut_Click(object sender,MouseButtonEventArgs e)
    {
        if(rect is null)return;
        if(direction.SelectedIndex<0) { status.Text="ابتدا جهت حذف را انتخاب کنید."; return; }
        var p=e.GetPosition(preview); var pixel=GraphImageOperations.FindPixel(rect.Width,rect.Height,p.X,p.Y,preview.ActualWidth,preview.ActualHeight); if(pixel is null) return;
        rect=GraphImageOperations.Cut(rect,(CropDirection)direction.SelectedIndex,pixel.Value.X,pixel.Value.Y); Refresh(); e.Handled=true;
    }
}
